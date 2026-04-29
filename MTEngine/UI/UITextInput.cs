using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace MTEngine.UI;

public class UITextInput : UIElement
{
    public string Text { get; set; } = "";
    public string Placeholder { get; set; } = "";
    public int MaxLength { get; set; } = 256;

    public Color? OverrideTextColor { get; set; }
    public Color? OverrideBackColor { get; set; }
    public Color? OverrideBorderColor { get; set; }
    public Color? OverrideFocusBorderColor { get; set; }

    public Color TextColor { get; set; } = Color.White;
    public Color PlaceholderColor { get; set; } = Color.Gray;
    public Color BackColor { get; set; } = new(20, 24, 28);
    public Color BorderColor { get; set; } = new(70, 70, 110);
    public Color FocusBorderColor { get; set; } = new(100, 160, 100);

    public UITheme? Theme { get; set; }

    public bool IsFocused { get; set; }
    public event Action<string>? OnSubmit;
    public event Action<string>? OnChanged;

    public override void Layout(Rectangle available)
    {
        base.Layout(available);
        if (Height <= 0)
        {
            var h = Theme?.InputDefaultHeight ?? 24;
            Bounds = new Rectangle(Bounds.X, Bounds.Y, Bounds.Width, h);
        }
    }

    public override void Draw(SpriteBatch sb, Texture2D pixel, SpriteFont font)
    {
        if (!Visible) return;

        var backColor = OverrideBackColor ?? Theme?.InputBackColor ?? BackColor;
        var borderColor = IsFocused
            ? (OverrideFocusBorderColor ?? Theme?.InputFocusBorderColor ?? FocusBorderColor)
            : (OverrideBorderColor ?? Theme?.InputBorderColor ?? BorderColor);
        var textColor = OverrideTextColor ?? Theme?.InputTextColor ?? TextColor;
        var placeholderColor = Theme?.InputPlaceholderColor ?? PlaceholderColor;

        sb.Draw(pixel, Bounds, backColor);
        UIDrawHelper.DrawBorder(sb, pixel, Bounds, borderColor);

        var displayText = string.IsNullOrEmpty(Text) ? Placeholder : Text;
        var color = string.IsNullOrEmpty(Text) ? placeholderColor : textColor;

        var textPos = new Vector2(Bounds.X + 4, Bounds.Y + (Bounds.Height - 14) / 2);
        sb.DrawString(font, displayText, new Vector2(MathF.Round(textPos.X), MathF.Round(textPos.Y)), color);

        // Cursor blink
        if (IsFocused && (int)(DateTime.Now.Millisecond / 500) % 2 == 0)
        {
            var cursorX = Bounds.X + 4 + (int)font.MeasureString(Text).X;
            sb.Draw(pixel, new Rectangle(cursorX, Bounds.Y + 4, 1, Bounds.Height - 8), textColor);
        }
    }

    public override bool HandleClick(Point mousePos)
    {
        if (!Visible || !Enabled) return false;
        IsFocused = Bounds.Contains(mousePos);
        return IsFocused;
    }

    public override bool HandleTextInput(char c)
    {
        if (!IsFocused) return false;

        if (c == '\b')
        {
            if (Text.Length > 0)
            {
                Text = Text[..^1];
                OnChanged?.Invoke(Text);
            }
            return true;
        }

        if (c == '\r' || c == '\n')
        {
            OnSubmit?.Invoke(Text);
            return true;
        }

        if (c == '\t' || char.IsControl(c)) return false;

        if (Text.Length < MaxLength)
        {
            Text += c;
            OnChanged?.Invoke(Text);
        }
        return true;
    }

    public override bool HandleKeyPress(Keys key)
    {
        if (!IsFocused) return false;
        if (key == Keys.Escape)
        {
            IsFocused = false;
            return true;
        }
        return false;
    }
}
