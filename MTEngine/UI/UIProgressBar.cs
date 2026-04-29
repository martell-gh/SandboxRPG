using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace MTEngine.UI;

public class UIProgressBar : UIElement
{
    public float Value { get; set; } = 1f;
    public float MaxValue { get; set; } = 1f;
    public Color FillColor { get; set; } = new(80, 180, 80);
    public Color? OverrideBackColor { get; set; }
    public Color? OverrideBorderColor { get; set; }
    public Color BackColor { get; set; } = new(30, 30, 30);
    public Color BorderColor { get; set; } = new(70, 110, 70);
    public bool ShowText { get; set; } = true;
    public string? TextFormat { get; set; }

    public UITheme? Theme { get; set; }

    public float Fraction => MaxValue > 0 ? MathHelper.Clamp(Value / MaxValue, 0f, 1f) : 0f;

    public override void Layout(Rectangle available)
    {
        base.Layout(available);
        if (Height <= 0)
        {
            var h = Theme?.ProgressDefaultHeight ?? 18;
            Bounds = new Rectangle(Bounds.X, Bounds.Y, Bounds.Width, h);
        }
    }

    public override void Draw(SpriteBatch sb, Texture2D pixel, SpriteFont font)
    {
        if (!Visible) return;

        var backColor = OverrideBackColor ?? Theme?.ProgressBackColor ?? BackColor;
        var borderColor = OverrideBorderColor ?? Theme?.ProgressBorderColor ?? BorderColor;
        var textColor = Theme?.ProgressTextColor ?? Color.White;

        sb.Draw(pixel, Bounds, backColor);

        var fillW = (int)(Bounds.Width * Fraction);
        if (fillW > 0)
            sb.Draw(pixel, new Rectangle(Bounds.X, Bounds.Y, fillW, Bounds.Height), FillColor);

        UIDrawHelper.DrawBorder(sb, pixel, Bounds, borderColor);

        if (ShowText)
        {
            var text = TextFormat != null
                ? string.Format(TextFormat, Value, MaxValue, Fraction * 100)
                : $"{Value:0}/{MaxValue:0}";
            var size = font.MeasureString(text);
            var pos = new Vector2(
                MathF.Round(Bounds.X + (Bounds.Width - size.X) / 2),
                MathF.Round(Bounds.Y + (Bounds.Height - size.Y) / 2));
            sb.DrawString(font, text, pos, textColor);
        }
    }
}
