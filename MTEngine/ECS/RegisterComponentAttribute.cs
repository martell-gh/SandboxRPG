using System;

namespace MTEngine.ECS;

[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class RegisterComponentAttribute : Attribute
{
    public string Name { get; }

    public RegisterComponentAttribute(string name)
    {
        Name = name;
    }
}