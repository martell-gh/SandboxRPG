using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MTEngine.Core;
using MTEngine.World;

namespace MTEditor.UI;

public class TilePalette
{
    private readonly PrototypeManager _prototypes;
    private readonly AssetManager _assets;
    private readonly SpriteFont _font;
    private readonly GraphicsDevice _graphics;

    public string? SelectedTileId { get; private set; }
    public Rectangle Bounds { get; private set; }

    private List<TilePrototype> _tiles = new();
    private Texture2D _pixel;

    private const int PaletteWidth = 180;
    private const int HeaderHeight = 28;
    private const int SelectedBlockHeight = 52;
    private const int HintHeight = 22;
    private const int TileRowHeight = 36;
    private const int TileIconSize = 24;
    private const int Padding = 10;

    public TilePalette(PrototypeManager prototypes, AssetManager assets, SpriteFont font, GraphicsDevice graphics)
    {
        _prototypes = prototypes;
        _assets = assets;
        _font = font;
        _graphics = graphics;
        _tiles = prototypes.GetAllTiles().ToList();

        if (_tiles.Count > 0)
            SelectedTileId = _tiles[0].Id;

        _pixel = new Texture2D(graphics, 1, 1);
        _pixel.SetData(new[] { Color.White });

        Bounds = new Rectangle(0, 0, PaletteWidth, graphics.Viewport.Height);
        Console.WriteLine($"[TilePalette] Loaded {_tiles.Count} tiles");
    }

    public void Update(MouseState mouse, MouseState prev)
    {
        if (mouse.LeftButton == ButtonState.Pressed && prev.LeftButton == ButtonState.Released)
        {
            for (int i = 0; i < _tiles.Count; i++)
            {
                if (GetTileRowRect(i).Contains(mouse.X, mouse.Y))
                {
                    SelectedTileId = _tiles[i].Id;
                    break;
                }
            }
        }
    }

    public void Draw(SpriteBatch spriteBatch)
    {
        // фон
        spriteBatch.Draw(_pixel, Bounds, Color.Black * 0.88f);
        spriteBatch.Draw(_pixel, new Rectangle(PaletteWidth - 1, 0, 1, Bounds.Height), Color.DarkGreen);

        int y = 0;

        // === ЗАГОЛОВОК ===
        spriteBatch.Draw(_pixel, new Rectangle(0, y, PaletteWidth, HeaderHeight), Color.DarkGreen * 0.5f);
        spriteBatch.DrawString(_font, "TILES  [1]", new Vector2(Padding, y + 7), Color.LimeGreen);
        y += HeaderHeight;

        // === ВЫБРАННЫЙ ТАЙЛ ===
        var selected = SelectedTileId != null ? _tiles.FirstOrDefault(t => t.Id == SelectedTileId) : null;

        spriteBatch.Draw(_pixel, new Rectangle(0, y, PaletteWidth, SelectedBlockHeight), Color.DarkGreen * 0.2f);
        spriteBatch.Draw(_pixel, new Rectangle(0, y, PaletteWidth, 1), Color.DarkGreen * 0.4f);

        if (selected != null)
        {
            spriteBatch.DrawString(_font, "Selected:", new Vector2(Padding, y + 6), Color.Gray);

            var tex = GetTileTexture(selected);
            var src = GetTileSourceRect(selected);
            spriteBatch.Draw(tex, new Rectangle(Padding, y + 20, TileIconSize, TileIconSize), src, Color.White);

            spriteBatch.DrawString(_font, selected.Name,
                new Vector2(Padding + TileIconSize + 8, y + 20), Color.White);
            spriteBatch.DrawString(_font,
                selected.Solid ? "SOLID" : "passable",
                new Vector2(Padding + TileIconSize + 8, y + 34),
                selected.Solid ? Color.Orange : Color.Gray);
        }
        else
        {
            spriteBatch.DrawString(_font, "None", new Vector2(Padding, y + 18), Color.Gray);
        }

        y += SelectedBlockHeight;

        // === ПОДСКАЗКА ===
        spriteBatch.Draw(_pixel, new Rectangle(0, y, PaletteWidth, 1), Color.DarkGreen * 0.4f);
        spriteBatch.DrawString(_font, "L=paint   R=erase", new Vector2(Padding, y + 5), Color.Gray);
        y += HintHeight;
        spriteBatch.Draw(_pixel, new Rectangle(0, y, PaletteWidth, 1), Color.DarkGreen * 0.3f);
        y += 2;

        if (_tiles.Count == 0)
        {
            spriteBatch.DrawString(_font, "No tiles!", new Vector2(Padding, y + 10), Color.Red);
            return;
        }

        // === СПИСОК ТАЙЛОВ ===
        for (int i = 0; i < _tiles.Count; i++)
        {
            var tile = _tiles[i];
            var rowRect = GetTileRowRect(i);
            bool isSelected = tile.Id == SelectedTileId;

            // фон строки
            if (isSelected)
                spriteBatch.Draw(_pixel, rowRect, Color.DarkGreen * 0.45f);

            // иконка тайла
            var iconRect = new Rectangle(
                rowRect.X + Padding,
                rowRect.Y + (TileRowHeight - TileIconSize) / 2,
                TileIconSize, TileIconSize
            );

            var tileTex = GetTileTexture(tile);
            var tileSrc = GetTileSourceRect(tile);
            spriteBatch.Draw(tileTex, iconRect, tileSrc, Color.White);

            // рамка иконки если выбран
            if (isSelected)
            {
                spriteBatch.Draw(_pixel, new Rectangle(iconRect.X - 1, iconRect.Y - 1, iconRect.Width + 2, 1), Color.White);
                spriteBatch.Draw(_pixel, new Rectangle(iconRect.X - 1, iconRect.Bottom, iconRect.Width + 2, 1), Color.White);
                spriteBatch.Draw(_pixel, new Rectangle(iconRect.X - 1, iconRect.Y - 1, 1, iconRect.Height + 2), Color.White);
                spriteBatch.Draw(_pixel, new Rectangle(iconRect.Right, iconRect.Y - 1, 1, iconRect.Height + 2), Color.White);
            }

            // название
            var textX = iconRect.Right + 8;
            var textY = rowRect.Y + 8;
            spriteBatch.DrawString(_font, tile.Name, new Vector2(textX, textY),
                isSelected ? Color.White : Color.LightGray);

            // solid/passable
            spriteBatch.DrawString(_font,
                tile.Solid ? "solid" : "pass",
                new Vector2(textX, textY + 16),
                tile.Solid ? new Color(200, 120, 0) : new Color(80, 120, 80));

            // разделитель
            spriteBatch.Draw(_pixel,
                new Rectangle(0, rowRect.Bottom - 1, PaletteWidth, 1),
                Color.White * 0.05f);
        }
    }

    private Texture2D GetTileTexture(TilePrototype tile)
    {
        if (tile.Sprite?.FullPath != null)
        {
            var tex = _assets.LoadFromFile(tile.Sprite.FullPath);
            if (tex != null) return tex;
        }

        if (tile.Animations != null && !string.IsNullOrEmpty(tile.Animations.TexturePath))
        {
            var tex = _assets.LoadFromFile(tile.Animations.TexturePath);
            if (tex != null) return tex;
        }

        return _assets.GetColorTexture(tile.Color);
    }

    private Rectangle? GetTileSourceRect(TilePrototype tile)
    {
        if (tile.Animations != null)
        {
            var clip = tile.Animations.GetClip("idle")
                    ?? tile.Animations.GetAllClips().FirstOrDefault();
            if (clip?.Frames.Count > 0)
                return clip.Frames[0].SourceRect;
        }

        if (tile.Sprite != null)
            return new Rectangle(tile.Sprite.SrcX, tile.Sprite.SrcY,
                                  tile.Sprite.Width, tile.Sprite.Height);

        return null;
    }

    private Rectangle GetTileRowRect(int index)
    {
        int startY = HeaderHeight + SelectedBlockHeight + HintHeight + 2;
        return new Rectangle(0, startY + index * TileRowHeight, PaletteWidth, TileRowHeight);
    }
}