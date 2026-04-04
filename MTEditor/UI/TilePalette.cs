using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    private readonly SpriteFont _font;
    private readonly GraphicsDevice _graphics;
    private readonly Texture2D _pixel;

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
    private Rectangle _tileHeaderRect;
    private Rectangle _prototypeHeaderRect;
    private Rectangle _tileFooterRect;
    private Rectangle _tileWindowRect;
    private Rectangle _prototypeWindowRect;
    private Rectangle _hoveredTileRect;
    private Rectangle _hoveredPrototypeRect;

    public string? SelectedTileId { get; private set; }
    public string? SelectedEntityId { get; private set; }
    public PalettePanel HoveredPanel { get; private set; } = PalettePanel.None;
    public PalettePanel SelectedPanel { get; private set; } = PalettePanel.Tiles;
    public EditorGame.Tool? PendingToolSwitch { get; private set; }
    public Rectangle Bounds { get; private set; }

    private const int SidebarWidth = 360;
    private const int WindowGap = 12;
    private const int OuterPadding = 12;
    private const int PanelPadding = 12;
    private const int HeaderHeight = 0;
    private const int FooterHeight = 34;
    private const int TitleBlockHeight = 56;
    private const int TileRowHeight = 54;
    private const int CategoryRowHeight = 34;
    private const int PrototypeRowHeight = 56;
    private const int IconSize = 24;
    private const int ButtonWidth = 28;
    private const int TopChrome = 170;
    private const int BottomChrome = 46;

    public TilePalette(PrototypeManager prototypes, AssetManager assets, SpriteFont font, GraphicsDevice graphics)
    {
        _prototypes = prototypes;
        _assets = assets;
        _font = font;
        _graphics = graphics;

        _pixel = new Texture2D(graphics, 1, 1);
        _pixel.SetData(new[] { Color.White });

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

    public void Draw(SpriteBatch spriteBatch)
    {
        UpdateLayout();

        DrawWindow(spriteBatch, _tileWindowRect, "Tiles", "Terrain and collision tiles");
        DrawWindow(spriteBatch, _prototypeWindowRect, "Prototypes", "Placeable entity prototypes");

        DrawTilePanel(spriteBatch);
        DrawPrototypePanel(spriteBatch);
    }

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
        var width = Math.Min(SidebarWidth, Math.Max(280, viewport.Width / 3));
        var height = viewport.Height - TopChrome - BottomChrome - OuterPadding * 2;
        var top = TopChrome + OuterPadding;

        _tileWindowRect = new Rectangle(
            OuterPadding,
            top,
            width,
            height / 2 - WindowGap / 2);

        _prototypeWindowRect = new Rectangle(
            OuterPadding,
            _tileWindowRect.Bottom + WindowGap,
            width,
            height - _tileWindowRect.Height - WindowGap);

        _tileHeaderRect = new Rectangle(_tileWindowRect.X + PanelPadding, _tileWindowRect.Y + PanelPadding + TitleBlockHeight + 8, _tileWindowRect.Width - PanelPadding * 2, HeaderHeight);
        _prototypeHeaderRect = new Rectangle(_prototypeWindowRect.X + PanelPadding, _prototypeWindowRect.Y + PanelPadding + TitleBlockHeight + 8, _prototypeWindowRect.Width - PanelPadding * 2, HeaderHeight);

        _tileFooterRect = new Rectangle(_tileWindowRect.X + PanelPadding, _tileWindowRect.Bottom - PanelPadding - FooterHeight, _tileWindowRect.Width - PanelPadding * 2, FooterHeight);

        _tileContentRect = new Rectangle(
            _tileWindowRect.X + PanelPadding,
            _tileHeaderRect.Bottom + 8,
            _tileWindowRect.Width - PanelPadding * 2,
            _tileFooterRect.Y - (_tileHeaderRect.Bottom + 16));

        _prototypeContentRect = new Rectangle(
            _prototypeWindowRect.X + PanelPadding,
            _prototypeHeaderRect.Bottom + 8,
            _prototypeWindowRect.Width - PanelPadding * 2,
            _prototypeWindowRect.Bottom - PanelPadding - (_prototypeHeaderRect.Bottom + 16));

        _tileItemsPerPage = Math.Max(1, _tileContentRect.Height / TileRowHeight);

        _tilePrevPageButton = new Rectangle(_tileFooterRect.X, _tileFooterRect.Y + 2, ButtonWidth, FooterHeight - 4);
        _tileNextPageButton = new Rectangle(_tileFooterRect.Right - ButtonWidth, _tileFooterRect.Y + 2, ButtonWidth, FooterHeight - 4);

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
            SelectedPanel = PalettePanel.Tiles;
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
            SelectedPanel = PalettePanel.Prototypes;
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

    private void DrawWindow(SpriteBatch spriteBatch, Rectangle rect, string title, string subtitle)
    {
        var isActive = SelectedPanel == PalettePanel.Tiles && rect == _tileWindowRect
            || SelectedPanel == PalettePanel.Prototypes && rect == _prototypeWindowRect;

        var fill = isActive ? new Color(13, 19, 21, 240) : new Color(10, 14, 16, 228);
        var border = isActive ? new Color(86, 118, 96) : new Color(54, 74, 62);

        DrawPanel(spriteBatch, rect, fill, border, 2);
        DrawText(spriteBatch, title.ToUpperInvariant(), new Vector2(rect.X + PanelPadding, rect.Y + PanelPadding - 2), Color.White);
        DrawText(spriteBatch, subtitle, new Vector2(rect.X + PanelPadding, rect.Y + PanelPadding + 28), new Color(150, 170, 156));
    }

    private void DrawTilePanel(SpriteBatch spriteBatch)
    {
        var pageStart = _tilePage * _tileItemsPerPage;
        var pageItems = _tileEntries.Skip(pageStart).Take(_tileItemsPerPage).ToList();
        var y = _tileContentRect.Y;

        foreach (var entry in _tileEntries)
            entry.Bounds = Rectangle.Empty;

        foreach (var entry in pageItems)
        {
            entry.Bounds = new Rectangle(_tileContentRect.X, y, _tileContentRect.Width, TileRowHeight - 4);
            DrawPaletteRow(
                spriteBatch,
                entry.Bounds,
                entry.Tile.Id == SelectedTileId,
                entry.Bounds == _hoveredTileRect,
                GetTileTexture(entry.Tile),
                GetTileSourceRect(entry.Tile),
                entry.Tile.Name,
                GetTileCategory(entry.Tile));
            y += TileRowHeight;
        }

        DrawPager(spriteBatch, _tileFooterRect, _tilePrevPageButton, _tileNextPageButton, _tilePage, GetTilePageCount());
    }

    private void DrawPrototypePanel(SpriteBatch spriteBatch)
    {
        var pageRows = _prototypeRows.Skip(_prototypeScrollIndex).ToList();
        var y = _prototypeContentRect.Y;

        foreach (var row in _prototypeRows)
            row.Bounds = Rectangle.Empty;

        foreach (var row in pageRows)
        {
            var height = row.IsCategory ? CategoryRowHeight : PrototypeRowHeight;
            if (y + height - 4 > _prototypeContentRect.Bottom)
                break;

            row.Bounds = new Rectangle(_prototypeContentRect.X, y, _prototypeContentRect.Width, height - 4);

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
                    row.Prototype!.Id == SelectedEntityId,
                    row.Bounds == _hoveredPrototypeRect,
                    GetEntityTexture(row.Prototype!),
                    GetEntitySourceRect(row.Prototype!),
                    row.Label,
                    row.Prototype!.Id);
            }

            y += height;
        }
    }

    private void DrawPaletteRow(SpriteBatch spriteBatch, Rectangle rect, bool selected, bool hovered, Texture2D texture, Rectangle? src, string title, string subtitle)
    {
        var bg = selected
            ? new Color(56, 88, 67, 230)
            : hovered ? new Color(30, 43, 37, 226)
            : new Color(18, 25, 24, 220);
        var border = selected ? new Color(154, 198, 128) : hovered ? new Color(86, 118, 96) : new Color(56, 78, 64);

        DrawPanel(spriteBatch, rect, bg, border, 1);

        var iconRect = new Rectangle(rect.X + 8, rect.Y + (rect.Height - IconSize) / 2, IconSize, IconSize);
        spriteBatch.Draw(texture, iconRect, src, Color.White);
        var textX = iconRect.Right + 10;
        var titleY = rect.Y + 7;
        DrawText(spriteBatch, title, new Vector2(textX, titleY), Color.White);

        if (!string.IsNullOrWhiteSpace(subtitle))
            DrawText(spriteBatch, subtitle, new Vector2(textX, titleY + 20), new Color(132, 152, 140));
    }

    private void DrawCategoryRow(SpriteBatch spriteBatch, Rectangle rect, bool hovered, bool expanded, string label)
    {
        var bg = hovered ? new Color(31, 43, 37, 224) : new Color(20, 29, 26, 220);
        DrawPanel(spriteBatch, rect, bg, new Color(56, 78, 64), 1);
        DrawText(spriteBatch, expanded ? $"v {label}" : $"> {label}", new Vector2(rect.X + 10, rect.Y + 7), new Color(210, 220, 170));
    }

    private void DrawPager(SpriteBatch spriteBatch, Rectangle footerRect, Rectangle prevButton, Rectangle nextButton, int currentPage, int totalPages)
    {
        DrawPanel(spriteBatch, footerRect, new Color(16, 22, 20, 220), new Color(62, 92, 70), 1);
        DrawPagerButton(spriteBatch, prevButton, "<", currentPage > 0);
        DrawPagerButton(spriteBatch, nextButton, ">", currentPage < totalPages - 1);

        var label = $"Page {currentPage + 1}/{Math.Max(1, totalPages)}";
        var size = _font.MeasureString(label);
        DrawText(spriteBatch, label, new Vector2(footerRect.Center.X - size.X / 2f, footerRect.Y + 7), new Color(190, 208, 192));
    }

    private void DrawPagerButton(SpriteBatch spriteBatch, Rectangle rect, string label, bool enabled)
    {
        DrawPanel(spriteBatch, rect, enabled ? new Color(34, 48, 40, 220) : new Color(20, 24, 22, 180), new Color(74, 102, 84), 1);
        var size = _font.MeasureString(label);
        DrawText(spriteBatch, label, new Vector2(rect.Center.X - size.X / 2f, rect.Y + 6), enabled ? Color.White : Color.Gray);
    }

    private void DrawPanel(SpriteBatch spriteBatch, Rectangle rect, Color fill, Color border, int borderThickness)
    {
        spriteBatch.Draw(_pixel, rect, fill);
        spriteBatch.Draw(_pixel, new Rectangle(rect.X, rect.Y, rect.Width, borderThickness), border);
        spriteBatch.Draw(_pixel, new Rectangle(rect.X, rect.Bottom - borderThickness, rect.Width, borderThickness), border);
        spriteBatch.Draw(_pixel, new Rectangle(rect.X, rect.Y, borderThickness, rect.Height), border);
        spriteBatch.Draw(_pixel, new Rectangle(rect.Right - borderThickness, rect.Y, borderThickness, rect.Height), border);
    }

    private void DrawText(SpriteBatch spriteBatch, string text, Vector2 position, Color color)
    {
        spriteBatch.DrawString(_font, text, position, color);
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
