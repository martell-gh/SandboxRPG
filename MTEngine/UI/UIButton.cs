using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MTEngine.Core;

namespace MTEngine.UI;

public class UIButton : UIElement
{
    public string Text { get; set; } = "";
    public float TextScale { get; set; } = 1f;

    // Per-button color overrides (null = use theme)
    public Color? OverrideTextColor { get; set; }
    public Color? OverrideBackColor { get; set; }
    public Color? OverrideHoverColor { get; set; }
    public Color? OverridePressColor { get; set; }
    public Color? OverrideBorderColor { get; set; }
    public Color? OverrideHoverBorderColor { get; set; }

    // Legacy direct colors (used when no theme and no override)
    public Color TextColor { get; set; } = Color.White;
    public Color BackColor { get; set; } = new(45, 65, 45);
    public Color HoverColor { get; set; } = new(65, 95, 65);
    public Color PressColor { get; set; } = new(35, 50, 35);
    public Color BorderColor { get; set; } = new(70, 110, 70);

    // Per-button 9-slice overrides
    public NineSlice? NormalSlice { get; set; }
    public NineSlice? HoverSlice { get; set; }
    public NineSlice? PressSlice { get; set; }

    public Texture2D? BackgroundTexture { get; set; }
    public bool TileBackground { get; set; } = true;

    /// <summary>Theme reference, set by UIManager.</summary>
    public UITheme? Theme { get; set; }

    public event Action? OnClick;

    public bool IsHovered { get; private set; }
    public bool IsPressed { get; private set; }

    public override void Layout(Rectangle available)
    {
        base.Layout(available);
        if (Height <= 0)
        {
            var h = Theme?.ButtonDefaultHeight ?? 28;
            Bounds = new Rectangle(Bounds.X, Bounds.Y, Bounds.Width, h);
        }
    }

    public override void Draw(SpriteBatch sb, Texture2D pixel, SpriteFont font)
    {
        if (!Visible) return;

        // Resolve colors from theme or direct values
        Color bg, border, text;
        NineSlice? slice;

        if (!Enabled)
        {
            bg = Theme?.ButtonDisabledColor ?? new Color(30, 30, 30);
            border = Theme?.ButtonBorderColor ?? BorderColor;
            text = Theme?.ButtonDisabledTextColor ?? Color.Gray;
            slice = null;
        }
        else if (IsPressed)
        {
            bg = OverridePressColor ?? Theme?.ButtonPressColor ?? PressColor;
            border = Theme?.ButtonPressBorderColor ?? BorderColor;
            text = Theme?.ButtonPressTextColor ?? TextColor;
            slice = PressSlice ?? Theme?.ButtonPressBackground;
        }
        else if (IsHovered)
        {
            bg = OverrideHoverColor ?? Theme?.ButtonHoverColor ?? HoverColor;
            border = OverrideHoverBorderColor ?? Theme?.ButtonHoverBorderColor ?? BorderColor;
            text = Theme?.ButtonHoverTextColor ?? TextColor;
            slice = HoverSlice ?? Theme?.ButtonHoverBackground;
        }
        else
        {
            bg = OverrideBackColor ?? Theme?.ButtonBackColor ?? BackColor;
            border = OverrideBorderColor ?? Theme?.ButtonBorderColor ?? BorderColor;
            text = OverrideTextColor ?? Theme?.ButtonTextColor ?? TextColor;
            slice = NormalSlice ?? Theme?.ButtonBackground;
        }

        // Draw background
        UIDrawHelper.DrawBackground(sb, pixel, Bounds, bg, BackgroundTexture, bg, TileBackground, slice, bg);

        // Draw border (skip if 9-slice provides it)
        if (slice == null)
        {
            var borderTh = Theme?.ButtonBorderThickness ?? 1;
            UIDrawHelper.DrawBorder(sb, pixel, Bounds, border, borderTh);
        }

        // Draw text
        var displayText = LocalizationManager.T(Text);
        if (!string.IsNullOrEmpty(displayText))
        {
            var size = font.MeasureString(displayText) * TextScale;
            var pos = new Vector2(
                MathF.Round(Bounds.X + (Bounds.Width - size.X) / 2),
                MathF.Round(Bounds.Y + (Bounds.Height - size.Y) / 2));
            sb.DrawString(font, displayText, pos, text,
                0f, Vector2.Zero, TextScale, SpriteEffects.None, 0f);
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
