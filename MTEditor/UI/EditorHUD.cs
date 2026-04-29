#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MTEngine.World;

namespace MTEditor.UI;

public enum EditorCommand
{
    None,
    NewMap,
    LoadMap,
    SaveMap,
    ResizeMap,
    InGameMaps,
    Undo,
    Redo,
    ToolTiles,
    ToolPrototypes,
    ToolSpawns,
    ToolTriggers,
    ToolAreas,
    PointerBrush,
    PointerMouse,
    ShapePoint,
    ShapeLine,
    ShapeFilledRectangle,
    ShapeHollowRectangle,
    OpenMapTab,
    OpenFactionsTab,
    OpenCitiesTab,
    OpenProfessionsTab,
    OpenNpcsTab,
    OpenPrototypesTab,
    OpenGlobalSettingsTab,
    CloseCurrentTab,
    NewFaction,
    SaveFaction,
    DeleteFaction,
    NewCity,
    SaveCity,
    DeleteCity,
    NewProfession,
    SaveProfession,
    DeleteProfession,
    ReloadProfessions,
    NewNpc,
    SaveNpcRoster,
    DeleteNpc,
    ReloadNpcs,
    SaveNpcTemplate,
    NewPrototype,
    SavePrototype,
    DeletePrototype,
    ReloadPrototypes,
    SaveGlobalSettings
}

public class EditorHUD
{
    private const int GlobalMenuHeight = 22;
    private const int TabStripHeight = 28;
    private const int LocalMenuHeight = 22;
    private const int StatusBarHeight = 22;
    private const int MenuItemPadX = 10;
    private const int DropdownPadV = 4;
    private const int DropdownPadX = 16;
    private const int DropdownRowH = 22;
    private const int DropdownMinW = 180;

    private enum MenuScope
    {
        None,
        Global,
        Local
    }

    private sealed record MenuItem(string Label, EditorCommand Command, string? Shortcut = null, bool Separator = false)
    {
        public static MenuItem Sep() => new("", EditorCommand.None, null, true);
    }

    private sealed class MenuHeader
    {
        public required string Label { get; init; }
        public required List<MenuItem> Items { get; init; }
        public Rectangle HeaderBounds;
        public Rectangle DropdownBounds;
        public readonly List<Rectangle> ItemBounds = new();
    }

    private sealed class WorkspaceTab
    {
        public required EditorWorkspaceTabKind Kind { get; init; }
        public required string Title { get; init; }
        public required bool Closable { get; init; }
        public Rectangle Bounds;
        public Rectangle CloseBounds;
    }

    private readonly GraphicsDevice _graphics;
    private readonly List<MenuHeader> _globalMenus;
    private readonly List<WorkspaceTab> _workspaceTabs = new();

    private readonly List<MenuHeader> _localMenus = new();

    private Rectangle _globalMenuRect;
    private Rectangle _tabStripRect;
    private Rectangle _localMenuRect;
    private Rectangle _statusBarRect;

    private string _message = "";
    private float _messageTimer;

    private MenuScope _openMenuScope = MenuScope.None;
    private int _openMenuIndex = -1;
    private int _hoveredGlobalMenu = -1;
    private int _hoveredLocalMenu = -1;
    private int _hoveredDropdownItem = -1;
    private int _hoveredTabIndex = -1;
    private bool _hoveredTabClose;

    private int _activeTabIndex;

    public EditorHUD(GraphicsDevice graphics)
    {
        _graphics = graphics;
        _globalMenus = new List<MenuHeader>
        {
            new()
            {
                Label = "Tabs",
                Items = new()
                {
                    new("Open Map Editor", EditorCommand.OpenMapTab),
                    new("Open Factions", EditorCommand.OpenFactionsTab),
                    new("Open Cities", EditorCommand.OpenCitiesTab),
                    new("Open Professions", EditorCommand.OpenProfessionsTab),
                    new("Open NPCs", EditorCommand.OpenNpcsTab),
                    new("Open Prototypes", EditorCommand.OpenPrototypesTab),
                    new("Open Global Settings", EditorCommand.OpenGlobalSettingsTab),
                    MenuItem.Sep(),
                    new("Close Current Tab", EditorCommand.CloseCurrentTab, "Ctrl+W"),
                }
            },
            new()
            {
                Label = "View",
                Items = new()
            }
        };

        _workspaceTabs.Add(new WorkspaceTab
        {
            Kind = EditorWorkspaceTabKind.Map,
            Title = "Map Editor",
            Closable = true
        });
    }

    public int TopChromeHeight => GlobalMenuHeight + TabStripHeight + LocalMenuHeight;
    public Rectangle TopBarBounds => Rectangle.Union(_globalMenuRect, Rectangle.Union(_tabStripRect, _localMenuRect));
    public Rectangle BottomBarBounds => _statusBarRect;
    public Rectangle MapToolbarBounds => ActiveTabKind == EditorWorkspaceTabKind.Map ? _localMenuRect : Rectangle.Empty;
    public EditorWorkspaceTabKind ActiveTabKind => _workspaceTabs[_activeTabIndex].Kind;
    public bool IsAnyMenuOpen => _openMenuScope != MenuScope.None;


    public void ShowMessage(string message)
    {
        _message = message;
        _messageTimer = 3f;
        Console.WriteLine($"[Editor] {message}");
    }

    public bool ContainsInteractive(Point point)
    {
        if (TopBarBounds.Contains(point) || BottomBarBounds.Contains(point))
            return true;

        var openMenus = GetOpenMenuCollection();
        if (openMenus != null && _openMenuIndex >= 0 && _openMenuIndex < openMenus.Count)
            return openMenus[_openMenuIndex].DropdownBounds.Contains(point);

        return false;
    }

    public EditorCommand Update(MouseState mouse, MouseState prev, Rectangle paletteBounds)
    {
        RebuildLayout(paletteBounds);

        if (_messageTimer > 0f)
            _messageTimer -= 0.016f;

        var point = mouse.Position;
        _hoveredGlobalMenu = FindHoveredMenu(_globalMenus, point);
        _hoveredLocalMenu = FindHoveredMenu(_localMenus, point);
        _hoveredDropdownItem = -1;
        _hoveredTabIndex = -1;
        _hoveredTabClose = false;

        var openMenus = GetOpenMenuCollection();
        if (openMenus != null && _openMenuIndex >= 0 && _openMenuIndex < openMenus.Count)
        {
            var menu = openMenus[_openMenuIndex];
            for (var i = 0; i < menu.ItemBounds.Count; i++)
            {
                if (menu.Items[i].Separator)
                    continue;
                if (!menu.ItemBounds[i].Contains(point))
                    continue;
                _hoveredDropdownItem = i;
                break;
            }
        }

        for (var i = 0; i < _workspaceTabs.Count; i++)
        {
            var tab = _workspaceTabs[i];
            if (!tab.Bounds.Contains(point))
                continue;

            _hoveredTabIndex = i;
            _hoveredTabClose = tab.Closable && tab.CloseBounds.Contains(point);
            break;
        }

        var clicked = mouse.LeftButton == ButtonState.Pressed && prev.LeftButton == ButtonState.Released;
        if (!clicked)
            return EditorCommand.None;

        // Highest z-order: an open dropdown must consume the click before
        // lower bars/tabs below it see anything.
        if (TryHandleOpenDropdownClick(point, out var dropdownCommand))
            return dropdownCommand;

        if (HandleMenuHeaderClick(_globalMenus, point, MenuScope.Global))
            return EditorCommand.None;

        if (HandleMenuHeaderClick(_localMenus, point, MenuScope.Local))
            return EditorCommand.None;

        for (var i = 0; i < _workspaceTabs.Count; i++)
        {
            var tab = _workspaceTabs[i];
            if (!tab.Bounds.Contains(point))
                continue;

            if (tab.Closable && tab.CloseBounds.Contains(point))
            {
                CloseTab(i);
                return EditorCommand.None;
            }

            _activeTabIndex = i;
            return EditorCommand.None;
        }

        if (_openMenuScope != MenuScope.None)
        {
            _openMenuScope = MenuScope.None;
            _openMenuIndex = -1;
        }

        return EditorCommand.None;
    }

    public void Draw(
        SpriteBatch spriteBatch,
        EditorGame.Tool activeTool,
        PointerTool pointerTool,
        BrushShape brushShape,
        MapData map,
        EditorHistory history,
        int activeLayer,
        TilePalette palette,
        string factionLabel,
        string cityLabel)
    {
        RebuildLayout(palette.Bounds);

        DrawGlobalMenuBar(spriteBatch);
        DrawTabStrip(spriteBatch);
        DrawLocalMenuBar(spriteBatch);

        var openMenus = GetOpenMenuCollection();
        if (openMenus != null && _openMenuIndex >= 0 && _openMenuIndex < openMenus.Count)
            DrawDropdown(spriteBatch, openMenus[_openMenuIndex], activeTool, pointerTool);

        DrawStatusBar(spriteBatch, activeTool, pointerTool, brushShape, map, activeLayer, palette, factionLabel, cityLabel);
    }

    private void DrawGlobalMenuBar(SpriteBatch spriteBatch)
    {
        EditorTheme.FillRect(spriteBatch, _globalMenuRect, EditorTheme.Bg);
        EditorTheme.DrawVerticalAccentBar(spriteBatch, new Rectangle(8, _globalMenuRect.Y + 4, 3, _globalMenuRect.Height - 8), EditorTheme.Accent);
        EditorTheme.DrawText(spriteBatch, EditorTheme.Small, "MTEditor",
            new Vector2(16, _globalMenuRect.Y + 5), EditorTheme.Text);

        DrawMenuHeaders(spriteBatch, _globalMenus, _hoveredGlobalMenu, _openMenuScope == MenuScope.Global);
        spriteBatch.Draw(EditorTheme.Pixel, new Rectangle(_globalMenuRect.X, _globalMenuRect.Bottom - 1, _globalMenuRect.Width, 1), EditorTheme.Divider);
    }

    private void DrawTabStrip(SpriteBatch spriteBatch)
    {
        EditorTheme.FillRect(spriteBatch, _tabStripRect, EditorTheme.Panel);
        spriteBatch.Draw(EditorTheme.Pixel, new Rectangle(_tabStripRect.X, _tabStripRect.Bottom - 1, _tabStripRect.Width, 1), EditorTheme.Divider);

        foreach (var (tab, index) in _workspaceTabs.Select((tab, index) => (tab, index)))
        {
            var active = index == _activeTabIndex;
            var hovered = index == _hoveredTabIndex;
            var fill = active ? EditorTheme.Bg : hovered ? EditorTheme.PanelHover : EditorTheme.BgDeep;
            var border = active ? EditorTheme.BorderSoft : EditorTheme.Border;

            EditorTheme.FillRect(spriteBatch, tab.Bounds, fill);
            EditorTheme.DrawBorder(spriteBatch, tab.Bounds, border);

            if (active)
            {
                spriteBatch.Draw(EditorTheme.Pixel, new Rectangle(tab.Bounds.X + 1, tab.Bounds.Bottom - 1, tab.Bounds.Width - 2, 1), fill);
                spriteBatch.Draw(EditorTheme.Pixel, new Rectangle(tab.Bounds.X, tab.Bounds.Y, 3, tab.Bounds.Height), EditorTheme.Accent);
            }

            EditorTheme.DrawText(spriteBatch, EditorTheme.Small, tab.Title,
                new Vector2(tab.Bounds.X + 12, tab.Bounds.Y + 6), active ? Color.White : EditorTheme.TextDim);

            if (tab.Closable)
            {
                var color = hovered && _hoveredTabClose ? EditorTheme.Error : EditorTheme.TextMuted;
                EditorTheme.DrawText(spriteBatch, EditorTheme.Small, "x",
                    new Vector2(tab.CloseBounds.X + 4, tab.CloseBounds.Y + 1), color);
            }
        }
    }

    private void DrawLocalMenuBar(SpriteBatch spriteBatch)
    {
        EditorTheme.FillRect(spriteBatch, _localMenuRect, EditorTheme.Bg);
        DrawMenuHeaders(spriteBatch, _localMenus, _hoveredLocalMenu, _openMenuScope == MenuScope.Local);
        spriteBatch.Draw(EditorTheme.Pixel, new Rectangle(_localMenuRect.X, _localMenuRect.Bottom - 1, _localMenuRect.Width, 1), EditorTheme.Divider);
    }

    private void DrawMenuHeaders(SpriteBatch spriteBatch, List<MenuHeader> menus, int hoveredIndex, bool isScopeOpen)
    {
        for (var i = 0; i < menus.Count; i++)
        {
            var menu = menus[i];
            var open = isScopeOpen && _openMenuIndex == i;
            var hover = hoveredIndex == i;
            if (open || hover)
                EditorTheme.FillRect(spriteBatch, menu.HeaderBounds, open ? EditorTheme.PanelActive : EditorTheme.PanelHover);

            EditorTheme.DrawText(spriteBatch, EditorTheme.Small, menu.Label,
                new Vector2(menu.HeaderBounds.X + 8, menu.HeaderBounds.Y + 5), EditorTheme.Text);
        }
    }

    private void DrawDropdown(SpriteBatch spriteBatch, MenuHeader menu, EditorGame.Tool activeTool, PointerTool pointerTool)
    {
        EditorTheme.DrawShadow(spriteBatch, menu.DropdownBounds, 6);
        EditorTheme.FillRect(spriteBatch, menu.DropdownBounds, EditorTheme.Panel);
        EditorTheme.DrawBorder(spriteBatch, menu.DropdownBounds, EditorTheme.Border);

        for (var i = 0; i < menu.Items.Count; i++)
        {
            var item = menu.Items[i];
            var rect = menu.ItemBounds[i];

            if (item.Separator)
            {
                spriteBatch.Draw(EditorTheme.Pixel,
                    new Rectangle(rect.X + 6, rect.Y + rect.Height / 2, rect.Width - 12, 1),
                    EditorTheme.BorderSoft);
                continue;
            }

            var hovered = i == _hoveredDropdownItem;
            var active = IsMenuItemActive(item.Command, activeTool, pointerTool);
            if (hovered)
                EditorTheme.FillRect(spriteBatch, rect, EditorTheme.Accent);
            else if (active)
                EditorTheme.FillRect(spriteBatch, rect, EditorTheme.AccentDim);

            var textColor = hovered || active ? Color.White : EditorTheme.Text;
            if (active)
                EditorTheme.DrawText(spriteBatch, EditorTheme.Small, "•", new Vector2(rect.X + 6, rect.Y + 5), textColor);

            EditorTheme.DrawText(spriteBatch, EditorTheme.Small, item.Label,
                new Vector2(rect.X + DropdownPadX, rect.Y + 5), textColor);

            if (!string.IsNullOrEmpty(item.Shortcut))
            {
                var size = EditorTheme.Small.MeasureString(item.Shortcut);
                EditorTheme.DrawText(spriteBatch, EditorTheme.Small, item.Shortcut,
                    new Vector2(rect.Right - size.X - 10, rect.Y + 5),
                    hovered || active ? new Color(230, 230, 230) : EditorTheme.TextMuted);
            }
        }
    }

    private void DrawStatusBar(
        SpriteBatch spriteBatch,
        EditorGame.Tool activeTool,
        PointerTool pointerTool,
        BrushShape brushShape,
        MapData map,
        int activeLayer,
        TilePalette palette,
        string factionLabel,
        string cityLabel)
    {
        EditorTheme.FillRect(spriteBatch, _statusBarRect, EditorTheme.BgDeep);
        spriteBatch.Draw(EditorTheme.Pixel, new Rectangle(_statusBarRect.X, _statusBarRect.Y, _statusBarRect.Width, 1), EditorTheme.Divider);

        var selected = activeTool == EditorGame.Tool.TilePainter
            ? palette.SelectedTileId ?? "-"
            : activeTool == EditorGame.Tool.EntityPainter
                ? palette.SelectedEntityId ?? "-"
                : "—";

        var segments = new (string Label, string Value, Color Color)[]
        {
            ("Tab", ActiveTabKind.ToString(), EditorTheme.Accent),
            ("Map", map.Id, EditorTheme.TextDim),
            ("Type", LocationKinds.GetDisplayName(map.LocationKind), EditorTheme.TextDim),
            ("Faction", factionLabel, EditorTheme.Success),
            ("City", cityLabel, EditorTheme.Warning),
            ("Tool", activeTool.ToString(), EditorTheme.TextDim),
            ("Selected", selected, EditorTheme.TextDim),
            ("Layer", activeLayer.ToString(), EditorTheme.Success),
            ("Size", $"{map.Width}×{map.Height}", EditorTheme.TextDim),
            ("Areas", map.Areas.Count.ToString(), EditorTheme.Accent),
        };

        var x = _statusBarRect.X + 10f;
        var y = _statusBarRect.Y + (_statusBarRect.Height - EditorTheme.Small.MeasureString("Ay").Y) / 2f - 1;
        foreach (var (label, value, color) in segments)
        {
            EditorTheme.DrawText(spriteBatch, EditorTheme.Small, label, new Vector2(x, y), EditorTheme.TextMuted);
            x += EditorTheme.Small.MeasureString(label).X + 4;
            EditorTheme.DrawText(spriteBatch, EditorTheme.Small, value, new Vector2(x, y), color);
            x += EditorTheme.Small.MeasureString(value).X + 14;
        }

        if (_messageTimer > 0f)
        {
            var alpha = Math.Min(1f, _messageTimer);
            var size = EditorTheme.Small.MeasureString(_message);
            EditorTheme.DrawText(spriteBatch, EditorTheme.Small, _message,
                new Vector2(_statusBarRect.Right - size.X - 12, _statusBarRect.Y + 5),
                EditorTheme.Warning * alpha);
        }
    }

    private bool HandleMenuHeaderClick(List<MenuHeader> menus, Point point, MenuScope scope)
    {
        for (var i = 0; i < menus.Count; i++)
        {
            if (!menus[i].HeaderBounds.Contains(point))
                continue;

            if (_openMenuScope == scope && _openMenuIndex == i)
            {
                _openMenuScope = MenuScope.None;
                _openMenuIndex = -1;
            }
            else
            {
                _openMenuScope = scope;
                _openMenuIndex = i;
            }

            return true;
        }

        return false;
    }

    private bool TryHandleOpenDropdownClick(Point point, out EditorCommand command)
    {
        command = EditorCommand.None;

        var menus = GetOpenMenuCollection();
        if (menus == null || _openMenuIndex < 0 || _openMenuIndex >= menus.Count)
            return false;

        var menu = menus[_openMenuIndex];

        if (menu.DropdownBounds.Contains(point))
        {
            for (var i = 0; i < menu.ItemBounds.Count; i++)
            {
                if (menu.Items[i].Separator || !menu.ItemBounds[i].Contains(point))
                    continue;

                command = menu.Items[i].Command;
                _openMenuScope = MenuScope.None;
                _openMenuIndex = -1;
                return true;
            }

            // Click inside dropdown padding/background is still consumed.
            return true;
        }

        if (!menu.DropdownBounds.Contains(point))
        {
            _openMenuScope = MenuScope.None;
            _openMenuIndex = -1;
            return true;
        }

        return false;
    }

    private int FindHoveredMenu(List<MenuHeader> menus, Point point)
    {
        for (var i = 0; i < menus.Count; i++)
        {
            if (menus[i].HeaderBounds.Contains(point))
                return i;
        }

        return -1;
    }

    private List<MenuHeader>? GetOpenMenuCollection()
    {
        return _openMenuScope switch
        {
            MenuScope.Global => _globalMenus,
            MenuScope.Local => _localMenus,
            _ => null
        };
    }

    private void RebuildLayout(Rectangle paletteBounds)
    {
        var viewport = _graphics.Viewport;
        _globalMenuRect = new Rectangle(0, 0, viewport.Width, GlobalMenuHeight);
        _tabStripRect = new Rectangle(0, _globalMenuRect.Bottom, viewport.Width, TabStripHeight);
        _localMenuRect = new Rectangle(0, _tabStripRect.Bottom, viewport.Width, LocalMenuHeight);
        _statusBarRect = new Rectangle(0, viewport.Height - StatusBarHeight, viewport.Width, StatusBarHeight);

        RebuildMenuHeaders(_globalMenus, startX: 16 + (int)EditorTheme.Small.MeasureString("MTEditor").X + 18, _globalMenuRect);
        RebuildTabs();
        RebuildLocalMenus();
        RebuildMenuHeaders(_localMenus, startX: 10, _localMenuRect);
    }

    private void RebuildTabs()
    {
        var x = 8;
        foreach (var tab in _workspaceTabs)
        {
            var width = (int)EditorTheme.Small.MeasureString(tab.Title).X + (tab.Closable ? 34 : 22);
            tab.Bounds = new Rectangle(x, _tabStripRect.Bottom - 24, width, 24);
            tab.CloseBounds = tab.Closable
                ? new Rectangle(tab.Bounds.Right - 18, tab.Bounds.Y + 4, 12, 12)
                : Rectangle.Empty;
            x += width + 4;
        }
    }

    private void RebuildLocalMenus()
    {
        _localMenus.Clear();

        switch (ActiveTabKind)
        {
            case EditorWorkspaceTabKind.Map:
                _localMenus.Add(new MenuHeader
                {
                    Label = "Map",
                    Items = new()
                    {
                        new("New Map", EditorCommand.NewMap, "Ctrl+N"),
                        new("Open Map…", EditorCommand.LoadMap, "Ctrl+O"),
                        new("Save Map…", EditorCommand.SaveMap, "Ctrl+S"),
                        new("Resize Map…", EditorCommand.ResizeMap, "Ctrl+R"),
                        MenuItem.Sep(),
                        new("In-Game Maps…", EditorCommand.InGameMaps, "F6"),
                    }
                });
                _localMenus.Add(new MenuHeader
                {
                    Label = "Edit",
                    Items = new()
                    {
                        new("Undo", EditorCommand.Undo, "Ctrl+Z"),
                        new("Redo", EditorCommand.Redo, "Ctrl+Y"),
                    }
                });
                _localMenus.Add(new MenuHeader
                {
                    Label = "Tools",
                    Items = new()
                    {
                        new("Tile Painter", EditorCommand.ToolTiles, "1"),
                        new("Prototype Painter", EditorCommand.ToolPrototypes, "2"),
                        new("Spawn Points", EditorCommand.ToolSpawns, "3"),
                        new("Trigger Zones", EditorCommand.ToolTriggers, "4"),
                        new("Area Zones", EditorCommand.ToolAreas, "5"),
                        MenuItem.Sep(),
                        new("Pointer: Brush", EditorCommand.PointerBrush, "B"),
                        new("Pointer: Select", EditorCommand.PointerMouse, "V"),
                        MenuItem.Sep(),
                        new("Shape: Point", EditorCommand.ShapePoint),
                        new("Shape: Line", EditorCommand.ShapeLine),
                        new("Shape: Filled Rectangle", EditorCommand.ShapeFilledRectangle),
                        new("Shape: Hollow Rectangle", EditorCommand.ShapeHollowRectangle),
                    }
                });
                break;

            case EditorWorkspaceTabKind.Factions:
                _localMenus.Add(new MenuHeader
                {
                    Label = "Faction",
                    Items = new()
                    {
                        new("New Faction", EditorCommand.NewFaction),
                        new("Save Faction", EditorCommand.SaveFaction, "Ctrl+S"),
                        new("Delete Faction", EditorCommand.DeleteFaction),
                    }
                });
                break;

            case EditorWorkspaceTabKind.Cities:
                _localMenus.Add(new MenuHeader
                {
                    Label = "City",
                    Items = new()
                    {
                        new("New City", EditorCommand.NewCity),
                        new("Save City", EditorCommand.SaveCity, "Ctrl+S"),
                        new("Delete City", EditorCommand.DeleteCity),
                    }
                });
                break;

            case EditorWorkspaceTabKind.Professions:
                _localMenus.Add(new MenuHeader
                {
                    Label = "Profession",
                    Items = new()
                    {
                        new("New Profession", EditorCommand.NewProfession),
                        new("Save Professions", EditorCommand.SaveProfession, "Ctrl+S"),
                        new("Delete Profession", EditorCommand.DeleteProfession),
                        MenuItem.Sep(),
                        new("Reload Professions", EditorCommand.ReloadProfessions),
                    }
                });
                break;

            case EditorWorkspaceTabKind.Npcs:
                _localMenus.Add(new MenuHeader
                {
                    Label = "NPC",
                    Items = new()
                    {
                        new("New NPC", EditorCommand.NewNpc),
                        new("Save .npc", EditorCommand.SaveNpcRoster, "Ctrl+S"),
                        new("Delete NPC", EditorCommand.DeleteNpc),
                        MenuItem.Sep(),
                        new("Save As Template", EditorCommand.SaveNpcTemplate),
                        new("Reload NPCs", EditorCommand.ReloadNpcs),
                    }
                });
                break;

            case EditorWorkspaceTabKind.Prototypes:
                _localMenus.Add(new MenuHeader
                {
                    Label = "Prototype",
                    Items = new()
                    {
                        new("New Prototype", EditorCommand.NewPrototype),
                        new("Save Prototype", EditorCommand.SavePrototype, "Ctrl+S"),
                        new("Delete Prototype", EditorCommand.DeletePrototype),
                        MenuItem.Sep(),
                        new("Reload Prototypes", EditorCommand.ReloadPrototypes),
                    }
                });
                break;

            case EditorWorkspaceTabKind.GlobalSettings:
                _localMenus.Add(new MenuHeader
                {
                    Label = "Global",
                    Items = new()
                    {
                        new("Save Global Settings", EditorCommand.SaveGlobalSettings, "Ctrl+S"),
                    }
                });
                break;
        }
    }

    private void RebuildMenuHeaders(List<MenuHeader> menus, int startX, Rectangle menuRect)
    {
        var x = startX;
        foreach (var menu in menus)
        {
            var width = (int)EditorTheme.Small.MeasureString(menu.Label).X + MenuItemPadX * 2;
            menu.HeaderBounds = new Rectangle(x, menuRect.Y, width, menuRect.Height);
            x += width;

            var maxWidth = DropdownMinW;
            foreach (var item in menu.Items)
            {
                if (item.Separator)
                    continue;

                var itemWidth = (int)EditorTheme.Small.MeasureString(item.Label).X + DropdownPadX * 2;
                if (!string.IsNullOrEmpty(item.Shortcut))
                    itemWidth += (int)EditorTheme.Small.MeasureString(item.Shortcut).X + 24;
                maxWidth = Math.Max(maxWidth, itemWidth);
            }

            var totalHeight = DropdownPadV * 2 + menu.Items.Sum(item => item.Separator ? 8 : DropdownRowH);
            menu.DropdownBounds = new Rectangle(menu.HeaderBounds.X, menu.HeaderBounds.Bottom, maxWidth, totalHeight);
            menu.ItemBounds.Clear();

            var y = menu.DropdownBounds.Y + DropdownPadV;
            foreach (var item in menu.Items)
            {
                var height = item.Separator ? 8 : DropdownRowH;
                menu.ItemBounds.Add(new Rectangle(menu.DropdownBounds.X, y, menu.DropdownBounds.Width, height));
                y += height;
            }
        }
    }

    private void CloseTab(int index)
    {
        if (index < 0 || index >= _workspaceTabs.Count || !_workspaceTabs[index].Closable || _workspaceTabs.Count == 1)
            return;

        _workspaceTabs.RemoveAt(index);
        _activeTabIndex = Math.Clamp(_activeTabIndex, 0, _workspaceTabs.Count - 1);
    }

    public void EnsureTab(EditorWorkspaceTabKind kind)
    {
        var existingIndex = _workspaceTabs.FindIndex(tab => tab.Kind == kind);
        if (existingIndex >= 0)
        {
            _activeTabIndex = existingIndex;
            return;
        }

        _workspaceTabs.Add(new WorkspaceTab
        {
            Kind = kind,
            Title = kind switch
            {
                EditorWorkspaceTabKind.Map => "Map Editor",
                EditorWorkspaceTabKind.Factions => "Factions",
                EditorWorkspaceTabKind.Cities => "Cities",
                EditorWorkspaceTabKind.Professions => "Professions",
                EditorWorkspaceTabKind.Npcs => "NPCs",
                EditorWorkspaceTabKind.Prototypes => "Prototypes",
                EditorWorkspaceTabKind.GlobalSettings => "Global Settings",
                _ => kind.ToString()
            },
            Closable = true
        });
        _activeTabIndex = _workspaceTabs.Count - 1;
    }

    public bool TryCloseCurrentTab()
    {
        var tab = _workspaceTabs[_activeTabIndex];
        if (!tab.Closable || _workspaceTabs.Count == 1)
            return false;

        CloseTab(_activeTabIndex);
        return true;
    }

    private static bool IsMenuItemActive(EditorCommand command, EditorGame.Tool tool, PointerTool pointer)
    {
        return command switch
        {
            EditorCommand.ToolTiles => tool == EditorGame.Tool.TilePainter,
            EditorCommand.ToolPrototypes => tool == EditorGame.Tool.EntityPainter,
            EditorCommand.ToolSpawns => tool == EditorGame.Tool.SpawnPoint,
            EditorCommand.ToolTriggers => tool == EditorGame.Tool.TriggerZone,
            EditorCommand.ToolAreas => tool == EditorGame.Tool.AreaZone,
            EditorCommand.PointerBrush => pointer == PointerTool.Brush,
            EditorCommand.PointerMouse => pointer == PointerTool.Mouse,
            _ => false,
        };
    }
}
