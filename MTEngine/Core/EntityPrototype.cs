using System.Text.Json.Nodes;

namespace MTEngine.Core;

public class EntityPrototype
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public JsonObject? Components { get; set; }
    public string? AnimationsPath { get; set; }
    public string? SpritePath { get; set; }
    public string? DirectoryPath { get; set; }

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

            var dir = Path.GetDirectoryName(protoPath)!;
            var proto = new EntityPrototype
            {
                Id = node["id"]?.GetValue<string>() ?? "",
                Name = node["name"]?.GetValue<string>() ?? "",
                Category = node["category"]?.GetValue<string>() ?? "",
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
            }

            return proto;
        }
        catch (Exception e)
        {
            Console.WriteLine($"[EntityPrototype] Error: {e.Message}");
            return null;
        }
    }
}