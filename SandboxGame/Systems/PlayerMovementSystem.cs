#nullable enable
using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using MTEngine.Components;
using MTEngine.Core;
using MTEngine.ECS;
using MTEngine.Items;
using MTEngine.Rendering;

namespace SandboxGame.Systems;

public class PlayerMovementSystem : GameSystem
{
    private InputManager _input = null!;
    private Camera _camera = null!;
    private IKeyBindingSource? _keys;

    private const float MinZoom = 1f;
    private const float MaxZoom = 6f;
    private const float ZoomStep = 0.25f;
    private const float DefaultZoom = 3f;

    public override void OnInitialize()
    {
        _input = ServiceLocator.Get<InputManager>();
        _camera = ServiceLocator.Get<Camera>();
        _keys = ServiceLocator.Has<IKeyBindingSource>() ? ServiceLocator.Get<IKeyBindingSource>() : null;
        _camera.Zoom = DefaultZoom;
    }

    public override void Update(float deltaTime)
    {
        if (DevConsole.IsOpen)
        {
            foreach (var entity in World.GetEntitiesWith<TransformComponent, VelocityComponent>())
            {
                var sprite = entity.GetComponent<SpriteComponent>();
                if (sprite != null)
                    sprite.PlayClip(ResolveDirectionalIdleClip(Vector2.Zero, sprite));
            }
            return;
        }

        // зум колёсиком
        if (_input.ScrollDelta > 0)
            _camera.Zoom = Math.Min(MaxZoom, _camera.Zoom + ZoomStep);
        else if (_input.ScrollDelta < 0)
            _camera.Zoom = Math.Max(MinZoom, _camera.Zoom - ZoomStep);

        foreach (var entity in World.GetEntitiesWith<TransformComponent, VelocityComponent>())
        {
            var transform = entity.GetComponent<TransformComponent>()!;
            var velocity = entity.GetComponent<VelocityComponent>()!;
            var sprite = entity.GetComponent<SpriteComponent>();
            var equipment = entity.GetComponent<EquipmentComponent>();
            var health = entity.GetComponent<HealthComponent>();

            if (health?.IsDead == true)
            {
                velocity.Velocity = Vector2.Zero;
                _camera.Follow(transform.Position);
                if (sprite != null)
                    sprite.PlayClip(ResolveDirectionalIdleClip(Vector2.Zero, sprite));
                continue;
            }

            var dir = Vector2.Zero;

            if (_input.IsDown(GetKey("MoveUp", Keys.W))) dir.Y -= 1;
            if (_input.IsDown(GetKey("MoveDown", Keys.S))) dir.Y += 1;
            if (_input.IsDown(GetKey("MoveLeft", Keys.A))) dir.X -= 1;
            if (_input.IsDown(GetKey("MoveRight", Keys.D))) dir.X += 1;

            if (dir != Vector2.Zero) dir.Normalize();

            var speedMultiplier = equipment?.GetMoveSpeedMultiplier() ?? 1f;
            velocity.Velocity = dir * velocity.Speed * speedMultiplier;
            transform.Position += dir * velocity.Speed * speedMultiplier * deltaTime;
            _camera.Follow(transform.Position);

            if (sprite != null)
            {
                sprite.PlayClip(ResolveDirectionalIdleClip(dir, sprite));
            }
        }
    }

    private static string ResolveDirectionalIdleClip(Vector2 dir, SpriteComponent sprite)
    {
        if (dir != Vector2.Zero)
        {
            if (Math.Abs(dir.X) > Math.Abs(dir.Y))
                return dir.X < 0 ? "idle_left" : "idle_right";

            return dir.Y < 0 ? "idle_up" : "idle_down";
        }

        var current = sprite.AnimationPlayer?.CurrentClipName;
        if (!string.IsNullOrWhiteSpace(current) && current.StartsWith("idle_", StringComparison.Ordinal))
            return current;

        return "idle_down";
    }

    private Keys GetKey(string action, Keys fallback)
        => _keys?.GetKey(action) ?? fallback;
}
