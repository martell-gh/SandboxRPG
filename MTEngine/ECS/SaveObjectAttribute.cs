[AttributeUsage(AttributeTargets.Class)]
public sealed class SaveObjectAttribute : Attribute
{
    public string? Id { get; }

    public SaveObjectAttribute(string? id = null)
    {
        Id = id;
    }
}
