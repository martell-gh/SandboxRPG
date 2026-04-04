using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace MTEngine.UI;

public class UISeparator : UIElement
{
    public Color Color { get; set; } = new(70, 110, 70, 100);
    public int Thickness { get; set; } = 1;

    public override void Layout(Rectangle available)
    {
        base.Layout(available);
        Bounds = new Rectangle(Bounds.X, Bounds.Y, Bounds.Width, Thickness);
    }

    public override void Draw(SpriteBatch sb, Texture2D pixel, SpriteFont font)
    {
        if (!Visible) return;
        sb.Draw(pixel, Bounds, Color);
    }
}
