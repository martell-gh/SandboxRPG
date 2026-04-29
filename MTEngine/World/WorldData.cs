using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace MTEngine.World;

public static class LocationKinds
{
    public const string Wilds = "wilds";
    public const string WildsFactionControlled = "wilds_faction_controlled";
    public const string Settlement = "settlement";

    public static readonly string[] All =
    {
        Wilds,
        WildsFactionControlled,
        Settlement
    };

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Wilds;

        return All.FirstOrDefault(option => string.Equals(option, value, StringComparison.OrdinalIgnoreCase))
            ?? Wilds;
    }

    public static string GetDisplayName(string? value)
    {
        return Normalize(value) switch
        {
            Wilds => "Wilds",
            WildsFactionControlled => "Wilds (Faction-Controlled)",
            Settlement => "Settlement",
            _ => "Wilds"
        };
    }
}

public sealed class FactionRelationData
{
    [JsonPropertyName("factionId")]
    public string FactionId { get; set; } = "";

    [JsonPropertyName("value")]
    public int Value { get; set; }
}

public sealed class FactionData
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("relations")]
    public List<FactionRelationData> Relations { get; set; } = new();

    public int GetRelationTo(string? otherFactionId)
    {
        if (string.IsNullOrWhiteSpace(otherFactionId))
            return 0;

        return Relations.FirstOrDefault(relation => string.Equals(relation.FactionId, otherFactionId, StringComparison.OrdinalIgnoreCase))?.Value ?? 0;
    }
}

public sealed class CityData
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";
}

public sealed class LocationGraphNodeData
{
    [JsonPropertyName("mapId")]
    public string MapId { get; set; } = "";

    [JsonPropertyName("x")]
    public int X { get; set; }

    [JsonPropertyName("y")]
    public int Y { get; set; }
}

public sealed class LocationGraphEdgeData
{
    [JsonPropertyName("a")]
    public string A { get; set; } = "";

    [JsonPropertyName("b")]
    public string B { get; set; } = "";
}

public sealed class LocationGraphData
{
    [JsonPropertyName("nodes")]
    public List<LocationGraphNodeData> Nodes { get; set; } = new();

    [JsonPropertyName("edges")]
    public List<LocationGraphEdgeData> Edges { get; set; } = new();
}

public sealed class WorldData
{
    [JsonPropertyName("factions")]
    public List<FactionData> Factions { get; set; } = new();

    [JsonPropertyName("cities")]
    public List<CityData> Cities { get; set; } = new();

    /// <summary>Карта, на которой стартует новая игра. Должна быть помечена в редакторе как InGame.</summary>
    [JsonPropertyName("startingMapId")]
    public string StartingMapId { get; set; } = "";

    /// <summary>Spawn-point id на стартовой карте. По умолчанию "default".</summary>
    [JsonPropertyName("startingSpawnId")]
    public string StartingSpawnId { get; set; } = "default";

    /// <summary>Фракция, к которой принадлежит игрок при старте новой игры.</summary>
    [JsonPropertyName("startingFactionId")]
    public string StartingFactionId { get; set; } = "";

    /// <summary>Стартовая одежда игрока: equipment slot id -> wearable prototype id.</summary>
    [JsonPropertyName("startingOutfit")]
    public Dictionary<string, string> StartingOutfit { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Граф связей локаций для AI-навигации NPC. Рёбра ненаправленные.</summary>
    [JsonPropertyName("locationGraph")]
    public LocationGraphData LocationGraph { get; set; } = new();

    public FactionData? GetFaction(string? factionId)
        => Factions.FirstOrDefault(faction => string.Equals(faction.Id, factionId, StringComparison.OrdinalIgnoreCase));

    public CityData? GetCity(string? cityId)
        => Cities.FirstOrDefault(city => string.Equals(city.Id, cityId, StringComparison.OrdinalIgnoreCase));

    public void SetFactionRelation(string leftFactionId, string rightFactionId, int value)
    {
        if (string.IsNullOrWhiteSpace(leftFactionId)
            || string.IsNullOrWhiteSpace(rightFactionId)
            || string.Equals(leftFactionId, rightFactionId, StringComparison.OrdinalIgnoreCase))
            return;

        var left = GetFaction(leftFactionId);
        var right = GetFaction(rightFactionId);
        if (left == null || right == null)
            return;

        SetSingleFactionRelation(left, right.Id, value);
        SetSingleFactionRelation(right, left.Id, value);
    }

    public void RenameFactionReferences(string oldFactionId, string newFactionId)
    {
        if (string.Equals(StartingFactionId, oldFactionId, StringComparison.OrdinalIgnoreCase))
            StartingFactionId = newFactionId;

        foreach (var faction in Factions)
        {
            foreach (var relation in faction.Relations)
            {
                if (string.Equals(relation.FactionId, oldFactionId, StringComparison.OrdinalIgnoreCase))
                    relation.FactionId = newFactionId;
            }
        }
    }

    public void RemoveFactionReferences(string factionId)
    {
        if (string.Equals(StartingFactionId, factionId, StringComparison.OrdinalIgnoreCase))
            StartingFactionId = "";

        foreach (var faction in Factions)
            faction.Relations.RemoveAll(relation => string.Equals(relation.FactionId, factionId, StringComparison.OrdinalIgnoreCase));
    }

    public void Normalize()
    {
        StartingFactionId = string.IsNullOrWhiteSpace(StartingFactionId) ? "" : StartingFactionId.Trim();

        Factions = Factions
            .Where(faction => !string.IsNullOrWhiteSpace(faction.Id))
            .GroupBy(faction => faction.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var faction = group.First();
                faction.Name = string.IsNullOrWhiteSpace(faction.Name) ? faction.Id : faction.Name.Trim();
                faction.Relations = faction.Relations
                    .Where(relation => !string.IsNullOrWhiteSpace(relation.FactionId)
                        && !string.Equals(relation.FactionId, faction.Id, StringComparison.OrdinalIgnoreCase))
                    .GroupBy(relation => relation.FactionId, StringComparer.OrdinalIgnoreCase)
                    .Select(relations =>
                    {
                        var relation = relations.Last();
                        relation.FactionId = relation.FactionId.Trim();
                        relation.Value = Math.Clamp(relation.Value, -100, 100);
                        return relation;
                    })
                    .OrderBy(relation => relation.FactionId, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                return faction;
            })
            .OrderBy(faction => faction.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(faction => faction.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        NormalizeSymmetricFactionRelations();

        if (!string.IsNullOrWhiteSpace(StartingFactionId) && GetFaction(StartingFactionId) == null)
            StartingFactionId = "";

        Cities = Cities
            .Where(city => !string.IsNullOrWhiteSpace(city.Id))
            .GroupBy(city => city.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var city = group.First();
                city.Name = string.IsNullOrWhiteSpace(city.Name) ? city.Id : city.Name.Trim();
                return city;
            })
            .OrderBy(city => city.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(city => city.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void NormalizeSymmetricFactionRelations()
    {
        var pairValues = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var faction in Factions)
        {
            foreach (var relation in faction.Relations)
            {
                var otherFaction = GetFaction(relation.FactionId);
                if (otherFaction == null || string.Equals(faction.Id, otherFaction.Id, StringComparison.OrdinalIgnoreCase))
                    continue;

                var pairKey = GetFactionPairKey(faction.Id, otherFaction.Id);
                var value = Math.Clamp(relation.Value, -100, 100);

                if (!pairValues.TryGetValue(pairKey, out var existingValue)
                    || existingValue == 0
                    || Math.Abs(value) > Math.Abs(existingValue))
                {
                    pairValues[pairKey] = value;
                }
            }
        }

        foreach (var faction in Factions)
            faction.Relations.Clear();

        foreach (var pair in pairValues)
        {
            var ids = pair.Key.Split('|', 2, StringSplitOptions.TrimEntries);
            if (ids.Length != 2)
                continue;

            SetFactionRelation(ids[0], ids[1], pair.Value);
        }

        foreach (var faction in Factions)
        {
            faction.Relations = faction.Relations
                .OrderBy(relation => relation.FactionId, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    private static string GetFactionPairKey(string leftFactionId, string rightFactionId)
    {
        return string.Compare(leftFactionId, rightFactionId, StringComparison.OrdinalIgnoreCase) <= 0
            ? $"{leftFactionId}|{rightFactionId}"
            : $"{rightFactionId}|{leftFactionId}";
    }

    private static void SetSingleFactionRelation(FactionData faction, string otherFactionId, int value)
    {
        value = Math.Clamp(value, -100, 100);

        var existing = faction.Relations.FirstOrDefault(relation => string.Equals(relation.FactionId, otherFactionId, StringComparison.OrdinalIgnoreCase));
        if (value == 0)
        {
            if (existing != null)
                faction.Relations.Remove(existing);
            return;
        }

        if (existing == null)
        {
            faction.Relations.Add(new FactionRelationData
            {
                FactionId = otherFactionId,
                Value = value
            });
            return;
        }

        existing.Value = value;
    }
}

public sealed class LocationContext
{
    public string LocationId { get; init; } = "";
    public string LocationName { get; init; } = "";
    public string LocationKind { get; init; } = LocationKinds.Wilds;
    public string? FactionId { get; init; }
    public string? FactionName { get; init; }
    public string? CityId { get; init; }
    public string? CityName { get; init; }
}
