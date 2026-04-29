using MTEngine.ECS;

namespace MTEngine.Npc;

/// <summary>
/// Runtime-only pause used while a player interaction is being completed.
/// </summary>
[RegisterComponent("npcInteractionHold")]
public class NpcInteractionHoldComponent : Component
{
    public int ActorId { get; set; }
}
