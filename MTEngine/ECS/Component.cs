namespace MTEngine.ECS;

public abstract class Component
{
    public Entity? Owner { get; internal set; }
    public bool Enabled { get; set; } = true;
}