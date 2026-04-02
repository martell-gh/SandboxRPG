using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MTEngine.World;

namespace MTEditor.UI;

public class EditorHUD
{
    private readonly SpriteFont _font;
    private readonly GraphicsDevice _graphics;
    private Texture2D _pixel;

    private string _message = "";
    private float _messageTimer = 0f;

    private const int PanelHeight = 44;

    public Rectangle Bounds { get; private set; }

    public EditorHUD(SpriteFont font, GraphicsDevice graphics)
    {
        _font = font;
        _graphics = graphics;
        _pixel = new Texture2D(graphics, 1, 1);
        _pixel.SetData(new[] { Color.White });
        Bounds = new Rectangle(0, graphics.Viewport.Height - PanelHeight, graphics.Viewport.Width, PanelHeight);
    }

    public void ShowMessage(string msg)
    {
        _message = msg;
        _messageTimer = 3f;
        Console.WriteLine($"[Editor] {msg}");
    }

    public void ApplyToMap(MapData map) { }
    public void SetMapInfo(MapData map) { }

    public void Update(MouseState mouse, MouseState prev)
    {
        if (_messageTimer > 0) _messageTimer -= 0.016f;
    }

    public void Draw(SpriteBatch spriteBatch, EditorGame.Tool activeTool, MapData map, EditorHistory history, int activeLayer, TilePalette palette)
    {
        var viewport = _graphics.Viewport;
        var panelY = viewport.Height - PanelHeight;

        // фон
        spriteBatch.Draw(_pixel, new Rectangle(0, panelY, viewport.Width, PanelHeight), Color.Black * 0.9f);
        spriteBatch.Draw(_pixel, new Rectangle(0, panelY, viewport.Width, 1), Color.DarkGreen);

        // строка 1 — инфо о карте
        var tool = activeTool switch
        {
            EditorGame.Tool.TilePainter => "[1] Tiles",
            EditorGame.Tool.EntityPainter => "[2] Objects",
            _ => "[3] Spawn"
        };
        var selected = activeTool == EditorGame.Tool.TilePainter
            ? palette.SelectedTileId ?? "-"
            : activeTool == EditorGame.Tool.EntityPainter
                ? palette.SelectedEntityId ?? "-"
                : "spawn";
        spriteBatch.DrawString(_font,
            $"Tool: {tool}   Selected: {selected}   Tile Layer: {activeLayer}   Map: {map.Id}   Size: {map.Width}x{map.Height}   Spawns: {map.SpawnPoints.Count}   Objects: {map.Entities.Count}",
            new Vector2(175, panelY + 5), Color.LimeGreen);

        spriteBatch.DrawString(_font,
            "Ctrl+S=Save   Ctrl+O=Load   Ctrl+N=New   Ctrl+Z=Undo   Ctrl+Y=Redo   [1]=Tiles   [2]=Objects   [3]=Spawn   Q/E=Layer   Arrows=Move   Scroll=Zoom",
            new Vector2(175, panelY + 22), Color.Gray);

        // сообщение по центру над панелью
        if (_messageTimer > 0)
        {
            var alpha = Math.Min(1f, _messageTimer);
            var size = _font.MeasureString(_message);
            spriteBatch.Draw(_pixel,
                new Rectangle((int)(viewport.Width / 2f - size.X / 2f - 6), panelY - 26, (int)size.X + 12, 22),
                Color.Black * 0.85f * alpha);
            spriteBatch.DrawString(_font, _message,
                new Vector2(viewport.Width / 2f - size.X / 2f, panelY - 24),
                Color.Yellow * alpha);
        }
    }
}
