using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Xna.Framework;
using MTEngine.ECS;

namespace MTEngine.Core;

public static class ComponentPrototypeSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        IncludeFields = true
    };

    public static Component Deserialize(Type componentType, JsonObject data)
    {
        if (!typeof(Component).IsAssignableFrom(componentType))
            throw new InvalidOperationException($"{componentType.Name} is not a Component.");

        if (Activator.CreateInstance(componentType) is not Component instance)
            throw new InvalidOperationException($"Failed to create component: {componentType.Name}");

        ApplyData(instance, data);
        return instance;
    }

    public static void ApplyData(Component instance, JsonObject data)
    {
        var type = instance.GetType();

        foreach (var prop in GetAllProperties(type))
        {
            if (!prop.CanWrite) continue;

            var attr = prop.GetCustomAttribute<DataFieldAttribute>();
            if (attr == null) continue;

            var key = attr.Name ?? prop.Name;
            if (!data.TryGetPropertyValue(key, out var node) || node == null) continue;

            var value = ConvertNode(node, prop.PropertyType);
            prop.SetValue(instance, value);
        }

        foreach (var field in GetAllFields(type))
        {
            var attr = field.GetCustomAttribute<DataFieldAttribute>();
            if (attr == null) continue;

            var key = attr.Name ?? field.Name;
            if (!data.TryGetPropertyValue(key, out var node) || node == null) continue;

            var value = ConvertNode(node, field.FieldType);
            field.SetValue(instance, value);
        }
    }

    private static IEnumerable<PropertyInfo> GetAllProperties(Type type)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var seen = new HashSet<string>();

        while (type != null && type != typeof(object))
        {
            foreach (var prop in type.GetProperties(flags))
            {
                if (seen.Add($"{prop.DeclaringType?.FullName}:{prop.Name}"))
                    yield return prop;
            }

            type = type.BaseType!;
        }
    }

    private static IEnumerable<FieldInfo> GetAllFields(Type type)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var seen = new HashSet<string>();

        while (type != null && type != typeof(object))
        {
            foreach (var field in type.GetFields(flags))
            {
                if (seen.Add($"{field.DeclaringType?.FullName}:{field.Name}"))
                    yield return field;
            }

            type = type.BaseType!;
        }
    }

    private static object? ConvertNode(JsonNode node, Type targetType)
    {
        var underlying = Nullable.GetUnderlyingType(targetType) ?? targetType;

        if (underlying == typeof(string))
            return node.GetValue<string>();

        if (underlying == typeof(int))
            return node.GetValue<int>();

        if (underlying == typeof(float))
            return node.GetValue<float>();

        if (underlying == typeof(double))
            return node.GetValue<double>();

        if (underlying == typeof(bool))
            return node.GetValue<bool>();

        if (underlying == typeof(long))
            return node.GetValue<long>();

        if (underlying == typeof(byte))
            return node.GetValue<byte>();

        if (underlying == typeof(Vector2))
        {
            var obj = node.AsObject();
            var x = obj["x"]?.GetValue<float>() ?? 0f;
            var y = obj["y"]?.GetValue<float>() ?? 0f;
            return new Vector2(x, y);
        }

        if (underlying.IsEnum)
        {
            if (node is JsonValue)
            {
                try
                {
                    var str = node.GetValue<string>();
                    return Enum.Parse(underlying, str, ignoreCase: true);
                }
                catch
                {
                    var num = node.GetValue<int>();
                    return Enum.ToObject(underlying, num);
                }
            }
        }

        return JsonSerializer.Deserialize(node.ToJsonString(), targetType, JsonOptions);
    }
}
