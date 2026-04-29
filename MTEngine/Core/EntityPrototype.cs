using System.Text.Json.Nodes;
using Microsoft.Xna.Framework;
using MTEngine.Rendering;

namespace MTEngine.Core;

public class EntityPrototype
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public string BaseId { get; set; } = "";
    public bool Abstract { get; set; }
    public JsonObject? Components { get; set; }
    public string? AnimationsPath { get; set; }
    public string? SpritePath { get; set; }
    public string? DirectoryPath { get; set; }
    public Rectangle? PreviewSourceRect { get; set; }
    public string PreviewColor { get; set; } = "#ffffff";

    public static EntityPrototype? LoadFromFile(string protoPath)
    {
        if (!File.Exists(protoPath))
        {
            Console.WriteLine($"[EntityPrototype] Not found: {protoPath}");
            return null;
        }

        try
        {
            var json = File.ReadAllText(protoPath);
            var node = JsonNode.Parse(json)?.AsObject();
            if (node == null) return null;

            return LoadFromNode(node, protoPath);
        }
        catch (Exception e)
        {
            Console.WriteLine($"[EntityPrototype] Error: {e.Message}");
            return null;
        }
    }

    public static EntityPrototype? LoadFromNode(JsonObject node, string protoPath)
    {
        try
        {
            var dir = Path.GetDirectoryName(protoPath)!;
            var proto = new EntityPrototype
            {
                Id = node["id"]?.GetValue<string>() ?? "",
                Name = node["name"]?.GetValue<string>() ?? "",
                Category = node["category"]?.GetValue<string>() ?? "",
                BaseId = node["base"]?.GetValue<string>() ?? "",
                Abstract = node["abstract"]?.GetValue<bool>() ?? false,
                Components = node["components"]?.AsObject(),
                DirectoryPath = dir
            };

            // путь к анимациям
            var animFile = node["animations"]?.GetValue<string>();
            if (animFile != null)
                proto.AnimationsPath = Path.Combine(dir, animFile);

            // путь к спрайту
            var spriteNode = node["components"]?["sprite"]?.AsObject();
            if (spriteNode != null)
            {
                var src = spriteNode["source"]?.GetValue<string>();
                if (src != null)
                    proto.SpritePath = Path.Combine(dir, src);

                var width = spriteNode["width"]?.GetValue<int>() ?? 32;
                var height = spriteNode["height"]?.GetValue<int>() ?? 32;
                proto.PreviewSourceRect = new Rectangle(
                    spriteNode["srcX"]?.GetValue<int>() ?? 0,
                    spriteNode["srcY"]?.GetValue<int>() ?? 0,
                    width,
                    height
                );
            }

            var colorNode = node["components"]?["light"]?["color"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(colorNode))
                proto.PreviewColor = colorNode;

            if (proto.PreviewSourceRect == null && proto.AnimationsPath != null)
            {
                var animSet = AnimationSet.LoadFromFile(proto.AnimationsPath);
                var clip = animSet?.GetClip("idle") ?? animSet?.GetAllClips().FirstOrDefault();
                if (clip?.Frames.Count > 0)
                {
                    proto.PreviewSourceRect = clip.Frames[0].SourceRect;

                    if (string.IsNullOrEmpty(proto.SpritePath) && !string.IsNullOrEmpty(animSet!.TexturePath))
                        proto.SpritePath = Path.Combine(dir, animSet.TexturePath);
                }
            }

            return proto;
        }
        catch (Exception e)
        {
            Console.WriteLine($"[EntityPrototype] Error loading {protoPath}: {e.Message}");
            return null;
        }
    }
}
