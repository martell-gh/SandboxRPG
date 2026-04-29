using System.Text.Json.Nodes;
using Microsoft.Xna.Framework;
using MTEngine.Components;
using MTEngine.Core;
using MTEngine.ECS;

namespace MTEngine.Npc;

/// <summary>
/// Снапшот NPC, который не существует как живая Entity в World
/// (типично — distant-зона по LOD-симуляции).
///
/// Components — JSON-данные, как они сохраняются через [SaveField] на компонентах.
/// Когда NPC попадает в Active/Background зону, снапшот разворачивается в Entity
/// через EntityFactory, а затем поверх прототипа применяются сохранённые поля.
/// </summary>
public class NpcSnapshot
{
    public string SaveId { get; set; } = "";
    public string PrototypeId { get; set; } = "npc_base";
    public string Name { get; set; } = "NPC";

    /// <summary>В какой карте NPC "обитает" (зашит в district/settlement).</summary>
    public string MapId { get; set; } = "";
    public float X { get; set; }
    public float Y { get; set; }

    /// <summary>typeId компонента -> JsonObject с его данными ([SaveField]-полями).</summary>
    public Dictionary<string, JsonObject> Components { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Хранилище снапшотов NPC, которых нет в World физически.
/// Умеет конвертировать Entity ↔ NpcSnapshot.
/// </summary>
[SaveObject("worldPopulation")]
public class WorldPopulationStore
{
    [SaveField("snapshots")]
    public Dictionary<string, NpcSnapshot> Snapshots { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public bool Has(string saveId) => Snapshots.ContainsKey(saveId);

    public NpcSnapshot? Get(string saveId)
        => Snapshots.TryGetValue(saveId, out var s) ? s : null;

    public void Put(NpcSnapshot snapshot)
    {
        if (string.IsNullOrEmpty(snapshot.SaveId))
            snapshot.SaveId = Guid.NewGuid().ToString("N");
        Snapshots[snapshot.SaveId] = snapshot;
    }

    public bool Remove(string saveId) => Snapshots.Remove(saveId);

    public IEnumerable<NpcSnapshot> InMap(string mapId)
        => Snapshots.Values.Where(s => string.Equals(s.MapId, mapId, StringComparison.OrdinalIgnoreCase));

    public NpcSnapshot Snapshot(Entity entity, string? mapId = null, bool store = true)
    {
        var saveMarker = entity.GetComponent<SaveEntityIdComponent>();
        if (saveMarker == null)
            saveMarker = entity.AddComponent(new SaveEntityIdComponent());

        var transform = entity.GetComponent<TransformComponent>();
        var existing = Get(saveMarker.SaveId);
        var resolvedMapId = !string.IsNullOrWhiteSpace(mapId)
            ? mapId!
            : existing?.MapId ?? "";

        var snapshot = new NpcSnapshot
        {
            SaveId = saveMarker.SaveId,
            PrototypeId = string.IsNullOrWhiteSpace(entity.PrototypeId) ? "npc_base" : entity.PrototypeId,
            Name = ResolveDisplayName(entity),
            MapId = resolvedMapId,
            X = transform?.Position.X ?? existing?.X ?? 0f,
            Y = transform?.Position.Y ?? existing?.Y ?? 0f
        };

        foreach (var component in entity.GetAllComponents())
        {
            if (component is SaveEntityIdComponent)
                continue;

            var componentType = component.GetType();
            if (!NpcSnapshotComponentSerializer.HasSerializableMembers(componentType))
                continue;

            var data = NpcSnapshotComponentSerializer.SerializeObject(component);
            if (data.Count == 0)
                continue;

            var typeId = NpcSnapshotComponentSerializer.ResolveComponentTypeId(componentType);
            snapshot.Components[typeId] = data;
        }

        if (store)
            Put(snapshot);

        return snapshot;
    }

    public NpcSnapshot SnapshotAndDespawn(Entity entity, string? mapId = null, bool flushWorld = true)
    {
        var snapshot = Snapshot(entity, mapId, store: true);
        entity.World?.DestroyEntity(entity);
        if (flushWorld)
            entity.World?.FlushEntityChanges();
        return snapshot;
    }

    public Entity? Live(string saveId, PrototypeManager prototypes, EntityFactory entityFactory, bool removeSnapshot = true)
    {
        var snapshot = Get(saveId);
        return snapshot == null
            ? null
            : Live(snapshot, prototypes, entityFactory, removeSnapshot);
    }

    public Entity? Live(NpcSnapshot snapshot, PrototypeManager prototypes, EntityFactory entityFactory, bool removeSnapshot = true)
    {
        if (string.IsNullOrWhiteSpace(snapshot.SaveId))
            snapshot.SaveId = Guid.NewGuid().ToString("N");

        var protoId = string.IsNullOrWhiteSpace(snapshot.PrototypeId) ? "npc_base" : snapshot.PrototypeId;
        var proto = prototypes.GetEntity(protoId);
        if (proto == null)
        {
            Console.WriteLine($"[WorldPopulationStore] Unknown NPC prototype: {protoId}");
            return null;
        }

        var entity = entityFactory.CreateFromPrototype(proto, new Vector2(snapshot.X, snapshot.Y));
        if (entity == null)
            return null;

        entity.Name = string.IsNullOrWhiteSpace(snapshot.Name) ? proto.Name : snapshot.Name;
        entity.PrototypeId = protoId;

        var saveMarker = entity.GetComponent<SaveEntityIdComponent>();
        if (saveMarker == null)
            entity.AddComponent(new SaveEntityIdComponent { SaveId = snapshot.SaveId });
        else
            saveMarker.SaveId = snapshot.SaveId;

        ApplySnapshotComponents(entity, snapshot, proto);
        EnsureTransformPosition(entity, snapshot.X, snapshot.Y);
        entityFactory.RefreshPresentationFromPrototype(entity, proto);

        // Спрайт оживлённого NPC должен соответствовать его полу.
        var identity = entity.GetComponent<IdentityComponent>();
        if (identity != null)
            entity.GetComponent<GenderedAppearanceComponent>()?.ApplyForGender(identity.Gender);

        if (removeSnapshot)
            Remove(snapshot.SaveId);

        return entity;
    }

    private static void ApplySnapshotComponents(Entity entity, NpcSnapshot snapshot, EntityPrototype proto)
    {
        foreach (var (typeId, data) in snapshot.Components)
        {
            var componentType = ComponentRegistry.GetComponentType(typeId);
            if (componentType == null)
            {
                Console.WriteLine($"[WorldPopulationStore] Unknown snapshot component: {typeId}");
                continue;
            }

            var component = entity.GetComponent(componentType);
            if (component == null)
            {
                component = CreateComponentFromPrototypeOrSnapshot(componentType, typeId, proto);
                entity.AddComponent(component);
            }

            NpcSnapshotComponentSerializer.ApplyObject(component, data);
        }
    }

    private static Component CreateComponentFromPrototypeOrSnapshot(Type componentType, string typeId, EntityPrototype proto)
    {
        if (proto.Components != null
            && proto.Components.TryGetPropertyValue(typeId, out var componentNode)
            && componentNode is JsonObject componentData)
        {
            return ComponentPrototypeSerializer.Deserialize(componentType, componentData);
        }

        return NpcSnapshotComponentSerializer.CreateComponent(componentType);
    }

    private static void EnsureTransformPosition(Entity entity, float x, float y)
    {
        var transform = entity.GetComponent<TransformComponent>();
        if (transform == null)
        {
            entity.AddComponent(new TransformComponent(x, y));
            return;
        }

        transform.Position = new Vector2(x, y);
    }

    private static string ResolveDisplayName(Entity entity)
    {
        var identity = entity.GetComponent<IdentityComponent>();
        if (identity != null && !string.IsNullOrWhiteSpace(identity.FullName))
            return identity.FullName;

        return string.IsNullOrWhiteSpace(entity.Name) ? "NPC" : entity.Name;
    }
}
