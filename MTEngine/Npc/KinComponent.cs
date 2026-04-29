using MTEngine.ECS;

namespace MTEngine.Npc;

public enum KinKind { Father, Mother, Spouse, Child, Sibling }

public class KinLink
{
    public string NpcSaveId { get; set; } = "";
    public KinKind Kind { get; set; }
}

/// <summary>
/// Родственники NPC. Хранятся как пары (NpcSaveId, KinKind).
/// Зеркальные ссылки поддерживает RelationshipsSystem (P4/P7).
/// </summary>
[RegisterComponent("kin")]
public class KinComponent : Component
{
    [DataField("links")] [SaveField("links")]
    public List<KinLink> Links { get; set; } = new();

    public IEnumerable<KinLink> OfKind(KinKind kind)
        => Links.Where(l => l.Kind == kind);

    public void Add(string npcSaveId, KinKind kind)
    {
        if (Links.Any(l => l.NpcSaveId == npcSaveId && l.Kind == kind)) return;
        Links.Add(new KinLink { NpcSaveId = npcSaveId, Kind = kind });
    }

    public void RemoveAll(string npcSaveId)
        => Links.RemoveAll(l => l.NpcSaveId == npcSaveId);
}
