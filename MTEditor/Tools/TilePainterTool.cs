using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using MTEngine.Core;
using MTEngine.World;

namespace MTEditor.Tools;

public class TilePainterTool
{
    private MapData _map;
    private TileMap _tileMap;
    private readonly PrototypeManager _prototypes;
    private readonly AssetManager _assets;
    private readonly EditorHistory _history;

    private bool _wasPaintingLeft = false;
    private bool _wasPaintingRight = false;

    public TilePainterTool(MapData map, TileMap tileMap, PrototypeManager prototypes,
                            AssetManager assets, EditorHistory history)
    {
        _map = map;
        _tileMap = tileMap;
        _prototypes = prototypes;
        _assets = assets;
        _history = history;
    }

    public void SetMap(MapData map, TileMap tileMap)
    {
        _map = map;
        _tileMap = tileMap;
    }

    public void Update(MouseState mouse, MouseState prev, Vector2 worldPos, string? selectedTileId)
    {
        var tileX = (int)Math.Floor(worldPos.X / _map.TileSize);
        var tileY = (int)Math.Floor(worldPos.Y / _map.TileSize);

        bool inBounds = tileX >= 0 && tileX < _map.Width && tileY >= 0 && tileY < _map.Height;

        bool isLeft = mouse.LeftButton == ButtonState.Pressed;
        bool isRight = mouse.RightButton == ButtonState.Pressed;

        // начало левого рисования
        if (isLeft && !_wasPaintingLeft)
            _history.BeginBatch();

        // начало правого стирания
        if (isRight && !_wasPaintingRight)
            _history.BeginBatch();

        if (inBounds)
        {
            // рисуем тайл
            if (isLeft && selectedTileId != null)
            {
                var proto = _prototypes.GetTile(selectedTileId);
                if (proto != null)
                {
                    var oldTile = _tileMap.GetTile(tileX, tileY);
                    var newTile = new Tile
                    {
                        ProtoId = selectedTileId,
                        Solid = proto.Solid,
                        Transparent = proto.Transparent,
                        Type = proto.Solid ? TileType.Wall : TileType.Floor
                    };

                    // записываем только если тайл реально изменился
                    if (oldTile.ProtoId != newTile.ProtoId)
                    {
                        _history.Record(tileX, tileY, oldTile, newTile);
                        _tileMap.SetTile(tileX, tileY, newTile);
                    }
                }
            }

            // стираем тайл
            if (isRight)
            {
                var oldTile = _tileMap.GetTile(tileX, tileY);
                if (oldTile.Type != TileType.Empty)
                {
                    _history.Record(tileX, tileY, oldTile, Tile.Empty);
                    _tileMap.SetTile(tileX, tileY, Tile.Empty);
                }
            }
        }

        // конец левого рисования — коммитим батч
        if (!isLeft && _wasPaintingLeft)
            _history.CommitBatch();

        // конец правого стирания — коммитим батч
        if (!isRight && _wasPaintingRight)
            _history.CommitBatch();

        _wasPaintingLeft = isLeft;
        _wasPaintingRight = isRight;
    }
}