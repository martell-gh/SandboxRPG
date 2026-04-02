using MTEngine.ECS;

namespace MTEngine.Items;

public sealed class EquipmentSlot
{
    public string Id { get; }
    public string DisplayName { get; }
    public Entity? Item { get; set; }

    public EquipmentSlot(string id, string displayName)
    {
        Id = id;
        DisplayName = displayName;
    }
}
