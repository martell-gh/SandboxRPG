using System;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MTEngine.Core;
using MTEngine.World;

namespace MTEditor.Tools;

public class SpawnPointTool
{
    private MapData _map;
    private Texture2D? _pixel;
    private GraphicsDevice _graphics;

    // ввод ID для нового спавнера
    public string InputId { get; private set; } = "default";
    private bool _typingId = false;
    private KeyboardState _prevKeys;

    public SpawnPointTool(MapData map, GraphicsDevice graphics)
    {
        _map = map;
        _graphics = graphics;
        _pixel = new Texture2D(graphics, 1, 1);
        _pixel.SetData(new[] { Color.White });
    }

    public void SetMap(MapData map)
    {
        _map = map;
        InputId = "default";
    }

    public void Update(MouseState mouse, MouseState prev, Vector2 worldPos)
    {
        var keys = Keyboard.GetState();

        // клик по полю ввода ID — активируем ввод
        var inputRect = GetInputRect();
        if (mouse.LeftButton == ButtonState.Pressed && prev.LeftButton == ButtonState.Released)
        {
            _typingId = inputRect.Contains(mouse.X, mouse.Y);
        }

        // ввод текста для ID
        if (_typingId)
        {
            foreach (var key in keys.GetPressedKeys())
            {
                if (_prevKeys.IsKeyDown(key)) continue;

                if (key == Keys.Back && InputId.Length > 0)
                    InputId = InputId[..^1];
                else if (key == Keys.Escape)
                    _typingId = false;
                else
                {
                    var ch = KeyToChar(key, keys.IsKeyDown(Keys.LeftShift));
                    if (ch != '\0') InputId += ch;
                }
            }
        }

        _prevKeys = keys;

        // не кликаем по UI полю ввода
        if (inputRect.Contains(mouse.X, mouse.Y)) return;

        var tileX = (int)(worldPos.X / _map.TileSize);
        var tileY = (int)(worldPos.Y / _map.TileSize);
        if (tileX < 0 || tileX >= _map.Width || tileY < 0 || tileY >= _map.Height) return;

        // левая кнопка — ставим спавн поинт
        if (mouse.LeftButton == ButtonState.Pressed && prev.LeftButton == ButtonState.Released)
        {
            if (string.IsNullOrWhiteSpace(InputId)) return;

            var existing = _map.SpawnPoints.FirstOrDefault(s => s.Id == InputId);
            if (existing != null)
            {
                existing.X = tileX;
                existing.Y = tileY;
            }
            else
            {
                _map.SpawnPoints.Add(new SpawnPoint
                {
                    Id = InputId,
                    X = tileX,
                    Y = tileY
                });
            }
        }

        // правая кнопка — удаляем
        if (mouse.RightButton == ButtonState.Pressed && prev.RightButton == ButtonState.Released)
        {
            _map.SpawnPoints.RemoveAll(s =>
                Math.Abs(s.X - tileX) <= 1 && Math.Abs(s.Y - tileY) <= 1);
        }
    }

    public void Draw(SpriteBatch spriteBatch, AssetManager assets, SpriteFont font)
    {
        // спавн поинты на карте
        var tex = assets.GetColorTexture("#ff00ff");
        foreach (var sp in _map.SpawnPoints)
        {
            var pos = new Vector2(sp.X * _map.TileSize, sp.Y * _map.TileSize);
            spriteBatch.Draw(tex, new Rectangle((int)pos.X, (int)pos.Y, _map.TileSize, _map.TileSize), Color.White * 0.8f);
            spriteBatch.DrawString(font, sp.Id, pos + new Vector2(0, -14), Color.Yellow);
        }
    }

    public void DrawUI(SpriteBatch spriteBatch, SpriteFont font)
    {
        // панель инструмента спавна — сверху справа
        var rect = new Rectangle(170, 5, 300, 80);
        spriteBatch.Draw(_pixel!, rect, Color.Black * 0.8f);
        spriteBatch.DrawString(font, "SPAWN POINT TOOL [2]", new Vector2(175, 8), Color.LimeGreen);
        spriteBatch.DrawString(font, "Spawn ID:", new Vector2(175, 25), Color.White);

        // поле ввода
        var inputRect = GetInputRect();
        spriteBatch.Draw(_pixel!, inputRect, _typingId ? Color.DarkGreen * 0.8f : Color.Gray * 0.4f);
        spriteBatch.DrawString(font, InputId + (_typingId ? "_" : ""), new Vector2(inputRect.X + 3, inputRect.Y + 3), Color.White);

        spriteBatch.DrawString(font, "LClick=place  RClick=delete", new Vector2(175, 60), Color.Gray);

        // список спавнеров
        spriteBatch.DrawString(font, $"Spawns: {string.Join(", ", _map.SpawnPoints.Select(s => s.Id))}", new Vector2(480, 8), Color.Cyan);
    }

    private Rectangle GetInputRect() => new Rectangle(245, 22, 150, 20);

    private static char KeyToChar(Keys key, bool shift)
    {
        if (key >= Keys.A && key <= Keys.Z)
            return shift ? (char)('A' + (key - Keys.A)) : (char)('a' + (key - Keys.A));
        if (key >= Keys.D0 && key <= Keys.D9)
            return (char)('0' + (key - Keys.D0));
        return key switch
        {
            Keys.OemMinus => '_',
            Keys.Space => '_',
            _ => '\0'
        };
    }
}