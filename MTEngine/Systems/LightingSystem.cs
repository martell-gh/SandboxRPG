using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MTEngine.Components;
using MTEngine.Core;
using MTEngine.ECS;
using MTEngine.Items;
using MTEngine.Rendering;
using MTEngine.World;

namespace MTEngine.Systems;

public class LightingSystem : GameSystem
{
    private const float ShadowFeatherWidth = 14f;

    public override void Draw() { }

    private RenderTarget2D? _lightRT;
    private RenderTarget2D? _singleLightRT;
    private Texture2D? _lightCircle;
    private SpriteBatch? _sb;
    private Camera? _camera;
    private TileMapRenderer? _tileMapRenderer;
    private BasicEffect? _shadowEffect;
    private readonly List<VertexPositionColor> _shadowVertices = new();
    private readonly List<VertexPositionColor> _shadowFeatherVertices = new();
    private readonly List<OccluderEdgeCollector.Edge> _edges = new();
    private VertexPositionColor[] _vertexBuffer = Array.Empty<VertexPositionColor>();
    private VertexPositionColor[] _featherBuffer = Array.Empty<VertexPositionColor>();

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

    public void BuildLightMap(GraphicsDevice gd)
    {
        if (!IsEnabled) return;

        _sb ??= ServiceLocator.Get<SpriteBatch>();
        _camera ??= ServiceLocator.Get<Camera>();
        EnsureRT(gd);

        gd.SetRenderTarget(_lightRT);
        gd.Clear(AmbientColor);

        foreach (var entity in World.GetEntities())
        {
            var lt = entity.GetComponent<LightComponent>();
            if (lt == null || !lt.Enabled) continue;
            if (!TryGetLightPosition(entity, out var position)) continue;

            BuildSingleLight(gd, position, lt);

            gd.SetRenderTarget(_lightRT);
            _sb.Begin(
                sortMode: SpriteSortMode.Deferred,
                blendState: BlendState.Additive,
                samplerState: SamplerState.LinearClamp
            );
            _sb.Draw(_singleLightRT!, gd.Viewport.Bounds, Color.White);
            _sb.End();
        }
    }

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
        _singleLightRT?.Dispose();
        _lightRT = new RenderTarget2D(
            gd,
            vp.Width,
            vp.Height,
            false,
            gd.PresentationParameters.BackBufferFormat,
            DepthFormat.None,
            0,
            RenderTargetUsage.PreserveContents);
        _singleLightRT = new RenderTarget2D(
            gd,
            vp.Width,
            vp.Height,
            false,
            gd.PresentationParameters.BackBufferFormat,
            DepthFormat.None,
            0,
            RenderTargetUsage.PreserveContents);
        _lightCircle ??= BuildLightCircle(gd, 256);
    }

    private void BuildSingleLight(GraphicsDevice gd, Vector2 lightPos, LightComponent light)
    {
        _camera ??= ServiceLocator.Get<Camera>();
        _tileMapRenderer ??= World.GetSystem<TileMapRenderer>();
        if (_camera == null || _tileMapRenderer?.TileMap == null || _singleLightRT == null)
            return;

        gd.SetRenderTarget(_singleLightRT);
        gd.Clear(Color.Transparent);

        _sb!.Begin(
            sortMode: SpriteSortMode.Deferred,
            blendState: BlendState.Additive,
            samplerState: SamplerState.LinearClamp,
            transformMatrix: _camera.GetViewMatrix()
        );

        var r = light.Radius;
        var dest = new Rectangle(
            (int)(lightPos.X - r), (int)(lightPos.Y - r),
            (int)(r * 2), (int)(r * 2)
        );
        _sb.Draw(_lightCircle!, dest, light.Color * light.Intensity);
        _sb.End();

        var map = _tileMapRenderer.TileMap;
        var viewport = gd.Viewport;
        var topLeft = _camera.ScreenToWorld(Vector2.Zero);
        var bottomRight = _camera.ScreenToWorld(new Vector2(viewport.Width, viewport.Height));

        // Expand scan area so shadows from off-screen walls are visible
        var startX = Math.Max(0, (int)MathF.Floor(topLeft.X / map.TileSize) - 3);
        var startY = Math.Max(0, (int)MathF.Floor(topLeft.Y / map.TileSize) - 3);
        var endX = Math.Min(map.Width - 1, (int)MathF.Ceiling(bottomRight.X / map.TileSize) + 3);
        var endY = Math.Min(map.Height - 1, (int)MathF.Ceiling(bottomRight.Y / map.TileSize) + 3);
        var shadowLength = MathF.Max(map.Width, map.Height) * map.TileSize * 2f;

        _shadowEffect ??= BuildShadowEffect(gd);
        _shadowEffect.View = _camera.GetViewMatrix();
        _shadowEffect.Projection = Matrix.CreateOrthographicOffCenter(
            0f, viewport.Width, viewport.Height, 0f, 0f, 1f);

        var lightTile = map.WorldToTile(lightPos);
        if (!map.IsInBounds(lightTile.X, lightTile.Y))
            return;

        OccluderEdgeCollector.Collect(map, lightPos, startX, startY, endX, endY, _edges);

        _shadowVertices.Clear();
        _shadowFeatherVertices.Clear();

        foreach (var edge in _edges)
        {
            TileShadowGeometry.AppendEdgeShadow(
                _shadowVertices,
                null,
                lightPos,
                edge.A, edge.B,
                shadowLength,
                0f,
                Color.Black,
                default,
                default);
        }

        DrawShadowVertices(gd);
    }

    private void DrawShadowVertices(GraphicsDevice gd)
    {
        if (_shadowEffect == null || (_shadowVertices.Count == 0 && _shadowFeatherVertices.Count == 0))
            return;

        var previousBlend = gd.BlendState;
        var previousRasterizer = gd.RasterizerState;
        var previousDepth = gd.DepthStencilState;

        gd.RasterizerState = RasterizerState.CullNone;
        gd.DepthStencilState = DepthStencilState.None;

        foreach (var pass in _shadowEffect.CurrentTechnique.Passes)
        {
            pass.Apply();

            if (_shadowVertices.Count > 0)
            {
                gd.BlendState = BlendState.Opaque;
                EnsureBuffer(ref _vertexBuffer, _shadowVertices);
                gd.DrawUserPrimitives(
                    PrimitiveType.TriangleList,
                    _vertexBuffer, 0,
                    _shadowVertices.Count / 3);
            }

            if (_shadowFeatherVertices.Count > 0)
            {
                gd.BlendState = BlendState.AlphaBlend;
                EnsureBuffer(ref _featherBuffer, _shadowFeatherVertices);
                gd.DrawUserPrimitives(
                    PrimitiveType.TriangleList,
                    _featherBuffer, 0,
                    _shadowFeatherVertices.Count / 3);
            }
        }

        gd.BlendState = previousBlend;
        gd.RasterizerState = previousRasterizer;
        gd.DepthStencilState = previousDepth;
    }

    private static void EnsureBuffer(ref VertexPositionColor[] buffer, List<VertexPositionColor> source)
    {
        if (buffer.Length < source.Count)
            buffer = new VertexPositionColor[source.Count * 2];
        source.CopyTo(buffer);
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

    private static BasicEffect BuildShadowEffect(GraphicsDevice graphicsDevice)
    {
        return new BasicEffect(graphicsDevice)
        {
            World = Matrix.Identity,
            VertexColorEnabled = true,
            TextureEnabled = false
        };
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
        if (container == null) return false;

        var carrierTf = container.GetComponent<TransformComponent>();
        if (carrierTf == null) return false;

        if (container.HasComponent<HandsComponent>() || container.HasComponent<EquipmentComponent>())
        {
            position = carrierTf.Position;
            return true;
        }

        return false;
    }
}
