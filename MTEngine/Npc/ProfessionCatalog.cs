#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MTEngine.Npc;

public sealed class ProfessionDefinition
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("primarySkill")]
    public string PrimarySkill { get; set; } = "Trade";

    [JsonPropertyName("isTrader")]
    public bool IsTrader { get; set; }

    [JsonPropertyName("tradeTags")]
    public List<string> TradeTags { get; set; } = new();

    [JsonPropertyName("stockSizeMin")]
    public int StockSizeMin { get; set; } = 4;

    [JsonPropertyName("stockSizeMax")]
    public int StockSizeMax { get; set; } = 12;

    [JsonPropertyName("restockEveryDays")]
    public int RestockEveryDays { get; set; } = 7;

    [JsonPropertyName("skillGainPerDay")]
    public float SkillGainPerDay { get; set; } = 0.005f;
}

public sealed class ProfessionCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    [JsonPropertyName("professions")]
    public List<ProfessionDefinition> Professions { get; set; } = new();

    public ProfessionDefinition? Get(string? id)
        => Professions.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));

    public void Normalize()
    {
        Professions = Professions
            .Where(p => !string.IsNullOrWhiteSpace(p.Id))
            .GroupBy(p => p.Id.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var p = group.First();
                p.Id = p.Id.Trim();
                p.Name = string.IsNullOrWhiteSpace(p.Name) ? p.Id : p.Name.Trim();
                p.Description = p.Description?.Trim() ?? "";
                p.PrimarySkill = string.IsNullOrWhiteSpace(p.PrimarySkill) ? "Trade" : p.PrimarySkill.Trim();
                p.TradeTags = p.TradeTags
                    .Where(g => !string.IsNullOrWhiteSpace(g))
                    .Select(g => g.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(g => g, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                return p;
            })
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static ProfessionCatalog Load(string path)
    {
        if (!File.Exists(path))
            return CreateDefault();

        try
        {
            var catalog = JsonSerializer.Deserialize<ProfessionCatalog>(File.ReadAllText(path), JsonOptions) ?? CreateDefault();
            catalog.Normalize();
            return catalog;
        }
        catch (Exception e)
        {
            Console.WriteLine($"[ProfessionCatalog] Failed to read {path}: {e.Message}");
            return CreateDefault();
        }
    }

    public void Save(string path)
    {
        Normalize();
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
    }

    public static ProfessionCatalog CreateDefault()
    {
        var catalog = new ProfessionCatalog();
        catalog.Professions.AddRange(new[]
        {
            new ProfessionDefinition
            {
                Id = "blacksmith",
                Name = "Blacksmith",
                PrimarySkill = "Smithing",
                IsTrader = true,
                TradeTags = new() { "metal", "weapon", "armor", "tool" },
                Description = "Makes and sells metal tools, weapons and armor."
            },
            new ProfessionDefinition
            {
                Id = "tailor",
                Name = "Tailor",
                PrimarySkill = "Tailoring",
                IsTrader = true,
                TradeTags = new() { "clothing", "cloth", "leather" },
                Description = "Makes and sells clothing."
            },
            new ProfessionDefinition
            {
                Id = "innkeeper",
                Name = "Innkeeper",
                PrimarySkill = "Trade",
                IsTrader = true,
                TradeTags = new() { "food", "drink" },
                Description = "Runs an inn or tavern."
            },
            new ProfessionDefinition
            {
                Id = "farmer",
                Name = "Farmer",
                PrimarySkill = "Craftsmanship",
                IsTrader = false,
                Description = "Works fields and handles food production."
            },
            new ProfessionDefinition
            {
                Id = "guard",
                Name = "Guard",
                PrimarySkill = "OneHandedWeapons",
                IsTrader = false,
                Description = "Protects a settlement or district."
            },
            new ProfessionDefinition
            {
                Id = "doctor",
                Name = "Doctor",
                PrimarySkill = "Medicine",
                IsTrader = true,
                TradeTags = new() { "medical", "medicine", "healing" },
                Description = "Treats wounds and sells medical supplies."
            },
            new ProfessionDefinition
            {
                Id = "healer",
                Name = "Healer",
                PrimarySkill = "Medicine",
                IsTrader = true,
                TradeTags = new() { "medical", "medicine", "healing" },
                Description = "Treats wounded people and sells simple remedies."
            }
        });
        catalog.Normalize();
        return catalog;
    }
}
