using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MTEngine.Components;
using MTEngine.ECS;
using MTEngine.Rendering;

namespace MTEngine.Core;

public class EntityFactory
{
    private readonly AssetManager _assets;
    private readonly ECS.World _world;

    public EntityFactory(AssetManager assets, ECS.World world)
    {
        _assets = assets;
        _world = world;
    }

    public Entity? CreateFromPrototype(EntityPrototype proto, Vector2 position)
    {
        var entity = _world.CreateEntity(proto.Name);
        var components = proto.Components;

        // Transform
        var tx = components?["transform"]?.AsObject();
        entity.AddComponent(new TransformComponent(position));

        // Sprite + Animations
        Texture2D? texture = null;
        if (proto.SpritePath != null)
            texture = _assets.LoadFromFile(proto.SpritePath);

        // фоллбек — белый квадрат
        if (texture == null)
        {
            texture = new Texture2D(_assets.GraphicsDevice, 16, 16);
            var pixels = new Color[16 * 16];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = Color.White;
            texture.SetData(pixels);
        }

        var spriteNode = components?["sprite"]?.AsObject();
        var sprite = entity.AddComponent(new SpriteComponent(texture)
        {
            LayerDepth = spriteNode?["layerDepth"]?.GetValue<float>() ?? 0.5f
        });

        // анимации
        if (proto.AnimationsPath != null)
        {
            var animSet = AnimationSet.LoadFromFile(proto.AnimationsPath);
            if (animSet != null)
            {
                // путь к текстуре анимации — относительно папки прототипа
                if (!string.IsNullOrEmpty(animSet.TexturePath) && proto.DirectoryPath != null)
                {
                    var animTexPath = Path.Combine(proto.DirectoryPath, animSet.TexturePath);
                    var animTex = _assets.LoadFromFile(animTexPath);
                    if (animTex != null)
                    {
                        sprite.Texture = animTex;
                        animSet.TexturePath = animTexPath;
                    }
                }
                sprite.SetAnimations(animSet, "idle_down");
            }
        }
        else if (spriteNode != null)
        {
            sprite.SourceRect = new Rectangle(
                spriteNode["srcX"]?.GetValue<int>() ?? 0,
                spriteNode["srcY"]?.GetValue<int>() ?? 0,
                spriteNode["width"]?.GetValue<int>() ?? 16,
                spriteNode["height"]?.GetValue<int>() ?? 16
            );
        }

        // Collider
        var colliderNode = components?["collider"]?.AsObject();
        if (colliderNode != null)
        {
            entity.AddComponent(new ColliderComponent
            {
                Width = colliderNode["width"]?.GetValue<int>() ?? 12,
                Height = colliderNode["height"]?.GetValue<int>() ?? 12,
                Offset = new Vector2(
                    colliderNode["offsetX"]?.GetValue<float>() ?? 2f,
                    colliderNode["offsetY"]?.GetValue<float>() ?? 2f
                )
            });
        }

        // Velocity
        var velocityNode = components?["velocity"]?.AsObject();
        if (velocityNode != null)
        {
            entity.AddComponent(new VelocityComponent
            {
                Speed = velocityNode["speed"]?.GetValue<float>() ?? 150f
            });
        }

        Console.WriteLine($"[EntityFactory] Created entity: {proto.Name}");
        return entity;
    }
}