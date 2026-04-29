using System.Xml.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MTEngine.Core;

namespace MTEngine.UI;

/// <summary>
/// UI theme loaded from XML. Controls all visual appearance of the UI system:
/// window backgrounds (9-slice), buttons, progress bars, text inputs, fonts.
/// </summary>
public class UITheme
{
    // ── Window ─────────────────────────────────────────────────────
    public NineSlice? WindowBackground { get; set; }
    public Color WindowBackgroundTint { get; set; } = Color.White;
    public Color WindowBackgroundFlatColor { get; set; } = new(18, 22, 28, 220);

    public NineSlice? TitleBarBackground { get; set; }
    public Color TitleBarColor { get; set; } = new(35, 55, 35);
    public Color TitleBarTint { get; set; } = Color.White;
    public bool TitleBarVisible { get; set; } = true;
    public Color TitleTextColor { get; set; } = Color.SkyBlue;
    public float TitleTextScale { get; set; } = 1f;
    public int TitleBarHeight { get; set; } = 34;

    public Color WindowBorderColor { get; set; } = new(70, 110, 70);
    public int WindowBorderThickness { get; set; } = 1;

    public Texture2D? CloseButtonTexture { get; set; }
    public Color CloseButtonColor { get; set; } = new(120, 40, 40);
    public Color CloseButtonHoverColor { get; set; } = new(180, 50, 50);
    public Color CloseButtonTint { get; set; } = Color.White;
    public Color CloseButtonHoverTint { get; set; } = Color.White;
    public int CloseButtonSize { get; set; } = 18;

    public Color ShadowColor { get; set; } = Color.Black;
    public float ShadowAlpha { get; set; } = 0.35f;
    public int ShadowOffsetX { get; set; } = 4;
    public int ShadowOffsetY { get; set; } = 4;

    // ── Button ─────────────────────────────────────────────────────
    public NineSlice? ButtonBackground { get; set; }
    public NineSlice? ButtonHoverBackground { get; set; }
    public NineSlice? ButtonPressBackground { get; set; }

    public Color ButtonBackColor { get; set; } = new(45, 65, 45);
    public Color ButtonHoverColor { get; set; } = new(65, 101, 65);
    public Color ButtonPressColor { get; set; } = new(35, 50, 35);
    public Color ButtonDisabledColor { get; set; } = new(30, 30, 30);
    public Color ButtonBorderColor { get; set; } = new(70, 110, 70);
    public Color ButtonHoverBorderColor { get; set; } = new(90, 140, 90);
    public Color ButtonPressBorderColor { get; set; } = new(50, 80, 50);
    public Color ButtonTextColor { get; set; } = Color.White;
    public Color ButtonHoverTextColor { get; set; } = Color.White;
    public Color ButtonPressTextColor { get; set; } = new(200, 200, 200);
    public Color ButtonDisabledTextColor { get; set; } = Color.Gray;
    public int ButtonBorderThickness { get; set; } = 1;
    public int ButtonDefaultHeight { get; set; } = 28;

    // ── ProgressBar ────────────────────────────────────────────────
    public Color ProgressBackColor { get; set; } = new(30, 30, 30);
    public Color ProgressBorderColor { get; set; } = new(70, 110, 70);
    public Color ProgressTextColor { get; set; } = Color.White;
    public int ProgressDefaultHeight { get; set; } = 18;

    // ── TextInput ──────────────────────────────────────────────────
    public Color InputBackColor { get; set; } = new(20, 24, 28);
    public Color InputBorderColor { get; set; } = new(70, 70, 110);
    public Color InputFocusBorderColor { get; set; } = new(100, 160, 100);
    public Color InputTextColor { get; set; } = Color.White;
    public Color InputPlaceholderColor { get; set; } = Color.Gray;
    public int InputDefaultHeight { get; set; } = 24;

    // ── Separator ──────────────────────────────────────────────────
    public Color SeparatorColor { get; set; } = new(70, 110, 70, 100);

    // ── ScrollBar ──────────────────────────────────────────────────
    public Color ScrollBarColor { get; set; } = new(70, 110, 70, 120);
    public int ScrollBarWidth { get; set; } = 4;

    // ── Font ───────────────────────────────────────────────────────
    public string FontName { get; set; } = "DefaultFont";

    // ── Loading ────────────────────────────────────────────────────

    /// <summary>Load theme from an XML file.</summary>
    public static UITheme LoadFromFile(string path, AssetManager assets)
    {
        if (!File.Exists(path))
        {
            Console.WriteLine($"[UITheme] Theme file not found: {path}, using defaults");
            return new UITheme();
        }

        var doc = XDocument.Load(path);
        return Parse(doc, assets, Path.GetDirectoryName(path) ?? "");
    }

    /// <summary>Create a default theme (no textures, flat colors).</summary>
    public static UITheme CreateDefault() => new();

    private static UITheme Parse(XDocument doc, AssetManager assets, string basePath)
    {
        var root = doc.Root;
        if (root == null || root.Name.LocalName != "Theme")
            return new UITheme();

        var theme = new UITheme();

        // ── Window ──
        var window = root.Element("Window");
        if (window != null)
        {
            var bg = window.Element("Background");
            if (bg != null)
            {
                theme.WindowBackground = LoadNineSlice(bg, assets, basePath);
                theme.WindowBackgroundTint = AttrColor(bg, "Tint", Color.White);
                theme.WindowBackgroundFlatColor = AttrColor(bg, "FlatColor", theme.WindowBackgroundFlatColor);
            }

            var title = window.Element("TitleBar");
            if (title != null)
            {
                theme.TitleBarBackground = LoadNineSlice(title, assets, basePath);
                theme.TitleBarColor = AttrColor(title, "Color", theme.TitleBarColor);
                theme.TitleBarTint = AttrColor(title, "Tint", Color.White);
                theme.TitleBarHeight = AttrInt(title, "Height", theme.TitleBarHeight);
                theme.TitleBarVisible = AttrBool(title, "Visible", theme.TitleBarVisible);
            }

            var titleText = window.Element("TitleText");
            if (titleText != null)
            {
                theme.TitleTextColor = AttrColor(titleText, "Color", theme.TitleTextColor);
                theme.TitleTextScale = AttrFloat(titleText, "Scale", theme.TitleTextScale);
            }

            var border = window.Element("Border");
            if (border != null)
            {
                theme.WindowBorderColor = AttrColor(border, "Color", theme.WindowBorderColor);
                theme.WindowBorderThickness = AttrInt(border, "Thickness", theme.WindowBorderThickness);
            }

            var close = window.Element("CloseButton");
            if (close != null)
            {
                theme.CloseButtonColor = AttrColor(close, "Color", theme.CloseButtonColor);
                theme.CloseButtonHoverColor = AttrColor(close, "HoverColor", theme.CloseButtonHoverColor);
                theme.CloseButtonTint = AttrColor(close, "Tint", theme.CloseButtonTint);
                theme.CloseButtonHoverTint = AttrColor(close, "HoverTint", theme.CloseButtonHoverTint);
                theme.CloseButtonSize = AttrInt(close, "Size", theme.CloseButtonSize);

                var texPath = close.Attribute("Texture")?.Value;
                if (!string.IsNullOrWhiteSpace(texPath))
                {
                    var fullPath = Path.IsPathRooted(texPath) ? texPath : Path.Combine(basePath, texPath);
                    theme.CloseButtonTexture = assets.LoadFromFile(fullPath);
                }
            }

            var shadow = window.Element("Shadow");
            if (shadow != null)
            {
                theme.ShadowColor = AttrColor(shadow, "Color", theme.ShadowColor);
                theme.ShadowAlpha = AttrFloat(shadow, "Alpha", theme.ShadowAlpha);
                theme.ShadowOffsetX = AttrInt(shadow, "OffsetX", theme.ShadowOffsetX);
                theme.ShadowOffsetY = AttrInt(shadow, "OffsetY", theme.ShadowOffsetY);
            }
        }

        // ── Button ──
        var button = root.Element("Button");
        if (button != null)
        {
            var normal = button.Element("Normal");
            if (normal != null)
            {
                theme.ButtonBackground = LoadNineSlice(normal, assets, basePath);
                theme.ButtonBackColor = AttrColor(normal, "BackColor", theme.ButtonBackColor);
                theme.ButtonBorderColor = AttrColor(normal, "BorderColor", theme.ButtonBorderColor);
                theme.ButtonTextColor = AttrColor(normal, "TextColor", theme.ButtonTextColor);
            }

            var hover = button.Element("Hover");
            if (hover != null)
            {
                theme.ButtonHoverBackground = LoadNineSlice(hover, assets, basePath);
                theme.ButtonHoverColor = AttrColor(hover, "BackColor", theme.ButtonHoverColor);
                theme.ButtonHoverBorderColor = AttrColor(hover, "BorderColor", theme.ButtonHoverBorderColor);
                theme.ButtonHoverTextColor = AttrColor(hover, "TextColor", theme.ButtonHoverTextColor);
            }

            var pressed = button.Element("Pressed");
            if (pressed != null)
            {
                theme.ButtonPressBackground = LoadNineSlice(pressed, assets, basePath);
                theme.ButtonPressColor = AttrColor(pressed, "BackColor", theme.ButtonPressColor);
                theme.ButtonPressBorderColor = AttrColor(pressed, "BorderColor", theme.ButtonPressBorderColor);
                theme.ButtonPressTextColor = AttrColor(pressed, "TextColor", theme.ButtonPressTextColor);
            }

            var disabled = button.Element("Disabled");
            if (disabled != null)
            {
                theme.ButtonDisabledColor = AttrColor(disabled, "BackColor", theme.ButtonDisabledColor);
                theme.ButtonDisabledTextColor = AttrColor(disabled, "TextColor", theme.ButtonDisabledTextColor);
            }

            theme.ButtonBorderThickness = AttrInt(button, "BorderThickness", theme.ButtonBorderThickness);
            theme.ButtonDefaultHeight = AttrInt(button, "DefaultHeight", theme.ButtonDefaultHeight);
        }

        // ── ProgressBar ──
        var progress = root.Element("ProgressBar");
        if (progress != null)
        {
            theme.ProgressBackColor = AttrColor(progress, "BackColor", theme.ProgressBackColor);
            theme.ProgressBorderColor = AttrColor(progress, "BorderColor", theme.ProgressBorderColor);
            theme.ProgressTextColor = AttrColor(progress, "TextColor", theme.ProgressTextColor);
            theme.ProgressDefaultHeight = AttrInt(progress, "DefaultHeight", theme.ProgressDefaultHeight);
        }

        // ── TextInput ──
        var input = root.Element("TextInput");
        if (input != null)
        {
            theme.InputBackColor = AttrColor(input, "BackColor", theme.InputBackColor);
            theme.InputBorderColor = AttrColor(input, "BorderColor", theme.InputBorderColor);
            theme.InputFocusBorderColor = AttrColor(input, "FocusBorderColor", theme.InputFocusBorderColor);
            theme.InputTextColor = AttrColor(input, "TextColor", theme.InputTextColor);
            theme.InputPlaceholderColor = AttrColor(input, "PlaceholderColor", theme.InputPlaceholderColor);
            theme.InputDefaultHeight = AttrInt(input, "DefaultHeight", theme.InputDefaultHeight);
        }

        // ── Separator ──
        var separator = root.Element("Separator");
        if (separator != null)
            theme.SeparatorColor = AttrColor(separator, "Color", theme.SeparatorColor);

        // ── ScrollBar ──
        var scrollBar = root.Element("ScrollBar");
        if (scrollBar != null)
        {
            theme.ScrollBarColor = AttrColor(scrollBar, "Color", theme.ScrollBarColor);
            theme.ScrollBarWidth = AttrInt(scrollBar, "Width", theme.ScrollBarWidth);
        }

        // ── Font ──
        var font = root.Element("Font");
        if (font != null)
            theme.FontName = Attr(font, "Name", theme.FontName);

        return theme;
    }

    // ── NineSlice loader ───────────────────────────────────────────

    private static NineSlice? LoadNineSlice(XElement el, AssetManager assets, string basePath)
    {
        var texturePath = el.Attribute("Texture")?.Value;
        if (string.IsNullOrWhiteSpace(texturePath)) return null;

        // Resolve relative to theme file directory
        var fullPath = Path.IsPathRooted(texturePath)
            ? texturePath
            : Path.Combine(basePath, texturePath);

        var texture = assets.LoadFromFile(fullPath);
        if (texture == null)
        {
            Console.WriteLine($"[UITheme] Failed to load texture: {fullPath}");
            return null;
        }

        // Parse NineSlice margins: "all" or "left,top,right,bottom"
        var sliceStr = el.Attribute("NineSlice")?.Value;
        if (string.IsNullOrWhiteSpace(sliceStr))
        {
            // Default: split texture into thirds
            var border = Math.Max(1, Math.Min(texture.Width, texture.Height) / 3);
            var ns = new NineSlice(texture, border);
            ns.Scale = AttrFloat(el, "Scale", 1f);
            return ns;
        }

        var parts = sliceStr.Split(',').Select(s => int.TryParse(s.Trim(), out var v) ? v : 0).ToArray();
        NineSlice slice;
        if (parts.Length == 1)
            slice = new NineSlice(texture, parts[0]);
        else if (parts.Length == 4)
            slice = new NineSlice(texture, parts[0], parts[1], parts[2], parts[3]);
        else
            slice = new NineSlice(texture, Math.Min(texture.Width, texture.Height) / 3);

        slice.Scale = AttrFloat(el, "Scale", 1f);
        return slice;
    }

    // ── Attribute helpers ──────────────────────────────────────────

    private static string Attr(XElement xml, string name, string fallback)
        => xml.Attribute(name)?.Value ?? fallback;

    private static int AttrInt(XElement xml, string name, int fallback)
        => int.TryParse(xml.Attribute(name)?.Value, out var v) ? v : fallback;

    private static bool AttrBool(XElement xml, string name, bool fallback)
        => bool.TryParse(xml.Attribute(name)?.Value, out var v) ? v : fallback;

    private static float AttrFloat(XElement xml, string name, float fallback)
        => float.TryParse(xml.Attribute(name)?.Value, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : fallback;

    private static Color AttrColor(XElement xml, string name, Color fallback)
    {
        var val = xml.Attribute(name)?.Value;
        return val != null ? AssetManager.ParseHexColor(val, fallback) : fallback;
    }
}
