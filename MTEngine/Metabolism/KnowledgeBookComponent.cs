using System;
using System.Collections.Generic;
using System.Linq;
using MTEngine.Core;
using MTEngine.ECS;
using MTEngine.Interactions;
using MTEngine.Items;

namespace MTEngine.Metabolism;

[RegisterComponent("knowledgeBook")]
public class KnowledgeBookComponent : Component, IInteractionSource, IPrototypeInitializable
{
    [DataField("bookId")]
    public string BookId { get; set; } = "";

    [DataField("title")]
    public string Title { get; set; } = "Book";

    [DataField("category")]
    public string Category { get; set; } = "substances";

    [DataField("readVerb")]
    public string ReadVerb { get; set; } = "Прочитать";

    [DataField("substances")]
    public List<string> SubstanceIds { get; set; } = new();

    [DataField("recipes")]
    public List<KnownRecipeEntry> Recipes { get; set; } = new();

    public List<SubstanceLoreEntry> Substances { get; private set; } = new();

    public void InitializeFromPrototype(EntityPrototype proto, AssetManager assets)
    {
        Substances.Clear();

        if (!ServiceLocator.Has<PrototypeManager>())
            return;

        var prototypes = ServiceLocator.Get<PrototypeManager>();
        foreach (var id in SubstanceIds.Where(id => !string.IsNullOrWhiteSpace(id)))
        {
            var substance = prototypes.GetSubstance(id);
            if (substance == null)
                continue;

            Substances.Add(substance.ToLoreEntry());
        }
    }

    public IEnumerable<InteractionEntry> GetInteractions(InteractionContext ctx)
    {
        var item = Owner?.GetComponent<ItemComponent>();
        if (item == null || ctx.Target != Owner || item.ContainedIn != ctx.Actor)
            yield break;

        yield return new InteractionEntry
        {
            Id = "knowledgeBook.read",
            Label = $"{ReadVerb} ({Title})",
            Priority = 24,
            Execute = c => Read(c.Actor)
        };
    }

    public void Read(Entity actor)
    {
        var memory = actor.GetComponent<KnowledgeMemoryComponent>();
        if (memory == null)
            memory = actor.AddComponent(new KnowledgeMemoryComponent());

        var learnedAnything = memory.LearnBook(this);
        var message = learnedAnything
            ? $"Запомнил: {Title}"
            : $"Уже знаю: {Title}";

        Systems.PopupTextSystem.Show(actor, message, Microsoft.Xna.Framework.Color.LightSkyBlue, lifetime: 1.75f);
        Console.WriteLine($"[Knowledge] {actor.Name} read '{Title}' ({Category})");
    }
}
