using System;
using System.Linq;
using Microsoft.Xna.Framework;
using MTEngine.Core;
using MTEngine.ECS;
using MTEngine.World;

namespace SandboxGame.Game;

public class MapEntitySpawner
{
    private readonly PrototypeManager _prototypes;
    private readonly EntityFactory _entityFactory;
    private readonly World _world;

    public MapEntitySpawner(PrototypeManager prototypes, EntityFactory entityFactory, World world, EventBus eventBus)
    {
        _prototypes = prototypes;
        _entityFactory = entityFactory;
        _world = world;
        eventBus.Subscribe<MapLoadedEvent>(OnMapLoaded);
    }

    private void OnMapLoaded(MapLoadedEvent eventData)
    {
        foreach (var entity in _world.GetEntitiesWith<MapEntityTagComponent>().ToList())
            _world.DestroyEntity(entity);

        foreach (var entry in eventData.Map.Entities)
        {
            var proto = _prototypes.GetEntity(entry.ProtoId);
            if (proto == null)
            {
                Console.WriteLine($"[MapEntitySpawner] Unknown entity proto: {entry.ProtoId}");
                continue;
            }

            var position = GetEntryWorldPosition(entry, eventData.Map);

            var entity = _entityFactory.CreateFromPrototype(proto, position);
            if (entity != null)
                entity.AddComponent(new MapEntityTagComponent());
        }
    }

    private static Vector2 GetEntryWorldPosition(MapEntityData entry, MapData map)
    {
        if (entry.WorldSpace)
            return new Vector2(entry.X, entry.Y);

        // Backward compatibility: old maps stored entity coordinates in tile space.
        if (entry.X >= 0 && entry.Y >= 0 && entry.X <= map.Width && entry.Y <= map.Height)
        {
            return new Vector2(
                (entry.X + 0.5f) * map.TileSize,
                (entry.Y + 0.5f) * map.TileSize);
        }

        return new Vector2(entry.X, entry.Y);
    }
}
