using System;

namespace MTEngine.ECS;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class PrototypeComponentAttribute : Attribute
{
    public string Name { get; }

    public PrototypeComponentAttribute(string name)
    {
        Name = name;
    }
}