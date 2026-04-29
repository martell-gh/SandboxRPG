#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MTEngine.Combat;
using MTEngine.Core;
using MTEngine.Npc;
using MTEngine.Rendering;
using MTEngine.World;

namespace MTEditor.UI;

public sealed class NpcEditorPanel
{
    private sealed class RosterDocument
    {
        public string MapId { get; init; } = "";
        public string FilePath { get; set; } = "";
        public bool Exists { get; set; }
        public List<NpcRosterEntry> Entries { get; set; } = new();
    }

    private readonly record struct PickerOption(string Value, string Label, string Hint = "", Color? Accent = null);
    private readonly record struct FreetimeControlRect(ScheduleAction Action, string Kind, Rectangle Rect);
    private readonly record struct PreviewLayer(Texture2D Texture, Rectangle SourceRect, Color Color, Vector2 Origin, Vector2 Offset);
    private readonly record struct ColorSwatch(Rectangle Rect, string Hex, Color Color);

    private readonly GraphicsDevice _graphics;
    private readonly string _mapsRoot;
    private readonly string _prototypesRoot;
    private readonly string _dataRoot;
    private readonly PrototypeManager _prototypes;
    private readonly AssetManager _assets;

    private readonly List<RosterDocument> _documents = new();
    private readonly Dictionary<string, Rectangle> _fieldRects = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _fields = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _scheduleTemplateIds = new();
    private readonly Dictionary<string, JsonObject> _scheduleTemplates = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<PickerOption> _pickerOptions = new();
    private readonly ScheduleAction[] _scheduleHours = new ScheduleAction[24];
    private readonly string[] _scheduleTargets = new string[24];
    private readonly int[] _schedulePriorities = new int[24];
    private readonly List<FreetimeOption> _draftFreetime = new();
    private readonly Dictionary<ScheduleAction, Rectangle> _scheduleActionRects = new();
    private readonly Rectangle[] _scheduleHourRects = new Rectangle[24];
    private readonly List<FreetimeControlRect> _freetimeControlRects = new();
    private readonly List<ColorSwatch> _colorSwatches = new();

    private Rectangle _bounds;
    private Rectangle _mapListRect;
    private Rectangle _npcListRect;
    private Rectangle _detailRect;
    private Rectangle _detailsContentRect;
    private Rectangle _pickerRect;
    private Rectangle _colorPickerRect;
    private Rectangle _scheduleEditorRect;
    private Rectangle _scheduleGridRect;
    private Rectangle _previewPanelRect;
    private Rectangle _previewLeftButtonRect;
    private Rectangle _previewRightButtonRect;
    private Rectangle _newHairButtonRect;
    private Rectangle _editHairButtonRect;
    private readonly HairStyleDialog _hairDialog;

    private const string NoHairSentinel = "!none";
    private string _selectedMapId = "";
    private string _selectedNpcId = "";
    private string _cachedMapId = "";
    private string _focus = "";
    private string _openPickerField = "";
    private string _openColorField = "";
    private int _mapScroll;
    private int _npcScroll;
    private int _detailsScroll;
    private int _pickerScroll;
    private Keys? _heldDeleteKey;
    private long _nextDeleteRepeatAt;
    private WorldData? _worldData;
    private DateTime _cachedMapWriteTimeUtc;
    private MapData? _cachedMapData;
    private ScheduleAction _selectedScheduleAction = ScheduleAction.Free;
    private int _previewFacingIndex;

    private const int DeleteRepeatInitialDelayMs = 350;
    private const int DeleteRepeatIntervalMs = 32;
    private const int RowH = 34;
    private const int FieldH = 24;
    private const int Gap = 8;
    private const int ScheduleEditorHeight = 156;
    private const string SkillFieldPrefix = "skill.";

    private static readonly ScheduleAction[] SchedulePaintActions =
    {
        ScheduleAction.Sleep,
        ScheduleAction.StayAtHome,
        ScheduleAction.EatAtHome,
        ScheduleAction.Work,
        ScheduleAction.Free,
        ScheduleAction.Wander,
        ScheduleAction.StayInTavern,
        ScheduleAction.SchoolDay
    };

    private static readonly ScheduleAction[] FreetimeActions =
    {
        ScheduleAction.Wander,
        ScheduleAction.Visit,
        ScheduleAction.StayInTavern
    };

    private static readonly string[] PreviewClips =
    {
        "idle_down",
        "idle_right",
        "idle_up",
        "idle_left"
    };

    private static readonly string[] NaturalHairColorPresets =
    {
        "#16100BFF", "#2B1A11FF", "#4C311FFF", "#6D4327FF",
        "#8E5A33FF", "#B8733DFF", "#D7A35DFF", "#E8D6A1FF",
        "#F0E7D2FF", "#8A8A8AFF", "#3C3C3CFF", "#7B2B22FF"
    };

    private static readonly string[] NaturalSkinColorPresets =
    {
        "#F7D7C4FF", "#F0B99DFF", "#D99A78FF", "#BA765AFF",
        "#8D563FFF", "#5E3529FF", "#FFE1CEFF", "#C58B6CFF"
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public bool IsTyping => !string.IsNullOrEmpty(_focus) || _hairDialog.IsTyping;

    public NpcEditorPanel(GraphicsDevice graphics, string mapsRoot, string prototypesRoot, string dataRoot, PrototypeManager prototypes, AssetManager assets)
    {
        _graphics = graphics;
        _mapsRoot = mapsRoot;
        _prototypesRoot = prototypesRoot;
        _dataRoot = dataRoot;
        _prototypes = prototypes;
        _assets = assets;
        _hairDialog = new HairStyleDialog(graphics);
        ReloadFromDisk();
    }

    public bool Update(MouseState mouse, MouseState prev, KeyboardState keys, KeyboardState prevKeys, WorldData worldData, Action<string> showMessage)
    {
        _worldData = worldData;
        RebuildLayout();
        RebuildFieldRects();

        if (_hairDialog.IsOpen)
        {
            _hairDialog.Update(mouse, prev, keys, prevKeys);
            return true;
        }

        var scroll = mouse.ScrollWheelValue - prev.ScrollWheelValue;
        if (scroll != 0)
        {
            var direction = Math.Sign(scroll);
            if (!string.IsNullOrEmpty(_openPickerField) && _pickerRect.Contains(mouse.Position))
                _pickerScroll = Math.Max(0, _pickerScroll - direction);
            else if (_mapListRect.Contains(mouse.Position))
                _mapScroll = Math.Max(0, _mapScroll - direction);
            else if (_npcListRect.Contains(mouse.Position))
                _npcScroll = Math.Max(0, _npcScroll - direction);
            else if (_detailRect.Contains(mouse.Position))
                _detailsScroll = Math.Max(0, _detailsScroll - direction);
        }

        if (mouse.LeftButton == ButtonState.Pressed
            && _detailsContentRect.Contains(mouse.Position)
            && TryPaintScheduleHour(mouse.Position))
        {
            CommitDraftToSelected();
            return true;
        }

        if (mouse.LeftButton == ButtonState.Pressed && prev.LeftButton == ButtonState.Released)
        {
            if (!_bounds.Contains(mouse.Position))
            {
                _focus = "";
                ClosePicker();
                return false;
            }

            if (TryHandleColorPickerClick(mouse.Position))
                return true;

            if (TryHandlePickerClick(mouse.Position))
                return true;

            if (TrySelectMap(mouse.Position) || TrySelectNpc(mouse.Position))
                return false;

            if (TryHandleScheduleClick(mouse.Position))
                return true;

            if (TryHandlePreviewClick(mouse.Position, showMessage))
                return true;

            if (TryHandleSpecialFieldClick(mouse.Position))
                return true;

            _focus = "";
            ClosePicker();
            foreach (var (key, rect) in _fieldRects)
            {
                if (rect.Contains(mouse.Position))
                {
                    if (TryOpenColorPicker(key, rect))
                        return true;

                    if (TryOpenPicker(key, rect))
                        return true;

                    _focus = key;
                    break;
                }
            }
        }

        if (IsPressed(keys, prevKeys, Keys.Enter) && IsTyping)
            return SaveCurrent(showMessage);

        HandleTextInput(keys, prevKeys);
        return false;
    }

    public void Draw(SpriteBatch spriteBatch, WorldData worldData)
    {
        _worldData = worldData;
        RebuildLayout();
        RebuildFieldRects();

        EditorTheme.DrawPanel(spriteBatch, _bounds, EditorTheme.Bg, EditorTheme.Border);
        var header = new Rectangle(_bounds.X, _bounds.Y, _bounds.Width, 30);
        EditorTheme.FillRect(spriteBatch, header, EditorTheme.Panel);
        spriteBatch.Draw(EditorTheme.Pixel, new Rectangle(header.X, header.Y, 3, header.Height), EditorTheme.Success);
        EditorTheme.DrawText(spriteBatch, EditorTheme.Medium, "NPC EDITOR (.npc)",
            new Vector2(header.X + 12, header.Y + 8), EditorTheme.Text);

        DrawMapList(spriteBatch);
        DrawNpcList(spriteBatch);
        DrawDetails(spriteBatch);
        DrawPicker(spriteBatch);
        DrawColorPicker(spriteBatch);
        _hairDialog.Draw(spriteBatch);
    }

    public void ReloadFromDisk()
    {
        CommitDraftToSelected();
        _documents.Clear();
        _scheduleTemplateIds.Clear();
        _scheduleTemplates.Clear();

        if (Directory.Exists(_mapsRoot))
        {
            var mapIds = Directory.GetFiles(_mapsRoot, "*.map.json", SearchOption.TopDirectoryOnly)
                .Select(path => Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(path)))
                .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var mapId in mapIds)
                _documents.Add(LoadRoster(mapId));
        }

        foreach (var npcFile in Directory.Exists(_mapsRoot)
                     ? Directory.GetFiles(_mapsRoot, "*.npc", SearchOption.TopDirectoryOnly)
                     : Array.Empty<string>())
        {
            var mapId = Path.GetFileNameWithoutExtension(npcFile);
            if (_documents.Any(d => string.Equals(d.MapId, mapId, StringComparison.OrdinalIgnoreCase)))
                continue;
            _documents.Add(LoadRoster(mapId));
        }

        LoadScheduleTemplateIds();

        if (string.IsNullOrWhiteSpace(_selectedMapId) || GetSelectedDocument() == null)
            _selectedMapId = _documents.FirstOrDefault()?.MapId ?? "";

        if (GetSelectedNpc() == null)
            _selectedNpcId = GetSelectedDocument()?.Entries.FirstOrDefault()?.Id ?? "";

        LoadDraftFromSelected();
    }

    public bool CreateNew(Action<string> showMessage)
    {
        CommitDraftToSelected();
        var doc = GetSelectedDocument() ?? _documents.FirstOrDefault();
        if (doc == null)
        {
            showMessage("Create or save a map first");
            return false;
        }

        _selectedMapId = doc.MapId;
        var id = GenerateUniqueNpcId(doc);
        var entry = new NpcRosterEntry
        {
            Id = id,
            PrototypeId = "npc_base",
            AgeYears = 25,
            Identity = new NpcRosterIdentity
            {
                FirstName = "New",
                LastName = "NPC",
                Gender = "Male"
            },
            Personality = new NpcRosterPersonality()
        };
        entry.Components["schedule"] = new JsonObject { ["templateId"] = "default_unemployed" };
        doc.Entries.Add(entry);
        _selectedNpcId = id;
        LoadDraftFromSelected();
        showMessage($"Created NPC '{id}'");
        return true;
    }

    public bool SaveCurrent(Action<string> showMessage)
    {
        CommitDraftToSelected();
        var doc = GetSelectedDocument();
        if (doc == null)
        {
            showMessage("No NPC roster selected");
            return false;
        }

        Directory.CreateDirectory(_mapsRoot);
        doc.FilePath = Path.Combine(_mapsRoot, $"{doc.MapId}.npc");
        AutoAssignRosterBedSlots(doc);
        File.WriteAllText(doc.FilePath, JsonSerializer.Serialize(doc.Entries, JsonOptions));
        doc.Exists = true;
        showMessage($"Saved {doc.MapId}.npc");
        return true;
    }

    public bool DeleteCurrent(Action<string> showMessage)
    {
        var doc = GetSelectedDocument();
        var npc = GetSelectedNpc();
        if (doc == null || npc == null)
        {
            showMessage("Select an NPC first");
            return false;
        }

        doc.Entries.Remove(npc);
        _selectedNpcId = doc.Entries.FirstOrDefault()?.Id ?? "";
        LoadDraftFromSelected();
        showMessage("Deleted NPC from roster");
        return true;
    }

    public bool SaveSelectedAsTemplate(Action<string> showMessage)
    {
        CommitDraftToSelected();
        var npc = GetSelectedNpc();
        if (npc == null)
        {
            showMessage("Select an NPC first");
            return false;
        }

        var templateId = MakeSafeId($"{npc.Id}_template");
        var dir = Path.Combine(_prototypesRoot, "Actors", "NpcTemplates", templateId);
        Directory.CreateDirectory(dir);

        var components = new JsonObject();
        foreach (var (key, value) in npc.Components)
            components[key] = value.DeepClone();

        var sprite = components["sprite"] as JsonObject;
        if (sprite != null && sprite["source"]?.GetValue<string>() == "../Player/sprite.png")
            sprite["source"] = "../../Player/sprite.png";

        var node = new JsonObject
        {
            ["id"] = templateId,
            ["name"] = string.IsNullOrWhiteSpace(npc.Identity.FirstName) ? templateId : $"{npc.Identity.FirstName} {npc.Identity.LastName}".Trim(),
            ["category"] = "entity",
            ["base"] = "npc_base",
            ["components"] = components
        };

        File.WriteAllText(Path.Combine(dir, "proto.json"), node.ToJsonString(JsonOptions));
        npc.PrototypeId = templateId;
        _fields["proto"] = templateId;
        showMessage($"Saved NPC template '{templateId}'");
        return true;
    }

    private RosterDocument LoadRoster(string mapId)
    {
        var npcPath = Path.Combine(_mapsRoot, $"{mapId}.npc");
        var legacyPath = Path.Combine(_mapsRoot, $"{mapId}.npcs.json");
        var path = File.Exists(npcPath) ? npcPath : legacyPath;
        var exists = File.Exists(path);

        var doc = new RosterDocument
        {
            MapId = mapId,
            FilePath = npcPath,
            Exists = exists
        };

        if (!exists)
            return doc;

        try
        {
            doc.Entries = JsonSerializer.Deserialize<List<NpcRosterEntry>>(File.ReadAllText(path), JsonOptions) ?? new();
        }
        catch (Exception e)
        {
            Console.WriteLine($"[NpcEditor] Failed to read {path}: {e.Message}");
        }

        return doc;
    }

    private void LoadScheduleTemplateIds()
    {
        var path = Path.Combine(_dataRoot, "schedule_templates.json");
        if (!File.Exists(path))
            return;

        try
        {
            var root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
            if (root != null)
            {
                _scheduleTemplateIds.AddRange(root.Select(pair => pair.Key).OrderBy(id => id, StringComparer.OrdinalIgnoreCase));
                foreach (var (id, node) in root)
                    if (node is JsonObject obj)
                        _scheduleTemplates[id] = obj.DeepClone().AsObject();
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"[NpcEditor] Failed to read schedule templates: {e.Message}");
        }
    }

    private void DrawMapList(SpriteBatch spriteBatch)
    {
        EditorTheme.DrawPanel(spriteBatch, _mapListRect, EditorTheme.PanelAlt, EditorTheme.Border);
        EditorTheme.DrawText(spriteBatch, EditorTheme.Small, $"Rosters: {_documents.Count}",
            new Vector2(_mapListRect.X + 10, _mapListRect.Y + 8), EditorTheme.TextDim);

        var y = _mapListRect.Y + 30;
        foreach (var doc in _documents.Skip(_mapScroll))
        {
            if (y + RowH > _mapListRect.Bottom - 8)
                break;

            var row = new Rectangle(_mapListRect.X + 8, y, _mapListRect.Width - 16, RowH - 2);
            var selected = string.Equals(doc.MapId, _selectedMapId, StringComparison.OrdinalIgnoreCase);
            EditorTheme.FillRect(spriteBatch, row, selected ? EditorTheme.Success : EditorTheme.BgDeep);
            EditorTheme.DrawBorder(spriteBatch, row, selected ? EditorTheme.BorderSoft : EditorTheme.Border);
            EditorTheme.DrawText(spriteBatch, EditorTheme.Small, Truncate(doc.MapId, row.Width - 14),
                new Vector2(row.X + 7, row.Y + 5), selected ? Color.Black : EditorTheme.Text);
            var meta = doc.Exists ? $"{doc.Entries.Count} people" : "no .npc yet";
            EditorTheme.DrawText(spriteBatch, EditorTheme.Tiny, meta,
                new Vector2(row.X + 7, row.Y + 19), selected ? new Color(18, 45, 20) : EditorTheme.TextMuted);
            y += RowH;
        }
    }

    private void DrawNpcList(SpriteBatch spriteBatch)
    {
        EditorTheme.DrawPanel(spriteBatch, _npcListRect, EditorTheme.PanelAlt, EditorTheme.Border);
        var doc = GetSelectedDocument();
        EditorTheme.DrawText(spriteBatch, EditorTheme.Small, doc == null ? "People" : $"People: {doc.Entries.Count}",
            new Vector2(_npcListRect.X + 10, _npcListRect.Y + 8), EditorTheme.TextDim);

        if (doc == null)
            return;

        var y = _npcListRect.Y + 30;
        foreach (var npc in doc.Entries.OrderBy(e => e.Id, StringComparer.OrdinalIgnoreCase).Skip(_npcScroll))
        {
            if (y + RowH > _npcListRect.Bottom - 8)
                break;

            var row = new Rectangle(_npcListRect.X + 8, y, _npcListRect.Width - 16, RowH - 2);
            var selected = string.Equals(npc.Id, _selectedNpcId, StringComparison.OrdinalIgnoreCase);
            EditorTheme.FillRect(spriteBatch, row, selected ? EditorTheme.Accent : EditorTheme.BgDeep);
            EditorTheme.DrawBorder(spriteBatch, row, selected ? EditorTheme.AccentDim : EditorTheme.Border);
            var name = $"{npc.Identity.FirstName} {npc.Identity.LastName}".Trim();
            if (string.IsNullOrWhiteSpace(name))
                name = npc.Id;
            EditorTheme.DrawText(spriteBatch, EditorTheme.Small, Truncate(name, row.Width - 14),
                new Vector2(row.X + 7, row.Y + 5), selected ? Color.White : EditorTheme.Text);
            EditorTheme.DrawText(spriteBatch, EditorTheme.Tiny, $"{npc.Id}  [{npc.PrototypeId}]",
                new Vector2(row.X + 7, row.Y + 19), selected ? new Color(225, 235, 255) : EditorTheme.TextMuted);
            y += RowH;
        }
    }

    private void DrawDetails(SpriteBatch spriteBatch)
    {
        EditorTheme.DrawPanel(spriteBatch, _detailRect, EditorTheme.PanelAlt, EditorTheme.Border);
        var npc = GetSelectedNpc();
        if (npc == null)
        {
            EditorTheme.DrawText(spriteBatch, EditorTheme.Medium, "Select or create an NPC",
                new Vector2(_detailRect.X + 16, _detailRect.Y + 16), EditorTheme.TextDim);
            return;
        }

        var title = $"{_selectedMapId}.npc / {npc.Id}";
        EditorTheme.DrawText(spriteBatch, EditorTheme.Small, Truncate(title, _detailRect.Width - 24),
            new Vector2(_detailRect.X + 12, _detailRect.Y + 10), EditorTheme.TextDim);

        DrawNpcPreview(spriteBatch);

        foreach (var section in GetSections())
        {
            var visibleRects = section.Fields
                .Select(field => _fieldRects.TryGetValue(field.Key, out var rect) ? rect : Rectangle.Empty)
                .Where(rect => rect != Rectangle.Empty && rect.Bottom >= _detailsContentRect.Y && rect.Y <= _detailsContentRect.Bottom)
                .ToList();
            if (visibleRects.Count > 0)
            {
                var titleY = visibleRects.Min(rect => rect.Y) - 29;
                EditorTheme.FillRect(spriteBatch, new Rectangle(_detailsContentRect.X, titleY + 13, _detailsContentRect.Width, 1), EditorTheme.Border);
                EditorTheme.DrawText(spriteBatch, EditorTheme.Tiny, section.Title.ToUpperInvariant(),
                    new Vector2(_detailsContentRect.X, titleY), EditorTheme.Success);
            }

            foreach (var field in section.Fields)
            {
                if (!_fieldRects.TryGetValue(field.Key, out var rect))
                    continue;
                if (rect.Bottom < _detailsContentRect.Y || rect.Y > _detailsContentRect.Bottom)
                    continue;

                EditorTheme.DrawText(spriteBatch, EditorTheme.Tiny, field.Label,
                    new Vector2(rect.X, rect.Y - 12), EditorTheme.TextMuted);
                DrawFieldForDefinition(spriteBatch, field, rect);
            }
        }

        DrawScheduleEditor(spriteBatch);
        DrawZoneSummary(spriteBatch);

        var hintY = _detailRect.Bottom - 24;
        EditorTheme.DrawText(spriteBatch, EditorTheme.Tiny,
            ".npc is JSON-compatible. Ctrl+S saves the roster. Pickers use real factions, zones and prototypes.",
            new Vector2(_detailRect.X + 12, hintY), EditorTheme.TextMuted);
    }

    private void DrawScheduleEditor(SpriteBatch spriteBatch)
    {
        if (_scheduleEditorRect == Rectangle.Empty
            || _scheduleEditorRect.Bottom < _detailsContentRect.Y
            || _scheduleEditorRect.Y > _detailsContentRect.Bottom)
        {
            return;
        }

        EditorTheme.DrawPanel(spriteBatch, _scheduleEditorRect, EditorTheme.BgDeep, EditorTheme.BorderSoft);
        EditorTheme.DrawText(spriteBatch, EditorTheme.Tiny, "VISUAL SCHEDULE",
            new Vector2(_scheduleEditorRect.X + 8, _scheduleEditorRect.Y + 7), EditorTheme.Success);
        EditorTheme.DrawText(spriteBatch, EditorTheme.Tiny, $"Paint: {GetScheduleActionLabel(_selectedScheduleAction)}",
            new Vector2(_scheduleEditorRect.Right - 148, _scheduleEditorRect.Y + 7), EditorTheme.TextMuted);

        foreach (var (action, rect) in _scheduleActionRects)
        {
            var selected = action == _selectedScheduleAction;
            EditorTheme.FillRect(spriteBatch, rect, selected ? GetScheduleActionColor(action) : EditorTheme.Panel);
            EditorTheme.DrawBorder(spriteBatch, rect, selected ? EditorTheme.Text : EditorTheme.Border);
            EditorTheme.DrawText(spriteBatch, EditorTheme.Small, GetScheduleActionLabel(action),
                new Vector2(rect.X + 7, rect.Y + 5), selected ? Color.Black : EditorTheme.TextDim);
        }

        for (var hour = 0; hour < 24; hour++)
        {
            var rect = _scheduleHourRects[hour];
            if (rect == Rectangle.Empty)
                continue;

            var action = _scheduleHours[hour];
            EditorTheme.FillRect(spriteBatch, rect, GetScheduleActionColor(action));
            EditorTheme.DrawBorder(spriteBatch, rect, EditorTheme.Border);

            if (hour % 3 == 0)
                EditorTheme.DrawText(spriteBatch, EditorTheme.Tiny, hour.ToString("00"),
                    new Vector2(rect.X + 2, rect.Y + 3), Color.Black);

            EditorTheme.DrawText(spriteBatch, EditorTheme.Tiny, GetScheduleActionShortLabel(action),
                new Vector2(rect.X + 2, rect.Bottom - 13), Color.Black);
        }

        EditorTheme.DrawText(spriteBatch, EditorTheme.Tiny, "00      03      06      09      12      15      18      21",
            new Vector2(_scheduleGridRect.X, _scheduleGridRect.Bottom + 3), EditorTheme.TextMuted);

        EditorTheme.DrawText(spriteBatch, EditorTheme.Tiny, "FREETIME OPTIONS",
            new Vector2(_scheduleEditorRect.X + 8, _scheduleEditorRect.Y + 108), EditorTheme.TextMuted);
        foreach (var control in _freetimeControlRects)
        {
            var option = GetFreetimeOption(control.Action);
            var enabled = option != null;
            var active = control.Kind switch
            {
                "any" => enabled && option is { DayOnly: false, NightOnly: false },
                "day" => enabled && option is { DayOnly: true },
                "night" => enabled && option is { NightOnly: true },
                "remove" => false,
                _ => enabled
            };

            var fill = control.Kind == "remove"
                ? EditorTheme.Panel
                : active ? GetScheduleActionColor(control.Action) : EditorTheme.Panel;
            EditorTheme.FillRect(spriteBatch, control.Rect, fill);
            EditorTheme.DrawBorder(spriteBatch, control.Rect, active ? EditorTheme.Text : EditorTheme.Border);

            var label = control.Kind switch
            {
                "any" => "Any",
                "day" => "Day",
                "night" => "Ngt",
                "remove" => "x",
                _ => GetScheduleActionLabel(control.Action)
            };
            EditorTheme.DrawText(spriteBatch, EditorTheme.Small, Truncate(label, control.Rect.Width - 8),
                new Vector2(control.Rect.X + 5, control.Rect.Y + 5),
                active ? Color.Black : control.Kind == "remove" ? EditorTheme.Warning : EditorTheme.TextDim);
        }
    }

    private bool TryPaintScheduleHour(Point point)
    {
        if (_scheduleGridRect == Rectangle.Empty || !_scheduleGridRect.Contains(point))
            return false;

        for (var hour = 0; hour < _scheduleHourRects.Length; hour++)
        {
            if (!_scheduleHourRects[hour].Contains(point))
                continue;

            SetScheduleHour(hour, _selectedScheduleAction);
            return true;
        }

        return false;
    }

    private bool TryHandleScheduleClick(Point point)
    {
        if (_scheduleEditorRect == Rectangle.Empty || !_scheduleEditorRect.Contains(point))
            return false;

        foreach (var (action, rect) in _scheduleActionRects)
        {
            if (!rect.Contains(point))
                continue;

            _selectedScheduleAction = action;
            _focus = "";
            ClosePicker();
            return true;
        }

        foreach (var control in _freetimeControlRects)
        {
            if (!control.Rect.Contains(point))
                continue;

            ApplyFreetimeControl(control);
            _focus = "";
            ClosePicker();
            CommitDraftToSelected();
            return true;
        }

        return TryPaintScheduleHour(point);
    }

    private bool TryHandlePreviewClick(Point point, Action<string> showMessage)
    {
        if (_previewPanelRect == Rectangle.Empty || !_previewPanelRect.Contains(point))
            return false;

        if (_previewLeftButtonRect.Contains(point))
        {
            _previewFacingIndex = (_previewFacingIndex + PreviewClips.Length - 1) % PreviewClips.Length;
            _focus = "";
            ClosePicker();
            return true;
        }

        if (_previewRightButtonRect.Contains(point))
        {
            _previewFacingIndex = (_previewFacingIndex + 1) % PreviewClips.Length;
            _focus = "";
            ClosePicker();
            return true;
        }

        if (_newHairButtonRect.Contains(point))
        {
            _focus = "";
            ClosePicker();
            OpenHairDialog(isEdit: false, showMessage);
            return true;
        }

        if (_editHairButtonRect.Contains(point))
        {
            _focus = "";
            ClosePicker();
            OpenHairDialog(isEdit: true, showMessage);
            return true;
        }

        return true;
    }

    private void OpenHairDialog(bool isEdit, Action<string> showMessage)
    {
        var styleId = GetField("hairStyleId");
        EntityPrototype? proto = null;
        if (isEdit)
        {
            if (string.Equals(styleId, NoHairSentinel, StringComparison.Ordinal) || string.IsNullOrWhiteSpace(styleId))
            {
                showMessage("Pick a hair style to edit first");
                return;
            }
            proto = _prototypes.GetEntity(styleId);
            if (proto == null)
            {
                showMessage($"Hair style '{styleId}' not found");
                return;
            }
        }

        var style = proto?.Components?["hairStyle"] as JsonObject;
        var id = isEdit ? proto!.Id : "hair_new_style";
        var name = isEdit
            ? FirstNonEmpty(proto!.Name, ReadJsonString(style, "displayName"), proto.Id)
            : "New Hair";
        var gender = isEdit
            ? FirstNonEmpty(ReadJsonString(style, "gender"), "Unisex")
            : (GetDraftGender() == Gender.Female ? "Female" : "Male");
        var sprite = isEdit
            ? FirstNonEmpty(ReadJsonString(style, "sprite"), "sprite.png")
            : "sprite.png";

        _hairDialog.Open(id, name, gender, sprite, isEdit, GetHairSpriteSuggestions, draft => SaveHairStyleFromDialog(draft, showMessage));
    }

    private IReadOnlyList<string> GetHairSpriteSuggestions()
        => _prototypes.GetAllEntities()
            .Where(p => HairAppearanceComponent.IsHairStylePrototype(p))
            .Select(p => ReadJsonString(p.Components?["hairStyle"] as JsonObject, "sprite"))
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .Cast<string>()
            .ToList();

    private (bool Ok, string Message) SaveHairStyleFromDialog(HairStyleDialog.Draft draft, Action<string> showMessage)
    {
        var rawId = string.IsNullOrWhiteSpace(draft.Id) ? "hair_new_style" : draft.Id;
        var id = MakeSafeId(rawId);
        if (string.IsNullOrWhiteSpace(id))
            return (false, "Hair id cannot be empty");

        var name = string.IsNullOrWhiteSpace(draft.Name) ? id : draft.Name;
        var gender = NormalizeHairGender(draft.Gender);
        var spriteName = string.IsNullOrWhiteSpace(draft.Sprite) ? "sprite.png" : Path.GetFileName(draft.Sprite);

        var existing = _prototypes.GetEntity(id);
        if (!draft.IsEdit && existing != null)
            return (false, $"Hair id '{id}' already exists");

        var dir = !string.IsNullOrWhiteSpace(existing?.DirectoryPath)
            ? existing!.DirectoryPath!
            : Path.Combine(_prototypesRoot, "Hair", ToPascalFolder(id));
        Directory.CreateDirectory(dir);

        var proto = new JsonObject
        {
            ["id"] = id,
            ["name"] = name,
            ["category"] = "entity",
            ["base"] = "base_hair",
            ["components"] = new JsonObject
            {
                ["hairStyle"] = new JsonObject
                {
                    ["displayName"] = name,
                    ["gender"] = gender,
                    ["sprite"] = spriteName,
                    ["animations"] = "animations.json",
                    ["srcX"] = 0,
                    ["srcY"] = 0,
                    ["width"] = 32,
                    ["height"] = 32
                }
            }
        };

        File.WriteAllText(Path.Combine(dir, "proto.json"), proto.ToJsonString(JsonOptions));
        EnsureHairSpriteFile(dir, spriteName);
        EnsureHairAnimationsFile(dir, spriteName);

        _prototypes.LoadFromDirectory(_prototypesRoot);
        _fields["hairStyleId"] = id;
        CommitDraftToSelected();
        showMessage(draft.IsEdit ? $"Updated hair style '{id}'" : $"Created hair style '{id}'");
        return (true, "");
    }

    private bool TrySelectMap(Point point)
    {
        if (!_mapListRect.Contains(point))
            return false;

        var y = _mapListRect.Y + 30;
        foreach (var doc in _documents.Skip(_mapScroll))
        {
            if (y + RowH > _mapListRect.Bottom - 8)
                break;

            var row = new Rectangle(_mapListRect.X + 8, y, _mapListRect.Width - 16, RowH - 2);
            if (row.Contains(point))
            {
                CommitDraftToSelected();
                ClosePicker();
                _selectedMapId = doc.MapId;
                _selectedNpcId = doc.Entries.FirstOrDefault()?.Id ?? "";
                _npcScroll = 0;
                _detailsScroll = 0;
                LoadDraftFromSelected();
                return true;
            }

            y += RowH;
        }

        return true;
    }

    private bool TrySelectNpc(Point point)
    {
        if (!_npcListRect.Contains(point))
            return false;

        var doc = GetSelectedDocument();
        if (doc == null)
            return true;

        var y = _npcListRect.Y + 30;
        foreach (var npc in doc.Entries.OrderBy(e => e.Id, StringComparer.OrdinalIgnoreCase).Skip(_npcScroll))
        {
            if (y + RowH > _npcListRect.Bottom - 8)
                break;

            var row = new Rectangle(_npcListRect.X + 8, y, _npcListRect.Width - 16, RowH - 2);
            if (row.Contains(point))
            {
                CommitDraftToSelected();
                ClosePicker();
                _selectedNpcId = npc.Id;
                _detailsScroll = 0;
                LoadDraftFromSelected();
                return true;
            }

            y += RowH;
        }

        return true;
    }

    private bool TryHandleSpecialFieldClick(Point point)
    {
        if (_fieldRects.TryGetValue("gender", out var genderRect) && genderRect.Contains(point))
        {
            _fields["gender"] = string.Equals(GetField("gender"), "Female", StringComparison.OrdinalIgnoreCase)
                ? "Male"
                : "Female";
            EnsureHairStyleMatchesGender();
            _focus = "";
            ClosePicker();
            CommitDraftToSelected();
            return true;
        }

        if (_fieldRects.TryGetValue("pacifist", out var pacifistRect) && pacifistRect.Contains(point))
        {
            _fields["pacifist"] = ReadBoolField("pacifist") ? "false" : "true";
            _focus = "";
            ClosePicker();
            CommitDraftToSelected();
            return true;
        }

        foreach (var (key, rect) in _fieldRects)
        {
            if (!IsNumericField(key) || !rect.Contains(point))
                continue;

            if (point.X <= rect.X + 22 || point.X >= rect.Right - 22)
            {
                AdjustNumericField(key, point.X >= rect.Right - 22 ? 1 : -1);
                _focus = "";
                ClosePicker();
                CommitDraftToSelected();
                return true;
            }
        }

        return false;
    }

    private bool TryOpenPicker(string key, Rectangle rect)
    {
        var options = BuildPickerOptions(key);
        if (options.Count == 0)
            return false;

        CloseColorPicker();
        _pickerOptions.Clear();
        _pickerOptions.AddRange(options);
        _openPickerField = key;
        _pickerScroll = 0;

        const int pickerRowH = 32;
        var visibleRows = Math.Min(9, _pickerOptions.Count);
        var height = Math.Max(pickerRowH, visibleRows * pickerRowH + 4);
        var y = rect.Bottom + 2;
        if (y + height > _detailRect.Bottom - 30)
            y = Math.Max(_detailRect.Y + 34, rect.Y - height - 2);

        _pickerRect = new Rectangle(rect.X, y, Math.Max(rect.Width, 260), height);
        _focus = "";
        return true;
    }

    private bool TryOpenColorPicker(string key, Rectangle rect)
    {
        if (!IsColorPickerField(key))
            return false;

        ClosePicker();
        _openColorField = key;
        _focus = "";

        const int width = 292;
        const int height = 230;
        var x = Math.Clamp(rect.X, _detailRect.X + 8, Math.Max(_detailRect.X + 8, _detailRect.Right - width - 8));
        var y = rect.Bottom + 2;
        if (y + height > _detailRect.Bottom - 30)
            y = Math.Max(_detailRect.Y + 34, rect.Y - height - 2);

        _colorPickerRect = new Rectangle(x, y, width, height);
        BuildColorSwatches();
        return true;
    }

    private bool TryHandlePickerClick(Point point)
    {
        if (string.IsNullOrEmpty(_openPickerField))
            return false;

        if (!_pickerRect.Contains(point))
        {
            ClosePicker();
            return false;
        }

        const int pickerRowH = 32;
        var localY = point.Y - _pickerRect.Y - 2;
        if (localY < 0)
            return true;

        var index = _pickerScroll + localY / pickerRowH;
        if (index >= 0 && index < _pickerOptions.Count)
        {
            ApplyPickerValue(_openPickerField, _pickerOptions[index].Value);
            CommitDraftToSelected();
        }

        ClosePicker();
        return true;
    }

    private bool TryHandleColorPickerClick(Point point)
    {
        if (string.IsNullOrEmpty(_openColorField))
            return false;

        if (!_colorPickerRect.Contains(point))
        {
            CloseColorPicker();
            return false;
        }

        foreach (var swatch in _colorSwatches)
        {
            if (!swatch.Rect.Contains(point))
                continue;

            _fields[_openColorField] = swatch.Hex;
            CommitDraftToSelected();
            CloseColorPicker();
            return true;
        }

        return true;
    }

    private void ClosePicker()
    {
        _openPickerField = "";
        _pickerOptions.Clear();
        _pickerScroll = 0;
        _pickerRect = Rectangle.Empty;
        CloseColorPicker();
    }

    private void CloseColorPicker()
    {
        _openColorField = "";
        _colorSwatches.Clear();
        _colorPickerRect = Rectangle.Empty;
    }

    private void SetScheduleHour(int hour, ScheduleAction action)
    {
        if (hour < 0 || hour >= 24)
            return;

        _scheduleHours[hour] = action;
        _scheduleTargets[hour] = GetDefaultTarget(action);
        _schedulePriorities[hour] = GetDefaultPriority(action);
    }

    private void ApplyFreetimeControl(FreetimeControlRect control)
    {
        var option = GetFreetimeOption(control.Action);
        switch (control.Kind)
        {
            case "toggle":
                if (option == null)
                    _draftFreetime.Add(CreateDefaultFreetimeOption(control.Action));
                else
                    _draftFreetime.Remove(option);
                break;
            case "remove":
                if (option != null)
                    _draftFreetime.Remove(option);
                break;
            case "any":
                if (option != null)
                    SetFreetimeMode(option, dayOnly: false, nightOnly: false);
                break;
            case "day":
                if (option != null)
                    SetFreetimeMode(option, dayOnly: true, nightOnly: false);
                break;
            case "night":
                if (option != null)
                    SetFreetimeMode(option, dayOnly: false, nightOnly: true);
                break;
        }

        RebuildFieldRects();
    }

    private FreetimeOption? GetFreetimeOption(ScheduleAction action)
        => _draftFreetime.FirstOrDefault(option => option.Action == action);

    private static void SetFreetimeMode(FreetimeOption option, bool dayOnly, bool nightOnly)
    {
        option.DayOnly = dayOnly;
        option.NightOnly = nightOnly;
    }

    private static FreetimeOption CreateDefaultFreetimeOption(ScheduleAction action)
    {
        var option = new FreetimeOption
        {
            Action = action,
            Priority = GetDefaultFreetimePriority(action),
            TargetAreaId = GetDefaultTarget(action)
        };

        if (action is ScheduleAction.Visit or ScheduleAction.StayInTavern)
            option.DayOnly = true;
        if (action == ScheduleAction.Visit)
            option.Conditions.Add("has_partner");

        return option;
    }

    private static ScheduleAction ParseScheduleAction(string value, ScheduleAction fallback)
        => Enum.TryParse<ScheduleAction>(value, true, out var action) ? action : fallback;

    private static string GetDefaultTarget(ScheduleAction action)
        => action switch
        {
            ScheduleAction.Sleep => "$house",
            ScheduleAction.EatAtHome => "$house",
            ScheduleAction.StayAtHome => "$house",
            ScheduleAction.Work => "$profession",
            ScheduleAction.SchoolDay => "$school",
            _ => ""
        };

    private static int GetDefaultPriority(ScheduleAction action)
        => action switch
        {
            ScheduleAction.Sleep => 20,
            ScheduleAction.Work => 18,
            ScheduleAction.SchoolDay => 14,
            ScheduleAction.EatAtHome => 12,
            ScheduleAction.StayAtHome => 10,
            ScheduleAction.Free => 8,
            ScheduleAction.StayInTavern => 6,
            ScheduleAction.Wander => 5,
            ScheduleAction.Visit => 5,
            _ => 5
        };

    private static int GetDefaultFreetimePriority(ScheduleAction action)
        => action switch
        {
            ScheduleAction.Visit => 3,
            ScheduleAction.StayInTavern => 2,
            _ => 1
        };

    private static string GetScheduleActionLabel(ScheduleAction action)
        => action switch
        {
            ScheduleAction.EatAtHome => "Eat",
            ScheduleAction.StayAtHome => "Home",
            ScheduleAction.StayInTavern => "Tavern",
            ScheduleAction.SchoolDay => "School",
            _ => action.ToString()
        };

    private static string GetScheduleActionShortLabel(ScheduleAction action)
        => action switch
        {
            ScheduleAction.Sleep => "Sl",
            ScheduleAction.EatAtHome => "Eat",
            ScheduleAction.StayAtHome => "Hm",
            ScheduleAction.Work => "Wrk",
            ScheduleAction.Free => "Free",
            ScheduleAction.Wander => "Wan",
            ScheduleAction.Visit => "Vis",
            ScheduleAction.StayInTavern => "Tav",
            ScheduleAction.SchoolDay => "Sch",
            _ => action.ToString()[..Math.Min(3, action.ToString().Length)]
        };

    private static Color GetScheduleActionColor(ScheduleAction action)
        => action switch
        {
            ScheduleAction.Sleep => new Color(82, 116, 186),
            ScheduleAction.EatAtHome => new Color(214, 179, 86),
            ScheduleAction.StayAtHome => new Color(160, 196, 124),
            ScheduleAction.Work => new Color(219, 137, 80),
            ScheduleAction.Free => new Color(118, 189, 133),
            ScheduleAction.Wander => new Color(126, 174, 205),
            ScheduleAction.Visit => new Color(191, 137, 204),
            ScheduleAction.StayInTavern => new Color(201, 121, 105),
            ScheduleAction.SchoolDay => new Color(111, 188, 183),
            _ => EditorTheme.PanelActive
        };

    private void ApplyPickerValue(string field, string value)
    {
        if (field is "hands" or "inventory")
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                _fields[field] = "";
                return;
            }

            const string removePrefix = "!remove:";
            if (value.StartsWith(removePrefix, StringComparison.OrdinalIgnoreCase))
            {
                var removeId = value[removePrefix.Length..];
                var remaining = ParseCsv(GetField(field))
                    .Where(item => !string.Equals(item, removeId, StringComparison.OrdinalIgnoreCase));
                _fields[field] = string.Join(", ", remaining);
            }
            else
            {
                _fields[field] = AppendCsvValue(GetField(field), value);
            }
            return;
        }

        _fields[field] = value;

        if (field == "houseId")
        {
            ApplyHomeDerivedFieldsToDraft();
        }
        else if (field == "scheduleTemplateId")
        {
            ApplyScheduleTemplateToDraft(value);
        }
    }

    private List<PickerOption> BuildPickerOptions(string field)
    {
        return field switch
        {
            "proto" => BuildPrototypeOptions(),
            "factionId" => BuildFactionOptions(),
            "settlementId" => BuildSettlementOptions(),
            "houseId" => BuildAreaOptions(AreaZoneKinds.House, includeNone: true),
            "professionSlotId" => BuildProfessionSlotOptions(),
            "scheduleTemplateId" => _scheduleTemplateIds
                .Select(id => new PickerOption(id, id, "schedule template", EditorTheme.Accent))
                .ToList(),
            "spriteSource" => BuildSpriteSourceOptions(),
            "hairStyleId" => BuildHairStyleOptions(),
            "outfitTorso" => BuildWearableOptions("torso"),
            "outfitPants" => BuildWearableOptions("pants"),
            "outfitShoes" => BuildWearableOptions("shoes"),
            "outfitBack" => BuildWearableOptions("back"),
            "hands" => BuildItemOptions("hands"),
            "inventory" => BuildItemOptions("inventory"),
            _ => new List<PickerOption>()
        };
    }

    private List<PickerOption> BuildPrototypeOptions()
    {
        var options = _prototypes.GetAllEntities()
            .Where(IsNpcPrototype)
            .OrderBy(proto => proto.Id, StringComparer.OrdinalIgnoreCase)
            .Select(proto => new PickerOption(proto.Id, string.IsNullOrWhiteSpace(proto.Name) ? proto.Id : proto.Name, proto.Id, EditorTheme.Success))
            .ToList();

        if (options.Count == 0)
        {
            options = _prototypes.GetAllEntities()
                .OrderBy(proto => proto.Id, StringComparer.OrdinalIgnoreCase)
                .Select(proto => new PickerOption(proto.Id, string.IsNullOrWhiteSpace(proto.Name) ? proto.Id : proto.Name, proto.Id))
                .ToList();
        }

        return options;
    }

    private List<PickerOption> BuildFactionOptions()
    {
        var options = new List<PickerOption> { new("", "Derived from home/map", "leave empty") };
        if (_worldData != null)
        {
            options.AddRange(_worldData.Factions
                .OrderBy(faction => LocalizationManager.T(string.IsNullOrWhiteSpace(faction.Name) ? faction.Id : faction.Name), StringComparer.OrdinalIgnoreCase)
                .Select(faction => new PickerOption(
                    faction.Id,
                    LocalizationManager.T(string.IsNullOrWhiteSpace(faction.Name) ? faction.Id : faction.Name),
                    faction.Id,
                    EditorTheme.Success)));
        }

        var currentMap = LoadSelectedMapData();
        if (!string.IsNullOrWhiteSpace(currentMap?.FactionId)
            && options.All(o => !string.Equals(o.Value, currentMap.FactionId, StringComparison.OrdinalIgnoreCase)))
        {
            options.Add(new PickerOption(currentMap.FactionId!, currentMap.FactionId!, "map faction", EditorTheme.Warning));
        }

        return options;
    }

    private List<PickerOption> BuildSettlementOptions()
    {
        var options = new List<PickerOption> { new("", "Derived from home/map", "leave empty") };
        if (_worldData != null)
        {
            options.AddRange(_worldData.Cities
                .OrderBy(city => city.Name, StringComparer.OrdinalIgnoreCase)
                .Select(city => new PickerOption(city.Id, string.IsNullOrWhiteSpace(city.Name) ? city.Id : city.Name, city.Id, EditorTheme.Accent)));
        }

        var currentMap = LoadSelectedMapData();
        if (!string.IsNullOrWhiteSpace(currentMap?.CityId)
            && options.All(o => !string.Equals(o.Value, currentMap.CityId, StringComparison.OrdinalIgnoreCase)))
        {
            options.Add(new PickerOption(currentMap.CityId!, currentMap.CityId!, "map city", EditorTheme.Warning));
        }

        return options;
    }

    private List<PickerOption> BuildAreaOptions(string kind, bool includeNone)
    {
        var map = LoadSelectedMapData();
        var options = includeNone
            ? new List<PickerOption> { new("", "None", kind) }
            : new List<PickerOption>();
        if (map == null)
            return options;

        options.AddRange(map.Areas
            .Where(area => string.Equals(area.Kind, kind, StringComparison.OrdinalIgnoreCase))
            .OrderBy(area => area.Id, StringComparer.OrdinalIgnoreCase)
            .Select(area => new PickerOption(area.Id, AreaDisplayName(area), AreaHint(area), kind == AreaZoneKinds.House ? EditorTheme.Success : EditorTheme.Accent)));
        return options;
    }

    private List<PickerOption> BuildProfessionSlotOptions()
    {
        var map = LoadSelectedMapData();
        var options = new List<PickerOption> { new("", "None", "no work") };
        if (map == null)
            return options;

        options.AddRange(map.Areas
            .Where(IsProfessionSlotArea)
            .OrderBy(area => area.Id, StringComparer.OrdinalIgnoreCase)
            .Select(area =>
            {
                var label = string.Equals(area.Kind, AreaZoneKinds.Tavern, StringComparison.OrdinalIgnoreCase)
                    ? $"Tavern: {AreaDisplayName(area)}"
                    : AreaDisplayName(area);
                var accent = string.Equals(area.Kind, AreaZoneKinds.Tavern, StringComparison.OrdinalIgnoreCase)
                    ? EditorTheme.Success
                    : EditorTheme.Accent;
                return new PickerOption(area.Id, label, AreaHint(area), accent);
            }));

        return options;
    }

    private List<PickerOption> BuildBedSlotOptions()
    {
        var options = new List<PickerOption> { new("", "Auto / first free bed", "leave empty") };
        var house = GetSelectedHouseArea();
        if (house == null)
            return options;

        var map = LoadSelectedMapData();
        var named = house.Points
            .Where(point => point.Id.StartsWith("bed_slot_", StringComparison.OrdinalIgnoreCase)
                || point.Id.StartsWith("child_bed_", StringComparison.OrdinalIgnoreCase));
        options.AddRange(named
            .OrderBy(point => point.Id, StringComparer.OrdinalIgnoreCase)
            .Select(point => new PickerOption(point.Id, point.Id, $"tile {point.X}, {point.Y}", EditorTheme.Success)));

        if (map != null)
        {
            options.AddRange(HouseBedScanner.EnumerateAutoBedPoints(house, map, _prototypes)
                .OrderBy(point => point.Id, StringComparer.OrdinalIgnoreCase)
                .Select(point => new PickerOption(point.Id, point.Id, $"bed entity {point.X}, {point.Y}", EditorTheme.Accent)));
        }
        return options;
    }

    private List<PickerOption> BuildSpriteSourceOptions()
    {
        var options = new List<PickerOption> { new("", "Use template sprite", "no override") };
        options.AddRange(_prototypes.GetAllEntities()
            .Select(proto => ReadJsonString(proto.Components?["sprite"] as JsonObject, "source"))
            .Where(source => !string.IsNullOrWhiteSpace(source))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(source => source, StringComparer.OrdinalIgnoreCase)
                .Select(source => new PickerOption(source!, source!, "existing sprite source")));
        return options;
    }

    private List<PickerOption> BuildHairStyleOptions()
    {
        var gender = GetDraftGender();
        var options = new List<PickerOption>
        {
            new("", "Inherited / first matching", "use prototype default"),
            new(NoHairSentinel, "None — bald", "no hair at all", EditorTheme.Warning)
        };
        options.AddRange(_prototypes.GetAllEntities()
            .Where(proto => HairAppearanceComponent.IsHairStylePrototype(proto, gender))
            .OrderBy(proto => proto.Name, StringComparer.OrdinalIgnoreCase)
            .Select(proto => new PickerOption(proto.Id, HairStyleDisplayName(proto), HairStyleHint(proto), EditorTheme.Success)));
        return options;
    }

    private List<PickerOption> BuildWearableOptions(string slot)
    {
        var options = new List<PickerOption> { new("", "None", slot) };
        options.AddRange(_prototypes.GetAllEntities()
            .Where(proto =>
            {
                var wearable = proto.Components?["wearable"] as JsonObject;
                if (wearable == null)
                    return false;
                var wearableSlot = ReadJsonString(wearable, "slot");
                return string.IsNullOrWhiteSpace(wearableSlot)
                       || string.Equals(wearableSlot, slot, StringComparison.OrdinalIgnoreCase);
            })
            .OrderBy(proto => proto.Name, StringComparer.OrdinalIgnoreCase)
            .Select(proto => new PickerOption(proto.Id, string.IsNullOrWhiteSpace(proto.Name) ? proto.Id : proto.Name, proto.Id, EditorTheme.Success)));
        return options;
    }

    private List<PickerOption> BuildItemOptions(string field)
    {
        var options = new List<PickerOption> { new("", "Clear list", "remove all") };
        foreach (var existing in ParseCsv(GetField(field)))
            options.Add(new PickerOption($"!remove:{existing}", $"Remove {existing}", "currently selected", EditorTheme.Warning));

        options.AddRange(_prototypes.GetAllEntities()
            .Where(proto => proto.Components?["item"] != null)
            .OrderBy(proto => proto.Name, StringComparer.OrdinalIgnoreCase)
            .Select(proto => new PickerOption(proto.Id, string.IsNullOrWhiteSpace(proto.Name) ? proto.Id : proto.Name, proto.Id, EditorTheme.Accent)));
        return options;
    }

    private void LoadDraftFromSelected()
    {
        _fields.Clear();
        var npc = GetSelectedNpc();
        if (npc == null)
            return;

        SetField("id", npc.Id);
        SetField("proto", npc.PrototypeId);
        SetField("firstName", npc.Identity.FirstName);
        SetField("lastName", npc.Identity.LastName);
        SetField("gender", string.IsNullOrWhiteSpace(npc.Identity.Gender) ? "Male" : npc.Identity.Gender);
        SetField("ageYears", npc.AgeYears.ToString());
        SetField("factionId", npc.Identity.FactionId);
        SetField("settlementId", npc.Identity.SettlementId);
        SetField("districtId", npc.Identity.DistrictId);
        SetField("houseId", npc.Residence?.HouseId ?? "");
        SetField("bedSlotId", "");
        SetField("professionSlotId", npc.Profession?.SlotId ?? "");
        SetField("description", npc.Description ?? "");
        LoadSkillFields(npc.Skills);
        SetField("hands", string.Join(", ", npc.Hands));
        SetField("inventory", string.Join(", ", npc.Inventory));
        SetField("outfitTorso", npc.Outfit.GetValueOrDefault("torso", ""));
        SetField("outfitPants", npc.Outfit.GetValueOrDefault("pants", ""));
        SetField("outfitShoes", npc.Outfit.GetValueOrDefault("shoes", ""));
        SetField("outfitBack", npc.Outfit.GetValueOrDefault("back", ""));

        var p = npc.Personality ?? new NpcRosterPersonality();
        SetField("infidelity", p.Infidelity.ToString());
        SetField("vengefulness", p.Vengefulness.ToString());
        SetField("childWish", p.ChildWish.ToString());
        SetField("marriageWish", p.MarriageWish.ToString());
        SetField("sociability", p.Sociability.ToString());
        SetField("pacifist", p.Pacifist ? "true" : "false");

        var schedule = npc.Components.GetValueOrDefault("schedule");
        SetField("scheduleTemplateId", schedule?["templateId"]?.GetValue<string>() ?? "");
        LoadScheduleDraft(schedule as JsonObject, GetField("scheduleTemplateId"));
        var sprite = npc.Components.GetValueOrDefault("sprite");
        SetField("spriteSource", sprite?["source"]?.GetValue<string>() ?? "");
        SetField("skinColor", sprite?["color"]?.GetValue<string>() ?? "");
        SetField("spriteSrcX", ReadJsonInt(sprite, "srcX", 0).ToString());
        SetField("spriteSrcY", ReadJsonInt(sprite, "srcY", 0).ToString());
        SetField("spriteWidth", ReadJsonInt(sprite, "width", 32).ToString());
        SetField("spriteHeight", ReadJsonInt(sprite, "height", 32).ToString());
        var hair = npc.Components.GetValueOrDefault("hair") ?? GetPrototypeComponent(npc.PrototypeId, "hair");
        var hairObj = hair as JsonObject;
        var hairVisible = hairObj?["visible"]?.GetValue<bool>() ?? true;
        SetField("hairStyleId", hairVisible ? ReadJsonString(hairObj, "styleId") : NoHairSentinel);
        SetField("hairColor", FirstNonEmpty(ReadJsonString(hairObj, "color"), "#4C311FFF"));
        ApplyHomeDerivedFieldsToDraft();
    }

    private void CommitDraftToSelected()
    {
        var doc = GetSelectedDocument();
        var npc = GetSelectedNpc();
        if (doc == null || npc == null || _fields.Count == 0)
            return;

        var oldId = npc.Id;
        npc.Id = MakeSafeId(GetField("id"));
        if (string.IsNullOrWhiteSpace(npc.Id))
            npc.Id = GenerateUniqueNpcId(doc);
        npc.PrototypeId = string.IsNullOrWhiteSpace(GetField("proto")) ? "npc_base" : GetField("proto").Trim();
        npc.AgeYears = ReadIntField("ageYears", 25);
        npc.Identity.FirstName = GetField("firstName").Trim();
        npc.Identity.LastName = GetField("lastName").Trim();
        npc.Identity.Gender = string.Equals(GetField("gender"), "Female", StringComparison.OrdinalIgnoreCase) ? "Female" : "Male";
        ApplyDerivedIdentity(npc);
        npc.Description = EmptyToNull(GetField("description"));
        npc.Residence = string.IsNullOrWhiteSpace(GetField("houseId"))
            ? null
            : new NpcRosterResidence { HouseId = GetField("houseId").Trim(), BedSlotId = "" };
        AutoAssignRosterBedSlots(doc);
        npc.Profession = string.IsNullOrWhiteSpace(GetField("professionSlotId"))
            ? null
            : new NpcRosterProfession { SlotId = GetField("professionSlotId").Trim() };
        npc.Personality = new NpcRosterPersonality
        {
            Infidelity = ReadIntField("infidelity", -1),
            Vengefulness = ReadIntField("vengefulness", -1),
            ChildWish = ReadIntField("childWish", -1),
            MarriageWish = ReadIntField("marriageWish", -1),
            Sociability = ReadIntField("sociability", -1),
            Pacifist = ReadBoolField("pacifist")
        };
        npc.Skills = ReadSkillFields();
        npc.Hands = ParseCsv(GetField("hands"));
        npc.Inventory = ParseCsv(GetField("inventory"));
        npc.Outfit = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        AddOutfit(npc, "torso", GetField("outfitTorso"));
        AddOutfit(npc, "pants", GetField("outfitPants"));
        AddOutfit(npc, "shoes", GetField("outfitShoes"));
        AddOutfit(npc, "back", GetField("outfitBack"));
        ApplyComponentDraft(npc);

        if (string.Equals(_selectedNpcId, oldId, StringComparison.OrdinalIgnoreCase))
            _selectedNpcId = npc.Id;
    }

    private void ApplyDerivedIdentity(NpcRosterEntry npc)
    {
        var map = LoadSelectedMapData();
        var house = GetSelectedHouseArea(map);

        var fallbackFaction = GetField("factionId").Trim();
        var fallbackSettlement = GetField("settlementId").Trim();
        var derivedDistrict = house != null
            ? GetAreaProperty(house, "districtId", "district", "district_id")
            : "";

        npc.Identity.FactionId = FirstNonEmpty(
            house != null ? GetAreaProperty(house, "factionId", "faction", "faction_id") : "",
            map?.FactionId,
            fallbackFaction);
        npc.Identity.SettlementId = FirstNonEmpty(
            house != null ? GetAreaProperty(house, "settlementId", "settlement", "settlement_id", "cityId", "city") : "",
            map?.CityId,
            fallbackSettlement);
        npc.Identity.DistrictId = derivedDistrict;

        SetField("factionId", npc.Identity.FactionId);
        SetField("settlementId", npc.Identity.SettlementId);
        SetField("districtId", npc.Identity.DistrictId);
    }

    private void ApplyHomeDerivedFieldsToDraft()
    {
        var map = LoadSelectedMapData();
        var house = GetSelectedHouseArea(map);
        if (house == null && map == null)
            return;

        var faction = FirstNonEmpty(
            house != null ? GetAreaProperty(house, "factionId", "faction", "faction_id") : "",
            map?.FactionId,
            GetField("factionId"));
        var settlement = FirstNonEmpty(
            house != null ? GetAreaProperty(house, "settlementId", "settlement", "settlement_id", "cityId", "city") : "",
            map?.CityId,
            GetField("settlementId"));
        var district = house != null
            ? GetAreaProperty(house, "districtId", "district", "district_id")
            : GetField("districtId");

        SetField("factionId", faction);
        SetField("settlementId", settlement);
        SetField("districtId", district);
    }

    private void ApplyComponentDraft(NpcRosterEntry npc)
    {
        npc.Components["schedule"] = BuildScheduleComponentJson();

        var hasSprite = !string.IsNullOrWhiteSpace(GetField("spriteSource"))
                        || !string.IsNullOrWhiteSpace(GetField("skinColor"));
        if (hasSprite)
        {
            var sprite = new JsonObject
            {
                ["srcX"] = ReadIntField("spriteSrcX", 0),
                ["srcY"] = ReadIntField("spriteSrcY", 0),
                ["width"] = ReadIntField("spriteWidth", 32),
                ["height"] = ReadIntField("spriteHeight", 32),
                ["ySort"] = true,
                ["layerDepth"] = 0.5f
            };
            if (!string.IsNullOrWhiteSpace(GetField("spriteSource")))
                sprite["source"] = GetField("spriteSource").Trim();
            if (!string.IsNullOrWhiteSpace(GetField("skinColor")))
                sprite["color"] = GetField("skinColor").Trim();
            npc.Components["sprite"] = sprite;
        }
        else
        {
            npc.Components.Remove("sprite");
        }

        var hairStyle = GetField("hairStyleId");
        var hairColor = GetField("hairColor");
        if (string.Equals(hairStyle, NoHairSentinel, StringComparison.Ordinal))
        {
            npc.Components["hair"] = new JsonObject { ["visible"] = false };
        }
        else if (!string.IsNullOrWhiteSpace(hairStyle) || !string.IsNullOrWhiteSpace(hairColor))
        {
            var hair = new JsonObject();
            if (!string.IsNullOrWhiteSpace(hairStyle))
                hair["styleId"] = hairStyle.Trim();
            if (!string.IsNullOrWhiteSpace(hairColor))
                hair["color"] = hairColor.Trim();
            npc.Components["hair"] = hair;
        }
        else
        {
            npc.Components.Remove("hair");
        }
    }

    private void LoadScheduleDraft(JsonObject? schedule, string templateId)
    {
        ResetScheduleDraft();

        var loadedSlots = false;
        if (schedule?["slots"] is JsonArray slots)
            loadedSlots = ReadScheduleSlots(slots);
        else if (!string.IsNullOrWhiteSpace(templateId)
                 && _scheduleTemplates.TryGetValue(templateId, out var template)
                 && template["slots"] is JsonArray templateSlots)
            loadedSlots = ReadScheduleSlots(templateSlots);

        if (!loadedSlots)
            FillScheduleHours(ScheduleAction.Free);

        var loadedFreetime = false;
        if (schedule?["freetime"] is JsonArray freetime)
            loadedFreetime = ReadFreetimeOptions(freetime);
        else if (!string.IsNullOrWhiteSpace(templateId)
                 && _scheduleTemplates.TryGetValue(templateId, out var template)
                 && template["freetime"] is JsonArray templateFreetime)
            loadedFreetime = ReadFreetimeOptions(templateFreetime);

        if (!loadedFreetime)
            AddDefaultFreetime();
    }

    private void ApplyScheduleTemplateToDraft(string templateId)
    {
        if (!string.IsNullOrWhiteSpace(templateId)
            && _scheduleTemplates.TryGetValue(templateId, out var template))
        {
            LoadScheduleDraft(template, templateId);
            return;
        }

        ResetScheduleDraft();
        FillScheduleHours(ScheduleAction.Free);
        AddDefaultFreetime();
    }

    private void ResetScheduleDraft()
    {
        _draftFreetime.Clear();
        for (var hour = 0; hour < 24; hour++)
        {
            _scheduleHours[hour] = ScheduleAction.Free;
            _scheduleTargets[hour] = "";
            _schedulePriorities[hour] = GetDefaultPriority(ScheduleAction.Free);
        }
    }

    private void FillScheduleHours(ScheduleAction action)
    {
        for (var hour = 0; hour < 24; hour++)
            SetScheduleHour(hour, action);
    }

    private bool ReadScheduleSlots(JsonArray slots)
    {
        var any = false;
        foreach (var node in slots)
        {
            if (node is not JsonObject obj)
                continue;

            var start = Math.Clamp(ReadJsonInt(obj, "start", 0), 0, 23);
            var end = Math.Clamp(ReadJsonInt(obj, "end", start + 1), 0, 24);
            if (end <= start)
                continue;

            var action = ParseScheduleAction(ReadJsonString(obj, "action"), ScheduleAction.Free);
            var target = ReadJsonString(obj, "targetAreaId");
            var priority = ReadJsonInt(obj, "priority", GetDefaultPriority(action));
            for (var hour = start; hour < end; hour++)
            {
                _scheduleHours[hour] = action;
                _scheduleTargets[hour] = string.IsNullOrWhiteSpace(target) ? GetDefaultTarget(action) : target;
                _schedulePriorities[hour] = priority;
                any = true;
            }
        }

        return any;
    }

    private bool ReadFreetimeOptions(JsonArray freetime)
    {
        _draftFreetime.Clear();
        foreach (var node in freetime)
        {
            if (node is not JsonObject obj)
                continue;

            var action = ParseScheduleAction(ReadJsonString(obj, "action"), ScheduleAction.Wander);
            if (action == ScheduleAction.Free || _draftFreetime.Any(option => option.Action == action))
                continue;

            var option = new FreetimeOption
            {
                Action = action,
                TargetAreaId = ReadJsonString(obj, "targetAreaId"),
                Priority = ReadJsonInt(obj, "priority", GetDefaultFreetimePriority(action)),
                DayOnly = obj["dayOnly"]?.GetValue<bool>() ?? false,
                NightOnly = obj["nightOnly"]?.GetValue<bool>() ?? false
            };

            if (obj["conditions"] is JsonArray conditions)
                foreach (var condition in conditions)
                {
                    var value = condition?.GetValue<string>()?.Trim();
                    if (!string.IsNullOrWhiteSpace(value))
                        option.Conditions.Add(value);
                }

            _draftFreetime.Add(option);
        }

        return _draftFreetime.Count > 0;
    }

    private void AddDefaultFreetime()
    {
        _draftFreetime.Clear();
        _draftFreetime.Add(new FreetimeOption { Action = ScheduleAction.Wander, Priority = 1 });
    }

    private JsonObject BuildScheduleComponentJson()
    {
        var obj = new JsonObject();
        var templateId = GetField("scheduleTemplateId").Trim();
        if (!string.IsNullOrWhiteSpace(templateId))
            obj["templateId"] = templateId;

        obj["slots"] = BuildScheduleSlotsJson();
        obj["freetime"] = BuildFreetimeJson();
        return obj;
    }

    private JsonArray BuildScheduleSlotsJson()
    {
        var result = new JsonArray();
        var start = 0;
        while (start < 24)
        {
            var action = _scheduleHours[start];
            var target = FirstNonEmpty(_scheduleTargets[start], GetDefaultTarget(action));
            var priority = _schedulePriorities[start] == 0 ? GetDefaultPriority(action) : _schedulePriorities[start];
            var end = start + 1;
            while (end < 24
                   && _scheduleHours[end] == action
                   && string.Equals(FirstNonEmpty(_scheduleTargets[end], GetDefaultTarget(action)), target, StringComparison.OrdinalIgnoreCase)
                   && (_schedulePriorities[end] == 0 ? GetDefaultPriority(action) : _schedulePriorities[end]) == priority)
            {
                end++;
            }

            result.Add(new JsonObject
            {
                ["start"] = start,
                ["end"] = end,
                ["action"] = action.ToString(),
                ["targetAreaId"] = target,
                ["priority"] = priority
            });

            start = end;
        }

        return result;
    }

    private JsonArray BuildFreetimeJson()
    {
        var result = new JsonArray();
        foreach (var option in _draftFreetime
                     .OrderByDescending(option => option.Priority)
                     .ThenBy(option => option.Action.ToString(), StringComparer.OrdinalIgnoreCase))
        {
            var obj = new JsonObject
            {
                ["action"] = option.Action.ToString(),
                ["priority"] = option.Priority
            };
            if (!string.IsNullOrWhiteSpace(option.TargetAreaId))
                obj["targetAreaId"] = option.TargetAreaId;
            if (option.DayOnly)
                obj["dayOnly"] = true;
            if (option.NightOnly)
                obj["nightOnly"] = true;
            if (option.Conditions.Count > 0)
            {
                var conditions = new JsonArray();
                foreach (var condition in option.Conditions.Where(c => !string.IsNullOrWhiteSpace(c)))
                    conditions.Add(condition.Trim());
                obj["conditions"] = conditions;
            }
            result.Add(obj);
        }

        return result;
    }

    private void RebuildLayout()
    {
        var viewport = _graphics.Viewport;
        const int topChrome = 72;
        const int bottomChrome = 24;
        _bounds = new Rectangle(8, topChrome + 8, viewport.Width - 16, viewport.Height - topChrome - bottomChrome - 16);
        var y = _bounds.Y + 38;
        var h = _bounds.Height - 46;
        _mapListRect = new Rectangle(_bounds.X + 10, y, 220, h);
        _npcListRect = new Rectangle(_mapListRect.Right + 8, y, 250, h);
        _detailRect = new Rectangle(_npcListRect.Right + 8, y, _bounds.Right - _npcListRect.Right - 18, h);
        _detailsContentRect = new Rectangle(_detailRect.X + 12, _detailRect.Y + 38, _detailRect.Width - 24, _detailRect.Height - 70);
    }

    private void RebuildFieldRects()
    {
        _fieldRects.Clear();
        var previewWidth = Math.Clamp(_detailsContentRect.Width / 3, 190, 250);
        if (_detailsContentRect.Width < 650)
            previewWidth = 0;

        _previewPanelRect = previewWidth > 0
            ? new Rectangle(_detailsContentRect.Right - previewWidth, _detailsContentRect.Y, previewWidth, 222)
            : Rectangle.Empty;
        _previewLeftButtonRect = _previewPanelRect == Rectangle.Empty
            ? Rectangle.Empty
            : new Rectangle(_previewPanelRect.X + 10, _previewPanelRect.Bottom - 30, 32, 22);
        _previewRightButtonRect = _previewPanelRect == Rectangle.Empty
            ? Rectangle.Empty
            : new Rectangle(_previewPanelRect.Right - 42, _previewPanelRect.Bottom - 30, 32, 22);
        if (_previewPanelRect == Rectangle.Empty)
        {
            _newHairButtonRect = Rectangle.Empty;
            _editHairButtonRect = Rectangle.Empty;
        }
        else
        {
            var btnTotal = _previewPanelRect.Width - 96;
            var halfW = (btnTotal - 4) / 2;
            _newHairButtonRect = new Rectangle(_previewPanelRect.X + 48, _previewPanelRect.Bottom - 30, halfW, 22);
            _editHairButtonRect = new Rectangle(_newHairButtonRect.Right + 4, _previewPanelRect.Bottom - 30, btnTotal - halfW - 4, 22);
        }

        var fieldsWidth = _detailsContentRect.Width - (previewWidth > 0 ? previewWidth + Gap : 0);
        var x = _detailsContentRect.X;
        var y = _detailsContentRect.Y - _detailsScroll * 34;
        var colW = Math.Max(170, (fieldsWidth - Gap) / 2);
        var rightX = x + colW + Gap;

        foreach (var section in GetSections())
        {
            y += 22;
            foreach (var field in section.Fields)
            {
                var fx = field.Wide ? x : (field.Right ? rightX : x);
                var fw = field.Wide ? fieldsWidth : colW;
                _fieldRects[field.Key] = new Rectangle(fx, y + 14, fw, FieldH);
                if (field.Right || field.Wide)
                    y += 44;
            }

            if (string.Equals(section.Title, "Home / Work / AI", StringComparison.OrdinalIgnoreCase))
            {
                y += 8;
                RebuildScheduleEditorRects(x, y, fieldsWidth);
                y += ScheduleEditorHeight;
            }
        }
    }

    private void RebuildScheduleEditorRects(int x, int y, int width)
    {
        _scheduleActionRects.Clear();
        _freetimeControlRects.Clear();
        for (var i = 0; i < _scheduleHourRects.Length; i++)
            _scheduleHourRects[i] = Rectangle.Empty;

        _scheduleEditorRect = new Rectangle(x, y, width, ScheduleEditorHeight);
        var paletteY = y + 25;
        var paletteX = x + 8;
        foreach (var action in SchedulePaintActions)
        {
            var label = GetScheduleActionLabel(action);
            var rectWidth = Math.Max(56, (int)EditorTheme.Small.MeasureString(label).X + 18);
            var rect = new Rectangle(paletteX, paletteY, rectWidth, 22);
            _scheduleActionRects[action] = rect;
            paletteX += rectWidth + 6;
        }

        var gridX = x + 8;
        var gridY = y + 58;
        var cellW = Math.Max(16, (width - 16) / 24);
        _scheduleGridRect = new Rectangle(gridX, gridY, cellW * 24, 40);
        for (var hour = 0; hour < 24; hour++)
            _scheduleHourRects[hour] = new Rectangle(gridX + hour * cellW, gridY, cellW, 40);

        var freeX = x + 8;
        var freeY = y + 122;
        foreach (var action in FreetimeActions)
        {
            var label = GetScheduleActionLabel(action);
            var rectWidth = Math.Max(76, (int)EditorTheme.Small.MeasureString(label).X + 20);
            var main = new Rectangle(freeX, freeY, rectWidth, 22);
            _freetimeControlRects.Add(new FreetimeControlRect(action, "toggle", main));
            freeX += rectWidth + 4;

            if (GetFreetimeOption(action) != null)
            {
                foreach (var kind in new[] { "any", "day", "night" })
                {
                    var modeRect = new Rectangle(freeX, freeY, 28, 22);
                    _freetimeControlRects.Add(new FreetimeControlRect(action, kind, modeRect));
                    freeX += 29;
                }

                var remove = new Rectangle(freeX, freeY, 22, 22);
                _freetimeControlRects.Add(new FreetimeControlRect(action, "remove", remove));
                freeX += 30;
            }

            freeX += 8;
        }
    }

    private IEnumerable<(string Title, List<FieldDef> Fields)> GetSections()
    {
        yield return ("Identity", new()
        {
            new("id", "ID"), new("proto", "Template Proto", true),
            new("firstName", "First Name"), new("lastName", "Last Name", true),
            new("gender", "Gender"), new("ageYears", "Age Years", true),
            new("factionId", "Faction"), new("settlementId", "Settlement", true),
            new("description", "Description", false, true),
        });
        yield return ("Home / Work / AI", new()
        {
            new("houseId", "House"), new("professionSlotId", "Profession Slot", true),
            new("scheduleTemplateId", "Schedule Template", false, true),
        });
        yield return ("Appearance", new()
        {
            new("spriteSource", "Sprite Source"), new("skinColor", "Skin Color", true),
            new("spriteSrcX", "Src X"), new("spriteSrcY", "Src Y", true),
            new("spriteWidth", "Width"), new("spriteHeight", "Height", true),
            new("hairStyleId", "Hair Style"), new("hairColor", "Hair Color #RRGGBBAA", true),
        });
        yield return ("Outfit / Items", new()
        {
            new("outfitTorso", "Torso"), new("outfitPants", "Pants", true),
            new("outfitShoes", "Shoes"), new("outfitBack", "Back", true),
            new("hands", "Hands", false, true),
            new("inventory", "Inventory", false, true),
        });
        var personalityAndSkills = new List<FieldDef>
        {
            new("infidelity", "Infidelity"), new("vengefulness", "Vengefulness", true),
            new("childWish", "Child Wish"), new("marriageWish", "Marriage Wish", true),
            new("sociability", "Sociability"), new("pacifist", "Pacifist", true),
        };

        var skillIds = GetSkillIdsForEditor().ToList();
        for (var i = 0; i < skillIds.Count; i++)
        {
            var skillId = skillIds[i];
            personalityAndSkills.Add(new FieldDef(
                SkillFieldKey(skillId),
                $"Skill / {FormatSkillLabel(skillId)}",
                i % 2 == 1));
        }

        yield return ("Personality / Skills", personalityAndSkills);
    }

    private IEnumerable<FieldDef> GetFieldDefinitions() => GetSections().SelectMany(s => s.Fields);

    private MapData? LoadSelectedMapData()
    {
        if (string.IsNullOrWhiteSpace(_selectedMapId))
            return null;

        var path = Path.Combine(_mapsRoot, $"{_selectedMapId}.map.json");
        if (!File.Exists(path))
            return null;

        var writeTime = File.GetLastWriteTimeUtc(path);
        if (string.Equals(_cachedMapId, _selectedMapId, StringComparison.OrdinalIgnoreCase)
            && _cachedMapWriteTimeUtc == writeTime)
        {
            return _cachedMapData;
        }

        try
        {
            _cachedMapId = _selectedMapId;
            _cachedMapWriteTimeUtc = writeTime;
            _cachedMapData = JsonSerializer.Deserialize<MapData>(File.ReadAllText(path), JsonOptions);
            return _cachedMapData;
        }
        catch (Exception e)
        {
            Console.WriteLine($"[NpcEditor] Failed to read map '{_selectedMapId}': {e.Message}");
            _cachedMapId = "";
            _cachedMapWriteTimeUtc = default;
            _cachedMapData = null;
            return null;
        }
    }

    private AreaZoneData? GetSelectedHouseArea(MapData? map = null)
    {
        var houseId = GetField("houseId").Trim();
        if (string.IsNullOrWhiteSpace(houseId))
            return null;

        map ??= LoadSelectedMapData();
        return map?.Areas.FirstOrDefault(area =>
            string.Equals(area.Kind, AreaZoneKinds.House, StringComparison.OrdinalIgnoreCase)
            && string.Equals(area.Id, houseId, StringComparison.OrdinalIgnoreCase));
    }

    private void AutoAssignRosterBedSlots(RosterDocument doc)
    {
        var map = LoadMapDataForRoster(doc.MapId);
        if (map == null)
            return;

        var houses = map.Areas
            .Where(area => string.Equals(area.Kind, AreaZoneKinds.House, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(area => area.Id, area => area, StringComparer.OrdinalIgnoreCase);

        var usedByHouse = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var npc in doc.Entries)
        {
            var residence = npc.Residence;
            if (residence == null || string.IsNullOrWhiteSpace(residence.HouseId))
                continue;

            if (!houses.TryGetValue(residence.HouseId, out var house))
            {
                residence.BedSlotId = "";
                continue;
            }

            var slots = GetAssignableBedPoints(house, map)
                .Select(point => point.Id)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (slots.Count == 0)
            {
                residence.BedSlotId = "";
                continue;
            }

            if (!usedByHouse.TryGetValue(house.Id, out var used))
            {
                used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                usedByHouse[house.Id] = used;
            }

            var current = residence.BedSlotId.Trim();
            var keepCurrent = slots.Contains(current, StringComparer.OrdinalIgnoreCase) && !used.Contains(current);
            residence.BedSlotId = keepCurrent
                ? current
                : slots.FirstOrDefault(id => !used.Contains(id)) ?? slots[0];

            used.Add(residence.BedSlotId);
        }
    }

    private MapData? LoadMapDataForRoster(string mapId)
    {
        if (string.Equals(mapId, _selectedMapId, StringComparison.OrdinalIgnoreCase))
            return LoadSelectedMapData();

        var path = Path.Combine(_mapsRoot, $"{mapId}.map.json");
        if (!File.Exists(path))
            return null;

        try
        {
            return JsonSerializer.Deserialize<MapData>(File.ReadAllText(path), JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private string GetAutoBedDisplay()
    {
        if (string.IsNullOrWhiteSpace(GetField("houseId")))
            return "no bed";

        var house = GetSelectedHouseArea();
        if (house == null)
            return "missing house";

        var slots = GetAssignableBedPoints(house)
            .Select(point => point.Id)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (slots.Count == 0)
            return "no bed_slot";

        var doc = GetSelectedDocument();
        var npc = GetSelectedNpc();
        var used = doc == null || npc == null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : doc.Entries
                .Where(entry => !string.Equals(entry.Id, npc.Id, StringComparison.OrdinalIgnoreCase))
                .Where(entry => string.Equals(entry.Residence?.HouseId, house.Id, StringComparison.OrdinalIgnoreCase))
                .Select(entry => entry.Residence?.BedSlotId ?? "")
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var picked = slots.FirstOrDefault(id => !used.Contains(id)) ?? slots[0];
        return $"auto {picked}";
    }

    private IEnumerable<AreaPointData> GetAssignableBedPoints(AreaZoneData house, MapData? map = null)
    {
        foreach (var point in house.Points.Where(point =>
                     point.Id.StartsWith("bed_slot_", StringComparison.OrdinalIgnoreCase)))
            yield return point;

        map ??= LoadSelectedMapData();
        if (map == null)
            yield break;

        foreach (var auto in HouseBedScanner.EnumerateAutoBedPoints(house, map, _prototypes))
            yield return auto;
    }

    private static string GetAreaProperty(AreaZoneData area, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (area.Properties.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }

        return "";
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }

        return "";
    }

    private static string AreaDisplayName(AreaZoneData area)
    {
        var name = GetAreaProperty(area, "name", "displayName", "label");
        return string.IsNullOrWhiteSpace(name) ? area.Id : $"{name}  ({area.Id})";
    }

    private static string AreaHint(AreaZoneData area)
    {
        var bits = new List<string>();
        var settlement = GetAreaProperty(area, "settlementId", "settlement", "cityId", "city");
        var district = GetAreaProperty(area, "districtId", "district");
        var profession = string.Equals(area.Kind, AreaZoneKinds.Tavern, StringComparison.OrdinalIgnoreCase)
            ? "innkeeper"
            : GetAreaProperty(area, "professionId", "profession");
        if (!string.IsNullOrWhiteSpace(profession))
            bits.Add(profession);
        if (!string.IsNullOrWhiteSpace(settlement))
            bits.Add(settlement);
        if (!string.IsNullOrWhiteSpace(district))
            bits.Add(district);
        bits.Add($"{area.Points.Count} points");
        return string.Join(" | ", bits);
    }

    private static bool IsProfessionSlotArea(AreaZoneData area)
    {
        if (string.Equals(area.Kind, AreaZoneKinds.Profession, StringComparison.OrdinalIgnoreCase))
            return true;

        return string.Equals(area.Kind, AreaZoneKinds.Tavern, StringComparison.OrdinalIgnoreCase);
    }

    private JsonObject? GetPrototypeComponent(string prototypeId, string componentId)
        => _prototypes.GetEntity(prototypeId)?.Components?[componentId] as JsonObject;

    private Gender GetDraftGender()
        => string.Equals(GetField("gender"), "Female", StringComparison.OrdinalIgnoreCase)
            ? Gender.Female
            : Gender.Male;

    private void EnsureHairStyleMatchesGender()
    {
        var styleId = GetField("hairStyleId");
        if (string.IsNullOrWhiteSpace(styleId) || string.Equals(styleId, NoHairSentinel, StringComparison.Ordinal))
            return;

        var gender = GetDraftGender();
        if (_prototypes.GetEntity(styleId) is { } proto
            && HairAppearanceComponent.IsHairStylePrototype(proto, gender))
        {
            return;
        }

        _fields["hairStyleId"] = HairAppearanceComponent.FindStylePrototype(_prototypes, "", gender)?.Id ?? "";
    }

    private Texture2D? LoadPrototypeTexture(EntityPrototype proto, string source)
    {
        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(proto.DirectoryPath))
            return null;

        var path = Path.IsPathRooted(source)
            ? source
            : Path.GetFullPath(Path.Combine(proto.DirectoryPath, source));
        return _assets.LoadFromFile(path);
    }

    private static string? ResolvePrototypeFile(EntityPrototype proto, string source)
    {
        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(proto.DirectoryPath))
            return null;

        return Path.IsPathRooted(source)
            ? source
            : Path.GetFullPath(Path.Combine(proto.DirectoryPath, source));
    }

    private static Rectangle? ResolveAnimationFrame(string? animationPath, string clip)
    {
        if (string.IsNullOrWhiteSpace(animationPath) || !File.Exists(animationPath))
            return null;

        try
        {
            var root = JsonNode.Parse(File.ReadAllText(animationPath)) as JsonObject;
            var frames = root?["clips"]?[clip]?["frames"] as JsonArray
                         ?? root?["clips"]?["idle_down"]?["frames"] as JsonArray;
            var frame = frames?.FirstOrDefault() as JsonObject;
            if (frame == null)
                return null;

            return new Rectangle(
                ReadJsonInt(frame, "srcX", 0),
                ReadJsonInt(frame, "srcY", 0),
                ReadJsonInt(frame, "width", 32),
                ReadJsonInt(frame, "height", 32));
        }
        catch
        {
            return null;
        }
    }

    private static string? ResolveAnimationTexturePath(string? animationPath)
    {
        if (string.IsNullOrWhiteSpace(animationPath) || !File.Exists(animationPath))
            return null;

        try
        {
            var root = JsonNode.Parse(File.ReadAllText(animationPath)) as JsonObject;
            var texture = root?["texture"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(texture))
                return null;

            var dir = Path.GetDirectoryName(animationPath);
            return string.IsNullOrWhiteSpace(dir) ? texture : Path.GetFullPath(Path.Combine(dir, texture));
        }
        catch
        {
            return null;
        }
    }

    private static string HairStyleDisplayName(EntityPrototype proto)
    {
        var style = proto.Components?["hairStyle"] as JsonObject;
        var display = FirstNonEmpty(ReadJsonString(style, "displayName"), proto.Name, proto.Id);
        return $"{display}  ({proto.Id})";
    }

    private static string HairStyleHint(EntityPrototype proto)
    {
        var style = proto.Components?["hairStyle"] as JsonObject;
        return $"{FirstNonEmpty(ReadJsonString(style, "gender"), "Unisex")} | {FirstNonEmpty(ReadJsonString(style, "sprite"), "sprite.png")}";
    }

    private static string NormalizeHairGender(string value)
        => Enum.TryParse<HairStyleGender>(value, true, out var gender) ? gender.ToString() : HairStyleGender.Unisex.ToString();

    private static string ToPascalFolder(string id)
    {
        var parts = id.Split(new[] { '_', '-', '.', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        var result = string.Concat(parts.Select(part =>
            char.ToUpperInvariant(part[0]) + (part.Length > 1 ? part[1..] : "")));
        return string.IsNullOrWhiteSpace(result) ? "HairStyle" : result;
    }

    private void EnsureHairSpriteFile(string directory, string spriteName)
    {
        var target = Path.Combine(directory, spriteName);
        if (File.Exists(target))
            return;

        var placeholder = Path.Combine(_prototypesRoot, "Hair", "ShortMessy", "sprite.png");
        if (File.Exists(placeholder))
            File.Copy(placeholder, target);
    }

    private static void EnsureHairAnimationsFile(string directory, string spriteName)
    {
        var path = Path.Combine(directory, "animations.json");
        if (File.Exists(path))
        {
            try
            {
                var existing = JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
                if (existing != null)
                {
                    existing["texture"] = spriteName;
                    File.WriteAllText(path, existing.ToJsonString(JsonOptions));
                    return;
                }
            }
            catch
            {
                // Fall through and write a clean four-facing placeholder animation file.
            }
        }

        var node = new JsonObject
        {
            ["texture"] = spriteName,
            ["clips"] = new JsonObject
            {
                ["idle_down"] = BuildOneFrameClip(0),
                ["idle_up"] = BuildOneFrameClip(32),
                ["idle_left"] = BuildOneFrameClip(64),
                ["idle_right"] = BuildOneFrameClip(96)
            }
        };
        File.WriteAllText(path, node.ToJsonString(JsonOptions));
    }

    private static JsonObject BuildOneFrameClip(int srcX)
        => new()
        {
            ["loop"] = true,
            ["frames"] = new JsonArray
            {
                new JsonObject
                {
                    ["srcX"] = srcX,
                    ["srcY"] = 0,
                    ["width"] = 32,
                    ["height"] = 32,
                    ["duration"] = 1.0f
                }
            }
        };

    private static float ReadJsonFloat(JsonObject? obj, string key, float fallback)
        => obj != null && obj[key] != null && float.TryParse(obj[key]!.ToString(), out var value) ? value : fallback;

    private static Color MultiplyColors(Color a, Color b)
        => new(
            (byte)(a.R * b.R / 255),
            (byte)(a.G * b.G / 255),
            (byte)(a.B * b.B / 255),
            (byte)(a.A * b.A / 255));

    private static Color ColorFromHsv(float hue, float saturation, float value)
    {
        hue = Math.Clamp(hue, 0f, 1f);
        saturation = Math.Clamp(saturation, 0f, 1f);
        value = Math.Clamp(value, 0f, 1f);

        var h = hue * 6f;
        var i = (int)MathF.Floor(h);
        var f = h - i;
        var p = value * (1f - saturation);
        var q = value * (1f - f * saturation);
        var t = value * (1f - (1f - f) * saturation);

        var (r, g, b) = (i % 6) switch
        {
            0 => (value, t, p),
            1 => (q, value, p),
            2 => (p, value, t),
            3 => (p, q, value),
            4 => (t, p, value),
            _ => (value, p, q)
        };

        return new Color((byte)(r * 255), (byte)(g * 255), (byte)(b * 255), (byte)255);
    }

    private static string ToHex(Color color)
        => $"#{color.R:X2}{color.G:X2}{color.B:X2}{color.A:X2}";

    private static string NormalizeHexColor(string value)
        => ToHex(AssetManager.ParseHexColor(value, Color.White));

    private RosterDocument? GetSelectedDocument()
        => _documents.FirstOrDefault(d => string.Equals(d.MapId, _selectedMapId, StringComparison.OrdinalIgnoreCase));

    private NpcRosterEntry? GetSelectedNpc()
        => GetSelectedDocument()?.Entries.FirstOrDefault(e => string.Equals(e.Id, _selectedNpcId, StringComparison.OrdinalIgnoreCase));

    private string GetField(string key) => _fields.GetValueOrDefault(key, "");
    private void SetField(string key, string? value) => _fields[key] = value ?? "";

    private void HandleTextInput(KeyboardState keys, KeyboardState prevKeys)
    {
        if (!IsTyping)
            return;

        if (keys.IsKeyDown(Keys.LeftControl) || keys.IsKeyDown(Keys.RightControl))
            return;

        if (IsPressed(keys, prevKeys, Keys.Escape))
        {
            _focus = "";
            return;
        }

        var deleteHandled = false;
        foreach (var key in keys.GetPressedKeys().OrderBy(static k => k))
        {
            if (key is Keys.Back or Keys.Delete)
            {
                if (ShouldRepeatKey(keys, prevKeys, key))
                {
                    var value = GetField(_focus);
                    if (value.Length > 0)
                        _fields[_focus] = value[..^1];
                }
                deleteHandled = true;
                continue;
            }

            if (prevKeys.IsKeyDown(key))
                continue;

            var ch = KeyToChar(key, keys.IsKeyDown(Keys.LeftShift) || keys.IsKeyDown(Keys.RightShift));
            if (ch == '\0')
                continue;

            if (!AllowsChar(_focus, ch))
                continue;

            _fields[_focus] = GetField(_focus) + ch;
        }

        if (!deleteHandled)
            ResetDeleteRepeat();
    }

    private static bool AllowsChar(string field, char ch)
    {
        if (field is "description" or "hands" or "inventory" or "spriteSource")
            return !char.IsControl(ch);
        if (field is "skinColor" or "hairColor")
            return char.IsLetterOrDigit(ch) || ch == '#';
        if (IsSkillField(field))
            return char.IsDigit(ch) || ch == '.';
        if (field.Contains("age", StringComparison.OrdinalIgnoreCase) ||
            field.Contains("Wish", StringComparison.OrdinalIgnoreCase) ||
            field is "infidelity" or "vengefulness" or "sociability" or "spriteSrcX" or "spriteSrcY" or "spriteWidth" or "spriteHeight")
            return char.IsDigit(ch) || ch == '-';
        return char.IsLetterOrDigit(ch) || ch is '_' or '-' or '.' or '/' or ',';
    }

    private bool ShouldRepeatKey(KeyboardState keys, KeyboardState prevKeys, Keys key)
    {
        if (prevKeys.IsKeyUp(key) || _heldDeleteKey != key)
        {
            _heldDeleteKey = key;
            _nextDeleteRepeatAt = Environment.TickCount64 + DeleteRepeatInitialDelayMs;
            return true;
        }

        if (Environment.TickCount64 < _nextDeleteRepeatAt)
            return false;

        _nextDeleteRepeatAt = Environment.TickCount64 + DeleteRepeatIntervalMs;
        return true;
    }

    private void ResetDeleteRepeat()
    {
        _heldDeleteKey = null;
        _nextDeleteRepeatAt = 0;
    }

    private static char KeyToChar(Keys key, bool shift)
    {
        if (key is >= Keys.A and <= Keys.Z)
            return (char)((shift ? 'A' : 'a') + (key - Keys.A));
        if (key is >= Keys.D0 and <= Keys.D9)
        {
            const string normal = "0123456789";
            const string shifted = ")!@#$%^&*(";
            var i = key - Keys.D0;
            return shift ? shifted[i] : normal[i];
        }
        if (key is >= Keys.NumPad0 and <= Keys.NumPad9)
            return (char)('0' + (key - Keys.NumPad0));

        return key switch
        {
            Keys.Space => ' ',
            Keys.OemMinus => shift ? '_' : '-',
            Keys.OemPeriod => '.',
            Keys.OemComma => ',',
            Keys.OemPlus => shift ? '+' : '=',
            Keys.OemQuestion => '/',
            Keys.OemPipe => shift ? '|' : '\\',
            Keys.OemQuotes => shift ? '"' : '\'',
            Keys.OemSemicolon => shift ? ':' : ';',
            _ => '\0'
        };
    }

    private void DrawFieldForDefinition(SpriteBatch spriteBatch, FieldDef field, Rectangle rect)
    {
        if (field.Key == "gender")
        {
            DrawButton(spriteBatch, rect, GetField(field.Key), _focus == field.Key, true);
            return;
        }

        if (field.Key == "pacifist")
        {
            var active = ReadBoolField(field.Key);
            DrawButton(spriteBatch, rect, active ? "Yes" : "No", _focus == field.Key, active);
            return;
        }

        if (field.Key is "hands" or "inventory")
        {
            DrawListField(spriteBatch, rect, GetField(field.Key), string.Equals(_openPickerField, field.Key, StringComparison.OrdinalIgnoreCase));
            return;
        }

        if (field.Key is "hairColor" or "skinColor")
        {
            DrawColorField(spriteBatch, rect, GetField(field.Key), string.Equals(_openColorField, field.Key, StringComparison.OrdinalIgnoreCase));
            return;
        }

        if (IsNumericField(field.Key))
        {
            DrawNumberField(spriteBatch, rect, GetField(field.Key), _focus == field.Key);
            return;
        }

        var picker = IsPickerField(field.Key);
        DrawField(spriteBatch, rect, GetDisplayValue(field.Key), _focus == field.Key || string.Equals(_openPickerField, field.Key, StringComparison.OrdinalIgnoreCase), picker);
    }

    private void DrawField(SpriteBatch spriteBatch, Rectangle rect, string value, bool focused, bool picker = false)
    {
        EditorTheme.FillRect(spriteBatch, rect, EditorTheme.BgDeep);
        EditorTheme.DrawBorder(spriteBatch, rect, focused ? EditorTheme.Accent : EditorTheme.BorderSoft);
        var text = focused ? value + "|" : value;
        if (picker && focused)
            text = value;

        var arrowSpace = picker ? 20 : 0;
        EditorTheme.DrawText(spriteBatch, EditorTheme.Small, Truncate(text, rect.Width - 10 - arrowSpace),
            new Vector2(rect.X + 5, rect.Y + 6), focused ? EditorTheme.Text : EditorTheme.TextDim);
        if (picker)
        {
            EditorTheme.DrawText(spriteBatch, EditorTheme.Small, "v",
                new Vector2(rect.Right - 15, rect.Y + 5), focused ? EditorTheme.Text : EditorTheme.TextMuted);
        }
    }

    private void DrawColorField(SpriteBatch spriteBatch, Rectangle rect, string value, bool focused)
    {
        EditorTheme.FillRect(spriteBatch, rect, EditorTheme.BgDeep);
        EditorTheme.DrawBorder(spriteBatch, rect, focused ? EditorTheme.Accent : EditorTheme.BorderSoft);

        var swatch = new Rectangle(rect.X + 5, rect.Y + 4, 18, rect.Height - 8);
        EditorTheme.FillRect(spriteBatch, swatch, AssetManager.ParseHexColor(value, Color.White));
        EditorTheme.DrawBorder(spriteBatch, swatch, EditorTheme.Border);

        var display = string.IsNullOrWhiteSpace(value) ? "auto" : value;
        EditorTheme.DrawText(spriteBatch, EditorTheme.Small, Truncate(display, rect.Width - 54),
            new Vector2(rect.X + 30, rect.Y + 6), focused ? EditorTheme.Text : EditorTheme.TextDim);
        EditorTheme.DrawText(spriteBatch, EditorTheme.Small, "v",
            new Vector2(rect.Right - 15, rect.Y + 5), focused ? EditorTheme.Text : EditorTheme.TextMuted);
    }

    private void DrawListField(SpriteBatch spriteBatch, Rectangle rect, string csv, bool active)
    {
        EditorTheme.FillRect(spriteBatch, rect, EditorTheme.BgDeep);
        EditorTheme.DrawBorder(spriteBatch, rect, active ? EditorTheme.Accent : EditorTheme.BorderSoft);

        var items = ParseCsv(csv);
        if (items.Count == 0)
        {
            EditorTheme.DrawText(spriteBatch, EditorTheme.Small, "Add item...",
                new Vector2(rect.X + 5, rect.Y + 6), EditorTheme.TextMuted);
        }
        else
        {
            var x = rect.X + 5;
            foreach (var item in items.Take(4))
            {
                var chipText = Truncate(item, 90);
                var w = Math.Min(112, (int)EditorTheme.Small.MeasureString(chipText).X + 12);
                if (x + w > rect.Right - 24)
                    break;
                var chip = new Rectangle(x, rect.Y + 4, w, rect.Height - 8);
                EditorTheme.FillRect(spriteBatch, chip, EditorTheme.PanelActive);
                EditorTheme.DrawBorder(spriteBatch, chip, EditorTheme.BorderSoft);
                EditorTheme.DrawText(spriteBatch, EditorTheme.Small, chipText,
                    new Vector2(chip.X + 6, chip.Y + 5), EditorTheme.Text);
                x += w + 5;
            }

            if (items.Count > 4)
                EditorTheme.DrawText(spriteBatch, EditorTheme.Small, $"+{items.Count - 4}",
                    new Vector2(rect.Right - 42, rect.Y + 6), EditorTheme.TextMuted);
        }

        EditorTheme.DrawText(spriteBatch, EditorTheme.Small, "+",
            new Vector2(rect.Right - 15, rect.Y + 5), active ? EditorTheme.Text : EditorTheme.TextMuted);
    }

    private void DrawNumberField(SpriteBatch spriteBatch, Rectangle rect, string value, bool focused)
    {
        EditorTheme.FillRect(spriteBatch, rect, EditorTheme.BgDeep);
        EditorTheme.DrawBorder(spriteBatch, rect, focused ? EditorTheme.Accent : EditorTheme.BorderSoft);

        var left = new Rectangle(rect.X + 1, rect.Y + 1, 21, rect.Height - 2);
        var right = new Rectangle(rect.Right - 22, rect.Y + 1, 21, rect.Height - 2);
        EditorTheme.FillRect(spriteBatch, left, EditorTheme.Panel);
        EditorTheme.FillRect(spriteBatch, right, EditorTheme.Panel);
        EditorTheme.DrawBorder(spriteBatch, left, EditorTheme.Border);
        EditorTheme.DrawBorder(spriteBatch, right, EditorTheme.Border);
        EditorTheme.DrawText(spriteBatch, EditorTheme.Small, "-", new Vector2(left.X + 8, left.Y + 5), EditorTheme.TextDim);
        EditorTheme.DrawText(spriteBatch, EditorTheme.Small, "+", new Vector2(right.X + 7, right.Y + 5), EditorTheme.TextDim);

        var text = focused ? value + "|" : value;
        EditorTheme.DrawText(spriteBatch, EditorTheme.Small, Truncate(text, rect.Width - 54),
            new Vector2(rect.X + 28, rect.Y + 6), focused ? EditorTheme.Text : EditorTheme.TextDim);
    }

    private void DrawPicker(SpriteBatch spriteBatch)
    {
        if (string.IsNullOrEmpty(_openPickerField) || _pickerOptions.Count == 0)
            return;

        const int pickerRowH = 32;
        EditorTheme.DrawShadow(spriteBatch, _pickerRect, 5);
        EditorTheme.DrawPanel(spriteBatch, _pickerRect, EditorTheme.Panel, EditorTheme.AccentDim);

        var y = _pickerRect.Y + 2;
        foreach (var option in _pickerOptions.Skip(_pickerScroll))
        {
            if (y + pickerRowH > _pickerRect.Bottom - 2)
                break;

            var row = new Rectangle(_pickerRect.X + 2, y, _pickerRect.Width - 4, pickerRowH - 1);
            var selected = string.Equals(option.Value, GetField(_openPickerField), StringComparison.OrdinalIgnoreCase);
            EditorTheme.FillRect(spriteBatch, row, selected ? EditorTheme.PanelActive : EditorTheme.BgDeep);
            if (option.Accent is { } accent)
                EditorTheme.FillRect(spriteBatch, new Rectangle(row.X, row.Y, 3, row.Height), accent);

            EditorTheme.DrawText(spriteBatch, EditorTheme.Small, Truncate(option.Label, row.Width - 16),
                new Vector2(row.X + 8, row.Y + 5), selected ? EditorTheme.Text : EditorTheme.TextDim);
            if (!string.IsNullOrWhiteSpace(option.Hint))
                EditorTheme.DrawText(spriteBatch, EditorTheme.Tiny, Truncate(option.Hint, row.Width - 16),
                    new Vector2(row.X + 8, row.Y + 19), EditorTheme.TextMuted);

            y += pickerRowH;
        }
    }

    private void DrawColorPicker(SpriteBatch spriteBatch)
    {
        if (string.IsNullOrEmpty(_openColorField))
            return;

        BuildColorSwatches();
        EditorTheme.DrawShadow(spriteBatch, _colorPickerRect, 5);
        EditorTheme.DrawPanel(spriteBatch, _colorPickerRect, EditorTheme.Panel, EditorTheme.AccentDim);

        var current = AssetManager.ParseHexColor(GetField(_openColorField), Color.White);
        var currentRect = new Rectangle(_colorPickerRect.X + 10, _colorPickerRect.Y + 9, 24, 24);
        EditorTheme.FillRect(spriteBatch, currentRect, current);
        EditorTheme.DrawBorder(spriteBatch, currentRect, EditorTheme.TextMuted);
        EditorTheme.DrawText(spriteBatch, EditorTheme.Small, Truncate(GetField(_openColorField), _colorPickerRect.Width - 54),
            new Vector2(currentRect.Right + 8, currentRect.Y + 6), EditorTheme.Text);

        var naturalLabel = string.Equals(_openColorField, "skinColor", StringComparison.OrdinalIgnoreCase)
            ? "SKIN"
            : "NATURAL";
        EditorTheme.DrawText(spriteBatch, EditorTheme.Tiny, naturalLabel,
            new Vector2(_colorPickerRect.X + 10, _colorPickerRect.Y + 42), EditorTheme.Success);
        var presets = GetNaturalColorPresets();
        var presetRows = Math.Max(1, (presets.Length + 7) / 8);
        var paletteLabelY = _colorPickerRect.Y + 58 + presetRows * (18 + 5) + 8;
        EditorTheme.DrawText(spriteBatch, EditorTheme.Tiny, "PALETTE",
            new Vector2(_colorPickerRect.X + 10, paletteLabelY), EditorTheme.TextMuted);

        foreach (var swatch in _colorSwatches)
        {
            var selected = string.Equals(swatch.Hex, GetField(_openColorField), StringComparison.OrdinalIgnoreCase);
            EditorTheme.FillRect(spriteBatch, swatch.Rect, swatch.Color);
            EditorTheme.DrawBorder(spriteBatch, swatch.Rect, selected ? EditorTheme.Text : EditorTheme.Border);
            if (selected)
                EditorTheme.DrawBorder(spriteBatch, new Rectangle(swatch.Rect.X - 1, swatch.Rect.Y - 1, swatch.Rect.Width + 2, swatch.Rect.Height + 2), EditorTheme.Text);
        }
    }

    private void BuildColorSwatches()
    {
        _colorSwatches.Clear();
        if (_colorPickerRect == Rectangle.Empty)
            return;

        const int size = 18;
        const int gap = 5;
        var x0 = _colorPickerRect.X + 10;
        var y = _colorPickerRect.Y + 58;
        var presets = GetNaturalColorPresets();

        for (var i = 0; i < presets.Length; i++)
        {
            var row = i / 8;
            var col = i % 8;
            AddColorSwatch(new Rectangle(x0 + col * (size + gap), y + row * (size + gap), size, size),
                presets[i]);
        }

        var presetRows = Math.Max(1, (presets.Length + 7) / 8);
        y = _colorPickerRect.Y + 58 + presetRows * (size + gap) + 24;
        for (var row = 0; row < 3; row++)
        {
            for (var col = 0; col < 12; col++)
            {
                var hue = col / 12f;
                var sat = 0.35f + row * 0.25f;
                var val = 0.95f - row * 0.12f;
                var color = ColorFromHsv(hue, sat, val);
                AddColorSwatch(new Rectangle(x0 + col * (size + gap), y + row * (size + gap), size, size),
                    ToHex(color));
            }
        }
    }

    private void AddColorSwatch(Rectangle rect, string hex)
        => _colorSwatches.Add(new ColorSwatch(rect, NormalizeHexColor(hex), AssetManager.ParseHexColor(hex, Color.White)));

    private string[] GetNaturalColorPresets()
        => string.Equals(_openColorField, "skinColor", StringComparison.OrdinalIgnoreCase)
            ? NaturalSkinColorPresets
            : NaturalHairColorPresets;

    private void DrawNpcPreview(SpriteBatch spriteBatch)
    {
        if (_previewPanelRect == Rectangle.Empty)
            return;

        EditorTheme.DrawPanel(spriteBatch, _previewPanelRect, EditorTheme.BgDeep, EditorTheme.BorderSoft);
        EditorTheme.DrawText(spriteBatch, EditorTheme.Tiny, "APPEARANCE PREVIEW",
            new Vector2(_previewPanelRect.X + 9, _previewPanelRect.Y + 7), EditorTheme.Success);

        var clip = PreviewClips[Math.Clamp(_previewFacingIndex, 0, PreviewClips.Length - 1)];
        var label = clip.Replace("idle_", "", StringComparison.OrdinalIgnoreCase).ToUpperInvariant();
        EditorTheme.DrawText(spriteBatch, EditorTheme.Tiny, label,
            new Vector2(_previewPanelRect.Right - 48, _previewPanelRect.Y + 7), EditorTheme.TextMuted);

        var layers = BuildNpcPreviewLayers(clip);
        if (layers.Count == 0)
        {
            EditorTheme.DrawText(spriteBatch, EditorTheme.Small, "No sprite",
                new Vector2(_previewPanelRect.X + 16, _previewPanelRect.Y + 58), EditorTheme.TextMuted);
        }
        else
        {
            var baseRect = layers[0].SourceRect;
            var fitScale = (_previewPanelRect.Width - 64) / (float)Math.Max(1, baseRect.Width);
            var scale = Math.Clamp((int)MathF.Floor(fitScale), 2, 6);
            var center = new Vector2(_previewPanelRect.X + _previewPanelRect.Width / 2f, _previewPanelRect.Y + 104);
            var shadow = new Rectangle((int)(center.X - 34), (int)(center.Y + baseRect.Height * scale * 0.42f), 68, 8);
            EditorTheme.FillRect(spriteBatch, shadow, Color.Black * 0.28f);

            spriteBatch.End();
            spriteBatch.Begin(samplerState: SamplerState.PointClamp);
            foreach (var layer in layers)
            {
                spriteBatch.Draw(
                    layer.Texture,
                    center + layer.Offset * scale,
                    layer.SourceRect,
                    layer.Color,
                    0f,
                    layer.Origin,
                    scale,
                    SpriteEffects.None,
                    0f);
            }
            spriteBatch.End();
            spriteBatch.Begin();
        }

        DrawButton(spriteBatch, _previewLeftButtonRect, "<", false, false);
        DrawButton(spriteBatch, _previewRightButtonRect, ">", false, false);
        DrawButton(spriteBatch, _newHairButtonRect, "+ New", false, false);
        var canEdit = !string.IsNullOrWhiteSpace(GetField("hairStyleId"))
                      && !string.Equals(GetField("hairStyleId"), NoHairSentinel, StringComparison.Ordinal);
        DrawButton(spriteBatch, _editHairButtonRect, "Edit", false, false, canEdit);
    }

    private List<PreviewLayer> BuildNpcPreviewLayers(string clip)
    {
        var layers = new List<PreviewLayer>();
        var gender = GetDraftGender();
        var proto = _prototypes.GetEntity(FirstNonEmpty(GetField("proto"), "npc_base"));
        var baseLayer = BuildActorBasePreviewLayer(proto, gender, clip);
        if (baseLayer.HasValue)
            layers.Add(baseLayer.Value);

        foreach (var slot in new[] { "outfitTorso", "outfitPants", "outfitShoes", "outfitBack" })
        {
            var layer = BuildWearablePreviewLayer(GetField(slot), gender, clip);
            if (layer.HasValue)
                layers.Add(layer.Value);
        }

        var hairLayer = BuildHairPreviewLayer(gender, clip);
        if (hairLayer.HasValue)
            layers.Add(hairLayer.Value);

        return layers;
    }

    private PreviewLayer? BuildActorBasePreviewLayer(EntityPrototype? proto, Gender gender, string clip)
    {
        if (proto == null)
            return null;

        var spriteObj = GetPrototypeComponent(proto.Id, "sprite");
        var gendered = GetPrototypeComponent(proto.Id, "genderedAppearance");
        var source = gender == Gender.Female
            ? ReadJsonString(gendered, "femaleSprite")
            : ReadJsonString(gendered, "maleSprite");
        if (string.IsNullOrWhiteSpace(source))
            source = FirstNonEmpty(GetField("spriteSource"), ReadJsonString(spriteObj, "source"));

        var texture = LoadPrototypeTexture(proto, source);
        if (texture == null)
            return null;

        var rect = ResolveAnimationFrame(proto.AnimationsPath, clip)
                   ?? new Rectangle(
                       ReadIntField("spriteSrcX", ReadJsonInt(spriteObj, "srcX", 0)),
                       ReadIntField("spriteSrcY", ReadJsonInt(spriteObj, "srcY", 0)),
                       ReadIntField("spriteWidth", ReadJsonInt(spriteObj, "width", 32)),
                       ReadIntField("spriteHeight", ReadJsonInt(spriteObj, "height", 32)));
        var color = AssetManager.ParseHexColor(FirstNonEmpty(GetField("skinColor"), ReadJsonString(spriteObj, "color")), Color.White);
        return new PreviewLayer(texture, rect, color, new Vector2(rect.Width / 2f, rect.Height / 2f), Vector2.Zero);
    }

    private PreviewLayer? BuildHairPreviewLayer(Gender gender, string clip)
    {
        var styleId = GetField("hairStyleId");
        if (string.Equals(styleId, NoHairSentinel, StringComparison.Ordinal))
            return null;
        var styleProto = HairAppearanceComponent.FindStylePrototype(_prototypes, styleId, gender);
        var style = styleProto?.Components?["hairStyle"] as JsonObject;
        if (styleProto == null || style == null)
            return null;

        var animPath = ResolvePrototypeFile(styleProto, ReadJsonString(style, "animations"));
        var rect = ResolveAnimationFrame(animPath, clip)
                   ?? new Rectangle(ReadJsonInt(style, "srcX", 0), ReadJsonInt(style, "srcY", 0),
                       ReadJsonInt(style, "width", 32), ReadJsonInt(style, "height", 32));

        var textureSource = ResolveAnimationTexturePath(animPath) ?? ReadJsonString(style, "sprite");
        var texture = LoadPrototypeTexture(styleProto, textureSource);
        if (texture == null)
            return null;

        var color = AssetManager.ParseHexColor(GetField("hairColor"), new Color(76, 49, 31));
        var origin = new Vector2(
            ReadJsonFloat(style, "originX", rect.Width / 2f),
            ReadJsonFloat(style, "originY", rect.Height / 2f));
        var offset = new Vector2(ReadJsonFloat(style, "offsetX", 0f), ReadJsonFloat(style, "offsetY", 0f));
        return new PreviewLayer(texture, rect, color, origin, offset);
    }

    private PreviewLayer? BuildWearablePreviewLayer(string protoId, Gender gender, string clip)
    {
        if (string.IsNullOrWhiteSpace(protoId))
            return null;

        var proto = _prototypes.GetEntity(protoId);
        var wearable = proto?.Components?["wearable"] as JsonObject;
        if (proto == null || wearable == null)
            return null;

        var animSource = gender == Gender.Female
            ? FirstNonEmpty(ReadJsonString(wearable, "equippedAnimationsFemale"), ReadJsonString(wearable, "equippedAnimations"))
            : FirstNonEmpty(ReadJsonString(wearable, "equippedAnimationsMale"), ReadJsonString(wearable, "equippedAnimations"));
        var spriteSource = gender == Gender.Female
            ? FirstNonEmpty(ReadJsonString(wearable, "equippedSpriteFemale"), ReadJsonString(wearable, "equippedSprite"))
            : FirstNonEmpty(ReadJsonString(wearable, "equippedSpriteMale"), ReadJsonString(wearable, "equippedSprite"));

        var animPath = ResolvePrototypeFile(proto, animSource);
        var rect = ResolveAnimationFrame(animPath, clip)
                   ?? new Rectangle(ReadJsonInt(wearable, "equippedSrcX", 0), ReadJsonInt(wearable, "equippedSrcY", 0),
                       ReadJsonInt(wearable, "equippedWidth", 32), ReadJsonInt(wearable, "equippedHeight", 32));
        var texture = LoadPrototypeTexture(proto, ResolveAnimationTexturePath(animPath) ?? spriteSource);
        if (texture == null)
            return null;

        var spriteColor = ReadJsonString(proto.Components?["sprite"] as JsonObject, "color");
        var wearableColor = ReadJsonString(wearable, "color");
        var color = MultiplyColors(
            AssetManager.ParseHexColor(wearableColor, Color.White),
            AssetManager.ParseHexColor(spriteColor, Color.White));
        return new PreviewLayer(texture, rect, color, new Vector2(rect.Width / 2f, rect.Height / 2f), Vector2.Zero);
    }

    private void DrawZoneSummary(SpriteBatch spriteBatch)
    {
        var rect = new Rectangle(_detailRect.X + 12, _detailRect.Bottom - 88, _detailRect.Width - 24, 58);
        EditorTheme.DrawPanel(spriteBatch, rect, EditorTheme.Panel, EditorTheme.Border);

        var map = LoadSelectedMapData();
        var house = GetSelectedHouseArea(map);
        var district = house == null ? "" : GetAreaProperty(house, "districtId", "district", "district_id");
        var settlement = FirstNonEmpty(
            house != null ? GetAreaProperty(house, "settlementId", "settlement", "settlement_id", "cityId", "city") : "",
            map?.CityId,
            GetField("settlementId"));
        var faction = FirstNonEmpty(
            house != null ? GetAreaProperty(house, "factionId", "faction", "faction_id") : "",
            map?.FactionId,
            GetField("factionId"));

        var home = string.IsNullOrWhiteSpace(GetField("houseId")) ? "no home" : GetField("houseId");
        var bed = GetAutoBedDisplay();
        var work = string.IsNullOrWhiteSpace(GetField("professionSlotId")) ? "no work" : GetField("professionSlotId");

        EditorTheme.DrawText(spriteBatch, EditorTheme.Tiny, "DERIVED ZONES",
            new Vector2(rect.X + 8, rect.Y + 5), EditorTheme.Success);
        EditorTheme.DrawText(spriteBatch, EditorTheme.Small, Truncate($"Home: {home} / {bed}    Work: {work}", rect.Width - 16),
            new Vector2(rect.X + 8, rect.Y + 18), EditorTheme.TextDim);
        EditorTheme.DrawText(spriteBatch, EditorTheme.Tiny,
            Truncate($"Faction: {EmptyLabel(faction)}    Settlement: {EmptyLabel(settlement)}    District: {EmptyLabel(district)}", rect.Width - 16),
            new Vector2(rect.X + 8, rect.Y + 32), EditorTheme.TextMuted);
        var spawnStatus = GetSpawnStatusDisplay(map);
        EditorTheme.DrawText(spriteBatch, EditorTheme.Tiny,
            Truncate(spawnStatus.Text, rect.Width - 16),
            new Vector2(rect.X + 8, rect.Y + 46), spawnStatus.Ok ? EditorTheme.Success : EditorTheme.Warning);
    }

    private (bool Ok, string Text) GetSpawnStatusDisplay(MapData? map)
    {
        if (map == null)
            return (false, "Spawn: no, map file is missing");

        var protoId = GetField("proto");
        if (string.IsNullOrWhiteSpace(protoId) || _prototypes.GetEntity(protoId) == null)
            return (false, $"Spawn: no, missing proto '{protoId}'");

        var houseId = GetField("houseId").Trim();
        if (!string.IsNullOrWhiteSpace(houseId))
        {
            var house = GetSelectedHouseArea(map);
            if (house == null)
                return (false, $"Spawn: fallback map spawn, missing house '{houseId}'");

            var bed = GetAutoBedDisplay();
            return (true, $"Spawn: yes @ home {house.Id} / {bed}");
        }

        var innSlots = map.Areas
            .Where(area => string.Equals(area.Kind, AreaZoneKinds.Inn, StringComparison.OrdinalIgnoreCase))
            .SelectMany(area => GetInnBedPoints(area, map).Select(point => (Area: area, Point: point)))
            .OrderBy(pair => pair.Area.Id, StringComparer.OrdinalIgnoreCase)
            .ThenBy(pair => pair.Point.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (innSlots.Count == 0)
            return (true, "Spawn: yes @ map spawn (no inn beds in inns)");

        var doc = GetSelectedDocument();
        var npc = GetSelectedNpc();
        var used = doc == null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : doc.Entries
                .Where(entry => npc == null || !string.Equals(entry.Id, npc.Id, StringComparison.OrdinalIgnoreCase))
                .Where(entry => string.IsNullOrWhiteSpace(entry.Residence?.HouseId)
                    || map.Areas.Any(area =>
                        string.Equals(area.Kind, AreaZoneKinds.Inn, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(area.Id, entry.Residence?.HouseId, StringComparison.OrdinalIgnoreCase)))
                .Select((entry, index) =>
                {
                    if (!string.IsNullOrWhiteSpace(entry.Residence?.HouseId)
                        && !string.IsNullOrWhiteSpace(entry.Residence.BedSlotId))
                    {
                        return $"{entry.Residence.HouseId}:{entry.Residence.BedSlotId}";
                    }

                    var slot = innSlots.ElementAtOrDefault(index);
                    return slot.Area == null ? "" : $"{slot.Area.Id}:{slot.Point.Id}";
                })
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var free = innSlots.FirstOrDefault(pair => !used.Contains($"{pair.Area.Id}:{pair.Point.Id}"));
        return free.Area == null
            ? (false, "Spawn: yes @ map spawn, but all inn beds are reserved")
            : (true, $"Spawn: yes @ inn {free.Area.Id} / {free.Point.Id}");
    }

    private List<AreaPointData> GetInnBedPoints(AreaZoneData inn, MapData map)
    {
        return inn.GetPointsByPrefix("inn_bed_")
            .Concat(HouseBedScanner.EnumerateAutoBedPoints(inn, map, _prototypes))
            .GroupBy(point => point.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(point => point.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private string GetDisplayValue(string field)
    {
        var value = GetField(field);
        if (string.IsNullOrWhiteSpace(value))
        {
            return field switch
            {
                "factionId" or "settlementId" => "Derived from home/map",
                "houseId" or "professionSlotId" => "None",
                "spriteSource" => "Use template sprite",
                "hairStyleId" => "Inherited / first matching",
                "outfitTorso" or "outfitPants" or "outfitShoes" or "outfitBack" => "None",
                _ => ""
            };
        }

        if (field == "proto" && _prototypes.GetEntity(value) is { } proto)
            return string.IsNullOrWhiteSpace(proto.Name) ? proto.Id : $"{proto.Name}  ({proto.Id})";

        if (field == "factionId" && _worldData?.GetFaction(value) is { } faction)
            return string.IsNullOrWhiteSpace(faction.Name)
                ? faction.Id
                : $"{LocalizationManager.T(faction.Name)}  ({faction.Id})";

        if (field == "settlementId" && _worldData?.GetCity(value) is { } city)
            return string.IsNullOrWhiteSpace(city.Name) ? city.Id : $"{city.Name}  ({city.Id})";

        if (field == "hairStyleId" && string.Equals(value, NoHairSentinel, StringComparison.Ordinal))
            return "None — bald";

        if (field == "hairStyleId" && _prototypes.GetEntity(value) is { } hair)
            return HairStyleDisplayName(hair);

        if (field == "houseId")
        {
            var area = LoadSelectedMapData()?.Areas.FirstOrDefault(area =>
                string.Equals(area.Kind, AreaZoneKinds.House, StringComparison.OrdinalIgnoreCase)
                && string.Equals(area.Id, value, StringComparison.OrdinalIgnoreCase));
            if (area != null)
                return AreaDisplayName(area);
        }

        if (field == "professionSlotId")
        {
            var area = LoadSelectedMapData()?.Areas.FirstOrDefault(area =>
                IsProfessionSlotArea(area)
                && string.Equals(area.Id, value, StringComparison.OrdinalIgnoreCase));
            if (area != null)
                return string.Equals(area.Kind, AreaZoneKinds.Tavern, StringComparison.OrdinalIgnoreCase)
                    ? $"Tavern: {AreaDisplayName(area)}"
                    : AreaDisplayName(area);
        }

        if (field.StartsWith("outfit", StringComparison.OrdinalIgnoreCase) && _prototypes.GetEntity(value) is { } item)
            return string.IsNullOrWhiteSpace(item.Name) ? item.Id : $"{item.Name}  ({item.Id})";

        return value;
    }

    private static bool IsPickerField(string field)
        => field is "proto" or "factionId" or "settlementId" or "houseId"
            or "professionSlotId" or "scheduleTemplateId" or "spriteSource"
            or "hairStyleId"
            or "outfitTorso" or "outfitPants" or "outfitShoes" or "outfitBack"
            or "hands" or "inventory";

    private static bool IsColorPickerField(string field)
        => field is "hairColor" or "skinColor";

    private static bool IsNumericField(string field)
        => field is "ageYears" or "infidelity" or "vengefulness" or "childWish" or "marriageWish" or "sociability"
            or "spriteSrcX" or "spriteSrcY" or "spriteWidth" or "spriteHeight"
            || IsSkillField(field);

    private void AdjustNumericField(string field, int delta)
    {
        if (IsSkillField(field))
        {
            var skill = ReadFloatField(field, 0f) + delta;
            _fields[field] = FormatSkillValue(Math.Clamp(skill, 0f, 10f));
            return;
        }

        var value = ReadIntField(field, field == "ageYears" ? 25 : 0) + delta;
        if (field == "ageYears")
            value = Math.Clamp(value, 0, 130);
        else if (field is "spriteWidth" or "spriteHeight")
            value = Math.Clamp(value, 1, 512);
        else if (field is "infidelity" or "vengefulness" or "childWish" or "marriageWish" or "sociability")
            value = Math.Clamp(value, -1, 100);
        else
            value = Math.Max(0, value);

        _fields[field] = value.ToString();
    }

    private static string EmptyLabel(string value)
        => string.IsNullOrWhiteSpace(value) ? "auto" : value;

    private static void DrawButton(SpriteBatch spriteBatch, Rectangle rect, string label, bool hovered, bool active, bool enabled = true)
        => EditorTheme.DrawButton(spriteBatch, rect, label, EditorTheme.Small, hovered, active, enabled);

    private static string Truncate(string value, int maxWidth)
    {
        if (EditorTheme.Small.MeasureString(value).X <= maxWidth)
            return value;
        while (value.Length > 1 && EditorTheme.Small.MeasureString(value + "...").X > maxWidth)
            value = value[..^1];
        return value + "...";
    }

    private static bool IsPressed(KeyboardState cur, KeyboardState prev, Keys key)
        => cur.IsKeyDown(key) && prev.IsKeyUp(key);

    private static int ReadJsonInt(JsonObject? obj, string key, int fallback)
        => obj != null && obj[key] != null && int.TryParse(obj[key]!.ToString(), out var value) ? value : fallback;

    private int ReadIntField(string key, int fallback)
        => int.TryParse(GetField(key), out var value) ? value : fallback;

    private float ReadFloatField(string key, float fallback)
    {
        var raw = GetField(key);
        if (float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var invariant))
            return invariant;
        return float.TryParse(raw, out var local) ? local : fallback;
    }

    private bool ReadBoolField(string key)
        => bool.TryParse(GetField(key), out var value) && value;

    private static string? EmptyToNull(string value)
        => string.IsNullOrWhiteSpace(value) ? null : value;

    private static List<string> ParseCsv(string value)
        => value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();

    private static string AppendCsvValue(string csv, string value)
    {
        var items = ParseCsv(csv);
        if (items.Any(item => string.Equals(item, value, StringComparison.OrdinalIgnoreCase)))
            return string.Join(", ", items);

        items.Add(value.Trim());
        return string.Join(", ", items);
    }

    private void LoadSkillFields(Dictionary<string, float> skills)
    {
        foreach (var skillId in GetSkillIdsForEditor(skills))
            SetField(SkillFieldKey(skillId), FormatSkillValue(skills.GetValueOrDefault(skillId, 0f)));
    }

    private Dictionary<string, float> ReadSkillFields()
    {
        var result = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in _fields.Keys.Where(IsSkillField).ToList())
        {
            var skillId = SkillIdFromFieldKey(key);
            if (string.IsNullOrWhiteSpace(skillId))
                continue;

            var skill = Math.Clamp(ReadFloatField(key, 0f), 0f, 10f);
            if (skill > 0f)
                result[skillId] = skill;
        }
        return result;
    }

    private IEnumerable<string> GetSkillIdsForEditor(Dictionary<string, float>? source = null)
    {
        var ids = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in Enum.GetNames<SkillType>())
            ids.Add(name);

        if (source != null)
            foreach (var id in source.Keys)
                if (!string.IsNullOrWhiteSpace(id))
                    ids.Add(id.Trim());

        foreach (var key in _fields.Keys.Where(IsSkillField))
        {
            var id = SkillIdFromFieldKey(key);
            if (!string.IsNullOrWhiteSpace(id))
                ids.Add(id);
        }

        return ids;
    }

    private static bool IsSkillField(string field)
        => field.StartsWith(SkillFieldPrefix, StringComparison.OrdinalIgnoreCase);

    private static string SkillFieldKey(string skillId)
        => $"{SkillFieldPrefix}{skillId}";

    private static string SkillIdFromFieldKey(string field)
        => IsSkillField(field) ? field[SkillFieldPrefix.Length..] : "";

    private static string FormatSkillValue(float value)
        => Math.Clamp(value, 0f, 10f).ToString("0.##", CultureInfo.InvariantCulture);

    private static string FormatSkillLabel(string skillId)
    {
        var result = "";
        for (var i = 0; i < skillId.Length; i++)
        {
            var ch = skillId[i];
            if (i > 0 && char.IsUpper(ch) && !char.IsUpper(skillId[i - 1]))
                result += " ";
            result += ch;
        }
        return result;
    }

    private static void AddOutfit(NpcRosterEntry npc, string slot, string protoId)
    {
        if (!string.IsNullOrWhiteSpace(protoId))
            npc.Outfit[slot] = protoId.Trim();
    }

    private static bool IsNpcPrototype(EntityPrototype proto)
    {
        if (proto.Components?["npc"] != null || proto.Components?["identity"] != null)
            return true;

        return proto.Id.Contains("npc", StringComparison.OrdinalIgnoreCase)
               || proto.Name.Contains("npc", StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadJsonString(JsonObject? obj, string key)
    {
        if (obj == null || obj[key] == null)
            return "";

        try
        {
            return obj[key]!.GetValue<string>() ?? "";
        }
        catch
        {
            return obj[key]!.ToString();
        }
    }

    private string GenerateUniqueNpcId(RosterDocument doc)
    {
        for (var i = 1; i < 10000; i++)
        {
            var id = $"npc_{i:000}";
            if (doc.Entries.All(e => !string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase)))
                return id;
        }
        return Guid.NewGuid().ToString("N");
    }

    private static string MakeSafeId(string value)
    {
        var chars = value.Trim().Select(ch =>
            char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '_').ToArray();
        var id = new string(chars).Trim('_');
        while (id.Contains("__", StringComparison.Ordinal))
            id = id.Replace("__", "_", StringComparison.Ordinal);
        return id;
    }

    private readonly record struct FieldDef(string Key, string Label, bool Right = false, bool Wide = false);
}
