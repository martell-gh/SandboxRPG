using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MTEngine.Core;
using MTEngine.ECS;

namespace MTEngine.UI;

/// <summary>
/// GameSystem that manages all XML-based windows.
/// Handles input routing, drawing order, and focus.
/// Register as a system in GameEngine — draws on the Overlay layer.
/// </summary>
public class UIManager : GameSystem
{
    public override DrawLayer DrawLayer => DrawLayer.Overlay;

    private readonly List<XmlWindow> _windows = new();
    private readonly Dictionary<string, XmlWindow> _windowsById = new();

    private InputManager? _input;
    private SpriteBatch? _sb;
    private SpriteFont? _font;
    private Texture2D? _pixel;
    private GraphicsDevice? _gd;
    private AssetManager? _assets;

    /// <summary>Whether any window consumed input this frame.</summary>
    public bool ConsumedInput { get; private set; }

    public void SetFont(SpriteFont font) => _font = font;

    public override void OnInitialize()
    {
        _input = ServiceLocator.Get<InputManager>();
        _gd = ServiceLocator.Get<GraphicsDevice>();
    }

    private void EnsureResources()
    {
        _sb ??= ServiceLocator.Get<SpriteBatch>();
        if (_pixel == null && _gd != null)
        {
            _pixel = new Texture2D(_gd, 1, 1);
            _pixel.SetData(new[] { Color.White });
        }
        if (_assets == null && ServiceLocator.Has<AssetManager>())
            _assets = ServiceLocator.Get<AssetManager>();
    }

    private float GetUiScale()
        => ServiceLocator.Has<IUiScaleSource>()
            ? Math.Clamp(ServiceLocator.Get<IUiScaleSource>().UiScale, 0.75f, 2f)
            : 1f;

    // ── Window management ──────────────────────────────��───────────

    /// <summary>Load a window from an XML file and register it.</summary>
    public XmlWindow LoadWindow(string xmlPath)
    {
        var window = UIParser.LoadFromFile(xmlPath);
        RegisterWindow(window);
        return window;
    }

    /// <summary>Load a window from an XML string and register it.</summary>
    public XmlWindow LoadWindowFromString(string xml)
    {
        var window = UIParser.LoadFromString(xml);
        RegisterWindow(window);
        return window;
    }

    /// <summary>Register a manually-created window.</summary>
    public void RegisterWindow(XmlWindow window)
    {
        if (!_windows.Contains(window))
            _windows.Add(window);

        if (!string.IsNullOrEmpty(window.Id))
            _windowsById[window.Id] = window;

        // Resolve image textures
        if (_assets != null)
            ResolveTextures(window.Root);
    }

    /// <summary>Remove a window from the manager.</summary>
    public void RemoveWindow(XmlWindow window)
    {
        window.Close();
        _windows.Remove(window);
        if (!string.IsNullOrEmpty(window.Id))
            _windowsById.Remove(window.Id);
    }

    /// <summary>Get a registered window by its Id.</summary>
    public XmlWindow? GetWindow(string id)
        => _windowsById.TryGetValue(id, out var w) ? w : null;

    /// <summary>Open a window by Id, optionally at a position.</summary>
    public void OpenWindow(string id, Point? position = null)
        => GetWindow(id)?.Open(position);

    /// <summary>Close a window by Id.</summary>
    public void CloseWindow(string id)
        => GetWindow(id)?.Close();

    /// <summary>Close all open windows.</summary>
    public void CloseAll()
    {
        foreach (var w in _windows) w.Close();
    }

    /// <summary>Get all currently open windows.</summary>
    public IEnumerable<XmlWindow> OpenWindows => _windows.Where(w => w.IsOpen);

    /// <summary>Whether any window is currently open.</summary>
    public bool AnyWindowOpen => _windows.Any(w => w.IsOpen);

    // ── Update ──────────────────────────���──────────────────────────

    public override void Update(float deltaTime)
    {
        if (_input == null) return;
        EnsureResources();
        ConsumedInput = false;

        var uiScale = GetUiScale();
        var mouse = new Point(
            (int)MathF.Round(_input.MousePosition.X / uiScale),
            (int)MathF.Round(_input.MousePosition.Y / uiScale));

        // Escape closes the topmost window
        if (_input.IsPressed(Keys.Escape))
        {
            for (int i = _windows.Count - 1; i >= 0; i--)
            {
                if (_windows[i].IsOpen && _windows[i].Closable)
                {
                    _windows[i].Close();
                    ConsumedInput = true;
                    return;
                }
            }
        }

        // Update all open windows (for hover, drag, etc.)
        foreach (var w in _windows)
            if (w.IsOpen) w.Update(deltaTime, _input, uiScale);

        // Left click — route to topmost window that contains the point
        if (_input.LeftClicked)
        {
            for (int i = _windows.Count - 1; i >= 0; i--)
            {
                if (!_windows[i].IsOpen) continue;
                if (_windows[i].HandleClick(mouse))
                {
                    BringToFront(_windows[i]);
                    ConsumedInput = true;
                    return;
                }
            }
        }

        // Left release
        if (_input.LeftReleased)
        {
            foreach (var w in _windows)
                if (w.IsOpen) w.HandleRelease(mouse);
        }

        // Scroll
        if (_input.ScrollDelta != 0)
        {
            for (int i = _windows.Count - 1; i >= 0; i--)
            {
                if (!_windows[i].IsOpen) continue;
                if (_windows[i].HandleScroll(mouse, _input.ScrollDelta))
                {
                    ConsumedInput = true;
                    break;
                }
            }
        }

        // Text input forwarding (for focused TextInput elements)
        // MonoGame requires Window.TextInput event; we handle key presses here
        if (_input.IsPressed(Keys.Back))
        {
            foreach (var w in _windows)
                if (w.IsOpen && w.HandleTextInput('\b')) { ConsumedInput = true; break; }
        }
        if (_input.IsPressed(Keys.Enter))
        {
            foreach (var w in _windows)
                if (w.IsOpen && w.HandleTextInput('\r')) { ConsumedInput = true; break; }
        }
    }

    /// <summary>Call from Game.Window.TextInput event to forward text to focused inputs.</summary>
    public void OnTextInput(char c)
    {
        foreach (var w in _windows)
            if (w.IsOpen && w.HandleTextInput(c)) break;
    }

    private void BringToFront(XmlWindow window)
    {
        _windows.Remove(window);
        _windows.Add(window);
    }

    // ── Draw ───────────────────────────────────────────────────────

    public override void Draw()
    {
        if (_font == null) return;
        EnsureResources();
        if (_sb == null || _pixel == null) return;

        _sb.Begin(
            samplerState: Math.Abs(GetUiScale() - 1f) > 0.01f ? SamplerState.LinearClamp : SamplerState.PointClamp,
            rasterizerState: new RasterizerState { ScissorTestEnable = true },
            transformMatrix: Matrix.CreateScale(GetUiScale(), GetUiScale(), 1f));

        foreach (var w in _windows)
            w.Draw(_sb, _pixel, _font);

        _sb.End();
    }

    // ── Helpers ─────────────────────────────────────────────────────

    private void ResolveTextures(UIElement element)
    {
        if (element is UIImage img && img.Texture == null && img.TexturePath != null)
            img.Texture = _assets?.LoadFromFile(img.TexturePath);

        if (element is UIPanel panel)
        {
            foreach (var child in panel.Children)
                ResolveTextures(child);
        }
    }
}
