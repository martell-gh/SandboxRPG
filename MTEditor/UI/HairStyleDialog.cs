#nullable enable
using System;
using System.Collections.Generic;
using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace MTEditor.UI;

public sealed class HairStyleDialog
{
    public readonly record struct Draft(string Id, string Name, string Gender, string Sprite, bool IsEdit);

    private readonly GraphicsDevice _graphics;

    public bool IsOpen { get; private set; }

    private string _id = "";
    private string _name = "";
    private string _gender = "Unisex";
    private string _sprite = "sprite.png";
    private bool _isEdit;
    private string _error = "";
    private Func<Draft, (bool Ok, string Message)>? _onSave;
    private Func<IReadOnlyList<string>>? _spriteSuggestions;

    private enum Focus { None, Id, Name, Sprite }
    private Focus _focus = Focus.Id;

    private Rectangle _dialog;
    private Rectangle _idRect;
    private Rectangle _nameRect;
    private Rectangle _spriteRect;
    private Rectangle _genderUnisexRect;
    private Rectangle _genderMaleRect;
    private Rectangle _genderFemaleRect;
    private Rectangle _saveRect;
    private Rectangle _cancelRect;
    private readonly List<(Rectangle Rect, string Sprite)> _spriteSuggestionRects = new();
    private bool _spriteDropdownOpen;

    public bool IsTyping => IsOpen && _focus is Focus.Id or Focus.Name or Focus.Sprite;

    public HairStyleDialog(GraphicsDevice graphics)
    {
        _graphics = graphics;
    }

    public void Open(
        string id,
        string name,
        string gender,
        string sprite,
        bool isEdit,
        Func<IReadOnlyList<string>> spriteSuggestions,
        Func<Draft, (bool Ok, string Message)> onSave)
    {
        _id = id ?? "";
        _name = string.IsNullOrWhiteSpace(name) ? id ?? "" : name;
        _gender = NormalizeGender(gender);
        _sprite = string.IsNullOrWhiteSpace(sprite) ? "sprite.png" : sprite;
        _isEdit = isEdit;
        _error = "";
        _focus = Focus.Id;
        _spriteDropdownOpen = false;
        _spriteSuggestions = spriteSuggestions;
        _onSave = onSave;
        IsOpen = true;
    }

    public void Close()
    {
        IsOpen = false;
        _spriteDropdownOpen = false;
        _onSave = null;
        _spriteSuggestions = null;
    }

    public void Update(MouseState mouse, MouseState prev, KeyboardState keys, KeyboardState prevKeys)
    {
        if (!IsOpen)
            return;

        Layout();

        var clicked = mouse.LeftButton == ButtonState.Pressed && prev.LeftButton == ButtonState.Released;
        if (clicked)
            HandleClick(mouse.Position);

        HandleKeyboard(keys, prevKeys);
    }

    public void Draw(SpriteBatch sb)
    {
        if (!IsOpen)
            return;

        Layout();

        var vp = _graphics.Viewport;
        EditorTheme.FillRect(sb, new Rectangle(0, 0, vp.Width, vp.Height), Color.Black * 0.55f);

        EditorTheme.DrawShadow(sb, _dialog, 8);
        EditorTheme.FillRect(sb, _dialog, EditorTheme.Bg);
        EditorTheme.DrawBorder(sb, _dialog, EditorTheme.Border);

        var title = new Rectangle(_dialog.X, _dialog.Y, _dialog.Width, 26);
        EditorTheme.FillRect(sb, title, EditorTheme.Panel);
        sb.Draw(EditorTheme.Pixel, new Rectangle(title.X, title.Bottom - 1, title.Width, 1), EditorTheme.Border);
        sb.Draw(EditorTheme.Pixel, new Rectangle(title.X, title.Y, 3, title.Height), EditorTheme.Success);
        var heading = _isEdit ? "EDIT HAIR STYLE" : "NEW HAIR STYLE";
        EditorTheme.DrawText(sb, EditorTheme.Medium, heading,
            new Vector2(title.X + 12, title.Y + (title.Height - EditorTheme.Medium.MeasureString(heading).Y) / 2f - 1),
            EditorTheme.Text);

        var hint = "Tab — next    Enter — save    Esc — cancel";
        var hintSize = EditorTheme.Tiny.MeasureString(hint);
        EditorTheme.DrawText(sb, EditorTheme.Tiny, hint,
            new Vector2(title.Right - hintSize.X - 10, title.Y + (title.Height - hintSize.Y) / 2f - 1),
            EditorTheme.TextMuted);

        DrawLabeledField(sb, "ID (folder & prototype id, lowercase)", _idRect, _id, _focus == Focus.Id, _isEdit);
        DrawLabeledField(sb, "Display name", _nameRect, _name, _focus == Focus.Name, false);

        EditorTheme.DrawText(sb, EditorTheme.Small, "Gender",
            new Vector2(_genderUnisexRect.X, _genderUnisexRect.Y - 14), EditorTheme.TextDim);
        DrawGenderButton(sb, _genderUnisexRect, "Unisex", _gender == "Unisex");
        DrawGenderButton(sb, _genderMaleRect, "Male", _gender == "Male");
        DrawGenderButton(sb, _genderFemaleRect, "Female", _gender == "Female");

        DrawLabeledField(sb, "Sprite filename", _spriteRect, _sprite, _focus == Focus.Sprite, false);

        if (_spriteDropdownOpen && _spriteSuggestionRects.Count > 0)
            DrawSpriteDropdown(sb);

        if (!string.IsNullOrEmpty(_error))
        {
            EditorTheme.DrawText(sb, EditorTheme.Small, _error,
                new Vector2(_dialog.X + 14, _saveRect.Y - 18), EditorTheme.Error);
        }

        EditorTheme.DrawButton(sb, _saveRect, _isEdit ? "Save changes" : "Create", EditorTheme.Body, false, true);
        EditorTheme.DrawButton(sb, _cancelRect, "Cancel", EditorTheme.Body, false, false);
    }

    private void HandleClick(Point point)
    {
        if (_spriteDropdownOpen)
        {
            foreach (var (rect, sprite) in _spriteSuggestionRects)
            {
                if (rect.Contains(point))
                {
                    _sprite = sprite;
                    _focus = Focus.Sprite;
                    _spriteDropdownOpen = false;
                    return;
                }
            }
            _spriteDropdownOpen = false;
        }

        if (_idRect.Contains(point)) { _focus = _isEdit ? Focus.Name : Focus.Id; return; }
        if (_nameRect.Contains(point)) { _focus = Focus.Name; return; }
        if (_spriteRect.Contains(point))
        {
            _focus = Focus.Sprite;
            _spriteDropdownOpen = (_spriteSuggestions?.Invoke().Count ?? 0) > 0;
            return;
        }
        if (_genderUnisexRect.Contains(point)) { _gender = "Unisex"; return; }
        if (_genderMaleRect.Contains(point)) { _gender = "Male"; return; }
        if (_genderFemaleRect.Contains(point)) { _gender = "Female"; return; }
        if (_saveRect.Contains(point)) { Submit(); return; }
        if (_cancelRect.Contains(point)) { Close(); return; }
        if (!_dialog.Contains(point)) { Close(); return; }
        _focus = Focus.None;
    }

    private void HandleKeyboard(KeyboardState keys, KeyboardState prevKeys)
    {
        foreach (var key in keys.GetPressedKeys())
        {
            if (prevKeys.IsKeyDown(key))
                continue;

            if (key == Keys.Escape) { Close(); return; }
            if (key == Keys.Enter) { Submit(); return; }
            if (key == Keys.Tab)
            {
                _focus = _focus switch
                {
                    Focus.Id => Focus.Name,
                    Focus.Name => Focus.Sprite,
                    Focus.Sprite => _isEdit ? Focus.Name : Focus.Id,
                    _ => Focus.Id
                };
                _spriteDropdownOpen = false;
                continue;
            }

            if (_focus == Focus.None)
                continue;

            if (key == Keys.Back)
            {
                ApplyBackspace();
                continue;
            }

            var ch = KeyToChar(key, keys.IsKeyDown(Keys.LeftShift) || keys.IsKeyDown(Keys.RightShift));
            if (ch == '\0')
                continue;

            switch (_focus)
            {
                case Focus.Id when !_isEdit && IsIdChar(ch):
                    _id += ch;
                    break;
                case Focus.Name when !char.IsControl(ch):
                    _name += ch;
                    break;
                case Focus.Sprite when IsSpriteChar(ch):
                    _sprite += ch;
                    _spriteDropdownOpen = false;
                    break;
            }
        }
    }

    private void ApplyBackspace()
    {
        switch (_focus)
        {
            case Focus.Id when !_isEdit && _id.Length > 0: _id = _id[..^1]; break;
            case Focus.Name when _name.Length > 0: _name = _name[..^1]; break;
            case Focus.Sprite when _sprite.Length > 0: _sprite = _sprite[..^1]; break;
        }
    }

    private void Submit()
    {
        if (_onSave == null)
        {
            Close();
            return;
        }

        var draft = new Draft(_id.Trim(), _name.Trim(), NormalizeGender(_gender), _sprite.Trim(), _isEdit);
        var (ok, message) = _onSave(draft);
        if (ok)
            Close();
        else
            _error = string.IsNullOrWhiteSpace(message) ? "Could not save hair style" : message;
    }

    private void Layout()
    {
        var vp = _graphics.Viewport;
        const int width = 460;
        const int height = 290;
        _dialog = new Rectangle((vp.Width - width) / 2, (vp.Height - height) / 2, width, height);

        var x = _dialog.X + 14;
        var w = _dialog.Width - 28;

        _idRect = new Rectangle(x, _dialog.Y + 56, w, 24);
        _nameRect = new Rectangle(x, _dialog.Y + 110, w, 24);

        var genderTop = _dialog.Y + 156;
        var btnW = (w - 8) / 3;
        _genderUnisexRect = new Rectangle(x, genderTop, btnW, 22);
        _genderMaleRect = new Rectangle(x + btnW + 4, genderTop, btnW, 22);
        _genderFemaleRect = new Rectangle(x + (btnW + 4) * 2, genderTop, w - (btnW + 4) * 2, 22);

        _spriteRect = new Rectangle(x, _dialog.Y + 210, w, 24);

        const int btnRowW = 130;
        _cancelRect = new Rectangle(_dialog.Right - 14 - btnRowW, _dialog.Bottom - 38, btnRowW, 26);
        _saveRect = new Rectangle(_cancelRect.X - 8 - btnRowW, _cancelRect.Y, btnRowW, 26);

        _spriteSuggestionRects.Clear();
        if (_spriteDropdownOpen && _spriteSuggestions != null)
        {
            var suggestions = _spriteSuggestions();
            var py = _spriteRect.Bottom + 2;
            foreach (var s in suggestions)
            {
                _spriteSuggestionRects.Add((new Rectangle(_spriteRect.X, py, _spriteRect.Width, 22), s));
                py += 22;
                if (py + 22 > _dialog.Bottom - 50)
                    break;
            }
        }
    }

    private static void DrawLabeledField(SpriteBatch sb, string label, Rectangle rect, string text, bool focused, bool disabled)
    {
        EditorTheme.DrawText(sb, EditorTheme.Small, label,
            new Vector2(rect.X, rect.Y - 14), EditorTheme.TextDim);

        var fill = disabled ? EditorTheme.Panel : (focused ? EditorTheme.BgDeep : EditorTheme.Panel);
        var border = focused && !disabled ? EditorTheme.Accent : EditorTheme.Border;
        EditorTheme.FillRect(sb, rect, fill);
        EditorTheme.DrawBorder(sb, rect, border);
        var color = disabled ? EditorTheme.TextDisabled : EditorTheme.Text;
        var caret = focused && !disabled ? "│" : "";
        EditorTheme.DrawText(sb, EditorTheme.Body, text + caret,
            new Vector2(rect.X + 6, rect.Y + (rect.Height - EditorTheme.Body.MeasureString("Ay").Y) / 2f - 1),
            color);
    }

    private static void DrawGenderButton(SpriteBatch sb, Rectangle rect, string label, bool active)
        => EditorTheme.DrawButton(sb, rect, label, EditorTheme.Small, false, active);

    private void DrawSpriteDropdown(SpriteBatch sb)
    {
        var first = _spriteSuggestionRects[0].Rect;
        var last = _spriteSuggestionRects[^1].Rect;
        var bg = new Rectangle(first.X, first.Y, first.Width, last.Bottom - first.Y);
        EditorTheme.DrawShadow(sb, bg, 6);
        EditorTheme.FillRect(sb, bg, EditorTheme.Panel);
        EditorTheme.DrawBorder(sb, bg, EditorTheme.BorderSoft);
        foreach (var (rect, sprite) in _spriteSuggestionRects)
        {
            EditorTheme.DrawText(sb, EditorTheme.Small, sprite,
                new Vector2(rect.X + 6, rect.Y + 4), EditorTheme.Text);
        }
    }

    private static string NormalizeGender(string value)
        => string.Equals(value, "Male", StringComparison.OrdinalIgnoreCase) ? "Male"
            : string.Equals(value, "Female", StringComparison.OrdinalIgnoreCase) ? "Female"
            : "Unisex";

    private static bool IsIdChar(char ch)
        => char.IsLetterOrDigit(ch) || ch is '_' or '-';

    private static bool IsSpriteChar(char ch)
        => char.IsLetterOrDigit(ch) || ch is '_' or '-' or '.' or '/';

    private static char KeyToChar(Keys key, bool shift)
    {
        if (key is >= Keys.A and <= Keys.Z)
            return (char)((shift ? 'A' : 'a') + (key - Keys.A));
        if (key is >= Keys.D0 and <= Keys.D9)
            return (char)('0' + (key - Keys.D0));
        return key switch
        {
            Keys.OemMinus => shift ? '_' : '-',
            Keys.OemPeriod => '.',
            Keys.OemQuestion => '/',
            Keys.Space => ' ',
            _ => '\0'
        };
    }
}
