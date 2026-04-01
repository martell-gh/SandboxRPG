using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MTEngine.Components;
using MTEngine.Core;
using MTEngine.Rendering;
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
                DevConsole.Log("  maps - list available maps");
                DevConsole.Log("  loadmap <id> [spawnId] - load map");
                DevConsole.Log("  goto <spawnId> - teleport to spawn point");
                DevConsole.Log("  clear - clear console");
                break;

            case "maps":
                var available = _mapManager.GetAvailableMaps();
                if (available.Count == 0)
                    DevConsole.Log("No maps found.");
                else
                    foreach (var m in available)
                        DevConsole.Log($"  - {m}");
                break;

            case "loadmap":
                if (parts.Length < 2) { DevConsole.Log("Usage: loadmap <id> [spawnId]"); break; }
                var mapId = parts[1];
                var spawnId = parts.Length > 2 ? parts[2] : "default";
                LoadMap(mapId, spawnId);
                break;

            case "goto":
                if (parts.Length < 2) { DevConsole.Log("Usage: goto <spawnId>"); break; }
                TeleportToSpawn(parts[1]);
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

        // подключаем коллизии к новой карте
        _engine.CollisionSystem.SetTileMap(tileMap);

        TeleportPlayerToSpawn(spawn);
        DevConsole.Log($"Loaded: {mapId} @ {spawnId}");
    }

    private void TeleportToSpawn(string spawnId)
    {
        var map = _mapManager.CurrentMap;
        if (map == null) { DevConsole.Log("No map loaded."); return; }

        var spawn = map.SpawnPoints.FirstOrDefault(s => s.Id == spawnId);
        if (spawn == null) { DevConsole.Log($"Spawn not found: {spawnId}"); return; }

        TeleportPlayerToSpawn(spawn);
    }

    private void TeleportPlayerToSpawn(SpawnPoint? spawn)
    {
        if (spawn == null) return;

        var player = _engine.World
            .GetEntitiesWith<TransformComponent, VelocityComponent>()
            .FirstOrDefault();

        if (player == null) return;

        var t = player.GetComponent<TransformComponent>()!;
        t.Position = new Vector2(spawn.X * 16, spawn.Y * 16);
        _engine.Camera.Position = t.Position;
    }
}