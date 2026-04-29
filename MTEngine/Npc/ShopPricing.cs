using System.Text.Json.Nodes;
using MTEngine.Core;
using MTEngine.Items;
using MTEngine.World;

namespace MTEngine.Npc;

public static class ShopPricing
{
    public static int GetBuyPrice(EntityPrototype proto)
        => GetBuyPrice(proto, null);

    public static int GetBuyPrice(EntityPrototype proto, MapData? marketMap)
    {
        var tags = GetTags(proto).ToList();
        var tier = GetQualityTier(proto);
        var size = GetItemSize(proto);

        var basePrice = size switch
        {
            ItemSize.Tiny => 2f,
            ItemSize.Small => 7f,
            ItemSize.Medium => 16f,
            ItemSize.Large => 34f,
            ItemSize.Huge => 72f,
            _ => 8f
        };

        if (tags.Contains("weapon", StringComparer.OrdinalIgnoreCase)) basePrice += 22f;
        if (tags.Contains("armor", StringComparer.OrdinalIgnoreCase)) basePrice += 28f;
        if (tags.Contains("tool", StringComparer.OrdinalIgnoreCase)) basePrice += 12f;
        if (tags.Contains("medical", StringComparer.OrdinalIgnoreCase)
            || tags.Contains("medicine", StringComparer.OrdinalIgnoreCase)
            || tags.Contains("healing", StringComparer.OrdinalIgnoreCase)) basePrice += 10f;
        if (tags.Contains("ingot", StringComparer.OrdinalIgnoreCase)) basePrice += 16f;
        if (tags.Contains("metal", StringComparer.OrdinalIgnoreCase)) basePrice += 8f;
        if (tags.Contains("food", StringComparer.OrdinalIgnoreCase)
            || tags.Contains("drink", StringComparer.OrdinalIgnoreCase)) basePrice += 4f;
        if (tags.Contains("rare", StringComparer.OrdinalIgnoreCase)) basePrice += 80f;
        if (tags.Contains("unique", StringComparer.OrdinalIgnoreCase)) basePrice += 160f;
        if (tags.Contains("legendary", StringComparer.OrdinalIgnoreCase)) basePrice += 260f;

        basePrice += GetCraftComplexityBonus(proto);

        var tierMultiplier = tier switch
        {
            1 => 4.0f,
            2 => 2.35f,
            3 => 1.45f,
            _ => 1.0f
        };

        return Math.Max(1, (int)MathF.Round(basePrice * tierMultiplier * GetMarketMultiplier(tags, marketMap)));
    }

    public static int GetSellPrice(EntityPrototype proto)
        => GetSellPrice(proto, null);

    public static int GetSellPrice(EntityPrototype proto, MapData? marketMap)
        => Math.Max(1, (int)MathF.Floor(GetBuyPrice(proto, marketMap) * 0.5f));

    public static int GetQualityTier(EntityPrototype proto)
    {
        if (proto.Components?["qualityTier"] is JsonObject quality)
            return Math.Clamp(ReadInt(quality, "tier", 4), 1, 4);

        return 4;
    }

    public static IEnumerable<string> GetTags(EntityPrototype proto)
    {
        if (proto.Components?["item"] is not JsonObject item)
            yield break;

        if (item["tags"] is not JsonArray tags)
            yield break;

        foreach (var tagNode in tags)
        {
            var tag = tagNode?.GetValue<string>()?.Trim();
            if (!string.IsNullOrWhiteSpace(tag))
                yield return tag;
        }
    }

    private static ItemSize GetItemSize(EntityPrototype proto)
    {
        if (proto.Components?["item"] is not JsonObject item)
            return ItemSize.Small;

        if (!item.TryGetPropertyValue("size", out var sizeNode) || sizeNode == null)
            return ItemSize.Small;

        try
        {
            if (sizeNode is JsonValue)
            {
                var raw = sizeNode.GetValue<string>();
                if (Enum.TryParse<ItemSize>(raw, true, out var parsed))
                    return parsed;
            }
        }
        catch
        {
            try
            {
                return (ItemSize)Math.Clamp(sizeNode.GetValue<int>(), (int)ItemSize.Tiny, (int)ItemSize.Huge);
            }
            catch
            {
                return ItemSize.Small;
            }
        }

        return ItemSize.Small;
    }

    private static float GetCraftComplexityBonus(EntityPrototype proto)
    {
        if (proto.Components?["craftable"] is not JsonObject craftable)
            return 0f;

        var bonus = 0f;
        var requiredSkill = ReadFloat(craftable, "requiredSkill", 0f);
        if (requiredSkill > 0f)
            bonus += requiredSkill * 1.35f;

        var craftTime = ReadFloat(craftable, "craftTime", 0f);
        if (craftTime > 0f)
            bonus += craftTime * 4f;

        bonus += ReadInt(craftable, "recipeTier", 4) switch
        {
            1 => 70f,
            2 => 35f,
            3 => 12f,
            _ => 0f
        };

        if (ReadBool(craftable, "requiresRecipe"))
            bonus += 15f;
        if (ReadBool(craftable, "discoverableBySmelting"))
            bonus += 8f;

        return bonus;
    }

    private static float GetMarketMultiplier(List<string> itemTags, MapData? marketMap)
    {
        if (marketMap == null || itemTags.Count == 0)
            return 1f;

        var wanted = marketMap.WantedTags.Any(tag => itemTags.Contains(tag, StringComparer.OrdinalIgnoreCase));
        var unwanted = marketMap.UnwantedTags.Any(tag => itemTags.Contains(tag, StringComparer.OrdinalIgnoreCase));

        var multiplier = 1f;
        if (wanted)
            multiplier *= 1.35f;
        if (unwanted)
            multiplier *= 0.55f;

        return multiplier;
    }

    private static int ReadInt(JsonObject obj, string key, int fallback)
    {
        try
        {
            return obj[key]?.GetValue<int>() ?? fallback;
        }
        catch
        {
            return fallback;
        }
    }

    private static float ReadFloat(JsonObject obj, string key, float fallback)
    {
        try
        {
            return obj[key]?.GetValue<float>() ?? fallback;
        }
        catch
        {
            return float.TryParse(obj[key]?.ToString(), out var value) ? value : fallback;
        }
    }

    private static bool ReadBool(JsonObject obj, string key)
    {
        try
        {
            return obj[key]?.GetValue<bool>() ?? false;
        }
        catch
        {
            return bool.TryParse(obj[key]?.ToString(), out var value) && value;
        }
    }
}
