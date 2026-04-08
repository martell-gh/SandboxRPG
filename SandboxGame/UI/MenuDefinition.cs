#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Xml.Linq;
using Microsoft.Xna.Framework;

namespace SandboxGame.UI;

public class MenuItemDef
{
    public string Label { get; set; } = "";
    public string? Description { get; set; }
    public string Action { get; set; } = "";
    public string? Condition { get; set; }
}

public class MenuLayoutDef
{
    public int Margin { get; set; } = 72;
    public float NavWidthPercent { get; set; } = 0.28f;
    public int NavMaxWidth { get; set; } = 320;
    public int NavContentGap { get; set; } = 20;
    public int PanelPadding { get; set; } = 24;
    public int TitleBottomMargin { get; set; } = 100;
}

public class MenuBackgroundDef
{
    public string Type { get; set; } = "solid"; // "solid" | "overlay"
    public Color Color { get; set; } = Color.Black;
    public Color GlowColor { get; set; } = new(22, 40, 28, 58);
    public float GlowHeightPercent { get; set; } = 0.33f;
}

public class MenuPanelDef
{
    public Color Fill { get; set; } = new(10, 18, 14, 228);
    public Color Border { get; set; } = new(84, 120, 92);
    public int BorderThickness { get; set; } = 2;
}

public class MenuTitleBarDef
{
    public Color Color { get; set; } = Color.White;
    public Color SeparatorColor { get; set; } = new(84, 120, 92);
    public int SeparatorHeight { get; set; } = 2;
    public int OffsetX { get; set; } = 24;
    public int OffsetY { get; set; } = 24;
    public int SeparatorGap { get; set; } = 10;
}

public class MenuNavPanelDef : MenuPanelDef
{
    public string Header { get; set; } = "Navigation";
    public string HeaderDescription { get; set; } = "";
    public Color HeaderColor { get; set; } = Color.White;
    public Color HeaderDescColor { get; set; } = new(150, 172, 156);
    public int ItemHeight { get; set; } = 46;
    public int ItemGap { get; set; } = 8;
    public int ItemStartY { get; set; } = 66;
}

public class MenuContentPanelDef : MenuPanelDef
{
    public Color CardFill { get; set; } = new(22, 36, 28, 230);
    public Color CardBorder { get; set; } = new(88, 132, 96);
    public int CardBorderThickness { get; set; } = 2;
    public int CardOffsetX { get; set; } = 28;
    public int CardOffsetY { get; set; } = 98;
    public int CardHeight { get; set; } = 116;
    public int CardPadding { get; set; } = 20;
    public Color LabelColor { get; set; } = Color.White;
    public Color DescriptionColor { get; set; } = new(190, 208, 192);
}

public class MenuItemStyleDef
{
    public Color SelectedFill { get; set; } = new(56, 92, 66, 240);
    public Color HoveredFill { get; set; } = new(36, 54, 42, 228);
    public Color DefaultFill { get; set; } = new(25, 37, 30, 220);
    public Color SelectedBorder { get; set; } = new(118, 176, 128);
    public Color HoveredBorder { get; set; } = new(96, 138, 104);
    public Color DefaultBorder { get; set; } = new(62, 92, 70);
    public Color SelectedText { get; set; } = Color.White;
    public Color DefaultText { get; set; } = new(196, 208, 198);
    public int AccentBarWidth { get; set; } = 4;
    public Color AccentBarColor { get; set; } = new(180, 222, 128);
}

public class MenuHintDef
{
    public string Text { get; set; } = "";
    public Color Color { get; set; } = new(150, 170, 150);
    public int OffsetX { get; set; } = 24;
    public int OffsetY { get; set; } = 32;
    public string Align { get; set; } = "right-bottom";
}

public class MenuDefinition
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public MenuLayoutDef Layout { get; set; } = new();
    public MenuBackgroundDef Background { get; set; } = new();
    public MenuPanelDef PagePanel { get; set; } = new();
    public MenuTitleBarDef TitleBar { get; set; } = new();
    public MenuNavPanelDef NavPanel { get; set; } = new();
    public MenuContentPanelDef ContentPanel { get; set; } = new();
    public MenuItemStyleDef ItemStyle { get; set; } = new();
    public MenuHintDef Hint { get; set; } = new();
    public List<MenuItemDef> Items { get; set; } = new();

    public static MenuDefinition LoadFromFile(string path)
    {
        var doc = XDocument.Load(path);
        var root = doc.Root ?? throw new InvalidOperationException($"Invalid menu XML: {path}");

        var def = new MenuDefinition
        {
            Id = Attr(root, "Id", ""),
            Title = Attr(root, "Title", "")
        };

        var layout = root.Element("Layout");
        if (layout != null)
        {
            def.Layout = new MenuLayoutDef
            {
                Margin = AttrInt(layout, "Margin", 72),
                NavWidthPercent = AttrFloat(layout, "NavWidthPercent", 0.28f),
                NavMaxWidth = AttrInt(layout, "NavMaxWidth", 320),
                NavContentGap = AttrInt(layout, "NavContentGap", 20),
                PanelPadding = AttrInt(layout, "PanelPadding", 24),
                TitleBottomMargin = AttrInt(layout, "TitleBottomMargin", 100)
            };
        }

        var bg = root.Element("Background");
        if (bg != null)
        {
            def.Background = new MenuBackgroundDef
            {
                Type = Attr(bg, "Type", "solid"),
                Color = AttrColor(bg, "Color", Color.Black),
                GlowColor = AttrColor(bg, "GlowColor", new Color(22, 40, 28, 58)),
                GlowHeightPercent = AttrFloat(bg, "GlowHeightPercent", 0.33f)
            };
        }

        var page = root.Element("PagePanel");
        if (page != null)
            def.PagePanel = ParsePanel(page);

        var titleBar = root.Element("TitleBar");
        if (titleBar != null)
        {
            def.TitleBar = new MenuTitleBarDef
            {
                Color = AttrColor(titleBar, "Color", Color.White),
                SeparatorColor = AttrColor(titleBar, "SeparatorColor", new Color(84, 120, 92)),
                SeparatorHeight = AttrInt(titleBar, "SeparatorHeight", 2),
                OffsetX = AttrInt(titleBar, "OffsetX", 24),
                OffsetY = AttrInt(titleBar, "OffsetY", 24),
                SeparatorGap = AttrInt(titleBar, "SeparatorGap", 10)
            };
        }

        var nav = root.Element("NavPanel");
        if (nav != null)
        {
            var p = ParsePanel(nav);
            def.NavPanel = new MenuNavPanelDef
            {
                Fill = p.Fill, Border = p.Border, BorderThickness = p.BorderThickness,
                Header = Attr(nav, "Header", "Navigation"),
                HeaderDescription = Attr(nav, "HeaderDescription", ""),
                HeaderColor = AttrColor(nav, "HeaderColor", Color.White),
                HeaderDescColor = AttrColor(nav, "HeaderDescColor", new Color(150, 172, 156)),
                ItemHeight = AttrInt(nav, "ItemHeight", 46),
                ItemGap = AttrInt(nav, "ItemGap", 8),
                ItemStartY = AttrInt(nav, "ItemStartY", 66)
            };
        }

        var content = root.Element("ContentPanel");
        if (content != null)
        {
            var p = ParsePanel(content);
            def.ContentPanel = new MenuContentPanelDef
            {
                Fill = p.Fill, Border = p.Border, BorderThickness = p.BorderThickness,
                CardFill = AttrColor(content, "CardFill", new Color(22, 36, 28, 230)),
                CardBorder = AttrColor(content, "CardBorder", new Color(88, 132, 96)),
                CardBorderThickness = AttrInt(content, "CardBorderThickness", 2),
                CardOffsetX = AttrInt(content, "CardOffsetX", 28),
                CardOffsetY = AttrInt(content, "CardOffsetY", 98),
                CardHeight = AttrInt(content, "CardHeight", 116),
                CardPadding = AttrInt(content, "CardPadding", 20),
                LabelColor = AttrColor(content, "LabelColor", Color.White),
                DescriptionColor = AttrColor(content, "DescriptionColor", new Color(190, 208, 192))
            };
        }

        var style = root.Element("ItemStyle");
        if (style != null)
        {
            def.ItemStyle = new MenuItemStyleDef
            {
                SelectedFill = AttrColor(style, "SelectedFill", new Color(56, 92, 66, 240)),
                HoveredFill = AttrColor(style, "HoveredFill", new Color(36, 54, 42, 228)),
                DefaultFill = AttrColor(style, "DefaultFill", new Color(25, 37, 30, 220)),
                SelectedBorder = AttrColor(style, "SelectedBorder", new Color(118, 176, 128)),
                HoveredBorder = AttrColor(style, "HoveredBorder", new Color(96, 138, 104)),
                DefaultBorder = AttrColor(style, "DefaultBorder", new Color(62, 92, 70)),
                SelectedText = AttrColor(style, "SelectedText", Color.White),
                DefaultText = AttrColor(style, "DefaultText", new Color(196, 208, 198)),
                AccentBarWidth = AttrInt(style, "AccentBarWidth", 4),
                AccentBarColor = AttrColor(style, "AccentBarColor", new Color(180, 222, 128))
            };
        }

        var hint = root.Element("Hint");
        if (hint != null)
        {
            def.Hint = new MenuHintDef
            {
                Text = Attr(hint, "Text", ""),
                Color = AttrColor(hint, "Color", new Color(150, 170, 150)),
                OffsetX = AttrInt(hint, "OffsetX", 24),
                OffsetY = AttrInt(hint, "OffsetY", 32),
                Align = Attr(hint, "Align", "right-bottom")
            };
        }

        var items = root.Element("Items");
        if (items != null)
        {
            foreach (var item in items.Elements("Item"))
            {
                def.Items.Add(new MenuItemDef
                {
                    Label = Attr(item, "Label", ""),
                    Description = item.Attribute("Description")?.Value,
                    Action = Attr(item, "Action", ""),
                    Condition = item.Attribute("Condition")?.Value
                });
            }
        }

        return def;
    }

    // === Parsing helpers ===

    private static MenuPanelDef ParsePanel(XElement el) => new()
    {
        Fill = AttrColor(el, "Fill", new Color(10, 18, 14, 228)),
        Border = AttrColor(el, "Border", new Color(84, 120, 92)),
        BorderThickness = AttrInt(el, "BorderThickness", 2)
    };

    private static string Attr(XElement el, string name, string fallback)
        => el.Attribute(name)?.Value ?? fallback;

    private static int AttrInt(XElement el, string name, int fallback)
        => int.TryParse(el.Attribute(name)?.Value, out var v) ? v : fallback;

    private static float AttrFloat(XElement el, string name, float fallback)
        => float.TryParse(el.Attribute(name)?.Value, CultureInfo.InvariantCulture, out var v) ? v : fallback;

    /// <summary>
    /// Парсит цвет из формата #RRGGBB или #RRGGBB_AA (hex alpha).
    /// </summary>
    public static Color AttrColor(XElement el, string name, Color fallback)
    {
        var raw = el.Attribute(name)?.Value;
        if (string.IsNullOrWhiteSpace(raw)) return fallback;
        return ParseColor(raw, fallback);
    }

    public static Color ParseColor(string raw, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(raw)) return fallback;

        // #RRGGBB_AA format
        var parts = raw.Split('_');
        var hex = parts[0].TrimStart('#');

        if (hex.Length != 6 || !uint.TryParse(hex, NumberStyles.HexNumber, null, out var rgb))
            return fallback;

        var r = (byte)((rgb >> 16) & 0xFF);
        var g = (byte)((rgb >> 8) & 0xFF);
        var b = (byte)(rgb & 0xFF);
        byte a = 255;

        if (parts.Length > 1 && byte.TryParse(parts[1], NumberStyles.HexNumber, null, out var alpha))
            a = alpha;

        return new Color(r, g, b, a);
    }
}
