using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace MTEngine.UI;

public class UISeparator : UIElement
{
    public Color? OverrideColor { get; set; }
    public Color Color { get; set; } = new(70, 70, 110, 100);
    public int Thickness { get; set; } = 1;

    public UITheme? Theme { get; set; }

    public override void Layout(Rectangle available)
    {
        base.Layout(available);
        Bounds = new Rectangle(Bounds.X, Bounds.Y, Bounds.Width, Thickness);
    }

    public override void Draw(SpriteBatch sb, Texture2D pixel, SpriteFont font)
    {
        if (!Visible) return;
        var color = OverrideColor ?? Theme?.SeparatorColor ?? Color;
        sb.Draw(pixel, Bounds, color);
    }
}
