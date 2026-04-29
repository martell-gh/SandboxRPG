#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MTEngine.Core;
using MTEngine.World;

namespace MTEditor.UI;

public sealed class MapLocationPanel
{
    private readonly GraphicsDevice _graphics;
    private readonly PrototypeManager _prototypes;

    private Rectangle _bounds;
    private Rectangle _settlementToggle;
    private Rectangle _locationPrevButton;
    private Rectangle _locationNextButton;
    private Rectangle _factionPrevButton;
    private Rectangle _factionNextButton;
    private Rectangle _factionClearButton;
    private Rectangle _cityPrevButton;
    private Rectangle _cityNextButton;
    private Rectangle _cityClearButton;
    private Rectangle _wantedPrevButton;
    private Rectangle _wantedNextButton;
    private Rectangle _wantedAddButton;
    private Rectangle _wantedChipsRect;
    private Rectangle _unwantedPrevButton;
    private Rectangle _unwantedNextButton;
    private Rectangle _unwantedAddButton;
    private Rectangle _unwantedChipsRect;
    private readonly List<(Rectangle Rect, string Tag, bool Wanted)> _tagChipRects = new();
    private string _wantedTagCandidate = "";
    private string _unwantedTagCandidate = "";

    public Rectangle Bounds => _bounds;

    public MapLocationPanel(GraphicsDevice graphics, PrototypeManager prototypes)
    {
        _graphics = graphics;
        _prototypes = prototypes;
    }

    public bool Update(MouseState mouse, MouseState prev, Rectangle mapToolbarBounds, Rectangle paletteBounds, MapData map, WorldData worldData)
    {
        RebuildLayout(mapToolbarBounds, paletteBounds);
        RebuildMarketChipRects(map);

        if (mouse.LeftButton != ButtonState.Pressed || prev.LeftButton != ButtonState.Released)
            return false;

        var point = mouse.Position;
        if (!_bounds.Contains(point))
            return false;

        if (_settlementToggle.Contains(point))
        {
            // Явный бинарный тогглер: Settlement ↔ Wilds. WildsFactionControlled
            // остаётся доступен через cycle Type ниже.
            map.LocationKind = string.Equals(map.LocationKind, LocationKinds.Settlement, StringComparison.OrdinalIgnoreCase)
                ? LocationKinds.Wilds
                : LocationKinds.Settlement;
            return true;
        }

        if (_locationPrevButton.Contains(point))
        {
            map.LocationKind = Cycle(LocationKinds.All, map.LocationKind, -1);
            return true;
        }

        if (_locationNextButton.Contains(point))
        {
            map.LocationKind = Cycle(LocationKinds.All, map.LocationKind, 1);
            return true;
        }

        var factions = BuildReferenceOptions(worldData.Factions, includeNone: true);
        if (_factionPrevButton.Contains(point))
        {
            map.FactionId = CycleReference(factions, map.FactionId, -1);
            return true;
        }

        if (_factionNextButton.Contains(point))
        {
            map.FactionId = CycleReference(factions, map.FactionId, 1);
            return true;
        }

        if (_factionClearButton.Contains(point))
        {
            map.FactionId = null;
            return true;
        }

        var cities = BuildReferenceOptions(worldData.Cities, includeNone: true);
        if (_cityPrevButton.Contains(point))
        {
            map.CityId = CycleReference(cities, map.CityId, -1);
            return true;
        }

        if (_cityNextButton.Contains(point))
        {
            map.CityId = CycleReference(cities, map.CityId, 1);
            return true;
        }

        if (_cityClearButton.Contains(point))
        {
            map.CityId = null;
            return true;
        }

        foreach (var chip in _tagChipRects)
        {
            if (!chip.Rect.Contains(point))
                continue;

            var list = chip.Wanted ? map.WantedTags : map.UnwantedTags;
            list.RemoveAll(tag => string.Equals(tag, chip.Tag, StringComparison.OrdinalIgnoreCase));
            return true;
        }

        if (_wantedPrevButton.Contains(point))
        {
            _wantedTagCandidate = CycleMarketTag(map, _wantedTagCandidate, -1);
            return true;
        }

        if (_wantedNextButton.Contains(point))
        {
            _wantedTagCandidate = CycleMarketTag(map, _wantedTagCandidate, 1);
            return true;
        }

        if (_wantedAddButton.Contains(point))
        {
            AddMarketTag(map, wanted: true);
            return true;
        }

        if (_unwantedPrevButton.Contains(point))
        {
            _unwantedTagCandidate = CycleMarketTag(map, _unwantedTagCandidate, -1);
            return true;
        }

        if (_unwantedNextButton.Contains(point))
        {
            _unwantedTagCandidate = CycleMarketTag(map, _unwantedTagCandidate, 1);
            return true;
        }

        if (_unwantedAddButton.Contains(point))
        {
            AddMarketTag(map, wanted: false);
            return true;
        }

        return true;
    }

    public void Draw(SpriteBatch spriteBatch, Rectangle mapToolbarBounds, Rectangle paletteBounds, MapData map, WorldData worldData)
    {
        RebuildLayout(mapToolbarBounds, paletteBounds);
        RebuildMarketChipRects(map);

        EditorTheme.DrawShadow(spriteBatch, _bounds, 8);
        EditorTheme.DrawPanel(spriteBatch, _bounds, EditorTheme.Bg, EditorTheme.Border);

        var headerRect = new Rectangle(_bounds.X, _bounds.Y, _bounds.Width, 28);
        EditorTheme.FillRect(spriteBatch, headerRect, EditorTheme.Panel);
        spriteBatch.Draw(EditorTheme.Pixel, new Rectangle(headerRect.X, headerRect.Y, 3, headerRect.Height), EditorTheme.Accent);
        EditorTheme.DrawText(spriteBatch, EditorTheme.Small, "LOCATION METADATA",
            new Vector2(headerRect.X + 12, headerRect.Y + 7), EditorTheme.Text);

        var locationKindText = LocationKinds.GetDisplayName(map.LocationKind);
        var faction = worldData.GetFaction(map.FactionId);
        var city = worldData.GetCity(map.CityId);

        DrawHighlight(spriteBatch, new Rectangle(_bounds.X + 12, _bounds.Y + 38, _bounds.Width - 24, 44),
            "Location", string.IsNullOrWhiteSpace(map.Name) ? map.Id : map.Name, map.Id, EditorTheme.Accent);

        DrawHighlight(spriteBatch, new Rectangle(_bounds.X + 12, _bounds.Y + 90, (_bounds.Width - 30) / 2, 40),
            "Faction", ResolveReferenceLabel(map.FactionId, faction?.Name), map.FactionId ?? "no faction",
            faction == null && !string.IsNullOrWhiteSpace(map.FactionId) ? EditorTheme.Error : EditorTheme.Success);

        DrawHighlight(spriteBatch, new Rectangle(_bounds.Center.X + 3, _bounds.Y + 90, (_bounds.Width - 30) / 2, 40),
            "City", ResolveReferenceLabel(map.CityId, city?.Name), map.CityId ?? "standalone",
            city == null && !string.IsNullOrWhiteSpace(map.CityId) ? EditorTheme.Error : EditorTheme.Warning);

        DrawSettlementToggle(spriteBatch, _settlementToggle, map.LocationKind);
        DrawCycleRow(spriteBatch, "Type", locationKindText, _locationPrevButton, _locationNextButton, Rectangle.Empty, false, EditorTheme.Text);
        DrawCycleRow(spriteBatch, "Faction", ResolveReferenceLabel(map.FactionId, faction?.Name), _factionPrevButton, _factionNextButton, _factionClearButton, true,
            faction == null && !string.IsNullOrWhiteSpace(map.FactionId) ? EditorTheme.Error : EditorTheme.Text);
        DrawCycleRow(spriteBatch, "City", ResolveReferenceLabel(map.CityId, city?.Name), _cityPrevButton, _cityNextButton, _cityClearButton, true,
            city == null && !string.IsNullOrWhiteSpace(map.CityId) ? EditorTheme.Error : EditorTheme.Text);
        DrawMarketRow(spriteBatch, "Wanted", GetMarketCandidate(map, wanted: true), _wantedPrevButton, _wantedNextButton, _wantedAddButton, _wantedChipsRect, map.WantedTags, EditorTheme.Success);
        DrawMarketRow(spriteBatch, "Unwanted", GetMarketCandidate(map, wanted: false), _unwantedPrevButton, _unwantedNextButton, _unwantedAddButton, _unwantedChipsRect, map.UnwantedTags, EditorTheme.Warning);
    }

    private void DrawHighlight(SpriteBatch spriteBatch, Rectangle rect, string label, string value, string subtitle, Color accent)
    {
        EditorTheme.FillRect(spriteBatch, rect, EditorTheme.PanelAlt);
        EditorTheme.DrawBorder(spriteBatch, rect, EditorTheme.Border);
        spriteBatch.Draw(EditorTheme.Pixel, new Rectangle(rect.X, rect.Y, 3, rect.Height), accent);
        EditorTheme.DrawText(spriteBatch, EditorTheme.Tiny, label.ToUpperInvariant(),
            new Vector2(rect.X + 10, rect.Y + 5), EditorTheme.TextMuted);
        EditorTheme.DrawText(spriteBatch, EditorTheme.Medium, value,
            new Vector2(rect.X + 10, rect.Y + 16), EditorTheme.Text);
        EditorTheme.DrawText(spriteBatch, EditorTheme.Tiny, subtitle,
            new Vector2(rect.Right - EditorTheme.Tiny.MeasureString(subtitle).X - 10, rect.Y + 5), accent);
    }

    private void DrawCycleRow(SpriteBatch spriteBatch, string label, string value, Rectangle prevButton, Rectangle nextButton, Rectangle clearButton, bool withClear, Color valueColor)
    {
        var rowY = prevButton.Y - 4;
        EditorTheme.DrawText(spriteBatch, EditorTheme.Small, label,
            new Vector2(_bounds.X + 16, rowY + 8), EditorTheme.TextDim);

        var valueRect = new Rectangle(_bounds.X + 74, rowY, _bounds.Width - 74 - 16 - 26 - 26 - (withClear ? 30 : 0), 28);
        EditorTheme.FillRect(spriteBatch, valueRect, EditorTheme.BgDeep);
        EditorTheme.DrawBorder(spriteBatch, valueRect, EditorTheme.Border);
        EditorTheme.DrawText(spriteBatch, EditorTheme.Small, value,
            new Vector2(valueRect.X + 8, valueRect.Y + 7), valueColor);

        DrawSmallButton(spriteBatch, prevButton, "<");
        DrawSmallButton(spriteBatch, nextButton, ">");
        if (withClear)
            DrawSmallButton(spriteBatch, clearButton, "x", EditorTheme.Error);
    }

    private void DrawMarketRow(SpriteBatch spriteBatch, string label, string candidate, Rectangle prevButton, Rectangle nextButton, Rectangle addButton, Rectangle chipsRect, List<string> tags, Color accent)
    {
        var rowY = prevButton.Y - 4;
        EditorTheme.DrawText(spriteBatch, EditorTheme.Small, label,
            new Vector2(_bounds.X + 16, rowY + 8), EditorTheme.TextDim);

        var valueRect = new Rectangle(_bounds.X + 88, rowY, _bounds.Width - 88 - 16 - 26 - 26 - 30, 28);
        EditorTheme.FillRect(spriteBatch, valueRect, EditorTheme.BgDeep);
        EditorTheme.DrawBorder(spriteBatch, valueRect, EditorTheme.Border);
        EditorTheme.DrawText(spriteBatch, EditorTheme.Small, string.IsNullOrWhiteSpace(candidate) ? "no known tags" : candidate,
            new Vector2(valueRect.X + 8, valueRect.Y + 7), string.IsNullOrWhiteSpace(candidate) ? EditorTheme.TextMuted : EditorTheme.Text);

        DrawSmallButton(spriteBatch, prevButton, "<");
        DrawSmallButton(spriteBatch, nextButton, ">");
        DrawSmallButton(spriteBatch, addButton, "+", accent);

        EditorTheme.FillRect(spriteBatch, chipsRect, EditorTheme.BgDeep * 0.72f);
        EditorTheme.DrawBorder(spriteBatch, chipsRect, EditorTheme.BorderSoft);
        if (tags.Count == 0)
        {
            EditorTheme.DrawText(spriteBatch, EditorTheme.Tiny, "none",
                new Vector2(chipsRect.X + 6, chipsRect.Y + 5), EditorTheme.TextMuted);
            return;
        }

        foreach (var chip in _tagChipRects.Where(chip => chip.Wanted == string.Equals(label, "Wanted", StringComparison.OrdinalIgnoreCase)))
        {
            EditorTheme.FillRect(spriteBatch, chip.Rect, accent * 0.22f);
            EditorTheme.DrawBorder(spriteBatch, chip.Rect, accent);
            EditorTheme.DrawText(spriteBatch, EditorTheme.Tiny, chip.Tag,
                new Vector2(chip.Rect.X + 6, chip.Rect.Y + 5), EditorTheme.Text);
        }
    }

    private static void DrawSettlementToggle(SpriteBatch spriteBatch, Rectangle rect, string locationKind)
    {
        var isSettlement = string.Equals(locationKind, LocationKinds.Settlement, StringComparison.OrdinalIgnoreCase);
        var fill = isSettlement ? EditorTheme.Success : EditorTheme.PanelAlt;
        var accent = isSettlement ? EditorTheme.Success : EditorTheme.Border;
        var label = isSettlement ? "ALIFE: SETTLEMENT (ON)" : "ALIFE: WILDERNESS (OFF)";
        var sub = isSettlement
            ? "matchmaking + jobs + births enabled here"
            : "click to mark map as settlement";

        EditorTheme.FillRect(spriteBatch, rect, fill * 0.18f);
        EditorTheme.DrawBorder(spriteBatch, rect, accent);
        spriteBatch.Draw(EditorTheme.Pixel, new Rectangle(rect.X, rect.Y, 3, rect.Height), accent);
        EditorTheme.DrawText(spriteBatch, EditorTheme.Tiny, label,
            new Vector2(rect.X + 12, rect.Y + 5),
            isSettlement ? EditorTheme.Success : EditorTheme.TextDim);
        EditorTheme.DrawText(spriteBatch, EditorTheme.Tiny, sub,
            new Vector2(rect.X + 12, rect.Y + 18), EditorTheme.TextMuted);
    }

    private void DrawSmallButton(SpriteBatch spriteBatch, Rectangle rect, string label, Color? accent = null)
    {
        EditorTheme.FillRect(spriteBatch, rect, EditorTheme.Panel);
        EditorTheme.DrawBorder(spriteBatch, rect, accent ?? EditorTheme.BorderSoft);
        var size = EditorTheme.Small.MeasureString(label);
        EditorTheme.DrawText(spriteBatch, EditorTheme.Small, label,
            new Vector2(rect.Center.X - size.X / 2f, rect.Y + (rect.Height - size.Y) / 2f - 1),
            accent ?? EditorTheme.Text);
    }

    private void RebuildLayout(Rectangle mapToolbarBounds, Rectangle paletteBounds)
    {
        var viewport = _graphics.Viewport;
        var width = 360;
        var x = viewport.Width - width - 12;
        var y = mapToolbarBounds.Bottom + 10;
        if (x < paletteBounds.Right + 12)
            x = paletteBounds.Right + 12;

        _bounds = new Rectangle(x, y, width, 430);

        _settlementToggle = new Rectangle(_bounds.X + 12, _bounds.Y + 144, _bounds.Width - 24, 36);

        var rowStartY = _bounds.Y + 188;
        BuildRow(rowStartY, out _locationPrevButton, out _locationNextButton, out _);
        BuildRow(rowStartY + 34, out _factionPrevButton, out _factionNextButton, out _factionClearButton);
        BuildRow(rowStartY + 68, out _cityPrevButton, out _cityNextButton, out _cityClearButton);
        BuildMarketRow(rowStartY + 112, out _wantedPrevButton, out _wantedNextButton, out _wantedAddButton, out _wantedChipsRect);
        BuildMarketRow(rowStartY + 170, out _unwantedPrevButton, out _unwantedNextButton, out _unwantedAddButton, out _unwantedChipsRect);
    }

    private void BuildRow(int y, out Rectangle prevButton, out Rectangle nextButton, out Rectangle clearButton)
    {
        var right = _bounds.Right - 16;
        nextButton = new Rectangle(right - 26, y, 26, 20);
        prevButton = new Rectangle(nextButton.X - 30, y, 26, 20);
        clearButton = new Rectangle(prevButton.X - 34, y, 30, 20);
    }

    private void BuildMarketRow(int y, out Rectangle prevButton, out Rectangle nextButton, out Rectangle addButton, out Rectangle chipsRect)
    {
        var right = _bounds.Right - 16;
        addButton = new Rectangle(right - 30, y, 30, 20);
        nextButton = new Rectangle(addButton.X - 30, y, 26, 20);
        prevButton = new Rectangle(nextButton.X - 30, y, 26, 20);
        chipsRect = new Rectangle(_bounds.X + 88, y + 28, _bounds.Width - 104, 24);
    }

    private void RebuildMarketChipRects(MapData map)
    {
        _tagChipRects.Clear();
        AddMarketChipRects(map.WantedTags, _wantedChipsRect, wanted: true);
        AddMarketChipRects(map.UnwantedTags, _unwantedChipsRect, wanted: false);
    }

    private void AddMarketChipRects(List<string> tags, Rectangle bounds, bool wanted)
    {
        var x = bounds.X + 5;
        foreach (var tag in tags.Take(8))
        {
            var label = tag.Trim();
            if (string.IsNullOrWhiteSpace(label))
                continue;

            var width = Math.Min(92, Math.Max(34, (int)EditorTheme.Tiny.MeasureString(label).X + 14));
            if (x + width > bounds.Right - 5)
                break;

            _tagChipRects.Add((new Rectangle(x, bounds.Y + 4, width, bounds.Height - 8), label, wanted));
            x += width + 5;
        }
    }

    private string GetMarketCandidate(MapData map, bool wanted)
    {
        var candidate = wanted ? _wantedTagCandidate : _unwantedTagCandidate;
        var options = BuildKnownItemTags(map);
        if (options.Count == 0)
            return "";

        if (string.IsNullOrWhiteSpace(candidate) || !options.Contains(candidate, StringComparer.OrdinalIgnoreCase))
            candidate = options[0];

        if (wanted)
            _wantedTagCandidate = candidate;
        else
            _unwantedTagCandidate = candidate;

        return candidate;
    }

    private string CycleMarketTag(MapData map, string current, int direction)
    {
        var options = BuildKnownItemTags(map);
        if (options.Count == 0)
            return "";

        var index = options.FindIndex(tag => string.Equals(tag, current, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
            index = 0;
        else
            index = (index + direction + options.Count) % options.Count;

        return options[index];
    }

    private void AddMarketTag(MapData map, bool wanted)
    {
        var tag = GetMarketCandidate(map, wanted);
        if (string.IsNullOrWhiteSpace(tag))
            return;

        var target = wanted ? map.WantedTags : map.UnwantedTags;
        var opposite = wanted ? map.UnwantedTags : map.WantedTags;
        opposite.RemoveAll(existing => string.Equals(existing, tag, StringComparison.OrdinalIgnoreCase));
        if (target.All(existing => !string.Equals(existing, tag, StringComparison.OrdinalIgnoreCase)))
            target.Add(tag);
        target.Sort(StringComparer.OrdinalIgnoreCase);
    }

    private List<string> BuildKnownItemTags(MapData map)
    {
        var tags = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tag in map.WantedTags.Concat(map.UnwantedTags))
            if (!string.IsNullOrWhiteSpace(tag))
                tags.Add(tag.Trim());

        foreach (var proto in _prototypes.GetAllEntities())
        {
            if (proto.Components?["item"] is not JsonObject item || item["tags"] is not JsonArray itemTags)
                continue;

            foreach (var node in itemTags)
            {
                var tag = node?.GetValue<string>()?.Trim();
                if (!string.IsNullOrWhiteSpace(tag))
                    tags.Add(tag);
            }
        }

        return tags.ToList();
    }

    private static string Cycle(string[] options, string current, int direction)
    {
        var normalizedCurrent = LocationKinds.Normalize(current);
        var index = Array.FindIndex(options, option => string.Equals(option, normalizedCurrent, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
            index = 0;

        index = (index + direction + options.Length) % options.Length;
        return options[index];
    }

    private static string? CycleReference(IReadOnlyList<(string? Id, string Label)> options, string? currentId, int direction)
    {
        if (options.Count == 0)
            return null;

        var index = 0;
        for (var i = 0; i < options.Count; i++)
        {
            if (string.Equals(options[i].Id, currentId, StringComparison.OrdinalIgnoreCase))
            {
                index = i;
                break;
            }
        }

        index = (index + direction + options.Count) % options.Count;
        return options[index].Id;
    }

    private static List<(string? Id, string Label)> BuildReferenceOptions<T>(IEnumerable<T> items, bool includeNone)
        where T : class
    {
        var result = new List<(string? Id, string Label)>();
        if (includeNone)
            result.Add((null, "None"));

        foreach (var item in items)
        {
            switch (item)
            {
                case FactionData faction:
                    result.Add((faction.Id, LocalizationManager.T(faction.Name)));
                    break;
                case CityData city:
                    result.Add((city.Id, city.Name));
                    break;
            }
        }

        return result;
    }

    private static string ResolveReferenceLabel(string? id, string? name)
    {
        if (string.IsNullOrWhiteSpace(id))
            return "None";

        return string.IsNullOrWhiteSpace(name) ? $"Missing: {id}" : LocalizationManager.T(name);
    }
}
