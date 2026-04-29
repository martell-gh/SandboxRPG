using System;
using System.Collections.Generic;
using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MTEngine.World;

namespace MTEditor.UI;

public class MapSelectDialog
{
    private readonly GraphicsDevice _graphics;

    public bool IsOpen { get; private set; } = false;
    private List<string> _maps = new();
    private int _selectedIndex = 0;
    private Action<string>? _onSelect;

    public MapSelectDialog(GraphicsDevice graphics)
    {
        _graphics = graphics;
    }

    public void Open(List<string> maps, Action<string> onSelect)
    {
        _maps = maps;
        _onSelect = onSelect;
        _selectedIndex = 0;
        IsOpen = true;
    }

    public void Close() => IsOpen = false;

    public void Update(MouseState mouse, MouseState prev, KeyboardState keys, KeyboardState prevKeys)
    {
        if (!IsOpen) return;

        if (IsPressed(keys, prevKeys, Keys.Up))
            _selectedIndex = Math.Max(0, _selectedIndex - 1);
        if (IsPressed(keys, prevKeys, Keys.Down))
            _selectedIndex = Math.Min(_maps.Count - 1, _selectedIndex + 1);

        if (IsPressed(keys, prevKeys, Keys.Enter) && _maps.Count > 0)
        {
            _onSelect?.Invoke(_maps[_selectedIndex]);
            IsOpen = false;
        }

        if (IsPressed(keys, prevKeys, Keys.Escape))
            IsOpen = false;

        if (mouse.LeftButton == ButtonState.Pressed && prev.LeftButton == ButtonState.Released)
        {
            for (int i = 0; i < _maps.Count; i++)
            {
                if (GetItemRect(i).Contains(mouse.X, mouse.Y))
                {
                    _selectedIndex = i;
                    _onSelect?.Invoke(_maps[i]);
                    IsOpen = false;
                    return;
                }
            }

            if (!GetDialogRect().Contains(mouse.X, mouse.Y))
                IsOpen = false;
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

        // Title bar
        var title = new Rectangle(dialog.X, dialog.Y, dialog.Width, 26);
        EditorTheme.FillRect(sb, title, EditorTheme.Panel);
        sb.Draw(EditorTheme.Pixel, new Rectangle(title.X, title.Bottom - 1, title.Width, 1), EditorTheme.Border);
        sb.Draw(EditorTheme.Pixel, new Rectangle(title.X, title.Y, 3, title.Height), EditorTheme.Accent);
        EditorTheme.DrawText(sb, EditorTheme.Medium, "OPEN MAP",
            new Vector2(title.X + 12, title.Y + (title.Height - EditorTheme.Medium.MeasureString("OPEN MAP").Y) / 2f - 1),
            EditorTheme.Text);

        var hint = "Enter — open     Esc — cancel";
        var hintSize = EditorTheme.Tiny.MeasureString(hint);
        EditorTheme.DrawText(sb, EditorTheme.Tiny, hint,
            new Vector2(title.Right - hintSize.X - 10, title.Y + (title.Height - hintSize.Y) / 2f - 1),
            EditorTheme.TextMuted);

        if (_maps.Count == 0)
        {
            EditorTheme.DrawText(sb, EditorTheme.Body, "No maps found. Create one first!",
                new Vector2(dialog.X + 14, dialog.Y + 40), EditorTheme.TextMuted);
            return;
        }

        for (int i = 0; i < _maps.Count; i++)
        {
            var rect = GetItemRect(i);
            var selected = i == _selectedIndex;
            if (selected)
                EditorTheme.FillRect(sb, rect, EditorTheme.Accent);
            EditorTheme.DrawText(sb, EditorTheme.Body, _maps[i],
                new Vector2(rect.X + 10, rect.Y + (rect.Height - EditorTheme.Body.MeasureString(_maps[i]).Y) / 2f - 1),
                selected ? Color.White : EditorTheme.Text);
        }
    }

    private Rectangle GetDialogRect()
    {
        var vp = _graphics.Viewport;
        var w = 420;
        var h = Math.Max(140, 40 + _maps.Count * 24 + 16);
        return new Rectangle(vp.Width / 2 - w / 2, vp.Height / 2 - h / 2, w, h);
    }

    private Rectangle GetItemRect(int i)
    {
        var d = GetDialogRect();
        return new Rectangle(d.X + 6, d.Y + 34 + i * 24, d.Width - 12, 22);
    }

    private static bool IsPressed(KeyboardState cur, KeyboardState prev, Keys key)
        => cur.IsKeyDown(key) && prev.IsKeyUp(key);
}
