using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Xna.Framework;
using MTEngine.Core;
using MTEngine.ECS;

namespace MTEngine.Metabolism;

public class SubstanceReference
{
    public string Id { get; set; } = "";
    public float Amount { get; set; } = 1f;
}

public class SubstancePrototype
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Color { get; set; } = "#FFFFFFFF";
    public float DefaultAmount { get; set; } = 1f;
    public float VolumePerUnit { get; set; } = 1f;
    public float Nutrition { get; set; }
    public float Hydration { get; set; }
    public float BladderLoad { get; set; }
    public float BowelLoad { get; set; }
    public float AbsorptionTime { get; set; } = 5f;
    public float ClearanceTime { get; set; } = 60f;
    public List<string> Smells { get; set; } = new();
    public string? PreparationHint { get; set; }
    public List<SubstanceEffectDefinition> Effects { get; set; } = new();
    public List<SubstanceResponseProfile> ResponseProfiles { get; set; } = new();
    public List<SubstanceRecipeDefinition> Recipes { get; set; } = new();

    public SubstanceDose CreateDose(float amount)
    {
        return new SubstanceDose
        {
            Id = Id,
            Name = string.IsNullOrWhiteSpace(Name) ? Id : Name,
            Color = Color,
            Amount = amount,
            Volume = amount * Math.Max(VolumePerUnit, 0.01f),
            Nutrition = Nutrition * amount,
            Hydration = Hydration * amount,
            BladderLoad = BladderLoad * amount,
            BowelLoad = BowelLoad * amount,
            AbsorptionTime = AbsorptionTime,
            ClearanceTime = ClearanceTime,
            Smells = new List<string>(Smells),
            PreparationHint = PreparationHint,
            Effects = Effects.Select(effect => effect.Clone()).ToList(),
            ResponseProfiles = ResponseProfiles.Select(profile => profile.Clone()).ToList(),
            Recipes = Recipes.Select(recipe => recipe.Clone()).ToList()
        };
    }

    public static SubstancePrototype? LoadFromFile(string path)
    {
        if (!File.Exists(path))
            return null;

        try
        {
            var json = File.ReadAllText(path);
            var node = JsonNode.Parse(json)?.AsObject();
            if (node == null)
                return null;

            return new SubstancePrototype
            {
                Id = node["id"]?.GetValue<string>() ?? "",
                Name = node["name"]?.GetValue<string>() ?? "",
                Color = node["color"]?.GetValue<string>() ?? "#FFFFFFFF",
                DefaultAmount = node["defaultAmount"]?.GetValue<float>() ?? 1f,
                VolumePerUnit = node["volumePerUnit"]?.GetValue<float>() ?? 1f,
                Nutrition = node["nutrition"]?.GetValue<float>() ?? 0f,
                Hydration = node["hydration"]?.GetValue<float>() ?? 0f,
                BladderLoad = node["bladderLoad"]?.GetValue<float>() ?? 0f,
                BowelLoad = node["bowelLoad"]?.GetValue<float>() ?? 0f,
                AbsorptionTime = node["absorptionTime"]?.GetValue<float>() ?? 5f,
                ClearanceTime = node["clearanceTime"]?.GetValue<float>() ?? 60f,
                Smells = DeserializeNode<List<string>>(node["smells"]) ?? new List<string>(),
                PreparationHint = node["preparationHint"]?.GetValue<string>(),
                Effects = DeserializeNode<List<SubstanceEffectDefinition>>(node["effects"]) ?? new List<SubstanceEffectDefinition>(),
                ResponseProfiles = DeserializeNode<List<SubstanceResponseProfile>>(node["responseProfiles"]) ?? new List<SubstanceResponseProfile>(),
                Recipes = DeserializeNode<List<SubstanceRecipeDefinition>>(node["recipes"]) ?? new List<SubstanceRecipeDefinition>()
            };
        }
        catch (Exception e)
        {
            Console.WriteLine($"[SubstancePrototype] Error loading {path}: {e.Message}");
            return null;
        }
    }

    private static T? DeserializeNode<T>(JsonNode? node)
    {
        if (node == null)
            return default;

        return JsonSerializer.Deserialize<T>(node.ToJsonString(), new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
    }
}

public static class SubstanceResolver
{
    public static List<SubstanceDose> ResolveMany(IEnumerable<SubstanceReference>? refs)
    {
        var result = new List<SubstanceDose>();
        if (refs == null || !ServiceLocator.Has<PrototypeManager>())
            return result;

        var prototypes = ServiceLocator.Get<PrototypeManager>();

        foreach (var reference in refs)
        {
            if (string.IsNullOrWhiteSpace(reference.Id))
                continue;

            var proto = prototypes.GetSubstance(reference.Id);
            if (proto == null)
            {
                Console.WriteLine($"[SubstanceResolver] Unknown substance id: {reference.Id}");
                continue;
            }

            var amount = reference.Amount > 0f ? reference.Amount : proto.DefaultAmount;
            result.Add(proto.CreateDose(amount));
        }

        return result;
    }

    public static List<SubstanceDose> MergeById(IEnumerable<SubstanceDose>? doses)
    {
        var result = new List<SubstanceDose>();
        if (doses == null)
            return result;

        foreach (var group in doses
                     .Where(dose => dose.Amount > 0.001f || dose.Volume > 0.001f)
                     .GroupBy(dose => dose.Id, StringComparer.OrdinalIgnoreCase))
        {
            var first = group.First().CloneScaled(1f);
            first.Amount = Math.Max(0f, group.Sum(dose => dose.Amount));
            first.Volume = Math.Max(0f, group.Sum(dose => dose.Volume));
            first.Nutrition = Math.Max(0f, group.Sum(dose => dose.Nutrition));
            first.Hydration = Math.Max(0f, group.Sum(dose => dose.Hydration));
            first.BladderLoad = Math.Max(0f, group.Sum(dose => dose.BladderLoad));
            first.BowelLoad = Math.Max(0f, group.Sum(dose => dose.BowelLoad));

            if (first.Amount > 0.001f || first.Volume > 0.001f)
                result.Add(first);
        }

        return result;
    }
}

public class SubstanceDose
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Color { get; set; } = "#FFFFFFFF";
    public float Amount { get; set; } = 1f;
    public float Volume { get; set; } = 1f;
    public float Nutrition { get; set; }
    public float Hydration { get; set; }
    public float BladderLoad { get; set; }
    public float BowelLoad { get; set; }
    public float AbsorptionTime { get; set; } = 5f;
    public float ClearanceTime { get; set; } = 60f;
    public List<string> Smells { get; set; } = new();
    public string? PreparationHint { get; set; }
    public List<SubstanceEffectDefinition> Effects { get; set; } = new();
    public List<SubstanceResponseProfile> ResponseProfiles { get; set; } = new();
    public List<SubstanceRecipeDefinition> Recipes { get; set; } = new();

    public float EffectiveVolume
    {
        get
        {
            if (Volume > 0.001f)
                return Volume;

            if (Amount > 0.001f)
                return Amount;

            return 0f;
        }
    }

    public SubstanceDose CloneScaled(float factor)
    {
        factor = Math.Clamp(factor, 0f, 1f);
        return new SubstanceDose
        {
            Id = Id,
            Name = Name,
            Color = Color,
            Amount = Amount * factor,
            Volume = EffectiveVolume * factor,
            Nutrition = Nutrition * factor,
            Hydration = Hydration * factor,
            BladderLoad = BladderLoad * factor,
            BowelLoad = BowelLoad * factor,
            AbsorptionTime = AbsorptionTime,
            ClearanceTime = ClearanceTime,
            Smells = new List<string>(Smells),
            PreparationHint = PreparationHint,
            Effects = Effects.Select(effect => effect.Clone()).ToList(),
            ResponseProfiles = ResponseProfiles.Select(profile => profile.Clone()).ToList(),
            Recipes = Recipes.Select(recipe => recipe.Clone()).ToList()
        };
    }

    public ActiveSubstanceDose ToActiveDose(string sourceName)
    {
        return new ActiveSubstanceDose
        {
            Id = Id,
            Name = string.IsNullOrWhiteSpace(Name) ? Id : Name,
            Color = Color,
            Amount = Amount,
            Volume = EffectiveVolume,
            Nutrition = Nutrition,
            Hydration = Hydration,
            BladderLoad = BladderLoad,
            BowelLoad = BowelLoad,
            AbsorptionTime = AbsorptionTime,
            ClearanceTime = ClearanceTime,
            Smells = new List<string>(Smells),
            PreparationHint = PreparationHint,
            Effects = Effects.Select(effect => effect.Clone()).ToList(),
            ResponseProfiles = ResponseProfiles.Select(profile => profile.Clone()).ToList(),
            Recipes = Recipes.Select(recipe => recipe.Clone()).ToList(),
            SourceName = sourceName
        };
    }
}

public class ActiveSubstanceDose : SubstanceDose
{
    public string SourceName { get; set; } = "";
    public float Elapsed { get; set; }
    public HashSet<string> StartedEffects { get; } = new();
    public HashSet<string> FinishedEffects { get; } = new();

    public float AbsorptionProgress
        => AbsorptionTime <= 0f ? 1f : Math.Clamp(Elapsed / AbsorptionTime, 0f, 1f);

    public float ClearanceFactor
    {
        get
        {
            if (ClearanceTime <= 0f)
                return Elapsed < AbsorptionTime ? 1f : 0f;

            if (Elapsed <= AbsorptionTime)
                return 1f;

            return Math.Clamp(1f - ((Elapsed - AbsorptionTime) / ClearanceTime), 0f, 1f);
        }
    }

    public float CurrentAmount => Amount * AbsorptionProgress * ClearanceFactor;

    public float Lifetime
    {
        get
        {
            var effectEnd = Effects.Count == 0
                ? 0f
                : Effects.Max(effect => Math.Max(0f, AbsorptionTime + effect.Delay + effect.Duration));

            return Math.Max(AbsorptionTime + Math.Max(0f, ClearanceTime), effectEnd);
        }
    }

    public bool IsExpired => Elapsed >= Lifetime;
}

public class SubstanceResponseProfile
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public float MinAmount { get; set; }
    public float? MaxAmount { get; set; }
    public List<SubstanceEffectDefinition> Effects { get; set; } = new();

    public bool Matches(float amount)
    {
        if (amount < MinAmount)
            return false;

        if (MaxAmount.HasValue && amount >= MaxAmount.Value)
            return false;

        return true;
    }

    public SubstanceResponseProfile Clone()
    {
        return new SubstanceResponseProfile
        {
            Id = Id,
            Name = Name,
            MinAmount = MinAmount,
            MaxAmount = MaxAmount,
            Effects = Effects.Select(effect => effect.Clone()).ToList()
        };
    }
}

public class SubstanceEffectDefinition
{
    public string Id { get; set; } = "";
    public string Type { get; set; } = "speedMultiplier";
    public float Magnitude { get; set; }
    public float Duration { get; set; } = 1f;
    public float Delay { get; set; }
    public string? Need { get; set; }
    public string? Message { get; set; }
    public string? Color { get; set; }

    public SubstanceEffectDefinition Clone()
    {
        return new SubstanceEffectDefinition
        {
            Id = Id,
            Type = Type,
            Magnitude = Magnitude,
            Duration = Duration,
            Delay = Delay,
            Need = Need,
            Message = Message,
            Color = Color
        };
    }
}

public class SubstanceRecipeDefinition
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public List<SubstanceRequirement> Ingredients { get; set; } = new();
    public List<SubstanceReference> Results { get; set; } = new();

    public SubstanceRecipeDefinition Clone()
    {
        return new SubstanceRecipeDefinition
        {
            Id = Id,
            Name = Name,
            Description = Description,
            Ingredients = Ingredients.Select(ingredient => ingredient.Clone()).ToList(),
            Results = Results.Select(result => new SubstanceReference
            {
                Id = result.Id,
                Amount = result.Amount
            }).ToList()
        };
    }
}

public class SubstanceRequirement
{
    public string SubstanceId { get; set; } = "";
    public float Amount { get; set; } = 1f;

    public SubstanceRequirement Clone()
    {
        return new SubstanceRequirement
        {
            SubstanceId = SubstanceId,
            Amount = Amount
        };
    }
}

public class SubstanceEffectContext
{
    public required Entity Entity { get; init; }
    public required MetabolismComponent Metabolism { get; init; }
    public ActiveSubstanceDose? Substance { get; init; }
    public required SubstanceEffectDefinition Effect { get; init; }
    public required float DeltaTime { get; init; }
    public required string EffectKey { get; init; }
    public required string SubstanceId { get; init; }
    public required string SubstanceName { get; init; }
    public required float CurrentAmount { get; init; }
    public SubstanceResponseProfile? Profile { get; init; }
    public required bool IsActive { get; init; }
    public required bool IsInstant { get; init; }
    public required Func<bool> MarkStarted { get; init; }
    public required Func<bool> MarkFinished { get; init; }

    public bool TryMarkStarted() => MarkStarted();
    public bool TryMarkFinished() => MarkFinished();
}

public class SubstanceConcentrationSnapshot
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public float Amount { get; init; }
    public IReadOnlyList<SubstanceResponseProfile> Profiles { get; init; } = Array.Empty<SubstanceResponseProfile>();
}

public interface ISubstanceEffectHandler
{
    string EffectType { get; }
    void Apply(SubstanceEffectContext context);
}

public static class SubstanceEffectRegistry
{
    private static readonly Dictionary<string, ISubstanceEffectHandler> Handlers =
        new(StringComparer.OrdinalIgnoreCase);

    static SubstanceEffectRegistry()
    {
        Register(new SpeedMultiplierSubstanceEffect());
        Register(new SlowMultiplierSubstanceEffect());
        Register(new NeedDeltaOverTimeSubstanceEffect());
        Register(new PopupSubstanceEffect());
    }

    public static void Register(ISubstanceEffectHandler handler)
    {
        Handlers[handler.EffectType] = handler;
    }

    public static bool TryGet(string effectType, out ISubstanceEffectHandler handler)
        => Handlers.TryGetValue(effectType, out handler!);
}

public sealed class SpeedMultiplierSubstanceEffect : ISubstanceEffectHandler
{
    public string EffectType => "speedMultiplier";

    public void Apply(SubstanceEffectContext context)
    {
        if (!context.IsActive)
            return;

        context.Metabolism.SubstanceSpeedModifier *= 1f + context.Effect.Magnitude;
    }
}

public sealed class SlowMultiplierSubstanceEffect : ISubstanceEffectHandler
{
    public string EffectType => "slowMultiplier";

    public void Apply(SubstanceEffectContext context)
    {
        if (!context.IsActive)
            return;

        var penalty = Math.Clamp(context.Effect.Magnitude, 0f, 1f);
        context.Metabolism.SubstanceSpeedModifier *= 1f - penalty;
    }
}

public sealed class NeedDeltaOverTimeSubstanceEffect : ISubstanceEffectHandler
{
    public string EffectType => "needDeltaOverTime";

    public void Apply(SubstanceEffectContext context)
    {
        if (!context.IsActive)
            return;

        var duration = Math.Max(context.Effect.Duration, 0.01f);
        var delta = context.Effect.Magnitude * (context.DeltaTime / duration);

        switch (context.Effect.Need?.Trim().ToLowerInvariant())
        {
            case "hunger":
                context.Metabolism.Hunger = Math.Clamp(context.Metabolism.Hunger + delta, 0f, 100f);
                break;
            case "thirst":
                context.Metabolism.Thirst = Math.Clamp(context.Metabolism.Thirst + delta, 0f, 100f);
                break;
            case "bladder":
                context.Metabolism.Bladder = Math.Clamp(context.Metabolism.Bladder + delta, 0f, 100f);
                break;
            case "bowel":
                context.Metabolism.Bowel = Math.Clamp(context.Metabolism.Bowel + delta, 0f, 100f);
                break;
        }
    }
}

public sealed class PopupSubstanceEffect : ISubstanceEffectHandler
{
    public string EffectType => "popup";

    public void Apply(SubstanceEffectContext context)
    {
        if (!context.IsActive || !context.TryMarkStarted())
            return;

        if (string.IsNullOrWhiteSpace(context.Effect.Message))
            return;

        var color = AssetManager.ParseHexColor(context.Effect.Color, Color.LightGoldenrodYellow);
        Systems.PopupTextSystem.Show(context.Entity, context.Effect.Message!, color, lifetime: 1.75f);
    }
}
