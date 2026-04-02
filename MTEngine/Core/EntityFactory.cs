using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MTEngine.Components;
using MTEngine.ECS;

namespace MTEngine.Core;

public class EntityFactory
{
    private readonly AssetManager _assets;
    private readonly ECS.World _world;

    public EntityFactory(AssetManager assets, ECS.World world)
    {
        _assets = assets;
        _world = world;
        ComponentRegistry.EnsureInitialized();
    }

    public Entity? CreateFromPrototype(EntityPrototype proto, Vector2 position)
    {
        var entity = _world.CreateEntity(proto.Name);
        entity.PrototypeId = proto.Id;

        bool hasTransform = false;

        if (proto.Components != null)
        {
            foreach (var pair in proto.Components)
            {
                var componentName = pair.Key;
                var componentData = pair.Value?.AsObject();
                if (componentData == null) continue;

                var componentType = ComponentRegistry.GetComponentType(componentName);
                if (componentType == null)
                {
                    Console.WriteLine($"[EntityFactory] Unknown component in prototype: {componentName}");
                    continue;
                }

                try
                {
                    var component = ComponentPrototypeSerializer.Deserialize(componentType, componentData);
                    entity.AddComponent(component);

                    if (component is TransformComponent)
                        hasTransform = true;

                    if (component is IPrototypeInitializable initializable)
                        initializable.InitializeFromPrototype(proto, _assets);
                }
                catch (Exception e)
                {
                    Console.WriteLine($"[EntityFactory] Failed to build component '{componentName}': {e.Message}");
                }
            }
        }

        if (!hasTransform)
        {
            entity.AddComponent(new TransformComponent(position));
        }
        else
        {
            var transform = entity.GetComponent<TransformComponent>();
            if (transform != null)
                transform.Position = position;
        }

        if (!entity.HasComponent<SpriteComponent>())
        {
            var texture = new Texture2D(_assets.GraphicsDevice, 32, 32);
            var pixels = new Color[32 * 32];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = Color.White;
            texture.SetData(pixels);

            entity.AddComponent(new SpriteComponent(texture));
        }

        Console.WriteLine($"[EntityFactory] Created entity: {proto.Name}");
        return entity;
    }
}
