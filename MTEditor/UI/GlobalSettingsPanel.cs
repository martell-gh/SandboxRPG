#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MTEngine.Core;
using MTEngine.World;

namespace MTEditor.UI;

/// <summary>
/// Tab-вкладка "Global Settings" — редактирование стартовой локации
/// (StartingMapId / StartingSpawnId). Показывает все карты, чтобы старт не зависел от ручного ввода.
/// </summary>
public sealed class GlobalSettingsPanel
{
    private enum FocusField
    {
        None,
        SpawnId
    }

    private enum SubTab
    {
        StartingLocation,
        LocationGraph
    }

    private readonly GraphicsDevice _graphics;
    private readonly PrototypeManager _prototypes;
    private readonly LocationGraphPanel _graphPanel;

    private SubTab _subTab = SubTab.StartingLocation;
    private Rectangle _subTabBarRect;
    private Rectangle _subTabStartingRect;
    private Rectangle _subTabGraphRect;
    private Rectangle _contentRect;

    private Rectangle _bounds;
    private Rectangle _listRect;
    private Rectangle _detailRect;
    private Rectangle _spawnFieldRect;
    private Rectangle _factionFieldRect;
    private Rectangle _factionPickerRect;
    private Rectangle _saveButtonRect;
    private Rectangle _outfitPickerRect;

    private string _draftMapId = "";
    private string _draftSpawnId = "default";
    private string _draftFactionId = "";
    private readonly Dictionary<string, string> _draftOutfit = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Rectangle> _outfitSlotRects = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<OutfitOption> _outfitOptions = new();
    private readonly List<FactionOption> _factionOptions = new();
    private bool _hasLoaded;
    private FocusField _focus;
    private int _listScroll;
    private bool _factionPickerOpen;
    private int _factionPickerScroll;
    private string _openOutfitSlot = "";
    private int _outfitPickerScroll;
    private Keys? _heldDeleteKey;
    private long _nextDeleteRepeatAt;

    private const int DeleteRepeatInitialDelayMs = 350;
    private const int DeleteRepeatIntervalMs = 32;
    private static readonly string[] OutfitSlots = { "torso", "pants", "shoes", "back" };

    private readonly record struct OutfitOption(string PrototypeId, string Label, string Hint);
    private readonly record struct FactionOption(string FactionId, string Label, string Hint);

    public bool IsTyping => _focus != FocusField.None || (_subTab == SubTab.LocationGraph && _graphPanel.IsTyping);
    public Rectangle Bounds => _bounds;

    public GlobalSettingsPanel(GraphicsDevice graphics, PrototypeManager prototypes)
    {
        _graphics = graphics;
        _prototypes = prototypes;
        _graphPanel = new LocationGraphPanel(graphics);
    }

    public void SyncFromWorldData(WorldData worldData)
    {
        _draftMapId = worldData.StartingMapId ?? "";
        _draftSpawnId = string.IsNullOrWhiteSpace(worldData.StartingSpawnId) ? "default" : worldData.StartingSpawnId;
        _draftFactionId = worldData.StartingFactionId ?? "";
        _draftOutfit.Clear();
        foreach (var (slot, protoId) in worldData.StartingOutfit)
        {
            if (!string.IsNullOrWhiteSpace(slot) && !string.IsNullOrWhiteSpace(protoId))
                _draftOutfit[slot.Trim()] = protoId.Trim();
        }
        _graphPanel.SyncFromWorldData(worldData);
        _hasLoaded = true;
    }

    public bool Update(
        MouseState mouse,
        MouseState prev,
        KeyboardState keys,
        KeyboardState prevKeys,
        WorldData worldData,
        IReadOnlyList<MapCatalogEntry> mapCatalog,
        Action<string> showMessage)
    {
        RebuildLayout();
        if (!_hasLoaded)
            SyncFromWorldData(worldData);

        if (mouse.LeftButton == ButtonState.Pressed && prev.LeftButton == ButtonState.Released)
        {
            if (_subTabStartingRect.Contains(mouse.Position))
            {
                _subTab = SubTab.StartingLocation;
                _focus = FocusField.None;
                CloseFactionPicker();
                CloseOutfitPicker();
                return false;
            }
            if (_subTabGraphRect.Contains(mouse.Position))
            {
                _subTab = SubTab.LocationGraph;
                _focus = FocusField.None;
                CloseFactionPicker();
                CloseOutfitPicker();
                return false;
            }
        }

        if (_subTab == SubTab.LocationGraph)
        {
            _graphPanel.Update(mouse, prev, keys, prevKeys, mapCatalog, _contentRect);
            return false;
        }

        var maps = mapCatalog
            .OrderByDescending(m => string.Equals(m.Id, _draftMapId, StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(m => m.InGame)
            .ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Сбрасываем скролл если количество карт изменилось
        var maxScroll = Math.Max(0, maps.Count - 1);
        if (_listScroll > maxScroll)
            _listScroll = 0;

        if (_focus == FocusField.SpawnId && IsPressed(keys, prevKeys, Keys.Enter))
            return SaveCurrent(worldData, showMessage);

        var scrollDelta = mouse.ScrollWheelValue - prev.ScrollWheelValue;
        if (scrollDelta != 0)
        {
            if (!string.IsNullOrEmpty(_openOutfitSlot) && _outfitPickerRect.Contains(mouse.Position))
                _outfitPickerScroll = Math.Max(0, _outfitPickerScroll - Math.Sign(scrollDelta));
            else if (_factionPickerOpen && _factionPickerRect.Contains(mouse.Position))
                _factionPickerScroll = Math.Max(0, _factionPickerScroll - Math.Sign(scrollDelta));
            else if (_listRect.Contains(mouse.Position))
                _listScroll = Math.Max(0, _listScroll - Math.Sign(scrollDelta));
        }

        var changed = false;
        if (mouse.LeftButton == ButtonState.Pressed && prev.LeftButton == ButtonState.Released)
        {
            if (!_bounds.Contains(mouse.Position))
            {
                _focus = FocusField.None;
            }
            else if (_saveButtonRect.Contains(mouse.Position))
            {
                changed = SaveCurrent(worldData, showMessage);
            }
            else if (_spawnFieldRect.Contains(mouse.Position))
            {
                CloseFactionPicker();
                CloseOutfitPicker();
                _focus = FocusField.SpawnId;
            }
            else if (TryHandleFactionPickerClick(mouse.Position))
            {
                changed = false;
            }
            else if (_factionFieldRect.Contains(mouse.Position))
            {
                _focus = FocusField.None;
                CloseOutfitPicker();
                OpenFactionPicker(worldData);
                changed = false;
            }
            else if (TryHandleOutfitPickerClick(mouse.Position))
            {
                changed = false;
            }
            else if (TryOpenOutfitPicker(mouse.Position))
            {
                changed = false;
            }
            else
            {
                _focus = FocusField.None;
                CloseFactionPicker();
                CloseOutfitPicker();
                // Клик по карте — только меняем draft, persist делает только Save (Ctrl+S или кнопка).
                // Иначе автоперсист бы перезагрузил worldData и затёр выбор.
                TryHandleMapClick(mouse.Position, maps);
            }
        }

        HandleTextInput(keys, prevKeys);
        return changed;
    }

    public bool SaveCurrent(WorldData worldData, Action<string> showMessage)
    {
        var mapId = (_draftMapId ?? "").Trim();
        var spawnId = string.IsNullOrWhiteSpace(_draftSpawnId) ? "default" : _draftSpawnId.Trim();

        worldData.StartingMapId = mapId;
        worldData.StartingSpawnId = spawnId;
        worldData.StartingFactionId = string.IsNullOrWhiteSpace(_draftFactionId) ? "" : _draftFactionId.Trim();
        worldData.StartingOutfit = _draftOutfit
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
            .ToDictionary(pair => pair.Key.Trim(), pair => pair.Value.Trim(), StringComparer.OrdinalIgnoreCase);

        _graphPanel.WriteToWorldData(worldData);

        showMessage(string.IsNullOrEmpty(mapId)
            ? "Starting location cleared"
            : $"Starting location: {mapId} @ {spawnId}; faction: {FactionDisplayName(worldData, worldData.StartingFactionId)}; outfit slots: {worldData.StartingOutfit.Count}");

        _draftSpawnId = spawnId;
        return true;
    }

    public void Draw(SpriteBatch spriteBatch, WorldData worldData, IReadOnlyList<MapCatalogEntry> mapCatalog)
    {
        RebuildLayout();
        if (!_hasLoaded)
            SyncFromWorldData(worldData);

        EditorTheme.DrawPanel(spriteBatch, _bounds, EditorTheme.Bg, EditorTheme.Border);

        var headerRect = new Rectangle(_bounds.X, _bounds.Y, _bounds.Width, 30);
        EditorTheme.FillRect(spriteBatch, headerRect, EditorTheme.Panel);
        spriteBatch.Draw(EditorTheme.Pixel, new Rectangle(headerRect.X, headerRect.Y, 3, headerRect.Height), EditorTheme.Accent);
        EditorTheme.DrawText(spriteBatch, EditorTheme.Medium, "GLOBAL SETTINGS",
            new Vector2(headerRect.X + 12, headerRect.Y + 8), EditorTheme.Text);

        DrawSubTabBar(spriteBatch);

        if (_subTab == SubTab.LocationGraph)
        {
            _graphPanel.Draw(spriteBatch, mapCatalog);
            return;
        }

        DrawMapList(spriteBatch, mapCatalog);
        DrawDetails(spriteBatch, worldData);
        DrawFactionPicker(spriteBatch, worldData);
        DrawOutfitPicker(spriteBatch);
    }

    private void DrawSubTabBar(SpriteBatch sb)
    {
        EditorTheme.FillRect(sb, _subTabBarRect, EditorTheme.Panel);
        EditorTheme.DrawBorder(sb, _subTabBarRect, EditorTheme.Border);
        DrawSubTabButton(sb, _subTabStartingRect, "Starting Location", _subTab == SubTab.StartingLocation);
        DrawSubTabButton(sb, _subTabGraphRect, "Location Graph", _subTab == SubTab.LocationGraph);
    }

    private static void DrawSubTabButton(SpriteBatch sb, Rectangle rect, string label, bool active)
    {
        EditorTheme.FillRect(sb, rect, active ? EditorTheme.Accent : EditorTheme.PanelAlt);
        EditorTheme.DrawBorder(sb, rect, active ? EditorTheme.AccentDim : EditorTheme.Border);
        var size = EditorTheme.Small.MeasureString(label);
        EditorTheme.DrawText(sb, EditorTheme.Small, label,
            new Vector2(rect.X + (rect.Width - size.X) / 2f, rect.Y + (rect.Height - size.Y) / 2f - 1),
            active ? Color.White : EditorTheme.TextDim);
    }

    private void DrawMapList(SpriteBatch spriteBatch, IReadOnlyList<MapCatalogEntry> mapCatalog)
    {
        EditorTheme.DrawPanel(spriteBatch, _listRect, EditorTheme.PanelAlt, EditorTheme.Border);

        var maps = mapCatalog
            .OrderByDescending(m => string.Equals(m.Id, _draftMapId, StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(m => m.InGame)
            .ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        EditorTheme.DrawText(spriteBatch, EditorTheme.Small, $"Maps: {maps.Count}",
            new Vector2(_listRect.X + 10, _listRect.Y + 8), EditorTheme.TextDim);

        if (maps.Count == 0)
        {
            EditorTheme.DrawText(spriteBatch, EditorTheme.Small,
                "No .map.json files found in SandboxGame/Maps.",
                new Vector2(_listRect.X + 10, _listRect.Y + 28), EditorTheme.TextMuted);
            return;
        }

        var rows = maps.Skip(_listScroll).ToList();
        var y = _listRect.Y + 30;
        foreach (var map in rows)
        {
            if (y + 32 > _listRect.Bottom - 10)
                break;

            var rowRect = new Rectangle(_listRect.X + 8, y, _listRect.Width - 16, 30);
            var selected = string.Equals(map.Id, _draftMapId, StringComparison.OrdinalIgnoreCase);
            EditorTheme.FillRect(spriteBatch, rowRect, selected ? EditorTheme.Accent : EditorTheme.BgDeep);
            EditorTheme.DrawBorder(spriteBatch, rowRect, selected ? EditorTheme.AccentDim : EditorTheme.Border);
            EditorTheme.DrawText(spriteBatch, EditorTheme.Small, map.Name,
                new Vector2(rowRect.X + 8, rowRect.Y + 6), selected ? Color.White : EditorTheme.Text);
            EditorTheme.DrawText(spriteBatch, EditorTheme.Tiny, map.Id,
                new Vector2(rowRect.X + 8, rowRect.Y + 18), selected ? new Color(225, 235, 255) : EditorTheme.TextMuted);
            var badge = map.InGame ? "InGame" : "Editor-only";
            var badgeColor = map.InGame ? EditorTheme.Success : EditorTheme.Warning;
            var badgeSize = EditorTheme.Tiny.MeasureString(badge);
            EditorTheme.DrawText(spriteBatch, EditorTheme.Tiny, badge,
                new Vector2(rowRect.Right - badgeSize.X - 8, rowRect.Y + 18), selected ? Color.White : badgeColor);
            y += 34;
        }
    }

    private void DrawDetails(SpriteBatch spriteBatch, WorldData worldData)
    {
        EditorTheme.DrawPanel(spriteBatch, _detailRect, EditorTheme.PanelAlt, EditorTheme.Border);

        EditorTheme.DrawText(spriteBatch, EditorTheme.Small, "Starting Map ID",
            new Vector2(_detailRect.X + 12, _detailRect.Y + 12), EditorTheme.TextDim);
        var mapLabel = string.IsNullOrWhiteSpace(_draftMapId) ? "(не выбрано)" : _draftMapId;
        var labelRect = new Rectangle(_detailRect.X + 12, _detailRect.Y + 28, _detailRect.Width - 24, 26);
        EditorTheme.FillRect(spriteBatch, labelRect, EditorTheme.Bg);
        EditorTheme.DrawBorder(spriteBatch, labelRect, EditorTheme.Border);
        EditorTheme.DrawText(spriteBatch, EditorTheme.Body, mapLabel,
            new Vector2(labelRect.X + 6, labelRect.Y + 4),
            string.IsNullOrWhiteSpace(_draftMapId) ? EditorTheme.TextMuted : EditorTheme.Text);

        EditorTheme.DrawText(spriteBatch, EditorTheme.Small, "Starting Spawn ID",
            new Vector2(_detailRect.X + 12, _detailRect.Y + 64), EditorTheme.TextDim);
        DrawField(spriteBatch, _spawnFieldRect, _draftSpawnId, _focus == FocusField.SpawnId);

        EditorTheme.DrawText(spriteBatch, EditorTheme.Small, "Starting Player Faction",
            new Vector2(_detailRect.X + 12, _detailRect.Y + 110), EditorTheme.TextDim);
        DrawFactionField(spriteBatch, worldData);

        DrawOutfitSection(spriteBatch);

        EditorTheme.FillRect(spriteBatch, _saveButtonRect, EditorTheme.Accent);
        EditorTheme.DrawBorder(spriteBatch, _saveButtonRect, EditorTheme.AccentDim);
        var saveText = "Save (Ctrl+S)";
        var saveSize = EditorTheme.Body.MeasureString(saveText);
        EditorTheme.DrawText(spriteBatch, EditorTheme.Body, saveText,
            new Vector2(_saveButtonRect.X + (_saveButtonRect.Width - saveSize.X) / 2f,
                        _saveButtonRect.Y + (_saveButtonRect.Height - saveSize.Y) / 2f - 1),
            Color.White);

        EditorTheme.DrawText(spriteBatch, EditorTheme.Tiny,
            "Click a map on the left to pick it as the starting map. Press Save (or Ctrl+S) to write world_data.json.",
            new Vector2(_detailRect.X + 12, _saveButtonRect.Bottom + 12), EditorTheme.TextMuted);
    }

    private void DrawOutfitSection(SpriteBatch spriteBatch)
    {
        _outfitSlotRects.Clear();
        var y = _detailRect.Y + 166;
        EditorTheme.DrawText(spriteBatch, EditorTheme.Small, "Starting Hero Outfit",
            new Vector2(_detailRect.X + 12, y), EditorTheme.TextDim);
        y += 18;

        foreach (var slot in OutfitSlots)
        {
            var labelRect = new Rectangle(_detailRect.X + 12, y, 76, 26);
            var fieldRect = new Rectangle(labelRect.Right + 8, y, _detailRect.Width - 108, 26);
            _outfitSlotRects[slot] = fieldRect;

            EditorTheme.DrawText(spriteBatch, EditorTheme.Small, SlotDisplayName(slot),
                new Vector2(labelRect.X, labelRect.Y + 6), EditorTheme.TextDim);

            var selected = _draftOutfit.GetValueOrDefault(slot, "");
            var active = string.Equals(_openOutfitSlot, slot, StringComparison.OrdinalIgnoreCase);
            EditorTheme.FillRect(spriteBatch, fieldRect, active ? EditorTheme.BgDeep : EditorTheme.Bg);
            EditorTheme.DrawBorder(spriteBatch, fieldRect, active ? EditorTheme.Accent : EditorTheme.Border);
            EditorTheme.DrawText(spriteBatch, EditorTheme.Small, Truncate(OutfitDisplayName(selected), fieldRect.Width - 28),
                new Vector2(fieldRect.X + 6, fieldRect.Y + 6), string.IsNullOrWhiteSpace(selected) ? EditorTheme.TextMuted : EditorTheme.Text);
            EditorTheme.DrawText(spriteBatch, EditorTheme.Small, "v",
                new Vector2(fieldRect.Right - 16, fieldRect.Y + 5), active ? EditorTheme.Text : EditorTheme.TextMuted);

            y += 32;
        }
    }

    private bool TryOpenOutfitPicker(Point point)
    {
        foreach (var (slot, rect) in _outfitSlotRects)
        {
            if (!rect.Contains(point))
                continue;

            _focus = FocusField.None;
            CloseFactionPicker();
            _openOutfitSlot = slot;
            _outfitPickerScroll = 0;
            _outfitOptions.Clear();
            _outfitOptions.AddRange(BuildOutfitOptions(slot));

            const int rowH = 32;
            var visibleRows = Math.Min(8, Math.Max(1, _outfitOptions.Count));
            var height = visibleRows * rowH + 4;
            var y = rect.Bottom + 2;
            if (y + height > _detailRect.Bottom - 48)
                y = Math.Max(_detailRect.Y + 34, rect.Y - height - 2);
            _outfitPickerRect = new Rectangle(rect.X, y, rect.Width, height);
            return true;
        }

        return false;
    }

    private bool TryHandleOutfitPickerClick(Point point)
    {
        if (string.IsNullOrEmpty(_openOutfitSlot))
            return false;

        if (!_outfitPickerRect.Contains(point))
        {
            CloseOutfitPicker();
            return false;
        }

        const int rowH = 32;
        var localY = point.Y - _outfitPickerRect.Y - 2;
        if (localY < 0)
            return true;

        var index = _outfitPickerScroll + localY / rowH;
        if (index >= 0 && index < _outfitOptions.Count)
        {
            var option = _outfitOptions[index];
            if (string.IsNullOrWhiteSpace(option.PrototypeId))
                _draftOutfit.Remove(_openOutfitSlot);
            else
                _draftOutfit[_openOutfitSlot] = option.PrototypeId;
            CloseOutfitPicker();
        }

        return true;
    }

    private void DrawOutfitPicker(SpriteBatch spriteBatch)
    {
        if (string.IsNullOrEmpty(_openOutfitSlot) || _outfitOptions.Count == 0)
            return;

        const int rowH = 32;
        EditorTheme.DrawShadow(spriteBatch, _outfitPickerRect, 5);
        EditorTheme.DrawPanel(spriteBatch, _outfitPickerRect, EditorTheme.Panel, EditorTheme.AccentDim);

        var y = _outfitPickerRect.Y + 2;
        foreach (var option in _outfitOptions.Skip(_outfitPickerScroll))
        {
            if (y + rowH > _outfitPickerRect.Bottom - 2)
                break;

            var row = new Rectangle(_outfitPickerRect.X + 2, y, _outfitPickerRect.Width - 4, rowH - 1);
            var selected = string.Equals(option.PrototypeId, _draftOutfit.GetValueOrDefault(_openOutfitSlot, ""), StringComparison.OrdinalIgnoreCase);
            EditorTheme.FillRect(spriteBatch, row, selected ? EditorTheme.PanelActive : EditorTheme.BgDeep);
            EditorTheme.DrawText(spriteBatch, EditorTheme.Small, Truncate(option.Label, row.Width - 16),
                new Vector2(row.X + 8, row.Y + 5), selected ? EditorTheme.Text : EditorTheme.TextDim);
            if (!string.IsNullOrWhiteSpace(option.Hint))
                EditorTheme.DrawText(spriteBatch, EditorTheme.Tiny, Truncate(option.Hint, row.Width - 16),
                    new Vector2(row.X + 8, row.Y + 19), EditorTheme.TextMuted);

            y += rowH;
        }
    }

    private void CloseOutfitPicker()
    {
        _openOutfitSlot = "";
        _outfitOptions.Clear();
        _outfitPickerScroll = 0;
        _outfitPickerRect = Rectangle.Empty;
    }

    private void DrawFactionField(SpriteBatch spriteBatch, WorldData worldData)
    {
        var active = _factionPickerOpen;
        var selected = _draftFactionId;
        EditorTheme.FillRect(spriteBatch, _factionFieldRect, active ? EditorTheme.BgDeep : EditorTheme.Bg);
        EditorTheme.DrawBorder(spriteBatch, _factionFieldRect, active ? EditorTheme.Accent : EditorTheme.Border);
        EditorTheme.DrawText(spriteBatch, EditorTheme.Body, Truncate(FactionDisplayName(worldData, selected), _factionFieldRect.Width - 32),
            new Vector2(_factionFieldRect.X + 6, _factionFieldRect.Y + 4),
            string.IsNullOrWhiteSpace(selected) ? EditorTheme.TextMuted : EditorTheme.Text);
        EditorTheme.DrawText(spriteBatch, EditorTheme.Small, "v",
            new Vector2(_factionFieldRect.Right - 16, _factionFieldRect.Y + 5), active ? EditorTheme.Text : EditorTheme.TextMuted);
    }

    private void OpenFactionPicker(WorldData worldData)
    {
        _factionPickerOpen = true;
        _factionPickerScroll = 0;
        _factionOptions.Clear();
        _factionOptions.AddRange(BuildFactionOptions(worldData));

        const int rowH = 32;
        var visibleRows = Math.Min(8, Math.Max(1, _factionOptions.Count));
        var height = visibleRows * rowH + 4;
        var y = _factionFieldRect.Bottom + 2;
        if (y + height > _detailRect.Bottom - 48)
            y = Math.Max(_detailRect.Y + 34, _factionFieldRect.Y - height - 2);
        _factionPickerRect = new Rectangle(_factionFieldRect.X, y, _factionFieldRect.Width, height);
    }

    private bool TryHandleFactionPickerClick(Point point)
    {
        if (!_factionPickerOpen)
            return false;

        if (!_factionPickerRect.Contains(point))
        {
            CloseFactionPicker();
            return false;
        }

        const int rowH = 32;
        var localY = point.Y - _factionPickerRect.Y - 2;
        if (localY < 0)
            return true;

        var index = _factionPickerScroll + localY / rowH;
        if (index >= 0 && index < _factionOptions.Count)
        {
            _draftFactionId = _factionOptions[index].FactionId;
            CloseFactionPicker();
        }

        return true;
    }

    private void DrawFactionPicker(SpriteBatch spriteBatch, WorldData worldData)
    {
        if (!_factionPickerOpen || _factionOptions.Count == 0)
            return;

        const int rowH = 32;
        EditorTheme.DrawShadow(spriteBatch, _factionPickerRect, 5);
        EditorTheme.DrawPanel(spriteBatch, _factionPickerRect, EditorTheme.Panel, EditorTheme.AccentDim);

        var y = _factionPickerRect.Y + 2;
        foreach (var option in _factionOptions.Skip(_factionPickerScroll))
        {
            if (y + rowH > _factionPickerRect.Bottom - 2)
                break;

            var row = new Rectangle(_factionPickerRect.X + 2, y, _factionPickerRect.Width - 4, rowH - 1);
            var selected = string.Equals(option.FactionId, _draftFactionId, StringComparison.OrdinalIgnoreCase);
            EditorTheme.FillRect(spriteBatch, row, selected ? EditorTheme.PanelActive : EditorTheme.BgDeep);
            EditorTheme.DrawText(spriteBatch, EditorTheme.Small, Truncate(option.Label, row.Width - 16),
                new Vector2(row.X + 8, row.Y + 5), selected ? EditorTheme.Text : EditorTheme.TextDim);
            if (!string.IsNullOrWhiteSpace(option.Hint))
                EditorTheme.DrawText(spriteBatch, EditorTheme.Tiny, Truncate(option.Hint, row.Width - 16),
                    new Vector2(row.X + 8, row.Y + 19), EditorTheme.TextMuted);

            y += rowH;
        }
    }

    private void CloseFactionPicker()
    {
        _factionPickerOpen = false;
        _factionPickerScroll = 0;
        _factionOptions.Clear();
        _factionPickerRect = Rectangle.Empty;
    }

    private static List<FactionOption> BuildFactionOptions(WorldData worldData)
    {
        var options = new List<FactionOption> { new("", "None", "player starts without faction") };
        options.AddRange(worldData.Factions
            .OrderBy(faction => LocalizationManager.T(string.IsNullOrWhiteSpace(faction.Name) ? faction.Id : faction.Name), StringComparer.OrdinalIgnoreCase)
            .ThenBy(faction => faction.Id, StringComparer.OrdinalIgnoreCase)
            .Select(faction => new FactionOption(
                faction.Id,
                LocalizationManager.T(string.IsNullOrWhiteSpace(faction.Name) ? faction.Id : faction.Name),
                faction.Id)));
        return options;
    }

    private static string FactionDisplayName(WorldData worldData, string factionId)
    {
        if (string.IsNullOrWhiteSpace(factionId))
            return "None";

        var faction = worldData.GetFaction(factionId);
        return faction == null
            ? $"{factionId} (missing)"
            : LocalizationManager.T(string.IsNullOrWhiteSpace(faction.Name) ? faction.Id : faction.Name);
    }

    private List<OutfitOption> BuildOutfitOptions(string slot)
    {
        var options = new List<OutfitOption> { new("", "None", "leave this equipment slot empty") };
        options.AddRange(_prototypes.GetAllEntities()
            .Where(proto => string.Equals(ReadWearableSlot(proto), slot, StringComparison.OrdinalIgnoreCase))
            .OrderBy(proto => proto.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(proto => proto.Id, StringComparer.OrdinalIgnoreCase)
            .Select(proto => new OutfitOption(proto.Id, string.IsNullOrWhiteSpace(proto.Name) ? proto.Id : proto.Name, proto.Id)));
        return options;
    }

    private string OutfitDisplayName(string protoId)
    {
        if (string.IsNullOrWhiteSpace(protoId))
            return "None";

        var proto = _prototypes.GetEntity(protoId);
        return proto == null
            ? $"{protoId} (missing)"
            : string.IsNullOrWhiteSpace(proto.Name) ? proto.Id : proto.Name;
    }

    private static string ReadWearableSlot(EntityPrototype proto)
    {
        if (proto.Components?["wearable"] is not System.Text.Json.Nodes.JsonObject wearable)
            return "";

        return wearable["slot"]?.GetValue<string>() ?? "";
    }

    private bool TryHandleMapClick(Point point, IReadOnlyList<MapCatalogEntry> maps)
    {
        var rows = maps.Skip(_listScroll).ToList();
        var y = _listRect.Y + 30;
        foreach (var map in rows)
        {
            // прекращаем, когда строка выходит за пределы
            if (y + 34 > _listRect.Bottom - 10)
                break;

            // расширяем высоту до 34 px: включает саму строку и отступ
            var hitRect = new Rectangle(_listRect.X + 8, y, _listRect.Width - 16, 34);
            if (hitRect.Contains(point))
            {
                _draftMapId = map.Id;
                if (string.IsNullOrWhiteSpace(_draftSpawnId))
                    _draftSpawnId = "default";
                return true;
            }
            y += 34;
        }
        return false;
    }

    private void HandleTextInput(KeyboardState keys, KeyboardState prevKeys)
    {
        if (_focus == FocusField.None)
        {
            ResetDeleteRepeat();
            return;
        }

        var deleteHandled = false;
        foreach (var key in keys.GetPressedKeys().OrderBy(static k => k))
        {
            if (key == Keys.Escape)
            {
                if (!prevKeys.IsKeyDown(key))
                {
                    _focus = FocusField.None;
                    ResetDeleteRepeat();
                    return;
                }
                continue;
            }

            if (key is Keys.Back or Keys.Delete)
            {
                if (ShouldRepeatKey(keys, prevKeys, key))
                    ApplyDelete();
                deleteHandled = true;
                continue;
            }

            if (prevKeys.IsKeyDown(key))
                continue;

            var character = KeyToChar(key, keys.IsKeyDown(Keys.LeftShift) || keys.IsKeyDown(Keys.RightShift));
            if (character == '\0')
                continue;

            _draftSpawnId += character;
        }

        if (!deleteHandled)
            ResetDeleteRepeat();
    }

    private void ApplyDelete()
    {
        if (_focus == FocusField.SpawnId && _draftSpawnId.Length > 0)
            _draftSpawnId = _draftSpawnId[..^1];
    }

    private bool ShouldRepeatKey(KeyboardState keys, KeyboardState prevKeys, Keys key)
    {
        var isDown = keys.IsKeyDown(key);
        if (!isDown)
        {
            if (_heldDeleteKey == key)
                ResetDeleteRepeat();
            return false;
        }

        var now = Environment.TickCount64;
        if (prevKeys.IsKeyUp(key) || _heldDeleteKey != key)
        {
            _heldDeleteKey = key;
            _nextDeleteRepeatAt = now + DeleteRepeatInitialDelayMs;
            return true;
        }

        if (now < _nextDeleteRepeatAt)
            return false;

        _nextDeleteRepeatAt = now + DeleteRepeatIntervalMs;
        return true;
    }

    private void ResetDeleteRepeat()
    {
        _heldDeleteKey = null;
        _nextDeleteRepeatAt = 0;
    }

    private void RebuildLayout()
    {
        var viewport = _graphics.Viewport;
        _bounds = new Rectangle(12, 68, viewport.Width - 24, viewport.Height - 102);

        const int subTabBarHeight = 30;
        _subTabBarRect = new Rectangle(_bounds.X, _bounds.Y + 30, _bounds.Width, subTabBarHeight);
        _subTabStartingRect = new Rectangle(_subTabBarRect.X + 8, _subTabBarRect.Y + 4, 160, subTabBarHeight - 8);
        _subTabGraphRect = new Rectangle(_subTabStartingRect.Right + 6, _subTabBarRect.Y + 4, 160, subTabBarHeight - 8);

        var contentTop = _subTabBarRect.Bottom + 8;
        _contentRect = new Rectangle(_bounds.X + 4, contentTop, _bounds.Width - 8, _bounds.Bottom - contentTop - 4);

        _listRect = new Rectangle(_bounds.X + 12, contentTop, 280, _bounds.Bottom - contentTop - 12);
        _detailRect = new Rectangle(_listRect.Right + 12, contentTop, _bounds.Right - _listRect.Right - 24, _bounds.Bottom - contentTop - 12);
        _spawnFieldRect = new Rectangle(_detailRect.X + 12, _detailRect.Y + 80, _detailRect.Width - 24, 26);
        _factionFieldRect = new Rectangle(_detailRect.X + 12, _detailRect.Y + 126, _detailRect.Width - 24, 26);
        _saveButtonRect = new Rectangle(_detailRect.X + 12, _detailRect.Y + 334, 160, 30);
    }

    private static void DrawField(SpriteBatch spriteBatch, Rectangle rect, string text, bool focused)
    {
        EditorTheme.FillRect(spriteBatch, rect, focused ? EditorTheme.BgDeep : EditorTheme.Bg);
        EditorTheme.DrawBorder(spriteBatch, rect, focused ? EditorTheme.Warning : EditorTheme.Border);
        EditorTheme.DrawText(spriteBatch, EditorTheme.Body, text + (focused ? "│" : ""),
            new Vector2(rect.X + 6, rect.Y + 4), EditorTheme.Text);
    }

    private static char KeyToChar(Keys key, bool shift)
    {
        if (key >= Keys.A && key <= Keys.Z)
            return shift ? (char)('A' + (key - Keys.A)) : (char)('a' + (key - Keys.A));
        if (key >= Keys.D0 && key <= Keys.D9)
            return (char)('0' + (key - Keys.D0));

        return key switch
        {
            Keys.OemMinus => '_',
            Keys.OemPeriod => '.',
            _ => '\0'
        };
    }

    private static bool IsPressed(KeyboardState keys, KeyboardState prevKeys, Keys key)
        => keys.IsKeyDown(key) && prevKeys.IsKeyUp(key);

    private static string SlotDisplayName(string slot)
        => slot switch
        {
            "torso" => "Torso",
            "pants" => "Pants",
            "shoes" => "Shoes",
            "back" => "Back",
            _ => slot
        };

    private static string Truncate(string value, int maxWidth)
    {
        if (EditorTheme.Small.MeasureString(value).X <= maxWidth)
            return value;
        while (value.Length > 1 && EditorTheme.Small.MeasureString(value + "...").X > maxWidth)
            value = value[..^1];
        return value + "...";
    }
}
