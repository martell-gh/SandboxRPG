#nullable enable
using System;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MTEngine.World;

namespace MTEditor.UI;

public sealed class CityEditorChange
{
    public string? RenamedFromId { get; init; }
    public string? RenamedToId { get; init; }
    public string? DeletedCityId { get; init; }
}

public sealed class CityEditorPanel
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
    private string? _selectedCityId;
    private string _draftId = "";
    private string _draftName = "";
    private FocusField _focus;
    private int _listScroll;
    private Keys? _heldDeleteKey;
    private long _nextDeleteRepeatAt;

    private const int DeleteRepeatInitialDelayMs = 350;
    private const int DeleteRepeatIntervalMs = 32;

    public bool IsTyping => _focus != FocusField.None;
    public Rectangle Bounds => _bounds;
    private bool HasSelection => !string.IsNullOrWhiteSpace(_selectedCityId);

    public CityEditorPanel(GraphicsDevice graphics)
    {
        _graphics = graphics;
    }

    public void SyncSelection(WorldData worldData)
    {
        if (!string.IsNullOrWhiteSpace(_selectedCityId) && worldData.GetCity(_selectedCityId) == null)
            StartNewCity();
        else if (!string.IsNullOrWhiteSpace(_selectedCityId))
            LoadCity(worldData.GetCity(_selectedCityId));
        else
        {
            var firstCity = worldData.Cities
                .OrderBy(city => city.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(city => city.Id, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();

            if (firstCity != null)
            {
                _selectedCityId = firstCity.Id;
                LoadCity(firstCity);
                _focus = FocusField.None;
            }
            else
            {
                StartNewCity();
            }
        }
    }

    public CityEditorChange? Update(MouseState mouse, MouseState prev, KeyboardState keys, KeyboardState prevKeys, WorldData worldData, Action<string> showMessage)
    {
        RebuildLayout();

        if (_focus != FocusField.None && IsPressed(keys, prevKeys, Keys.Enter))
            return SaveCity(worldData, showMessage);

        var scrollDelta = mouse.ScrollWheelValue - prev.ScrollWheelValue;
        if (scrollDelta != 0 && _listRect.Contains(mouse.Position))
            _listScroll = Math.Max(0, _listScroll - Math.Sign(scrollDelta));

        if (mouse.LeftButton == ButtonState.Pressed && prev.LeftButton == ButtonState.Released)
        {
            if (!_bounds.Contains(mouse.Position))
            {
                _focus = FocusField.None;
                return null;
            }

            if (TryHandleCitySelection(mouse.Position, worldData))
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
        }

        HandleTextInput(keys, prevKeys);
        return null;
    }

    public CityEditorChange? CreateNew(WorldData worldData, Action<string> showMessage)
    {
        var seedName = string.IsNullOrWhiteSpace(_draftName) ? "City" : _draftName.Trim();
        var uniqueId = GenerateUniqueCityId(worldData, seedName);
        var uniqueName = GenerateUniqueCityName(worldData, seedName);

        var city = new CityData
        {
            Id = uniqueId,
            Name = uniqueName
        };

        worldData.Cities.Add(city);
        worldData.Normalize();
        _selectedCityId = uniqueId;
        LoadCity(worldData.GetCity(uniqueId));
        _focus = FocusField.Name;
        showMessage($"Created city '{uniqueId}'");

        return new CityEditorChange
        {
            RenamedToId = uniqueId
        };
    }

    public CityEditorChange? SaveCurrent(WorldData worldData, Action<string> showMessage)
        => SaveCity(worldData, showMessage);

    public CityEditorChange? DeleteCurrent(WorldData worldData, Action<string> showMessage)
        => DeleteCity(worldData, showMessage);

    public void Draw(SpriteBatch spriteBatch, WorldData worldData)
    {
        RebuildLayout();

        EditorTheme.DrawPanel(spriteBatch, _bounds, EditorTheme.Bg, EditorTheme.Border);
        var headerRect = new Rectangle(_bounds.X, _bounds.Y, _bounds.Width, 30);
        EditorTheme.FillRect(spriteBatch, headerRect, EditorTheme.Panel);
        spriteBatch.Draw(EditorTheme.Pixel, new Rectangle(headerRect.X, headerRect.Y, 3, headerRect.Height), EditorTheme.Warning);
        EditorTheme.DrawText(spriteBatch, EditorTheme.Medium, "CITY EDITOR",
            new Vector2(headerRect.X + 12, headerRect.Y + 8), EditorTheme.Text);

        DrawCityList(spriteBatch, worldData);
        DrawDetails(spriteBatch, worldData);
    }

    private void DrawCityList(SpriteBatch spriteBatch, WorldData worldData)
    {
        EditorTheme.DrawPanel(spriteBatch, _listRect, EditorTheme.PanelAlt, EditorTheme.Border);
        EditorTheme.DrawText(spriteBatch, EditorTheme.Small, $"Cities: {worldData.Cities.Count}",
            new Vector2(_listRect.X + 10, _listRect.Y + 8), EditorTheme.TextDim);

        var y = _listRect.Y + 30;
        foreach (var city in worldData.Cities
                     .OrderBy(city => city.Name, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(city => city.Id, StringComparer.OrdinalIgnoreCase)
                     .Skip(_listScroll))
        {
            if (y + 32 > _listRect.Bottom - 10)
                break;

            var rowRect = new Rectangle(_listRect.X + 8, y, _listRect.Width - 16, 30);
            var selected = string.Equals(city.Id, _selectedCityId, StringComparison.OrdinalIgnoreCase);
            EditorTheme.FillRect(spriteBatch, rowRect, selected ? EditorTheme.Warning : EditorTheme.BgDeep);
            EditorTheme.DrawBorder(spriteBatch, rowRect, selected ? EditorTheme.BorderSoft : EditorTheme.Border);
            EditorTheme.DrawText(spriteBatch, EditorTheme.Small, city.Name,
                new Vector2(rowRect.X + 8, rowRect.Y + 6), selected ? Color.Black : EditorTheme.Text);
            EditorTheme.DrawText(spriteBatch, EditorTheme.Tiny, city.Id,
                new Vector2(rowRect.X + 8, rowRect.Y + 18), selected ? new Color(45, 30, 0) : EditorTheme.TextMuted);
            y += 34;
        }
    }

    private void DrawDetails(SpriteBatch spriteBatch, WorldData worldData)
    {
        EditorTheme.DrawPanel(spriteBatch, _detailRect, EditorTheme.PanelAlt, EditorTheme.Border);

        if (!HasSelection)
        {
            var subtitle = worldData.Cities.Count == 0
                ? "Use the menubar: City -> New City"
                : "Select a city from the list or use City -> New City";
            DrawEmptyState(spriteBatch, "No city selected", subtitle);
            return;
        }

        EditorTheme.DrawText(spriteBatch, EditorTheme.Small, "City ID",
            new Vector2(_detailRect.X + 12, _detailRect.Y + 12), EditorTheme.TextDim);
        DrawField(spriteBatch, _idFieldRect, _draftId, _focus == FocusField.Id);

        EditorTheme.DrawText(spriteBatch, EditorTheme.Small, "City Name",
            new Vector2(_detailRect.X + 12, _detailRect.Y + 52), EditorTheme.TextDim);
        DrawField(spriteBatch, _nameFieldRect, _draftName, _focus == FocusField.Name);
        EditorTheme.DrawText(spriteBatch, EditorTheme.Tiny, "Use the menubar: New City, Save City, Delete City",
            new Vector2(_detailRect.X + 12, _detailRect.Y + 104), EditorTheme.TextMuted);
        EditorTheme.DrawText(spriteBatch, EditorTheme.Tiny, "Enter saves the current selected city",
            new Vector2(_detailRect.X + 12, _detailRect.Y + 118), EditorTheme.TextMuted);

        EditorTheme.DrawText(spriteBatch, EditorTheme.Small, "Cities currently only store id and name. Faction and district assignment happens on the map tab.",
            new Vector2(_detailRect.X + 12, _detailRect.Y + 154), EditorTheme.TextDim);
    }

    private CityEditorChange? SaveCity(WorldData worldData, Action<string> showMessage)
    {
        var newId = _draftId.Trim();
        var newName = string.IsNullOrWhiteSpace(_draftName) ? newId : _draftName.Trim();
        if (string.IsNullOrWhiteSpace(newId))
        {
            showMessage("City id cannot be empty");
            return null;
        }

        var duplicate = worldData.Cities.FirstOrDefault(city =>
            string.Equals(city.Id, newId, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(city.Id, _selectedCityId, StringComparison.OrdinalIgnoreCase));
        if (duplicate != null)
        {
            showMessage($"City id '{newId}' already exists");
            return null;
        }

        var oldId = _selectedCityId;
        var city = !string.IsNullOrWhiteSpace(oldId) ? worldData.GetCity(oldId) : null;
        if (city == null)
        {
            city = new CityData();
            worldData.Cities.Add(city);
        }

        city.Id = newId;
        city.Name = newName;
        worldData.Normalize();
        _selectedCityId = newId;
        LoadCity(worldData.GetCity(newId));
        showMessage(string.IsNullOrWhiteSpace(oldId) ? $"Created city '{newId}'" : $"Saved city '{newId}'");

        return new CityEditorChange
        {
            RenamedFromId = string.Equals(oldId, newId, StringComparison.OrdinalIgnoreCase) ? null : oldId,
            RenamedToId = newId
        };
    }

    private CityEditorChange? DeleteCity(WorldData worldData, Action<string> showMessage)
    {
        if (string.IsNullOrWhiteSpace(_selectedCityId))
        {
            showMessage("Select a city to delete");
            return null;
        }

        var deletedId = _selectedCityId;
        worldData.Cities.RemoveAll(city => string.Equals(city.Id, deletedId, StringComparison.OrdinalIgnoreCase));
        worldData.Normalize();
        StartNewCity();
        showMessage($"Deleted city '{deletedId}'");

        return new CityEditorChange
        {
            DeletedCityId = deletedId
        };
    }

    private bool TryHandleCitySelection(Point point, WorldData worldData)
    {
        if (!_listRect.Contains(point))
            return false;

        var y = _listRect.Y + 30;
        foreach (var city in worldData.Cities
                     .OrderBy(city => city.Name, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(city => city.Id, StringComparer.OrdinalIgnoreCase)
                     .Skip(_listScroll))
        {
            if (y + 32 > _listRect.Bottom - 10)
                break;

            var rowRect = new Rectangle(_listRect.X + 8, y, _listRect.Width - 16, 30);
            if (rowRect.Contains(point))
            {
                _selectedCityId = city.Id;
                LoadCity(city);
                return true;
            }

            y += 34;
        }

        return false;
    }

    private void LoadCity(CityData? city)
    {
        if (city == null)
        {
            _draftId = "";
            _draftName = "";
            return;
        }

        _draftId = city.Id;
        _draftName = city.Name;
    }

    private void StartNewCity()
    {
        _selectedCityId = null;
        _draftId = "";
        _draftName = "";
        _focus = FocusField.Id;
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
    }

    private void DrawEmptyState(SpriteBatch spriteBatch, string title, string subtitle)
    {
        var box = new Rectangle(_detailRect.X + 24, _detailRect.Y + 24, _detailRect.Width - 48, 120);
        EditorTheme.FillRect(spriteBatch, box, EditorTheme.BgDeep);
        EditorTheme.DrawBorder(spriteBatch, box, EditorTheme.BorderSoft);
        spriteBatch.Draw(EditorTheme.Pixel, new Rectangle(box.X, box.Y, 3, box.Height), EditorTheme.Warning);

        EditorTheme.DrawText(spriteBatch, EditorTheme.Title, title,
            new Vector2(box.X + 16, box.Y + 22), EditorTheme.Text);
        EditorTheme.DrawText(spriteBatch, EditorTheme.Body, subtitle,
            new Vector2(box.X + 16, box.Y + 56), EditorTheme.TextDim);
    }

    private static void DrawField(SpriteBatch spriteBatch, Rectangle rect, string text, bool focused)
    {
        EditorTheme.FillRect(spriteBatch, rect, focused ? EditorTheme.BgDeep : EditorTheme.Bg);
        EditorTheme.DrawBorder(spriteBatch, rect, focused ? EditorTheme.Warning : EditorTheme.Border);
        EditorTheme.DrawText(spriteBatch, EditorTheme.Body, text + (focused ? "│" : ""),
            new Vector2(rect.X + 6, rect.Y + 4), EditorTheme.Text);
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
            Keys.OemMinus => '_',
            Keys.OemPeriod => '.',
            _ => '\0'
        };
    }

    private static bool IsPressed(KeyboardState keys, KeyboardState prevKeys, Keys key)
        => keys.IsKeyDown(key) && prevKeys.IsKeyUp(key);

    private static string GenerateUniqueCityId(WorldData worldData, string seedName)
    {
        var slug = Slugify(seedName);
        if (string.IsNullOrWhiteSpace(slug))
            slug = "city";

        var baseId = $"city_{slug}";
        var candidate = baseId;
        var index = 2;
        while (worldData.GetCity(candidate) != null)
        {
            candidate = $"{baseId}_{index}";
            index++;
        }

        return candidate;
    }

    private static string GenerateUniqueCityName(WorldData worldData, string seedName)
    {
        var baseName = string.IsNullOrWhiteSpace(seedName) ? "City" : seedName.Trim();
        var candidate = baseName;
        var index = 2;
        while (worldData.Cities.Any(city => string.Equals(city.Name, candidate, StringComparison.OrdinalIgnoreCase)))
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
