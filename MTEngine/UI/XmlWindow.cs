using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MTEngine.Core;

namespace MTEngine.UI;

/// <summary>
/// A draggable, closable window loaded from XML.
/// Uses UITheme for all visual appearance (9-slice backgrounds, colors, fonts).
/// </summary>
public class XmlWindow
{
    // ── Static defaults (used by external code for layout calculations) ──
    public const int DefaultTitleBarHeight = 34;
    public const int DefaultCloseButtonSize = 18;

    // ── Theme reference ────────────────────────────────────────────
    public UITheme? Theme { get; set; }

    // ── State ──────────────────────────────────────────────────────
    public string Id { get; set; } = "";
    public string Title { get; set; } = "Window";
    public int Width { get; set; } = 300;
    public int Height { get; set; } = 200;
    public bool Closable { get; set; } = true;
    public bool Draggable { get; set; } = true;
    public bool IsOpen { get; private set; }
    public Point Position { get; set; }

    // Per-window overrides (null = use theme)
    public Color? OverrideBackgroundColor { get; set; }
    public Color? OverrideTitleBarColor { get; set; }
    public Color? OverrideTitleTextColor { get; set; }
    public Color? OverrideBorderColor { get; set; }

    // Legacy texture support (used if theme has no 9-slice)
    public Texture2D? BackgroundTexture { get; set; }
    public Texture2D? TitleTexture { get; set; }
    public bool TileBackgroundTexture { get; set; } = true;
    public bool TileTitleTexture { get; set; } = true;
    public float TitleScale { get; set; } = 1f;

    /// <summary>Root panel that holds all child elements from XML.</summary>
    public UIPanel Root { get; } = new() { Direction = LayoutDirection.Vertical, Gap = 4, Padding = 6 };

    // ── Events ─────────────────────────────────────────────────────
    public event Action? OnOpened;
    public event Action? OnClosed;
    public event Action<float>? OnUpdate;
    public event Action<SpriteBatch, Texture2D, SpriteFont>? OnDrawOverlay;

    // ── Drag state ─────────────────────────────────────────────────
    private bool _dragging;
    private Point _dragOffset;
    private bool _closeHovered;

    // ── Element registry ───────────────────────────────────────────
    private readonly Dictionary<string, UIElement> _named = new();

    internal void Register(UIElement el)
    {
        if (!string.IsNullOrEmpty(el.Name))
            _named[el.Name] = el;
    }

    public T? Get<T>(string name) where T : UIElement
        => _named.TryGetValue(name, out var el) ? el as T : null;

    public UIElement? Get(string name)
        => _named.TryGetValue(name, out var el) ? el : null;

    public IEnumerable<UIElement> NamedElements => _named.Values;

    // ── Resolved style helpers ─────────────────────────────────────

    private int TitleBarHeight => Theme?.TitleBarHeight ?? DefaultTitleBarHeight;
    private int CloseButtonSize => Theme?.CloseButtonSize ?? DefaultCloseButtonSize;

    private Color BgColor => OverrideBackgroundColor ?? Theme?.WindowBackgroundFlatColor ?? new Color(18, 22, 28, 220);
    private Color TitleBarColor => OverrideTitleBarColor ?? Theme?.TitleBarColor ?? new Color(35, 55, 35);
    private Color TitleTextColor => OverrideTitleTextColor ?? Theme?.TitleTextColor ?? Color.SkyBlue;
    private Color BorderColor => OverrideBorderColor ?? Theme?.WindowBorderColor ?? new Color(70, 110, 70);
    private int BorderThickness => Theme?.WindowBorderThickness ?? 1;
    private Color CloseNormalColor => Theme?.CloseButtonColor ?? new Color(120, 40, 40);
    private Color CloseHoverColor => Theme?.CloseButtonHoverColor ?? new Color(180, 50, 50);
    private float ResolvedTitleScale => Theme?.TitleTextScale ?? TitleScale;

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
        var tbh = TitleBarHeight;
        var contentArea = new Rectangle(
            Position.X, Position.Y + tbh,
            Width, Height - tbh);
        Root.Layout(contentArea);

        if (Root.Bounds.Height + tbh > Height)
            Height = Root.Bounds.Height + tbh;
    }

    // ── Update ─────────────────────────────────────────────────────

    public void Update(float dt, InputManager input, float uiScale = 1f)
    {
        if (!IsOpen) return;
        OnUpdate?.Invoke(dt);

        var mousePx = input.MousePosition;
        var mouse = new Point(
            (int)MathF.Round(mousePx.X / Math.Max(0.01f, uiScale)),
            (int)MathF.Round(mousePx.Y / Math.Max(0.01f, uiScale)));

        _closeHovered = Closable && GetCloseRect().Contains(mouse);

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

        Root.HandleHover(mouse);
    }

    // ── Input ──────────────────────────────────────────────────────

    public bool HandleClick(Point mouse)
    {
        if (!IsOpen) return false;

        var windowRect = GetWindowRect();
        if (!windowRect.Contains(mouse)) return false;

        if (Closable && GetCloseRect().Contains(mouse))
        {
            Close();
            return true;
        }

        if (Draggable && GetTitleBarRect().Contains(mouse))
        {
            _dragging = true;
            _dragOffset = new Point(mouse.X - Position.X, mouse.Y - Position.Y);
            return true;
        }

        Root.HandleClick(mouse);
        return true;
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
        var tbh = TitleBarHeight;

        // Shadow
        var shadowAlpha = Theme?.ShadowAlpha ?? 0.35f;
        var shadowOx = Theme?.ShadowOffsetX ?? 4;
        var shadowOy = Theme?.ShadowOffsetY ?? 4;
        var shadowColor = Theme?.ShadowColor ?? Color.Black;
        sb.Draw(pixel, new Rectangle(rect.X + shadowOx, rect.Y + shadowOy, rect.Width, rect.Height),
            shadowColor * shadowAlpha);

        // Background — 9-slice covers the ENTIRE window (including title area)
        var bgSlice = Theme?.WindowBackground;
        var bgTint = Theme?.WindowBackgroundTint ?? Color.White;
        UIDrawHelper.DrawBackground(sb, pixel, rect, BgColor,
            BackgroundTexture, BgColor, TileBackgroundTexture,
            bgSlice, bgTint);

        // Title bar background (only if theme says it's visible)
        var titleBarVisible = Theme?.TitleBarVisible ?? true;
        if (titleBarVisible)
        {
            var titleRect = GetTitleBarRect();
            var titleSlice = Theme?.TitleBarBackground;
            var titleTint = Theme?.TitleBarTint ?? Color.White;
            UIDrawHelper.DrawBackground(sb, pixel, titleRect, TitleBarColor,
                TitleTexture, TitleBarColor, TileTitleTexture,
                titleSlice, titleTint);
        }

        // Title text
        var titleScale = ResolvedTitleScale;
        var titleSize = font.MeasureString(Title) * titleScale;
        sb.DrawString(font, Title,
            new Vector2(
                MathF.Round(rect.X + 10),
                MathF.Round(rect.Y + Math.Max(2f, (tbh - titleSize.Y) / 2f - 1f))),
            TitleTextColor,
            0f, Vector2.Zero, titleScale, SpriteEffects.None, 0f);

        // Close button
        if (Closable)
        {
            var closeRect = GetCloseRect();
            var closeTex = Theme?.CloseButtonTexture;
            if (closeTex != null)
            {
                // Draw sprite close button
                var tint = _closeHovered
                    ? (Theme?.CloseButtonHoverTint ?? Color.White)
                    : (Theme?.CloseButtonTint ?? Color.White);
                sb.Draw(closeTex, closeRect, tint);
            }
            else
            {
                // Fallback: colored rectangle with "X" text
                sb.Draw(pixel, closeRect, _closeHovered ? CloseHoverColor : CloseNormalColor);
                var xSize = font.MeasureString("X");
                sb.DrawString(font, "X",
                    new Vector2(
                        MathF.Round(closeRect.X + (closeRect.Width - xSize.X) / 2f),
                        MathF.Round(closeRect.Y + Math.Max(0f, (closeRect.Height - xSize.Y) / 2f - 1f))),
                    Color.White);
            }
        }

        // Title bar bottom separator (only if title bar bg is visible)
        var borderTh = BorderThickness;
        if (titleBarVisible && borderTh > 0)
            sb.Draw(pixel, new Rectangle(rect.X, rect.Y + tbh, rect.Width, Math.Max(1, borderTh)), BorderColor);

        // Content
        Root.Draw(sb, pixel, font);

        // Window border (skip if 9-slice already provides visual border)
        if (bgSlice == null && borderTh > 0)
            UIDrawHelper.DrawBorder(sb, pixel, rect, BorderColor, borderTh);

        OnDrawOverlay?.Invoke(sb, pixel, font);
    }

    // ── Helpers ─────────────────────────────────────────────────────

    public Rectangle GetWindowRect() => new(Position.X, Position.Y, Width, Height);
    public Rectangle GetTitleBarRect() => new(Position.X, Position.Y, Width, TitleBarHeight);

    private Rectangle GetCloseRect() => new(
        Position.X + Width - CloseButtonSize - 6,
        Position.Y + (TitleBarHeight - CloseButtonSize) / 2,
        CloseButtonSize, CloseButtonSize);

    public T AddElement<T>(T element) where T : UIElement
    {
        Root.Add(element);
        Register(element);
        return element;
    }
}
