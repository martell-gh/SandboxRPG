using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace MTEngine.UI;

public class UIProgressBar : UIElement
{
    public float Value { get; set; } = 1f;
    public float MaxValue { get; set; } = 1f;
    public Color FillColor { get; set; } = new(80, 180, 80);
    public Color BackColor { get; set; } = new(30, 30, 30);
    public Color BorderColor { get; set; } = new(70, 110, 70);
    public bool ShowText { get; set; } = true;
    public string? TextFormat { get; set; }

    public float Fraction => MaxValue > 0 ? MathHelper.Clamp(Value / MaxValue, 0f, 1f) : 0f;

    public override void Layout(Rectangle available)
    {
        base.Layout(available);
        if (Height <= 0)
            Bounds = new Rectangle(Bounds.X, Bounds.Y, Bounds.Width, 18);
    }

    public override void Draw(SpriteBatch sb, Texture2D pixel, SpriteFont font)
    {
        if (!Visible) return;

        sb.Draw(pixel, Bounds, BackColor);

        var fillW = (int)(Bounds.Width * Fraction);
        if (fillW > 0)
            sb.Draw(pixel, new Rectangle(Bounds.X, Bounds.Y, fillW, Bounds.Height), FillColor);

        // Border
        sb.Draw(pixel, new Rectangle(Bounds.X, Bounds.Y, Bounds.Width, 1), BorderColor);
        sb.Draw(pixel, new Rectangle(Bounds.X, Bounds.Bottom - 1, Bounds.Width, 1), BorderColor);
        sb.Draw(pixel, new Rectangle(Bounds.X, Bounds.Y, 1, Bounds.Height), BorderColor);
        sb.Draw(pixel, new Rectangle(Bounds.Right - 1, Bounds.Y, 1, Bounds.Height), BorderColor);

        if (ShowText)
        {
            var text = TextFormat != null
                ? string.Format(TextFormat, Value, MaxValue, Fraction * 100)
                : $"{Value:0}/{MaxValue:0}";
            var size = font.MeasureString(text);
            var pos = new Vector2(
                Bounds.X + (Bounds.Width - size.X) / 2,
                Bounds.Y + (Bounds.Height - size.Y) / 2);
            sb.DrawString(font, text, pos, Color.White);
        }
    }
}
