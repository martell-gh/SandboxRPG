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
}
