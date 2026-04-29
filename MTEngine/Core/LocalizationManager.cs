using System.Text.Json;
using System.Text.Json.Nodes;

namespace MTEngine.Core;

public sealed class LocalizationLanguage
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
}

public class LocalizationManager
{
    public const string RussianId = "RU";
    public const string EnglishId = "EN";

    private readonly Dictionary<string, Dictionary<string, string>> _tables = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<LocalizationLanguage> _languages = new();

    public string CurrentLanguageId { get; private set; } = RussianId;
    public IReadOnlyList<LocalizationLanguage> Languages => _languages;

    public void Load(string rootDirectory)
    {
        _tables.Clear();
        _languages.Clear();

        if (!Directory.Exists(rootDirectory))
            return;

        foreach (var directory in Directory.EnumerateDirectories(rootDirectory).OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            var id = Path.GetFileName(directory).Trim();
            if (string.IsNullOrWhiteSpace(id))
                continue;

            var table = LoadLanguageTable(directory);
            _tables[id] = table;
            _languages.Add(new LocalizationLanguage
            {
                Id = id,
                Name = LoadLanguageName(directory, id)
            });
        }
    }

    public void SetLanguage(string? languageId)
    {
        if (string.IsNullOrWhiteSpace(languageId))
            languageId = RussianId;

        CurrentLanguageId = languageId.Trim();
    }

    public string Translate(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return "";

        var normalized = NormalizeKey(key);
        if (string.IsNullOrWhiteSpace(normalized))
            return "";

        if (TryGet(CurrentLanguageId, normalized, out var value))
            return value;

        if (!string.Equals(CurrentLanguageId, EnglishId, StringComparison.OrdinalIgnoreCase)
            && TryGet(EnglishId, normalized, out value))
        {
            return value;
        }

        if (!string.Equals(CurrentLanguageId, RussianId, StringComparison.OrdinalIgnoreCase)
            && TryGet(RussianId, normalized, out value))
        {
            return value;
        }

        return "";
    }

    public string Resolve(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return "";

        return IsLocalizationKey(text) ? Translate(text) : text;
    }

    public string GetLanguageName(string? languageId)
    {
        if (string.IsNullOrWhiteSpace(languageId))
            languageId = CurrentLanguageId;

        return _languages.FirstOrDefault(l => string.Equals(l.Id, languageId, StringComparison.OrdinalIgnoreCase))?.Name
               ?? languageId
               ?? "";
    }

    public static bool IsLocalizationKey(string? text)
        => !string.IsNullOrWhiteSpace(text) && text.TrimStart().StartsWith("--", StringComparison.Ordinal);

    public static string T(string? text)
    {
        if (ServiceLocator.Has<LocalizationManager>())
            return ServiceLocator.Get<LocalizationManager>().Resolve(text);

        return IsLocalizationKey(text) ? "" : text ?? "";
    }

    private bool TryGet(string languageId, string key, out string value)
    {
        value = "";
        if (!_tables.TryGetValue(languageId, out var table)
            || !table.TryGetValue(key, out var found)
            || found == null)
        {
            return false;
        }

        value = found;
        return true;
    }

    private static string NormalizeKey(string key)
        => key.Trim();

    private static Dictionary<string, string> LoadLanguageTable(string directory)
    {
        var table = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.EnumerateFiles(directory, "*.lcl", SearchOption.AllDirectories).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            foreach (var line in File.ReadLines(file))
                TryParseLine(line, table);
        }

        return table;
    }

    private static void TryParseLine(string rawLine, Dictionary<string, string> table)
    {
        var line = rawLine.Trim();
        if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal) || line.StartsWith("//", StringComparison.Ordinal))
            return;

        if (!line.StartsWith("--", StringComparison.Ordinal))
            return;

        var colonIndex = line.IndexOf(':');
        if (colonIndex <= 2)
            return;

        var key = line[..colonIndex].Trim();
        var rawValue = line[(colonIndex + 1)..].Trim();
        if (string.IsNullOrWhiteSpace(key))
            return;

        table[key] = ParseValue(rawValue);
    }

    private static string ParseValue(string rawValue)
    {
        if (rawValue.Length == 0)
            return "";

        if (rawValue.StartsWith('"') && rawValue.EndsWith('"'))
        {
            try
            {
                return JsonSerializer.Deserialize<string>(rawValue) ?? "";
            }
            catch
            {
                return rawValue.Trim('"');
            }
        }

        return rawValue;
    }

    private static string LoadLanguageName(string directory, string fallback)
    {
        var metaPath = Path.Combine(directory, "meta.json");
        if (!File.Exists(metaPath))
            return fallback;

        try
        {
            var node = JsonNode.Parse(File.ReadAllText(metaPath))?.AsObject();
            return FirstNonEmpty(
                node?["name"]?.GetValue<string>(),
                node?["displayName"]?.GetValue<string>(),
                node?["nativeName"]?.GetValue<string>(),
                fallback);
        }
        catch
        {
            return fallback;
        }
    }

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? "";
}
