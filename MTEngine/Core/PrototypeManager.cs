using System.Text.Json.Nodes;
using MTEngine.Metabolism;
using MTEngine.Rendering;

namespace MTEngine.Core;

public class TileSpriteInfo
{
    public string Source { get; set; } = "";
    public int SrcX { get; set; }
    public int SrcY { get; set; }
    public int Width { get; set; } = 16;
    public int Height { get; set; } = 16;
    public string? FullPath { get; set; }
}

public class TilePrototype
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? DirectoryPath { get; set; }
    public bool Solid { get; set; }
    public bool Transparent { get; set; } = true;
    public bool Opaque { get; set; }
    public string Color { get; set; } = "#ffffff";
    public string? Tileset { get; set; }
    public int SrcX { get; set; }
    public int SrcY { get; set; }
    public TileSpriteInfo? Sprite { get; set; }
    public AnimationSet? Animations { get; set; }
}

public class PrototypeManager
{
    private readonly Dictionary<string, TilePrototype> _tiles = new();
    private readonly Dictionary<string, EntityPrototype> _entities = new();
    private readonly Dictionary<string, SubstancePrototype> _substances = new();

    public void LoadFromDirectory(string rootDirectory)
    {
        if (!Directory.Exists(rootDirectory))
        {
            Console.WriteLine($"[PrototypeManager] Not found: {rootDirectory}");
            return;
        }

        foreach (var file in Directory.GetFiles(rootDirectory, "proto.json", SearchOption.AllDirectories))
        {
            try
            {
                var json = File.ReadAllText(file);
                var node = JsonNode.Parse(json)?.AsObject();
                if (node == null) continue;

                var category = node["category"]?.GetValue<string>();
                var id = node["id"]?.GetValue<string>();
                if (id == null) continue;

                if (category == "tile")
                {
                    var proto = new TilePrototype
                    {
                        Id = id,
                        Name = node["name"]?.GetValue<string>() ?? id,
                        DirectoryPath = Path.GetDirectoryName(file),
                        Solid = node["solid"]?.GetValue<bool>() ?? false,
                        Transparent = node["transparent"]?.GetValue<bool>() ?? true,
                        Opaque = node["opaque"]?.GetValue<bool>() ?? false,
                        Color = node["color"]?.GetValue<string>() ?? "#ffffff",
                        Tileset = node["tileset"]?.GetValue<string>(),
                        SrcX = node["srcX"]?.GetValue<int>() ?? 0,
                        SrcY = node["srcY"]?.GetValue<int>() ?? 0,
                    };

                    var dir = Path.GetDirectoryName(file)!;

                    // спрайт
                    var spriteNode = node["sprite"]?.AsObject();
                    if (spriteNode != null)
                    {
                        var spriteInfo = new TileSpriteInfo
                        {
                            Source = spriteNode["source"]?.GetValue<string>() ?? "",
                            SrcX = spriteNode["srcX"]?.GetValue<int>() ?? 0,
                            SrcY = spriteNode["srcY"]?.GetValue<int>() ?? 0,
                            Width = spriteNode["width"]?.GetValue<int>() ?? 16,
                            Height = spriteNode["height"]?.GetValue<int>() ?? 16,
                        };
                        spriteInfo.FullPath = Path.Combine(dir, spriteInfo.Source);
                        proto.Sprite = spriteInfo;
                    }

                    // анимации — ищем animations.json рядом
                    var animPath = Path.Combine(dir, "animations.json");
                    if (File.Exists(animPath))
                    {
                        proto.Animations = AnimationSet.LoadFromFile(animPath);
                        Console.WriteLine($"[PrototypeManager] Loaded animations for: {id}");
                    }

                    _tiles[id] = proto;
                    Console.WriteLine($"[PrototypeManager] Loaded tile: {id}");
                }
                else if (category == "entity")
                {
                    var entityProto = EntityPrototype.LoadFromFile(file);
                    if (entityProto == null) continue;

                    _entities[id] = entityProto;
                    Console.WriteLine($"[PrototypeManager] Loaded entity: {id}");
                }
                else if (category == "substance")
                {
                    var substanceProto = SubstancePrototype.LoadFromFile(file);
                    if (substanceProto == null) continue;

                    _substances[id] = substanceProto;
                    Console.WriteLine($"[PrototypeManager] Loaded substance: {id}");
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"[PrototypeManager] Error loading {file}: {e.Message}");
            }
        }

        Console.WriteLine($"[PrototypeManager] Total tiles: {_tiles.Count}");
        Console.WriteLine($"[PrototypeManager] Total entities: {_entities.Count}");
        Console.WriteLine($"[PrototypeManager] Total substances: {_substances.Count}");
    }

    public TilePrototype? GetTile(string id)
        => _tiles.TryGetValue(id, out var p) ? p : null;

    public EntityPrototype? GetEntity(string id)
        => _entities.TryGetValue(id, out var p) ? p : null;

    public SubstancePrototype? GetSubstance(string id)
        => _substances.TryGetValue(id, out var p) ? p : null;

    public IEnumerable<TilePrototype> GetAllTiles() => _tiles.Values;
    public IEnumerable<EntityPrototype> GetAllEntities() => _entities.Values;
    public IEnumerable<SubstancePrototype> GetAllSubstances() => _substances.Values;
    public bool TileExists(string id) => _tiles.ContainsKey(id);
    public bool EntityExists(string id) => _entities.ContainsKey(id);
    public bool SubstanceExists(string id) => _substances.ContainsKey(id);
}
