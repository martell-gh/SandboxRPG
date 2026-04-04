using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace MTEngine.UI;

public class UIButton : UIElement
{
    public string Text { get; set; } = "";
    public Color TextColor { get; set; } = Color.White;
    public Color BackColor { get; set; } = new(45, 65, 45);
    public Color HoverColor { get; set; } = new(65, 95, 65);
    public Color PressColor { get; set; } = new(35, 50, 35);
    public Color BorderColor { get; set; } = new(70, 110, 70);

    public event Action? OnClick;

    public bool IsHovered { get; private set; }
    public bool IsPressed { get; private set; }

    public override void Layout(Rectangle available)
    {
        base.Layout(available);
        if (Height <= 0)
            Bounds = new Rectangle(Bounds.X, Bounds.Y, Bounds.Width, 26);
    }

    public override void Draw(SpriteBatch sb, Texture2D pixel, SpriteFont font)
    {
        if (!Visible) return;

        var bg = IsPressed ? PressColor : IsHovered ? HoverColor : BackColor;
        sb.Draw(pixel, Bounds, bg);

        // Border
        sb.Draw(pixel, new Rectangle(Bounds.X, Bounds.Y, Bounds.Width, 1), BorderColor);
        sb.Draw(pixel, new Rectangle(Bounds.X, Bounds.Bottom - 1, Bounds.Width, 1), BorderColor);
        sb.Draw(pixel, new Rectangle(Bounds.X, Bounds.Y, 1, Bounds.Height), BorderColor);
        sb.Draw(pixel, new Rectangle(Bounds.Right - 1, Bounds.Y, 1, Bounds.Height), BorderColor);

        if (!string.IsNullOrEmpty(Text))
        {
            var size = font.MeasureString(Text);
            var pos = new Vector2(
                Bounds.X + (Bounds.Width - size.X) / 2,
                Bounds.Y + (Bounds.Height - size.Y) / 2);
            sb.DrawString(font, Text, pos, Enabled ? TextColor : Color.Gray);
        }
    }

    public override bool HandleClick(Point mousePos)
    {
        if (!Visible || !Enabled || !Bounds.Contains(mousePos)) return false;
        IsPressed = true;
        return true;
    }

    public override bool HandleRelease(Point mousePos)
    {
        if (IsPressed)
        {
            IsPressed = false;
            if (Bounds.Contains(mousePos))
            {
                OnClick?.Invoke();
                return true;
            }
        }
        return false;
    }

    public override bool HandleHover(Point mousePos)
    {
        IsHovered = Visible && Enabled && Bounds.Contains(mousePos);
        return IsHovered;
    }
}
