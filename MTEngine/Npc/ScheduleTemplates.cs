using System.Text.Json.Nodes;

namespace MTEngine.Npc;

/// <summary>
/// Загружает шаблоны расписаний из Data/schedule_templates.json.
/// Шаблон — это набор `slots` + `freetime`. ScheduleSystem применяет шаблон по id.
/// </summary>
public class ScheduleTemplates
{
    private readonly Dictionary<string, ScheduleTemplate> _templates =
        new(StringComparer.OrdinalIgnoreCase);

    public ScheduleTemplate? Get(string id)
        => _templates.TryGetValue(id, out var t) ? t : null;

    public IEnumerable<string> AllIds => _templates.Keys;

    public static ScheduleTemplates LoadFromFile(string path)
    {
        var result = new ScheduleTemplates();
        if (!File.Exists(path))
        {
            Console.WriteLine($"[ScheduleTemplates] Not found: {path}");
            return result;
        }

        try
        {
            var root = JsonNode.Parse(File.ReadAllText(path))?.AsObject();
            if (root == null) return result;

            foreach (var (id, value) in root)
            {
                if (value is not JsonObject obj) continue;
                var tpl = new ScheduleTemplate { Id = id };

                if (obj["slots"] is JsonArray slotsArr)
                    foreach (var s in slotsArr) tpl.Slots.Add(ParseSlot(s!));

                if (obj["freetime"] is JsonArray freeArr)
                    foreach (var f in freeArr) tpl.Freetime.Add(ParseFree(f!));

                result._templates[id] = tpl;
                Console.WriteLine($"[ScheduleTemplates] Loaded: {id} ({tpl.Slots.Count} slots, {tpl.Freetime.Count} freetime)");
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"[ScheduleTemplates] Error: {e.Message}");
        }
        return result;
    }

    /// <summary>Применить шаблон к расписанию NPC: затирает Slots/Freetime.</summary>
    public bool Apply(ScheduleComponent target, string templateId)
    {
        var tpl = Get(templateId);
        if (tpl == null) return false;
        target.TemplateId = templateId;
        target.Slots = tpl.Slots.Select(CloneSlot).ToList();
        target.Freetime = tpl.Freetime.Select(CloneFree).ToList();
        return true;
    }

    private static ScheduleSlot ParseSlot(JsonNode n)
    {
        var o = n.AsObject();
        return new ScheduleSlot
        {
            StartHour = o["start"]?.GetValue<int>() ?? 0,
            EndHour = o["end"]?.GetValue<int>() ?? 24,
            Action = ParseAction(o["action"]?.GetValue<string>() ?? "Wander"),
            TargetAreaId = o["targetAreaId"]?.GetValue<string>() ?? "",
            Priority = o["priority"]?.GetValue<int>() ?? 5
        };
    }

    private static FreetimeOption ParseFree(JsonNode n)
    {
        var o = n.AsObject();
        var opt = new FreetimeOption
        {
            Action = ParseAction(o["action"]?.GetValue<string>() ?? "Wander"),
            TargetAreaId = o["targetAreaId"]?.GetValue<string>() ?? "",
            Priority = o["priority"]?.GetValue<int>() ?? 1,
            DayOnly = o["dayOnly"]?.GetValue<bool>() ?? false,
            NightOnly = o["nightOnly"]?.GetValue<bool>() ?? false
        };
        if (o["conditions"] is JsonArray ca)
            foreach (var c in ca) opt.Conditions.Add(c?.GetValue<string>() ?? "");
        return opt;
    }

    private static ScheduleAction ParseAction(string s)
        => Enum.TryParse<ScheduleAction>(s, true, out var a) ? a : ScheduleAction.Wander;

    private static ScheduleSlot CloneSlot(ScheduleSlot s)
        => new() { StartHour = s.StartHour, EndHour = s.EndHour, Action = s.Action,
            TargetAreaId = s.TargetAreaId, Priority = s.Priority };

    private static FreetimeOption CloneFree(FreetimeOption f)
        => new() { Action = f.Action, TargetAreaId = f.TargetAreaId, Priority = f.Priority,
            DayOnly = f.DayOnly, NightOnly = f.NightOnly, Conditions = new List<string>(f.Conditions) };
}

public class ScheduleTemplate
{
    public string Id { get; set; } = "";
    public List<ScheduleSlot> Slots { get; set; } = new();
    public List<FreetimeOption> Freetime { get; set; } = new();
}
