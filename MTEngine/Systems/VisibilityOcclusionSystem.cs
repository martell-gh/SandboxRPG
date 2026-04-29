using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Linq;
using MTEngine.Components;
using MTEngine.Core;
using MTEngine.ECS;
using MTEngine.Rendering;
using MTEngine.World;

namespace MTEngine.Systems;

public class VisibilityOcclusionSystem : GameSystem
{
    private const float FeatherWidth = 10f;

    private GraphicsDevice? _graphicsDevice;
    private SpriteBatch? _spriteBatch;
    private Camera? _camera;
    private TileMapRenderer? _tileMapRenderer;
    private BasicEffect? _effect;
    private AssetManager? _assets;
    private PrototypeManager? _prototypes;
    private readonly List<VertexPositionColor> _mainVertices = new();
    private readonly List<VertexPositionColor> _featherVertices = new();
    private readonly List<OccluderEdgeCollector.Edge> _edges = new();
    private VertexPositionColor[] _mainBuffer = Array.Empty<VertexPositionColor>();
    private VertexPositionColor[] _featherBuffer = Array.Empty<VertexPositionColor>();

    public Color OcclusionColor { get; set; } = Color.Black;

    public override void OnInitialize()
    {
        _graphicsDevice = ServiceLocator.Get<GraphicsDevice>();
        _camera = ServiceLocator.Get<Camera>();
    }

    public override void Draw()
    {
        if (ServiceLocator.Has<IGodModeService>() && ServiceLocator.Get<IGodModeService>().IsGodModeActive)
            return;

        _graphicsDevice ??= ServiceLocator.Get<GraphicsDevice>();
        _spriteBatch ??= ServiceLocator.Get<SpriteBatch>();
        _camera ??= ServiceLocator.Get<Camera>();
        _assets ??= ServiceLocator.Get<AssetManager>();
        _prototypes ??= ServiceLocator.Get<PrototypeManager>();
        _tileMapRenderer ??= World.GetSystem<TileMapRenderer>();

        if (_graphicsDevice == null || _camera == null || _tileMapRenderer?.TileMap == null)
            return;

        TransformComponent? playerTransform = null;
        foreach (var entity in World.GetEntities())
        {
            if (entity.Active && entity.HasComponent<PlayerTagComponent>())
            {
                playerTransform = entity.GetComponent<TransformComponent>();
                if (playerTransform != null) break;
            }
        }
        if (playerTransform == null) return;

        var map = _tileMapRenderer.TileMap;
        var playerPos = playerTransform.Position;
        var playerTile = map.WorldToTile(playerPos);
        if (!map.IsInBounds(playerTile.X, playerTile.Y)) return;

        var viewport = _graphicsDevice.Viewport;
        var topLeft = _camera.ScreenToWorld(Vector2.Zero);
        var bottomRight = _camera.ScreenToWorld(new Vector2(viewport.Width, viewport.Height));

        var startX = Math.Max(0, (int)MathF.Floor(topLeft.X / map.TileSize) - 3);
        var startY = Math.Max(0, (int)MathF.Floor(topLeft.Y / map.TileSize) - 3);
        var endX = Math.Min(map.Width - 1, (int)MathF.Ceiling(bottomRight.X / map.TileSize) + 3);
        var endY = Math.Min(map.Height - 1, (int)MathF.Ceiling(bottomRight.Y / map.TileSize) + 3);
        var shadowLength = MathF.Max(map.Width, map.Height) * map.TileSize * 2f;
        var visibleWorldBounds = new Rectangle(
            (int)topLeft.X - map.TileSize,
            (int)topLeft.Y - map.TileSize,
            (int)(bottomRight.X - topLeft.X) + map.TileSize * 2,
            (int)(bottomRight.Y - topLeft.Y) + map.TileSize * 2);

        // Collect silhouette edges facing the player
        OccluderEdgeCollector.Collect(map, playerPos, startX, startY, endX, endY, _edges);
        EntityOcclusionHelper.AppendVisionBlockerEdges(World, playerPos, visibleWorldBounds, _edges);

        _mainVertices.Clear();
        _featherVertices.Clear();

        var featherInner = OcclusionColor * 0.28f;

        foreach (var edge in _edges)
        {
            TileShadowGeometry.AppendEdgeShadow(
                _mainVertices,
                _featherVertices,
                playerPos,
                edge.A, edge.B,
                shadowLength,
                FeatherWidth,
                OcclusionColor,
                featherInner,
                Color.Transparent);
        }

        if (_mainVertices.Count == 0 && _featherVertices.Count == 0)
            return;

        _effect ??= BuildEffect(_graphicsDevice);
        _effect.View = _camera.GetViewMatrix();
        _effect.Projection = Matrix.CreateOrthographicOffCenter(
            0f, viewport.Width, viewport.Height, 0f, 0f, 1f);

        var previousBlend = _graphicsDevice.BlendState;
        var previousRasterizer = _graphicsDevice.RasterizerState;
        var previousDepth = _graphicsDevice.DepthStencilState;

        _graphicsDevice.BlendState = BlendState.AlphaBlend;
        _graphicsDevice.RasterizerState = RasterizerState.CullNone;
        _graphicsDevice.DepthStencilState = DepthStencilState.None;

        foreach (var pass in _effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            if (_mainVertices.Count > 0)
            {
                EnsureBuffer(ref _mainBuffer, _mainVertices);
                _graphicsDevice.DrawUserPrimitives(
                    PrimitiveType.TriangleList,
                    _mainBuffer, 0,
                    _mainVertices.Count / 3);
            }

            if (_featherVertices.Count > 0)
            {
                EnsureBuffer(ref _featherBuffer, _featherVertices);
                _graphicsDevice.DrawUserPrimitives(
                    PrimitiveType.TriangleList,
                    _featherBuffer, 0,
                    _featherVertices.Count / 3);
            }
        }

        _graphicsDevice.BlendState = previousBlend;
        _graphicsDevice.RasterizerState = previousRasterizer;
        _graphicsDevice.DepthStencilState = previousDepth;

        // Redraw visible opaque tiles on top so walls aren't hidden by their own shadows
        RedrawVisibleOpaqueTiles(map, playerTile, visibleWorldBounds);
        RedrawVisibleVisionBlockers(map, playerPos, visibleWorldBounds);
    }

    private static BasicEffect BuildEffect(GraphicsDevice graphicsDevice)
    {
        return new BasicEffect(graphicsDevice)
        {
            World = Matrix.Identity,
            VertexColorEnabled = true,
            TextureEnabled = false
        };
    }

    private static void EnsureBuffer(ref VertexPositionColor[] buffer, List<VertexPositionColor> source)
    {
        if (buffer.Length < source.Count)
            buffer = new VertexPositionColor[source.Count * 2];
        source.CopyTo(buffer);
    }

    private void RedrawVisibleOpaqueTiles(TileMap map, Point playerTile, Rectangle visibleArea)
    {
        if (_spriteBatch == null || _camera == null || _assets == null || _prototypes == null)
            return;

        _spriteBatch.Begin(
            sortMode: SpriteSortMode.Deferred,
            blendState: BlendState.AlphaBlend,
            samplerState: SamplerState.PointClamp,
            transformMatrix: _camera.GetViewMatrix());

        var playerWorld = new Vector2(
            playerTile.X * map.TileSize + map.TileSize * 0.5f,
            playerTile.Y * map.TileSize + map.TileSize * 0.5f);

        map.DrawFilteredWithPrototypes(_spriteBatch, visibleArea, _prototypes, _assets,
            (x, y, tile) => tile.Opaque
                            && (tile.ProtoId == null || _prototypes.GetTile(tile.ProtoId)?.HiddenInGame != true)
                            && IsOpaqueTileVisible(map, playerWorld, x, y));

        _spriteBatch.End();
    }

    private static bool IsOpaqueTileVisible(TileMap map, Vector2 originWorld, int tileX, int tileY)
    {
        var left = tileX * map.TileSize + 1f;
        var top = tileY * map.TileSize + 1f;
        var right = (tileX + 1) * map.TileSize - 1f;
        var bottom = (tileY + 1) * map.TileSize - 1f;
        var centerX = (left + right) * 0.5f;
        var centerY = (top + bottom) * 0.5f;

        Span<Vector2> samples = stackalloc Vector2[8];
        samples[0] = new Vector2(left, top);
        samples[1] = new Vector2(centerX, top);
        samples[2] = new Vector2(right, top);
        samples[3] = new Vector2(left, centerY);
        samples[4] = new Vector2(right, centerY);
        samples[5] = new Vector2(left, bottom);
        samples[6] = new Vector2(centerX, bottom);
        samples[7] = new Vector2(right, bottom);

        for (var i = 0; i < samples.Length; i++)
        {
            if (map.HasWorldLineOfSight(originWorld, samples[i]))
                return true;
        }

        return false;
    }

    private void RedrawVisibleVisionBlockers(TileMap map, Vector2 playerWorld, Rectangle visibleArea)
    {
        if (_spriteBatch == null || _camera == null)
            return;

        _spriteBatch.Begin(
            sortMode: SpriteSortMode.Deferred,
            blendState: BlendState.AlphaBlend,
            samplerState: SamplerState.PointClamp,
            transformMatrix: _camera.GetViewMatrix());

        var renderables = World.GetEntitiesWith<TransformComponent, SpriteComponent>()
            .Where(entity => EntityOcclusionHelper.IsVisionBlocker(entity)
                             && entity.GetComponent<SpriteComponent>()?.Visible == true
                             && entity.GetComponent<SpriteComponent>()?.Texture != null);

        foreach (var entity in renderables)
        {
            var transform = entity.GetComponent<TransformComponent>()!;
            var sprite = entity.GetComponent<SpriteComponent>()!;
            if (!EntityOcclusionHelper.TryGetBlockerBounds(entity, out var bounds))
                continue;

            if (!bounds.Intersects(visibleArea))
                continue;

            var targetPoint = bounds.Center.ToVector2();
            if (!EntityOcclusionHelper.HasWorldLineOfSight(map, World, playerWorld, targetPoint, entity))
                continue;

            _spriteBatch.Draw(
                sprite.Texture!,
                transform.Position,
                sprite.SourceRect,
                sprite.Color,
                transform.Rotation,
                sprite.Origin,
                transform.Scale,
                SpriteEffects.None,
                0f);
        }

        _spriteBatch.End();
    }
}
