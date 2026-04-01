using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using MTEngine.Components;
using MTEngine.Core;
using MTEngine.ECS;
using MTEngine.Rendering;

namespace SandboxGame.Systems;

public class PlayerMovementSystem : GameSystem
{
    private InputManager _input = null!;
    private Camera _camera = null!;

    public override void OnInitialize()
    {
        _input = ServiceLocator.Get<InputManager>();
        _camera = ServiceLocator.Get<Camera>();
    }

    public override void Update(float deltaTime)
    {
        foreach (var entity in World.GetEntitiesWith<TransformComponent, VelocityComponent>())
        {
            var transform = entity.GetComponent<TransformComponent>()!;
            var velocity = entity.GetComponent<VelocityComponent>()!;
            var sprite = entity.GetComponent<SpriteComponent>();

            var dir = Vector2.Zero;

            if (_input.IsDown(Keys.W)) dir.Y -= 1;
            if (_input.IsDown(Keys.S)) dir.Y += 1;
            if (_input.IsDown(Keys.A)) dir.X -= 1;
            if (_input.IsDown(Keys.D)) dir.X += 1;

            if (dir != Vector2.Zero) dir.Normalize();

            transform.Position += dir * velocity.Speed * deltaTime;
            _camera.Follow(transform.Position);

            // переключаем анимацию
            if (sprite != null)
            {
                if (dir == Vector2.Zero)
                    sprite.PlayClip("idle_down");
                else if (Math.Abs(dir.X) > Math.Abs(dir.Y))
                    sprite.PlayClip(dir.X < 0 ? "walk_left" : "walk_right");
                else
                    sprite.PlayClip(dir.Y < 0 ? "walk_up" : "walk_down");
            }
        }
    }
}