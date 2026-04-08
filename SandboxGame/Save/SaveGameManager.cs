#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Xna.Framework;
using MTEngine.Components;
using MTEngine.Core;
using MTEngine.ECS;
using MTEngine.Items;
using MTEngine.Rendering;
using MTEngine.World;
using SandboxGame.Game;

namespace SandboxGame.Save;

public class SaveGameManager : IMapStateSource, IWorldStateTracker
{
    private readonly World _world;
    private readonly MapManager _mapManager;
    private readonly PrototypeManager _prototypes;
    private readonly AssetManager _assets;
    private readonly GameClock _clock;
    private readonly Dictionary<string, object> _registeredSaveObjects = new(StringComparer.OrdinalIgnoreCase);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        IncludeFields = true,
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public SaveSessionData? ActiveSession { get; private set; }
    public int? ActiveSlotIndex { get; private set; }
    public bool HasActiveSession => ActiveSession != null;
    public bool HasLoadedSave => ActiveSlotIndex.HasValue;
    public bool HasUnsavedChanges { get; private set; }

    private string SaveDirectory => Path.Combine(AppContext.BaseDirectory, "Saves");

    public SaveGameManager(World world, MapManager mapManager, PrototypeManager prototypes, AssetManager assets, GameClock clock)
    {
        _world = world;
        _mapManager = mapManager;
        _prototypes = prototypes;
        _assets = assets;
        _clock = clock;
        RegisterSaveObject(clock);
        Directory.CreateDirectory(SaveDirectory);
    }

    public void RegisterSaveObject(object instance)
    {
        if (!SaveComponentSerializer.HasSerializableMembers(instance))
            return;

        _registeredSaveObjects[SaveComponentSerializer.ResolveObjectId(instance)] = instance;
    }

    public void StartNewGame()
    {
        _clock.TimeScale = 72f;
        _clock.SetTime(8f);

        ActiveSession = new SaveSessionData
        {
            Version = 1,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            SaveName = "Новая игра",
            PlayerName = "Player",
            TimeOfDaySeconds = _clock.TotalSeconds,
            TimeScale = _clock.TimeScale
        };
        ActiveSlotIndex = null;
        HasUnsavedChanges = true;
    }

    public IReadOnlyList<SaveSlotSummary> GetSlotSummaries(int slotCount = 5)
    {
        var list = new List<SaveSlotSummary>(slotCount);
        for (var i = 1; i <= slotCount; i++)
        {
            if (!SlotHasData(i))
            {
                list.Add(new SaveSlotSummary
                {
                    SlotIndex = i,
                    HasData = false,
                    Title = $"Слот {i}",
                    Description = "Пустой слот"
                });
                continue;
            }

            try
            {
                var save = ReadSlotSummaryData(i);
                var updated = save?.UpdatedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "unknown";
                var mapName = !string.IsNullOrWhiteSpace(save?.CurrentMapName) ? save!.CurrentMapName : save?.CurrentMapId ?? "unknown map";
                var playerName = string.IsNullOrWhiteSpace(save?.PlayerName) ? "Player" : save!.PlayerName;
                var saveName = string.IsNullOrWhiteSpace(save?.SaveName) ? $"Слот {i}" : save!.SaveName;
                list.Add(new SaveSlotSummary
                {
                    SlotIndex = i,
                    HasData = true,
                    Title = $"{saveName}",
                    Description = $"{playerName} - {mapName} - {updated}"
                });
            }
            catch
            {
                list.Add(new SaveSlotSummary
                {
                    SlotIndex = i,
                    HasData = true,
                    Title = $"Слот {i}: повреждён",
                    Description = "Файл сохранения не удалось прочитать"
                });
            }
        }

        return list;
    }

    public bool SlotHasData(int slotIndex)
        => File.Exists(GetSlotManifestPath(slotIndex)) || File.Exists(GetLegacySlotPath(slotIndex));

    public bool SaveToSlot(int slotIndex, string? saveName = null)
    {
        EnsureSession();
        CaptureCurrentMapState();
        ActiveSession!.UpdatedAtUtc = DateTime.UtcNow;
        ActiveSession.CurrentMapId = _mapManager.CurrentMap?.Id ?? ActiveSession.CurrentMapId;
        ActiveSession.CurrentMapName = _mapManager.CurrentMap?.Name ?? ActiveSession.CurrentMapName;
        ActiveSession.TimeOfDaySeconds = _clock.TotalSeconds;
        ActiveSession.TimeScale = _clock.TimeScale;
        CaptureSaveObjectStates();
        ActiveSession.PlayerEntities = CapturePlayerEntityTree();
        if (!string.IsNullOrWhiteSpace(saveName))
            ActiveSession.SaveName = saveName.Trim();
        if (string.IsNullOrWhiteSpace(ActiveSession.SaveName))
            ActiveSession.SaveName = $"Слот {slotIndex}";
        ActiveSession.PlayerName = GetPlayerEntity()?.Name ?? ActiveSession.PlayerName;

        WriteSlotData(slotIndex, ActiveSession!);
        ActiveSlotIndex = slotIndex;
        HasUnsavedChanges = false;
        return true;
    }

    public string GetSuggestedSaveName(int slotIndex)
    {
        if (!string.IsNullOrWhiteSpace(ActiveSession?.SaveName))
            return ActiveSession!.SaveName;

        try
        {
            var save = ReadSlotSummaryData(slotIndex);
            if (!string.IsNullOrWhiteSpace(save?.SaveName))
                return save!.SaveName;
        }
        catch
        {
            // fall through to default slot name
        }

        return $"Слот {slotIndex}";
    }

    public bool LoadFromSlot(int slotIndex)
    {
        var session = ReadSlotData(slotIndex);
        if (session == null)
            return false;

        ActiveSession = session;
        ActiveSlotIndex = slotIndex;
        HasUnsavedChanges = false;
        return true;
    }

    public void ApplyClockState()
    {
        if (ActiveSession == null)
            return;

        ApplyRegisteredObjectStates();

        if (ActiveSession.SystemStates.ContainsKey("gameClock"))
            return;

        _clock.TimeScale = ActiveSession.TimeScale > 0f ? ActiveSession.TimeScale : 72f;
        _clock.SetTime(ActiveSession.TimeOfDaySeconds / 3600f);
    }

    public void ApplyRegisteredObjectStates()
    {
        if (ActiveSession == null)
            return;

        foreach (var saveObject in EnumerateSaveObjects())
        {
            var objectId = SaveComponentSerializer.ResolveObjectId(saveObject);
            if (ActiveSession.SystemStates.TryGetValue(objectId, out var state))
                SaveComponentSerializer.ApplyObject(saveObject, state);
        }
    }

    public bool RenameSlot(int slotIndex, string newName)
    {
        var trimmedName = newName.Trim();
        if (string.IsNullOrWhiteSpace(trimmedName))
            return false;

        var session = ReadSlotData(slotIndex);
        if (session == null)
            return false;

        session.SaveName = trimmedName;
        session.UpdatedAtUtc = DateTime.UtcNow;
        WriteSlotData(slotIndex, session);

        if (ActiveSlotIndex == slotIndex && ActiveSession != null)
            ActiveSession.SaveName = trimmedName;

        return true;
    }

    public bool DeleteSlot(int slotIndex)
    {
        var deleted = false;
        var slotDirectory = GetSlotDirectory(slotIndex);
        if (Directory.Exists(slotDirectory))
        {
            Directory.Delete(slotDirectory, recursive: true);
            deleted = true;
        }

        var legacyPath = GetLegacySlotPath(slotIndex);
        if (File.Exists(legacyPath))
        {
            File.Delete(legacyPath);
            deleted = true;
        }

        if (deleted && ActiveSlotIndex == slotIndex)
            ActiveSlotIndex = null;

        return deleted;
    }

    public void EnsureMapInstance(MapData map)
    {
        if (ActiveSession == null)
            return;

        if (!ActiveSession.Maps.ContainsKey(map.Id))
        {
            ActiveSession.Maps[map.Id] = new MapRuntimeStateData
            {
                Map = CloneMap(map),
                HasCapturedState = false,
                Entities = new List<EntitySaveData>()
            };
        }
    }

    public MapData? GetMapOverride(string mapId)
    {
        if (ActiveSession == null)
            return null;

        return ActiveSession.Maps.TryGetValue(mapId, out var state) && state.HasCapturedState
            ? CloneMap(state.Map)
            : null;
    }

    public IReadOnlyList<EntitySaveData>? GetMapEntityStates(string mapId)
    {
        if (ActiveSession == null)
            return null;

        return ActiveSession.Maps.TryGetValue(mapId, out var state) && state.HasCapturedState
            ? state.Entities
            : null;
    }

    public void CaptureCurrentMapState()
    {
        if (ActiveSession == null || _mapManager.CurrentMap == null)
            return;

        var mapId = _mapManager.CurrentMap.Id;
        var state = ActiveSession.Maps.GetValueOrDefault(mapId) ?? new MapRuntimeStateData();
        state.Map = CloneMap(_mapManager.CurrentMap);
        state.HasCapturedState = true;
        state.Entities = CaptureMapEntities().ToList();
        ActiveSession.Maps[mapId] = state;
        ActiveSession.CurrentMapId = mapId;
        ActiveSession.CurrentMapName = _mapManager.CurrentMap.Name;
        ActiveSession.UpdatedAtUtc = DateTime.UtcNow;
    }

    public void MarkDirty()
    {
        if (ActiveSession == null)
            return;

        HasUnsavedChanges = true;
        ActiveSession.UpdatedAtUtc = DateTime.UtcNow;
    }

    public void RestorePlayerEntities()
    {
        if (ActiveSession == null)
            return;

        var currentEntities = _world.GetEntities().ToList();
        foreach (var entity in currentEntities)
            _world.DestroyEntity(entity);
        _world.Update(0f);

        RestoreEntities(ActiveSession.PlayerEntities, markAsMapEntities: false);
        _world.Update(0f);
    }

    public void RestoreMapEntities(string mapId)
    {
        if (ActiveSession == null)
            return;

        if (!ActiveSession.Maps.TryGetValue(mapId, out var state))
            return;

        RestoreEntities(state.Entities, markAsMapEntities: true);
    }

    public EntitySaveData? GetPlayerRootState()
        => ActiveSession?.PlayerEntities.FirstOrDefault(s => s.Components.Any(c => c.TypeId == "playertag" || c.ClrType?.Contains("PlayerTagComponent") == true));

    private IEnumerable<EntitySaveData> CaptureMapEntities()
    {
        var player = GetPlayerEntity();
        foreach (var entity in _world.GetEntities().Where(e => e.Active || e.GetComponent<ItemComponent>() != null))
        {
            if (player != null && BelongsToPlayer(entity, player))
                continue;

            if (entity.HasComponent<PlayerTagComponent>())
                continue;

            yield return CaptureEntity(entity);
        }
    }

    private List<EntitySaveData> CapturePlayerEntityTree()
    {
        var player = GetPlayerEntity();
        if (player == null)
            return new List<EntitySaveData>();

        var entities = _world.GetEntities().Where(e => BelongsToPlayer(e, player)).ToList();
        if (!entities.Contains(player))
            entities.Insert(0, player);

        return entities
            .Distinct()
            .Select(CaptureEntity)
            .ToList();
    }

    private EntitySaveData CaptureEntity(Entity entity)
    {
        var save = new EntitySaveData
        {
            SaveId = GetOrCreateSaveId(entity),
            PrototypeId = entity.PrototypeId,
            Name = entity.Name,
            Active = entity.Active,
            Components = entity.GetAllComponents().Select(SaveComponentSerializer.Serialize).ToList()
        };

        if (entity.GetComponent<ItemComponent>() is { } item)
        {
            save.ItemRefs = new ItemReferenceState
            {
                ContainedInEntityId = item.ContainedIn != null ? GetOrCreateSaveId(item.ContainedIn) : null
            };
        }

        if (entity.GetComponent<HandsComponent>() is { } hands)
        {
            save.HandsRefs = new HandsReferenceState
            {
                ActiveHandIndex = hands.ActiveHandIndex,
                Hands = hands.Hands.Select(hand => new HandReferenceData
                {
                    Name = hand.Name,
                    HeldEntityId = hand.HeldItem != null ? GetOrCreateSaveId(hand.HeldItem) : null,
                    BlockedByTwoHanded = hand.BlockedByTwoHanded
                }).ToList()
            };
        }

        if (entity.GetComponent<EquipmentComponent>() is { } equipment)
        {
            save.EquipmentRefs = new EquipmentReferenceState
            {
                Slots = equipment.Slots.Select(slot => new EquipmentSlotReferenceData
                {
                    SlotId = slot.Id,
                    ItemEntityId = slot.Item != null ? GetOrCreateSaveId(slot.Item) : null
                }).ToList()
            };
        }

        if (entity.GetComponent<StorageComponent>() is { } storage)
        {
            save.StorageRefs = new StorageReferenceState
            {
                ContentEntityIds = storage.Contents.Select(GetOrCreateSaveId).ToList()
            };
        }

        return save;
    }

    private void RestoreEntities(IEnumerable<EntitySaveData> saves, bool markAsMapEntities)
    {
        var list = saves.ToList();
        var map = new Dictionary<string, Entity>(StringComparer.OrdinalIgnoreCase);

        foreach (var save in list)
        {
            var entity = _world.CreateEntity(save.Name);
            entity.Name = save.Name;
            entity.PrototypeId = save.PrototypeId;
            entity.Active = save.Active;
            entity.AddComponent(new SaveEntityIdComponent { SaveId = save.SaveId });

            var proto = !string.IsNullOrWhiteSpace(save.PrototypeId)
                ? _prototypes.GetEntity(save.PrototypeId)
                : null;

            foreach (var componentSave in save.Components)
            {
                var component = CreateComponentFromPrototypeOrSave(componentSave, proto);
                entity.AddComponent(component);

                if (component is IPrototypeInitializable initializable && proto != null)
                    initializable.InitializeFromPrototype(proto, _assets);

                SaveComponentSerializer.Apply(component, componentSave.Data);
            }

            if (proto != null)
                RefreshVisualComponents(entity, proto);

            if (markAsMapEntities && !entity.HasComponent<MapEntityTagComponent>())
                entity.AddComponent(new MapEntityTagComponent());

            map[save.SaveId] = entity;
        }

        _world.Update(0f);

        foreach (var save in list)
        {
            if (!map.TryGetValue(save.SaveId, out var entity))
                continue;

            if (save.ItemRefs != null && entity.GetComponent<ItemComponent>() is { } item)
            {
                item.ContainedIn = save.ItemRefs.ContainedInEntityId != null && map.TryGetValue(save.ItemRefs.ContainedInEntityId, out var container)
                    ? container
                    : null;
            }

            if (save.HandsRefs != null && entity.GetComponent<HandsComponent>() is { } hands)
            {
                hands.ActiveHandIndex = Math.Clamp(save.HandsRefs.ActiveHandIndex, 0, Math.Max(0, hands.Hands.Count - 1));
                for (var i = 0; i < hands.Hands.Count && i < save.HandsRefs.Hands.Count; i++)
                {
                    var hand = hands.Hands[i];
                    var handSave = save.HandsRefs.Hands[i];
                    hand.Name = handSave.Name;
                    hand.BlockedByTwoHanded = handSave.BlockedByTwoHanded;
                    hand.HeldItem = handSave.HeldEntityId != null && map.TryGetValue(handSave.HeldEntityId, out var held)
                        ? held
                        : null;
                }
            }

            if (save.EquipmentRefs != null && entity.GetComponent<EquipmentComponent>() is { } equipment)
            {
                foreach (var slotSave in save.EquipmentRefs.Slots)
                {
                    var slot = equipment.GetSlot(slotSave.SlotId);
                    if (slot == null)
                        continue;

                    slot.Item = slotSave.ItemEntityId != null && map.TryGetValue(slotSave.ItemEntityId, out var equipped)
                        ? equipped
                        : null;
                }
            }

            if (save.StorageRefs != null && entity.GetComponent<StorageComponent>() is { } storage)
            {
                storage.Contents.Clear();
                foreach (var contentId in save.StorageRefs.ContentEntityIds)
                {
                    if (map.TryGetValue(contentId, out var contentEntity))
                        storage.Contents.Add(contentEntity);
                }
            }
        }
    }

    private Entity? GetPlayerEntity()
        => _world.GetEntitiesWith<PlayerTagComponent>().FirstOrDefault();

    private bool BelongsToPlayer(Entity entity, Entity player)
    {
        if (entity == player)
            return true;

        var item = entity.GetComponent<ItemComponent>();
        if (item?.ContainedIn == null)
            return false;

        Entity? current = item.ContainedIn;
        while (current != null)
        {
            if (current == player)
                return true;

            current = current.GetComponent<ItemComponent>()?.ContainedIn;
        }

        return false;
    }

    private static string GetOrCreateSaveId(Entity entity)
    {
        var marker = entity.GetComponent<SaveEntityIdComponent>();
        if (marker != null)
            return marker.SaveId;

        marker = entity.AddComponent(new SaveEntityIdComponent());
        return marker.SaveId;
    }

    private static MapData CloneMap(MapData map)
    {
        var json = JsonSerializer.Serialize(map, JsonOptions);
        return JsonSerializer.Deserialize<MapData>(json, JsonOptions) ?? new MapData();
    }

    private void RefreshVisualComponents(Entity entity, EntityPrototype proto)
    {
        entity.GetComponent<SpriteComponent>()?.InitializeFromPrototype(proto, _assets);
        entity.GetComponent<WearableComponent>()?.InitializeFromPrototype(proto, _assets);
    }

    private static Component CreateComponentFromPrototypeOrSave(ComponentSaveData save, EntityPrototype? proto)
    {
        if (proto?.Components != null
            && proto.Components.TryGetPropertyValue(save.TypeId, out var componentNode)
            && componentNode is JsonObject componentData)
        {
            var componentType = ComponentRegistry.GetComponentType(save.TypeId);
            if (componentType != null)
                return ComponentPrototypeSerializer.Deserialize(componentType, componentData);
        }

        return SaveComponentSerializer.CreateComponent(save);
    }

    private void CaptureSaveObjectStates()
    {
        if (ActiveSession == null)
            return;

        ActiveSession.SystemStates.Clear();
        foreach (var saveObject in EnumerateSaveObjects())
            ActiveSession.SystemStates[SaveComponentSerializer.ResolveObjectId(saveObject)] =
                SaveComponentSerializer.SerializeObject(saveObject);
    }

    private IEnumerable<object> EnumerateSaveObjects()
    {
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var system in _world.GetSystems())
        {
            if (!SaveComponentSerializer.HasSerializableMembers(system))
                continue;

            var objectId = SaveComponentSerializer.ResolveObjectId(system);
            if (seenIds.Add(objectId))
                yield return system;
        }

        foreach (var (objectId, saveObject) in _registeredSaveObjects)
        {
            if (seenIds.Add(objectId))
                yield return saveObject;
        }
    }

    private void EnsureSession()
    {
        if (ActiveSession == null)
            StartNewGame();
    }

    private SaveManifestData? ReadSlotSummaryData(int slotIndex)
    {
        var manifestPath = GetSlotManifestPath(slotIndex);
        if (File.Exists(manifestPath))
        {
            var json = File.ReadAllText(manifestPath);
            return JsonSerializer.Deserialize<SaveManifestData>(json, JsonOptions);
        }

        var legacyPath = GetLegacySlotPath(slotIndex);
        if (!File.Exists(legacyPath))
            return null;

        var legacyJson = File.ReadAllText(legacyPath);
        var legacy = JsonSerializer.Deserialize<SaveSessionData>(legacyJson, JsonOptions);
        if (legacy == null)
            return null;

        return CreateManifest(legacy);
    }

    private SaveSessionData? ReadSlotData(int slotIndex)
    {
        var manifestPath = GetSlotManifestPath(slotIndex);
        if (File.Exists(manifestPath))
        {
            var manifestJson = File.ReadAllText(manifestPath);
            var manifest = JsonSerializer.Deserialize<SaveManifestData>(manifestJson, JsonOptions);
            if (manifest == null)
                return null;

            var session = new SaveSessionData
            {
                Version = manifest.Version,
                CreatedAtUtc = manifest.CreatedAtUtc,
                UpdatedAtUtc = manifest.UpdatedAtUtc,
                SaveName = manifest.SaveName,
                PlayerName = manifest.PlayerName,
                CurrentMapName = manifest.CurrentMapName,
                CurrentMapId = manifest.CurrentMapId,
                TimeOfDaySeconds = manifest.TimeOfDaySeconds,
                TimeScale = manifest.TimeScale,
                SystemStates = manifest.SystemStates,
                PlayerEntities = manifest.PlayerEntities
            };

            foreach (var mapId in manifest.SavedMapIds.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var mapStatePath = GetMapStatePath(slotIndex, mapId);
                if (!File.Exists(mapStatePath))
                    continue;

                var mapJson = File.ReadAllText(mapStatePath);
                var state = JsonSerializer.Deserialize<MapRuntimeStateData>(mapJson, JsonOptions);
                if (state != null)
                    session.Maps[mapId] = state;
            }

            return session;
        }

        var legacyPath = GetLegacySlotPath(slotIndex);
        if (!File.Exists(legacyPath))
            return null;

        var legacyJson = File.ReadAllText(legacyPath);
        return JsonSerializer.Deserialize<SaveSessionData>(legacyJson, JsonOptions);
    }

    private void WriteSlotData(int slotIndex, SaveSessionData session)
    {
        var slotDirectory = GetSlotDirectory(slotIndex);
        var mapsDirectory = GetSlotMapsDirectory(slotIndex);
        Directory.CreateDirectory(slotDirectory);
        Directory.CreateDirectory(mapsDirectory);

        var manifest = CreateManifest(session);
        File.WriteAllText(GetSlotManifestPath(slotIndex), JsonSerializer.Serialize(manifest, JsonOptions));

        var savedMapIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (mapId, state) in session.Maps)
        {
            savedMapIds.Add(mapId);
            File.WriteAllText(GetMapStatePath(slotIndex, mapId), JsonSerializer.Serialize(state, JsonOptions));
        }

        foreach (var stalePath in Directory.GetFiles(mapsDirectory, "*.json"))
        {
            var mapId = Path.GetFileNameWithoutExtension(stalePath);
            if (!savedMapIds.Contains(mapId))
                File.Delete(stalePath);
        }
    }

    private static SaveManifestData CreateManifest(SaveSessionData session)
    {
        return new SaveManifestData
        {
            Version = session.Version,
            CreatedAtUtc = session.CreatedAtUtc,
            UpdatedAtUtc = session.UpdatedAtUtc,
            SaveName = session.SaveName,
            PlayerName = session.PlayerName,
            CurrentMapName = session.CurrentMapName,
            CurrentMapId = session.CurrentMapId,
            TimeOfDaySeconds = session.TimeOfDaySeconds,
            TimeScale = session.TimeScale,
            SystemStates = session.SystemStates,
            PlayerEntities = session.PlayerEntities,
            SavedMapIds = session.Maps.Keys.OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToList()
        };
    }

    private string GetSlotDirectory(int slotIndex)
        => Path.Combine(SaveDirectory, $"slot_{slotIndex}");

    private string GetSlotManifestPath(int slotIndex)
        => Path.Combine(GetSlotDirectory(slotIndex), "manifest.json");

    private string GetSlotMapsDirectory(int slotIndex)
        => Path.Combine(GetSlotDirectory(slotIndex), "maps");

    private string GetMapStatePath(int slotIndex, string mapId)
        => Path.Combine(GetSlotMapsDirectory(slotIndex), $"{mapId}.json");

    private string GetLegacySlotPath(int slotIndex)
        => Path.Combine(SaveDirectory, $"slot_{slotIndex}.json");
}

public class SaveEntityIdComponent : Component
{
    public string SaveId { get; set; } = Guid.NewGuid().ToString("N");
}
