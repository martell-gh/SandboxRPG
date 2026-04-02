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

            var position = new Vector2(
                (entry.X + 0.5f) * eventData.Map.TileSize,
                (entry.Y + 0.5f) * eventData.Map.TileSize
            );

            var entity = _entityFactory.CreateFromPrototype(proto, position);
            if (entity != null)
                entity.AddComponent(new MapEntityTagComponent());
        }
    }
}
