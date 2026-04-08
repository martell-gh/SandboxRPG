#nullable enable
using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using MTEngine.World;

namespace SandboxGame.Save;

public class SaveSlotSummary
{
    public int SlotIndex { get; set; }
    public bool HasData { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
}

public class SaveSessionData
{
    public int Version { get; set; } = 1;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public string SaveName { get; set; } = "";
    public string PlayerName { get; set; } = "";
    public string CurrentMapName { get; set; } = "";
    public string CurrentMapId { get; set; } = "";
    public float TimeOfDaySeconds { get; set; } = 8f * 3600f;
    public float TimeScale { get; set; } = 72f;
    public Dictionary<string, JsonObject> SystemStates { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<EntitySaveData> PlayerEntities { get; set; } = new();
    public Dictionary<string, MapRuntimeStateData> Maps { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public class SaveManifestData
{
    public int Version { get; set; } = 1;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public string SaveName { get; set; } = "";
    public string PlayerName { get; set; } = "";
    public string CurrentMapName { get; set; } = "";
    public string CurrentMapId { get; set; } = "";
    public float TimeOfDaySeconds { get; set; } = 8f * 3600f;
    public float TimeScale { get; set; } = 72f;
    public Dictionary<string, JsonObject> SystemStates { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<EntitySaveData> PlayerEntities { get; set; } = new();
    public List<string> SavedMapIds { get; set; } = new();
}

public class MapRuntimeStateData
{
    public MapData Map { get; set; } = new();
    public bool HasCapturedState { get; set; }
    public List<EntitySaveData> Entities { get; set; } = new();
}

public class EntitySaveData
{
    public string SaveId { get; set; } = Guid.NewGuid().ToString("N");
    public string PrototypeId { get; set; } = "";
    public string Name { get; set; } = "Entity";
    public bool Active { get; set; } = true;
    public List<ComponentSaveData> Components { get; set; } = new();
    public ItemReferenceState? ItemRefs { get; set; }
    public HandsReferenceState? HandsRefs { get; set; }
    public EquipmentReferenceState? EquipmentRefs { get; set; }
    public StorageReferenceState? StorageRefs { get; set; }
}

public class ComponentSaveData
{
    public string TypeId { get; set; } = "";
    public string? ClrType { get; set; }
    public JsonObject Data { get; set; } = new();
}

public class ItemReferenceState
{
    public string? ContainedInEntityId { get; set; }
}

public class HandReferenceData
{
    public string Name { get; set; } = "";
    public string? HeldEntityId { get; set; }
    public bool BlockedByTwoHanded { get; set; }
}

public class HandsReferenceState
{
    public int ActiveHandIndex { get; set; }
    public List<HandReferenceData> Hands { get; set; } = new();
}

public class EquipmentSlotReferenceData
{
    public string SlotId { get; set; } = "";
    public string? ItemEntityId { get; set; }
}

public class EquipmentReferenceState
{
    public List<EquipmentSlotReferenceData> Slots { get; set; } = new();
}

public class StorageReferenceState
{
    public List<string> ContentEntityIds { get; set; } = new();
}
