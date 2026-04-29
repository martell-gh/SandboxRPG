#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MTEngine.Combat;
using MTEngine.Core;
using MTEngine.Npc;
using System.Text.Json.Nodes;

namespace MTEditor.UI;

public sealed class ProfessionEditorChange
{
    public string? RenamedFromId { get; init; }
    public string? RenamedToId { get; init; }
    public string? DeletedProfessionId { get; init; }
}

public sealed class ProfessionEditorPanel
{
    private enum FocusField
    {
        None,
        Id,
        Name,
        Description
    }

    private readonly GraphicsDevice _graphics;
    private readonly PrototypeManager _prototypes;

    private Rectangle _bounds;
    private Rectangle _listRect;
    private Rectangle _detailRect;
    private Rectangle _idRect;
    private Rectangle _nameRect;
    private Rectangle _skillRect;
    private Rectangle _traderRect;
    private Rectangle _goodsRect;
    private Rectangle _descriptionRect;
    private Rectangle _skillDropdownRect;
    private Rectangle _goodsDropdownRect;

    private string? _selectedProfessionId;
    private string _draftId = "";
    private string _draftName = "";
    private string _draftDescription = "";
    private string _draftPrimarySkill = "Trade";
    private bool _draftIsTrader;
    private readonly List<string> _draftTradeTags = new();

    private FocusField _focus;
    private bool _skillDropdownOpen;
    private bool _goodsDropdownOpen;
    private int _listScroll;
    private int _goodsScroll;
    private Keys? _heldDeleteKey;
    private long _nextDeleteRepeatAt;

    private const int RowH = 38;
    private const int FieldH = 26;
    private const int DropdownRowH = 30;
    private const int DeleteRepeatInitialDelayMs = 350;
    private const int DeleteRepeatIntervalMs = 32;

    public bool IsTyping => _focus != FocusField.None;

    public ProfessionEditorPanel(GraphicsDevice graphics, PrototypeManager prototypes)
    {
        _graphics = graphics;
        _prototypes = prototypes;
    }

    public void SyncSelection(ProfessionCatalog catalog)
    {
        if (!string.IsNullOrWhiteSpace(_selectedProfessionId) && catalog.Get(_selectedProfessionId) == null)
            StartNewProfession();
        else if (!string.IsNullOrWhiteSpace(_selectedProfessionId))
            LoadProfession(catalog.Get(_selectedProfessionId));
        else
        {
            var first = catalog.Professions.FirstOrDefault();
            if (first != null)
            {
                _selectedProfessionId = first.Id;
                LoadProfession(first);
                _focus = FocusField.None;
            }
            else
            {
                StartNewProfession();
            }
        }
    }

    public ProfessionEditorChange? Update(MouseState mouse, MouseState prev, KeyboardState keys, KeyboardState prevKeys, ProfessionCatalog catalog, Action<string> showMessage)
    {
        RebuildLayout();

        var scrollDelta = mouse.ScrollWheelValue - prev.ScrollWheelValue;
        if (scrollDelta != 0)
        {
            if (_goodsDropdownOpen && _goodsDropdownRect.Contains(mouse.Position))
                _goodsScroll = Math.Max(0, _goodsScroll - Math.Sign(scrollDelta));
            else if (_listRect.Contains(mouse.Position))
                _listScroll = Math.Max(0, _listScroll - Math.Sign(scrollDelta));
        }

        if (_focus != FocusField.None && IsPressed(keys, prevKeys, Keys.Enter))
            return SaveProfession(catalog, showMessage);

        if (mouse.LeftButton == ButtonState.Pressed && prev.LeftButton == ButtonState.Released)
        {
            if (!_bounds.Contains(mouse.Position))
            {
                ClearFocusAndDropdowns();
                return null;
            }

            if (TryHandleDropdownClick(mouse.Position))
                return null;

            if (TryHandleProfessionSelection(mouse.Position, catalog))
                return null;

            if (string.IsNullOrWhiteSpace(_selectedProfessionId))
            {
                ClearFocusAndDropdowns();
                return null;
            }

            _skillDropdownOpen = false;
            _goodsDropdownOpen = false;

            if (_idRect.Contains(mouse.Position))
                _focus = FocusField.Id;
            else if (_nameRect.Contains(mouse.Position))
                _focus = FocusField.Name;
            else if (_descriptionRect.Contains(mouse.Position))
                _focus = FocusField.Description;
            else if (_skillRect.Contains(mouse.Position))
            {
                _focus = FocusField.None;
                _skillDropdownOpen = true;
            }
            else if (_traderRect.Contains(mouse.Position))
            {
                _focus = FocusField.None;
                _draftIsTrader = !_draftIsTrader;
            }
            else if (_goodsRect.Contains(mouse.Position))
            {
                _focus = FocusField.None;
                _goodsDropdownOpen = true;
                _goodsScroll = 0;
            }
            else
            {
                _focus = FocusField.None;
            }
        }

        HandleTextInput(keys, prevKeys);
        return null;
    }

    public ProfessionEditorChange? CreateNew(ProfessionCatalog catalog, Action<string> showMessage)
    {
        var seedName = string.IsNullOrWhiteSpace(_draftName) ? "Profession" : _draftName.Trim();
        var id = GenerateUniqueProfessionId(catalog, seedName);
        var profession = new ProfessionDefinition
        {
            Id = id,
            Name = GenerateUniqueProfessionName(catalog, seedName),
            PrimarySkill = "Trade",
            IsTrader = false
        };

        catalog.Professions.Add(profession);
        catalog.Normalize();
        _selectedProfessionId = id;
        LoadProfession(catalog.Get(id));
        _focus = FocusField.Name;
        showMessage($"Created profession '{id}'");

        return new ProfessionEditorChange { RenamedToId = id };
    }

    public ProfessionEditorChange? SaveCurrent(ProfessionCatalog catalog, Action<string> showMessage)
        => SaveProfession(catalog, showMessage);

    public ProfessionEditorChange? DeleteCurrent(ProfessionCatalog catalog, Action<string> showMessage)
        => DeleteProfession(catalog, showMessage);

    public void Draw(SpriteBatch spriteBatch, ProfessionCatalog catalog)
    {
        RebuildLayout();

        EditorTheme.DrawPanel(spriteBatch, _bounds, EditorTheme.Bg, EditorTheme.Border);
        var header = new Rectangle(_bounds.X, _bounds.Y, _bounds.Width, 30);
        EditorTheme.FillRect(spriteBatch, header, EditorTheme.Panel);
        spriteBatch.Draw(EditorTheme.Pixel, new Rectangle(header.X, header.Y, 3, header.Height), EditorTheme.Success);
        EditorTheme.DrawText(spriteBatch, EditorTheme.Medium, "PROFESSION EDITOR",
            new Vector2(header.X + 12, header.Y + 8), EditorTheme.Text);

        DrawProfessionList(spriteBatch, catalog);
        DrawDetails(spriteBatch, catalog);

        if (_skillDropdownOpen)
            DrawSkillDropdown(spriteBatch);
        if (_goodsDropdownOpen)
            DrawGoodsDropdown(spriteBatch);
    }

    private void DrawProfessionList(SpriteBatch spriteBatch, ProfessionCatalog catalog)
    {
        EditorTheme.DrawPanel(spriteBatch, _listRect, EditorTheme.PanelAlt, EditorTheme.Border);
        EditorTheme.DrawText(spriteBatch, EditorTheme.Small, $"Professions: {catalog.Professions.Count}",
            new Vector2(_listRect.X + 10, _listRect.Y + 8), EditorTheme.TextDim);

        var y = _listRect.Y + 30;
        foreach (var profession in catalog.Professions.Skip(_listScroll))
        {
            if (y + RowH > _listRect.Bottom - 8)
                break;

            var row = new Rectangle(_listRect.X + 8, y, _listRect.Width - 16, RowH - 2);
            var selected = string.Equals(profession.Id, _selectedProfessionId, StringComparison.OrdinalIgnoreCase);
            EditorTheme.FillRect(spriteBatch, row, selected ? EditorTheme.Success : EditorTheme.BgDeep);
            EditorTheme.DrawBorder(spriteBatch, row, selected ? EditorTheme.BorderSoft : EditorTheme.Border);
            EditorTheme.DrawText(spriteBatch, EditorTheme.Small, Truncate(profession.Name, row.Width - 16),
                new Vector2(row.X + 8, row.Y + 5), selected ? Color.Black : EditorTheme.Text);

            var meta = $"{profession.Id}  |  {profession.PrimarySkill}";
            if (profession.IsTrader)
                meta += $"  |  tags:{profession.TradeTags.Count}";
            EditorTheme.DrawText(spriteBatch, EditorTheme.Tiny, Truncate(meta, row.Width - 16),
                new Vector2(row.X + 8, row.Y + 22), selected ? new Color(20, 45, 24) : EditorTheme.TextMuted);
            y += RowH;
        }
    }

    private void DrawDetails(SpriteBatch spriteBatch, ProfessionCatalog catalog)
    {
        EditorTheme.DrawPanel(spriteBatch, _detailRect, EditorTheme.PanelAlt, EditorTheme.Border);
        if (string.IsNullOrWhiteSpace(_selectedProfessionId))
        {
            DrawEmptyState(spriteBatch, catalog.Professions.Count == 0
                ? "Use Profession -> New Profession"
                : "Select a profession or create a new one");
            return;
        }

        DrawLabel(spriteBatch, "ID", _idRect);
        DrawField(spriteBatch, _idRect, _draftId, _focus == FocusField.Id);

        DrawLabel(spriteBatch, "Name", _nameRect);
        DrawField(spriteBatch, _nameRect, _draftName, _focus == FocusField.Name);

        DrawLabel(spriteBatch, "Primary Skill", _skillRect);
        DrawDropdownField(spriteBatch, _skillRect, _draftPrimarySkill, _skillDropdownOpen);

        DrawLabel(spriteBatch, "Trader", _traderRect);
        EditorTheme.DrawButton(spriteBatch, _traderRect, _draftIsTrader ? "Trades by Tags" : "No Trading",
            EditorTheme.Small, false, _draftIsTrader);

        DrawLabel(spriteBatch, "Trade Tags", _goodsRect);
        DrawGoodsField(spriteBatch, _goodsRect);

        DrawLabel(spriteBatch, "Description", _descriptionRect);
        DrawField(spriteBatch, _descriptionRect, _draftDescription, _focus == FocusField.Description);

        EditorTheme.DrawText(spriteBatch, EditorTheme.Tiny,
            "Profession definitions are global. Profession areas only store professionId.",
            new Vector2(_detailRect.X + 12, _detailRect.Bottom - 24), EditorTheme.TextMuted);
    }

    private void DrawSkillDropdown(SpriteBatch spriteBatch)
    {
        EditorTheme.DrawShadow(spriteBatch, _skillDropdownRect, 5);
        EditorTheme.DrawPanel(spriteBatch, _skillDropdownRect, EditorTheme.Panel, EditorTheme.AccentDim);

        var y = _skillDropdownRect.Y + 2;
        foreach (var skill in Enum.GetNames<SkillType>())
        {
            var row = new Rectangle(_skillDropdownRect.X + 2, y, _skillDropdownRect.Width - 4, DropdownRowH - 1);
            var selected = string.Equals(skill, _draftPrimarySkill, StringComparison.OrdinalIgnoreCase);
            EditorTheme.FillRect(spriteBatch, row, selected ? EditorTheme.PanelActive : EditorTheme.BgDeep);
            EditorTheme.DrawText(spriteBatch, EditorTheme.Small, skill,
                new Vector2(row.X + 8, row.Y + 7), selected ? EditorTheme.Text : EditorTheme.TextDim);
            y += DropdownRowH;
        }
    }

    private void DrawGoodsDropdown(SpriteBatch spriteBatch)
    {
        var options = BuildGoodsOptions();
        EditorTheme.DrawShadow(spriteBatch, _goodsDropdownRect, 5);
        EditorTheme.DrawPanel(spriteBatch, _goodsDropdownRect, EditorTheme.Panel, EditorTheme.AccentDim);

        var y = _goodsDropdownRect.Y + 2;
        foreach (var option in options.Skip(_goodsScroll))
        {
            if (y + DropdownRowH > _goodsDropdownRect.Bottom - 2)
                break;

            var row = new Rectangle(_goodsDropdownRect.X + 2, y, _goodsDropdownRect.Width - 4, DropdownRowH - 1);
            EditorTheme.FillRect(spriteBatch, row, option.Value.StartsWith("!remove:", StringComparison.Ordinal) ? EditorTheme.Panel : EditorTheme.BgDeep);
            if (option.Value.StartsWith("!remove:", StringComparison.Ordinal))
                spriteBatch.Draw(EditorTheme.Pixel, new Rectangle(row.X, row.Y, 3, row.Height), EditorTheme.Warning);
            EditorTheme.DrawText(spriteBatch, EditorTheme.Small, Truncate(option.Label, row.Width - 16),
                new Vector2(row.X + 8, row.Y + 5), EditorTheme.TextDim);
            EditorTheme.DrawText(spriteBatch, EditorTheme.Tiny, Truncate(option.Hint, row.Width - 16),
                new Vector2(row.X + 8, row.Y + 19), EditorTheme.TextMuted);
            y += DropdownRowH;
        }
    }

    private bool TryHandleDropdownClick(Point point)
    {
        if (_skillDropdownOpen)
        {
            if (_skillDropdownRect.Contains(point))
            {
                var index = (point.Y - _skillDropdownRect.Y - 2) / DropdownRowH;
                var skills = Enum.GetNames<SkillType>();
                if (index >= 0 && index < skills.Length)
                    _draftPrimarySkill = skills[index];
                _skillDropdownOpen = false;
                return true;
            }

            _skillDropdownOpen = false;
        }

        if (_goodsDropdownOpen)
        {
            if (_goodsDropdownRect.Contains(point))
            {
                var index = _goodsScroll + (point.Y - _goodsDropdownRect.Y - 2) / DropdownRowH;
                var options = BuildGoodsOptions();
                if (index >= 0 && index < options.Count)
                    ApplyGoodsOption(options[index].Value);
                _goodsDropdownOpen = false;
                return true;
            }

            _goodsDropdownOpen = false;
        }

        return false;
    }

    private bool TryHandleProfessionSelection(Point point, ProfessionCatalog catalog)
    {
        if (!_listRect.Contains(point))
            return false;

        var y = _listRect.Y + 30;
        foreach (var profession in catalog.Professions.Skip(_listScroll))
        {
            if (y + RowH > _listRect.Bottom - 8)
                break;

            var row = new Rectangle(_listRect.X + 8, y, _listRect.Width - 16, RowH - 2);
            if (row.Contains(point))
            {
                _selectedProfessionId = profession.Id;
                LoadProfession(profession);
                ClearFocusAndDropdowns();
                return true;
            }

            y += RowH;
        }

        return true;
    }

    private ProfessionEditorChange? SaveProfession(ProfessionCatalog catalog, Action<string> showMessage)
    {
        var id = MakeSafeId(_draftId);
        if (string.IsNullOrWhiteSpace(id))
        {
            showMessage("Profession id cannot be empty");
            return null;
        }

        var duplicate = catalog.Professions.FirstOrDefault(p =>
            string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(p.Id, _selectedProfessionId, StringComparison.OrdinalIgnoreCase));
        if (duplicate != null)
        {
            showMessage($"Profession id '{id}' already exists");
            return null;
        }

        var oldId = _selectedProfessionId;
        var profession = !string.IsNullOrWhiteSpace(oldId) ? catalog.Get(oldId) : null;
        if (profession == null)
        {
            profession = new ProfessionDefinition();
            catalog.Professions.Add(profession);
        }

        profession.Id = id;
        profession.Name = string.IsNullOrWhiteSpace(_draftName) ? id : _draftName.Trim();
        profession.Description = _draftDescription.Trim();
        profession.PrimarySkill = string.IsNullOrWhiteSpace(_draftPrimarySkill) ? "Trade" : _draftPrimarySkill.Trim();
        profession.IsTrader = _draftIsTrader;
        profession.TradeTags = _draftTradeTags.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        catalog.Normalize();

        _selectedProfessionId = id;
        LoadProfession(catalog.Get(id));
        showMessage(string.IsNullOrWhiteSpace(oldId) ? $"Created profession '{id}'" : $"Saved profession '{id}'");

        return new ProfessionEditorChange
        {
            RenamedFromId = string.Equals(oldId, id, StringComparison.OrdinalIgnoreCase) ? null : oldId,
            RenamedToId = id
        };
    }

    private ProfessionEditorChange? DeleteProfession(ProfessionCatalog catalog, Action<string> showMessage)
    {
        if (string.IsNullOrWhiteSpace(_selectedProfessionId))
        {
            showMessage("Select a profession to delete");
            return null;
        }

        var deletedId = _selectedProfessionId;
        catalog.Professions.RemoveAll(p => string.Equals(p.Id, deletedId, StringComparison.OrdinalIgnoreCase));
        catalog.Normalize();
        StartNewProfession();
        showMessage($"Deleted profession '{deletedId}'");

        return new ProfessionEditorChange { DeletedProfessionId = deletedId };
    }

    private void LoadProfession(ProfessionDefinition? profession)
    {
        if (profession == null)
        {
            StartNewProfession();
            return;
        }

        _draftId = profession.Id;
        _draftName = profession.Name;
        _draftDescription = profession.Description;
        _draftPrimarySkill = string.IsNullOrWhiteSpace(profession.PrimarySkill) ? "Trade" : profession.PrimarySkill;
        _draftIsTrader = profession.IsTrader;
        _draftTradeTags.Clear();
        _draftTradeTags.AddRange(profession.TradeTags);
    }

    private void StartNewProfession()
    {
        _selectedProfessionId = null;
        _draftId = "";
        _draftName = "";
        _draftDescription = "";
        _draftPrimarySkill = "Trade";
        _draftIsTrader = false;
        _draftTradeTags.Clear();
        _focus = FocusField.Id;
        _skillDropdownOpen = false;
        _goodsDropdownOpen = false;
        ResetDeleteRepeat();
    }

    private List<(string Value, string Label, string Hint)> BuildGoodsOptions()
    {
        var options = new List<(string Value, string Label, string Hint)>
        {
            ("!clear", "Clear tags", "remove all trade tags")
        };

        foreach (var tag in _draftTradeTags.OrderBy(id => id, StringComparer.OrdinalIgnoreCase))
            options.Add(($"!remove:{tag}", $"Remove {tag}", "currently selected"));

        options.AddRange(BuildKnownTradeTags()
            .Where(option => _draftTradeTags.All(tag => !string.Equals(tag, option.Tag, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(option => option.Tag, StringComparer.OrdinalIgnoreCase)
            .Select(option => (option.Tag, option.Tag, $"{option.Count} item prototypes")));
        return options;
    }

    private List<(string Tag, int Count)> BuildKnownTradeTags()
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var proto in _prototypes.GetAllEntities())
        {
            if (proto.Components == null
                || proto.Components["item"] is not JsonObject itemObject
                || itemObject["tags"] is not JsonArray tags)
                continue;

            foreach (var tagNode in tags)
            {
                var tag = tagNode?.GetValue<string>()?.Trim();
                if (string.IsNullOrWhiteSpace(tag))
                    continue;

                counts[tag] = counts.TryGetValue(tag, out var count) ? count + 1 : 1;
            }
        }

        return counts.Select(pair => (pair.Key, pair.Value)).ToList();
    }

    private void ApplyGoodsOption(string value)
    {
        if (value == "!clear")
        {
            _draftTradeTags.Clear();
            return;
        }

        const string removePrefix = "!remove:";
        if (value.StartsWith(removePrefix, StringComparison.OrdinalIgnoreCase))
        {
            var id = value[removePrefix.Length..];
            _draftTradeTags.RemoveAll(item => string.Equals(item, id, StringComparison.OrdinalIgnoreCase));
            return;
        }

        if (_draftTradeTags.All(item => !string.Equals(item, value, StringComparison.OrdinalIgnoreCase)))
            _draftTradeTags.Add(value);
    }

    private void HandleTextInput(KeyboardState keys, KeyboardState prevKeys)
    {
        if (_focus == FocusField.None)
        {
            ResetDeleteRepeat();
            return;
        }

        if (keys.IsKeyDown(Keys.LeftControl) || keys.IsKeyDown(Keys.RightControl))
            return;

        var deleteHandled = false;
        foreach (var key in keys.GetPressedKeys().OrderBy(static key => key))
        {
            if (key == Keys.Escape && prevKeys.IsKeyUp(key))
            {
                _focus = FocusField.None;
                ResetDeleteRepeat();
                return;
            }

            if (key == Keys.Tab && prevKeys.IsKeyUp(key))
            {
                _focus = _focus switch
                {
                    FocusField.Id => FocusField.Name,
                    FocusField.Name => FocusField.Description,
                    _ => FocusField.Id
                };
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

            var ch = KeyToChar(key, keys.IsKeyDown(Keys.LeftShift) || keys.IsKeyDown(Keys.RightShift),
                _focus is FocusField.Name or FocusField.Description);
            if (ch == '\0')
                continue;

            if (_focus == FocusField.Id)
                _draftId += ch;
            else if (_focus == FocusField.Name)
                _draftName += ch;
            else if (_focus == FocusField.Description)
                _draftDescription += ch;
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
        else if (_focus == FocusField.Description && _draftDescription.Length > 0)
            _draftDescription = _draftDescription[..^1];
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

    private void ClearFocusAndDropdowns()
    {
        _focus = FocusField.None;
        _skillDropdownOpen = false;
        _goodsDropdownOpen = false;
    }

    private void RebuildLayout()
    {
        var viewport = _graphics.Viewport;
        _bounds = new Rectangle(12, 68, viewport.Width - 24, viewport.Height - 102);
        _listRect = new Rectangle(_bounds.X + 12, _bounds.Y + 42, 300, _bounds.Height - 54);
        _detailRect = new Rectangle(_listRect.Right + 12, _bounds.Y + 42, _bounds.Right - _listRect.Right - 24, _bounds.Height - 54);

        var x = _detailRect.X + 12;
        var y = _detailRect.Y + 28;
        var w = _detailRect.Width - 24;
        var half = (w - 8) / 2;
        _idRect = new Rectangle(x, y, half, FieldH);
        _nameRect = new Rectangle(x + half + 8, y, half, FieldH);
        y += 48;
        _skillRect = new Rectangle(x, y, half, FieldH);
        _traderRect = new Rectangle(x + half + 8, y, half, FieldH);
        y += 48;
        _goodsRect = new Rectangle(x, y, w, FieldH);
        y += 48;
        _descriptionRect = new Rectangle(x, y, w, FieldH);

        var skillRows = Enum.GetNames<SkillType>().Length;
        _skillDropdownRect = new Rectangle(_skillRect.X, _skillRect.Bottom + 2, _skillRect.Width, skillRows * DropdownRowH + 4);
        _goodsDropdownRect = new Rectangle(_goodsRect.X, _goodsRect.Bottom + 2, _goodsRect.Width, Math.Min(10, Math.Max(1, BuildGoodsOptions().Count)) * DropdownRowH + 4);
    }

    private void DrawEmptyState(SpriteBatch spriteBatch, string text)
    {
        var box = new Rectangle(_detailRect.X + 24, _detailRect.Y + 24, _detailRect.Width - 48, 100);
        EditorTheme.FillRect(spriteBatch, box, EditorTheme.BgDeep);
        EditorTheme.DrawBorder(spriteBatch, box, EditorTheme.BorderSoft);
        spriteBatch.Draw(EditorTheme.Pixel, new Rectangle(box.X, box.Y, 3, box.Height), EditorTheme.Success);
        EditorTheme.DrawText(spriteBatch, EditorTheme.Title, "No profession selected",
            new Vector2(box.X + 16, box.Y + 22), EditorTheme.Text);
        EditorTheme.DrawText(spriteBatch, EditorTheme.Body, text,
            new Vector2(box.X + 16, box.Y + 56), EditorTheme.TextDim);
    }

    private static void DrawLabel(SpriteBatch spriteBatch, string label, Rectangle fieldRect)
    {
        EditorTheme.DrawText(spriteBatch, EditorTheme.Small, label,
            new Vector2(fieldRect.X, fieldRect.Y - 16), EditorTheme.TextDim);
    }

    private static void DrawField(SpriteBatch spriteBatch, Rectangle rect, string text, bool focused)
    {
        EditorTheme.FillRect(spriteBatch, rect, focused ? EditorTheme.BgDeep : EditorTheme.Bg);
        EditorTheme.DrawBorder(spriteBatch, rect, focused ? EditorTheme.Success : EditorTheme.Border);
        EditorTheme.DrawText(spriteBatch, EditorTheme.Body, Truncate(text + (focused ? "|" : ""), rect.Width - 12),
            new Vector2(rect.X + 6, rect.Y + 4), EditorTheme.Text);
    }

    private static void DrawDropdownField(SpriteBatch spriteBatch, Rectangle rect, string text, bool open)
    {
        EditorTheme.FillRect(spriteBatch, rect, open ? EditorTheme.BgDeep : EditorTheme.Bg);
        EditorTheme.DrawBorder(spriteBatch, rect, open ? EditorTheme.Accent : EditorTheme.Border);
        EditorTheme.DrawText(spriteBatch, EditorTheme.Body, Truncate(text, rect.Width - 30),
            new Vector2(rect.X + 6, rect.Y + 4), EditorTheme.Text);
        EditorTheme.DrawText(spriteBatch, EditorTheme.Small, "v", new Vector2(rect.Right - 16, rect.Y + 4), EditorTheme.TextMuted);
    }

    private void DrawGoodsField(SpriteBatch spriteBatch, Rectangle rect)
    {
        EditorTheme.FillRect(spriteBatch, rect, _goodsDropdownOpen ? EditorTheme.BgDeep : EditorTheme.Bg);
        EditorTheme.DrawBorder(spriteBatch, rect, _goodsDropdownOpen ? EditorTheme.Accent : EditorTheme.Border);

        if (_draftTradeTags.Count == 0)
        {
            EditorTheme.DrawText(spriteBatch, EditorTheme.Body, _draftIsTrader ? "Add trade tags..." : "No tags",
                new Vector2(rect.X + 6, rect.Y + 4), EditorTheme.TextMuted);
        }
        else
        {
            var x = rect.X + 6;
            foreach (var item in _draftTradeTags.Take(5))
            {
                var label = Truncate(item, 96);
                var width = Math.Min(118, (int)EditorTheme.Small.MeasureString(label).X + 12);
                if (x + width > rect.Right - 28)
                    break;

                var chip = new Rectangle(x, rect.Y + 4, width, rect.Height - 8);
                EditorTheme.FillRect(spriteBatch, chip, EditorTheme.PanelActive);
                EditorTheme.DrawBorder(spriteBatch, chip, EditorTheme.BorderSoft);
                EditorTheme.DrawText(spriteBatch, EditorTheme.Small, label,
                    new Vector2(chip.X + 6, chip.Y + 5), EditorTheme.Text);
                x += width + 5;
            }
        }

        EditorTheme.DrawText(spriteBatch, EditorTheme.Small, "+", new Vector2(rect.Right - 16, rect.Y + 4), EditorTheme.TextMuted);
    }

    private static string GenerateUniqueProfessionId(ProfessionCatalog catalog, string seedName)
    {
        var slug = MakeSafeId(seedName);
        if (string.IsNullOrWhiteSpace(slug))
            slug = "profession";

        var candidate = slug;
        var index = 2;
        while (catalog.Get(candidate) != null)
        {
            candidate = $"{slug}_{index}";
            index++;
        }

        return candidate;
    }

    private static string GenerateUniqueProfessionName(ProfessionCatalog catalog, string seedName)
    {
        var baseName = string.IsNullOrWhiteSpace(seedName) ? "Profession" : seedName.Trim();
        var candidate = baseName;
        var index = 2;
        while (catalog.Professions.Any(p => string.Equals(p.Name, candidate, StringComparison.OrdinalIgnoreCase)))
        {
            candidate = $"{baseName} {index}";
            index++;
        }

        return candidate;
    }

    private static string MakeSafeId(string value)
    {
        var chars = value.Trim().Select(ch => char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '_').ToArray();
        var id = new string(chars).Trim('_');
        while (id.Contains("__", StringComparison.Ordinal))
            id = id.Replace("__", "_", StringComparison.Ordinal);
        return id;
    }

    private static string Truncate(string value, int maxWidth)
    {
        if (EditorTheme.Small.MeasureString(value).X <= maxWidth)
            return value;

        while (value.Length > 1 && EditorTheme.Small.MeasureString(value + "...").X > maxWidth)
            value = value[..^1];
        return value + "...";
    }

    private static char KeyToChar(Keys key, bool shift, bool allowSpaces)
    {
        if (key is >= Keys.A and <= Keys.Z)
            return (char)((shift ? 'A' : 'a') + (key - Keys.A));
        if (key is >= Keys.D0 and <= Keys.D9)
            return (char)('0' + (key - Keys.D0));
        if (key is >= Keys.NumPad0 and <= Keys.NumPad9)
            return (char)('0' + (key - Keys.NumPad0));

        return key switch
        {
            Keys.Space when allowSpaces => ' ',
            Keys.OemMinus => shift ? '_' : '_',
            Keys.OemPeriod => '.',
            Keys.OemComma when allowSpaces => ',',
            Keys.OemSemicolon when allowSpaces => shift ? ':' : ';',
            Keys.OemQuestion when allowSpaces => '/',
            _ => '\0'
        };
    }

    private static bool IsPressed(KeyboardState keys, KeyboardState prevKeys, Keys key)
        => keys.IsKeyDown(key) && prevKeys.IsKeyUp(key);
}
