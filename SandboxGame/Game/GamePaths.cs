using System.IO;

public static class GamePaths
{
    public static readonly string Content = "SandboxGame/Content";
    public static readonly string Maps = "SandboxGame/Maps";

    public static string Tiles => Path.Combine(Content, "Tiles");
    public static string Entities => Path.Combine(Content, "Entities");
}