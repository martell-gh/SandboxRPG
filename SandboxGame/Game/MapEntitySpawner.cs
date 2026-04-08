using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Microsoft.Xna.Framework;
using MTEngine.Core;
using MTEngine.ECS;
using MTEngine.Items;
using MTEngine.World;
using SandboxGame.Save;

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

        if (ServiceLocator.Has<SaveGameManager>())
        {
            var saveManager = ServiceLocator.Get<SaveGameManager>();
            var savedEntities = saveManager.GetMapEntityStates(eventData.Map.Id);
            if (savedEntities != null)
            {
                saveManager.RestoreMapEntities(eventData.Map.Id);
                return;
            }
        }

        foreach (var entry in eventData.Map.Entities)
            SpawnMapEntity(entry, eventData.Map, null);
    }

    private Entity? SpawnMapEntity(MapEntityData entry, MapData map, StorageComponent? parentStorage)
    {
        var proto = _prototypes.GetEntity(entry.ProtoId);
        if (proto == null)
        {
            Console.WriteLine($"[MapEntitySpawner] Unknown entity proto: {entry.ProtoId}");
            return null;
        }

        var position = GetEntryWorldPosition(entry, map);
        var entity = _entityFactory.CreateFromPrototype(proto, position);
        if (entity == null)
            return null;

        entity.AddComponent(new MapEntityTagComponent());
        ApplyComponentOverrides(entity, entry.ComponentOverrides);

        if (parentStorage != null && !parentStorage.TryInsert(entity))
            Console.WriteLine($"[MapEntitySpawner] Failed to insert '{entry.ProtoId}' into storage '{parentStorage.StorageName}'.");

        var storage = entity.GetComponent<StorageComponent>();
        if (storage != null)
        {
            foreach (var containedEntry in entry.ContainedEntities)
                SpawnMapEntity(containedEntry, map, storage);
        }

        return entity;
    }

    private static void ApplyComponentOverrides(Entity entity, IReadOnlyDictionary<string, JsonObject> overrides)
    {
        foreach (var pair in overrides)
        {
            var componentType = ComponentRegistry.GetComponentType(pair.Key);
            if (componentType == null)
                continue;

            var component = entity.GetComponent(componentType);
            if (component == null)
                continue;

            ComponentPrototypeSerializer.ApplyData(component, pair.Value);
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
