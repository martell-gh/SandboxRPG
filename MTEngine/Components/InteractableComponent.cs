using MTEngine.ECS;

namespace MTEngine.Components;

/// <summary>
/// Marker component that makes an entity interactable via right-click.
/// Actions are NOT stored here — they come from other components
/// that implement IInteractionSource.
/// </summary>
[RegisterComponent("interactable")]
public class InteractableComponent : Component
{
    /// <summary>Name shown in the interaction menu header.</summary>
    [DataField("name")]
    [SaveField("name")]
    public string DisplayName { get; set; } = "Object";

    /// <summary>Max distance (in pixels) from which the player can interact.</summary>
    [DataField("range")]
    [SaveField("range")]
    public float InteractRange { get; set; } = 64f;
}
