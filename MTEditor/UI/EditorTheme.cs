using System;
using System.IO;
using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace MTEditor.UI;

/// <summary>
/// UE-inspired dark theme. Holds the color palette, TTF-based fonts at fixed
/// pixel sizes, and common drawing helpers so every editor panel has the same
/// look and never relies on blurry SpriteFont scaling.
/// </summary>
public static class EditorTheme
{
    // ── Color palette (UE-ish dark theme) ─────────────────────────────
    public static readonly Color Bg             = new(22, 22, 24);
    public static readonly Color BgDeep         = new(14, 14, 16);
    public static readonly Color Panel          = new(34, 34, 38);
    public static readonly Color PanelAlt       = new(42, 42, 46);
    public static readonly Color PanelHover     = new(52, 52, 58);
    public static readonly Color PanelActive    = new(62, 62, 70);
    public static readonly Color Border         = new(12, 12, 14);
    public static readonly Color BorderSoft     = new(60, 60, 66);
    public static readonly Color BorderStrong   = new(80, 80, 88);
    public static readonly Color Divider        = new(10, 10, 12);

    public static readonly Color Text           = new(226, 228, 232);
    public static readonly Color TextDim        = new(160, 164, 172);
    public static readonly Color TextMuted      = new(110, 114, 122);
    public static readonly Color TextDisabled   = new(78, 82, 90);

    // Accent blue — UE-ish "editor" accent
    public static readonly Color Accent         = new(0, 122, 204);
    public static readonly Color AccentHover    = new(28, 151, 234);
    public static readonly Color AccentDim      = new(0, 90, 158);

    public static readonly Color Success        = new(114, 188, 120);
    public static readonly Color Warning        = new(232, 173, 72);
    public static readonly Color Error          = new(220, 86, 86);

    // ── Fonts (pixel sizes — rasterized on demand, always crisp) ──────
    private static FontSystem? _fontSystem;
    public static SpriteFontBase Tiny   { get; private set; } = null!;   // 10px — badges
    public static SpriteFontBase Small  { get; private set; } = null!;   // 11px — menubar
    public static SpriteFontBase Body   { get; private set; } = null!;   // 12px — default
    public static SpriteFontBase Medium { get; private set; } = null!;   // 13px — labels
    public static SpriteFontBase Title  { get; private set; } = null!;   // 15px — panel titles
    public static SpriteFontBase Header { get; private set; } = null!;   // 20px — big headings

    // ── Shared 1×1 pixel texture ─────────────────────────────────────
    public static Texture2D Pixel { get; private set; } = null!;

    private static bool _initialized;

    public static void Initialize(GraphicsDevice graphics)
    {
        if (_initialized) return;
        _initialized = true;

        Pixel = new Texture2D(graphics, 1, 1);
        Pixel.SetData(new[] { Color.White });

        _fontSystem = new FontSystem(new FontSystemSettings
        {
            FontResolutionFactor = 2f,
            KernelWidth = 2,
            KernelHeight = 2,
            TextureWidth = 1024,
            TextureHeight = 1024,
        });

        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Content", "Fonts", "Inter-Regular.ttf"),
            Path.Combine(AppContext.BaseDirectory, "Content", "Fonts", "Inter-Regular.otf"),
        };

        byte[]? data = null;
        foreach (var path in candidates)
        {
            if (File.Exists(path))
            {
                data = File.ReadAllBytes(path);
                break;
            }
        }

        if (data == null)
            throw new FileNotFoundException(
                "EditorTheme: could not locate Inter-Regular.ttf under Content/Fonts/. " +
                "Make sure the font is copied to the output directory.");

        _fontSystem.AddFont(data);

        Tiny   = _fontSystem.GetFont(10);
        Small  = _fontSystem.GetFont(11);
        Body   = _fontSystem.GetFont(12);
        Medium = _fontSystem.GetFont(13);
        Title  = _fontSystem.GetFont(15);
        Header = _fontSystem.GetFont(20);
    }

    // ── Drawing helpers ──────────────────────────────────────────────

    public static void FillRect(SpriteBatch sb, Rectangle rect, Color color)
        => sb.Draw(Pixel, rect, color);

    public static void DrawBorder(SpriteBatch sb, Rectangle rect, Color color, int thickness = 1)
    {
        sb.Draw(Pixel, new Rectangle(rect.X, rect.Y, rect.Width, thickness), color);
        sb.Draw(Pixel, new Rectangle(rect.X, rect.Bottom - thickness, rect.Width, thickness), color);
        sb.Draw(Pixel, new Rectangle(rect.X, rect.Y, thickness, rect.Height), color);
        sb.Draw(Pixel, new Rectangle(rect.Right - thickness, rect.Y, thickness, rect.Height), color);
    }

    /// <summary>Standard UE-style panel: fill + 1px dark border + subtle highlight on top.</summary>
    public static void DrawPanel(SpriteBatch sb, Rectangle rect, Color? fill = null, Color? border = null)
    {
        FillRect(sb, rect, fill ?? Panel);
        // top highlight
        sb.Draw(Pixel, new Rectangle(rect.X + 1, rect.Y, rect.Width - 2, 1), new Color(255, 255, 255, 18));
        DrawBorder(sb, rect, border ?? Border, 1);
    }

    public static void DrawShadow(SpriteBatch sb, Rectangle rect, int size = 6)
    {
        for (int i = 0; i < size; i++)
        {
            var alpha = (byte)(80 - i * 12);
            sb.Draw(Pixel,
                new Rectangle(rect.X, rect.Bottom + i, rect.Width, 1),
                new Color((byte)0, (byte)0, (byte)0, alpha));
        }
    }

    /// <summary>Draws a flat UE-style button with hover/active/disabled states.</summary>
    public static void DrawButton(SpriteBatch sb, Rectangle rect, string label,
                                  SpriteFontBase font, bool hovered, bool active, bool enabled = true)
    {
        Color fill, textColor, border;
        if (!enabled)
        {
            fill = Panel;
            textColor = TextDisabled;
            border = Border;
        }
        else if (active)
        {
            fill = hovered ? AccentHover : Accent;
            textColor = Color.White;
            border = AccentDim;
        }
        else if (hovered)
        {
            fill = PanelHover;
            textColor = Text;
            border = BorderSoft;
        }
        else
        {
            fill = Panel;
            textColor = TextDim;
            border = Border;
        }

        FillRect(sb, rect, fill);
        DrawBorder(sb, rect, border, 1);
        var size = font.MeasureString(label);
        var x = rect.X + (rect.Width - size.X) / 2f;
        var y = rect.Y + (rect.Height - size.Y) / 2f - 1;
        font.DrawText(sb, label, new Vector2(MathF.Round(x), MathF.Round(y)), textColor);
    }

    public static void DrawText(SpriteBatch sb, SpriteFontBase font, string text, Vector2 pos, Color color)
        => font.DrawText(sb, text, new Vector2(MathF.Round(pos.X), MathF.Round(pos.Y)), color);

    public static Vector2 Measure(SpriteFontBase font, string text) => font.MeasureString(text);

    public static void DrawVerticalAccentBar(SpriteBatch sb, Rectangle rect, Color color)
        => sb.Draw(Pixel, rect, color);

    public static void DrawSeparator(SpriteBatch sb, int x, int y, int length, bool vertical = false)
    {
        if (vertical)
            sb.Draw(Pixel, new Rectangle(x, y, 1, length), BorderSoft);
        else
            sb.Draw(Pixel, new Rectangle(x, y, length, 1), BorderSoft);
    }
}
