#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MTEngine.Components;
using MTEngine.Core;
using MTEngine.ECS;
using MTEngine.Npc;
using MTEngine.Rendering;
using MTEngine.World;

namespace SandboxGame.Systems;

public class GodModeSystem : GameSystem, IGodModeService
{
    private MapManager? _mapManager;
    private Action<string, string, bool>? _loadMap;
    private SpriteFont? _font;
    private InputManager? _input;
    private Camera? _camera;
    private SpriteBatch? _spriteBatch;
    private GraphicsDevice? _graphics;
    private Texture2D? _pixel;

    private bool _active;
    private string? _returnMapId;
    private Vector2? _returnPlayerPosition;
    private Vector2? _returnCameraPosition;
    private int _mapScroll;
    private int? _selectedNpcEntityId;

    private const int PanelMargin = 16;
    private const int MapPanelWidth = 330;
    private const int RowHeight = 24;
    private const int PanelTop = 72;

    public override DrawLayer DrawLayer => DrawLayer.Overlay;
    public bool IsGodModeActive => _active;

    public void Configure(MapManager mapManager, Action<string, string, bool> loadMap, SpriteFont font)
    {
        _mapManager = mapManager;
        _loadMap = loadMap;
        _font = font;
    }

    public override void OnInitialize()
    {
        _input = ServiceLocator.Get<InputManager>();
        _camera = ServiceLocator.Get<Camera>();
        _spriteBatch = ServiceLocator.Get<SpriteBatch>();
        _graphics = ServiceLocator.Get<GraphicsDevice>();
    }

    public void Toggle()
        => SetActive(!_active);

    public void SetActive(bool active)
    {
        if (_active == active)
            return;

        if (active)
        {
            _returnMapId = _mapManager?.CurrentMap?.Id;
            _returnCameraPosition = _camera?.Position;
            _returnPlayerPosition = GetPlayer()?.GetComponent<TransformComponent>()?.Position;
            _selectedNpcEntityId = null;
            _active = true;
            DevConsole.Log("God mode: ON. Player control suspended.");
            return;
        }

        _active = false;
        _selectedNpcEntityId = null;
        RestorePlayerMapAndCamera();
        DevConsole.Log("God mode: OFF. Player control restored.");
    }

    public override void Update(float deltaTime)
    {
        if (!_active || _input == null || _camera == null || _mapManager == null)
            return;

        if (DevConsole.IsOpen)
            return;

        HandleCamera(deltaTime);
        HandleMapPanelInput();
        HandleNpcInspectInput();
    }

    public override void Draw()
    {
        if (!_active || _font == null || _spriteBatch == null || _graphics == null)
            return;

        EnsurePixel();
        _spriteBatch.Begin(samplerState: SamplerState.PointClamp);
        DrawMapPanel();
        DrawNpcPanel();
        DrawFooterHint();
        _spriteBatch.End();
    }

    private void HandleCamera(float deltaTime)
    {
        var dir = Vector2.Zero;
        if (_input!.IsDown(Keys.A) || _input.IsDown(Keys.Left)) dir.X -= 1f;
        if (_input.IsDown(Keys.D) || _input.IsDown(Keys.Right)) dir.X += 1f;
        if (_input.IsDown(Keys.W) || _input.IsDown(Keys.Up)) dir.Y -= 1f;
        if (_input.IsDown(Keys.S) || _input.IsDown(Keys.Down)) dir.Y += 1f;

        if (dir != Vector2.Zero)
        {
            dir.Normalize();
            var fast = _input.IsDown(Keys.LeftShift) || _input.IsDown(Keys.RightShift);
            var speed = (fast ? 1500f : 600f) / Math.Max(0.35f, _camera!.Zoom);
            _camera.Position += dir * speed * deltaTime;
        }

        var mouse = _input.MousePosition;
        if (_input.ScrollDelta != 0 && !GetMapPanelRect().Contains(mouse))
        {
            var delta = _input.ScrollDelta > 0 ? 0.15f : -0.15f;
            _camera!.Zoom = Math.Clamp(_camera.Zoom + delta, 0.5f, 6f);
        }
    }

    private void HandleMapPanelInput()
    {
        var mouse = _input!.MousePosition;
        var panel = GetMapPanelRect();
        if (!panel.Contains(mouse))
            return;

        var maps = GetMaps();
        var visibleRows = GetVisibleMapRows();
        var maxScroll = Math.Max(0, maps.Count - visibleRows);
        if (_input.ScrollDelta != 0)
            _mapScroll = Math.Clamp(_mapScroll - Math.Sign(_input.ScrollDelta) * 3, 0, maxScroll);

        if (!_input.LeftClicked)
            return;

        var rowIndex = (mouse.Y - panel.Y - 48) / RowHeight;
        if (rowIndex < 0 || rowIndex >= visibleRows)
            return;

        var mapIndex = _mapScroll + rowIndex;
        if (mapIndex < 0 || mapIndex >= maps.Count)
            return;

        LoadObserverMap(maps[mapIndex].Id);
    }

    private void HandleNpcInspectInput()
    {
        if (!_input!.RightClicked || _camera == null)
            return;

        var worldPos = _camera.ScreenToWorld(_input.ViewportMousePosition.ToVector2());
        var npc = FindNpcAt(worldPos);
        _selectedNpcEntityId = npc?.Id;
    }

    private void LoadObserverMap(string mapId)
    {
        if (_loadMap == null || _mapManager == null || string.IsNullOrWhiteSpace(mapId))
            return;

        if (string.Equals(_mapManager.CurrentMap?.Id, mapId, StringComparison.OrdinalIgnoreCase))
        {
            _selectedNpcEntityId = null;
            CenterCameraOnLoadedMap();
            return;
        }

        _loadMap(mapId, "default", false);
        _selectedNpcEntityId = null;

        CenterCameraOnLoadedMap();
    }

    private void CenterCameraOnLoadedMap()
    {
        var map = _mapManager?.CurrentMap;
        if (map == null || _camera == null)
            return;

        var spawn = map.SpawnPoints.FirstOrDefault(s => string.Equals(s.Id, "default", StringComparison.OrdinalIgnoreCase))
                    ?? map.SpawnPoints.FirstOrDefault();
        _camera.Position = spawn != null
            ? new Vector2((spawn.X + 0.5f) * map.TileSize, (spawn.Y + 0.5f) * map.TileSize)
            : new Vector2(map.Width * map.TileSize * 0.5f, map.Height * map.TileSize * 0.5f);
    }

    private void RestorePlayerMapAndCamera()
    {
        if (!string.IsNullOrWhiteSpace(_returnMapId) && _mapManager?.CurrentMap?.Id != _returnMapId)
            _loadMap?.Invoke(_returnMapId!, "default", false);

        var playerTransform = GetPlayer()?.GetComponent<TransformComponent>();
        if (playerTransform != null && _returnPlayerPosition.HasValue)
            playerTransform.Position = _returnPlayerPosition.Value;

        if (_camera != null)
            _camera.Position = playerTransform?.Position ?? _returnCameraPosition ?? _camera.Position;
    }

    private List<MapCatalogEntry> GetMaps()
        => _mapManager?.GetMapCatalog() ?? new List<MapCatalogEntry>();

    private Entity? GetPlayer()
        => World.GetEntitiesWith<PlayerTagComponent>().FirstOrDefault();

    private Entity? GetSelectedNpc()
    {
        if (!_selectedNpcEntityId.HasValue)
            return null;

        return World.GetEntities()
            .FirstOrDefault(entity => entity.Active
                                      && entity.Id == _selectedNpcEntityId.Value
                                      && entity.HasComponent<NpcTagComponent>());
    }

    private Entity? FindNpcAt(Vector2 worldPos)
    {
        Entity? best = null;
        var bestSortY = float.MinValue;

        foreach (var entity in World.GetEntitiesWith<NpcTagComponent, TransformComponent>())
        {
            if (!TryGetEntityBounds(entity, out var bounds) || !bounds.Contains(worldPos))
                continue;

            var sortY = entity.GetComponent<TransformComponent>()!.Position.Y;
            if (best == null || sortY > bestSortY)
            {
                best = entity;
                bestSortY = sortY;
            }
        }

        return best;
    }

    private static bool TryGetEntityBounds(Entity entity, out RectangleF bounds)
    {
        bounds = default;
        var transform = entity.GetComponent<TransformComponent>();
        if (transform == null)
            return false;

        var collider = entity.GetComponent<ColliderComponent>();
        if (collider != null)
        {
            var rect = collider.GetBounds(transform.Position);
            bounds = new RectangleF(rect.X, rect.Y, rect.Width, rect.Height);
            return true;
        }

        var sprite = entity.GetComponent<SpriteComponent>();
        if (sprite != null)
        {
            var source = sprite.SourceRect ?? new Rectangle(0, 0, sprite.Width, sprite.Height);
            var width = Math.Max(1f, source.Width * Math.Abs(transform.Scale.X));
            var height = Math.Max(1f, source.Height * Math.Abs(transform.Scale.Y));
            bounds = new RectangleF(
                transform.Position.X - sprite.Origin.X * transform.Scale.X,
                transform.Position.Y - sprite.Origin.Y * transform.Scale.Y,
                width,
                height);
            return true;
        }

        bounds = new RectangleF(transform.Position.X - 16f, transform.Position.Y - 16f, 32f, 32f);
        return true;
    }

    private void DrawMapPanel()
    {
        var panel = GetMapPanelRect();
        DrawRect(panel, Color.Black * 0.72f);
        DrawBorder(panel, Color.DeepSkyBlue * 0.8f);

        DrawText("GOD MODE", panel.X + 12, panel.Y + 8, Color.DeepSkyBlue, 1.05f);
        DrawText("Maps: left click to observe", panel.X + 12, panel.Y + 28, Color.LightGray, 0.82f);

        var maps = GetMaps();
        var visibleRows = GetVisibleMapRows();
        var maxScroll = Math.Max(0, maps.Count - visibleRows);
        _mapScroll = Math.Clamp(_mapScroll, 0, maxScroll);

        var y = panel.Y + 48;
        for (var i = 0; i < visibleRows; i++)
        {
            var index = _mapScroll + i;
            if (index >= maps.Count)
                break;

            var entry = maps[index];
            var row = new Rectangle(panel.X + 8, y, panel.Width - 16, RowHeight - 2);
            var current = string.Equals(entry.Id, _mapManager?.CurrentMap?.Id, StringComparison.OrdinalIgnoreCase);
            var color = current ? Color.DarkSlateBlue * 0.85f : Color.Black * 0.35f;
            DrawRect(row, color);

            var marker = entry.InGame ? "*" : "-";
            var label = $"{marker} {entry.Name} [{entry.Id}]";
            DrawText(TrimToWidth(label, row.Width - 10, 0.78f), row.X + 5, row.Y + 3,
                current ? Color.White : Color.LightGray, 0.78f);
            y += RowHeight;
        }
    }

    private void DrawNpcPanel()
    {
        var npc = GetSelectedNpc();
        if (npc == null)
            return;

        var panel = GetNpcPanelRect();
        DrawRect(panel, Color.Black * 0.74f);
        DrawBorder(panel, Color.Gold * 0.85f);

        var lines = BuildNpcDebugLines(npc);
        var y = panel.Y + 10;
        foreach (var line in lines)
        {
            if (y > panel.Bottom - 20)
                break;

            var color = line.StartsWith("#", StringComparison.Ordinal) ? Color.Gold : Color.White;
            var scale = line.StartsWith("#", StringComparison.Ordinal) ? 0.9f : 0.74f;
            var text = line.StartsWith("#", StringComparison.Ordinal) ? line[1..] : line;
            DrawText(TrimToWidth(text, panel.Width - 20, scale), panel.X + 10, y, color, scale);
            y += line.StartsWith("#", StringComparison.Ordinal) ? 20 : 16;
        }
    }

    private void DrawFooterHint()
    {
        if (_graphics == null)
            return;

        var text = "WASD/Arrows: camera  |  Wheel: zoom/list  |  RMB NPC: debug  |  console: god off";
        var size = _font!.MeasureString(text) * 0.75f;
        var x = MathF.Max(PanelMargin, (_graphics.Viewport.Width - size.X) * 0.5f);
        var y = _graphics.Viewport.Height - 28f;
        DrawText(text, x + 1f, y + 1f, Color.Black * 0.8f, 0.75f);
        DrawText(text, x, y, Color.LightSkyBlue, 0.75f);
    }

    private List<string> BuildNpcDebugLines(Entity npc)
    {
        var lines = new List<string>();
        var identity = npc.GetComponent<IdentityComponent>();
        var save = npc.GetComponent<SaveEntityIdComponent>();
        var transform = npc.GetComponent<TransformComponent>();
        var health = npc.GetComponent<HealthComponent>();
        var age = npc.GetComponent<AgeComponent>();
        var residence = npc.GetComponent<ResidenceComponent>();
        var profession = npc.GetComponent<ProfessionComponent>();
        var schedule = npc.GetComponent<ScheduleComponent>();
        var intent = npc.GetComponent<NpcIntentComponent>();
        var rel = npc.GetComponent<RelationshipsComponent>();
        var personality = npc.GetComponent<PersonalityComponent>();
        var aggression = npc.GetComponent<NpcAggressionComponent>();
        var avenger = npc.GetComponent<AvengerComponent>();
        var revenge = npc.GetComponent<RevengeTriggerComponent>();
        var kin = npc.GetComponent<KinComponent>();
        var skills = npc.GetComponent<SkillsComponent>();

        lines.Add($"#NPC DEBUG: {identity?.FullName ?? npc.Name}");
        lines.Add($"EntityId: {npc.Id}  SaveId: {save?.SaveId ?? "-"}");
        if (transform != null)
            lines.Add($"Pos: {transform.Position.X:0.#}, {transform.Position.Y:0.#}");
        if (health != null)
            lines.Add($"HP: {health.Health:0.#}/{health.MaxHealth:0.#}  Dead: {health.IsDead}");
        if (age != null)
            lines.Add($"Age: {age.Years}  Pensioner: {age.IsPensioner}");
        if (identity != null)
            lines.Add($"Gender: {identity.Gender}  Faction: {identity.FactionId}  Settlement: {identity.SettlementId}  District: {identity.DistrictId}");
        if (residence != null)
            lines.Add($"Residence: house={residence.HouseId} bed={residence.BedSlotId}");
        if (profession != null)
            lines.Add($"Profession: {profession.ProfessionId} slot={profession.SlotId} joined={profession.JoinedDayIndex}");

        lines.Add("#Schedule / Intent");
        if (schedule != null)
        {
            var clock = ServiceLocator.Has<GameClock>() ? ServiceLocator.Get<GameClock>() : null;
            var slot = clock != null ? schedule.FindSlot(clock.HourInt) : null;
            lines.Add($"Template: {schedule.TemplateId}  Slots: {schedule.Slots.Count}  Freetime: {schedule.Freetime.Count}");
            lines.Add(slot != null
                ? $"Current slot: {slot.StartHour:00}-{slot.EndHour:00} {slot.Action} target={slot.TargetAreaId}"
                : "Current slot: none");
        }
        if (intent != null)
        {
            lines.Add($"Intent: {intent.Action} has={intent.HasTarget} arrived={intent.Arrived}");
            lines.Add($"Target: map={intent.TargetMapId} area={intent.TargetAreaId} point={intent.TargetPointId}");
            lines.Add($"TargetPos: {intent.TargetX:0.#}, {intent.TargetY:0.#}");
        }

        lines.Add("#Relationships");
        if (rel != null)
        {
            lines.Add($"Status: {rel.Status}  PartnerNpc={rel.PartnerNpcSaveId} player={rel.PartnerIsPlayer}");
            lines.Add($"DateDay: {FormatDay(rel.ScheduledDateDayIndex)}  DatingStarted: {FormatDay(rel.DatingStartedDayIndex)}");
            lines.Add($"WeddingDay: {FormatDay(rel.ScheduledWeddingDayIndex)}  MarriageDay: {FormatDay(rel.MarriageDayIndex)}");
            lines.Add($"BirthDay: {FormatDay(rel.ScheduledBirthDayIndex)}  Opinion: {rel.PlayerOpinion}  Overnight: {rel.OvernightStreak}");
        }
        if (kin != null)
            lines.Add($"Kin: {string.Join(", ", kin.Links.Select(k => $"{k.Kind}:{k.NpcSaveId}"))}");

        lines.Add("#Mind / Combat");
        if (personality != null)
            lines.Add($"Infidelity {personality.Infidelity}  Venge {personality.Vengefulness}  Child {personality.ChildWish}  Marriage {personality.MarriageWish}  Social {personality.Sociability}  Pacifist {personality.Pacifist}");
        if (aggression != null)
            lines.Add($"Aggro: {aggression.Mode} reason={aggression.Reason} house={aggression.ProtectedHouseId} provoked={aggression.ProvokedByTarget}");
        if (avenger != null)
            lines.Add($"Avenger: target={avenger.TargetSaveId} victim={avenger.VictimSaveId} started={FormatDay(avenger.StartedDayIndex)} last={avenger.LastKnownMapId}@{avenger.LastKnownX:0.#},{avenger.LastKnownY:0.#}");
        if (revenge != null && revenge.Triggers.Count > 0)
        {
            lines.Add("#Revenge");
            foreach (var trigger in revenge.Triggers.OrderBy(t => t.TriggerAfterDayIndex))
                lines.Add($"{trigger.Behavior} ready={trigger.Ready} after={FormatDay(trigger.TriggerAfterDayIndex)} victim={trigger.VictimSaveId} cause={trigger.CauseKin}");
        }
        if (skills?.Best() is { } best)
            lines.Add($"Best skill: {best.Id} {best.Value:0.#}");

        lines.Add("#Components");
        lines.Add(string.Join(", ", npc.GetAllComponents().Select(c => c.GetType().Name).OrderBy(n => n)));
        return lines;
    }

    private static string FormatDay(long dayIndex)
    {
        if (dayIndex < 0)
            return "-";

        if (!ServiceLocator.Has<Calendar>())
            return dayIndex.ToString();

        var calendar = ServiceLocator.Get<Calendar>();
        var date = calendar.FromDayIndex(dayIndex);
        var month = date.Month >= 1 && date.Month <= calendar.MonthNames.Count
            ? calendar.MonthNames[date.Month - 1]
            : date.Month.ToString();
        return $"{date.Day} {month} {date.Year}";
    }

    private Rectangle GetMapPanelRect()
    {
        var height = _graphics == null
            ? 520
            : Math.Max(220, _graphics.Viewport.Height - PanelTop - 58);
        return new Rectangle(PanelMargin, PanelTop, MapPanelWidth, height);
    }

    private Rectangle GetNpcPanelRect()
    {
        var viewport = _graphics!.Viewport;
        var width = Math.Min(470, Math.Max(360, viewport.Width - MapPanelWidth - PanelMargin * 4));
        return new Rectangle(viewport.Width - width - PanelMargin, PanelTop, width, Math.Max(260, viewport.Height - PanelTop - 58));
    }

    private int GetVisibleMapRows()
        => Math.Max(1, (GetMapPanelRect().Height - 54) / RowHeight);

    private void EnsurePixel()
    {
        if (_pixel != null || _graphics == null)
            return;

        _pixel = new Texture2D(_graphics, 1, 1);
        _pixel.SetData(new[] { Color.White });
    }

    private void DrawRect(Rectangle rect, Color color)
        => _spriteBatch!.Draw(_pixel!, rect, color);

    private void DrawBorder(Rectangle rect, Color color)
    {
        DrawRect(new Rectangle(rect.X, rect.Y, rect.Width, 1), color);
        DrawRect(new Rectangle(rect.X, rect.Bottom - 1, rect.Width, 1), color);
        DrawRect(new Rectangle(rect.X, rect.Y, 1, rect.Height), color);
        DrawRect(new Rectangle(rect.Right - 1, rect.Y, 1, rect.Height), color);
    }

    private void DrawText(string text, float x, float y, Color color, float scale = 1f)
        => _spriteBatch!.DrawString(_font!, text, new Vector2(x, y), color, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);

    private string TrimToWidth(string text, float maxWidth, float scale)
    {
        if (_font == null || string.IsNullOrEmpty(text) || _font.MeasureString(text).X * scale <= maxWidth)
            return text;

        const string ellipsis = "...";
        while (text.Length > 0 && _font.MeasureString(text + ellipsis).X * scale > maxWidth)
            text = text[..^1];
        return text + ellipsis;
    }

    private readonly struct RectangleF
    {
        public float X { get; }
        public float Y { get; }
        public float Width { get; }
        public float Height { get; }

        public RectangleF(float x, float y, float width, float height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        public bool Contains(Vector2 point)
            => point.X >= X && point.X <= X + Width && point.Y >= Y && point.Y <= Y + Height;
    }
}
