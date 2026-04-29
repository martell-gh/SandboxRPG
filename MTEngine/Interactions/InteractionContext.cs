using MTEngine.ECS;

namespace MTEngine.Interactions;

/// <summary>
/// Context passed to IInteractionSource.GetInteractions() and to InteractionEntry.Execute().
/// Contains everything a component might need to decide what actions to show
/// and to execute them.
/// </summary>
public class InteractionContext
{
    /// <summary>The entity performing the interaction (usually the player).</summary>
    public required Entity Actor { get; init; }

    /// <summary>The entity being interacted with.</summary>
    public required Entity Target { get; init; }

    /// <summary>The game world — use to query other entities, distances, etc.</summary>
    public required ECS.World World { get; init; }

    /// <summary>
    /// The entity the user originally clicked on. Set when a self/item context is synthesized
    /// from a held item so components can decide whether to surface self-only actions in a
    /// menu attached to a different target.
    /// </summary>
    public Entity? OriginalTarget { get; init; }
}
