using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MTEngine.World;

namespace MTEditor.UI;

public class MapSelectDialog
{
    private readonly SpriteFont _font;
    private readonly GraphicsDevice _graphics;
    private Texture2D _pixel;

    public bool IsOpen { get; private set; } = false;
    private List<string> _maps = new();
    private int _selectedIndex = 0;
    private Action<string>? _onSelect;

    private KeyboardState _prevKeys;
    private MouseState _prevMouse;

    public MapSelectDialog(SpriteFont font, GraphicsDevice graphics)
    {
        _font = font;
        _graphics = graphics;
        _pixel = new Texture2D(graphics, 1, 1);
        _pixel.SetData(new[] { Color.White });
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

        // стрелки вверх/вниз
        if (IsPressed(keys, prevKeys, Keys.Up))
            _selectedIndex = Math.Max(0, _selectedIndex - 1);
        if (IsPressed(keys, prevKeys, Keys.Down))
            _selectedIndex = Math.Min(_maps.Count - 1, _selectedIndex + 1);

        // Enter — выбрать
        if (IsPressed(keys, prevKeys, Keys.Enter) && _maps.Count > 0)
        {
            _onSelect?.Invoke(_maps[_selectedIndex]);
            IsOpen = false;
        }

        // Escape — закрыть
        if (IsPressed(keys, prevKeys, Keys.Escape))
            IsOpen = false;

        // клик мышью
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

            // клик вне диалога — закрыть
            if (!GetDialogRect().Contains(mouse.X, mouse.Y))
                IsOpen = false;
        }
    }

    public void Draw(SpriteBatch spriteBatch)
    {
        if (!IsOpen) return;

        var dialog = GetDialogRect();

        // затемнение фона
        spriteBatch.Draw(_pixel, new Rectangle(0, 0, _graphics.Viewport.Width, _graphics.Viewport.Height), Color.Black * 0.5f);

        // диалог
        spriteBatch.Draw(_pixel, dialog, Color.Black * 0.95f);
        spriteBatch.Draw(_pixel, new Rectangle(dialog.X, dialog.Y, dialog.Width, 1), Color.DarkGreen);
        spriteBatch.Draw(_pixel, new Rectangle(dialog.X, dialog.Bottom - 1, dialog.Width, 1), Color.DarkGreen);
        spriteBatch.Draw(_pixel, new Rectangle(dialog.X, dialog.Y, 1, dialog.Height), Color.DarkGreen);
        spriteBatch.Draw(_pixel, new Rectangle(dialog.Right - 1, dialog.Y, 1, dialog.Height), Color.DarkGreen);

        spriteBatch.DrawString(_font, "SELECT MAP  (Enter=load  Esc=cancel)", new Vector2(dialog.X + 10, dialog.Y + 8), Color.LimeGreen);
        spriteBatch.Draw(_pixel, new Rectangle(dialog.X, dialog.Y + 25, dialog.Width, 1), Color.DarkGreen * 0.5f);

        if (_maps.Count == 0)
        {
            spriteBatch.DrawString(_font, "No maps found. Create one first!", new Vector2(dialog.X + 10, dialog.Y + 35), Color.Gray);
            return;
        }

        for (int i = 0; i < _maps.Count; i++)
        {
            var rect = GetItemRect(i);
            if (i == _selectedIndex)
                spriteBatch.Draw(_pixel, rect, Color.DarkGreen * 0.6f);
            spriteBatch.DrawString(_font, _maps[i], new Vector2(rect.X + 8, rect.Y + 4), i == _selectedIndex ? Color.White : Color.LimeGreen);
        }
    }

    private Rectangle GetDialogRect()
    {
        var vp = _graphics.Viewport;
        var w = 400; var h = Math.Max(120, 40 + _maps.Count * 26 + 20);
        return new Rectangle(vp.Width / 2 - w / 2, vp.Height / 2 - h / 2, w, h);
    }

    private Rectangle GetItemRect(int i)
    {
        var d = GetDialogRect();
        return new Rectangle(d.X + 5, d.Y + 30 + i * 26, d.Width - 10, 24);
    }

    private static bool IsPressed(KeyboardState cur, KeyboardState prev, Keys key)
        => cur.IsKeyDown(key) && prev.IsKeyUp(key);
}