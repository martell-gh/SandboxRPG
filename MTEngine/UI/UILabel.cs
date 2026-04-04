using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace MTEngine.UI;

public class UILabel : UIElement
{
    public string Text { get; set; } = "";
    public Color Color { get; set; } = Color.White;
    public float Scale { get; set; } = 1f;

    public override void Layout(Rectangle available)
    {
        base.Layout(available);
        if (Height <= 0)
            Bounds = new Rectangle(Bounds.X, Bounds.Y, Bounds.Width, (int)(16 * Scale));
    }

    public override void Draw(SpriteBatch sb, Texture2D pixel, SpriteFont font)
    {
        if (!Visible || string.IsNullOrEmpty(Text)) return;
        sb.DrawString(font, Text, new Vector2(Bounds.X, Bounds.Y), Color,
            0f, Vector2.Zero, Scale, SpriteEffects.None, 0f);
    }
}
