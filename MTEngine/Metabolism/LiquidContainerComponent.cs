using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MTEngine.Core;
using MTEngine.ECS;
using MTEngine.Interactions;
using MTEngine.Items;

namespace MTEngine.Metabolism;

[RegisterComponent("liquidContainer")]
public class LiquidContainerComponent : Component, IInteractionSource, IPrototypeInitializable, ISubstanceReservoir
{
    private const float DrinkSipVolume = 10f;

    [DataField("name")]
    public string ContainerName { get; set; } = "Container";

    [DataField("capacity")]
    public float Capacity { get; set; } = 100f;

    [DataField("transparent")]
    public bool Transparent { get; set; }

    [DataField("showContents")]
    public bool ShowContents { get; set; }

    [DataField("drinkVerb")]
    public string DrinkVerb { get; set; } = "Выпить";

    [DataField("nutrition")]
    public float Nutrition { get; set; }

    [DataField("hydration")]
    public float Hydration { get; set; }

    [DataField("bladderLoad")]
    public float BladderLoad { get; set; }

    [DataField("bowelLoad")]
    public float BowelLoad { get; set; }

    [DataField("digestTime")]
    public float DigestTime { get; set; } = 8f;

    [DataField("contents")]
    public List<SubstanceReference> ContentRefs { get; set; } = new();

    public List<SubstanceDose> Contents { get; set; } = new();

    [DataField("fillSprite25")]
    public string? FillSprite25Path { get; set; }

    [DataField("fillSprite50")]
    public string? FillSprite50Path { get; set; }

    [DataField("fillSprite75")]
    public string? FillSprite75Path { get; set; }

    [DataField("fillSprite100")]
    public string? FillSprite100Path { get; set; }

    private bool _normalized;
    private Texture2D? _fill25;
    private Texture2D? _fill50;
    private Texture2D? _fill75;
    private Texture2D? _fill100;

    public float CurrentVolume => Contents.Sum(dose => dose.EffectiveVolume);
    public float FreeCapacity => Math.Max(0f, Capacity - CurrentVolume);
    public bool HasContents => CurrentVolume > 0.01f || Hydration > 0.01f || Nutrition > 0.01f;
    public string DisplayName => ContainerName;
    public bool HasSubstances => Contents.Count > 0;

    public void InitializeFromPrototype(EntityPrototype proto, AssetManager assets)
    {
        var dir = proto.DirectoryPath ?? "";
        _fill25 = LoadTexture(assets, dir, FillSprite25Path);
        _fill50 = LoadTexture(assets, dir, FillSprite50Path);
        _fill75 = LoadTexture(assets, dir, FillSprite75Path);
        _fill100 = LoadTexture(assets, dir, FillSprite100Path);
        Contents = SubstanceResolver.ResolveMany(ContentRefs);
        NormalizeContents();
    }

    public IEnumerable<InteractionEntry> GetInteractions(InteractionContext ctx)
    {
        NormalizeContents();

        var item = Owner?.GetComponent<ItemComponent>();
        var heldByActor = item?.ContainedIn == ctx.Actor;

        if (ctx.Target == Owner && heldByActor && HasContents)
        {
            yield return new InteractionEntry
            {
                Id = "liquidContainer.drink",
                Label = $"{DrinkVerb} ({ContainerName})",
                Priority = 23,
                Execute = c => Drink(c.Actor)
            };

            yield return new InteractionEntry
            {
                Id = "liquidContainer.smell",
                Label = $"Понюхать ({ContainerName})",
                Priority = 22,
                Execute = c => Smell(c.Actor)
            };
        }

        var actorHands = ctx.Actor.GetComponent<HandsComponent>();
        var sourceEntity = actorHands?.ActiveItem;
        var sourceContainer = sourceEntity?.GetComponent<LiquidContainerComponent>();

        if (ctx.Target == Owner &&
            sourceContainer != null &&
            sourceContainer != this &&
            sourceContainer.HasContents &&
            FreeCapacity > 0.01f)
        {
            var sourceName = sourceEntity?.GetComponent<ItemComponent>()?.ItemName ?? sourceContainer.ContainerName;
            yield return new InteractionEntry
            {
                Id = "liquidContainer.transferUi",
                Label = $"Открыть переливание ({sourceName})",
                Priority = 21,
                Execute = c =>
                {
                    var ui = Owner?.World?.GetSystem<SubstanceWorkbenchSystem>();
                    ui?.OpenTransferWindow(c.Actor, sourceContainer, this);
                }
            };

            yield return new InteractionEntry
            {
                Id = "liquidContainer.transferAll",
                Label = $"Перелить всё ({sourceName})",
                Priority = 19,
                Execute = c =>
                {
                    var moved = sourceContainer.TransferTo(this, sourceContainer.CurrentVolume);
                    if (moved <= 0.01f)
                        return;

                    Systems.PopupTextSystem.Show(c.Actor, $"Перелито: {moved:0.#}", Color.LightSteelBlue);
                }
            };
        }

        if (ctx.Target == Owner &&
            sourceEntity != null &&
            sourceEntity.GetComponent<MortarComponent>() is { HasSubstances: true } mortar &&
            FreeCapacity > 0.01f)
        {
            yield return new InteractionEntry
            {
                Id = "liquidContainer.extractUi",
                Label = $"Вывести из ({mortar.DisplayName})",
                Priority = 20,
                Execute = c =>
                {
                    var ui = Owner?.World?.GetSystem<SubstanceWorkbenchSystem>();
                    ui?.OpenTransferWindow(c.Actor, mortar, this);
                }
            };
        }
    }

    public float TransferTo(LiquidContainerComponent target, float requestedVolume)
    {
        NormalizeContents();
        target.NormalizeContents();

        var movable = Math.Min(Math.Min(requestedVolume, CurrentVolume), target.FreeCapacity);
        if (movable <= 0.01f)
            return 0f;

        var totalVolume = Math.Max(CurrentVolume, 0.01f);
        var movedRatio = movable / totalVolume;

        foreach (var substance in Contents.ToList())
        {
            var part = substance.CloneScaled(movedRatio);
            if (part.Amount <= 0.001f && part.EffectiveVolume <= 0.001f)
                continue;

            target.AddSubstance(part);
            substance.Amount = Math.Max(0f, substance.Amount - part.Amount);
            substance.Volume = Math.Max(0f, substance.EffectiveVolume - part.EffectiveVolume);
        }

        target.Nutrition += Nutrition * movedRatio;
        target.Hydration += Hydration * movedRatio;
        target.BladderLoad += BladderLoad * movedRatio;
        target.BowelLoad += BowelLoad * movedRatio;

        Nutrition *= 1f - movedRatio;
        Hydration *= 1f - movedRatio;
        BladderLoad *= 1f - movedRatio;
        BowelLoad *= 1f - movedRatio;

        CleanupContents();
        target.NormalizeContents();
        return movable;
    }

    public IReadOnlyList<SubstanceDose> GetSubstances()
    {
        NormalizeContents();
        return Contents;
    }

    public float TransferSubstanceTo(LiquidContainerComponent target, string substanceId, float amount)
    {
        NormalizeContents();
        target.NormalizeContents();

        if (amount <= 0.001f || target.FreeCapacity <= 0.001f)
            return 0f;

        var remaining = Math.Min(amount, target.FreeCapacity);
        var moved = 0f;

        foreach (var substance in Contents.Where(dose =>
                     string.Equals(dose.Id, substanceId, StringComparison.OrdinalIgnoreCase)).ToList())
        {
            if (remaining <= 0.001f)
                break;

            var take = Math.Min(substance.Amount, remaining);
            if (take <= 0.001f)
                continue;

            var ratio = substance.Amount <= 0f ? 1f : take / substance.Amount;
            var part = substance.CloneScaled(ratio);
            part.Amount = take;
            part.Volume = Math.Min(part.EffectiveVolume, take);
            target.AddSubstance(part);

            substance.Amount = Math.Max(0f, substance.Amount - take);
            substance.Volume = Math.Max(0f, substance.EffectiveVolume - part.EffectiveVolume);

            remaining -= take;
            moved += take;
        }

        CleanupContents();
        target.NormalizeContents();
        return moved;
    }

    public void AddSubstance(SubstanceDose dose)
    {
        if (dose.Amount <= 0.001f && dose.EffectiveVolume <= 0.001f)
            return;

        Contents.Add(dose.CloneScaled(1f));
        _normalized = false;
    }

    public void Drink(Entity actor)
    {
        NormalizeContents();

        var metabolism = actor.GetComponent<MetabolismComponent>();
        if (metabolism == null)
            return;

        var item = Owner?.GetComponent<ItemComponent>();
        var itemName = item?.ItemName ?? ContainerName;
        var availableVolume = CurrentVolume;
        if (availableVolume <= 0.01f)
            return;

        var sipVolume = Math.Min(DrinkSipVolume, availableVolume);
        var sipRatio = Math.Clamp(sipVolume / Math.Max(availableVolume, 0.01f), 0f, 1f);

        var sipNutrition = Nutrition * sipRatio;
        var sipHydration = Hydration * sipRatio;
        var sipBladderLoad = BladderLoad * sipRatio;
        var sipBowelLoad = BowelLoad * sipRatio;

        if (sipNutrition > 0f || sipHydration > 0f || sipBladderLoad > 0f || sipBowelLoad > 0f)
        {
            metabolism.DigestingItems.Add(new DigestingItem
            {
                Name = itemName,
                RemainingNutrition = sipNutrition,
                RemainingHydration = sipHydration,
                BladderLoad = sipBladderLoad,
                BowelLoad = sipBowelLoad,
                Duration = Math.Max(1f, DigestTime),
                Elapsed = 0f
            });
        }

        if (Contents.Count > 0)
        {
            foreach (var dose in Contents.ToList())
            {
                var part = dose.CloneScaled(sipRatio);
                if (part.Amount <= 0.001f && part.EffectiveVolume <= 0.001f)
                    continue;

                if (part.Nutrition > 0f || part.Hydration > 0f || part.BladderLoad > 0f || part.BowelLoad > 0f)
                {
                    metabolism.DigestingItems.Add(new DigestingItem
                    {
                        Name = $"{itemName}:{part.Name}",
                        RemainingNutrition = part.Nutrition,
                        RemainingHydration = part.Hydration,
                        BladderLoad = part.BladderLoad,
                        BowelLoad = part.BowelLoad,
                        Duration = Math.Max(1f, part.AbsorptionTime),
                        Elapsed = 0f
                    });
                }

                metabolism.ActiveSubstances.Add(part.ToActiveDose(itemName));
                dose.Amount = Math.Max(0f, dose.Amount - part.Amount);
                dose.Volume = Math.Max(0f, dose.EffectiveVolume - part.EffectiveVolume);
                dose.Nutrition = Math.Max(0f, dose.Nutrition - part.Nutrition);
                dose.Hydration = Math.Max(0f, dose.Hydration - part.Hydration);
                dose.BladderLoad = Math.Max(0f, dose.BladderLoad - part.BladderLoad);
                dose.BowelLoad = Math.Max(0f, dose.BowelLoad - part.BowelLoad);
            }
        }

        Systems.PopupTextSystem.Show(actor, $"{DrinkVerb}: {itemName} ({sipVolume:0.#} мл)", Color.LightBlue);

        Nutrition = Math.Max(0f, Nutrition - sipNutrition);
        Hydration = Math.Max(0f, Hydration - sipHydration);
        BladderLoad = Math.Max(0f, BladderLoad - sipBladderLoad);
        BowelLoad = Math.Max(0f, BowelLoad - sipBowelLoad);
        CleanupContents();
        _normalized = true;
    }

    public void Smell(Entity actor)
    {
        var smell = DescribeSmell();
        Systems.PopupTextSystem.Show(actor, smell, Color.Wheat, lifetime: 1.75f);
        Console.WriteLine($"[Substances] {actor.Name} smells {ContainerName}: {smell}");
    }

    public string DescribeContents()
    {
        NormalizeContents();

        if (!HasContents)
            return $"{ContainerName}: пусто.";

        var parts = ShowContents && Contents.Count > 0
            ? string.Join(", ", Contents.Select(dose => $"{dose.Name} x{dose.Amount:0.##}"))
            : Contents.Count == 0 ? "без веществ" : "скрытая смесь";

        return $"{ContainerName}: {CurrentVolume:0.#}/{Capacity:0.#}. Состав: {parts}. Цвет смеси: {GetMixedColorHex()}.";
    }

    public string DescribeSmell()
    {
        NormalizeContents();

        var smells = Contents
            .SelectMany(dose => dose.Smells)
            .Where(smell => !string.IsNullOrWhiteSpace(smell))
            .GroupBy(smell => smell, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key)
            .Take(3)
            .Select(group => group.Key)
            .ToList();

        if (smells.Count == 0)
            return $"{ContainerName}: почти без запаха.";

        return $"{ContainerName}: пахнет {string.Join(", ", smells)}.";
    }

    public Texture2D? GetFillTexture()
    {
        if (!Transparent || !ShowContents || CurrentVolume <= 0.01f || Capacity <= 0.01f)
            return null;

        var ratio = CurrentVolume / Capacity;
        if (ratio >= 0.99f)
            return _fill100 ?? _fill75 ?? _fill50 ?? _fill25;
        if (ratio >= 0.75f)
            return _fill75 ?? _fill50 ?? _fill25;
        if (ratio >= 0.50f)
            return _fill50 ?? _fill25;
        return _fill25;
    }

    public Color GetFillColor()
        => AssetManager.ParseHexColor(GetMixedColorHex(), Color.White);

    public string GetMixedColorHex()
    {
        NormalizeContents();
        if (Contents.Count == 0)
            return "#00000000";

        float total = 0f;
        float r = 0f;
        float g = 0f;
        float b = 0f;
        float a = 0f;

        foreach (var dose in Contents)
        {
            var weight = Math.Max(dose.EffectiveVolume, 0.01f);
            var color = AssetManager.ParseHexColor(dose.Color, Color.White);
            total += weight;
            r += color.R * weight;
            g += color.G * weight;
            b += color.B * weight;
            a += color.A * weight;
        }

        if (total <= 0f)
            return "#FFFFFFFF";

        var mixed = new Color(
            (byte)Math.Clamp(r / total, 0, 255),
            (byte)Math.Clamp(g / total, 0, 255),
            (byte)Math.Clamp(b / total, 0, 255),
            (byte)Math.Clamp(a / total, 0, 255));

        return $"#{mixed.R:X2}{mixed.G:X2}{mixed.B:X2}{mixed.A:X2}";
    }

    public void NormalizeContents()
    {
        if (_normalized)
            return;

        Contents = SubstanceResolver.MergeById(Contents);

        ResolveRecipes();
        CleanupContents();
        _normalized = true;
    }

    private void ResolveRecipes()
    {
        var changed = true;

        while (changed)
        {
            changed = false;

            foreach (var recipe in Contents.SelectMany(dose => dose.Recipes).ToList())
            {
                if (!CanApplyRecipe(recipe))
                    continue;

                foreach (var ingredient in recipe.Ingredients)
                    ConsumeIngredient(ingredient);

                Contents.AddRange(SubstanceResolver.ResolveMany(recipe.Results));

                changed = true;
                break;
            }
        }
    }

    private bool CanApplyRecipe(SubstanceRecipeDefinition recipe)
    {
        foreach (var ingredient in recipe.Ingredients)
        {
            var total = Contents
                .Where(dose => string.Equals(dose.Id, ingredient.SubstanceId, StringComparison.OrdinalIgnoreCase))
                .Sum(dose => dose.Amount);

            if (total + 0.001f < ingredient.Amount)
                return false;
        }

        return recipe.Results.Count > 0;
    }

    private void ConsumeIngredient(SubstanceRequirement ingredient)
    {
        var remaining = ingredient.Amount;

        foreach (var dose in Contents.Where(dose =>
                     string.Equals(dose.Id, ingredient.SubstanceId, StringComparison.OrdinalIgnoreCase)).ToList())
        {
            if (remaining <= 0f)
                break;

            var take = Math.Min(dose.Amount, remaining);
            var ratio = dose.Amount <= 0f ? 1f : take / dose.Amount;
            dose.Amount = Math.Max(0f, dose.Amount - take);
            dose.Volume = Math.Max(0f, dose.EffectiveVolume * (1f - ratio));
            remaining -= take;
        }
    }

    private void CleanupContents()
    {
        Contents = SubstanceResolver.MergeById(Contents);
    }

    private static Texture2D? LoadTexture(AssetManager assets, string dir, string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return null;

        var path = System.IO.Path.Combine(dir, relativePath);
        return assets.LoadFromFile(path);
    }
}
