using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MTEngine.Components;
using MTEngine.Core;
using MTEngine.ECS;
using MTEngine.Npc;
using MTEngine.Rendering;

namespace MTEngine.Systems;

public class SpeechBubbleSystem : GameSystem
{
    public override DrawLayer DrawLayer => DrawLayer.Overlay;

    private const float CharactersPerSecond = 24f;
    private const float HoldSeconds = 3.4f;
    private const float FadeSeconds = 0.65f;
    private const int MaxBubbleWidth = 220;
    private const int BubblePaddingX = 9;
    private const int BubblePaddingY = 7;
    private const int BubbleGap = 5;

    private readonly List<SpeechBubbleEntry> _entries = new();
    private readonly Dictionary<int, float> _ambientCooldowns = new();
    private readonly Random _rng = new();
    private SpriteBatch? _spriteBatch;
    private SpriteFont? _font;
    private Camera? _camera;
    private GraphicsDevice? _graphicsDevice;
    private Texture2D? _pixel;
    private int _sequence;

    private static readonly string[] AmbientLines =
    {
        "Хороший день.",
        "Скукота...",
        "Надо бы пройтись.",
        "Интересно, что нового?",
        "Погода ничего."
    };

    public void SetFont(SpriteFont font) => _font = font;

    public override void OnInitialize()
    {
        _camera = ServiceLocator.Get<Camera>();
        _graphicsDevice = ServiceLocator.Get<GraphicsDevice>();
    }

    public override void Update(float deltaTime)
    {
        for (var i = _entries.Count - 1; i >= 0; i--)
        {
            var entry = _entries[i];
            entry.Age += deltaTime;
            if (entry.Age >= entry.Lifetime)
                _entries.RemoveAt(i);
        }

        UpdateAmbientSpeech(deltaTime);
    }

    public void ShowNear(Entity speaker, string text)
    {
        text = SanitizeText(LocalizationManager.T(text));
        if (speaker == null || string.IsNullOrWhiteSpace(text))
            return;

        var typingDuration = Math.Max(0.2f, text.Length / CharactersPerSecond);
        _entries.Add(new SpeechBubbleEntry
        {
            SpeakerId = speaker.Id,
            Text = text,
            TypingDuration = typingDuration,
            Lifetime = typingDuration + HoldSeconds + FadeSeconds,
            Sequence = ++_sequence
        });
    }

    public static void Show(Entity speaker, string text)
    {
        if (!ServiceLocator.Has<SpeechBubbleSystem>())
            return;

        ServiceLocator.Get<SpeechBubbleSystem>().ShowNear(speaker, text);
    }

    public override void Draw()
    {
        if (_entries.Count == 0 || _font == null)
            return;

        _spriteBatch ??= ServiceLocator.Get<SpriteBatch>();
        _camera ??= ServiceLocator.Get<Camera>();
        _graphicsDevice ??= ServiceLocator.Get<GraphicsDevice>();
        EnsurePixel();

        if (_pixel == null || _camera == null || _graphicsDevice == null)
            return;

        var layouts = BuildBubbleLayouts();
        var occupied = new List<Rectangle>();

        _spriteBatch.Begin(samplerState: SamplerState.PointClamp);

        foreach (var layout in layouts)
        {
            var rect = AvoidOverlaps(layout.Rect, occupied);
            occupied.Add(Inflate(rect, BubbleGap));
            DrawBubble(rect, layout.Lines, layout.Entry, layout.DrawTail, layout.AnchorScreen);
        }

        _spriteBatch.End();
    }

    private List<SpeechBubbleLayout> BuildBubbleLayouts()
    {
        var layouts = new List<SpeechBubbleLayout>();
        if (_camera == null)
            return layouts;

        foreach (var group in _entries.GroupBy(e => e.SpeakerId).ToList())
        {
            var speaker = FindSpeaker(group.Key);
            if (speaker == null || !TryGetBubbleAnchor(speaker, out var anchorWorld))
                continue;

            var anchorScreen = _camera.WorldToScreen(anchorWorld);
            var ordered = group.OrderByDescending(e => e.Sequence).ToList();
            var stackY = anchorScreen.Y - 10f;

            for (var i = 0; i < ordered.Count; i++)
            {
                var entry = ordered[i];
                var lines = WrapText(entry.Text, MaxBubbleWidth - BubblePaddingX * 2);
                var size = MeasureBubble(lines);
                var rect = new Rectangle(
                    (int)MathF.Round(anchorScreen.X - size.X * 0.5f),
                    (int)MathF.Round(stackY - size.Y),
                    (int)MathF.Ceiling(size.X),
                    (int)MathF.Ceiling(size.Y));

                layouts.Add(new SpeechBubbleLayout(rect, lines, entry, i == 0, anchorScreen));
                stackY = rect.Y - BubbleGap;
            }
        }

        return layouts
            .OrderByDescending(l => l.AnchorScreen.Y)
            .ThenByDescending(l => l.Entry.Sequence)
            .ToList();
    }

    private static Rectangle AvoidOverlaps(Rectangle rect, List<Rectangle> occupied)
    {
        var adjusted = rect;
        var changed = true;
        var guard = 0;
        while (changed && guard++ < 32)
        {
            changed = false;
            foreach (var other in occupied)
            {
                if (!adjusted.Intersects(other))
                    continue;

                adjusted.Y = other.Y - adjusted.Height - BubbleGap;
                changed = true;
            }
        }

        return adjusted;
    }

    private static Rectangle Inflate(Rectangle rect, int amount)
        => new(rect.X - amount, rect.Y - amount, rect.Width + amount * 2, rect.Height + amount * 2);

    private void UpdateAmbientSpeech(float deltaTime)
    {
        if (_font == null || _camera == null || _graphicsDevice == null)
            return;

        foreach (var npc in World.GetEntitiesWith<NpcTagComponent, TransformComponent>())
        {
            if (!NpcLod.IsActive(npc)
                || npc.GetComponent<HealthComponent>()?.IsDead == true
                || HasActiveBubble(npc.Id)
                || npc.GetComponent<NpcIntentComponent>()?.Action != ScheduleAction.Wander
                || !IsOnScreen(npc, margin: 48))
            {
                continue;
            }

            var cooldown = _ambientCooldowns.GetValueOrDefault(npc.Id, 8f + (float)_rng.NextDouble() * 16f) - deltaTime;
            if (cooldown > 0f)
            {
                _ambientCooldowns[npc.Id] = cooldown;
                continue;
            }

            ShowNear(npc, AmbientLines[_rng.Next(AmbientLines.Length)]);
            _ambientCooldowns[npc.Id] = 18f + (float)_rng.NextDouble() * 28f;
        }
    }

    private bool HasActiveBubble(int speakerId)
        => _entries.Any(e => e.SpeakerId == speakerId && e.Age < e.Lifetime - FadeSeconds);

    private bool IsOnScreen(Entity entity, int margin)
    {
        if (!TryGetBubbleAnchor(entity, out var anchorWorld) || _camera == null || _graphicsDevice == null)
            return false;

        var screen = _camera.WorldToScreen(anchorWorld);
        var viewport = _graphicsDevice.Viewport.Bounds;
        return screen.X >= viewport.Left - margin
               && screen.X <= viewport.Right + margin
               && screen.Y >= viewport.Top - margin
               && screen.Y <= viewport.Bottom + margin;
    }

    private Entity? FindSpeaker(int speakerId)
        => World.GetEntities().FirstOrDefault(e => e.Active && e.Id == speakerId);

    private static bool TryGetBubbleAnchor(Entity entity, out Vector2 anchorWorld)
    {
        var transform = entity.GetComponent<TransformComponent>();
        if (transform == null)
        {
            anchorWorld = Vector2.Zero;
            return false;
        }

        if (entity.GetComponent<ColliderComponent>() is { } collider)
        {
            var bounds = collider.GetBounds(transform.Position);
            anchorWorld = new Vector2(bounds.Center.X, bounds.Top - 6f);
            return true;
        }

        anchorWorld = transform.Position + new Vector2(0f, -32f);
        return true;
    }

    private List<string> WrapText(string text, int maxWidth)
    {
        var lines = new List<string>();
        if (_font == null)
            return lines;

        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var line = "";
        foreach (var word in words)
        {
            var candidate = string.IsNullOrEmpty(line) ? word : $"{line} {word}";
            if (_font.MeasureString(candidate).X > maxWidth && !string.IsNullOrEmpty(line))
            {
                lines.Add(line);
                line = word;
            }
            else
            {
                line = candidate;
            }
        }

        if (!string.IsNullOrEmpty(line))
            lines.Add(line);

        if (lines.Count == 0)
            lines.Add("");

        return lines;
    }

    private Vector2 MeasureBubble(List<string> lines)
    {
        var textWidth = 0f;
        var lineHeight = GetLineHeight();
        foreach (var line in lines)
            textWidth = Math.Max(textWidth, _font!.MeasureString(line).X);

        return new Vector2(
            MathF.Max(58f, textWidth + BubblePaddingX * 2),
            lines.Count * lineHeight + BubblePaddingY * 2);
    }

    private void DrawBubble(Rectangle rect, List<string> lines, SpeechBubbleEntry entry, bool drawTail, Vector2 anchorScreen)
    {
        if (_spriteBatch == null || _font == null || _pixel == null)
            return;

        var fadeT = Math.Clamp((entry.Age - (entry.TypingDuration + HoldSeconds)) / FadeSeconds, 0f, 1f);
        var appearT = Math.Clamp(entry.Age / 0.18f, 0f, 1f);
        var alpha = appearT * (1f - fadeT);
        if (alpha <= 0f)
            return;

        var fill = new Color(18, 24, 22) * (0.76f * alpha);
        var border = new Color(132, 176, 150) * (0.85f * alpha);
        var shadow = Color.Black * (0.34f * alpha);
        var textColor = new Color(232, 244, 232) * alpha;

        _spriteBatch.Draw(_pixel, new Rectangle(rect.X + 2, rect.Y + 2, rect.Width, rect.Height), shadow);
        DrawSoftPanel(rect, fill, border);

        if (drawTail)
        {
            var tailX = Math.Clamp((int)MathF.Round(anchorScreen.X), rect.X + 12, rect.Right - 12);
            _spriteBatch.Draw(_pixel, new Rectangle(tailX - 4, rect.Bottom - 1, 8, 4), fill);
            _spriteBatch.Draw(_pixel, new Rectangle(tailX - 2, rect.Bottom + 3, 4, 3), fill);
            _spriteBatch.Draw(_pixel, new Rectangle(tailX - 5, rect.Bottom - 1, 1, 4), border);
            _spriteBatch.Draw(_pixel, new Rectangle(tailX + 4, rect.Bottom - 1, 1, 4), border);
        }

        var visibleChars = Math.Clamp((int)MathF.Floor(entry.Age * CharactersPerSecond), 0, entry.Text.Length);
        var remaining = visibleChars;
        var y = rect.Y + (float)BubblePaddingY;
        var lineHeight = GetLineHeight();

        foreach (var line in lines)
        {
            var count = Math.Clamp(remaining, 0, line.Length);
            if (count > 0)
                _spriteBatch.DrawString(_font, line[..count], new Vector2(rect.X + BubblePaddingX, y), textColor);

            remaining -= line.Length + 1;
            y += lineHeight;
        }
    }

    private void DrawSoftPanel(Rectangle rect, Color fill, Color border)
    {
        if (_spriteBatch == null || _pixel == null)
            return;

        _spriteBatch.Draw(_pixel, new Rectangle(rect.X + 2, rect.Y, rect.Width - 4, rect.Height), fill);
        _spriteBatch.Draw(_pixel, new Rectangle(rect.X, rect.Y + 2, rect.Width, rect.Height - 4), fill);

        _spriteBatch.Draw(_pixel, new Rectangle(rect.X + 2, rect.Y, rect.Width - 4, 1), border);
        _spriteBatch.Draw(_pixel, new Rectangle(rect.X + 2, rect.Bottom - 1, rect.Width - 4, 1), border);
        _spriteBatch.Draw(_pixel, new Rectangle(rect.X, rect.Y + 2, 1, rect.Height - 4), border);
        _spriteBatch.Draw(_pixel, new Rectangle(rect.Right - 1, rect.Y + 2, 1, rect.Height - 4), border);
    }

    private float GetLineHeight()
        => MathF.Ceiling(_font?.LineSpacing * 0.82f ?? 16f);

    private void EnsurePixel()
    {
        if (_pixel != null || _graphicsDevice == null)
            return;

        _pixel = new Texture2D(_graphicsDevice, 1, 1);
        _pixel.SetData(new[] { Color.White });
    }

    private static string SanitizeText(string text)
        => text
            .Replace('↻', 'R')
            .Replace('—', '-')
            .Replace('–', '-')
            .Replace('−', '-')
            .Replace('←', '<')
            .Replace('→', '>')
            .Replace('↑', '^')
            .Replace('↓', 'v');

    private sealed class SpeechBubbleEntry
    {
        public int SpeakerId { get; init; }
        public string Text { get; init; } = "";
        public float Age { get; set; }
        public float TypingDuration { get; init; }
        public float Lifetime { get; init; }
        public int Sequence { get; init; }
    }

    private sealed record SpeechBubbleLayout(
        Rectangle Rect,
        List<string> Lines,
        SpeechBubbleEntry Entry,
        bool DrawTail,
        Vector2 AnchorScreen);
}
