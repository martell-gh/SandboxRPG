using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MTEngine.Components;
using MTEngine.ECS;

namespace MTEngine.Rendering;

public class Renderer : GameSystem
{
    private SpriteBatch? _spriteBatch;
    private Camera? _camera;

    public override void Draw()
    {
        _spriteBatch ??= Core.ServiceLocator.Get<SpriteBatch>();
        _camera ??= Core.ServiceLocator.Get<Camera>();

        _spriteBatch.Begin(
            sortMode: SpriteSortMode.FrontToBack,
            blendState: BlendState.AlphaBlend,
            samplerState: SamplerState.PointClamp,
            transformMatrix: _camera.GetViewMatrix()
        );

        foreach (var entity in World.GetEntitiesWith<TransformComponent, SpriteComponent>())
        {
            var transform = entity.GetComponent<TransformComponent>()!;
            var sprite = entity.GetComponent<SpriteComponent>()!;

            if (!sprite.Visible || sprite.Texture == null) continue;

            _spriteBatch.Draw(
                texture: sprite.Texture,
                position: transform.Position,
                sourceRectangle: sprite.SourceRect,
                color: sprite.Color,
                rotation: transform.Rotation,
                origin: sprite.Origin,
                scale: transform.Scale,
                effects: SpriteEffects.None,
                layerDepth: sprite.LayerDepth
            );
        }

        _spriteBatch.End();
    }

    public override void Update(float deltaTime)
    {
        foreach (var entity in World.GetEntitiesWith<TransformComponent, SpriteComponent>())
        {
            var sprite = entity.GetComponent<SpriteComponent>()!;
            sprite.UpdateAnimation(deltaTime);
        }
    }
}