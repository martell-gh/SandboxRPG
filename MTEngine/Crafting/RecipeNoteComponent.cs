using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using MTEngine.Combat;
using MTEngine.Components;
using MTEngine.Core;
using MTEngine.ECS;
using MTEngine.Interactions;
using MTEngine.Items;
using MTEngine.Systems;

namespace MTEngine.Crafting;

[RegisterComponent("recipeNote")]
public class RecipeNoteComponent : Component, IInteractionSource, IPrototypeInitializable
{
    [DataField("recipeId")]
    [SaveField("recipeId")]
    public string RecipeId { get; set; } = "";

    [DataField("recipeTitle")]
    [SaveField("recipeTitle")]
    public string RecipeTitle { get; set; } = "";

    [DataField("consumeOnRead")]
    [SaveField("consumeOnRead")]
    public bool ConsumeOnRead { get; set; }

    [DataField("readSkill")]
    [SaveField("readSkill")]
    public SkillType ReadSkill { get; set; } = SkillType.Smithing;

    [DataField("readRequiredSkill")]
    [SaveField("readRequiredSkill")]
    public float ReadRequiredSkill { get; set; }

    public void InitializeFromPrototype(EntityPrototype proto, AssetManager assets)
    {
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

        yield return new InteractionEntry
        {
            Id = $"recipe.read.{Owner.Id}",
            Label = "Прочитать рецепт",
            Priority = 24,
            Execute = c => ReadRecipe(c.Actor)
        };
    }

    public void RefreshPresentation()
    {
        if (Owner == null)
            return;

        var title = ResolveRecipeTitle();
        var item = Owner.GetComponent<ItemComponent>();
        if (item != null)
            item.ItemName = $"Рецепт: {title}";

        Owner.Name = item?.ItemName ?? $"Рецепт: {title}";

        if (Owner.GetComponent<InfoComponent>() is { } info)
        {
            var requirementText = ReadRequiredSkill > 0f
                ? $" Для прочтения нужно: {GetSkillTitle(ReadSkill)} {ReadRequiredSkill:0}+."
                : "";
            info.Description = $"Лист с описанием изготовления предмета \"{title}\". Прочтение добавит рецепт в список известных.{requirementText}";
        }
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
        _ => skill.ToString()
    };

    private void ReadRecipe(Entity actor)
    {
        var recipeId = RecipeId.Trim();
        if (string.IsNullOrWhiteSpace(recipeId))
        {
            PopupTextSystem.Show(actor, "В записи нет понятного рецепта.", Color.IndianRed, lifetime: 1.3f);
            return;
        }

        if (ReadRequiredSkill > 0f)
        {
            var actorSkills = actor.GetComponent<SkillComponent>();
            var actorSkill = actorSkills?.GetSkill(ReadSkill) ?? 0f;
            if (actorSkill < ReadRequiredSkill)
            {
                PopupTextSystem.Show(
                    actor,
                    $"Запись пока не понять. Нужно {GetSkillTitle(ReadSkill)} {ReadRequiredSkill:0}+ (есть {actorSkill:0}).",
                    Color.IndianRed,
                    lifetime: 1.6f);
                return;
            }
        }

        var knownRecipes = actor.GetComponent<KnownRecipesComponent>() ?? actor.AddComponent(new KnownRecipesComponent());
        var learned = knownRecipes.Learn(recipeId);
        PopupTextSystem.Show(
            actor,
            learned ? $"Изучен рецепт: {ResolveRecipeTitle()}" : "Этот рецепт уже известен.",
            learned ? Color.LightGreen : Color.LightGray,
            lifetime: 1.5f);

        if (learned)
            GrantReadingSkillGain(actor, recipeId);

        MarkWorldDirty();

        if (ConsumeOnRead)
            ConsumeNote();
    }

    private static void GrantReadingSkillGain(Entity actor, string recipeId)
    {
        var skills = actor.GetComponent<SkillComponent>();
        if (skills == null)
            return;

        var prototypes = ServiceLocator.Has<PrototypeManager>() ? ServiceLocator.Get<PrototypeManager>() : null;
        var recipeProto = prototypes?.GetEntity(recipeId);
        var (skill, tier) = ResolveRecipeSkillAndTier(recipeProto);
        var gain = 2.0f + 0.4f * Math.Max(0, 5 - tier);

        skills.Improve(skill, gain);
        if (skill != SkillType.Craftsmanship)
            skills.Improve(SkillType.Craftsmanship, gain * 0.3f);
    }

    private static (SkillType skill, int tier) ResolveRecipeSkillAndTier(EntityPrototype? recipeProto)
    {
        var skill = SkillType.Smithing;
        var tier = 5;

        if (recipeProto?.Components?["craftable"]?.AsObject() is { } craftableNode)
        {
            if (craftableNode["skill"] is { } skillNode &&
                Enum.TryParse<SkillType>(skillNode.ToString(), ignoreCase: true, out var parsedSkill))
                skill = parsedSkill;

            if (craftableNode["recipeTier"] is { } tierNode && int.TryParse(tierNode.ToString(), out var t) && t > 0)
                tier = t;
        }

        if (tier == 5 && recipeProto?.Components?["qualityTier"]?.AsObject() is { } qNode &&
            qNode["tier"] is { } qTierNode && int.TryParse(qTierNode.ToString(), out var qt) && qt > 0)
            tier = qt;

        return (skill, tier);
    }

    private string ResolveRecipeTitle()
    {
        if (!string.IsNullOrWhiteSpace(RecipeTitle))
            return RecipeTitle;

        var recipeProto = ServiceLocator.Has<PrototypeManager>()
            ? ServiceLocator.Get<PrototypeManager>().GetEntity(RecipeId)
            : null;

        return recipeProto?.Name ?? RecipeId;
    }

    private void ConsumeNote()
    {
        if (Owner == null)
            return;

        var item = Owner.GetComponent<ItemComponent>();
        var container = item?.ContainedIn;
        container?.GetComponent<StorageComponent>()?.Contents.Remove(Owner);
        container?.GetComponent<HandsComponent>()?.RemoveFromHand(Owner);
        container?.GetComponent<EquipmentComponent>()?.RemoveEquipped(Owner);
        if (item != null)
            item.ContainedIn = null;

        Owner.World?.DestroyEntity(Owner);
    }

    private static void MarkWorldDirty()
    {
        if (ServiceLocator.Has<IWorldStateTracker>())
            ServiceLocator.Get<IWorldStateTracker>().MarkDirty();
    }
}
