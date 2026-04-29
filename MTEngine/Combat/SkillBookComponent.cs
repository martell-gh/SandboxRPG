using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using MTEngine.Components;
using MTEngine.Core;
using MTEngine.ECS;
using MTEngine.Interactions;
using MTEngine.Items;
using MTEngine.Systems;

namespace MTEngine.Combat;

[RegisterComponent("skillBook")]
public class SkillBookComponent : Component, IInteractionSource, IPrototypeInitializable
{
    [DataField("skill")]
    [SaveField("skill")]
    public SkillType Skill { get; set; } = SkillType.Smithing;

    [DataField("gain")]
    [SaveField("gain")]
    public float SkillGain { get; set; } = 8f;

    [DataField("readSeconds")]
    [SaveField("readSeconds")]
    public float ReadDurationSeconds { get; set; } = 15f;

    [DataField("readLabel")]
    [SaveField("readLabel")]
    public string ReadLabel { get; set; } = "Чтение книги";

    [SaveField("isRead")]
    public bool IsRead { get; set; }

    [SaveField("instanceId")]
    public string InstanceId { get; set; } = "";

    public void InitializeFromPrototype(EntityPrototype proto, AssetManager assets)
    {
        if (string.IsNullOrEmpty(InstanceId))
            InstanceId = Guid.NewGuid().ToString("N");
        RefreshPresentation();
    }

    public IEnumerable<InteractionEntry> GetInteractions(InteractionContext ctx)
    {
        if (Owner == null)
            yield break;

        var item = Owner.GetComponent<ItemComponent>();
        var heldByActor = item?.ContainedIn == ctx.Actor;
        if (!heldByActor && ctx.Target != Owner)
            yield break;

        if (IsRead)
            yield break;

        yield return new InteractionEntry
        {
            Id = $"book.read.{Owner.Id}",
            Label = "Прочитать книгу",
            Priority = 22,
            Delay = InteractionDelay.Seconds(ReadDurationSeconds, ReadLabel),
            Execute = c => CompleteRead(c.Actor)
        };
    }

    public void RefreshPresentation()
    {
        if (Owner == null)
            return;

        var info = Owner.GetComponent<InfoComponent>();
        if (info != null)
        {
            var status = IsRead ? " (уже прочитана)" : "";
            info.Description =
                $"Книга по теме «{GetSkillTitle(Skill)}». Прочтение даёт +{SkillGain:0.#} к навыку. " +
                $"Чтение занимает {ReadDurationSeconds:0} с. Каждый экземпляр читается лишь однажды.{status}";
        }

        var item = Owner.GetComponent<ItemComponent>();
        if (item != null && IsRead && !item.ItemName.Contains("(прочитана)", StringComparison.Ordinal))
            item.ItemName = $"{item.ItemName} (прочитана)";
    }

    private void CompleteRead(Entity actor)
    {
        if (IsRead)
        {
            PopupTextSystem.Show(actor, "Эту книгу ты уже прочитал.", Color.LightGray, lifetime: 1.3f);
            return;
        }

        var skills = actor.GetComponent<SkillComponent>();
        if (skills == null)
        {
            PopupTextSystem.Show(actor, "Некому усвоить написанное.", Color.IndianRed, lifetime: 1.3f);
            return;
        }

        var before = skills.GetSkill(Skill);
        skills.Improve(Skill, SkillGain);
        var after = skills.GetSkill(Skill);

        IsRead = true;
        RefreshPresentation();

        PopupTextSystem.Show(
            actor,
            $"{GetSkillTitle(Skill)}: {before:0.#} → {after:0.#}",
            Color.LightGreen,
            lifetime: 1.8f);

        if (ServiceLocator.Has<IWorldStateTracker>())
            ServiceLocator.Get<IWorldStateTracker>().MarkDirty();
    }

    private static string GetSkillTitle(SkillType skill) => skill switch
    {
        SkillType.Smithing => "Кузнечное дело",
        SkillType.Tailoring => "Шитьё",
        SkillType.Medicine => "Медицина",
        SkillType.Thievery => "Воровство",
        SkillType.Social => "Социалка",
        SkillType.Trade => "Торговля",
        SkillType.Craftsmanship => "Ремесло",
        SkillType.Fortitude => "Крепость",
        SkillType.Dodge => "Уворот",
        SkillType.Blocking => "Блок",
        SkillType.HandToHand => "Рукопашный бой",
        SkillType.OneHandedWeapons => "Одноручное оружие",
        SkillType.TwoHandedWeapons => "Двуручное оружие",
        _ => skill.ToString()
    };
}
