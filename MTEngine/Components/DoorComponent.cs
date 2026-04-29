using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MTEngine.Core;
using MTEngine.ECS;
using MTEngine.Interactions;
using MTEngine.Systems;

namespace MTEngine.Components;

[RegisterComponent("door")]
public class DoorComponent : Component, IInteractionSource, IPrototypeInitializable
{
    private Texture2D? _openTexture;
    private Texture2D? _closedTexture;
    private bool _isOpen;

    [DataField("name")]
    [SaveField("name")]
    public string DoorName { get; set; } = "Door";

    [DataField("openSprite")]
    [SaveField("openSprite")]
    public string? OpenSprite { get; set; }

    [DataField("closedSprite")]
    [SaveField("closedSprite")]
    public string? ClosedSprite { get; set; }

    [DataField("open")]
    [SaveField("open")]
    public bool IsOpen
    {
        get => _isOpen;
        set
        {
            _isOpen = value;
            SyncState();
        }
    }

    public void InitializeFromPrototype(EntityPrototype proto, AssetManager assets)
    {
        _openTexture = LoadTexture(assets, proto.DirectoryPath, OpenSprite);
        _closedTexture = LoadTexture(assets, proto.DirectoryPath, ClosedSprite);
        SyncState();
    }

    public IEnumerable<InteractionEntry> GetInteractions(InteractionContext ctx)
    {
        if (ctx.Target != Owner)
            yield break;

        yield return new InteractionEntry
        {
            Id = IsOpen ? "door.close" : "door.open",
            Label = IsOpen ? $"Закрыть ({LocalizationManager.T(DoorName)})" : $"Открыть ({LocalizationManager.T(DoorName)})",
            Priority = 32,
            IsPrimaryAction = true,
            Execute = c => Toggle(c.Actor)
        };
    }

    public void Toggle(Entity actor)
    {
        IsOpen = !IsOpen;

        if (ServiceLocator.Has<IWorldStateTracker>())
            ServiceLocator.Get<IWorldStateTracker>().MarkDirty();
    }

    public void SyncState()
    {
        var sprite = Owner?.GetComponent<SpriteComponent>();
        if (sprite != null)
        {
            sprite.Texture = IsOpen ? _openTexture ?? sprite.Texture : _closedTexture ?? sprite.Texture;
            if (sprite.Texture != null)
            {
                sprite.SourceRect = new Rectangle(0, 0, sprite.Texture.Width, sprite.Texture.Height);
                sprite.Origin = new Vector2(sprite.Texture.Width / 2f, sprite.Texture.Height / 2f);
                sprite.Width = sprite.Texture.Width;
                sprite.Height = sprite.Texture.Height;
            }
        }

        var blocker = Owner?.GetComponent<BlockerComponent>();
        if (blocker != null)
        {
            blocker.BlocksMovement = !IsOpen;
            blocker.BlocksVision = !IsOpen;
        }
    }

    private static Texture2D? LoadTexture(AssetManager assets, string? directoryPath, string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || string.IsNullOrWhiteSpace(directoryPath))
            return null;

        return assets.LoadFromFile(Path.Combine(directoryPath, relativePath));
    }
}
