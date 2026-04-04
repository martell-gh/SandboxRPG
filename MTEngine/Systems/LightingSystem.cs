using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MTEngine.Components;
using MTEngine.Core;
using MTEngine.ECS;
using MTEngine.Items;
using MTEngine.Rendering;

namespace MTEngine.Systems;

public class LightingSystem : GameSystem
{
    public override void Draw() { }

    private RenderTarget2D? _lightRT;
    private Texture2D? _lightCircle;
    private SpriteBatch? _sb;
    private Camera? _camera;

    public Color AmbientColor { get; set; } = Color.White;
    public bool IsEnabled { get; set; } = true;

    private static readonly BlendState MultiplyBlend = new()
    {
        ColorBlendFunction = BlendFunction.Add,
        ColorSourceBlend = Blend.DestinationColor,
        ColorDestinationBlend = Blend.Zero,
        AlphaBlendFunction = BlendFunction.Add,
        AlphaSourceBlend = Blend.One,
        AlphaDestinationBlend = Blend.Zero
    };

    // Шаг A: строим lightmap в отдельный RT
    // Вызывается ДО переключения на backbuffer
    public void BuildLightMap(GraphicsDevice gd)
    {
        if (!IsEnabled) return;

        _sb ??= ServiceLocator.Get<SpriteBatch>();
        _camera ??= ServiceLocator.Get<Camera>();
        EnsureRT(gd);

        gd.SetRenderTarget(_lightRT);
        gd.Clear(AmbientColor);

        _sb.Begin(
            sortMode: SpriteSortMode.Deferred,
            blendState: BlendState.Additive,
            samplerState: SamplerState.LinearClamp,
            transformMatrix: _camera.GetViewMatrix()
        );

        foreach (var entity in World.GetEntities())
        {
            var lt = entity.GetComponent<LightComponent>();
            if (lt == null || !lt.Enabled)
                continue;

            if (!TryGetLightPosition(entity, out var position))
                continue;

            float r = lt.Radius;
            var dest = new Rectangle(
                (int)(position.X - r), (int)(position.Y - r),
                (int)(r * 2), (int)(r * 2)
            );
            _sb.Draw(_lightCircle!, dest, lt.Color * lt.Intensity);
        }

        _sb.End();
        // НЕ переключаем на null здесь — это сделает GameEngine
    }

    // Шаг B: накладываем lightmap на backbuffer (multiply)
    // Вызывается когда уже на backbuffer и сцена уже нарисована
    public void ApplyLightMap(GraphicsDevice gd)
    {
        if (!IsEnabled || _lightRT == null) return;

        _sb ??= ServiceLocator.Get<SpriteBatch>();

        _sb.Begin(blendState: MultiplyBlend, samplerState: SamplerState.PointClamp);
        _sb.Draw(_lightRT, gd.Viewport.Bounds, Color.White);
        _sb.End();
    }

    private void EnsureRT(GraphicsDevice gd)
    {
        var vp = gd.Viewport;
        if (_lightRT != null && _lightRT.Width == vp.Width && _lightRT.Height == vp.Height)
            return;

        _lightRT?.Dispose();
        _lightRT = new RenderTarget2D(gd, vp.Width, vp.Height);
        _lightCircle ??= BuildLightCircle(gd, 256);
    }

    private static Texture2D BuildLightCircle(GraphicsDevice gd, int size)
    {
        var tex = new Texture2D(gd, size, size);
        var pixels = new Color[size * size];
        float center = size / 2f;

        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dist = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
                float t = 1f - MathHelper.Clamp(dist / center, 0f, 1f);
                t = t * t;
                pixels[y * size + x] = Color.White * t;
            }

        tex.SetData(pixels);
        return tex;
    }

    private static bool TryGetLightPosition(Entity entity, out Vector2 position)
    {
        position = Vector2.Zero;

        if (entity.Active && entity.GetComponent<TransformComponent>() is { } worldTf)
        {
            position = worldTf.Position;
            return true;
        }

        var item = entity.GetComponent<ItemComponent>();
        var container = item?.ContainedIn;
        if (container == null)
            return false;

        var carrierTf = container.GetComponent<TransformComponent>();
        if (carrierTf == null)
            return false;

        if (container.HasComponent<HandsComponent>() || container.HasComponent<EquipmentComponent>())
        {
            position = carrierTf.Position;
            return true;
        }

        return false;
    }
}
