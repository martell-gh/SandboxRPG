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

public sealed class FactionEditorChange
{
    public string? RenamedFromId { get; init; }
    public string? RenamedToId { get; init; }
    public string? DeletedFactionId { get; init; }
    public bool SkipReload { get; init; }
}

public sealed class FactionEditorPanel
{
    private enum FocusField
    {
        None,
        Id,
        Name
    }

    private readonly GraphicsDevice _graphics;

    private Rectangle _bounds;
    private Rectangle _listRect;
    private Rectangle _detailRect;
    private Rectangle _idFieldRect;
    private Rectangle _nameFieldRect;
    private Rectangle _relationRect;

    private string? _selectedFactionId;
    private string _draftId = "";
    private string _draftName = "";
    private readonly Dictionary<string, int> _draftRelations = new(StringComparer.OrdinalIgnoreCase);
    private FocusField _focus;
    private int _listScroll;
    private int _relationScroll;
    private Keys? _heldDeleteKey;
    private long _nextDeleteRepeatAt;

    private const int DeleteRepeatInitialDelayMs = 350;
    private const int DeleteRepeatIntervalMs = 32;

    public bool IsTyping => _focus != FocusField.None;
    public Rectangle Bounds => _bounds;
    private bool HasSelection => !string.IsNullOrWhiteSpace(_selectedFactionId);

    public FactionEditorPanel(GraphicsDevice graphics)
    {
        _graphics = graphics;
    }

    public void SyncSelection(WorldData worldData)
    {
        if (!string.IsNullOrWhiteSpace(_selectedFactionId) && worldData.GetFaction(_selectedFactionId) == null)
            StartNewFaction();
        else if (!string.IsNullOrWhiteSpace(_selectedFactionId))
            LoadFaction(worldData.GetFaction(_selectedFactionId));
        else
        {
            var firstFaction = worldData.Factions
                .OrderBy(DisplayFactionName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(faction => faction.Id, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();

            if (firstFaction != null)
            {
                _selectedFactionId = firstFaction.Id;
                LoadFaction(firstFaction);
                _focus = FocusField.None;
            }
            else
            {
                StartNewFaction();
            }
        }
    }

    public FactionEditorChange? Update(MouseState mouse, MouseState prev, KeyboardState keys, KeyboardState prevKeys, WorldData worldData, Action<string> showMessage)
    {
        RebuildLayout();

        if (_focus != FocusField.None && IsPressed(keys, prevKeys, Keys.Enter))
            return SaveFaction(worldData, showMessage);

        var scrollDelta = mouse.ScrollWheelValue - prev.ScrollWheelValue;
        if (scrollDelta != 0)
        {
            if (_listRect.Contains(mouse.Position))
                _listScroll = Math.Max(0, _listScroll - Math.Sign(scrollDelta));
            else if (_relationRect.Contains(mouse.Position))
                _relationScroll = Math.Max(0, _relationScroll - Math.Sign(scrollDelta));
        }

        if (mouse.LeftButton == ButtonState.Pressed && prev.LeftButton == ButtonState.Released)
        {
            if (!_bounds.Contains(mouse.Position))
            {
                _focus = FocusField.None;
                return null;
            }

            if (TryHandleFactionSelection(mouse.Position, worldData))
                return null;

            if (!HasSelection)
            {
                _focus = FocusField.None;
                return null;
            }

            if (_idFieldRect.Contains(mouse.Position))
                _focus = FocusField.Id;
            else if (_nameFieldRect.Contains(mouse.Position))
                _focus = FocusField.Name;
            else
                _focus = FocusField.None;

            if (TryHandleRelationClick(mouse.Position, worldData))
            {
                return new FactionEditorChange
                {
                    SkipReload = true
                };
            }
        }

        HandleTextInput(keys, prevKeys);
        return null;
    }

    public FactionEditorChange? CreateNew(WorldData worldData, Action<string> showMessage)
    {
        var seedName = string.IsNullOrWhiteSpace(_draftName) ? "Faction" : _draftName.Trim();
        var uniqueId = GenerateUniqueFactionId(worldData, seedName);
        var uniqueName = GenerateUniqueFactionName(worldData, seedName);

        var faction = new FactionData
        {
            Id = uniqueId,
            Name = uniqueName
        };

        worldData.Factions.Add(faction);
        worldData.Normalize();
        _selectedFactionId = uniqueId;
        LoadFaction(worldData.GetFaction(uniqueId));
        _focus = FocusField.Name;
        showMessage($"Created faction '{uniqueId}'");

        return new FactionEditorChange
        {
            RenamedToId = uniqueId
        };
    }

    public FactionEditorChange? SaveCurrent(WorldData worldData, Action<string> showMessage)
        => SaveFaction(worldData, showMessage);

    public FactionEditorChange? DeleteCurrent(WorldData worldData, Action<string> showMessage)
        => DeleteFaction(worldData, showMessage);

    public void Draw(SpriteBatch spriteBatch, WorldData worldData)
    {
        RebuildLayout();

        EditorTheme.DrawPanel(spriteBatch, _bounds, EditorTheme.Bg, EditorTheme.Border);

        var headerRect = new Rectangle(_bounds.X, _bounds.Y, _bounds.Width, 30);
        EditorTheme.FillRect(spriteBatch, headerRect, EditorTheme.Panel);
        spriteBatch.Draw(EditorTheme.Pixel, new Rectangle(headerRect.X, headerRect.Y, 3, headerRect.Height), EditorTheme.Accent);
        EditorTheme.DrawText(spriteBatch, EditorTheme.Medium, "FACTION EDITOR",
            new Vector2(headerRect.X + 12, headerRect.Y + 8), EditorTheme.Text);

        DrawFactionList(spriteBatch, worldData);
        DrawDetails(spriteBatch, worldData);
    }

    private void DrawFactionList(SpriteBatch spriteBatch, WorldData worldData)
    {
        EditorTheme.DrawPanel(spriteBatch, _listRect, EditorTheme.PanelAlt, EditorTheme.Border);
        EditorTheme.DrawText(spriteBatch, EditorTheme.Small, $"Factions: {worldData.Factions.Count}",
            new Vector2(_listRect.X + 10, _listRect.Y + 8), EditorTheme.TextDim);

        var rows = worldData.Factions
            .OrderBy(DisplayFactionName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(faction => faction.Id, StringComparer.OrdinalIgnoreCase)
            .Skip(_listScroll)
            .ToList();

        var y = _listRect.Y + 30;
        foreach (var faction in rows)
        {
            if (y + 32 > _listRect.Bottom - 10)
                break;

            var rowRect = new Rectangle(_listRect.X + 8, y, _listRect.Width - 16, 30);
            var selected = string.Equals(faction.Id, _selectedFactionId, StringComparison.OrdinalIgnoreCase);
            EditorTheme.FillRect(spriteBatch, rowRect, selected ? EditorTheme.Accent : EditorTheme.BgDeep);
            EditorTheme.DrawBorder(spriteBatch, rowRect, selected ? EditorTheme.AccentDim : EditorTheme.Border);
            EditorTheme.DrawText(spriteBatch, EditorTheme.Small, DisplayFactionName(faction),
                new Vector2(rowRect.X + 8, rowRect.Y + 6), selected ? Color.White : EditorTheme.Text);
            EditorTheme.DrawText(spriteBatch, EditorTheme.Tiny, faction.Id,
                new Vector2(rowRect.X + 8, rowRect.Y + 18), selected ? new Color(225, 235, 255) : EditorTheme.TextMuted);
            y += 34;
        }
    }

    private void DrawDetails(SpriteBatch spriteBatch, WorldData worldData)
    {
        EditorTheme.DrawPanel(spriteBatch, _detailRect, EditorTheme.PanelAlt, EditorTheme.Border);

        if (!HasSelection)
        {
            var subtitle = worldData.Factions.Count == 0
                ? "Use the menubar: Faction -> New Faction"
                : "Select a faction from the list or use Faction -> New Faction";
            DrawEmptyState(spriteBatch, "No faction selected", subtitle);
            return;
        }

        EditorTheme.DrawText(spriteBatch, EditorTheme.Small, "Faction ID",
            new Vector2(_detailRect.X + 12, _detailRect.Y + 12), EditorTheme.TextDim);
        DrawField(spriteBatch, _idFieldRect, _draftId, _focus == FocusField.Id);

        EditorTheme.DrawText(spriteBatch, EditorTheme.Small, "Faction Name",
            new Vector2(_detailRect.X + 12, _detailRect.Y + 52), EditorTheme.TextDim);
        DrawField(spriteBatch, _nameFieldRect, _draftName, _focus == FocusField.Name);
        EditorTheme.DrawText(spriteBatch, EditorTheme.Tiny, "Use the menubar: New Faction, Save Faction, Delete Faction",
            new Vector2(_detailRect.X + 12, _detailRect.Y + 104), EditorTheme.TextMuted);
        EditorTheme.DrawText(spriteBatch, EditorTheme.Tiny, "Enter saves the current selected faction",
            new Vector2(_detailRect.X + 12, _detailRect.Y + 118), EditorTheme.TextMuted);

        EditorTheme.DrawText(spriteBatch, EditorTheme.Small, "Relations",
            new Vector2(_relationRect.X, _relationRect.Y - 18), EditorTheme.TextDim);
        EditorTheme.DrawPanel(spriteBatch, _relationRect, EditorTheme.BgDeep, EditorTheme.Border);

        var relationRows = worldData.Factions
            .Where(faction =>
                !string.Equals(faction.Id, _draftId, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(faction.Id, _selectedFactionId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(DisplayFactionName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(faction => faction.Id, StringComparer.OrdinalIgnoreCase)
            .Skip(_relationScroll)
            .ToList();

        var y = _relationRect.Y + 8;
        foreach (var faction in relationRows)
        {
            if (y + 28 > _relationRect.Bottom - 8)
                break;

            var rowRect = new Rectangle(_relationRect.X + 8, y, _relationRect.Width - 16, 26);
            EditorTheme.FillRect(spriteBatch, rowRect, EditorTheme.Panel);
            EditorTheme.DrawBorder(spriteBatch, rowRect, EditorTheme.Border);

            EditorTheme.DrawText(spriteBatch, EditorTheme.Small, DisplayFactionName(faction),
                new Vector2(rowRect.X + 8, rowRect.Y + 5), EditorTheme.Text);
            EditorTheme.DrawText(spriteBatch, EditorTheme.Tiny, faction.Id,
                new Vector2(rowRect.X + 8, rowRect.Y + 16), EditorTheme.TextMuted);

            var value = _draftRelations.GetValueOrDefault(faction.Id);
            var valueText = value >= 0 ? $"+{value}" : value.ToString();
            var valueColor = GetRelationColor(value);
            var valueSize = EditorTheme.Small.MeasureString(valueText);
            var firstButtonRect = GetRelationButtonRect(rowRect, 0);
            EditorTheme.DrawText(spriteBatch, EditorTheme.Small, valueText,
                new Vector2(firstButtonRect.X - valueSize.X - 12, rowRect.Y + (rowRect.Height - valueSize.Y) / 2f - 1), valueColor);

            DrawRelationButton(spriteBatch, GetRelationButtonRect(rowRect, 0), "-10");
            DrawRelationButton(spriteBatch, GetRelationButtonRect(rowRect, 1), "-1");
            DrawRelationButton(spriteBatch, GetRelationButtonRect(rowRect, 2), "0");
            DrawRelationButton(spriteBatch, GetRelationButtonRect(rowRect, 3), "+1");
            DrawRelationButton(spriteBatch, GetRelationButtonRect(rowRect, 4), "+10");

            y += 30;
        }
    }

    private FactionEditorChange? SaveFaction(WorldData worldData, Action<string> showMessage)
    {
        var newId = _draftId.Trim();
        var newName = string.IsNullOrWhiteSpace(_draftName) ? newId : _draftName.Trim();
        if (string.IsNullOrWhiteSpace(newId))
        {
            showMessage("Faction id cannot be empty");
            return null;
        }

        var duplicate = worldData.Factions.FirstOrDefault(faction =>
            string.Equals(faction.Id, newId, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(faction.Id, _selectedFactionId, StringComparison.OrdinalIgnoreCase));
        if (duplicate != null)
        {
            showMessage($"Faction id '{newId}' already exists");
            return null;
        }

        var oldId = _selectedFactionId;
        if (!string.IsNullOrWhiteSpace(oldId) && !string.Equals(oldId, newId, StringComparison.OrdinalIgnoreCase))
            worldData.RenameFactionReferences(oldId, newId);

        var existing = !string.IsNullOrWhiteSpace(oldId)
            ? worldData.GetFaction(oldId)
            : null;

        if (existing == null)
        {
            existing = new FactionData();
            worldData.Factions.Add(existing);
        }

        existing.Id = newId;
        existing.Name = newName;
        existing.Relations.Clear();
        worldData.RemoveFactionReferences(newId);

        foreach (var faction in worldData.Factions.Where(faction => !string.Equals(faction.Id, newId, StringComparison.OrdinalIgnoreCase)).ToList())
            worldData.SetFactionRelation(newId, faction.Id, _draftRelations.GetValueOrDefault(faction.Id));

        worldData.Normalize();
        _selectedFactionId = newId;
        LoadFaction(worldData.GetFaction(newId));
        showMessage(string.IsNullOrWhiteSpace(oldId) ? $"Created faction '{newId}'" : $"Saved faction '{newId}'");

        return new FactionEditorChange
        {
            RenamedFromId = string.Equals(oldId, newId, StringComparison.OrdinalIgnoreCase) ? null : oldId,
            RenamedToId = newId
        };
    }

    private FactionEditorChange? DeleteFaction(WorldData worldData, Action<string> showMessage)
    {
        if (string.IsNullOrWhiteSpace(_selectedFactionId))
        {
            showMessage("Select a faction to delete");
            return null;
        }

        var deletedId = _selectedFactionId;
        worldData.Factions.RemoveAll(faction => string.Equals(faction.Id, deletedId, StringComparison.OrdinalIgnoreCase));
        worldData.RemoveFactionReferences(deletedId);
        worldData.Normalize();
        StartNewFaction();
        showMessage($"Deleted faction '{deletedId}'");

        return new FactionEditorChange
        {
            DeletedFactionId = deletedId
        };
    }

    private bool TryHandleFactionSelection(Point point, WorldData worldData)
    {
        if (!_listRect.Contains(point))
            return false;

        var y = _listRect.Y + 30;
        foreach (var faction in worldData.Factions
                     .OrderBy(DisplayFactionName, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(faction => faction.Id, StringComparer.OrdinalIgnoreCase)
                     .Skip(_listScroll))
        {
            if (y + 32 > _listRect.Bottom - 10)
                break;

            var rowRect = new Rectangle(_listRect.X + 8, y, _listRect.Width - 16, 30);
            if (rowRect.Contains(point))
            {
                _selectedFactionId = faction.Id;
                LoadFaction(faction);
                return true;
            }

            y += 34;
        }

        return false;
    }

    private bool TryHandleRelationClick(Point point, WorldData worldData)
    {
        if (!_relationRect.Contains(point) || string.IsNullOrWhiteSpace(_selectedFactionId))
            return false;

        var y = _relationRect.Y + 8;
        foreach (var faction in worldData.Factions
                     .Where(faction =>
                         !string.Equals(faction.Id, _draftId, StringComparison.OrdinalIgnoreCase)
                         && !string.Equals(faction.Id, _selectedFactionId, StringComparison.OrdinalIgnoreCase))
                     .OrderBy(DisplayFactionName, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(faction => faction.Id, StringComparer.OrdinalIgnoreCase)
                     .Skip(_relationScroll))
        {
            if (y + 28 > _relationRect.Bottom - 8)
                break;

            var rowRect = new Rectangle(_relationRect.X + 8, y, _relationRect.Width - 16, 26);
            for (var i = 0; i < 5; i++)
            {
                var buttonRect = GetRelationButtonRect(rowRect, i);
                if (!buttonRect.Contains(point))
                    continue;

                var delta = i switch
                {
                    0 => -10,
                    1 => -1,
                    2 => int.MinValue,
                    3 => 1,
                    4 => 10,
                    _ => 0
                };

                var newValue = delta == int.MinValue
                    ? 0
                    : Math.Clamp(_draftRelations.GetValueOrDefault(faction.Id) + delta, -100, 100);

                if (newValue == 0)
                    _draftRelations.Remove(faction.Id);
                else
                    _draftRelations[faction.Id] = newValue;

                worldData.SetFactionRelation(_selectedFactionId, faction.Id, newValue);

                return true;
            }

            y += 30;
        }

        return false;
    }

    private void LoadFaction(FactionData? faction)
    {
        _draftRelations.Clear();
        if (faction == null)
        {
            _draftId = "";
            _draftName = "";
            return;
        }

        _draftId = faction.Id;
        _draftName = faction.Name;
        foreach (var relation in faction.Relations)
            _draftRelations[relation.FactionId] = relation.Value;
    }

    private void StartNewFaction()
    {
        _selectedFactionId = null;
        _draftId = "";
        _draftName = "";
        _draftRelations.Clear();
        _focus = FocusField.Id;
        _relationScroll = 0;
        ResetDeleteRepeat();
    }

    private void HandleTextInput(KeyboardState keys, KeyboardState prevKeys)
    {
        if (_focus == FocusField.None)
        {
            ResetDeleteRepeat();
            return;
        }

        var deleteHandled = false;
        foreach (var key in keys.GetPressedKeys().OrderBy(static key => key))
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

            if (key == Keys.Tab)
            {
                if (!prevKeys.IsKeyDown(key))
                    _focus = _focus == FocusField.Id ? FocusField.Name : FocusField.Id;
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

            var character = KeyToChar(key, keys.IsKeyDown(Keys.LeftShift) || keys.IsKeyDown(Keys.RightShift), _focus == FocusField.Name);
            if (character == '\0')
                continue;

            if (_focus == FocusField.Id)
                _draftId += character;
            else
                _draftName += character;
        }

        if (!deleteHandled)
            ResetDeleteRepeat();
    }

    private void ApplyDelete()
    {
        if (_focus == FocusField.Id && _draftId.Length > 0)
            _draftId = _draftId[..^1];
        else if (_focus == FocusField.Name && _draftName.Length > 0)
            _draftName = _draftName[..^1];
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
        _listRect = new Rectangle(_bounds.X + 12, _bounds.Y + 42, 280, _bounds.Height - 54);
        _detailRect = new Rectangle(_listRect.Right + 12, _bounds.Y + 42, _bounds.Right - _listRect.Right - 24, _bounds.Height - 54);

        _idFieldRect = new Rectangle(_detailRect.X + 12, _detailRect.Y + 28, _detailRect.Width - 24, 26);
        _nameFieldRect = new Rectangle(_detailRect.X + 12, _detailRect.Y + 68, _detailRect.Width - 24, 26);
        _relationRect = new Rectangle(_detailRect.X + 12, _detailRect.Y + 154, _detailRect.Width - 24, _detailRect.Bottom - (_detailRect.Y + 166));
    }

    private void DrawEmptyState(SpriteBatch spriteBatch, string title, string subtitle)
    {
        var box = new Rectangle(_detailRect.X + 24, _detailRect.Y + 24, _detailRect.Width - 48, 120);
        EditorTheme.FillRect(spriteBatch, box, EditorTheme.BgDeep);
        EditorTheme.DrawBorder(spriteBatch, box, EditorTheme.BorderSoft);
        spriteBatch.Draw(EditorTheme.Pixel, new Rectangle(box.X, box.Y, 3, box.Height), EditorTheme.Accent);

        EditorTheme.DrawText(spriteBatch, EditorTheme.Title, title,
            new Vector2(box.X + 16, box.Y + 22), EditorTheme.Text);
        EditorTheme.DrawText(spriteBatch, EditorTheme.Body, subtitle,
            new Vector2(box.X + 16, box.Y + 56), EditorTheme.TextDim);
    }

    private static void DrawField(SpriteBatch spriteBatch, Rectangle rect, string text, bool focused)
    {
        EditorTheme.FillRect(spriteBatch, rect, focused ? EditorTheme.BgDeep : EditorTheme.Bg);
        EditorTheme.DrawBorder(spriteBatch, rect, focused ? EditorTheme.Accent : EditorTheme.Border);
        EditorTheme.DrawText(spriteBatch, EditorTheme.Body, text + (focused ? "│" : ""),
            new Vector2(rect.X + 6, rect.Y + 4), EditorTheme.Text);
    }

    private static void DrawRelationButton(SpriteBatch spriteBatch, Rectangle rect, string label)
    {
        EditorTheme.FillRect(spriteBatch, rect, EditorTheme.PanelAlt);
        EditorTheme.DrawBorder(spriteBatch, rect, EditorTheme.BorderSoft);
        var size = EditorTheme.Tiny.MeasureString(label);
        EditorTheme.DrawText(spriteBatch, EditorTheme.Tiny, label,
            new Vector2(rect.Center.X - size.X / 2f, rect.Y + (rect.Height - size.Y) / 2f - 1), EditorTheme.TextDim);
    }

    private static Rectangle GetRelationButtonRect(Rectangle rowRect, int index)
    {
        const int width = 32;
        const int gap = 4;
        var startX = rowRect.Right - (width * 5 + gap * 4) - 8;
        return new Rectangle(startX + index * (width + gap), rowRect.Y + 3, width, rowRect.Height - 6);
    }

    private static Color GetRelationColor(int value)
    {
        if (Math.Abs(value) <= 10)
            return EditorTheme.Warning;

        return value > 0 ? EditorTheme.Success : EditorTheme.Error;
    }

    private static char KeyToChar(Keys key, bool shift, bool allowSpaces)
    {
        if (key >= Keys.A && key <= Keys.Z)
            return shift ? (char)('A' + (key - Keys.A)) : (char)('a' + (key - Keys.A));
        if (key >= Keys.D0 && key <= Keys.D9)
            return (char)('0' + (key - Keys.D0));

        return key switch
        {
            Keys.Space when allowSpaces => ' ',
            Keys.OemMinus => shift ? '_' : '-',
            Keys.OemPeriod => '.',
            _ => '\0'
        };
    }

    private static string DisplayFactionName(FactionData faction)
        => LocalizationManager.T(string.IsNullOrWhiteSpace(faction.Name) ? faction.Id : faction.Name);

    private static bool IsPressed(KeyboardState keys, KeyboardState prevKeys, Keys key)
        => keys.IsKeyDown(key) && prevKeys.IsKeyUp(key);

    private static string GenerateUniqueFactionId(WorldData worldData, string seedName)
    {
        var slug = Slugify(seedName);
        if (string.IsNullOrWhiteSpace(slug))
            slug = "faction";

        var baseId = $"fac_{slug}";
        var candidate = baseId;
        var index = 2;
        while (worldData.GetFaction(candidate) != null)
        {
            candidate = $"{baseId}_{index}";
            index++;
        }

        return candidate;
    }

    private static string GenerateUniqueFactionName(WorldData worldData, string seedName)
    {
        var baseName = string.IsNullOrWhiteSpace(seedName) ? "Faction" : seedName.Trim();
        var candidate = baseName;
        var index = 2;
        while (worldData.Factions.Any(faction => string.Equals(faction.Name, candidate, StringComparison.OrdinalIgnoreCase)))
        {
            candidate = $"{baseName} {index}";
            index++;
        }

        return candidate;
    }

    private static string Slugify(string value)
    {
        var chars = value
            .Trim()
            .ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '_')
            .ToArray();

        var slug = new string(chars);
        while (slug.Contains("__", StringComparison.Ordinal))
            slug = slug.Replace("__", "_", StringComparison.Ordinal);

        return slug.Trim('_');
    }
}
