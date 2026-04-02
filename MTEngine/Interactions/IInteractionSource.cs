using MTEngine.ECS;

namespace MTEngine.Interactions;

/// <summary>
/// Any component can implement this to contribute actions to the interaction menu.
/// When a player right-clicks an entity, InteractionSystem collects actions
/// from ALL components on that entity that implement this interface.
/// </summary>
public interface IInteractionSource
{
    /// <summary>
    /// Return the interaction actions this component wants to add to the menu.
    /// Called every time the menu opens, so conditions are evaluated live.
    /// Return empty to add nothing (e.g. if conditions aren't met).
    /// </summary>
    IEnumerable<InteractionEntry> GetInteractions(InteractionContext ctx);
}
