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
    public string BaseId { get; set; } = "";
    public bool Abstract { get; set; }
    public string? DirectoryPath { get; set; }
    public bool Solid { get; set; }
    public bool Transparent { get; set; } = true;
    public bool Opaque { get; set; }
    public bool HiddenInGame { get; set; }
    public string Color { get; set; } = "#ffffff";
    public string? Tileset { get; set; }
    public int SrcX { get; set; }
    public int SrcY { get; set; }
    public TileSpriteInfo? Sprite { get; set; }
    public AnimationSet? Animations { get; set; }
    public SmoothingConfig? Smoothing { get; set; }
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

        _tiles.Clear();
        _entities.Clear();
        _substances.Clear();

        var documents = new Dictionary<string, PrototypeDocument>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.GetFiles(rootDirectory, "proto.json", SearchOption.AllDirectories))
        {
            try
            {
                var json = File.ReadAllText(file);
                var node = JsonNode.Parse(json)?.AsObject();
                if (node == null) continue;

                var id = node["id"]?.GetValue<string>();
                if (id == null) continue;

                documents[id] = new PrototypeDocument(id, file, node);
            }
            catch (Exception e)
            {
                Console.WriteLine($"[PrototypeManager] Error reading {file}: {e.Message}");
            }
        }

        var resolved = new Dictionary<string, JsonObject>(StringComparer.OrdinalIgnoreCase);
        var resolving = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var document in documents.Values)
        {
            try
            {
                var node = ResolvePrototypeNode(document.Id, documents, resolved, resolving);
                var category = node["category"]?.GetValue<string>();
                var id = node["id"]?.GetValue<string>();
                if (id == null) continue;

                if (category == "tile")
                {
                    var proto = LoadTilePrototype(node, document.FilePath);

                    _tiles[id] = proto;
                    Console.WriteLine($"[PrototypeManager] Loaded tile: {id}");
                }
                else if (category == "entity")
                {
                    var entityProto = EntityPrototype.LoadFromNode(node, document.FilePath);
                    if (entityProto == null) continue;

                    _entities[id] = entityProto;
                    Console.WriteLine($"[PrototypeManager] Loaded entity: {id}");
                }
                else if (category == "substance")
                {
                    var substanceProto = SubstancePrototype.LoadFromNode(node);
                    if (substanceProto == null) continue;

                    _substances[id] = substanceProto;
                    Console.WriteLine($"[PrototypeManager] Loaded substance: {id}");
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"[PrototypeManager] Error loading {document.FilePath}: {e.Message}");
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

    public IEnumerable<TilePrototype> GetAllTiles() => _tiles.Values.Where(p => !p.Abstract);
    public IEnumerable<EntityPrototype> GetAllEntities() => _entities.Values.Where(p => !p.Abstract);
    public IEnumerable<SubstancePrototype> GetAllSubstances() => _substances.Values.Where(p => !p.Abstract);
    public bool TileExists(string id) => _tiles.ContainsKey(id);
    public bool EntityExists(string id) => _entities.ContainsKey(id);
    public bool SubstanceExists(string id) => _substances.ContainsKey(id);

    private static TilePrototype LoadTilePrototype(JsonObject node, string filePath)
    {
        var id = node["id"]?.GetValue<string>() ?? "";
        var dir = Path.GetDirectoryName(filePath)!;
        var proto = new TilePrototype
        {
            Id = id,
            Name = node["name"]?.GetValue<string>() ?? id,
            BaseId = node["base"]?.GetValue<string>() ?? "",
            Abstract = node["abstract"]?.GetValue<bool>() ?? false,
            DirectoryPath = dir,
            Solid = node["solid"]?.GetValue<bool>() ?? false,
            Transparent = node["transparent"]?.GetValue<bool>() ?? true,
            Opaque = node["opaque"]?.GetValue<bool>() ?? false,
            HiddenInGame = node["hiddenInGame"]?.GetValue<bool>() ?? false,
            Color = node["color"]?.GetValue<string>() ?? "#ffffff",
            Tileset = node["tileset"]?.GetValue<string>(),
            SrcX = node["srcX"]?.GetValue<int>() ?? 0,
            SrcY = node["srcY"]?.GetValue<int>() ?? 0,
        };

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

        var animPath = Path.Combine(dir, "animations.json");
        if (File.Exists(animPath))
        {
            proto.Animations = AnimationSet.LoadFromFile(animPath);
            Console.WriteLine($"[PrototypeManager] Loaded animations for: {id}");
        }

        var smoothPath = Path.Combine(dir, "smoothing.json");
        if (File.Exists(smoothPath))
        {
            proto.Smoothing = SmoothingConfig.LoadFromFile(smoothPath);
            Console.WriteLine($"[PrototypeManager] Loaded smoothing for: {id}");
        }

        return proto;
    }

    private static JsonObject ResolvePrototypeNode(
        string id,
        Dictionary<string, PrototypeDocument> documents,
        Dictionary<string, JsonObject> resolved,
        HashSet<string> resolving)
    {
        if (resolved.TryGetValue(id, out var cached))
            return cached;

        if (!documents.TryGetValue(id, out var document))
            throw new InvalidOperationException($"Unknown base prototype: {id}");

        if (!resolving.Add(id))
            throw new InvalidOperationException($"Cyclic prototype inheritance detected at: {id}");

        var baseId = document.Node["base"]?.GetValue<string>();
        JsonObject merged;
        if (!string.IsNullOrWhiteSpace(baseId))
        {
            merged = CloneObject(ResolvePrototypeNode(baseId!, documents, resolved, resolving));
            merged.Remove("abstract");
        }
        else
        {
            merged = new JsonObject();
        }

        MergePrototypeObject(merged, document.Node);
        resolving.Remove(id);
        resolved[id] = CloneObject(merged);
        return merged;
    }

    private static void MergePrototypeObject(JsonObject target, JsonObject source)
    {
        foreach (var (key, value) in source)
        {
            if (value == null)
            {
                target.Remove(key);
                continue;
            }

            if (string.Equals(key, "components", StringComparison.OrdinalIgnoreCase)
                && value is JsonObject sourceComponents)
            {
                if (target["components"] is not JsonObject targetComponents)
                {
                    targetComponents = new JsonObject();
                    target["components"] = targetComponents;
                }

                foreach (var (componentId, componentValue) in sourceComponents)
                {
                    if (componentValue == null)
                        targetComponents.Remove(componentId);
                    else
                        targetComponents[componentId] = CloneNode(componentValue);
                }
                continue;
            }

            target[key] = CloneNode(value);
        }
    }

    private static JsonObject CloneObject(JsonObject source)
        => CloneNode(source).AsObject();

    private static JsonNode CloneNode(JsonNode node)
        => JsonNode.Parse(node.ToJsonString()) ?? throw new InvalidOperationException("Failed to clone JSON node.");

    private sealed record PrototypeDocument(string Id, string FilePath, JsonObject Node);
}
