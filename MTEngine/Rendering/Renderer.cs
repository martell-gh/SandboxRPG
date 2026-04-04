using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MTEngine.Components;
using MTEngine.ECS;
using MTEngine.Items;

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
            sortMode: SpriteSortMode.Deferred,
            blendState: BlendState.AlphaBlend,
            samplerState: SamplerState.PointClamp,
            transformMatrix: _camera.GetViewMatrix()
        );

        var renderables = World.GetEntitiesWith<TransformComponent, SpriteComponent>()
            .Select(entity => new
            {
                Entity = entity,
                Transform = entity.GetComponent<TransformComponent>()!,
                Sprite = entity.GetComponent<SpriteComponent>()!
            })
            .Where(x => x.Sprite.Visible && x.Sprite.Texture != null)
            .OrderBy(x => x.Sprite.LayerDepth)
            .ThenBy(x => GetSortY(x.Transform, x.Sprite));

        foreach (var entry in renderables)
        {
            DrawLiquidContents(entry.Entity, entry.Transform, entry.Sprite);

            _spriteBatch.Draw(
                texture: entry.Sprite.Texture!,
                position: entry.Transform.Position,
                sourceRectangle: entry.Sprite.SourceRect,
                color: entry.Sprite.Color,
                rotation: entry.Transform.Rotation,
                origin: entry.Sprite.Origin,
                scale: entry.Transform.Scale,
                effects: SpriteEffects.None,
                layerDepth: 0f
            );

            DrawEquippedItems(entry.Entity, entry.Transform, entry.Sprite);
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

        foreach (var entity in World.GetEntitiesWith<EquipmentComponent, SpriteComponent>())
        {
            var equipment = entity.GetComponent<EquipmentComponent>()!;
            var ownerSprite = entity.GetComponent<SpriteComponent>()!;

            foreach (var slot in equipment.Slots)
            {
                var wearable = slot.Item?.GetComponent<WearableComponent>();
                if (wearable == null)
                    continue;

                wearable.SyncAnimation(ownerSprite, deltaTime);
            }
        }

        foreach (var entity in World.GetEntities().Where(entity => entity.HasComponent<WearableComponent>()))
        {
            var wearable = entity.GetComponent<WearableComponent>()!;
            if (wearable.IsBroken)
                BreakWearable(entity);
        }
    }

    private static float GetSortY(TransformComponent transform, SpriteComponent sprite)
    {
        if (!sprite.YSort)
            return 0f;

        var sourceHeight = sprite.SourceRect?.Height ?? sprite.Height;
        return transform.Position.Y + (sourceHeight * transform.Scale.Y * 0.5f) + sprite.SortOffsetY;
    }

    private void DrawEquippedItems(Entity owner, TransformComponent transform, SpriteComponent ownerSprite)
    {
        var equipment = owner.GetComponent<EquipmentComponent>();
        if (equipment == null)
            return;

        foreach (var slot in equipment.Slots)
        {
            var itemEntity = slot.Item;
            var wearable = itemEntity?.GetComponent<WearableComponent>();
            if (itemEntity == null || wearable == null)
                continue;

            var itemSprite = itemEntity.GetComponent<SpriteComponent>();
            var texture = wearable.EquippedTexture ?? itemSprite?.Texture;
            if (texture == null)
                continue;

            var sourceRect = wearable.GetEquippedSourceRect() ?? itemSprite?.SourceRect;
            var origin = wearable.EquippedTexture != null
                ? wearable.EquippedOrigin
                : itemSprite?.Origin ?? ownerSprite.Origin;

            _spriteBatch!.Draw(
                texture: texture,
                position: transform.Position,
                sourceRectangle: sourceRect,
                color: wearable.GetRenderColor(itemEntity),
                rotation: transform.Rotation,
                origin: origin,
                scale: transform.Scale,
                effects: SpriteEffects.None,
                layerDepth: 0f
            );
        }
    }

    private void DrawLiquidContents(Entity entity, TransformComponent transform, SpriteComponent ownerSprite)
    {
        var liquid = entity.GetComponent<Metabolism.LiquidContainerComponent>();
        var fillTexture = liquid?.GetFillTexture();
        if (liquid == null || fillTexture == null)
            return;

        var sourceRect = new Rectangle(0, 0, fillTexture.Width, fillTexture.Height);
        var origin = new Vector2(fillTexture.Width / 2f, fillTexture.Height / 2f);

        _spriteBatch!.Draw(
            texture: fillTexture,
            position: transform.Position,
            sourceRectangle: sourceRect,
            color: liquid.GetFillColor(),
            rotation: transform.Rotation,
            origin: origin,
            scale: transform.Scale,
            effects: SpriteEffects.None,
            layerDepth: 0f
        );
    }

    private void BreakWearable(Entity entity)
    {
        var item = entity.GetComponent<ItemComponent>();
        if (item == null)
            return;

        var container = item.ContainedIn;
        if (container != null)
        {
            container.GetComponent<EquipmentComponent>()?.RemoveEquipped(entity);
            container.GetComponent<HandsComponent>()?.RemoveFromHand(entity);
            container.GetComponent<StorageComponent>()?.Contents.Remove(entity);
            item.ContainedIn = null;
        }

        entity.Active = false;
        World.DestroyEntity(entity);
        Console.WriteLine($"[Wearable] {item.ItemName} broke apart.");
    }
}
