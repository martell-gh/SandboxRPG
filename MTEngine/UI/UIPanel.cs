using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace MTEngine.UI;

public enum LayoutDirection { Vertical, Horizontal }

public class UIPanel : UIElement
{
    public LayoutDirection Direction { get; set; } = LayoutDirection.Vertical;
    public int Gap { get; set; } = 4;
    public int Padding { get; set; } = 0;
    public Color? BackColor { get; set; }
    public Texture2D? BackgroundTexture { get; set; }
    public Color BackgroundTint { get; set; } = Color.White;
    public bool TileBackground { get; set; } = true;
    public Color BorderColor { get; set; } = Color.Transparent;
    public int BorderThickness { get; set; } = 1;
    public List<UIElement> Children { get; } = new();

    public void Add(UIElement child)
    {
        child.Parent = this;
        Children.Add(child);
    }

    public void Remove(UIElement child)
    {
        child.Parent = null;
        Children.Remove(child);
    }

    public void Clear()
    {
        foreach (var c in Children) c.Parent = null;
        Children.Clear();
    }

    public override void Layout(Rectangle available)
    {
        base.Layout(available);

        var inner = new Rectangle(
            Bounds.X + Padding, Bounds.Y + Padding,
            Bounds.Width - Padding * 2, Bounds.Height - Padding * 2);

        // ── Pass 1: measure children to get their natural sizes ────
        // Give each child a generous area so it can compute its natural Bounds.
        foreach (var child in Children)
        {
            if (!child.Visible) continue;
            var measure = new Rectangle(inner.X, inner.Y, inner.Width, inner.Height);
            child.Layout(measure);
        }

        // ── Pass 2: arrange with proper offsets ────────────────────
        int offset = 0;
        if (Direction == LayoutDirection.Vertical)
        {
            foreach (var child in Children)
            {
                if (!child.Visible) continue;

                int childH = child.Height > 0 ? child.Height : child.Bounds.Height;
                var childArea = new Rectangle(
                    inner.X + child.Margin.Left,
                    inner.Y + offset + child.Margin.Top,
                    inner.Width - child.Margin.Horizontal,
                    childH);
                child.Layout(childArea);
                offset += child.Bounds.Height + child.Margin.Vertical + Gap;
            }
        }
        else // Horizontal
        {
            // Compute max child height for uniform row height
            int maxH = 0;
            foreach (var child in Children)
            {
                if (!child.Visible) continue;
                int h = child.Height > 0 ? child.Height : child.Bounds.Height;
                if (h > maxH) maxH = h;
            }

            foreach (var child in Children)
            {
                if (!child.Visible) continue;

                int childW = child.Width > 0 ? child.Width
                    : Math.Max(0, inner.Width - offset);
                var childArea = new Rectangle(
                    inner.X + offset + child.Margin.Left,
                    inner.Y + child.Margin.Top,
                    childW - child.Margin.Horizontal,
                    maxH);
                child.Layout(childArea);
                offset += child.Bounds.Width + child.Margin.Horizontal + Gap;
            }
        }

        // ── Auto-size: shrink to content ───────────────────────────
        if (Height <= 0 && Children.Count > 0)
        {
            var lastVisible = Children.LastOrDefault(c => c.Visible);
            if (lastVisible != null)
            {
                var totalH = lastVisible.Bounds.Bottom - Bounds.Y + Padding;
                Bounds = new Rectangle(Bounds.X, Bounds.Y, Bounds.Width, totalH);
            }
        }

        if (Width <= 0 && Direction == LayoutDirection.Horizontal && Children.Count > 0)
        {
            var lastVisible = Children.LastOrDefault(c => c.Visible);
            if (lastVisible != null)
            {
                var totalW = lastVisible.Bounds.Right - Bounds.X + Padding;
                Bounds = new Rectangle(Bounds.X, Bounds.Y, totalW, Bounds.Height);
            }
        }
    }

    public override void Draw(SpriteBatch sb, Texture2D pixel, SpriteFont font)
    {
        if (!Visible) return;

        UIDrawHelper.DrawBackground(sb, pixel, Bounds, BackColor, BackgroundTexture, BackgroundTint, TileBackground);
        UIDrawHelper.DrawBorder(sb, pixel, Bounds, BorderColor, BorderThickness);

        foreach (var child in Children)
            child.Draw(sb, pixel, font);
    }

    public override bool HandleClick(Point mousePos)
    {
        if (!Visible || !Bounds.Contains(mousePos)) return false;
        for (int i = Children.Count - 1; i >= 0; i--)
            if (Children[i].HandleClick(mousePos)) return true;
        return false;
    }

    public override bool HandleRelease(Point mousePos)
    {
        for (int i = Children.Count - 1; i >= 0; i--)
            if (Children[i].HandleRelease(mousePos)) return true;
        return false;
    }

    public override bool HandleHover(Point mousePos)
    {
        bool any = false;
        foreach (var child in Children)
            any |= child.HandleHover(mousePos);
        return any;
    }

    public override bool HandleScroll(Point mousePos, int delta)
    {
        if (!Visible || !Bounds.Contains(mousePos)) return false;
        for (int i = Children.Count - 1; i >= 0; i--)
            if (Children[i].HandleScroll(mousePos, delta)) return true;
        return false;
    }

    public override bool HandleTextInput(char c)
    {
        foreach (var child in Children)
            if (child.HandleTextInput(c)) return true;
        return false;
    }

    public override bool HandleKeyPress(Microsoft.Xna.Framework.Input.Keys key)
    {
        foreach (var child in Children)
            if (child.HandleKeyPress(key)) return true;
        return false;
    }
}
