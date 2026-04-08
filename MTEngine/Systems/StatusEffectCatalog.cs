using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using MTEngine.Core;

namespace MTEngine.Systems;

public sealed class StatusEffectDefinition
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public string Description { get; set; } = "";
    public string Pattern { get; set; } = "";
    public string Tint { get; set; } = "#ffffff";
    public int Priority { get; set; }
}

public static class StatusEffectCatalog
{
    private static readonly object Sync = new();
    private static readonly Dictionary<string, StatusEffectDefinition> Definitions = new(StringComparer.OrdinalIgnoreCase);
    private static bool _loaded;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static StatusEffectDefinition? Get(string id)
    {
        EnsureLoaded();
        return Definitions.TryGetValue(id, out var definition) ? definition : null;
    }

    public static IReadOnlyCollection<StatusEffectDefinition> GetAll()
    {
        EnsureLoaded();
        return Definitions.Values.ToArray();
    }

    private static void EnsureLoaded()
    {
        if (_loaded)
            return;

        lock (Sync)
        {
            if (_loaded)
                return;

            LoadDefinitions();
            _loaded = true;
        }
    }

    private static void LoadDefinitions()
    {
        Definitions.Clear();

        var path = Path.Combine(ContentPaths.AbsoluteContentRoot, "Data", "status_effects.json");
        if (!File.Exists(path))
        {
            Console.WriteLine($"[StatusEffectCatalog] File not found: {path}");
            return;
        }

        try
        {
            var json = File.ReadAllText(path);
            var definitions = JsonSerializer.Deserialize<List<StatusEffectDefinition>>(json, JsonOptions) ?? new();
            foreach (var definition in definitions.Where(d => !string.IsNullOrWhiteSpace(d.Id)))
                Definitions[definition.Id] = definition;
        }
        catch (Exception e)
        {
            Console.WriteLine($"[StatusEffectCatalog] Failed to load status effects: {e.Message}");
        }
    }
}
