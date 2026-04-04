using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace MTEngine.UI;

/// <summary>
/// A vertical scroll panel. Children are laid out vertically and can overflow;
/// the panel clips drawing and scrolls with the mouse wheel.
/// </summary>
public class UIScrollPanel : UIPanel
{
    public int ScrollOffset { get; set; }
    public int ScrollStep { get; set; } = 20;
    public Color ScrollBarColor { get; set; } = new(70, 110, 70, 120);

    private int _contentHeight;

    public override void Layout(Rectangle available)
    {
        base.Layout(available);

        // Compute total content height
        _contentHeight = 0;
        foreach (var child in Children)
        {
            if (!child.Visible) continue;
            var bottom = child.Bounds.Bottom - Bounds.Y + Padding;
            if (bottom > _contentHeight) _contentHeight = bottom;
        }

        // Clamp scroll
        var maxScroll = Math.Max(0, _contentHeight - Bounds.Height);
        ScrollOffset = Math.Clamp(ScrollOffset, 0, maxScroll);

        // Shift children by scroll offset
        foreach (var child in Children)
        {
            if (!child.Visible) continue;
            child.Bounds = new Rectangle(
                child.Bounds.X,
                child.Bounds.Y - ScrollOffset,
                child.Bounds.Width,
                child.Bounds.Height);
        }
    }

    public override void Draw(SpriteBatch sb, Texture2D pixel, SpriteFont font)
    {
        if (!Visible) return;

        if (BackColor.HasValue)
            sb.Draw(pixel, Bounds, BackColor.Value);

        // Scissor-based clipping via simple bounds check
        foreach (var child in Children)
        {
            if (child.Bounds.Bottom < Bounds.Top || child.Bounds.Top > Bounds.Bottom)
                continue;
            child.Draw(sb, pixel, font);
        }

        // Scroll bar
        if (_contentHeight > Bounds.Height)
        {
            var barHeight = Math.Max(20, Bounds.Height * Bounds.Height / _contentHeight);
            var maxScroll = _contentHeight - Bounds.Height;
            var barY = maxScroll > 0
                ? Bounds.Y + (int)((Bounds.Height - barHeight) * ((float)ScrollOffset / maxScroll))
                : Bounds.Y;
            sb.Draw(pixel, new Rectangle(Bounds.Right - 4, barY, 4, barHeight), ScrollBarColor);
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
}
