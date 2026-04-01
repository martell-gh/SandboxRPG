using Microsoft.Xna.Framework;
using MTEngine.ECS;

namespace MTEngine.Components;

public class LightComponent : Component
{
    public Color Color { get; set; } = Color.White;
    public float Radius { get; set; } = 150f;
    public float Intensity { get; set; } = 1f;

    private bool enabled = true;

    public bool GetEnabled()
    {
        return enabled;
    }

    public void SetEnabled(bool value)
    {
        enabled = value;
    }
}