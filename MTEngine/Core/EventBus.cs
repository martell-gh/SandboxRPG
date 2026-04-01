namespace MTEngine.Core;

public class EventBus
{
    private readonly Dictionary<Type, List<Delegate>> _handlers = new();

    public void Subscribe<T>(Action<T> handler)
    {
        var type = typeof(T);
        if (!_handlers.ContainsKey(type))
            _handlers[type] = new List<Delegate>();
        _handlers[type].Add(handler);
    }

    public void Unsubscribe<T>(Action<T> handler)
    {
        var type = typeof(T);
        if (_handlers.TryGetValue(type, out var list))
            list.Remove(handler);
    }

    public void Publish<T>(T eventData)
    {
        var type = typeof(T);
        if (_handlers.TryGetValue(type, out var list))
            foreach (var handler in list.ToList())
                ((Action<T>)handler)(eventData);
    }
}