using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace MTEditor.UI;

public class SaveMapDialog
{
    private readonly SpriteFont _font;
    private readonly GraphicsDevice _graphics;
    private Texture2D _pixel;

    public bool IsOpen { get; private set; } = false;

    private string _mapId = "";
    private string _mapName = "";
    private enum Focus { Id, Name }
    private Focus _focus = Focus.Id;
    private Action<string, string>? _onSave;

    public SaveMapDialog(SpriteFont font, GraphicsDevice graphics)
    {
        _font = font;
        _graphics = graphics;
        _pixel = new Texture2D(graphics, 1, 1);
        _pixel.SetData(new[] { Color.White });
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
                var ch = KeyToChar(key, keys.IsKeyDown(Keys.LeftShift));
                if (ch != '\0')
                {
                    if (_focus == Focus.Id) _mapId += ch;
                    else _mapName += ch;
                }
            }
        }
    }

    public void Draw(SpriteBatch spriteBatch)
    {
        if (!IsOpen) return;

        var vp = _graphics.Viewport;
        spriteBatch.Draw(_pixel, new Rectangle(0, 0, vp.Width, vp.Height), Color.Black * 0.5f);

        var dialog = GetDialogRect();
        spriteBatch.Draw(_pixel, dialog, Color.Black * 0.95f);
        spriteBatch.Draw(_pixel, new Rectangle(dialog.X, dialog.Y, dialog.Width, 1), Color.DarkGreen);
        spriteBatch.Draw(_pixel, new Rectangle(dialog.X, dialog.Bottom - 1, dialog.Width, 1), Color.DarkGreen);
        spriteBatch.Draw(_pixel, new Rectangle(dialog.X, dialog.Y, 1, dialog.Height), Color.DarkGreen);
        spriteBatch.Draw(_pixel, new Rectangle(dialog.Right - 1, dialog.Y, 1, dialog.Height), Color.DarkGreen);

        spriteBatch.DrawString(_font, "SAVE MAP", new Vector2(dialog.X + 10, dialog.Y + 8), Color.LimeGreen);
        spriteBatch.Draw(_pixel, new Rectangle(dialog.X, dialog.Y + 26, dialog.Width, 1), Color.DarkGreen * 0.4f);

        // Map ID
        spriteBatch.DrawString(_font, "Map ID (filename, no spaces):", new Vector2(dialog.X + 10, dialog.Y + 34), Color.Gray);
        var idRect = GetIdRect();
        spriteBatch.Draw(_pixel, idRect, _focus == Focus.Id ? Color.DarkGreen * 0.7f : Color.Gray * 0.3f);
        spriteBatch.DrawString(_font, _mapId + (_focus == Focus.Id ? "_" : ""), new Vector2(idRect.X + 4, idRect.Y + 4), Color.White);

        // Map Name
        spriteBatch.DrawString(_font, "Map Name:", new Vector2(dialog.X + 10, dialog.Y + 80), Color.Gray);
        var nameRect = GetNameRect();
        spriteBatch.Draw(_pixel, nameRect, _focus == Focus.Name ? Color.DarkGreen * 0.7f : Color.Gray * 0.3f);
        spriteBatch.DrawString(_font, _mapName + (_focus == Focus.Name ? "_" : ""), new Vector2(nameRect.X + 4, nameRect.Y + 4), Color.White);

        // подсказки
        spriteBatch.DrawString(_font, "Tab=next  Enter=save  Esc=cancel", new Vector2(dialog.X + 10, dialog.Y + 125), Color.Gray);

        if (string.IsNullOrWhiteSpace(_mapId))
            spriteBatch.DrawString(_font, "ID cannot be empty!", new Vector2(dialog.X + 10, dialog.Y + 142), Color.Red);
    }

    private Rectangle GetDialogRect()
    {
        var vp = _graphics.Viewport;
        return new Rectangle(vp.Width / 2 - 200, vp.Height / 2 - 90, 400, 170);
    }

    private Rectangle GetIdRect()
    {
        var d = GetDialogRect();
        return new Rectangle(d.X + 10, d.Y + 52, d.Width - 20, 22);
    }

    private Rectangle GetNameRect()
    {
        var d = GetDialogRect();
        return new Rectangle(d.X + 10, d.Y + 97, d.Width - 20, 22);
    }

    private static char KeyToChar(Keys key, bool shift)
    {
        if (key >= Keys.A && key <= Keys.Z)
            return shift ? (char)('A' + (key - Keys.A)) : (char)('a' + (key - Keys.A));
        if (key >= Keys.D0 && key <= Keys.D9)
            return (char)('0' + (key - Keys.D0));
        return key switch
        {
            Keys.OemMinus => '_',
            Keys.OemPeriod => '.',
            _ => '\0'
        };
    }
}