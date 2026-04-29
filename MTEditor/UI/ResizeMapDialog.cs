using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace MTEditor.UI;

public class ResizeMapDialog
{
    private readonly GraphicsDevice _graphics;

    private enum Focus
    {
        Width,
        Height
    }

    private Focus _focus = Focus.Width;
    private string _widthText = "";
    private string _heightText = "";
    private Action<int, int>? _onApply;

    public bool IsOpen { get; private set; }

    public ResizeMapDialog(GraphicsDevice graphics)
    {
        _graphics = graphics;
    }

    public void Open(int currentWidth, int currentHeight, Action<int, int> onApply)
    {
        _widthText = currentWidth.ToString();
        _heightText = currentHeight.ToString();
        _focus = Focus.Width;
        _onApply = onApply;
        IsOpen = true;
    }

    public void Close() => IsOpen = false;

    public void Update(MouseState mouse, MouseState prev, KeyboardState keys, KeyboardState prevKeys)
    {
        if (!IsOpen)
            return;

        if (mouse.LeftButton == ButtonState.Pressed && prev.LeftButton == ButtonState.Released)
        {
            if (GetWidthRect().Contains(mouse.X, mouse.Y))
                _focus = Focus.Width;
            else if (GetHeightRect().Contains(mouse.X, mouse.Y))
                _focus = Focus.Height;
            else if (!GetDialogRect().Contains(mouse.X, mouse.Y))
            {
                IsOpen = false;
                return;
            }
        }

        foreach (var key in keys.GetPressedKeys())
        {
            if (prevKeys.IsKeyDown(key))
                continue;

            if (key == Keys.Escape)
            {
                IsOpen = false;
                return;
            }

            if (key == Keys.Tab)
            {
                _focus = _focus == Focus.Width ? Focus.Height : Focus.Width;
                continue;
            }

            if (key == Keys.Enter)
            {
                if (!TryParseDimensions(out var width, out var height))
                    return;

                _onApply?.Invoke(width, height);
                IsOpen = false;
                return;
            }

            if (key == Keys.Back)
            {
                ref var target = ref GetFocusedText();
                if (target.Length > 0)
                    target = target[..^1];
                continue;
            }

            if (key >= Keys.D0 && key <= Keys.D9)
            {
                GetFocusedText() += (char)('0' + (key - Keys.D0));
                continue;
            }

            if (key >= Keys.NumPad0 && key <= Keys.NumPad9)
                GetFocusedText() += (char)('0' + (key - Keys.NumPad0));
        }
    }

    public void Draw(SpriteBatch sb)
    {
        if (!IsOpen)
            return;

        var vp = _graphics.Viewport;
        EditorTheme.FillRect(sb, new Rectangle(0, 0, vp.Width, vp.Height), Color.Black * 0.55f);

        var dialog = GetDialogRect();
        EditorTheme.DrawShadow(sb, dialog, 8);
        EditorTheme.FillRect(sb, dialog, EditorTheme.Bg);
        EditorTheme.DrawBorder(sb, dialog, EditorTheme.Border);

        var titleRect = new Rectangle(dialog.X, dialog.Y, dialog.Width, 26);
        EditorTheme.FillRect(sb, titleRect, EditorTheme.Panel);
        sb.Draw(EditorTheme.Pixel, new Rectangle(titleRect.X, titleRect.Bottom - 1, titleRect.Width, 1), EditorTheme.Border);
        sb.Draw(EditorTheme.Pixel, new Rectangle(titleRect.X, titleRect.Y, 3, titleRect.Height), EditorTheme.Accent);
        EditorTheme.DrawText(sb, EditorTheme.Medium, "RESIZE MAP",
            new Vector2(titleRect.X + 12, titleRect.Y + (titleRect.Height - EditorTheme.Medium.MeasureString("RESIZE MAP").Y) / 2f - 1),
            EditorTheme.Text);

        const string hint = "Tab - next    Enter - apply    Esc - cancel";
        var hintSize = EditorTheme.Tiny.MeasureString(hint);
        EditorTheme.DrawText(sb, EditorTheme.Tiny, hint,
            new Vector2(titleRect.Right - hintSize.X - 10, titleRect.Y + (titleRect.Height - hintSize.Y) / 2f - 1),
            EditorTheme.TextMuted);

        EditorTheme.DrawText(sb, EditorTheme.Small, "New Width",
            new Vector2(dialog.X + 14, dialog.Y + 42), EditorTheme.TextDim);
        DrawField(sb, GetWidthRect(), _widthText, _focus == Focus.Width);

        EditorTheme.DrawText(sb, EditorTheme.Small, "New Height",
            new Vector2(dialog.X + 14, dialog.Y + 96), EditorTheme.TextDim);
        DrawField(sb, GetHeightRect(), _heightText, _focus == Focus.Height);

        EditorTheme.DrawText(sb, EditorTheme.Tiny,
            "Tiles, prototypes, spawns and trigger tiles outside the new bounds will be removed.",
            new Vector2(dialog.X + 14, dialog.Y + 148),
            EditorTheme.Warning);

        if (!TryParseDimensions(out _, out _))
        {
            EditorTheme.DrawText(sb, EditorTheme.Small, "Width and height must be positive integers.",
                new Vector2(dialog.X + 14, dialog.Bottom - 24), EditorTheme.Error);
        }
    }

    private ref string GetFocusedText()
    {
        if (_focus == Focus.Width)
            return ref _widthText;

        return ref _heightText;
    }

    private bool TryParseDimensions(out int width, out int height)
    {
        width = 0;
        height = 0;
        return int.TryParse(_widthText, out width)
            && int.TryParse(_heightText, out height)
            && width > 0
            && height > 0;
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
        return new Rectangle(vp.Width / 2 - 220, vp.Height / 2 - 104, 440, 194);
    }

    private Rectangle GetWidthRect()
    {
        var dialog = GetDialogRect();
        return new Rectangle(dialog.X + 14, dialog.Y + 60, dialog.Width - 28, 24);
    }

    private Rectangle GetHeightRect()
    {
        var dialog = GetDialogRect();
        return new Rectangle(dialog.X + 14, dialog.Y + 114, dialog.Width - 28, 24);
    }
}
