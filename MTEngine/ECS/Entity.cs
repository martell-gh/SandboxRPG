namespace MTEngine.ECS;

public class Entity
{
    private static int _nextId = 0;

    public int Id { get; } = _nextId++;
    public string Name { get; set; }
    public bool Active { get; set; } = true;

    private readonly Dictionary<Type, Component> _components = new();

    public Entity(string name = "Entity")
    {
        Name = name;
    }

    public T AddComponent<T>(T component) where T : Component
    {
        component.Owner = this;
        _components[typeof(T)] = component;
        return component;
    }

    public Component AddComponent(Component component)
    {
        component.Owner = this;
        _components[component.GetType()] = component;
        return component;
    }

    public T? GetComponent<T>() where T : Component
    {
        return _components.TryGetValue(typeof(T), out var c) ? (T)c : null;
    }

    public Component? GetComponent(Type componentType)
    {
        return _components.TryGetValue(componentType, out var c) ? c : null;
    }

    public bool HasComponent<T>() where T : Component
    {
        return _components.ContainsKey(typeof(T));
    }

    public bool HasComponent(Type componentType)
    {
        return _components.ContainsKey(componentType);
    }

    public void RemoveComponent<T>() where T : Component
    {
        _components.Remove(typeof(T));
    }

    public IEnumerable<Component> GetAllComponents() => _components.Values;
}