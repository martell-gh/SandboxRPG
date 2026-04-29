using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MTEngine.Components;
using MTEngine.Combat;
using MTEngine.Core;
using MTEngine.ECS;
using MTEngine.Items;
using MTEngine.Npc;

namespace MTEngine.Rendering;

public class Renderer : GameSystem
{
    private SpriteBatch? _spriteBatch;
    private Camera? _camera;
    private CombatSystem? _combat;
    private PrototypeManager? _prototypes;
    private AssetManager? _assets;

    public override void Draw()
    {
        _spriteBatch ??= Core.ServiceLocator.Get<SpriteBatch>();
        _camera ??= Core.ServiceLocator.Get<Camera>();
        _combat ??= World.GetSystem<CombatSystem>();
        _prototypes ??= Core.ServiceLocator.Get<PrototypeManager>();
        _assets ??= Core.ServiceLocator.Get<AssetManager>();

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
            .ThenBy(x => GetSortY(x.Entity, x.Transform, x.Sprite));

        foreach (var entry in renderables)
        {
            DrawLiquidContents(entry.Entity, entry.Transform, entry.Sprite);
            var drawPosition = entry.Transform.Position + GetCombatOffset(entry.Entity);
            var spriteColor = ApplyDamageFlash(entry.Sprite.Color, entry.Entity.GetComponent<DamageFlashComponent>()?.Intensity ?? 0f);

            _spriteBatch.Draw(
                texture: entry.Sprite.Texture!,
                position: drawPosition,
                sourceRectangle: entry.Sprite.SourceRect,
                color: spriteColor,
                rotation: entry.Transform.Rotation,
                origin: entry.Sprite.Origin,
                scale: entry.Transform.Scale,
                effects: SpriteEffects.None,
                layerDepth: 0f
            );

            DrawEquippedItems(entry.Entity, entry.Transform, entry.Sprite, drawPosition);
            DrawHair(entry.Entity, entry.Transform, drawPosition);
        }

        _spriteBatch.End();
    }

    public override void Update(float deltaTime)
    {
        foreach (var entity in World.GetEntitiesWith<DamageFlashComponent>())
        {
            var flash = entity.GetComponent<DamageFlashComponent>()!;
            flash.Remaining = Math.Max(0f, flash.Remaining - deltaTime);
        }

        foreach (var entity in World.GetEntitiesWith<TransformComponent, SpriteComponent>())
        {
            var sprite = entity.GetComponent<SpriteComponent>()!;
            sprite.UpdateAnimation(deltaTime);
        }

        _prototypes ??= Core.ServiceLocator.Get<PrototypeManager>();
        _assets ??= Core.ServiceLocator.Get<AssetManager>();

        foreach (var entity in World.GetEntitiesWith<HairAppearanceComponent, SpriteComponent>())
        {
            var hair = entity.GetComponent<HairAppearanceComponent>()!;
            var ownerSprite = entity.GetComponent<SpriteComponent>()!;
            hair.SyncAnimation(_prototypes, _assets, entity, ownerSprite, deltaTime);
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

                wearable.SyncAnimation(entity, ownerSprite, deltaTime);
            }
        }

        foreach (var entity in World.GetEntities().Where(entity => entity.HasComponent<WearableComponent>()))
        {
            var wearable = entity.GetComponent<WearableComponent>()!;
            if (wearable.IsBroken)
                BreakWearable(entity);
        }
    }

    private float GetSortY(Entity entity, TransformComponent transform, SpriteComponent sprite)
    {
        if (!sprite.YSort)
            return 0f;

        var sourceHeight = sprite.SourceRect?.Height ?? sprite.Height;
        var sortY = transform.Position.Y + (sourceHeight * transform.Scale.Y * 0.5f) + sprite.SortOffsetY;
        if (IsSleepingVisual(entity))
            sortY += Math.Max(32f, sourceHeight * Math.Abs(transform.Scale.Y));

        return sortY;
    }

    private bool IsSleepingVisual(Entity entity)
        => World.GetSystem<MTEngine.Systems.SleepSystem>()?.IsSleeping(entity) == true
           || entity.GetComponent<NpcIntentComponent>() is { Action: ScheduleAction.Sleep, Arrived: true };

    private void DrawEquippedItems(Entity owner, TransformComponent transform, SpriteComponent ownerSprite, Vector2 drawPosition)
    {
        var equipment = owner.GetComponent<EquipmentComponent>();
        if (equipment == null)
            return;

        var flashAmount = owner.GetComponent<DamageFlashComponent>()?.Intensity ?? 0f;

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
                position: drawPosition,
                sourceRectangle: sourceRect,
                color: ApplyDamageFlash(wearable.GetRenderColor(itemEntity), flashAmount),
                rotation: transform.Rotation,
                origin: origin,
                scale: transform.Scale,
                effects: SpriteEffects.None,
                layerDepth: 0f
            );
        }
    }

    private void DrawHair(Entity owner, TransformComponent transform, Vector2 drawPosition)
    {
        var hair = owner.GetComponent<HairAppearanceComponent>();
        if (hair == null || _prototypes == null || _assets == null)
            return;

        if (!hair.TryGetDrawData(_prototypes, _assets, owner, out var data))
            return;

        var flashAmount = owner.GetComponent<DamageFlashComponent>()?.Intensity ?? 0f;
        _spriteBatch!.Draw(
            texture: data.Texture,
            position: drawPosition + data.Offset,
            sourceRectangle: data.SourceRect,
            color: ApplyDamageFlash(data.Color, flashAmount),
            rotation: transform.Rotation,
            origin: data.Origin,
            scale: transform.Scale,
            effects: SpriteEffects.None,
            layerDepth: 0f
        );
    }

    private void DrawLiquidContents(Entity entity, TransformComponent transform, SpriteComponent ownerSprite)
    {
        var liquid = entity.GetComponent<Metabolism.LiquidContainerComponent>();
        var fillTexture = liquid?.GetFillTexture();
        if (liquid == null || fillTexture == null)
            return;

        var sourceRect = new Rectangle(0, 0, fillTexture.Width, fillTexture.Height);
        var origin = new Vector2(fillTexture.Width / 2f, fillTexture.Height / 2f);

        var drawPosition = transform.Position + GetCombatOffset(entity);

        _spriteBatch!.Draw(
            texture: fillTexture,
            position: drawPosition,
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
        if (Core.ServiceLocator.Has<Core.IWorldStateTracker>())
            Core.ServiceLocator.Get<Core.IWorldStateTracker>().MarkDirty();
        Console.WriteLine($"[Wearable] {item.ItemName} broke apart.");
    }

    private Vector2 GetCombatOffset(Entity entity)
        => _combat?.GetVisualOffset(entity) ?? Vector2.Zero;

    private static Color ApplyDamageFlash(Color baseColor, float amount)
    {
        if (amount <= 0.001f)
            return baseColor;

        var alpha = baseColor.A;
        var flashed = Color.Lerp(baseColor, new Color((byte)255, (byte)72, (byte)72, alpha), MathHelper.Clamp(amount * 0.75f, 0f, 0.75f));
        flashed.A = alpha;
        return flashed;
    }
}
