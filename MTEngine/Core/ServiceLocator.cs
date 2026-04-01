namespace MTEngine.Core;

public static class ServiceLocator
{
    private static readonly Dictionary<Type, object> _services = new();

    public static void Register<T>(T service) where T : class
    {
        _services[typeof(T)] = service;
    }

    public static T Get<T>() where T : class
    {
        if (_services.TryGetValue(typeof(T), out var s))
            return (T)s;
        throw new Exception($"Service {typeof(T).Name} not registered!");
    }

    public static bool Has<T>() where T : class
    {
        return _services.ContainsKey(typeof(T));
    }
}