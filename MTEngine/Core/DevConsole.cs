using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace MTEngine.Core;

public static class DevConsole
{
    public static bool DevMode { get; set; } = true;
    public static bool IsOpen { get; private set; } = false;

    private static readonly List<string> _history = new();
    private static string _input = "";
    private static int _scrollOffset = 0;
    private static SpriteFont? _font;
    private static SpriteBatch? _spriteBatch;
    private static GraphicsDevice? _graphics;

    private static KeyboardState _prev;
    private static KeyboardState _current;

    // колбэк для обработки команд
    public static Action<string>? OnCommand { get; set; }

    public static void SetFont(SpriteFont font, GraphicsDevice graphics)
    {
        _font = font;
        _graphics = graphics;
        _spriteBatch = ServiceLocator.Get<SpriteBatch>();
    }

    public static void Log(string message)
    {
        _history.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
        Console.WriteLine($"[DEV] {message}");
    }

    public static void Clear() => _history.Clear();

    public static void Update()
    {
        if (!DevMode) return;

        _prev = _current;
        _current = Keyboard.GetState();

        // тильда — открыть/закрыть
        if (IsPressed(Keys.OemTilde))
        {
            IsOpen = !IsOpen;
            _input = "";
        }

        if (!IsOpen) return;

        // скролл
        if (IsPressed(Keys.PageUp)) _scrollOffset = Math.Max(0, _scrollOffset - 5);
        if (IsPressed(Keys.PageDown)) _scrollOffset = Math.Min(Math.Max(0, _history.Count - 20), _scrollOffset + 5);

        // ввод текста
        foreach (var key in _current.GetPressedKeys())
        {
            if (!IsPressed(key)) continue;

            if (key == Keys.Back && _input.Length > 0)
            {
                _input = _input[..^1];
                continue;
            }

            if (key == Keys.Enter)
            {
                if (_input.Trim().Length > 0)
                {
                    Log($"> {_input}");
                    OnCommand?.Invoke(_input.Trim());
                    _input = "";
                    _scrollOffset = Math.Max(0, _history.Count - 20);
                }
                continue;
            }

            var ch = KeyToChar(key, _current.IsKeyDown(Keys.LeftShift) || _current.IsKeyDown(Keys.RightShift));
            if (ch != '\0') _input += ch;
        }
    }

    public static void Draw()
    {
        if (!DevMode || _font == null || _spriteBatch == null || _graphics == null) return;

        if (IsOpen)
            DrawFullConsole();
        else
            DrawMiniConsole();
    }

    private static void DrawMiniConsole()
    {
        _spriteBatch!.Begin();

        // последние 10 строк
        var lines = _history.TakeLast(10).ToList();
        float y = 10f;
        foreach (var line in lines)
        {
            _spriteBatch.DrawString(_font!, line, new Vector2(11, y + 1), Color.Black * 0.5f);
            _spriteBatch.DrawString(_font!, line, new Vector2(10, y), Color.LimeGreen);
            y += 16f;
        }

        _spriteBatch.End();
    }

    private static void DrawFullConsole()
    {
        var width = _graphics!.Viewport.Width;
        var height = _graphics.Viewport.Height / 2;

        _spriteBatch!.Begin();

        // фон
        DrawRect(0, 0, width, height, Color.Black * 0.85f);

        // заголовок
        DrawRect(0, 0, width, 20, Color.DarkGreen * 0.9f);
        _spriteBatch.DrawString(_font!, "[ DEV CONSOLE ] PageUp/PageDown - scroll | Enter - command | ` - close",
    new Vector2(5, 2), Color.LimeGreen);

        // логи
        int maxLines = (height - 50) / 16;
        int startIdx = Math.Max(0, _history.Count - maxLines - _scrollOffset);
        int endIdx = Math.Min(_history.Count, startIdx + maxLines);

        float y = 25f;
        for (int i = startIdx; i < endIdx; i++)
        {
            _spriteBatch.DrawString(_font!, _history[i], new Vector2(5, y), Color.LimeGreen);
            y += 16f;
        }

        // поле ввода
        DrawRect(0, height - 26, width, 26, Color.DarkGreen * 0.7f);
        _spriteBatch.DrawString(_font!, $"> {_input}_", new Vector2(5, height - 22), Color.White);

        // скроллбар
        if (_history.Count > maxLines)
        {
            var barHeight = height - 50;
            var thumbHeight = Math.Max(20, barHeight * maxLines / _history.Count);
            var thumbY = 25 + (barHeight - thumbHeight) * _scrollOffset / Math.Max(1, _history.Count - maxLines);
            DrawRect(width - 8, 25, 8, barHeight, Color.DarkGreen * 0.5f);
            DrawRect(width - 8, (int)thumbY, 8, thumbHeight, Color.LimeGreen * 0.8f);
        }

        _spriteBatch.End();
    }

    private static Texture2D? _pixel;

    private static void DrawRect(int x, int y, int w, int h, Color color)
    {
        if (_pixel == null)
        {
            _pixel = new Texture2D(_graphics!, 1, 1);
            _pixel.SetData(new[] { Color.White });
        }
        _spriteBatch!.Draw(_pixel, new Rectangle(x, y, w, h), color);
    }

    private static bool IsPressed(Keys key)
        => _current.IsKeyDown(key) && _prev.IsKeyUp(key);

    private static char KeyToChar(Keys key, bool shift)
    {
        if (key >= Keys.A && key <= Keys.Z)
            return shift ? (char)('A' + (key - Keys.A)) : (char)('a' + (key - Keys.A));
        if (key >= Keys.D0 && key <= Keys.D9)
            return shift ? "!@#$%^&*()"[key - Keys.D0] : (char)('0' + (key - Keys.D0));
        return key switch
        {
            Keys.Space => ' ',
            Keys.OemPeriod => shift ? '>' : '.',
            Keys.OemComma => shift ? '<' : ',',
            Keys.OemMinus => shift ? '_' : '-',
            Keys.OemPlus => shift ? '+' : '=',
            Keys.OemQuestion => shift ? '?' : '/',
            _ => '\0'
        };
    }
}