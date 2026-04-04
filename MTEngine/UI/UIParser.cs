using System.Xml.Linq;
using Microsoft.Xna.Framework;
using MTEngine.Core;

namespace MTEngine.UI;

/// <summary>
/// Parses XML files into XmlWindow instances.
///
/// Supported elements:
///   Window           — root, becomes XmlWindow
///   Label            — text label
///   Button           — clickable button
///   Image            — texture image
///   Panel            — container (Vertical/Horizontal)
///   ScrollPanel      — scrollable vertical panel
///   ProgressBar      — value bar
///   TextInput        — editable text field
///   Separator        — horizontal line
///
/// Common attributes:
///   Name, Width, Height, Margin, HAlign, VAlign, Visible, Tooltip
///
/// Example:
///   <Window Id="crafting" Title="Crafting" Width="320" Height="260" Closable="true">
///     <Panel Direction="Vertical" Padding="6" Gap="4">
///       <Label Name="header" Text="Recipes" Color="#00FF00" />
///       <Separator />
///       <ScrollPanel Name="list" Height="150" Gap="2" />
///       <Button Name="craft" Text="Craft!" Width="100" Height="26" />
///     </Panel>
///   </Window>
/// </summary>
public static class UIParser
{
    public static XmlWindow LoadFromFile(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"UI XML not found: {path}");

        var doc = XDocument.Load(path);
        return Parse(doc);
    }

    public static XmlWindow LoadFromString(string xml)
    {
        var doc = XDocument.Parse(xml);
        return Parse(doc);
    }

    private static XmlWindow Parse(XDocument doc)
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
        };

        foreach (var childXml in root.Elements())
        {
            var el = ParseElement(childXml, window);
            if (el != null)
                window.Root.Add(el);
        }

        return window;
    }

    private static UIElement? ParseElement(XElement xml, XmlWindow window)
    {
        UIElement? el = xml.Name.LocalName switch
        {
            "Label" => ParseLabel(xml),
            "Button" => ParseButton(xml),
            "Image" => ParseImage(xml),
            "Panel" => ParsePanel(xml, window),
            "ScrollPanel" => ParseScrollPanel(xml, window),
            "ProgressBar" => ParseProgressBar(xml),
            "TextInput" => ParseTextInput(xml),
            "Separator" => ParseSeparator(xml),
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

    private static UIButton ParseButton(XElement xml) => new()
    {
        Text = Attr(xml, "Text", ""),
        TextColor = AttrColor(xml, "TextColor", Color.White),
        BackColor = AttrColor(xml, "BackColor", new Color(45, 65, 45)),
        HoverColor = AttrColor(xml, "HoverColor", new Color(65, 95, 65)),
        BorderColor = AttrColor(xml, "BorderColor", new Color(70, 110, 70)),
    };

    private static UIImage ParseImage(XElement xml) => new()
    {
        TexturePath = AttrNullable(xml, "Texture"),
        Tint = AttrColor(xml, "Tint", Color.White),
    };

    private static UIPanel ParsePanel(XElement xml, XmlWindow window)
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
            var child = ParseElement(childXml, window);
            if (child != null) panel.Add(child);
        }

        return panel;
    }

    private static UIScrollPanel ParseScrollPanel(XElement xml, XmlWindow window)
    {
        var panel = new UIScrollPanel
        {
            Direction = LayoutDirection.Vertical,
            Gap = AttrInt(xml, "Gap", 4),
            Padding = AttrInt(xml, "Padding", 0),
            BackColor = AttrColorNullable(xml, "BackColor"),
            ScrollStep = AttrInt(xml, "ScrollStep", 20),
        };

        foreach (var childXml in xml.Elements())
        {
            var child = ParseElement(childXml, window);
            if (child != null) panel.Add(child);
        }

        return panel;
    }

    private static UIProgressBar ParseProgressBar(XElement xml) => new()
    {
        Value = AttrFloat(xml, "Value", 1f),
        MaxValue = AttrFloat(xml, "MaxValue", 1f),
        FillColor = AttrColor(xml, "FillColor", new Color(80, 180, 80)),
        BackColor = AttrColor(xml, "BackColor", new Color(30, 30, 30)),
        BorderColor = AttrColor(xml, "BorderColor", new Color(70, 110, 70)),
        ShowText = AttrBool(xml, "ShowText", true),
        TextFormat = AttrNullable(xml, "TextFormat"),
    };

    private static UITextInput ParseTextInput(XElement xml) => new()
    {
        Placeholder = Attr(xml, "Placeholder", ""),
        MaxLength = AttrInt(xml, "MaxLength", 256),
        TextColor = AttrColor(xml, "TextColor", Color.White),
    };

    private static UISeparator ParseSeparator(XElement xml) => new()
    {
        Color = AttrColor(xml, "Color", new Color(70, 110, 70, 100)),
        Thickness = AttrInt(xml, "Thickness", 1),
    };

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
