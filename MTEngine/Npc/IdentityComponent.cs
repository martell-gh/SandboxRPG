using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using MTEngine.Components;
using MTEngine.Core;
using MTEngine.ECS;
using MTEngine.Interactions;
using MTEngine.Systems;

namespace MTEngine.Npc;

public enum Gender { Male, Female }

/// <summary>
/// Личность NPC: имя, фамилия, пол, привязка к фракции/поселению/району.
/// `SettlementId/DistrictId` — это "прописка", не обязательно совпадает с тем,
/// где NPC сейчас физически. Перемещения отслеживает другой слой.
///
/// Также является источником интеракции «Поговорить» (см. §9.3): доступна, когда NPC
/// не мёртв, не в бою и не убегает. По нажатию после короткой задержки меняет Friendship
/// случайно на -1/0/+1 с кулдауном.
/// </summary>
[RegisterComponent("identity")]
public class IdentityComponent : Component, IInteractionSource
{
    /// <summary>Кулдаун «Поговорить» в игровых часах (см. §9.3).</summary>
    private const float TalkCooldownGameHours = 4f;
    private const int FriendshipMin = -100;
    private const int FriendshipCap = 100;

    [DataField("firstName")] [SaveField("firstName")]
    public string FirstName { get; set; } = "";

    [DataField("lastName")] [SaveField("lastName")]
    public string LastName { get; set; } = "";

    [DataField("gender")] [SaveField("gender")]
    public Gender Gender { get; set; } = Gender.Male;

    [DataField("factionId")] [SaveField("factionId")]
    public string FactionId { get; set; } = "";

    [DataField("settlementId")] [SaveField("settlementId")]
    public string SettlementId { get; set; } = "";

    [DataField("districtId")] [SaveField("districtId")]
    public string DistrictId { get; set; } = "";

    public string FullName =>
        string.IsNullOrWhiteSpace(LastName) ? FirstName : $"{FirstName} {LastName}";

    public IEnumerable<InteractionEntry> GetInteractions(InteractionContext ctx)
    {
        if (Owner == null || ctx.Target != Owner)
            yield break;
        if (!ctx.Actor.HasComponent<PlayerTagComponent>())
            yield break;
        if (!Owner.HasComponent<NpcTagComponent>())
            yield break;
        if (Owner.GetComponent<HealthComponent>()?.IsDead == true)
            yield break;
        if (Owner.GetComponent<NpcAggressionComponent>() is { Mode: not AggressionMode.None })
            yield break;
        if (Owner.GetComponent<NpcFleeComponent>() != null)
            yield break;

        yield return new InteractionEntry
        {
            Id = "npc.talk",
            Label = "Поговорить",
            Priority = 5,
            InterruptsCurrentAction = false,
            Delay = InteractionDelay.Seconds(1.35f, "Разговор"),
            Execute = c => Talk(c.Actor, Owner)
        };
    }

    private static void Talk(Entity actor, Entity npc)
    {
        var rel = npc.GetComponent<RelationshipWithPlayerComponent>()
                  ?? npc.AddComponent(new RelationshipWithPlayerComponent());

        var clock = ServiceLocator.Has<GameClock>() ? ServiceLocator.Get<GameClock>() : null;
        var now = clock?.TotalSecondsAbsolute ?? 0d;
        var cooldownSeconds = TalkCooldownGameHours * 3600d;

        if (now - rel.LastTalkAtSeconds < cooldownSeconds)
        {
            PopupTextSystem.Show(npc, "Мы уже поговорили...", Color.LightGray, lifetime: 1.2f);
            return;
        }

        rel.LastTalkAtSeconds = now;
        var delta = Random.Shared.Next(-1, 2);
        rel.Friendship = Math.Clamp(rel.Friendship + delta, FriendshipMin, FriendshipCap);

        if (actor.GetComponent<TransformComponent>() is { } actorTf
            && npc.GetComponent<TransformComponent>() is { } npcTf)
        {
            npc.GetComponent<SpriteComponent>()?.PlayDirectionalIdle(actorTf.Position - npcTf.Position);
        }

        var (text, color) = delta switch
        {
            > 0 => ("+1 дружба", Color.LightGreen),
            < 0 => ("-1 дружба", Color.IndianRed),
            _ => ("Обычный разговор", Color.LightGray)
        };
        PopupTextSystem.Show(npc, text, color, lifetime: 1.2f);

        if (ServiceLocator.Has<IWorldStateTracker>())
            ServiceLocator.Get<IWorldStateTracker>().MarkDirty();
    }
}
