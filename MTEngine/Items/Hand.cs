using MTEngine.ECS;

namespace MTEngine.Items;

/// <summary>
/// A single hand slot. Holds one item entity (or null if empty).
/// </summary>
public class Hand
{
    public string Name { get; set; }
    public Entity? HeldItem { get; set; }

    /// <summary>
    /// True if this hand is blocked by a two-handed item held in another hand.
    /// </summary>
    public bool BlockedByTwoHanded { get; set; }

    public bool IsFree => HeldItem == null && !BlockedByTwoHanded;

    public Hand(string name)
    {
        Name = name;
    }
}
