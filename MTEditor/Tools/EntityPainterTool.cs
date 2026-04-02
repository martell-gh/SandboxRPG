using System;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using MTEngine.World;

namespace MTEditor.Tools;

public class EntityPainterTool
{
    private MapData _map;

    public EntityPainterTool(MapData map)
    {
        _map = map;
    }

    public void SetMap(MapData map)
    {
        _map = map;
    }

    public void Update(MouseState mouse, MouseState prev, Vector2 worldPos, string selectedEntityId)
    {
        var tileX = (int)Math.Floor(worldPos.X / _map.TileSize);
        var tileY = (int)Math.Floor(worldPos.Y / _map.TileSize);

        var inBounds = tileX >= 0 && tileX < _map.Width && tileY >= 0 && tileY < _map.Height;
        if (!inBounds)
            return;

        if (mouse.LeftButton == ButtonState.Pressed && prev.LeftButton == ButtonState.Released)
        {
            _map.Entities.RemoveAll(entity => entity.X == tileX && entity.Y == tileY);
            _map.Entities.Add(new MapEntityData
            {
                X = tileX,
                Y = tileY,
                ProtoId = selectedEntityId
            });
        }

        if (mouse.RightButton == ButtonState.Pressed && prev.RightButton == ButtonState.Released)
        {
            _map.Entities.RemoveAll(entity => entity.X == tileX && entity.Y == tileY);
        }
    }
}
