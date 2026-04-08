[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class SaveFieldAttribute : Attribute
{
    public string? Name { get; }

    public SaveFieldAttribute(string? name = null)
    {
        Name = name;
    }
}
