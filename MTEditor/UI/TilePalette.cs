using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MTEngine.Core;
using MTEngine.World;

namespace MTEditor.UI;

public enum PalettePanel
{
    None,
    Tiles,
    Prototypes
}

public class TilePalette
{
    private sealed class TileEntry
    {
        public required TilePrototype Tile { get; init; }
        public Rectangle Bounds { get; set; }
    }

    private sealed class PrototypeRow
    {
        public required bool IsCategory { get; init; }
        public required string Label { get; init; }
        public required string Category { get; init; }
        public EntityPrototype? Prototype { get; init; }
        public Rectangle Bounds { get; set; }
    }

    private readonly PrototypeManager _prototypes;
    private readonly AssetManager _assets;
    private readonly GraphicsDevice _graphics;

    private readonly List<TileEntry> _tileEntries = new();
    private readonly List<PrototypeRow> _prototypeRows = new();
    private readonly Dictionary<string, bool> _expandedPrototypeCategories = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _prototypeCategories = new();

    private int _tilePage;
    private int _prototypeScrollIndex;
    private int _tileItemsPerPage = 1;
    private Rectangle _tilePrevPageButton;
    private Rectangle _tileNextPageButton;
    private Rectangle _tileContentRect;
    private Rectangle _prototypeContentRect;
    private Rectangle _tileFooterRect;
    private Rectangle _tileWindowRect;
    private Rectangle _prototypeWindowRect;
    private Rectangle _tileHeaderRect;
    private Rectangle _prototypeHeaderRect;
    private Rectangle _hoveredTileRect;
    private Rectangle _hoveredPrototypeRect;

    public string? SelectedTileId { get; private set; }
    public string? SelectedEntityId { get; private set; }
    public PalettePanel HoveredPanel { get; private set; } = PalettePanel.None;
    public EditorGame.Tool? PendingToolSwitch { get; private set; }
    public Rectangle Bounds { get; private set; }

    public void ClearSelectedEntity() => SelectedEntityId = null;
    public void ClearSelectedTile() => SelectedTileId = null;

    public void Refresh()
    {
        var selectedTileId = SelectedTileId;
        var selectedEntityId = SelectedEntityId;

        BuildTileEntries();
        BuildPrototypeRows();

        SelectedTileId = selectedTileId != null && _tileEntries.Any(entry => entry.Tile.Id == selectedTileId)
            ? selectedTileId
            : _tileEntries.FirstOrDefault()?.Tile.Id;
        SelectedEntityId = selectedEntityId != null && _prototypeRows.Any(row => !row.IsCategory && row.Prototype?.Id == selectedEntityId)
            ? selectedEntityId
            : _prototypeRows.FirstOrDefault(row => !row.IsCategory)?.Prototype?.Id;

        ClampPages();
    }

    // UE-style compact sidebar
    private const int SidebarWidth = 300;
    private const int WindowGap = 6;
    private const int OuterPadding = 8;
    private const int PanelPadding = 6;
    private const int HeaderHeight = 22;
    private const int FooterHeight = 22;
    private const int TileRowHeight = 36;
    private const int CategoryRowHeight = 22;
    private const int PrototypeRowHeight = 36;
    private const int IconSize = 24;
    private const int ButtonWidth = 22;
    // top chrome = global menubar + tabs + local menubar
    private const int TopChrome = 72;
    // bottom chrome = StatusBar (22)
    private const int BottomChrome = 22;

    public TilePalette(PrototypeManager prototypes, AssetManager assets, GraphicsDevice graphics)
    {
        _prototypes = prototypes;
        _assets = assets;
        _graphics = graphics;

        BuildTileEntries();
        BuildPrototypeRows();

        SelectedTileId = _tileEntries.FirstOrDefault()?.Tile.Id;
        SelectedEntityId = _prototypeRows.FirstOrDefault(r => !r.IsCategory)?.Prototype?.Id;

        UpdateLayout();
    }

    public void Update(MouseState mouse, MouseState prev, int scrollDelta)
    {
        PendingToolSwitch = null;
        UpdateLayout();

        var point = new Point(mouse.X, mouse.Y);
        HoveredPanel = PalettePanel.None;
        _hoveredTileRect = Rectangle.Empty;
        _hoveredPrototypeRect = Rectangle.Empty;

        if (_tileContentRect.Contains(point) || _tileWindowRect.Contains(point))
            HoveredPanel = PalettePanel.Tiles;
        else if (_prototypeContentRect.Contains(point) || _prototypeWindowRect.Contains(point))
            HoveredPanel = PalettePanel.Prototypes;

        CaptureHoveredRows(point);

        if (scrollDelta != 0)
            HandleScroll(point, scrollDelta);

        if (mouse.LeftButton == ButtonState.Pressed && prev.LeftButton == ButtonState.Released)
            HandleClick(point);
    }

    public void Draw(SpriteBatch spriteBatch, EditorGame.Tool activeTool)
    {
        UpdateLayout();

        var activePanel = GetActivePanel(activeTool);

        DrawWindow(spriteBatch, _tileWindowRect, _tileHeaderRect, "TILES", activePanel == PalettePanel.Tiles);
        DrawWindow(spriteBatch, _prototypeWindowRect, _prototypeHeaderRect, "PROTOTYPES", activePanel == PalettePanel.Prototypes);

        DrawTilePanel(spriteBatch, activePanel == PalettePanel.Tiles);
        DrawPrototypePanel(spriteBatch, activePanel == PalettePanel.Prototypes);
    }

    private static PalettePanel GetActivePanel(EditorGame.Tool activeTool) => activeTool switch
    {
        EditorGame.Tool.TilePainter => PalettePanel.Tiles,
        EditorGame.Tool.EntityPainter => PalettePanel.Prototypes,
        _ => PalettePanel.None
    };

    private void BuildTileEntries()
    {
        _tileEntries.Clear();
        foreach (var tile in _prototypes.GetAllTiles().OrderBy(GetTileCategory).ThenBy(t => t.Name))
            _tileEntries.Add(new TileEntry { Tile = tile });
    }

    private void BuildPrototypeRows()
    {
        _prototypeCategories.Clear();
        _prototypeRows.Clear();

        var grouped = _prototypes.GetAllEntities()
            .OrderBy(GetEntityCategory)
            .ThenBy(e => e.Name)
            .GroupBy(GetEntityCategory)
            .ToList();

        foreach (var group in grouped)
        {
            _prototypeCategories.Add(group.Key);
            if (!_expandedPrototypeCategories.ContainsKey(group.Key))
                _expandedPrototypeCategories[group.Key] = true;

            _prototypeRows.Add(new PrototypeRow
            {
                IsCategory = true,
                Label = group.Key,
                Category = group.Key
            });

            if (_expandedPrototypeCategories[group.Key])
            {
                foreach (var entity in group)
                {
                    _prototypeRows.Add(new PrototypeRow
                    {
                        IsCategory = false,
                        Label = entity.Name,
                        Category = group.Key,
                        Prototype = entity
                    });
                }
            }
        }
    }

    private void UpdateLayout()
    {
        var viewport = _graphics.Viewport;
        var width = Math.Min(SidebarWidth, Math.Max(260, viewport.Width / 3));
        var availableHeight = viewport.Height - TopChrome - BottomChrome - OuterPadding * 2;
        var top = TopChrome + OuterPadding;

        _tileWindowRect = new Rectangle(
            OuterPadding,
            top,
            width,
            availableHeight / 2 - WindowGap / 2);

        _prototypeWindowRect = new Rectangle(
            OuterPadding,
            _tileWindowRect.Bottom + WindowGap,
            width,
            availableHeight - _tileWindowRect.Height - WindowGap);

        _tileHeaderRect = new Rectangle(
            _tileWindowRect.X, _tileWindowRect.Y, _tileWindowRect.Width, HeaderHeight);
        _prototypeHeaderRect = new Rectangle(
            _prototypeWindowRect.X, _prototypeWindowRect.Y, _prototypeWindowRect.Width, HeaderHeight);

        _tileFooterRect = new Rectangle(
            _tileWindowRect.X + PanelPadding,
            _tileWindowRect.Bottom - PanelPadding - FooterHeight,
            _tileWindowRect.Width - PanelPadding * 2,
            FooterHeight);

        _tileContentRect = new Rectangle(
            _tileWindowRect.X + PanelPadding,
            _tileHeaderRect.Bottom + 4,
            _tileWindowRect.Width - PanelPadding * 2,
            _tileFooterRect.Y - _tileHeaderRect.Bottom - 8);

        _prototypeContentRect = new Rectangle(
            _prototypeWindowRect.X + PanelPadding,
            _prototypeHeaderRect.Bottom + 4,
            _prototypeWindowRect.Width - PanelPadding * 2,
            _prototypeWindowRect.Bottom - PanelPadding - _prototypeHeaderRect.Bottom - 4);

        _tileItemsPerPage = Math.Max(1, _tileContentRect.Height / TileRowHeight);

        _tilePrevPageButton = new Rectangle(_tileFooterRect.X, _tileFooterRect.Y, ButtonWidth, _tileFooterRect.Height);
        _tileNextPageButton = new Rectangle(_tileFooterRect.Right - ButtonWidth, _tileFooterRect.Y, ButtonWidth, _tileFooterRect.Height);

        Bounds = Rectangle.Union(_tileWindowRect, _prototypeWindowRect);
        ClampPages();
    }

    private void CaptureHoveredRows(Point point)
    {
        foreach (var entry in _tileEntries)
        {
            if (!entry.Bounds.IsEmpty && entry.Bounds.Contains(point))
            {
                _hoveredTileRect = entry.Bounds;
                break;
            }
        }

        foreach (var row in _prototypeRows)
        {
            if (!row.Bounds.IsEmpty && row.Bounds.Contains(point))
            {
                _hoveredPrototypeRect = row.Bounds;
                break;
            }
        }
    }

    private void HandleScroll(Point point, int scrollDelta)
    {
        if (_tileWindowRect.Contains(point))
        {
            _tilePage = Math.Clamp(_tilePage - Math.Sign(scrollDelta), 0, GetTilePageCount() - 1);
            return;
        }

        if (_prototypeWindowRect.Contains(point))
            _prototypeScrollIndex = Math.Clamp(_prototypeScrollIndex - Math.Sign(scrollDelta), 0, GetPrototypeScrollMax());
    }

    private void HandleClick(Point point)
    {
        if (_tilePrevPageButton.Contains(point))
        {
            _tilePage = Math.Max(0, _tilePage - 1);
            return;
        }

        if (_tileNextPageButton.Contains(point))
        {
            _tilePage = Math.Min(GetTilePageCount() - 1, _tilePage + 1);
            return;
        }

        if (_tileWindowRect.Contains(point))
        {
            foreach (var entry in _tileEntries)
            {
                if (entry.Bounds.IsEmpty || !entry.Bounds.Contains(point))
                    continue;

                SelectedTileId = entry.Tile.Id;
                PendingToolSwitch = EditorGame.Tool.TilePainter;
                return;
            }
        }

        if (_prototypeWindowRect.Contains(point))
        {
            foreach (var row in _prototypeRows)
            {
                if (row.Bounds.IsEmpty || !row.Bounds.Contains(point))
                    continue;

                if (row.IsCategory)
                {
                    _expandedPrototypeCategories[row.Category] = !_expandedPrototypeCategories.GetValueOrDefault(row.Category, true);
                    BuildPrototypeRows();
                    ClampPages();
                    return;
                }

                SelectedEntityId = row.Prototype!.Id;
                PendingToolSwitch = EditorGame.Tool.EntityPainter;
                return;
            }
        }
    }

    private void DrawWindow(SpriteBatch sb, Rectangle rect, Rectangle headerRect, string title, bool isActive)
    {
        // Panel body
        EditorTheme.FillRect(sb, rect, EditorTheme.Bg);
        EditorTheme.DrawBorder(sb, rect, EditorTheme.Border);

        // Header
        EditorTheme.FillRect(sb, headerRect, isActive ? EditorTheme.PanelActive : EditorTheme.Panel);
        sb.Draw(EditorTheme.Pixel,
            new Rectangle(headerRect.X, headerRect.Bottom - 1, headerRect.Width, 1),
            EditorTheme.Border);

        if (isActive)
            sb.Draw(EditorTheme.Pixel,
                new Rectangle(headerRect.X, headerRect.Y, 3, headerRect.Height),
                EditorTheme.Accent);

        var titleSize = EditorTheme.Small.MeasureString(title);
        EditorTheme.DrawText(sb, EditorTheme.Small, title,
            new Vector2(headerRect.X + 10, headerRect.Y + (headerRect.Height - titleSize.Y) / 2f - 1),
            isActive ? Color.White : EditorTheme.TextDim);
    }

    private void DrawTilePanel(SpriteBatch spriteBatch, bool panelActive)
    {
        var pageStart = _tilePage * _tileItemsPerPage;
        var pageItems = _tileEntries.Skip(pageStart).Take(_tileItemsPerPage).ToList();
        var y = _tileContentRect.Y;

        foreach (var entry in _tileEntries)
            entry.Bounds = Rectangle.Empty;

        foreach (var entry in pageItems)
        {
            entry.Bounds = new Rectangle(_tileContentRect.X, y, _tileContentRect.Width, TileRowHeight - 2);
            DrawPaletteRow(
                spriteBatch,
                entry.Bounds,
                panelActive && entry.Tile.Id == SelectedTileId,
                entry.Bounds == _hoveredTileRect,
                GetTileTexture(entry.Tile),
                GetTileSourceRect(entry.Tile),
                entry.Tile.Name,
                GetTileCategory(entry.Tile));
            y += TileRowHeight;
        }

        DrawPager(spriteBatch, _tileFooterRect, _tilePrevPageButton, _tileNextPageButton, _tilePage, GetTilePageCount());
    }

    private void DrawPrototypePanel(SpriteBatch spriteBatch, bool panelActive)
    {
        var pageRows = _prototypeRows.Skip(_prototypeScrollIndex).ToList();
        var y = _prototypeContentRect.Y;

        foreach (var row in _prototypeRows)
            row.Bounds = Rectangle.Empty;

        foreach (var row in pageRows)
        {
            var height = row.IsCategory ? CategoryRowHeight : PrototypeRowHeight;
            if (y + height - 2 > _prototypeContentRect.Bottom)
                break;

            row.Bounds = new Rectangle(_prototypeContentRect.X, y, _prototypeContentRect.Width, height - 2);

            if (row.IsCategory)
            {
                var expanded = _expandedPrototypeCategories.GetValueOrDefault(row.Category, true);
                DrawCategoryRow(spriteBatch, row.Bounds, row.Bounds == _hoveredPrototypeRect, expanded, row.Label);
            }
            else
            {
                DrawPaletteRow(
                    spriteBatch,
                    row.Bounds,
                    panelActive && row.Prototype!.Id == SelectedEntityId,
                    row.Bounds == _hoveredPrototypeRect,
                    GetEntityTexture(row.Prototype!),
                    GetEntitySourceRect(row.Prototype!),
                    row.Label,
                    row.Prototype!.Id);
            }

            y += height;
        }
    }

    private void DrawPaletteRow(SpriteBatch sb, Rectangle rect, bool selected, bool hovered,
                                Texture2D texture, Rectangle? src, string title, string subtitle)
    {
        Color bg;
        if (selected) bg = EditorTheme.Accent;
        else if (hovered) bg = EditorTheme.PanelHover;
        else bg = EditorTheme.PanelAlt;

        EditorTheme.FillRect(sb, rect, bg);
        if (selected)
            sb.Draw(EditorTheme.Pixel, new Rectangle(rect.X, rect.Y, 2, rect.Height), Color.White);

        var iconRect = new Rectangle(rect.X + 6, rect.Y + (rect.Height - IconSize) / 2, IconSize, IconSize);
        sb.Draw(texture, iconRect, src, Color.White);

        var textX = iconRect.Right + 8;
        var textColor = selected ? Color.White : EditorTheme.Text;
        var subColor  = selected ? new Color(220, 230, 255) : EditorTheme.TextMuted;

        var titleSize = EditorTheme.Small.MeasureString(title);
        var subSize = EditorTheme.Tiny.MeasureString(subtitle);
        var textBlockH = titleSize.Y + subSize.Y + 1;
        var ty = rect.Y + (rect.Height - textBlockH) / 2f - 1;

        EditorTheme.DrawText(sb, EditorTheme.Small, title, new Vector2(textX, ty), textColor);
        if (!string.IsNullOrWhiteSpace(subtitle))
            EditorTheme.DrawText(sb, EditorTheme.Tiny, subtitle, new Vector2(textX, ty + titleSize.Y), subColor);
    }

    private void DrawCategoryRow(SpriteBatch sb, Rectangle rect, bool hovered, bool expanded, string label)
    {
        EditorTheme.FillRect(sb, rect, hovered ? EditorTheme.PanelHover : EditorTheme.Panel);
        sb.Draw(EditorTheme.Pixel,
            new Rectangle(rect.X, rect.Bottom - 1, rect.Width, 1),
            EditorTheme.Divider);
        var arrow = expanded ? "▾" : "▸";
        var labelText = $"{arrow} {label.ToUpperInvariant()}";
        var size = EditorTheme.Small.MeasureString(labelText);
        EditorTheme.DrawText(sb, EditorTheme.Small, labelText,
            new Vector2(rect.X + 8, rect.Y + (rect.Height - size.Y) / 2f - 1),
            EditorTheme.TextDim);
    }

    private void DrawPager(SpriteBatch sb, Rectangle footerRect, Rectangle prevButton, Rectangle nextButton, int currentPage, int totalPages)
    {
        EditorTheme.FillRect(sb, footerRect, EditorTheme.Panel);
        sb.Draw(EditorTheme.Pixel,
            new Rectangle(footerRect.X, footerRect.Y, footerRect.Width, 1),
            EditorTheme.Border);

        DrawPagerButton(sb, prevButton, "◀", currentPage > 0);
        DrawPagerButton(sb, nextButton, "▶", currentPage < totalPages - 1);

        var label = $"{currentPage + 1} / {Math.Max(1, totalPages)}";
        var size = EditorTheme.Small.MeasureString(label);
        EditorTheme.DrawText(sb, EditorTheme.Small, label,
            new Vector2(footerRect.Center.X - size.X / 2f, footerRect.Y + (footerRect.Height - size.Y) / 2f - 1),
            EditorTheme.TextDim);
    }

    private void DrawPagerButton(SpriteBatch sb, Rectangle rect, string label, bool enabled)
    {
        EditorTheme.FillRect(sb, rect, enabled ? EditorTheme.PanelAlt : EditorTheme.Panel);
        EditorTheme.DrawBorder(sb, rect, EditorTheme.Border);
        var size = EditorTheme.Small.MeasureString(label);
        EditorTheme.DrawText(sb, EditorTheme.Small, label,
            new Vector2(rect.Center.X - size.X / 2f, rect.Y + (rect.Height - size.Y) / 2f - 1),
            enabled ? EditorTheme.Text : EditorTheme.TextDisabled);
    }

    private int GetTilePageCount()
        => Math.Max(1, (int)Math.Ceiling(_tileEntries.Count / (float)Math.Max(1, _tileItemsPerPage)));

    private void ClampPages()
    {
        _tilePage = Math.Clamp(_tilePage, 0, GetTilePageCount() - 1);
        _prototypeScrollIndex = Math.Clamp(_prototypeScrollIndex, 0, GetPrototypeScrollMax());
    }

    private int GetPrototypeScrollMax()
    {
        var usedHeight = 0;
        var visibleRows = 0;
        foreach (var row in _prototypeRows)
        {
            var rowHeight = row.IsCategory ? CategoryRowHeight : PrototypeRowHeight;
            if (usedHeight + rowHeight > _prototypeContentRect.Height)
                break;

            usedHeight += rowHeight;
            visibleRows++;
        }

        return Math.Max(0, _prototypeRows.Count - Math.Max(1, visibleRows));
    }

    private Texture2D GetTileTexture(TilePrototype tile)
    {
        if (tile.Sprite?.FullPath != null)
        {
            var tex = _assets.LoadFromFile(tile.Sprite.FullPath);
            if (tex != null)
                return tex;
        }

        if (tile.Animations != null && !string.IsNullOrEmpty(tile.Animations.TexturePath))
        {
            var tex = _assets.LoadFromFile(tile.Animations.TexturePath);
            if (tex != null)
                return tex;
        }

        return _assets.GetColorTexture(tile.Color);
    }

    private Rectangle? GetTileSourceRect(TilePrototype tile)
    {
        if (tile.Animations != null)
        {
            var clip = tile.Animations.GetClip("idle") ?? tile.Animations.GetAllClips().FirstOrDefault();
            if (clip?.Frames.Count > 0)
                return clip.Frames[0].SourceRect;
        }

        if (tile.Sprite != null)
            return new Rectangle(tile.Sprite.SrcX, tile.Sprite.SrcY, tile.Sprite.Width, tile.Sprite.Height);

        return null;
    }

    private Texture2D GetEntityTexture(EntityPrototype entity)
    {
        if (entity.SpritePath != null)
        {
            var tex = _assets.LoadFromFile(entity.SpritePath);
            if (tex != null)
                return tex;
        }

        return _assets.GetColorTexture(entity.PreviewColor);
    }

    private Rectangle? GetEntitySourceRect(EntityPrototype entity) => entity.PreviewSourceRect;

    private static string GetTileCategory(TilePrototype tile)
    {
        if (!string.IsNullOrWhiteSpace(tile.Tileset))
            return tile.Tileset!;

        if (string.IsNullOrWhiteSpace(tile.DirectoryPath))
            return "General";

        var relative = Path.GetRelativePath(ContentPaths.TilesRoot, tile.DirectoryPath);
        var parts = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return parts.Length > 1 ? ToTitle(parts[0]) : "General";
    }

    private static string GetEntityCategory(EntityPrototype entity)
    {
        if (string.IsNullOrWhiteSpace(entity.DirectoryPath))
            return "Misc";

        var relative = Path.GetRelativePath(ContentPaths.PrototypesRoot, entity.DirectoryPath);
        var parts = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return parts.Length > 0 ? ToTitle(parts[0]) : "Misc";
    }

    private static string ToTitle(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "Misc";

        return raw.Replace('_', ' ').Replace('-', ' ');
    }
}
