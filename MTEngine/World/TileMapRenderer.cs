using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MTEngine.Core;
using MTEngine.ECS;
using MTEngine.Rendering;

namespace MTEngine.World;

public class TileMapRenderer : GameSystem
{
    private SpriteBatch _spriteBatch = null!;
    private Camera _camera = null!;
    private AssetManager _assets = null!;
    private PrototypeManager _prototypes = null!;

    public TileMap? TileMap { get; set; }

    public override void OnInitialize()
    {
        // лениво — получим в Draw
    }

    public override void Draw()
    {
        _spriteBatch ??= ServiceLocator.Get<SpriteBatch>();
        _camera ??= ServiceLocator.Get<Camera>();
        _assets ??= ServiceLocator.Get<AssetManager>();
        _prototypes ??= ServiceLocator.Get<PrototypeManager>();

        if (TileMap == null) return;

        // вычисляем видимую область
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

        TileMap.DrawWithPrototypes(_spriteBatch, visibleArea, _prototypes, _assets);

        _spriteBatch.End();
    }
}