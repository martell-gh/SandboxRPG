using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MTEngine.Core;

namespace MTEngine.UI;

/// <summary>
/// A draggable, closable window loaded from XML.
/// Provides named-element access for logic binding.
/// </summary>
public class XmlWindow
{
    // ── Style constants ────────────────────────────────────────────
    public const int TitleBarHeight = 28;
    public const int CloseButtonSize = 18;
    private static readonly Color TitleBarColor = new(35, 55, 35);
    private static readonly Color TitleTextColor = Color.SkyBlue;
    private static readonly Color WindowBgColor = new(18, 22, 28, 220);
    private static readonly Color BorderColor = new(70, 110, 70);
    private static readonly Color CloseNormal = new(120, 40, 40);
    private static readonly Color CloseHover = new(180, 50, 50);

    // ── State ──────────────────────────────────────────────────────
    public string Id { get; set; } = "";
    public string Title { get; set; } = "Window";
    public int Width { get; set; } = 300;
    public int Height { get; set; } = 200;
    public bool Closable { get; set; } = true;
    public bool Draggable { get; set; } = true;
    public bool IsOpen { get; private set; }
    public Point Position { get; set; }

    /// <summary>Root panel that holds all child elements from XML.</summary>
    public UIPanel Root { get; } = new() { Direction = LayoutDirection.Vertical, Gap = 4, Padding = 6 };

    // ── Events ─────────────────────────────────────────────────────
    public event Action? OnOpened;
    public event Action? OnClosed;
    public event Action<float>? OnUpdate;

    // ── Drag state ─────────────────────────────────────────────────
    private bool _dragging;
    private Point _dragOffset;
    private bool _closeHovered;

    // ── Element registry ───────────────────────────────────────────
    private readonly Dictionary<string, UIElement> _named = new();

    /// <summary>Registers a named element for quick lookup.</summary>
    internal void Register(UIElement el)
    {
        if (!string.IsNullOrEmpty(el.Name))
            _named[el.Name] = el;
    }

    /// <summary>Get a named element by name and type.</summary>
    public T? Get<T>(string name) where T : UIElement
        => _named.TryGetValue(name, out var el) ? el as T : null;

    /// <summary>Get a named element by name.</summary>
    public UIElement? Get(string name)
        => _named.TryGetValue(name, out var el) ? el : null;

    /// <summary>Enumerate all named elements.</summary>
    public IEnumerable<UIElement> NamedElements => _named.Values;

    // ── Lifecycle ──────────────────────────────────────────────────

    public void Open(Point? position = null)
    {
        if (position.HasValue)
            Position = position.Value;
        IsOpen = true;
        PerformLayout();
        OnOpened?.Invoke();
    }

    public void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        _dragging = false;
        OnClosed?.Invoke();
    }

    public void Toggle(Point? position = null)
    {
        if (IsOpen) Close(); else Open(position);
    }

    // ── Layout ─────────────────────────────────────────────────────

    public void PerformLayout()
    {
        var contentArea = new Rectangle(
            Position.X, Position.Y + TitleBarHeight,
            Width, Height - TitleBarHeight);
        Root.Layout(contentArea);

        // Auto-fit height to content if Root is auto-sized
        if (Root.Bounds.Height + TitleBarHeight > Height)
            Height = Root.Bounds.Height + TitleBarHeight;
    }

    // ── Update (called every frame when open) ──────────────────────

    public void Update(float dt, InputManager input, float uiScale = 1f)
    {
        if (!IsOpen) return;
        OnUpdate?.Invoke(dt);

        var mouse = new Point(
            (int)MathF.Round(input.MousePosition.X / Math.Max(0.01f, uiScale)),
            (int)MathF.Round(input.MousePosition.Y / Math.Max(0.01f, uiScale)));

        // Close button hover
        _closeHovered = Closable && GetCloseRect().Contains(mouse);

        // Dragging
        if (_dragging)
        {
            if (input.LeftDown)
            {
                Position = new Point(mouse.X - _dragOffset.X, mouse.Y - _dragOffset.Y);
                PerformLayout();
            }
            else
            {
                _dragging = false;
            }
            return;
        }

        // Hover propagation
        Root.HandleHover(mouse);
    }

    // ── Input ──────────────────────────────────────────────────────

    public bool HandleClick(Point mouse)
    {
        if (!IsOpen) return false;

        var windowRect = GetWindowRect();
        if (!windowRect.Contains(mouse)) return false;

        // Close button
        if (Closable && GetCloseRect().Contains(mouse))
        {
            Close();
            return true;
        }

        // Title bar drag
        if (Draggable && GetTitleBarRect().Contains(mouse))
        {
            _dragging = true;
            _dragOffset = new Point(mouse.X - Position.X, mouse.Y - Position.Y);
            return true;
        }

        // Content click
        Root.HandleClick(mouse);
        return true; // consume click even if nothing handled inside
    }

    public bool HandleRelease(Point mouse)
    {
        if (!IsOpen) return false;
        if (_dragging) { _dragging = false; return true; }
        Root.HandleRelease(mouse);
        return false;
    }

    public bool HandleScroll(Point mouse, int delta)
    {
        if (!IsOpen) return false;
        if (!GetWindowRect().Contains(mouse)) return false;
        return Root.HandleScroll(mouse, delta);
    }

    public bool HandleTextInput(char c)
    {
        if (!IsOpen) return false;
        return Root.HandleTextInput(c);
    }

    public bool HandleKeyPress(Microsoft.Xna.Framework.Input.Keys key)
    {
        if (!IsOpen) return false;
        return Root.HandleKeyPress(key);
    }

    // ── Draw ───────────────────────────────────────────────────────

    public void Draw(SpriteBatch sb, Texture2D pixel, SpriteFont font)
    {
        if (!IsOpen) return;
        PerformLayout();

        var rect = GetWindowRect();

        // Shadow
        sb.Draw(pixel, new Rectangle(rect.X + 4, rect.Y + 4, rect.Width, rect.Height),
            Color.Black * 0.35f);

        // Background
        sb.Draw(pixel, rect, WindowBgColor);

        // Title bar
        var titleRect = GetTitleBarRect();
        sb.Draw(pixel, titleRect, TitleBarColor);
        sb.DrawString(font, Title,
            new Vector2(titleRect.X + 8, titleRect.Y + (TitleBarHeight - 14) / 2),
            TitleTextColor);

        // Close button
        if (Closable)
        {
            var closeRect = GetCloseRect();
            sb.Draw(pixel, closeRect, _closeHovered ? CloseHover : CloseNormal);
            // Draw X
            var cx = closeRect.X + closeRect.Width / 2;
            var cy = closeRect.Y + closeRect.Height / 2;
            sb.DrawString(font, "X",
                new Vector2(cx - 4, cy - 7), Color.White);
        }

        // Title bar bottom border
        sb.Draw(pixel, new Rectangle(rect.X, titleRect.Bottom, rect.Width, 1), BorderColor);

        // Content
        Root.Draw(sb, pixel, font);

        // Window border
        sb.Draw(pixel, new Rectangle(rect.X, rect.Y, rect.Width, 1), BorderColor);
        sb.Draw(pixel, new Rectangle(rect.X, rect.Bottom - 1, rect.Width, 1), BorderColor);
        sb.Draw(pixel, new Rectangle(rect.X, rect.Y, 1, rect.Height), BorderColor);
        sb.Draw(pixel, new Rectangle(rect.Right - 1, rect.Y, 1, rect.Height), BorderColor);
    }

    // ── Helpers ─────────────────────────────────────────────────────

    public Rectangle GetWindowRect() => new(Position.X, Position.Y, Width, Height);
    public Rectangle GetTitleBarRect() => new(Position.X, Position.Y, Width, TitleBarHeight);

    private Rectangle GetCloseRect() => new(
        Position.X + Width - CloseButtonSize - 6,
        Position.Y + (TitleBarHeight - CloseButtonSize) / 2,
        CloseButtonSize, CloseButtonSize);

    /// <summary>
    /// Shortcut: add a dynamic child element at runtime and register it.
    /// </summary>
    public T AddElement<T>(T element) where T : UIElement
    {
        Root.Add(element);
        Register(element);
        return element;
    }
}
