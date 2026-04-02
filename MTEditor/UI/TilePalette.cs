using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MTEngine.Core;
using MTEngine.World;

namespace MTEditor.UI;

public enum PaletteMode
{
    Tiles,
    Entities
}

public class TilePalette
{
    private readonly PrototypeManager _prototypes;
    private readonly AssetManager _assets;
    private readonly SpriteFont _font;
    private readonly GraphicsDevice _graphics;

    public string? SelectedTileId { get; private set; }
    public string? SelectedEntityId { get; private set; }
    public PaletteMode Mode { get; set; } = PaletteMode.Tiles;
    public Rectangle Bounds { get; private set; }

    private List<TilePrototype> _tiles = new();
    private List<EntityPrototype> _entities = new();
    private Texture2D _pixel;

    private const int PaletteWidth = 280;
    private const int HeaderHeight = 28;
    private const int TabHeight = 28;
    private const int SelectedBlockHeight = 52;
    private const int HintHeight = 22;
    private const int TileRowHeight = 40;
    private const int TileIconSize = 24;
    private const int Padding = 10;

    public TilePalette(PrototypeManager prototypes, AssetManager assets, SpriteFont font, GraphicsDevice graphics)
    {
        _prototypes = prototypes;
        _assets = assets;
        _font = font;
        _graphics = graphics;
        _tiles = prototypes.GetAllTiles().ToList();
        _entities = prototypes.GetAllEntities().ToList();

        if (_tiles.Count > 0)
            SelectedTileId = _tiles[0].Id;
        if (_entities.Count > 0)
            SelectedEntityId = _entities[0].Id;

        _pixel = new Texture2D(graphics, 1, 1);
        _pixel.SetData(new[] { Color.White });

        Bounds = new Rectangle(0, 0, PaletteWidth, graphics.Viewport.Height);
        Console.WriteLine($"[TilePalette] Loaded {_tiles.Count} tiles");
    }

    public void Update(MouseState mouse, MouseState prev)
    {
        if (mouse.LeftButton == ButtonState.Pressed && prev.LeftButton == ButtonState.Released)
        {
            if (GetTabRect(PaletteMode.Tiles).Contains(mouse.X, mouse.Y))
            {
                Mode = PaletteMode.Tiles;
                return;
            }

            if (GetTabRect(PaletteMode.Entities).Contains(mouse.X, mouse.Y))
            {
                Mode = PaletteMode.Entities;
                return;
            }

            if (Mode == PaletteMode.Tiles)
            {
                for (int i = 0; i < _tiles.Count; i++)
                {
                    if (GetRowRect(i).Contains(mouse.X, mouse.Y))
                    {
                        SelectedTileId = _tiles[i].Id;
                        break;
                    }
                }
            }
            else
            {
                for (int i = 0; i < _entities.Count; i++)
                {
                    if (GetRowRect(i).Contains(mouse.X, mouse.Y))
                    {
                        SelectedEntityId = _entities[i].Id;
                        break;
                    }
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

        spriteBatch.Draw(_pixel, new Rectangle(0, y, PaletteWidth, HeaderHeight), Color.DarkGreen * 0.5f);
        spriteBatch.DrawString(_font, "PALETTE", new Vector2(Padding, y + 7), Color.LimeGreen);
        y += HeaderHeight;

        DrawTab(spriteBatch, PaletteMode.Tiles, "Tiles [1]");
        DrawTab(spriteBatch, PaletteMode.Entities, "Objects [2]");
        y += TabHeight;

        var selectedTile = SelectedTileId != null ? _tiles.FirstOrDefault(t => t.Id == SelectedTileId) : null;
        var selectedEntity = SelectedEntityId != null ? _entities.FirstOrDefault(t => t.Id == SelectedEntityId) : null;

        spriteBatch.Draw(_pixel, new Rectangle(0, y, PaletteWidth, SelectedBlockHeight), Color.DarkGreen * 0.2f);
        spriteBatch.Draw(_pixel, new Rectangle(0, y, PaletteWidth, 1), Color.DarkGreen * 0.4f);

        if (Mode == PaletteMode.Tiles && selectedTile != null)
        {
            spriteBatch.DrawString(_font, "Selected:", new Vector2(Padding, y + 6), Color.Gray);

            var tex = GetTileTexture(selectedTile);
            var src = GetTileSourceRect(selectedTile);
            spriteBatch.Draw(tex, new Rectangle(Padding, y + 20, TileIconSize, TileIconSize), src, Color.White);

            spriteBatch.DrawString(_font, selectedTile.Name,
                new Vector2(Padding + TileIconSize + 8, y + 20), Color.White);
            spriteBatch.DrawString(_font,
                selectedTile.Solid ? "SOLID" : "passable",
                new Vector2(Padding + TileIconSize + 8, y + 34),
                selectedTile.Solid ? Color.Orange : Color.Gray);
        }
        else if (Mode == PaletteMode.Entities && selectedEntity != null)
        {
            spriteBatch.DrawString(_font, "Selected object:", new Vector2(Padding, y + 6), Color.Gray);

            var tex = GetEntityTexture(selectedEntity);
            var src = GetEntitySourceRect(selectedEntity);
            spriteBatch.Draw(tex, new Rectangle(Padding, y + 20, TileIconSize, TileIconSize), src, Color.White);

            spriteBatch.DrawString(_font, selectedEntity.Name,
                new Vector2(Padding + TileIconSize + 8, y + 20), Color.White);
            spriteBatch.DrawString(_font, selectedEntity.Id,
                new Vector2(Padding + TileIconSize + 8, y + 34), Color.Gray);
        }
        else
        {
            spriteBatch.DrawString(_font, "None", new Vector2(Padding, y + 18), Color.Gray);
        }

        y += SelectedBlockHeight;

        spriteBatch.Draw(_pixel, new Rectangle(0, y, PaletteWidth, 1), Color.DarkGreen * 0.4f);
        var hint = Mode == PaletteMode.Tiles ? "L=paint   R=erase" : "L=place   R=remove";
        spriteBatch.DrawString(_font, hint, new Vector2(Padding, y + 5), Color.Gray);
        y += HintHeight;
        spriteBatch.Draw(_pixel, new Rectangle(0, y, PaletteWidth, 1), Color.DarkGreen * 0.3f);
        y += 2;

        if (Mode == PaletteMode.Tiles && _tiles.Count == 0)
        {
            spriteBatch.DrawString(_font, "No tiles!", new Vector2(Padding, y + 10), Color.Red);
            return;
        }

        if (Mode == PaletteMode.Entities && _entities.Count == 0)
        {
            spriteBatch.DrawString(_font, "No entity prototypes!", new Vector2(Padding, y + 10), Color.Red);
            return;
        }

        var rowCount = Mode == PaletteMode.Tiles ? _tiles.Count : _entities.Count;
        for (int i = 0; i < rowCount; i++)
        {
            var rowRect = GetRowRect(i);
            var isSelected = false;
            Texture2D tex;
            Rectangle? src;
            string name;
            string subtext;

            if (Mode == PaletteMode.Tiles)
            {
                var tile = _tiles[i];
                isSelected = tile.Id == SelectedTileId;
                tex = GetTileTexture(tile);
                src = GetTileSourceRect(tile);
                name = tile.Name;
                subtext = tile.Solid ? "solid" : "pass";
            }
            else
            {
                var entity = _entities[i];
                isSelected = entity.Id == SelectedEntityId;
                tex = GetEntityTexture(entity);
                src = GetEntitySourceRect(entity);
                name = entity.Name;
                subtext = entity.Id;
            }

            if (isSelected)
                spriteBatch.Draw(_pixel, rowRect, Color.DarkGreen * 0.45f);

            var iconRect = new Rectangle(
                rowRect.X + Padding,
                rowRect.Y + (TileRowHeight - TileIconSize) / 2,
                TileIconSize, TileIconSize
            );

            spriteBatch.Draw(tex, iconRect, src, Color.White);

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
            spriteBatch.DrawString(_font, name, new Vector2(textX, textY),
                isSelected ? Color.White : Color.LightGray);

            spriteBatch.DrawString(_font,
                subtext,
                new Vector2(textX, textY + 16),
                Mode == PaletteMode.Tiles && subtext == "solid"
                    ? new Color(200, 120, 0)
                    : new Color(80, 120, 80));

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

    private Texture2D GetEntityTexture(EntityPrototype entity)
    {
        if (entity.SpritePath != null)
        {
            var tex = _assets.LoadFromFile(entity.SpritePath);
            if (tex != null) return tex;
        }

        return _assets.GetColorTexture(entity.PreviewColor);
    }

    private Rectangle? GetEntitySourceRect(EntityPrototype entity) => entity.PreviewSourceRect;

    private Rectangle GetRowRect(int index)
    {
        int startY = HeaderHeight + TabHeight + SelectedBlockHeight + HintHeight + 2;
        return new Rectangle(0, startY + index * TileRowHeight, PaletteWidth, TileRowHeight);
    }

    private void DrawTab(SpriteBatch spriteBatch, PaletteMode mode, string label)
    {
        var rect = GetTabRect(mode);
        var selected = Mode == mode;

        spriteBatch.Draw(_pixel, rect, selected ? Color.DarkGreen * 0.7f : Color.Black * 0.3f);
        spriteBatch.DrawString(_font, label, new Vector2(rect.X + 10, rect.Y + 6), selected ? Color.White : Color.Gray);
    }

    private Rectangle GetTabRect(PaletteMode mode)
    {
        var half = PaletteWidth / 2;
        return mode == PaletteMode.Tiles
            ? new Rectangle(0, HeaderHeight, half, TabHeight)
            : new Rectangle(half, HeaderHeight, PaletteWidth - half, TabHeight);
    }
}
