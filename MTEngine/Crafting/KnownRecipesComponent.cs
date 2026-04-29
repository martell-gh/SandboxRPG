using System;
using System.Collections.Generic;
using System.Linq;
using MTEngine.ECS;

namespace MTEngine.Crafting;

[RegisterComponent("knownRecipes")]
public class KnownRecipesComponent : Component
{
    [DataField("recipes")]
    [SaveField("recipes")]
    public List<string> Recipes { get; set; } = new();

    public bool Knows(string? recipeId)
    {
        if (string.IsNullOrWhiteSpace(recipeId))
            return false;

        return Recipes.Any(id => string.Equals(id, recipeId, StringComparison.OrdinalIgnoreCase));
    }

    public bool Learn(string? recipeId)
    {
        var normalized = Normalize(recipeId);
        if (string.IsNullOrWhiteSpace(normalized) || Knows(normalized))
            return false;

        Recipes.Add(normalized);
        return true;
    }

    public int LearnMany(IEnumerable<string> recipeIds)
    {
        var learned = 0;
        foreach (var recipeId in recipeIds)
        {
            if (Learn(recipeId))
                learned++;
        }

        return learned;
    }

    private static string Normalize(string? recipeId)
        => recipeId?.Trim() ?? string.Empty;
}
