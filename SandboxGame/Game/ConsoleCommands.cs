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
using MTEngine.Systems;
using MTEngine.World;

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
                DevConsole.Log("  goto <spawnId>         - teleport");
                DevConsole.Log("  time                   - show game time");
                DevConsole.Log("  settime <0-24>         - set time of day");
                DevConsole.Log("  timescale <x>          - speed of time");
                DevConsole.Log("  lighting <on|off>      - toggle lighting");
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

            case "clear":
                DevConsole.Clear();
                break;

            default:
                DevConsole.Log($"Unknown: {parts[0]}. Type 'help'.");
                break;
        }
    }

    public void LoadMap(string mapId, string spawnId = "default")
    {
        var (tileMap, spawn) = _mapManager.LoadMap(mapId, spawnId);
        if (tileMap == null) { DevConsole.Log($"Map not found: {mapId}"); return; }

        _tileMapRenderer.TileMap = tileMap;
        _engine.CollisionSystem.SetTileMap(tileMap);
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
