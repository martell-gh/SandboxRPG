namespace MTEngine.ECS;

public abstract class GameSystem
{
    protected World World { get; private set; } = null!;
    public bool Enabled { get; set; } = true;

    internal void Initialize(World world)
    {
        World = world;
        OnInitialize();
    }

    public virtual void OnInitialize() { }
    public virtual void Update(float deltaTime) { }
    public virtual void Draw() { }
    public virtual void OnDestroy() { }
}