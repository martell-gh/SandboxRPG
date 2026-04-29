using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MTEngine.Core;

namespace MTEngine.UI;

/// <summary>
/// A vertical scroll panel. Children are laid out vertically and can overflow;
/// the panel clips drawing and scrolls with the mouse wheel.
/// </summary>
public class UIScrollPanel : UIPanel
{
    public int ScrollOffset { get; set; }
    public int ScrollStep { get; set; } = 20;
    public Color? OverrideScrollBarColor { get; set; }
    public Color ScrollBarColor { get; set; } = new(70, 110, 70, 120);

    public UITheme? Theme { get; set; }

    private int _contentHeight;

    public override void Layout(Rectangle available)
    {
        base.Layout(available);

        _contentHeight = 0;
        foreach (var child in Children)
        {
            if (!child.Visible) continue;
            var bottom = child.Bounds.Bottom - Bounds.Y + Padding;
            if (bottom > _contentHeight) _contentHeight = bottom;
        }

        var maxScroll = Math.Max(0, _contentHeight - Bounds.Height);
        ScrollOffset = Math.Clamp(ScrollOffset, 0, maxScroll);

        foreach (var child in Children)
        {
            if (!child.Visible) continue;
            OffsetElementTree(child, 0, -ScrollOffset);
        }
    }

    public override void Draw(SpriteBatch sb, Texture2D pixel, SpriteFont font)
    {
        if (!Visible) return;

        UIDrawHelper.DrawBackground(sb, pixel, Bounds, BackColor, BackgroundTexture, BackgroundTint, TileBackground);
        UIDrawHelper.DrawBorder(sb, pixel, Bounds, BorderColor, BorderThickness);

        var gd = ServiceLocator.Has<GraphicsDevice>() ? ServiceLocator.Get<GraphicsDevice>() : null;
        var previousScissor = gd?.ScissorRectangle ?? Rectangle.Empty;
        if (gd != null)
            gd.ScissorRectangle = Rectangle.Intersect(previousScissor, Bounds);

        foreach (var child in Children)
        {
            if (child.Bounds.Bottom < Bounds.Top || child.Bounds.Top > Bounds.Bottom)
                continue;
            child.Draw(sb, pixel, font);
        }

        if (gd != null)
            gd.ScissorRectangle = previousScissor;

        // Scroll bar
        if (_contentHeight > Bounds.Height)
        {
            var scrollBarWidth = Theme?.ScrollBarWidth ?? 4;
            var scrollBarColor = OverrideScrollBarColor ?? Theme?.ScrollBarColor ?? ScrollBarColor;
            var barHeight = Math.Max(20, Bounds.Height * Bounds.Height / _contentHeight);
            var maxScroll = _contentHeight - Bounds.Height;
            var barY = maxScroll > 0
                ? Bounds.Y + (int)((Bounds.Height - barHeight) * ((float)ScrollOffset / maxScroll))
                : Bounds.Y;
            sb.Draw(pixel, new Rectangle(Bounds.Right - scrollBarWidth, barY, scrollBarWidth, barHeight), scrollBarColor);
        }
    }

    public override bool HandleScroll(Point mousePos, int delta)
    {
        if (!Visible || !Bounds.Contains(mousePos)) return false;
        ScrollOffset -= Math.Sign(delta) * ScrollStep;
        var maxScroll = Math.Max(0, _contentHeight - Bounds.Height);
        ScrollOffset = Math.Clamp(ScrollOffset, 0, maxScroll);
        return true;
    }

    public override bool HandleClick(Point mousePos)
    {
        if (!Visible || !Bounds.Contains(mousePos)) return false;
        for (int i = Children.Count - 1; i >= 0; i--)
        {
            if (Children[i].Bounds.Bottom < Bounds.Top || Children[i].Bounds.Top > Bounds.Bottom)
                continue;
            if (Children[i].HandleClick(mousePos)) return true;
        }
        return false;
    }

    private static void OffsetElementTree(UIElement element, int dx, int dy)
    {
        element.Bounds = new Rectangle(
            element.Bounds.X + dx,
            element.Bounds.Y + dy,
            element.Bounds.Width,
            element.Bounds.Height);

        if (element is not UIPanel panel)
            return;

        foreach (var child in panel.Children)
        {
            if (!child.Visible)
                continue;

            OffsetElementTree(child, dx, dy);
        }
    }
}
