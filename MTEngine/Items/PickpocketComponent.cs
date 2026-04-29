using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using MTEngine.Combat;
using MTEngine.Components;
using MTEngine.ECS;
using MTEngine.Interactions;
using MTEngine.Systems;

namespace MTEngine.Items;

[RegisterComponent("pickpocket")]
public class PickpocketComponent : Component, IInteractionSource
{
    [DataField("difficulty")]
    [SaveField("difficulty")]
    public float Difficulty { get; set; } = 20f;

    [DataField("pocketLabel")]
    [SaveField("pocketLabel")]
    public string PocketLabel { get; set; } = "Заглянуть в карманы";

    public IEnumerable<InteractionEntry> GetInteractions(InteractionContext ctx)
    {
        if (Owner == null || ctx.Target != Owner || ctx.Actor == Owner)
            yield break;

        if (Owner.GetComponent<StorageComponent>() != null)
        {
            yield return new InteractionEntry
            {
                Id = $"pickpocket.inspect.{Owner.Id}",
                Label = PocketLabel,
                Priority = 21,
                Execute = c => c.World.GetSystem<InteractionSystem>()?.OpenStorage(c.Actor, Owner, allowNpcStorage: true)
            };
        }
    }

    public bool TryStealItem(Entity actor, Entity item)
    {
        if (Owner == null)
            return false;

        var skills = actor.GetComponent<SkillComponent>();
        var chance = GetStealChance(actor, item);
        var success = Random.Shared.NextSingle() <= chance;

        if (success)
        {
            skills?.Improve(SkillType.Thievery, 0.32f);
            PopupTextSystem.Show(actor, $"Украдено: {item.GetComponent<ItemComponent>()?.ItemName ?? item.Name}", Color.Khaki, lifetime: 1.3f);
            return true;
        }

        skills?.Improve(SkillType.Thievery, 0.08f);
        PopupTextSystem.Show(Owner, "Замечено!", Color.IndianRed, lifetime: 1.4f);
        PopupTextSystem.Show(actor, "Провал карманной кражи.", Color.IndianRed, lifetime: 1.2f);
        return false;
    }

    public float GetStealChance(Entity actor, Entity item)
    {
        var thievery = actor.GetComponent<SkillComponent>()?.GetSkill(SkillType.Thievery) ?? 0f;
        var bulk = item.GetComponent<ItemComponent>()?.SlotSize ?? 1;
        return SkillChecks.GetStealChance(thievery, Difficulty, bulk - 1);
    }
}
