namespace MTEngine.Interactions;

public sealed class InteractionDelay
{
    public float Duration { get; init; }
    public string? ProgressLabel { get; init; }
    public bool CancelOnMove { get; init; } = true;
    public bool CancelOnOtherAction { get; init; } = true;

    public static InteractionDelay Seconds(float duration, string? progressLabel = null)
        => new()
        {
            Duration = duration,
            ProgressLabel = progressLabel
        };
}

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

    /// <summary>
    /// Optional do-after configuration. If set, the action completes only after the delay.
    /// </summary>
    public InteractionDelay? Delay { get; init; }

    /// <summary>
    /// Whether executing this action should interrupt the current delayed action.
    /// Default is true. Set false for lightweight actions like "Инфо".
    /// </summary>
    public bool InterruptsCurrentAction { get; init; } = true;
}
