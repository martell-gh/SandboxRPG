using System.Text.Json.Nodes;

namespace MTEngine.Rendering;

/// <summary>
/// Autotile (smoothing) configuration.
/// Uses a 4-bit cardinal bitmask: N=1, S=2, W=4, E=8.
/// Each of the 16 states maps to a sprite file + source region.
/// </summary>
public class SmoothingConfig
{
    public string[] SmoothWith { get; set; } = Array.Empty<string>();
    public string Mode { get; set; } = "mask";
    public SmoothingState? FillCorner { get; set; }
    public SmoothingState? InnerCorner { get; set; }
    public SmoothingState? HorizontalEdge { get; set; }
    public SmoothingState? VerticalEdge { get; set; }
    public SmoothingState? OuterCorner { get; set; }

    /// <summary>
    /// 16 entries — one per bitmask state (0..15).
    /// Each entry is the full path to the sprite file and source rect.
    /// </summary>
    public SmoothingState[] States { get; } = new SmoothingState[16];

    public SmoothingConfig()
    {
        for (int i = 0; i < 16; i++)
            States[i] = new SmoothingState();
    }

    public static SmoothingConfig? LoadFromFile(string path)
    {
        try
        {
            var dir = Path.GetDirectoryName(path)!;
            var json = File.ReadAllText(path);
            var root = JsonNode.Parse(json)?.AsObject();
            if (root == null) return null;

            var config = new SmoothingConfig();

            // smoothWith
            var smoothWithNode = root["smoothWith"]?.AsArray();
            if (smoothWithNode != null)
            {
                config.SmoothWith = smoothWithNode
                    .Select(n => n?.GetValue<string>() ?? "")
                    .Where(s => !string.IsNullOrEmpty(s))
                    .ToArray();
            }

            config.Mode = root["mode"]?.GetValue<string>() ?? "mask";
            config.FillCorner = LoadState(root["fillCorner"]?.AsObject(), dir);
            config.InnerCorner = LoadState(root["innerCorner"]?.AsObject(), dir);
            config.HorizontalEdge = LoadState(root["horizontalEdge"]?.AsObject(), dir);
            config.VerticalEdge = LoadState(root["verticalEdge"]?.AsObject(), dir);
            config.OuterCorner = LoadState(root["outerCorner"]?.AsObject(), dir);

            // states: array of 16 entries, each { "file": "wood0.png", "srcY": 0 }
            // or shorthand: sprites array of { "file", "srcX", "srcY", "width", "height" }
            var statesNode = root["states"]?.AsArray();
            if (statesNode != null)
            {
                for (int i = 0; i < Math.Min(16, statesNode.Count); i++)
                {
                    var stateNode = statesNode[i]?.AsObject();
                    if (stateNode == null) continue;

                    var file = stateNode["file"]?.GetValue<string>();
                    if (file == null) continue;

                    config.States[i] = new SmoothingState
                    {
                        FilePath = Path.Combine(dir, file),
                        SrcX = stateNode["srcX"]?.GetValue<int>() ?? 0,
                        SrcY = stateNode["srcY"]?.GetValue<int>() ?? 0,
                        Width = stateNode["width"]?.GetValue<int>() ?? 32,
                        Height = stateNode["height"]?.GetValue<int>() ?? 32,
                        Rotation = stateNode["rotation"]?.GetValue<float>() ?? 0f,
                    };
                }
            }

            return config;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SmoothingConfig] Error loading {path}: {ex.Message}");
            return null;
        }
    }

    private static SmoothingState? LoadState(JsonObject? node, string dir)
    {
        if (node == null)
            return null;

        var file = node["file"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(file))
            return null;

        return new SmoothingState
        {
            FilePath = Path.Combine(dir, file),
            SrcX = node["srcX"]?.GetValue<int>() ?? 0,
            SrcY = node["srcY"]?.GetValue<int>() ?? 0,
            Width = node["width"]?.GetValue<int>() ?? 32,
            Height = node["height"]?.GetValue<int>() ?? 32,
            Rotation = node["rotation"]?.GetValue<float>() ?? 0f,
        };
    }
}

public class SmoothingState
{
    public string? FilePath { get; set; }
    public int SrcX { get; set; }
    public int SrcY { get; set; }
    public int Width { get; set; } = 32;
    public int Height { get; set; } = 32;
    public float Rotation { get; set; }
}
