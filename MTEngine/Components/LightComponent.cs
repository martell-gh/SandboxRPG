using Microsoft.Xna.Framework;
using MTEngine.ECS;

namespace MTEngine.Components;

[RegisterComponent("light")]
public class LightComponent : Component
{
    public Color Color { get; set; } = Color.White;

    [DataField("radius")]
    public float Radius { get; set; } = 150f;

    [DataField("intensity")]
    public float Intensity { get; set; } = 1f;

    [DataField("enabled")]
    [SaveField("enabled")]
    public bool PrototypeEnabled
    {
        get => Enabled;
        set => Enabled = value;
    }

    [DataField("color")]
    public string ColorHex
    {
        get => $"#{Color.R:X2}{Color.G:X2}{Color.B:X2}";
        set => Color = ParseHex(value);
    }

    private static Color ParseHex(string hex)
    {
        hex = hex.Trim();
        if (string.IsNullOrWhiteSpace(hex))
            return Color.White;

        hex = hex.TrimStart('#');

        if (hex.Length != 6)
            return Color.White;

        try
        {
            var r = Convert.ToInt32(hex[..2], 16);
            var g = Convert.ToInt32(hex[2..4], 16);
            var b = Convert.ToInt32(hex[4..6], 16);
            return new Color(r, g, b);
        }
        catch
        {
            return Color.White;
        }
    }
}
