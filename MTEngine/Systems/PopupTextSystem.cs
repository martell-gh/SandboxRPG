using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MTEngine.Components;
using MTEngine.Core;
using MTEngine.ECS;
using MTEngine.Rendering;

namespace MTEngine.Systems;

public class PopupTextSystem : GameSystem
{
    public override DrawLayer DrawLayer => DrawLayer.Overlay;

    private readonly List<PopupTextEntry> _entries = new();
    private SpriteBatch? _spriteBatch;
    private SpriteFont? _font;
    private Camera? _camera;

    public void SetFont(SpriteFont font)
    {
        _font = font;
    }

    public void ShowWorld(string text, Vector2 worldPosition, Color? color = null, float lifetime = 1.1f, float riseSpeed = 28f)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        _entries.Add(new PopupTextEntry
        {
            Text = text,
            WorldPosition = worldPosition,
            Color = color ?? new Color(230, 230, 230),
            Lifetime = Math.Max(0.1f, lifetime),
            RiseSpeed = riseSpeed
        });
    }

    public void ShowNear(Entity entity, string text, Color? color = null, float verticalOffset = -18f, float lifetime = 1.1f)
    {
        var transform = entity.GetComponent<TransformComponent>();
        if (transform == null)
            return;

        ShowWorld(text, transform.Position + new Vector2(0f, verticalOffset), color, lifetime);
    }

    public static void Show(string text, Vector2 worldPosition, Color? color = null, float lifetime = 1.1f)
    {
        if (!ServiceLocator.Has<PopupTextSystem>())
            return;

        ServiceLocator.Get<PopupTextSystem>().ShowWorld(text, worldPosition, color, lifetime);
    }

    public static void Show(Entity entity, string text, Color? color = null, float verticalOffset = -18f, float lifetime = 1.1f)
    {
        if (!ServiceLocator.Has<PopupTextSystem>())
            return;

        ServiceLocator.Get<PopupTextSystem>().ShowNear(entity, text, color, verticalOffset, lifetime);
    }

    public override void OnInitialize()
    {
        _camera = ServiceLocator.Get<Camera>();
    }

    public override void Update(float deltaTime)
    {
        for (int i = _entries.Count - 1; i >= 0; i--)
        {
            var entry = _entries[i];
            entry.Age += deltaTime;
            entry.WorldPosition += new Vector2(0f, -entry.RiseSpeed * deltaTime);

            if (entry.Age >= entry.Lifetime)
                _entries.RemoveAt(i);
        }
    }

    public override void Draw()
    {
        if (_entries.Count == 0 || _font == null)
            return;

        _spriteBatch ??= ServiceLocator.Get<SpriteBatch>();
        _camera ??= ServiceLocator.Get<Camera>();

        _spriteBatch.Begin(samplerState: SamplerState.PointClamp);

        foreach (var entry in _entries)
        {
            var ageT = Math.Clamp(entry.Age / entry.Lifetime, 0f, 1f);
            var alpha = 1f - ageT;
            var screenPos = _camera.WorldToScreen(entry.WorldPosition);
            var origin = _font.MeasureString(entry.Text) * 0.5f;
            var shadowColor = Color.Black * (0.35f * alpha);
            var textColor = entry.Color * alpha;

            _spriteBatch.DrawString(_font, entry.Text, screenPos + new Vector2(1f, 1f), shadowColor, -0.06f, origin, 0.9f, SpriteEffects.None, 0f);
            _spriteBatch.DrawString(_font, entry.Text, screenPos, textColor, -0.06f, origin, 0.9f, SpriteEffects.None, 0f);
        }

        _spriteBatch.End();
    }

    private sealed class PopupTextEntry
    {
        public required string Text { get; init; }
        public required Vector2 WorldPosition { get; set; }
        public required Color Color { get; init; }
        public required float Lifetime { get; init; }
        public required float RiseSpeed { get; init; }
        public float Age { get; set; }
    }
}
