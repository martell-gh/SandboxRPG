#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MTEngine.Core;
using MTEngine.ECS;
using MTEngine.Rendering;

namespace SandboxGame.Save;

public static class SaveComponentSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        IncludeFields = true,
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static ComponentSaveData Serialize(Component component)
    {
        var type = component.GetType();

        return new ComponentSaveData
        {
            TypeId = ResolveComponentTypeId(type),
            ClrType = type.AssemblyQualifiedName,
            Data = SerializeObject(component)
        };
    }

    public static void Apply(Component component, JsonObject data)
    {
        ApplyObject(component, data);
    }

    public static JsonObject SerializeObject(object instance)
    {
        var data = new JsonObject();
        var type = instance.GetType();

        foreach (var prop in GetSerializableProperties(type))
        {
            var value = prop.GetValue(instance);
            if (!TryCreateNode(prop.PropertyType, value, out var node) || node == null)
                continue;

            data[GetSerializedName(prop)] = node;
        }

        foreach (var field in GetSerializableFields(type))
        {
            var value = field.GetValue(instance);
            if (!TryCreateNode(field.FieldType, value, out var node) || node == null)
                continue;

            data[GetSerializedName(field)] = node;
        }

        return data;
    }

    public static void ApplyObject(object instance, JsonObject data)
    {
        var type = instance.GetType();

        foreach (var prop in GetSerializableProperties(type))
        {
            if (!data.TryGetPropertyValue(GetSerializedName(prop), out var node) || node == null)
                continue;

            var value = ConvertNode(node, prop.PropertyType);
            if (prop.CanWrite)
            {
                if (value != null || IsNullable(prop.PropertyType))
                    prop.SetValue(instance, value);
            }
            else
            {
                TryPopulateExistingInstance(prop.GetValue(instance), value);
            }
        }

        foreach (var field in GetSerializableFields(type))
        {
            if (!data.TryGetPropertyValue(GetSerializedName(field), out var node) || node == null)
                continue;

            var value = ConvertNode(node, field.FieldType);
            if (value != null || IsNullable(field.FieldType))
                field.SetValue(instance, value);
        }
    }

    public static bool HasSerializableMembers(object instance)
        => HasSerializableMembers(instance.GetType());

    public static bool HasSerializableMembers(Type type)
        => GetSerializableProperties(type).Any() || GetSerializableFields(type).Any();

    public static string ResolveObjectId(object instance)
    {
        var type = instance.GetType();
        return type.GetCustomAttribute<SaveObjectAttribute>()?.Id
               ?? type.FullName
               ?? type.Name;
    }

    public static Component CreateComponent(ComponentSaveData save)
    {
        var type = ResolveComponentType(save);
        if (type == null || !typeof(Component).IsAssignableFrom(type))
            throw new InvalidOperationException($"Unknown component type: {save.TypeId} / {save.ClrType}");

        if (Activator.CreateInstance(type) is not Component component)
            throw new InvalidOperationException($"Failed to instantiate component: {type.FullName}");

        return component;
    }

    public static string ResolveComponentTypeId(Type type)
    {
        var registerAttr = type.GetCustomAttribute<RegisterComponentAttribute>();
        if (registerAttr != null)
            return registerAttr.Name;

        var protoAttr = type.GetCustomAttribute<PrototypeComponentAttribute>();
        if (protoAttr != null)
            return protoAttr.Name;

        return type.FullName ?? type.Name;
    }

    private static Type? ResolveComponentType(ComponentSaveData save)
    {
        var registryType = ComponentRegistry.GetComponentType(save.TypeId);
        if (registryType != null)
            return registryType;

        if (!string.IsNullOrWhiteSpace(save.ClrType))
            return Type.GetType(save.ClrType!, throwOnError: false);

        return null;
    }

    private static IEnumerable<PropertyInfo> GetSerializableProperties(Type type)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        return GetAllProperties(type, flags)
            .Where(prop => prop.CanRead
                && prop.GetIndexParameters().Length == 0
                && HasSaveMarker(prop)
                && IsSupportedMemberType(prop.PropertyType)
                && !ShouldSkipMember(prop.Name, prop.PropertyType));
    }

    private static IEnumerable<FieldInfo> GetSerializableFields(Type type)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        return GetAllFields(type, flags)
            .Where(field => !field.IsInitOnly
                && HasSaveMarker(field)
                && IsSupportedMemberType(field.FieldType)
                && !ShouldSkipMember(field.Name, field.FieldType));
    }

    private static IEnumerable<PropertyInfo> GetAllProperties(Type type, BindingFlags flags)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        Type? currentType = type;
        while (currentType != null && currentType != typeof(object))
        {
            foreach (var prop in currentType.GetProperties(flags))
            {
                if (seen.Add($"{prop.DeclaringType?.FullName}:{prop.Name}"))
                    yield return prop;
            }

            currentType = currentType.BaseType;
        }
    }

    private static IEnumerable<FieldInfo> GetAllFields(Type type, BindingFlags flags)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        Type? currentType = type;
        while (currentType != null && currentType != typeof(object))
        {
            foreach (var field in currentType.GetFields(flags))
            {
                if (seen.Add($"{field.DeclaringType?.FullName}:{field.Name}"))
                    yield return field;
            }

            currentType = currentType.BaseType;
        }
    }

    private static bool HasSaveMarker(MemberInfo member)
        => member.GetCustomAttribute<SaveFieldAttribute>() != null;

    private static string GetSerializedName(MemberInfo member)
        => member.GetCustomAttribute<SaveFieldAttribute>()?.Name
           ?? member.Name;

    private static bool ShouldSkipMember(string name, Type type)
    {
        if (string.Equals(name, nameof(Component.Owner), StringComparison.Ordinal))
            return true;

        if (type == typeof(Texture2D)
            || type == typeof(AnimationPlayer)
            || type == typeof(AnimationSet)
            || typeof(Entity).IsAssignableFrom(type))
            return true;

        if (IsEntityCollection(type))
            return true;

        return name switch
        {
            "ContainedIn" => true,
            "Hands" => true,
            "Slots" => true,
            "ActiveHand" => true,
            "ActiveItem" => true,
            "IsFree" => true,
            _ => false
        };
    }

    private static bool IsSupportedMemberType(Type type)
    {
        if (type == typeof(string)
            || type == typeof(int)
            || type == typeof(float)
            || type == typeof(double)
            || type == typeof(long)
            || type == typeof(bool)
            || type == typeof(byte)
            || type == typeof(Color)
            || type == typeof(Vector2)
            || type == typeof(Rectangle))
            return true;

        if (type.IsEnum)
            return true;

        var underlying = Nullable.GetUnderlyingType(type);
        if (underlying != null)
            return IsSupportedMemberType(underlying);

        if (ShouldSkipMember("", type))
            return false;

        return true;
    }

    private static bool TryCreateNode(Type type, object? value, out JsonNode? node)
    {
        node = null;
        if (value == null)
            return false;

        var underlying = Nullable.GetUnderlyingType(type) ?? type;

        if (underlying == typeof(Color))
        {
            var color = (Color)value;
            node = new JsonObject
            {
                ["r"] = color.R,
                ["g"] = color.G,
                ["b"] = color.B,
                ["a"] = color.A
            };
            return true;
        }

        if (underlying == typeof(Vector2))
        {
            var vector = (Vector2)value;
            node = new JsonObject
            {
                ["x"] = vector.X,
                ["y"] = vector.Y
            };
            return true;
        }

        if (underlying == typeof(Rectangle))
        {
            var rect = (Rectangle)value;
            node = new JsonObject
            {
                ["x"] = rect.X,
                ["y"] = rect.Y,
                ["width"] = rect.Width,
                ["height"] = rect.Height
            };
            return true;
        }

        if (typeof(Entity).IsAssignableFrom(underlying) || IsEntityCollection(underlying))
            return false;

        node = JsonSerializer.SerializeToNode(value, underlying, JsonOptions);
        return node != null;
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
        if (underlying == typeof(long))
            return node.GetValue<long>();
        if (underlying == typeof(bool))
            return node.GetValue<bool>();
        if (underlying == typeof(byte))
            return node.GetValue<byte>();
        if (underlying.IsEnum)
        {
            if (node is JsonValue)
            {
                try { return Enum.Parse(underlying, node.GetValue<string>(), true); }
                catch { return Enum.ToObject(underlying, node.GetValue<int>()); }
            }
        }

        if (underlying == typeof(Color))
        {
            var obj = node.AsObject();
            return new Color(
                obj["r"]?.GetValue<byte>() ?? (byte)255,
                obj["g"]?.GetValue<byte>() ?? (byte)255,
                obj["b"]?.GetValue<byte>() ?? (byte)255,
                obj["a"]?.GetValue<byte>() ?? (byte)255);
        }

        if (underlying == typeof(Vector2))
        {
            var obj = node.AsObject();
            return new Vector2(
                obj["x"]?.GetValue<float>() ?? 0f,
                obj["y"]?.GetValue<float>() ?? 0f);
        }

        if (underlying == typeof(Rectangle))
        {
            var obj = node.AsObject();
            return new Rectangle(
                obj["x"]?.GetValue<int>() ?? 0,
                obj["y"]?.GetValue<int>() ?? 0,
                obj["width"]?.GetValue<int>() ?? 0,
                obj["height"]?.GetValue<int>() ?? 0);
        }

        return JsonSerializer.Deserialize(node.ToJsonString(), targetType, JsonOptions);
    }

    private static bool IsEntityCollection(Type type)
    {
        if (type == typeof(string))
            return false;
        if (!typeof(IEnumerable).IsAssignableFrom(type))
            return false;
        if (!type.IsGenericType)
            return false;
        var genericArg = type.GetGenericArguments().FirstOrDefault();
        return genericArg != null && typeof(Entity).IsAssignableFrom(genericArg);
    }

    private static bool IsNullable(Type type)
        => !type.IsValueType || Nullable.GetUnderlyingType(type) != null;

    private static bool TryPopulateExistingInstance(object? existing, object? value)
    {
        if (existing == null || value == null)
            return false;

        if (existing is IDictionary existingDict && value is IDictionary valueDict)
        {
            existingDict.Clear();
            foreach (DictionaryEntry entry in valueDict)
                existingDict[entry.Key] = entry.Value;
            return true;
        }

        if (existing is IList existingList && value is IEnumerable enumerable && value is not string)
        {
            existingList.Clear();
            foreach (var item in enumerable)
                existingList.Add(item);
            return true;
        }

        var clearMethod = existing.GetType().GetMethod("Clear", Type.EmptyTypes);
        var addMethod = existing.GetType().GetMethods()
            .FirstOrDefault(method => method.Name == "Add" && method.GetParameters().Length == 1);

        if (clearMethod != null && addMethod != null && value is IEnumerable genericEnumerable && value is not string)
        {
            clearMethod.Invoke(existing, null);
            foreach (var item in genericEnumerable)
                addMethod.Invoke(existing, new[] { item });
            return true;
        }

        return false;
    }
}
