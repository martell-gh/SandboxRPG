using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MTEngine.Components;
using MTEngine.Core;
using MTEngine.ECS;
using MTEngine.Rendering;

namespace MTEngine.Systems;

public class LightingSystem : GameSystem
{
    // Этот Draw пустой — система управляется из GameEngine.Draw()
    public override void Draw() { }

    private RenderTarget2D? _lightRT;
    private Texture2D? _lightCircle;
    private SpriteBatch? _sb;
    private Camera? _camera;

    public Color AmbientColor { get; set; } = Color.White;
    public bool IsEnabled { get; set; } = true;

    // Multiply: result = scene * lightmap
    private static readonly BlendState MultiplyBlend = new()
    {
        ColorBlendFunction = BlendFunction.Add,
        ColorSourceBlend = Blend.DestinationColor,
        ColorDestinationBlend = Blend.Zero,
        AlphaBlendFunction = BlendFunction.Add,
        AlphaSourceBlend = Blend.One,
        AlphaDestinationBlend = Blend.Zero
    };

    // Вызывается из GameEngine.Draw() ПОСЛЕ того как сцена уже на экране
    public void Apply(GraphicsDevice gd)
    {
        if (!IsEnabled) return;

        _sb ??= ServiceLocator.Get<SpriteBatch>();
        _camera ??= ServiceLocator.Get<Camera>();

        EnsureRT(gd);
        BuildLightMap(gd);
        ApplyMultiply(gd);
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

    private void BuildLightMap(GraphicsDevice gd)
    {
        // Рисуем в lightRT: ambient + additive точечные источники
        gd.SetRenderTarget(_lightRT);
        gd.Clear(AmbientColor);

        _sb!.Begin(
            sortMode: SpriteSortMode.Deferred,
            blendState: BlendState.Additive,
            samplerState: SamplerState.LinearClamp,
            transformMatrix: _camera!.GetViewMatrix()
        );

        foreach (var entity in World.GetEntitiesWith<TransformComponent, LightComponent>())
        {
            var tf = entity.GetComponent<TransformComponent>()!;
            var lt = entity.GetComponent<LightComponent>()!;
            if (!lt.Enabled) continue;

            float r = lt.Radius;
            var dest = new Rectangle(
                (int)(tf.Position.X - r), (int)(tf.Position.Y - r),
                (int)(r * 2), (int)(r * 2)
            );
            _sb.Draw(_lightCircle!, dest, lt.Color * lt.Intensity);
        }

        _sb.End();

        // Возвращаемся на backbuffer
        gd.SetRenderTarget(null);
    }

    private void ApplyMultiply(GraphicsDevice gd)
    {
        // Накладываем lightRT на уже нарисованную сцену через multiply
        _sb!.Begin(blendState: MultiplyBlend, samplerState: SamplerState.PointClamp);
        _sb.Draw(_lightRT!, gd.Viewport.Bounds, Color.White);
        _sb.End();
    }

    // Мягкий круг: яркий в центре, затухает к краям
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
                t = t * t; // квадратное затухание — более реалистично
                pixels[y * size + x] = Color.White * t;
            }

        tex.SetData(pixels);
        return tex;
    }
}