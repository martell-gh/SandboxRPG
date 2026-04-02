namespace MTEngine.Interactions;

/// <summary>
/// A single action shown in the interaction context menu.
/// Created by components that implement IInteractionSource.
/// </summary>
public class InteractionEntry
{
    /// <summary>Unique id for this action (e.g. "dispenser.pour", "container.open").</summary>
    public string Id { get; init; } = "";

    /// <summary>Label shown in the menu UI.</summary>
    public string Label { get; init; } = "";

    /// <summary>
    /// The callback that executes when the player clicks this action.
    /// Receives the same InteractionContext that was used to create it.
    /// </summary>
    public Action<InteractionContext>? Execute { get; init; }

    /// <summary>
    /// Optional priority for ordering in the menu. Higher = closer to top.
    /// Default is 0.
    /// </summary>
    public int Priority { get; init; } = 0;
}
