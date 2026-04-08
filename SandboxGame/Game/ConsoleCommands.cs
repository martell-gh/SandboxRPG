using System;
using System.Linq;
using System.Reflection;
using System.Globalization;
using System.Collections;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using MTEngine.Components;
using MTEngine.Core;
using MTEngine.ECS;
using MTEngine.Metabolism;
using MTEngine.Systems;
using MTEngine.World;
using MTEngine.Wounds;

namespace SandboxGame.Game;

public class ConsoleCommands
{
    private readonly GameEngine _engine;
    private readonly MapManager _mapManager;
    private readonly TileMapRenderer _tileMapRenderer;

    public ConsoleCommands(GameEngine engine, MapManager mapManager, TileMapRenderer tileMapRenderer)
    {
        _engine = engine;
        _mapManager = mapManager;
        _tileMapRenderer = tileMapRenderer;
        DevConsole.OnCommand = HandleCommand;
    }

    private void HandleCommand(string input)
    {
        var parts = input.Trim().Split(' ');
        switch (parts[0].ToLower())
        {
            case "help":
                DevConsole.Log("Commands:");
                DevConsole.Log("  maps                   - list maps");
                DevConsole.Log("  loadmap <id> [spawnId] - load map");
                DevConsole.Log("  ingamemaps             - list maps marked for world simulation");
                DevConsole.Log("  ingamemap <id> <on|off|toggle> - change inGame flag on a map");
                DevConsole.Log("  goto <spawnId>         - teleport");
                DevConsole.Log("  time                   - show game time");
                DevConsole.Log("  settime <0-24>         - set time of day");
                DevConsole.Log("  timescale <x>          - speed of time");
                DevConsole.Log("  lighting <on|off>      - toggle lighting");
                DevConsole.Log("  spawn <protoId>        - spawn entity near player");
                DevConsole.Log("  heal [value]           - heal player hp or fully heal");
                DevConsole.Log("  hurt <value>           - damage player hp");
                DevConsole.Log("  hunger [value]         - get/set hunger (0-100)");
                DevConsole.Log("  thirst [value]         - get/set thirst (0-100)");
                DevConsole.Log("  bladder [value]        - get/set bladder (0-100)");
                DevConsole.Log("  bowel [value]          - get/set bowel (0-100)");
                DevConsole.Log("  feed                   - max hunger+thirst");
                DevConsole.Log("  starve                 - zero hunger+thirst");
                DevConsole.Log("  relieve                - empty bladder+bowel");
                DevConsole.Log("  metab                  - show all metabolism");
                DevConsole.Log("  clear                  - clear console");
                break;
            case "comps":
                var player = _engine.World
                    .GetEntitiesWith<TransformComponent, PlayerTagComponent>()
                    .FirstOrDefault();

                if (player == null)
                {
                    DevConsole.Log("Player not found.");
                    break;
                }

                foreach (var comp in player.GetAllComponents())
                {
                    DevConsole.Log($"  - {comp.GetType().Name}");

                    foreach (var property in GetDisplayProperties(comp.GetType()))
                    {
                        object value;

                        try
                        {
                            value = property.GetValue(comp);
                        }
                        catch
                        {
                            continue;
                        }

                        DevConsole.Log($"      {property.Name}: {FormatValue(value)}");
                    }
                }
                break;
            case "maps":
                var available = _mapManager.GetAvailableMaps();
                if (available.Count == 0) DevConsole.Log("No maps found.");
                else foreach (var m in available) DevConsole.Log($"  - {m}");
                break;

            case "loadmap":
                if (parts.Length < 2) { DevConsole.Log("Usage: loadmap <id> [spawnId]"); break; }
                LoadMap(parts[1], parts.Length > 2 ? parts[2] : "default");
                break;

            case "ingamemaps":
                var catalog = _mapManager.GetMapCatalog();
                if (catalog.Count == 0)
                {
                    DevConsole.Log("No maps found.");
                    break;
                }

                foreach (var entry in catalog)
                    DevConsole.Log($"  - [{(entry.InGame ? "ON " : "OFF")}] {entry.Id} ({entry.Name})");
                break;

            case "ingamemap":
                if (parts.Length < 3)
                {
                    DevConsole.Log("Usage: ingamemap <id> <on|off|toggle>");
                    break;
                }

                var targetMapId = parts[1];
                var catalogEntry = _mapManager.GetMapCatalog()
                    .FirstOrDefault(entry => string.Equals(entry.Id, targetMapId, StringComparison.OrdinalIgnoreCase));
                if (catalogEntry == null)
                {
                    DevConsole.Log($"Map not found: {targetMapId}");
                    break;
                }

                var mode = parts[2].ToLowerInvariant();
                var nextValue = mode switch
                {
                    "on" => true,
                    "off" => false,
                    "toggle" => !catalogEntry.InGame,
                    _ => catalogEntry.InGame
                };

                if (mode is not ("on" or "off" or "toggle"))
                {
                    DevConsole.Log("Usage: ingamemap <id> <on|off|toggle>");
                    break;
                }

                if (_mapManager.SetMapInGameFlag(catalogEntry.Id, nextValue))
                    DevConsole.Log($"Map '{catalogEntry.Id}' inGame = {nextValue}");
                else
                    DevConsole.Log($"Failed to update map '{catalogEntry.Id}'.");
                break;

            case "goto":
                if (parts.Length < 2) { DevConsole.Log("Usage: goto <spawnId>"); break; }
                TeleportToSpawn(parts[1]);
                break;

            case "time":
                DevConsole.Log($"Time: {_engine.Clock.TimeString}  ({(int)_engine.Clock.Hour}h)  " +
                               $"{(_engine.Clock.IsDay ? "Day" : "Night")}");
                break;

            case "settime":
                if (parts.Length < 2 || !float.TryParse(parts[1], out float h))
                { DevConsole.Log("Usage: settime <0-24>"); break; }
                _engine.Clock.SetTime(h);
                DevConsole.Log($"Time set to {_engine.Clock.TimeString}");
                break;

            case "timescale":
                if (parts.Length < 2 || !float.TryParse(parts[1], out float ts))
                { DevConsole.Log("Usage: timescale <multiplier>"); break; }
                _engine.Clock.TimeScale = ts;
                DevConsole.Log($"Time scale: {ts}x");
                break;

            case "lighting":
                if (parts.Length > 1 && parts[1] == "off")
                {
                    _engine.LightingSystem.IsEnabled = false;
                    DevConsole.Log("Lighting disabled.");
                }
                else
                {
                    _engine.LightingSystem.IsEnabled = true;
                    DevConsole.Log("Lighting enabled.");
                }
                break;

            case "spawn":
                if (parts.Length < 2) { DevConsole.Log("Usage: spawn <protoId>"); break; }
                SpawnEntity(parts[1]);
                break;

            case "heal":
                HealPlayer(parts);
                break;

            case "hurt":
                HurtPlayer(parts);
                break;

            case "hunger":
                MetabSetValue(parts, "hunger"); break;
            case "thirst":
                MetabSetValue(parts, "thirst"); break;
            case "bladder":
                MetabSetValue(parts, "bladder"); break;
            case "bowel":
                MetabSetValue(parts, "bowel"); break;

            case "feed":
                if (GetPlayerMetab() is var (_, fm1))
                { fm1.Hunger = 100; fm1.Thirst = 100; DevConsole.Log("Fed & hydrated."); }
                break;

            case "starve":
                if (GetPlayerMetab() is var (_, sm1))
                { sm1.Hunger = 0; sm1.Thirst = 0; DevConsole.Log("Starving & dehydrated."); }
                break;

            case "relieve":
                if (GetPlayerMetab() is var (_, rm))
                { rm.Bladder = 0; rm.Bowel = 0; DevConsole.Log("Bladder & bowel emptied."); }
                break;

            case "metab":
                if (GetPlayerMetab() is var (_, mm))
                {
                    DevConsole.Log($"  Hunger:  {mm.Hunger:0.0}/100  ({mm.HungerStatus})");
                    DevConsole.Log($"  Thirst:  {mm.Thirst:0.0}/100  ({mm.ThirstStatus})");
                    DevConsole.Log($"  Bladder: {mm.Bladder:0.0}/100  ({mm.BladderStatus})");
                    DevConsole.Log($"  Bowel:   {mm.Bowel:0.0}/100  ({mm.BowelStatus})");
                    DevConsole.Log($"  Speed:   {mm.SpeedModifier * 100:0}%");
                    if (mm.DigestingItems.Count > 0)
                    {
                        foreach (var d in mm.DigestingItems)
                            DevConsole.Log($"  Digesting: {d.Name} ({d.Progress * 100:0}%)");
                    }
                    if (mm.SubstanceConcentrations.Count > 0)
                    {
                        foreach (var concentration in mm.SubstanceConcentrations.Values.OrderBy(v => v.Name))
                            DevConsole.Log($"  Substance: {concentration.Name} ({concentration.Amount:0.##})");
                    }
                }
                break;

            case "clear":
                DevConsole.Clear();
                break;

            default:
                DevConsole.Log($"Unknown: {parts[0]}. Type 'help'.");
                break;
        }
    }

    public void LoadMap(string mapId, string spawnId = "default", bool placePlayerAtSpawn = true)
    {
        var (tileMap, spawn) = _mapManager.LoadMap(mapId, spawnId);
        if (tileMap == null) { DevConsole.Log($"Map not found: {mapId}"); return; }

        _tileMapRenderer.TileMap = tileMap;
        _engine.CollisionSystem.SetTileMap(tileMap);
        if (placePlayerAtSpawn)
            TeleportPlayerToSpawn(spawn);
        DevConsole.Log($"Loaded: {mapId} @ {spawnId}");
    }

    private void TeleportToSpawn(string spawnId)
    {
        var map = _mapManager.CurrentMap;
        if (map == null) { DevConsole.Log("No map loaded."); return; }
        var spawn = map.SpawnPoints.FirstOrDefault(s => s.Id == spawnId);
        if (spawn == null) { DevConsole.Log($"Spawn '{spawnId}' not found."); return; }
        TeleportPlayerToSpawn(spawn);
    }

    private void TeleportPlayerToSpawn(SpawnPoint spawn)
    {
        if (spawn == null) return;

        var player = _engine.World
            .GetEntitiesWith<TransformComponent, PlayerTagComponent>()
            .FirstOrDefault();
        if (player == null) return;

        var t = player.GetComponent<TransformComponent>()!;
        var tileSize = _mapManager.CurrentMap?.TileSize ?? 32;
        t.Position = new Vector2(spawn.X * tileSize, spawn.Y * tileSize);
        _engine.Camera.Position = t.Position;
    }

    private (Entity, MetabolismComponent)? GetPlayerMetab()
    {
        var player = _engine.World
            .GetEntitiesWith<PlayerTagComponent, MetabolismComponent>()
            .FirstOrDefault();
        if (player == null) { DevConsole.Log("Player has no metabolism."); return null; }
        return (player, player.GetComponent<MetabolismComponent>()!);
    }

    private (Entity, HealthComponent)? GetPlayerHealth()
    {
        var player = _engine.World
            .GetEntitiesWith<PlayerTagComponent, HealthComponent>()
            .FirstOrDefault();
        if (player == null) { DevConsole.Log("Player has no health."); return null; }
        return (player, player.GetComponent<HealthComponent>()!);
    }

    private void SpawnEntity(string protoId)
    {
        var proto = _engine.Prototypes.GetEntity(protoId);
        if (proto == null)
        {
            DevConsole.Log($"Prototype not found: {protoId}");
            return;
        }

        var player = _engine.World
            .GetEntitiesWith<TransformComponent, PlayerTagComponent>()
            .FirstOrDefault();
        if (player == null)
        {
            DevConsole.Log("Player not found.");
            return;
        }

        var playerTransform = player.GetComponent<TransformComponent>()!;
        var spawnPos = playerTransform.Position + new Vector2(40, 0);
        var entity = _engine.EntityFactory.CreateFromPrototype(proto, spawnPos);
        if (entity == null)
        {
            DevConsole.Log($"Failed to spawn: {protoId}");
            return;
        }

        if (ServiceLocator.Has<IWorldStateTracker>())
            ServiceLocator.Get<IWorldStateTracker>().MarkDirty();

        DevConsole.Log($"Spawned: {protoId}");
    }

    private void MetabSetValue(string[] parts, string field)
    {
        if (GetPlayerMetab() is not var (_, m)) return;

        if (parts.Length < 2)
        {
            var val = field switch
            {
                "hunger" => m.Hunger, "thirst" => m.Thirst,
                "bladder" => m.Bladder, "bowel" => m.Bowel,
                _ => 0f
            };
            var status = field switch
            {
                "hunger" => m.HungerStatus, "thirst" => m.ThirstStatus,
                "bladder" => m.BladderStatus, "bowel" => m.BowelStatus,
                _ => NeedStatus.Normal
            };
            DevConsole.Log($"{field}: {val:0.0}/100 ({status})");
            return;
        }

        if (!float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float v))
        { DevConsole.Log($"Usage: {field} <0-100>"); return; }

        v = Math.Clamp(v, 0f, 100f);
        switch (field)
        {
            case "hunger": m.Hunger = v; break;
            case "thirst": m.Thirst = v; break;
            case "bladder": m.Bladder = v; break;
            case "bowel": m.Bowel = v; break;
        }
        DevConsole.Log($"{field} = {v:0.0}");
    }

    private void HealPlayer(string[] parts)
    {
        if (GetPlayerHealth() is not var (player, health))
            return;

        health.MaxHealth = Math.Max(1f, health.MaxHealth);
        var wounds = player.GetComponent<WoundComponent>();

        if (parts.Length < 2)
        {
            if (wounds != null)
            {
                wounds.SlashDamage = 0f;
                wounds.BluntDamage = 0f;
                wounds.BurnDamage = 0f;
                wounds.ExhaustionDamage = 0f;
                wounds.Bleedings.Clear();
            }

            health.Health = health.MaxHealth;
            health.IsDead = false;
            health.DeathPoseApplied = false;
            if (player.GetComponent<TransformComponent>() is { } transform)
            {
                transform.Rotation = 0f;
            }
            DevConsole.Log($"Healed to full: {health.Health:0.##}/{health.MaxHealth:0.##}");
            return;
        }

        if (!float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var amount))
        {
            DevConsole.Log("Usage: heal [amount]");
            return;
        }

        amount = Math.Max(0f, amount);
        if (wounds != null)
        {
            var remaining = amount;

            remaining -= WoundComponent.HealDamage(player, DamageType.Exhaustion, remaining);
            if (remaining > 0f)
                remaining -= WoundComponent.HealDamage(player, DamageType.Blunt, remaining);
            if (remaining > 0f)
                remaining -= WoundComponent.HealDamage(player, DamageType.Slash, remaining);
            if (remaining > 0f)
                WoundComponent.HealDamage(player, DamageType.Burn, remaining);

            if (amount > 0f)
                WoundComponent.StopBleeding(player, amount * 0.25f);

            health.Health = Math.Clamp(health.MaxHealth - wounds.TotalDamage, 0f, health.MaxHealth);
        }
        else
        {
            health.Health = Math.Clamp(health.Health + amount, 0f, health.MaxHealth);
        }

        if (health.Health > 0f || wounds?.TotalDamage <= 0.01f)
        {
            health.IsDead = false;
            health.DeathPoseApplied = false;
            if (player.GetComponent<TransformComponent>() is { } transform)
            {
                transform.Rotation = 0f;
            }
        }
        DevConsole.Log($"Health: {health.Health:0.##}/{health.MaxHealth:0.##}");
    }

    private void HurtPlayer(string[] parts)
    {
        if (parts.Length < 2)
        {
            DevConsole.Log("Usage: hurt <amount>");
            return;
        }

        if (GetPlayerHealth() is not var (player, health))
            return;

        if (!float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var amount))
        {
            DevConsole.Log("Usage: hurt <amount>");
            return;
        }

        amount = Math.Max(0f, amount);
        var wounds = player.GetComponent<WoundComponent>();
        if (wounds != null)
        {
            var perType = amount / 3f;
            WoundComponent.ApplyDamage(player, DamageType.Slash, perType);
            WoundComponent.ApplyDamage(player, DamageType.Blunt, perType);
            WoundComponent.ApplyDamage(player, DamageType.Burn, amount - perType - perType);
            health.Health = Math.Clamp(health.MaxHealth - wounds.TotalDamage, 0f, Math.Max(1f, health.MaxHealth));
        }
        else
        {
            health.Health = Math.Clamp(health.Health - amount, 0f, Math.Max(1f, health.MaxHealth));
        }

        DevConsole.Log($"Health: {health.Health:0.##}/{health.MaxHealth:0.##}");
    }

    private static IEnumerable<PropertyInfo> GetDisplayProperties(Type type)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public;

        return type
            .GetProperties(flags)
            .Where(prop =>
                prop.CanRead &&
                prop.GetIndexParameters().Length == 0 &&
                prop.Name != nameof(Component.Owner));
    }

    private static string FormatValue(object value)
    {
        if (value == null)
            return "null";

        return value switch
        {
            string str => string.IsNullOrEmpty(str) ? "\"\"" : str,
            Vector2 vector => $"({FormatNumber(vector.X)}, {FormatNumber(vector.Y)})",
            Rectangle rect => $"({rect.X}, {rect.Y}, {rect.Width}, {rect.Height})",
            Color color => $"#{color.R:X2}{color.G:X2}{color.B:X2}{color.A:X2}",
            IEnumerable enumerable when value is not string => FormatEnumerable(enumerable),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? value.GetType().Name
        };
    }

    private static string FormatEnumerable(IEnumerable values)
    {
        var items = values.Cast<object>()
            .Take(5)
            .Select(FormatValue)
            .ToList();

        return items.Count == 0 ? "[]" : $"[{string.Join(", ", items)}]";
    }

    private static string FormatNumber(float value)
    {
        return value.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
