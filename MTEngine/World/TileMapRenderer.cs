using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MTEngine.Core;
using MTEngine.ECS;
using MTEngine.Rendering;

namespace MTEngine.World;

public class TileMapRenderer : GameSystem
{
    private SpriteBatch? _spriteBatch;
    private Camera? _camera;
    private AssetManager? _assets;
    private PrototypeManager? _prototypes;

    public TileMap? TileMap { get; set; }

    public override void Update(float deltaTime)
    {
        _prototypes ??= ServiceLocator.Get<PrototypeManager>();
        TileMap?.Update(deltaTime, _prototypes);
    }

    public override void Draw()
    {
        _spriteBatch ??= ServiceLocator.Get<SpriteBatch>();
        _camera ??= ServiceLocator.Get<Camera>();
        _assets ??= ServiceLocator.Get<AssetManager>();
        _prototypes ??= ServiceLocator.Get<PrototypeManager>();

        if (TileMap == null) return;

        var viewport = _spriteBatch.GraphicsDevice.Viewport;
        var topLeft = _camera.ScreenToWorld(Vector2.Zero);
        var bottomRight = _camera.ScreenToWorld(new Vector2(viewport.Width, viewport.Height));

        var visibleArea = new Rectangle(
            (int)topLeft.X - TileMap.TileSize,
            (int)topLeft.Y - TileMap.TileSize,
            (int)(bottomRight.X - topLeft.X) + TileMap.TileSize * 2,
            (int)(bottomRight.Y - topLeft.Y) + TileMap.TileSize * 2
        );

        _spriteBatch.Begin(
            sortMode: SpriteSortMode.Deferred,
            blendState: BlendState.AlphaBlend,
            samplerState: SamplerState.PointClamp,
            transformMatrix: _camera.GetViewMatrix()
        );

        TileMap.DrawFilteredWithPrototypes(
            _spriteBatch,
            visibleArea,
            _prototypes,
            _assets,
            (_, _, tile) => tile.ProtoId == null || _prototypes.GetTile(tile.ProtoId)?.HiddenInGame != true);

        _spriteBatch.End();
    }
}
