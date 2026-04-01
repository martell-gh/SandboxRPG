namespace MTEngine.ECS;

public enum DrawLayer { Scene, Overlay }

public abstract class GameSystem
{
    protected World World { get; private set; } = null!;
    public bool Enabled { get; set; } = true;

    // Scene = рисуется в SceneRT, Overlay = поверх без освещения
    public virtual DrawLayer DrawLayer => DrawLayer.Scene;

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