using System.Collections.Generic;
using MTEngine.ECS;

namespace MTEngine.Crafting;

public sealed class CraftIngredient
{
    public string PrototypeId { get; set; } = "";
    public int Count { get; set; } = 1;
}

[RegisterComponent("craftable")]
public class CraftableComponent : Component
{
    [DataField("recipeId")]
    [SaveField("recipeId")]
    public string RecipeId { get; set; } = "";

    [DataField("requiresRecipe")]
    [SaveField("requiresRecipe")]
    public bool RequiresRecipe { get; set; }

    [DataField("recipeFamily")]
    [SaveField("recipeFamily")]
    public string RecipeFamily { get; set; } = "";

    [DataField("recipeTier")]
    [SaveField("recipeTier")]
    public int RecipeTier { get; set; }

    [DataField("discoverableBySmelting")]
    [SaveField("discoverableBySmelting")]
    public bool DiscoverableBySmelting { get; set; }

    [DataField("station")]
    [SaveField("station")]
    public string StationId { get; set; } = "";

    [DataField("skill")]
    [SaveField("skill")]
    public Combat.SkillType Skill { get; set; } = Combat.SkillType.Craftsmanship;

    [DataField("requiredSkill")]
    [SaveField("requiredSkill")]
    public float RequiredSkill { get; set; } = 0f;

    [DataField("craftTime")]
    [SaveField("craftTime")]
    public float CraftTimeSeconds { get; set; } = 1.8f;

    [DataField("ingredients")]
    [SaveField("ingredients")]
    public List<CraftIngredient> Ingredients { get; set; } = new();

    public string ResolveRecipeId(string fallbackPrototypeId)
        => string.IsNullOrWhiteSpace(RecipeId) ? fallbackPrototypeId : RecipeId.Trim();
}
