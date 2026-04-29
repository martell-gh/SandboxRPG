using MTEngine.Core;
using MTEngine.ECS;

namespace MTEngine.Npc;

/// <summary>
/// MTLiving: keeps KinComponent links mirrored after family events.
/// BirthSystem/RelationshipTickSystem may set their direct links; this system repairs
/// the full graph around those events.
/// </summary>
public class KinSyncSystem : GameSystem
{
    private EventBus _bus = null!;

    public override void OnInitialize()
    {
        _bus = ServiceLocator.Get<EventBus>();
        _bus.Subscribe<RelationshipMarried>(OnRelationshipMarried);
        _bus.Subscribe<NpcBorn>(OnNpcBorn);
        _bus.Subscribe<EntityDied>(OnEntityDied);
    }

    public override void Update(float deltaTime) { }

    public override void OnDestroy()
    {
        _bus.Unsubscribe<RelationshipMarried>(OnRelationshipMarried);
        _bus.Unsubscribe<NpcBorn>(OnNpcBorn);
        _bus.Unsubscribe<EntityDied>(OnEntityDied);
    }

    private void OnRelationshipMarried(RelationshipMarried ev)
    {
        var changed = false;
        changed |= AddMirroredKin(ev.NpcASaveId, ev.NpcBSaveId, KinKind.Spouse, KinKind.Spouse);
        MarkDirtyIfNeeded(changed);
    }

    private void OnNpcBorn(NpcBorn ev)
    {
        var changed = false;
        changed |= AddMirroredKin(ev.ChildSaveId, ev.FatherSaveId, KinKind.Father, KinKind.Child);
        changed |= AddMirroredKin(ev.ChildSaveId, ev.MotherSaveId, KinKind.Mother, KinKind.Child);

        var siblingIds = CollectSiblingIds(ev.ChildSaveId, ev.FatherSaveId, ev.MotherSaveId);
        foreach (var siblingId in siblingIds)
            changed |= AddMirroredKin(ev.ChildSaveId, siblingId, KinKind.Sibling, KinKind.Sibling);

        MarkDirtyIfNeeded(changed);
    }

    private void OnEntityDied(EntityDied ev)
    {
        var victimSaveId = GetSaveId(ev.Victim);
        if (string.IsNullOrWhiteSpace(victimSaveId))
            return;

        var changed = false;
        foreach (var entity in World.GetEntitiesWith<SaveEntityIdComponent>().ToList())
        {
            var entitySaveId = GetSaveId(entity);
            if (string.IsNullOrWhiteSpace(entitySaveId)
                || string.Equals(entitySaveId, victimSaveId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (HasKin(entity, victimSaveId, KinKind.Spouse) || HasMarriedPartner(entity, victimSaveId))
                changed |= MarkWidowed(entity, ev.DayIndex);
        }

        MarkDirtyIfNeeded(changed);
    }

    private bool AddMirroredKin(
        string aSaveId,
        string bSaveId,
        KinKind aSeesBAs,
        KinKind bSeesAAs)
    {
        if (string.IsNullOrWhiteSpace(aSaveId)
            || string.IsNullOrWhiteSpace(bSaveId)
            || string.Equals(aSaveId, bSaveId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var changed = false;

        var a = FindEntityBySaveId(aSaveId);
        if (a != null)
            changed |= AddKin(a, bSaveId, aSeesBAs);

        var b = FindEntityBySaveId(bSaveId);
        if (b != null)
            changed |= AddKin(b, aSaveId, bSeesAAs);

        return changed;
    }

    private HashSet<string> CollectSiblingIds(string childSaveId, string fatherSaveId, string motherSaveId)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddChildrenFromParentKin(result, FindEntityBySaveId(fatherSaveId), childSaveId);
        AddChildrenFromParentKin(result, FindEntityBySaveId(motherSaveId), childSaveId);

        foreach (var entity in World.GetEntitiesWith<KinComponent>())
        {
            var saveId = GetSaveId(entity);
            if (string.IsNullOrWhiteSpace(saveId)
                || string.Equals(saveId, childSaveId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var kin = entity.GetComponent<KinComponent>()!;
            if (kin.Links.Any(link =>
                    (link.Kind == KinKind.Father && string.Equals(link.NpcSaveId, fatherSaveId, StringComparison.OrdinalIgnoreCase))
                    || (link.Kind == KinKind.Mother && string.Equals(link.NpcSaveId, motherSaveId, StringComparison.OrdinalIgnoreCase))))
            {
                result.Add(saveId);
            }
        }

        result.Remove("");
        result.Remove(childSaveId);
        result.Remove(fatherSaveId);
        result.Remove(motherSaveId);
        return result;
    }

    private static void AddChildrenFromParentKin(HashSet<string> target, Entity? parent, string childSaveId)
    {
        var parentKin = parent?.GetComponent<KinComponent>();
        if (parentKin == null)
            return;

        foreach (var childLink in parentKin.OfKind(KinKind.Child))
        {
            if (!string.Equals(childLink.NpcSaveId, childSaveId, StringComparison.OrdinalIgnoreCase))
                target.Add(childLink.NpcSaveId);
        }
    }

    private Entity? FindEntityBySaveId(string saveId)
    {
        if (string.IsNullOrWhiteSpace(saveId))
            return null;

        foreach (var entity in World.GetEntitiesWith<SaveEntityIdComponent>())
        {
            if (string.Equals(entity.GetComponent<SaveEntityIdComponent>()?.SaveId, saveId, StringComparison.OrdinalIgnoreCase))
                return entity;
        }

        return null;
    }

    private static string GetSaveId(Entity entity)
        => entity.GetComponent<SaveEntityIdComponent>()?.SaveId ?? "";

    private static bool AddKin(Entity entity, string otherSaveId, KinKind kind)
    {
        var kin = entity.GetComponent<KinComponent>() ?? entity.AddComponent(new KinComponent());
        if (kin.Links.Any(link =>
                link.Kind == kind
                && string.Equals(link.NpcSaveId, otherSaveId, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        kin.Add(otherSaveId, kind);
        return true;
    }

    private static bool HasKin(Entity entity, string otherSaveId, KinKind kind)
    {
        var kin = entity.GetComponent<KinComponent>();
        return kin != null && kin.Links.Any(link =>
            link.Kind == kind
            && string.Equals(link.NpcSaveId, otherSaveId, StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasMarriedPartner(Entity entity, string partnerSaveId)
    {
        var relationships = entity.GetComponent<RelationshipsComponent>();
        return relationships != null
               && relationships.Status == RelationshipStatus.Married
               && !relationships.PartnerIsPlayer
               && string.Equals(relationships.PartnerNpcSaveId, partnerSaveId, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MarkWidowed(Entity entity, long today)
    {
        var relationships = entity.GetComponent<RelationshipsComponent>();
        if (relationships == null)
            return false;

        var changed = relationships.Status != RelationshipStatus.Widowed
                      || !string.IsNullOrWhiteSpace(relationships.PartnerNpcSaveId)
                      || relationships.PartnerIsPlayer
                      || relationships.ScheduledDateDayIndex != -1L
                      || relationships.ScheduledWeddingDayIndex != -1L
                      || relationships.ScheduledBirthDayIndex != -1L
                      || relationships.LastMatchSearchDayIndex != today;

        relationships.Status = RelationshipStatus.Widowed;
        relationships.PartnerNpcSaveId = "";
        relationships.PartnerIsPlayer = false;
        relationships.ScheduledDateDayIndex = -1L;
        relationships.ScheduledWeddingDayIndex = -1L;
        relationships.ScheduledBirthDayIndex = -1L;
        relationships.LastMatchSearchDayIndex = today;
        relationships.OvernightStreak = 0;
        return changed;
    }

    private static void MarkDirtyIfNeeded(bool changed)
    {
        if (changed && ServiceLocator.Has<IWorldStateTracker>())
            ServiceLocator.Get<IWorldStateTracker>().MarkDirty();
    }
}
