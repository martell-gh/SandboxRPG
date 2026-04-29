using Microsoft.Xna.Framework;
using MTEngine.ECS;

namespace MTEngine.Npc;

public enum AggressionMode
{
    None,
    /// <summary>Бьём, но останавливаемся когда у цели HP падает ниже HpFloor.</summary>
    Restrained,
    /// <summary>Бьём насмерть.</summary>
    Lethal
}

public enum AggressionReason
{
    None,
    SelfDefense,
    HomeIntrusion
}

/// <summary>
/// Боевое состояние NPC к конкретной цели (обычно игроку). Заполняется
/// <see cref="HomeIntrusionSystem"/> и <see cref="NpcCombatReactionSystem"/>.
/// Сериализуется: намерение драться переживает save/load. <see cref="TargetEntityId"/>
/// — рантайм-id, после загрузки переразрешается из <see cref="TargetSaveId"/>.
/// </summary>
[RegisterComponent("npcAggression")]
public class NpcAggressionComponent : Component
{
    [SaveField("mode")] public AggressionMode Mode { get; set; } = AggressionMode.None;
    [SaveField("reason")] public AggressionReason Reason { get; set; } = AggressionReason.None;
    public int TargetEntityId { get; set; }
    [SaveField("targetSaveId")] public string TargetSaveId { get; set; } = "";
    [SaveField("targetIsPlayer")] public bool TargetIsPlayer { get; set; }
    [SaveField("protectedHouseId")] public string ProtectedHouseId { get; set; } = "";
    [SaveField("provokedByTarget")] public bool ProvokedByTarget { get; set; }

    /// <summary>Время последнего "вижу цель" (в TotalSecondsAbsolute).</summary>
    [SaveField("lastSightedAt")] public double LastSightedAt { get; set; }
    /// <summary>Последняя известная позиция цели.</summary>
    [SaveField("lastSightedPosition")] public Vector2 LastSightedPosition { get; set; }

    /// <summary>Сколько секунд "стоим и хмуримся" после потери цели прежде чем разагриться.</summary>
    [SaveField("disengageLingerSeconds")] public float DisengageLingerSeconds { get; set; }
    [SaveField("isDisengaging")] public bool IsDisengaging { get; set; }

    /// <summary>Был ли уже хотя бы один "обмен ударами" в этой стычке (для эскалации Restrained → Lethal).</summary>
    [SaveField("hasExchanged")] public bool HasExchanged { get; set; }

    /// <summary>Сколько раз NPC попал по цели (для лимита ответных ударов в режиме предупреждения).</summary>
    [SaveField("hitsLandedOnTarget")] public int HitsLandedOnTarget { get; set; }

    /// <summary>Режим "предупреждения": одиночный игрок-удар без плохих отношений → NPC ответит несколько раз и успокоится.</summary>
    [SaveField("isWarning")] public bool IsWarning { get; set; }

    [SaveField("lastOpinionPenaltyAt")] public double LastOpinionPenaltyAt { get; set; } = double.MinValue;
    [SaveField("opinionPenaltyInConflict")] public int OpinionPenaltyInConflict { get; set; }
}

internal static class Aggression
{
    public static void MarkChasing(Entity npc, Entity target, double nowSeconds, bool lethal)
        => MarkChasing(npc, target, nowSeconds, lethal, AggressionReason.SelfDefense, "", provokedByTarget: true);

    public static void MarkHomeIntrusion(Entity npc, Entity target, double nowSeconds, string houseId, bool lethal)
        => MarkChasing(npc, target, nowSeconds, lethal, AggressionReason.HomeIntrusion, houseId, provokedByTarget: false);

    private static void MarkChasing(
        Entity npc,
        Entity target,
        double nowSeconds,
        bool lethal,
        AggressionReason reason,
        string protectedHouseId,
        bool provokedByTarget)
    {
        var comp = npc.GetComponent<NpcAggressionComponent>() ?? npc.AddComponent(new NpcAggressionComponent());
        var newMode = lethal ? AggressionMode.Lethal : AggressionMode.Restrained;
        if (comp.Mode == AggressionMode.None || NewModeIsStronger(comp.Mode, newMode))
            comp.Mode = newMode;

        comp.Reason = reason;
        comp.ProtectedHouseId = protectedHouseId;
        comp.ProvokedByTarget = comp.ProvokedByTarget || provokedByTarget;
        comp.TargetEntityId = target.Id;
        comp.TargetIsPlayer = target.HasComponent<MTEngine.Components.PlayerTagComponent>();
        var marker = target.GetComponent<SaveEntityIdComponent>();
        if (marker != null) comp.TargetSaveId = marker.SaveId;
        var tf = target.GetComponent<MTEngine.Components.TransformComponent>();
        if (tf != null) comp.LastSightedPosition = tf.Position;
        comp.LastSightedAt = nowSeconds;
        comp.IsDisengaging = false;
        comp.DisengageLingerSeconds = 0f;
    }

    public static void MarkLethal(Entity npc, Entity target, double nowSeconds)
        => MarkChasing(npc, target, nowSeconds, lethal: true);

    public static void Escalate(Entity npc, Entity target, double nowSeconds)
    {
        var comp = npc.GetComponent<NpcAggressionComponent>();
        if (comp == null)
        {
            MarkChasing(npc, target, nowSeconds, lethal: false);
            return;
        }

        comp.Reason = AggressionReason.SelfDefense;
        comp.ProvokedByTarget = true;
        // Повторное нападение игрока → выход из режима предупреждения, эскалация если уже обменялись.
        comp.IsWarning = false;
        if (comp.HasExchanged && comp.Mode != AggressionMode.Lethal)
            comp.Mode = AggressionMode.Lethal;
        comp.HasExchanged = true;
        comp.TargetEntityId = target.Id;
        comp.TargetIsPlayer = comp.TargetIsPlayer || target.HasComponent<MTEngine.Components.PlayerTagComponent>();
        var targetMarker = target.GetComponent<SaveEntityIdComponent>();
        if (targetMarker != null) comp.TargetSaveId = targetMarker.SaveId;
        comp.LastSightedAt = nowSeconds;
        var tf = target.GetComponent<MTEngine.Components.TransformComponent>();
        if (tf != null) comp.LastSightedPosition = tf.Position;
        comp.IsDisengaging = false;
        comp.DisengageLingerSeconds = 0f;
    }

    public static void Clear(Entity npc)
    {
        var comp = npc.GetComponent<NpcAggressionComponent>();
        if (comp == null) return;
        comp.Mode = AggressionMode.None;
        comp.Reason = AggressionReason.None;
        comp.TargetEntityId = 0;
        comp.TargetSaveId = "";
        comp.TargetIsPlayer = false;
        comp.ProtectedHouseId = "";
        comp.ProvokedByTarget = false;
        comp.HasExchanged = false;
        comp.IsDisengaging = false;
        comp.DisengageLingerSeconds = 0f;
        comp.HitsLandedOnTarget = 0;
        comp.IsWarning = false;
        comp.LastOpinionPenaltyAt = double.MinValue;
        comp.OpinionPenaltyInConflict = 0;
    }

    private static bool NewModeIsStronger(AggressionMode current, AggressionMode candidate)
        => (int)candidate > (int)current;
}
