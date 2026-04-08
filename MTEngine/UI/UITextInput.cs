using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace MTEngine.UI;

public class UITextInput : UIElement
{
    public string Text { get; set; } = "";
    public string Placeholder { get; set; } = "";
    public int MaxLength { get; set; } = 256;
    public Color TextColor { get; set; } = Color.White;
    public Color PlaceholderColor { get; set; } = Color.Gray;
    public Color BackColor { get; set; } = new(20, 24, 28);
    public Color BorderColor { get; set; } = new(70, 70, 110);
    public Color FocusBorderColor { get; set; } = new(100, 160, 100);

    public bool IsFocused { get; set; }
    public event Action<string>? OnSubmit;
    public event Action<string>? OnChanged;

    public override void Layout(Rectangle available)
    {
        base.Layout(available);
        if (Height <= 0)
            Bounds = new Rectangle(Bounds.X, Bounds.Y, Bounds.Width, 24);
    }

    public override void Draw(SpriteBatch sb, Texture2D pixel, SpriteFont font)
    {
        if (!Visible) return;

        sb.Draw(pixel, Bounds, BackColor);

        var border = IsFocused ? FocusBorderColor : BorderColor;
        sb.Draw(pixel, new Rectangle(Bounds.X, Bounds.Y, Bounds.Width, 1), border);
        sb.Draw(pixel, new Rectangle(Bounds.X, Bounds.Bottom - 1, Bounds.Width, 1), border);
        sb.Draw(pixel, new Rectangle(Bounds.X, Bounds.Y, 1, Bounds.Height), border);
        sb.Draw(pixel, new Rectangle(Bounds.Right - 1, Bounds.Y, 1, Bounds.Height), border);

        var displayText = string.IsNullOrEmpty(Text) ? Placeholder : Text;
        var color = string.IsNullOrEmpty(Text) ? PlaceholderColor : TextColor;

        var textPos = new Vector2(Bounds.X + 4, Bounds.Y + (Bounds.Height - 14) / 2);
        sb.DrawString(font, displayText, textPos, color);

        // Cursor blink
        if (IsFocused && (int)(DateTime.Now.Millisecond / 500) % 2 == 0)
        {
            var cursorX = Bounds.X + 4 + (int)font.MeasureString(Text).X;
            sb.Draw(pixel, new Rectangle(cursorX, Bounds.Y + 4, 1, Bounds.Height - 8), TextColor);
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
