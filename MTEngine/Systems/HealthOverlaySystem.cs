using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MTEngine.Components;
using MTEngine.Core;
using MTEngine.ECS;

namespace MTEngine.Systems;

public class HealthOverlaySystem : GameSystem
{
    private SpriteBatch? _spriteBatch;
    private GraphicsDevice? _graphicsDevice;
    private Texture2D? _pixel;
    private Texture2D? _vignetteFade;
    private float _vignetteInnerRadius = -1f;
    private float _vignetteFadeWidth = -1f;

    public override DrawLayer DrawLayer => DrawLayer.Overlay;

    public override void OnInitialize()
    {
        _graphicsDevice = ServiceLocator.Get<GraphicsDevice>();
    }

    public override void Draw()
    {
        var player = World.GetEntitiesWith<PlayerTagComponent, HealthComponent>().FirstOrDefault();
        var health = player?.GetComponent<HealthComponent>();
        if (health == null)
            return;

        EnsureResources();
        if (_spriteBatch == null || _pixel == null || _graphicsDevice == null)
            return;

        var viewport = _graphicsDevice.Viewport;
        var bounds = viewport.Bounds;

        _spriteBatch.Begin(samplerState: SamplerState.LinearClamp, blendState: BlendState.AlphaBlend);

        if (health.IsDead)
        {
            DrawRect(bounds, new Color(30, 30, 30, 145));
            DrawRadialFade(bounds, new Color(235, 235, 235, 110), innerRadius: 0.78f, fadeWidth: 0.26f);
            _spriteBatch.End();
            return;
        }

        var healthRatio = Math.Clamp(health.Health / Math.Max(1f, health.MaxHealth), 0f, 1f);
        var danger = Math.Clamp((0.50f - healthRatio) / 0.50f, 0f, 1f);
        if (danger <= 0f)
        {
            _spriteBatch.End();
            return;
        }

        var overlay = new Color(145, 10, 10, (int)(danger * danger * 120f));
        var innerRadius = MathHelper.Lerp(1.08f, 0.72f, danger);
        var fadeWidth = MathHelper.Lerp(0.16f, 0.20f, danger);
        DrawRadialFade(bounds, overlay, innerRadius, fadeWidth);

        _spriteBatch.End();
    }

    private void EnsureResources()
    {
        _spriteBatch ??= ServiceLocator.Get<SpriteBatch>();
        if (_pixel == null && _graphicsDevice != null)
        {
            _pixel = new Texture2D(_graphicsDevice, 1, 1);
            _pixel.SetData(new[] { Color.White });
        }

        EnsureVignetteTexture(1.08f, 0.16f);
    }

    private void DrawRect(Rectangle rect, Color color)
    {
        if (rect.Width <= 0 || rect.Height <= 0)
            return;

        _spriteBatch!.Draw(_pixel!, rect, color);
    }

    private void DrawRadialFade(Rectangle bounds, Color color, float innerRadius, float fadeWidth)
    {
        EnsureVignetteTexture(innerRadius, fadeWidth);
        _spriteBatch!.Draw(_vignetteFade!, bounds, null, color);
    }

    private void EnsureVignetteTexture(float innerRadius, float fadeWidth)
    {
        if (_graphicsDevice == null)
            return;

        innerRadius = Math.Clamp(innerRadius, 0f, 2f);
        fadeWidth = Math.Clamp(fadeWidth, 0.01f, 1f);

        if (_vignetteFade != null
            && MathF.Abs(_vignetteInnerRadius - innerRadius) < 0.001f
            && MathF.Abs(_vignetteFadeWidth - fadeWidth) < 0.001f)
            return;

        _vignetteFade?.Dispose();
        _vignetteFade = CreateVignetteTexture(_graphicsDevice, 256, innerRadius, fadeWidth);
        _vignetteInnerRadius = innerRadius;
        _vignetteFadeWidth = fadeWidth;
    }

    private static Texture2D CreateVignetteTexture(GraphicsDevice graphicsDevice, int size, float innerRadius, float fadeWidth)
    {
        var texture = new Texture2D(graphicsDevice, size, size);
        var pixels = new Color[size * size];

        for (var y = 0; y < size; y++)
        {
            var ny = y / (float)Math.Max(1, size - 1);

            for (var x = 0; x < size; x++)
            {
                var nx = x / (float)Math.Max(1, size - 1);
                var dx = (nx - 0.5f) * 2f;
                var dy = (ny - 0.5f) * 2f;

                var distance = MathF.Sqrt((dx * dx) + (dy * dy));
                var alphaFactor = Math.Clamp((distance - innerRadius) / fadeWidth, 0f, 1f);
                alphaFactor = MathF.Pow(alphaFactor, 2.8f);

                var alpha = (byte)(alphaFactor * 255f);
                pixels[(y * size) + x] = new Color(alpha, alpha, alpha, alpha);
            }
        }

        texture.SetData(pixels);
        return texture;
    }
}
