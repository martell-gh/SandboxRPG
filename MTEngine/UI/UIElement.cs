using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace MTEngine.UI;

public abstract class UIElement
{
    public string? Name { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public bool Visible { get; set; } = true;
    public bool Enabled { get; set; } = true;
    public HAlign HAlign { get; set; } = HAlign.Left;
    public VAlign VAlign { get; set; } = VAlign.Top;
    public Margin Margin { get; set; } = Margin.Zero;
    public string? Tooltip { get; set; }

    /// <summary>Parent element (set automatically when added to a panel).</summary>
    public UIElement? Parent { get; internal set; }

    /// <summary>Absolute rectangle in screen space, computed during layout.</summary>
    public Rectangle Bounds { get; set; }

    /// <summary>Arbitrary user data for binding logic.</summary>
    public object? Tag { get; set; }

    public virtual void Layout(Rectangle available)
    {
        Bounds = new Rectangle(available.X + Margin.Left, available.Y + Margin.Top,
            Width > 0 ? Width : available.Width - Margin.Left - Margin.Right,
            Height > 0 ? Height : available.Height - Margin.Top - Margin.Bottom);
    }

    public abstract void Draw(SpriteBatch sb, Texture2D pixel, SpriteFont font);

    public virtual bool HandleClick(Point mousePos) => false;
    public virtual bool HandleRelease(Point mousePos) => false;
    public virtual bool HandleHover(Point mousePos) => false;
    public virtual bool HandleScroll(Point mousePos, int delta) => false;
    public virtual bool HandleTextInput(char c) => false;
    public virtual bool HandleKeyPress(Microsoft.Xna.Framework.Input.Keys key) => false;

    public bool ContainsMouse(Point mousePos) => Visible && Bounds.Contains(mousePos);
}

public enum HAlign { Left, Center, Right, Stretch }
public enum VAlign { Top, Center, Bottom, Stretch }

public struct Margin
{
    public int Left, Top, Right, Bottom;

    public static Margin Zero => new();

    public Margin(int all) { Left = Top = Right = Bottom = all; }
    public Margin(int horizontal, int vertical) { Left = Right = horizontal; Top = Bottom = vertical; }
    public Margin(int left, int top, int right, int bottom)
    {
        Left = left; Top = top; Right = right; Bottom = bottom;
    }

    public int Horizontal => Left + Right;
    public int Vertical => Top + Bottom;
}
