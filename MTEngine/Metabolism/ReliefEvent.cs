using MTEngine.ECS;

namespace MTEngine.Metabolism;

/// <summary>
/// Fired on EventBus whenever an entity relieves itself.
/// Subscribe to this to make NPCs react, add mood penalties, etc.
/// </summary>
public class ReliefEvent
{
    /// <summary>Who did it.</summary>
    public required Entity Actor { get; init; }

    /// <summary>Bladder or Bowel.</summary>
    public required ReliefNeed Need { get; init; }

    /// <summary>Was this done in an acceptable way (toilet) or not (self/accident)?</summary>
    public required ReliefType Type { get; init; }
}

public enum ReliefNeed
{
    Bladder,
    Bowel
}

/// <summary>
/// Acceptable = toilet, sink, designated place.
/// Unacceptable = on yourself, in public, accident.
/// </summary>
public enum ReliefType
{
    Acceptable,
    Unacceptable
}
