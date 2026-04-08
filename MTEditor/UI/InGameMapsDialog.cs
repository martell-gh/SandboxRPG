using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MTEngine.World;

namespace MTEditor.UI;

public class InGameMapsDialog
{
    private readonly SpriteFont _font;
    private readonly GraphicsDevice _graphics;
    private readonly Texture2D _pixel;

    private List<MapCatalogEntry> _maps = new();
    private int _selectedIndex;
    private Action<string, bool>? _onToggle;

    public bool IsOpen { get; private set; }

    public InGameMapsDialog(SpriteFont font, GraphicsDevice graphics)
    {
        _font = font;
        _graphics = graphics;
        _pixel = new Texture2D(graphics, 1, 1);
        _pixel.SetData(new[] { Color.White });
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
        if (!IsOpen)
            return;

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
                if (!GetItemRect(i).Contains(mouse.Position))
                    continue;

                _selectedIndex = i;
                var item = _maps[i];
                _onToggle?.Invoke(item.Id, !item.InGame);
                return;
            }

            if (!GetDialogRect().Contains(mouse.Position))
                IsOpen = false;
        }
    }

    public void Draw(SpriteBatch spriteBatch)
    {
        if (!IsOpen)
            return;

        var viewport = _graphics.Viewport;
        spriteBatch.Draw(_pixel, new Rectangle(0, 0, viewport.Width, viewport.Height), Color.Black * 0.55f);

        var dialog = GetDialogRect();
        spriteBatch.Draw(_pixel, dialog, Color.Black * 0.94f);
        DrawBorder(spriteBatch, dialog, new Color(90, 140, 96));

        spriteBatch.DrawString(_font, "IN-GAME MAPS  (Enter/Click=toggle  F6/Esc=close)", new Vector2(dialog.X + 12, dialog.Y + 10), Color.LimeGreen);
        spriteBatch.DrawString(_font, "Maps marked ON are intended for future world simulation outside the loaded location.", new Vector2(dialog.X + 12, dialog.Y + 34), Color.Gray);

        for (var i = 0; i < _maps.Count; i++)
        {
            var rect = GetItemRect(i);
            var item = _maps[i];
            if (i == _selectedIndex)
                spriteBatch.Draw(_pixel, rect, new Color(40, 70, 48, 220));

            var flagRect = new Rectangle(rect.X + 8, rect.Y + 4, 58, rect.Height - 8);
            spriteBatch.Draw(_pixel, flagRect, item.InGame ? new Color(48, 110, 60) : new Color(90, 56, 56));
            DrawBorder(spriteBatch, flagRect, item.InGame ? new Color(120, 200, 130) : new Color(170, 100, 100));
            spriteBatch.DrawString(_font, item.InGame ? "ON" : "OFF", new Vector2(flagRect.X + 16, flagRect.Y + 3), Color.White);

            spriteBatch.DrawString(_font, item.Name, new Vector2(rect.X + 78, rect.Y + 4), Color.White);
            spriteBatch.DrawString(_font, item.Id, new Vector2(rect.X + 78, rect.Y + 22), Color.Gray);
        }
    }

    private Rectangle GetDialogRect()
    {
        var vp = _graphics.Viewport;
        return new Rectangle(vp.Width / 2 - 260, vp.Height / 2 - 220, 520, 440);
    }

    private Rectangle GetItemRect(int index)
    {
        var dialog = GetDialogRect();
        return new Rectangle(dialog.X + 12, dialog.Y + 68 + index * 44, dialog.Width - 24, 40);
    }

    private void DrawBorder(SpriteBatch spriteBatch, Rectangle rect, Color color)
    {
        spriteBatch.Draw(_pixel, new Rectangle(rect.X, rect.Y, rect.Width, 1), color);
        spriteBatch.Draw(_pixel, new Rectangle(rect.X, rect.Bottom - 1, rect.Width, 1), color);
        spriteBatch.Draw(_pixel, new Rectangle(rect.X, rect.Y, 1, rect.Height), color);
        spriteBatch.Draw(_pixel, new Rectangle(rect.Right - 1, rect.Y, 1, rect.Height), color);
    }

    private static bool IsPressed(KeyboardState current, KeyboardState previous, Keys key)
        => current.IsKeyDown(key) && previous.IsKeyUp(key);
}
