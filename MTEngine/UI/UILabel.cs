using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Text;

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
        if (!Visible || string.IsNullOrEmpty(Text))
            return;

        var wrapped = WrapText(font, Text, Math.Max(1, Bounds.Width), Scale);
        var lineHeight = Math.Max(1, (int)MathF.Ceiling(font.LineSpacing * Scale));
        var maxLines = Bounds.Height > 0
            ? Math.Max(1, Bounds.Height / lineHeight)
            : int.MaxValue;

        var lines = wrapped.Split('\n');
        for (int i = 0; i < lines.Length && i < maxLines; i++)
        {
            sb.DrawString(
                font,
                lines[i],
                new Vector2(Bounds.X, Bounds.Y + i * lineHeight),
                Color,
                0f,
                Vector2.Zero,
                Scale,
                SpriteEffects.None,
                0f);
        }
    }

    private static string WrapText(SpriteFont font, string text, int maxWidth, float scale)
    {
        if (string.IsNullOrWhiteSpace(text) || maxWidth <= 0)
            return text;

        var paragraphs = text.Replace("\r\n", "\n").Split('\n');
        var wrapped = new StringBuilder(text.Length + 16);

        for (int p = 0; p < paragraphs.Length; p++)
        {
            var paragraph = paragraphs[p];
            if (string.IsNullOrWhiteSpace(paragraph))
            {
                if (p > 0)
                    wrapped.Append('\n');
                continue;
            }

            var words = paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var line = string.Empty;

            foreach (var word in words)
            {
                var candidate = string.IsNullOrEmpty(line) ? word : $"{line} {word}";
                if (font.MeasureString(candidate).X * scale <= maxWidth)
                {
                    line = candidate;
                    continue;
                }

                if (!string.IsNullOrEmpty(line))
                    wrapped.AppendLine(line);

                line = word;
            }

            if (!string.IsNullOrEmpty(line))
                wrapped.Append(line);

            if (p < paragraphs.Length - 1)
                wrapped.Append('\n');
        }

        return wrapped.ToString();
    }
}
