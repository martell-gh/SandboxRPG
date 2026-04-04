using System;
using System.Collections.Generic;
using System.Linq;
using MTEngine.ECS;

namespace MTEngine.Metabolism;

[RegisterComponent("knowledgeMemory")]
public class KnowledgeMemoryComponent : Component
{
    public HashSet<string> ReadBookIds { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<SubstanceLoreEntry> KnownSubstances { get; } = new();
    public List<KnownRecipeEntry> KnownRecipes { get; } = new();

    public bool LearnBook(KnowledgeBookComponent book)
    {
        var wasNew = ReadBookIds.Add(GetBookKey(book));

        foreach (var substance in book.Substances)
        {
            if (KnownSubstances.Any(existing => string.Equals(existing.Id, substance.Id, StringComparison.OrdinalIgnoreCase)))
                continue;

            KnownSubstances.Add(new SubstanceLoreEntry
            {
                Id = substance.Id,
                Name = substance.Name,
                Preparation = substance.Preparation,
                Smell = substance.Smell
            });
            wasNew = true;
        }

        foreach (var recipe in book.Recipes)
        {
            if (KnownRecipes.Any(existing => string.Equals(existing.Id, recipe.Id, StringComparison.OrdinalIgnoreCase)))
                continue;

            KnownRecipes.Add(new KnownRecipeEntry
            {
                Id = recipe.Id,
                Name = recipe.Name,
                Description = recipe.Description
            });
            wasNew = true;
        }

        return wasNew;
    }

    public string DescribeSubstanceCatalog()
    {
        if (KnownSubstances.Count == 0 && KnownRecipes.Count == 0)
            return "Справочник памяти пуст.";

        var lines = new List<string>();

        if (KnownSubstances.Count > 0)
        {
            lines.Add("Вещества:");
            lines.AddRange(KnownSubstances
                .OrderBy(entry => entry.Name)
                .Select(entry => $"- {entry.Name}: {entry.Preparation}"));
        }

        if (KnownRecipes.Count > 0)
        {
            lines.Add("Рецепты:");
            lines.AddRange(KnownRecipes
                .OrderBy(entry => entry.Name)
                .Select(entry => $"- {entry.Name}: {entry.Description}"));
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string GetBookKey(KnowledgeBookComponent book)
    {
        if (!string.IsNullOrWhiteSpace(book.BookId))
            return book.BookId;

        return string.IsNullOrWhiteSpace(book.Title) ? "book" : book.Title;
    }
}
