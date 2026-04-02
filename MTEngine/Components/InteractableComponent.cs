using MTEngine.ECS;

namespace MTEngine.Components;

public class InteractionAction
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public Action<Entity, Entity>? Execute { get; set; }
}


public class InteractableComponent : Component
{
    public string DisplayName { get; set; } = "Object";
    public float InteractRange { get; set; } = 64f; // пиксели

    public List<InteractionAction> Actions { get; } = new();

    public void AddAction(string id, string label, Action<Entity, Entity> execute)
    {
        Actions.Add(new InteractionAction { Id = id, Label = label, Execute = execute });
    }
}