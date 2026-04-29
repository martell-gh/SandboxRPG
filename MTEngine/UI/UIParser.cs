using System.Xml.Linq;
using Microsoft.Xna.Framework;
using MTEngine.Core;

namespace MTEngine.UI;

/// <summary>
/// Parses XML files into XmlWindow instances.
/// All elements inherit theme from the window.
///
/// Supported elements:
///   Window           — root, becomes XmlWindow
///   Label            — text label
///   Button           — clickable button (themed with hover/press states)
///   Image            — texture image
///   Panel            — container (Vertical/Horizontal)
///   ScrollPanel      — scrollable vertical panel
///   ProgressBar      — value bar
///   TextInput        — editable text field
///   Separator        — horizontal line
///
/// Common attributes:
///   Name, Width, Height, Margin, HAlign, VAlign, Visible, Tooltip
/// </summary>
public static class UIParser
{
    public static XmlWindow LoadFromFile(string path, UITheme? theme = null)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"UI XML not found: {path}");

        var doc = XDocument.Load(path);
        return Parse(doc, theme);
    }

    public static XmlWindow LoadFromString(string xml, UITheme? theme = null)
    {
        var doc = XDocument.Parse(xml);
        return Parse(doc, theme);
    }

    private static XmlWindow Parse(XDocument doc, UITheme? theme)
    {
        var root = doc.Root ?? throw new Exception("Empty XML document");
        if (root.Name.LocalName != "Window")
            throw new Exception($"Root element must be <Window>, got <{root.Name.LocalName}>");

        var window = new XmlWindow
        {
            Id = Attr(root, "Id", ""),
            Title = Attr(root, "Title", "Window"),
            Width = AttrInt(root, "Width", 300),
            Height = AttrInt(root, "Height", 200),
            Closable = AttrBool(root, "Closable", true),
            Draggable = AttrBool(root, "Draggable", true),
            Theme = theme,
        };

        // Per-window color overrides
        var bgColor = AttrColorNullable(root, "BackColor");
        if (bgColor.HasValue) window.OverrideBackgroundColor = bgColor;

        var titleColor = AttrColorNullable(root, "TitleBarColor");
        if (titleColor.HasValue) window.OverrideTitleBarColor = titleColor;

        var titleTextColor = AttrColorNullable(root, "TitleTextColor");
        if (titleTextColor.HasValue) window.OverrideTitleTextColor = titleTextColor;

        var borderColor = AttrColorNullable(root, "BorderColor");
        if (borderColor.HasValue) window.OverrideBorderColor = borderColor;

        foreach (var childXml in root.Elements())
        {
            var el = ParseElement(childXml, window, theme);
            if (el != null)
                window.Root.Add(el);
        }

        return window;
    }

    private static UIElement? ParseElement(XElement xml, XmlWindow window, UITheme? theme)
    {
        UIElement? el = xml.Name.LocalName switch
        {
            "Label" => ParseLabel(xml),
            "Button" => ParseButton(xml, theme),
            "Image" => ParseImage(xml),
            "Panel" => ParsePanel(xml, window, theme),
            "ScrollPanel" => ParseScrollPanel(xml, window, theme),
            "ProgressBar" => ParseProgressBar(xml, theme),
            "TextInput" => ParseTextInput(xml, theme),
            "Separator" => ParseSeparator(xml, theme),
            _ => null
        };

        if (el == null)
        {
            Console.WriteLine($"[UIParser] Unknown element: <{xml.Name.LocalName}>");
            return null;
        }

        ApplyCommon(xml, el);
        window.Register(el);
        return el;
    }

    // ── Element parsers ─────────────────────────────────────────────

    private static UILabel ParseLabel(XElement xml) => new()
    {
        Text = Attr(xml, "Text", ""),
        Color = AttrColor(xml, "Color", Color.White),
        Scale = AttrFloat(xml, "Scale", 1f),
    };

    private static UIButton ParseButton(XElement xml, UITheme? theme)
    {
        var btn = new UIButton
        {
            Text = Attr(xml, "Text", ""),
            TextScale = AttrFloat(xml, "TextScale", 1f),
            Theme = theme,
        };

        // Per-button color overrides (only set if explicitly specified in XML)
        var textColor = AttrColorNullable(xml, "TextColor");
        if (textColor.HasValue) btn.OverrideTextColor = textColor;

        var backColor = AttrColorNullable(xml, "BackColor");
        if (backColor.HasValue) btn.OverrideBackColor = backColor;

        var hoverColor = AttrColorNullable(xml, "HoverColor");
        if (hoverColor.HasValue) btn.OverrideHoverColor = hoverColor;

        var pressColor = AttrColorNullable(xml, "PressColor");
        if (pressColor.HasValue) btn.OverridePressColor = pressColor;

        var borderColor = AttrColorNullable(xml, "BorderColor");
        if (borderColor.HasValue) btn.OverrideBorderColor = borderColor;

        var hoverBorderColor = AttrColorNullable(xml, "HoverBorderColor");
        if (hoverBorderColor.HasValue) btn.OverrideHoverBorderColor = hoverBorderColor;

        return btn;
    }

    private static UIImage ParseImage(XElement xml) => new()
    {
        TexturePath = AttrNullable(xml, "Texture"),
        Tint = AttrColor(xml, "Tint", Color.White),
    };

    private static UIPanel ParsePanel(XElement xml, XmlWindow window, UITheme? theme)
    {
        var panel = new UIPanel
        {
            Direction = AttrEnum(xml, "Direction", LayoutDirection.Vertical),
            Gap = AttrInt(xml, "Gap", 4),
            Padding = AttrInt(xml, "Padding", 0),
            BackColor = AttrColorNullable(xml, "BackColor"),
        };

        foreach (var childXml in xml.Elements())
        {
            var child = ParseElement(childXml, window, theme);
            if (child != null) panel.Add(child);
        }

        return panel;
    }

    private static UIScrollPanel ParseScrollPanel(XElement xml, XmlWindow window, UITheme? theme)
    {
        var panel = new UIScrollPanel
        {
            Direction = LayoutDirection.Vertical,
            Gap = AttrInt(xml, "Gap", 4),
            Padding = AttrInt(xml, "Padding", 0),
            BackColor = AttrColorNullable(xml, "BackColor"),
            ScrollStep = AttrInt(xml, "ScrollStep", 20),
            Theme = theme,
        };

        foreach (var childXml in xml.Elements())
        {
            var child = ParseElement(childXml, window, theme);
            if (child != null) panel.Add(child);
        }

        return panel;
    }

    private static UIProgressBar ParseProgressBar(XElement xml, UITheme? theme)
    {
        var bar = new UIProgressBar
        {
            Value = AttrFloat(xml, "Value", 1f),
            MaxValue = AttrFloat(xml, "MaxValue", 1f),
            FillColor = AttrColor(xml, "FillColor", new Color(80, 180, 80)),
            ShowText = AttrBool(xml, "ShowText", true),
            TextFormat = AttrNullable(xml, "TextFormat"),
            Theme = theme,
        };

        var backColor = AttrColorNullable(xml, "BackColor");
        if (backColor.HasValue) bar.OverrideBackColor = backColor;

        var borderColor = AttrColorNullable(xml, "BorderColor");
        if (borderColor.HasValue) bar.OverrideBorderColor = borderColor;

        return bar;
    }

    private static UITextInput ParseTextInput(XElement xml, UITheme? theme) => new()
    {
        Placeholder = Attr(xml, "Placeholder", ""),
        MaxLength = AttrInt(xml, "MaxLength", 256),
        Theme = theme,
    };

    private static UISeparator ParseSeparator(XElement xml, UITheme? theme)
    {
        var sep = new UISeparator
        {
            Thickness = AttrInt(xml, "Thickness", 1),
            Theme = theme,
        };

        var color = AttrColorNullable(xml, "Color");
        if (color.HasValue) sep.OverrideColor = color;

        return sep;
    }

    // ── Common attributes ───────────────────────────────────────────

    private static void ApplyCommon(XElement xml, UIElement el)
    {
        el.Name = AttrNullable(xml, "Name");
        el.Width = AttrInt(xml, "Width", 0);
        el.Height = AttrInt(xml, "Height", 0);
        el.Visible = AttrBool(xml, "Visible", true);
        el.Enabled = AttrBool(xml, "Enabled", true);
        el.Tooltip = AttrNullable(xml, "Tooltip");
        el.HAlign = AttrEnum(xml, "HAlign", HAlign.Left);
        el.VAlign = AttrEnum(xml, "VAlign", VAlign.Top);

        var margin = AttrNullable(xml, "Margin");
        if (margin != null)
            el.Margin = ParseMargin(margin);
    }

    // ── Attribute helpers ───────────────────────────────────────────

    private static string Attr(XElement xml, string name, string fallback)
        => xml.Attribute(name)?.Value ?? fallback;

    private static string? AttrNullable(XElement xml, string name)
        => xml.Attribute(name)?.Value;

    private static int AttrInt(XElement xml, string name, int fallback)
        => int.TryParse(xml.Attribute(name)?.Value, out var v) ? v : fallback;

    private static float AttrFloat(XElement xml, string name, float fallback)
        => float.TryParse(xml.Attribute(name)?.Value, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : fallback;

    private static bool AttrBool(XElement xml, string name, bool fallback)
        => bool.TryParse(xml.Attribute(name)?.Value, out var v) ? v : fallback;

    private static Color AttrColor(XElement xml, string name, Color fallback)
    {
        var val = xml.Attribute(name)?.Value;
        return val != null ? AssetManager.ParseHexColor(val, fallback) : fallback;
    }

    private static Color? AttrColorNullable(XElement xml, string name)
    {
        var val = xml.Attribute(name)?.Value;
        return val != null ? AssetManager.ParseHexColor(val) : null;
    }

    private static T AttrEnum<T>(XElement xml, string name, T fallback) where T : struct, Enum
        => Enum.TryParse<T>(xml.Attribute(name)?.Value, true, out var v) ? v : fallback;

    private static Margin ParseMargin(string value)
    {
        var parts = value.Split(',', ' ').Where(s => s.Length > 0).Select(int.Parse).ToArray();
        return parts.Length switch
        {
            1 => new Margin(parts[0]),
            2 => new Margin(parts[0], parts[1]),
            4 => new Margin(parts[0], parts[1], parts[2], parts[3]),
            _ => Margin.Zero
        };
    }
}
