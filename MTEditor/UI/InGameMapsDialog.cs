using System;
using System.Collections.Generic;
using System.Linq;
using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MTEngine.World;

namespace MTEditor.UI;

public class InGameMapsDialog
{
    private readonly GraphicsDevice _graphics;

    private List<MapCatalogEntry> _maps = new();
    private int _selectedIndex;
    private Action<string, bool>? _onToggle;

    public bool IsOpen { get; private set; }

    public InGameMapsDialog(GraphicsDevice graphics)
    {
        _graphics = graphics;
    }

    public void Open(List<MapCatalogEntry> maps, Action<string, bool> onToggle)
    {
        _maps = maps.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToList();
        _selectedIndex = Math.Clamp(_selectedIndex, 0, Math.Max(0, _maps.Count - 1));
        _onToggle = onToggle;
        IsOpen = true;
    }

    public void Close() => IsOpen = false;

    public void Refresh(List<MapCatalogEntry> maps)
    {
        var selectedId = _maps.Count > 0 && _selectedIndex >= 0 && _selectedIndex < _maps.Count
            ? _maps[_selectedIndex].Id
            : null;

        _maps = maps.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToList();
        if (selectedId != null)
        {
            var idx = _maps.FindIndex(m => string.Equals(m.Id, selectedId, StringComparison.OrdinalIgnoreCase));
            _selectedIndex = idx >= 0 ? idx : 0;
        }
        else
        {
            _selectedIndex = 0;
        }
    }

    public void Update(MouseState mouse, MouseState prev, KeyboardState keys, KeyboardState prevKeys)
    {
        if (!IsOpen) return;

        if (IsPressed(keys, prevKeys, Keys.Escape) || IsPressed(keys, prevKeys, Keys.F6))
        {
            IsOpen = false;
            return;
        }

        if (IsPressed(keys, prevKeys, Keys.Up))
            _selectedIndex = Math.Max(0, _selectedIndex - 1);
        if (IsPressed(keys, prevKeys, Keys.Down))
            _selectedIndex = Math.Min(Math.Max(0, _maps.Count - 1), _selectedIndex + 1);

        if ((IsPressed(keys, prevKeys, Keys.Enter) || IsPressed(keys, prevKeys, Keys.Space)) && _maps.Count > 0)
        {
            var item = _maps[_selectedIndex];
            _onToggle?.Invoke(item.Id, !item.InGame);
            return;
        }

        if (mouse.LeftButton == ButtonState.Pressed && prev.LeftButton == ButtonState.Released)
        {
            for (var i = 0; i < _maps.Count; i++)
            {
                if (!GetItemRect(i).Contains(mouse.Position)) continue;
                _selectedIndex = i;
                var item = _maps[i];
                _onToggle?.Invoke(item.Id, !item.InGame);
                return;
            }

            if (!GetDialogRect().Contains(mouse.Position))
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

        // Title
        var title = new Rectangle(dialog.X, dialog.Y, dialog.Width, 26);
        EditorTheme.FillRect(sb, title, EditorTheme.Panel);
        sb.Draw(EditorTheme.Pixel, new Rectangle(title.X, title.Bottom - 1, title.Width, 1), EditorTheme.Border);
        sb.Draw(EditorTheme.Pixel, new Rectangle(title.X, title.Y, 3, title.Height), EditorTheme.Accent);
        EditorTheme.DrawText(sb, EditorTheme.Medium, "IN-GAME MAPS",
            new Vector2(title.X + 12, title.Y + (title.Height - EditorTheme.Medium.MeasureString("IN-GAME MAPS").Y) / 2f - 1),
            EditorTheme.Text);

        var hint = "Enter/Click — toggle     F6/Esc — close";
        var hintSize = EditorTheme.Tiny.MeasureString(hint);
        EditorTheme.DrawText(sb, EditorTheme.Tiny, hint,
            new Vector2(title.Right - hintSize.X - 10, title.Y + (title.Height - hintSize.Y) / 2f - 1),
            EditorTheme.TextMuted);

        EditorTheme.DrawText(sb, EditorTheme.Small,
            "Maps marked ON participate in future world simulation outside the loaded location.",
            new Vector2(dialog.X + 14, dialog.Y + 34), EditorTheme.TextMuted);

        for (var i = 0; i < _maps.Count; i++)
        {
            var rect = GetItemRect(i);
            var item = _maps[i];
            var selected = i == _selectedIndex;

            EditorTheme.FillRect(sb, rect, selected ? EditorTheme.Accent : EditorTheme.PanelAlt);
            EditorTheme.DrawBorder(sb, rect, selected ? EditorTheme.AccentHover : EditorTheme.Border);

            // Flag pill
            var flagRect = new Rectangle(rect.X + 8, rect.Y + 8, 44, rect.Height - 16);
            EditorTheme.FillRect(sb, flagRect, item.InGame ? EditorTheme.Success : EditorTheme.Error);
            var flagText = item.InGame ? "ON" : "OFF";
            var flagSize = EditorTheme.Tiny.MeasureString(flagText);
            EditorTheme.DrawText(sb, EditorTheme.Tiny, flagText,
                new Vector2(flagRect.X + (flagRect.Width - flagSize.X) / 2f, flagRect.Y + (flagRect.Height - flagSize.Y) / 2f - 1),
                Color.White);

            EditorTheme.DrawText(sb, EditorTheme.Body, item.Name,
                new Vector2(rect.X + 60, rect.Y + 6),
                selected ? Color.White : EditorTheme.Text);
            EditorTheme.DrawText(sb, EditorTheme.Tiny, item.Id,
                new Vector2(rect.X + 60, rect.Y + 22),
                selected ? new Color(220, 230, 255) : EditorTheme.TextMuted);
        }
    }

    private Rectangle GetDialogRect()
    {
        var vp = _graphics.Viewport;
        return new Rectangle(vp.Width / 2 - 280, vp.Height / 2 - 230, 560, 460);
    }

    private Rectangle GetItemRect(int index)
    {
        var dialog = GetDialogRect();
        return new Rectangle(dialog.X + 12, dialog.Y + 60 + index * 42, dialog.Width - 24, 38);
    }

    private static bool IsPressed(KeyboardState current, KeyboardState previous, Keys key)
        => current.IsKeyDown(key) && previous.IsKeyUp(key);
}
