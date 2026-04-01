namespace MTEngine.ECS;

public class World
{
    private readonly List<Entity> _entities = new();
    private readonly List<Entity> _toAdd = new();
    private readonly List<Entity> _toRemove = new();
    private readonly List<GameSystem> _systems = new();

    public Entity CreateEntity(string name = "Entity")
    {
        var e = new Entity(name);
        _toAdd.Add(e);
        return e;
    }

    public void DestroyEntity(Entity entity)
    {
        _toRemove.Add(entity);
    }

    public IEnumerable<Entity> GetEntities() => _entities;

    public IEnumerable<Entity> GetEntitiesWith<T>() where T : Component
    {
        return _entities.Where(e => e.Active && e.HasComponent<T>());
    }

    public IEnumerable<Entity> GetEntitiesWith<T1, T2>()
        where T1 : Component where T2 : Component
    {
        return _entities.Where(e => e.Active && e.HasComponent<T1>() && e.HasComponent<T2>());
    }

    public T AddSystem<T>(T system) where T : GameSystem
    {
        system.Initialize(this);
        _systems.Add(system);
        return system;
    }

    public T? GetSystem<T>() where T : GameSystem
    {
        return _systems.OfType<T>().FirstOrDefault();
    }

    public void Update(float deltaTime)
    {
        foreach (var e in _toAdd) _entities.Add(e);
        _toAdd.Clear();

        foreach (var e in _toRemove) _entities.Remove(e);
        _toRemove.Clear();

        foreach (var system in _systems)
            if (system.Enabled) system.Update(deltaTime);
    }

    public void Draw()
    {
        foreach (var system in _systems)
            if (system.Enabled) system.Draw();
    }
}