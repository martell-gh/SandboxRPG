using System.Reflection;
using MTEngine.ECS;

namespace MTEngine.Core;

public static class ComponentRegistry
{
    private static readonly Dictionary<string, Type> _registered = new(StringComparer.OrdinalIgnoreCase);
    private static bool _initialized;

    public static void EnsureInitialized()
    {
        if (_initialized) return;
        _initialized = true;

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            RegisterAssembly(assembly);
        }
    }

    public static void RegisterAssembly(Assembly assembly)
    {
        Type[] types;

        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException e)
        {
            types = e.Types.Where(t => t != null).Cast<Type>().ToArray();
        }

        foreach (var type in types)
        {
            if (type.IsAbstract) continue;
            if (!typeof(Component).IsAssignableFrom(type)) continue;

            var attr = type.GetCustomAttribute<RegisterComponentAttribute>();
            if (attr == null) continue;

            _registered[attr.Name] = type;
        }
    }

    public static Type? GetComponentType(string name)
    {
        EnsureInitialized();
        return _registered.TryGetValue(name, out var type) ? type : null;
    }

    public static IReadOnlyDictionary<string, Type> GetAll()
    {
        EnsureInitialized();
        return _registered;
    }
}