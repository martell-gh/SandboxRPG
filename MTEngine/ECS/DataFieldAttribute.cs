[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class DataFieldAttribute : Attribute
{
    public string? Name { get; }
    public DataFieldAttribute(string? name = null) => Name = name;
}