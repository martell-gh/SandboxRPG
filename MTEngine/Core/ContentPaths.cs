using System.IO;

namespace MTEngine.Core;

public static class ContentPaths
{
    public static readonly string ContentRoot = Path.Combine("SandboxGame", "Content");
    public static readonly string MapsRoot = Path.Combine("SandboxGame", "Maps");

    public static string PrototypesRoot => Path.Combine(ContentRoot, "Prototypes");
    public static string DataRoot => Path.Combine(ContentRoot, "Data");
    public static string LocalizationRoot => Path.Combine(ContentRoot, "Localization");
    public static string TilesRoot => Path.Combine(PrototypesRoot, "Tiles");
    public static string ActorsRoot => Path.Combine(PrototypesRoot, "Actors");
    public static string SubstancesRoot => Path.Combine(PrototypesRoot, "Substances");
    public static string TexturesRoot => Path.Combine(ContentRoot, "Textures");
    public static string UiRoot => Path.Combine(TexturesRoot, "UI");
    public static string UiWindowsRoot => Path.Combine(ContentRoot, "UI");
    public static string DataRoot => Path.Combine(ContentRoot, "Data");

    public static string AbsoluteContentRoot => ResolveDirectory(ContentRoot);
    public static string AbsoluteMapsRoot => ResolveDirectory(MapsRoot);
    public static string AbsolutePrototypesRoot => ResolveDirectory(PrototypesRoot);
    public static string AbsoluteDataRoot => ResolveDirectory(DataRoot);
    public static string AbsoluteLocalizationRoot => ResolveDirectory(LocalizationRoot);
    public static string AbsoluteTilesRoot => ResolveDirectory(TilesRoot);
    public static string AbsoluteActorsRoot => ResolveDirectory(ActorsRoot);
    public static string AbsoluteSubstancesRoot => ResolveDirectory(SubstancesRoot);

    public static string ResolveDirectory(string relativePath)
    {
        foreach (var baseDirectory in GetSearchRoots())
        {
            var current = new DirectoryInfo(baseDirectory);
            while (current != null)
            {
                var candidate = Path.GetFullPath(Path.Combine(current.FullName, relativePath));
                if (Directory.Exists(candidate))
                    return candidate;

                current = current.Parent;
            }
        }

        return Path.GetFullPath(relativePath);
    }

    private static IEnumerable<string> GetSearchRoots()
    {
        yield return Directory.GetCurrentDirectory();
        yield return AppContext.BaseDirectory;
    }
}
