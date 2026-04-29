using System;
using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace MTEditor.UI;

public class SaveMapDialog
{
    private readonly GraphicsDevice _graphics;

    public bool IsOpen { get; private set; } = false;

    private string _mapId = "";
    private string _mapName = "";
    private enum Focus { Id, Name }
    private Focus _focus = Focus.Id;
    private Action<string, string>? _onSave;

    public SaveMapDialog(GraphicsDevice graphics)
    {
        _graphics = graphics;
    }

    public void Open(string currentId, string currentName, Action<string, string> onSave)
    {
        _mapId = currentId;
        _mapName = currentName;
        _onSave = onSave;
        _focus = Focus.Id;
        IsOpen = true;
    }

    public void Close() => IsOpen = false;

    public void Update(MouseState mouse, MouseState prev, KeyboardState keys, KeyboardState prevKeys)
    {
        if (!IsOpen) return;

        if (mouse.LeftButton == ButtonState.Pressed && prev.LeftButton == ButtonState.Released)
        {
            if (GetIdRect().Contains(mouse.X, mouse.Y)) _focus = Focus.Id;
            else if (GetNameRect().Contains(mouse.X, mouse.Y)) _focus = Focus.Name;
            else if (!GetDialogRect().Contains(mouse.X, mouse.Y)) { IsOpen = false; return; }
        }

        foreach (var key in keys.GetPressedKeys())
        {
            if (prevKeys.IsKeyDown(key)) continue;

            if (key == Keys.Escape) { IsOpen = false; return; }
            if (key == Keys.Tab) _focus = _focus == Focus.Id ? Focus.Name : Focus.Id;
            if (key == Keys.Enter)
            {
                if (string.IsNullOrWhiteSpace(_mapId)) return;
                _onSave?.Invoke(_mapId.Trim(), _mapName.Trim());
                IsOpen = false;
                return;
            }
            if (key == Keys.Back)
            {
                if (_focus == Focus.Id && _mapId.Length > 0) _mapId = _mapId[..^1];
                if (_focus == Focus.Name && _mapName.Length > 0) _mapName = _mapName[..^1];
            }
            else
            {
                var ch = KeyToChar(key, keys.IsKeyDown(Keys.LeftShift), _focus == Focus.Name);
                if (ch != '\0')
                {
                    if (_focus == Focus.Id) _mapId += ch;
                    else _mapName += ch;
                }
            }
        }
    }

    public void Draw(SpriteBatch sb)
    {
        if (!IsOpen) return;

        var vp = _graphics.Viewport;
        EditorTheme.FillRect(sb, new Rectangle(0, 0, vp.Width, vp.Height), Color.Black * 0.55f);

        var dialog = GetDialogRect();
        EditorTheme.DrawShadow(sb, dialog, 8);
        EditorTheme.FillRect(sb, dialog, EditorTheme.Bg);
        EditorTheme.DrawBorder(sb, dialog, EditorTheme.Border);

        // Title
        var title = new Rectangle(dialog.X, dialog.Y, dialog.Width, 26);
        EditorTheme.FillRect(sb, title, EditorTheme.Panel);
        sb.Draw(EditorTheme.Pixel, new Rectangle(title.X, title.Bottom - 1, title.Width, 1), EditorTheme.Border);
        sb.Draw(EditorTheme.Pixel, new Rectangle(title.X, title.Y, 3, title.Height), EditorTheme.Accent);
        EditorTheme.DrawText(sb, EditorTheme.Medium, "SAVE MAP",
            new Vector2(title.X + 12, title.Y + (title.Height - EditorTheme.Medium.MeasureString("SAVE MAP").Y) / 2f - 1),
            EditorTheme.Text);

        var hint = "Tab — next    Enter — save    Esc — cancel";
        var hintSize = EditorTheme.Tiny.MeasureString(hint);
        EditorTheme.DrawText(sb, EditorTheme.Tiny, hint,
            new Vector2(title.Right - hintSize.X - 10, title.Y + (title.Height - hintSize.Y) / 2f - 1),
            EditorTheme.TextMuted);

        // Id field
        EditorTheme.DrawText(sb, EditorTheme.Small, "Map ID (filename, no spaces)",
            new Vector2(dialog.X + 14, dialog.Y + 38), EditorTheme.TextDim);
        DrawField(sb, GetIdRect(), _mapId, _focus == Focus.Id);

        // Name field
        EditorTheme.DrawText(sb, EditorTheme.Small, "Map Name",
            new Vector2(dialog.X + 14, dialog.Y + 92), EditorTheme.TextDim);
        DrawField(sb, GetNameRect(), _mapName, _focus == Focus.Name);

        if (string.IsNullOrWhiteSpace(_mapId))
        {
            EditorTheme.DrawText(sb, EditorTheme.Small, "ID cannot be empty",
                new Vector2(dialog.X + 14, dialog.Bottom - 26), EditorTheme.Error);
        }
    }

    private static void DrawField(SpriteBatch sb, Rectangle rect, string text, bool focused)
    {
        EditorTheme.FillRect(sb, rect, focused ? EditorTheme.BgDeep : EditorTheme.Panel);
        EditorTheme.DrawBorder(sb, rect, focused ? EditorTheme.Accent : EditorTheme.Border);
        EditorTheme.DrawText(sb, EditorTheme.Body, text + (focused ? "│" : ""),
            new Vector2(rect.X + 6, rect.Y + (rect.Height - EditorTheme.Body.MeasureString("Ay").Y) / 2f - 1),
            EditorTheme.Text);
    }

    private Rectangle GetDialogRect()
    {
        var vp = _graphics.Viewport;
        return new Rectangle(vp.Width / 2 - 210, vp.Height / 2 - 100, 420, 180);
    }

    private Rectangle GetIdRect()
    {
        var d = GetDialogRect();
        return new Rectangle(d.X + 14, d.Y + 56, d.Width - 28, 24);
    }

    private Rectangle GetNameRect()
    {
        var d = GetDialogRect();
        return new Rectangle(d.X + 14, d.Y + 110, d.Width - 28, 24);
    }

    private static char KeyToChar(Keys key, bool shift, bool allowSpaces)
    {
        if (key >= Keys.A && key <= Keys.Z)
            return shift ? (char)('A' + (key - Keys.A)) : (char)('a' + (key - Keys.A));
        if (key >= Keys.D0 && key <= Keys.D9)
            return (char)('0' + (key - Keys.D0));
        return key switch
        {
            Keys.OemMinus => '_',
            Keys.OemPeriod => '.',
            Keys.Space when allowSpaces => ' ',
            _ => '\0'
        };
    }
}
