using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MTEngine.Components;
using MTEngine.Core;
using MTEngine.ECS;
using MTEngine.Rendering;

namespace MTEngine.Npc;

public enum HairStyleGender
{
    Unisex,
    Male,
    Female
}

[RegisterComponent("hairStyle")]
public class HairStyleComponent : Component, IPrototypeInitializable
{
    [DataField("displayName")]
    public string DisplayName { get; set; } = "";

    [DataField("gender")]
    public HairStyleGender Gender { get; set; } = HairStyleGender.Unisex;

    [DataField("sprite")]
    public string SpriteSource { get; set; } = "sprite.png";

    [DataField("animations")]
    public string? AnimationsSource { get; set; }

    [DataField("srcX")]
    public int SrcX { get; set; }

    [DataField("srcY")]
    public int SrcY { get; set; }

    [DataField("width")]
    public int Width { get; set; } = 32;

    [DataField("height")]
    public int Height { get; set; } = 32;

    [DataField("offsetX")]
    public float OffsetX { get; set; }

    [DataField("offsetY")]
    public float OffsetY { get; set; }

    [DataField("originX")]
    public float OriginX { get; set; } = -1f;

    [DataField("originY")]
    public float OriginY { get; set; } = -1f;

    public Texture2D? Texture { get; private set; }
    public Rectangle SourceRect { get; private set; }
    public Vector2 Origin { get; private set; } = new(16f, 16f);
    public Vector2 Offset => new(OffsetX, OffsetY);
    public AnimationSet? AnimationSet { get; private set; }
    public AnimationPlayer? AnimationPlayer { get; private set; }

    public void InitializeFromPrototype(EntityPrototype proto, AssetManager assets)
    {
        SourceRect = new Rectangle(SrcX, SrcY, Width, Height);
        Origin = new Vector2(
            OriginX >= 0f ? OriginX : Width / 2f,
            OriginY >= 0f ? OriginY : Height / 2f);

        var dir = proto.DirectoryPath;
        if (string.IsNullOrWhiteSpace(dir))
            return;

        if (!string.IsNullOrWhiteSpace(SpriteSource))
            Texture = assets.LoadFromFile(Path.Combine(dir, SpriteSource));

        if (!string.IsNullOrWhiteSpace(AnimationsSource))
        {
            var animSet = AnimationSet.LoadFromFile(Path.Combine(dir, AnimationsSource));
            if (animSet != null)
            {
                if (!string.IsNullOrWhiteSpace(animSet.TexturePath))
                {
                    var texture = assets.LoadFromFile(Path.Combine(dir, animSet.TexturePath));
                    if (texture != null)
                    {
                        Texture = texture;
                        animSet.TexturePath = Path.Combine(dir, animSet.TexturePath);
                    }
                }

                AnimationSet = animSet;
                AnimationPlayer = new AnimationPlayer();
                var defaultClip = animSet.HasClip("idle_down")
                    ? "idle_down"
                    : animSet.GetAllClips().FirstOrDefault()?.Name;
                if (!string.IsNullOrWhiteSpace(defaultClip))
                    AnimationPlayer.Play(animSet, defaultClip);
            }
        }
    }

    public bool IsCompatible(Gender gender)
        => Gender == HairStyleGender.Unisex
           || (Gender == HairStyleGender.Male && gender == MTEngine.Npc.Gender.Male)
           || (Gender == HairStyleGender.Female && gender == MTEngine.Npc.Gender.Female);
}

[RegisterComponent("hair")]
public class HairAppearanceComponent : Component
{
    [DataField("styleId")]
    [SaveField("styleId")]
    public string StyleId { get; set; } = "";

    public Color Color { get; set; } = new(76, 49, 31);

    [DataField("color")]
    [SaveField("color")]
    public string ColorHex
    {
        get => $"#{Color.R:X2}{Color.G:X2}{Color.B:X2}{Color.A:X2}";
        set => Color = AssetManager.ParseHexColor(value, Color);
    }

    [DataField("visible")]
    [SaveField("visible")]
    public bool Visible { get; set; } = true;

    private string _resolvedStyleId = "";
    private Gender _resolvedGender = Gender.Male;
    private HairStyleComponent? _style;

    public bool Resolve(PrototypeManager prototypes, AssetManager assets, Gender gender)
    {
        if (!Visible)
            return false;

        var proto = FindStylePrototype(prototypes, StyleId, gender);
        if (proto == null)
            return false;

        if (_style != null
            && string.Equals(_resolvedStyleId, proto.Id, StringComparison.OrdinalIgnoreCase)
            && _resolvedGender == gender)
        {
            return _style.Texture != null;
        }

        if (proto.Components?["hairStyle"] is not System.Text.Json.Nodes.JsonObject styleData)
            return false;

        var component = ComponentPrototypeSerializer.Deserialize(typeof(HairStyleComponent), styleData) as HairStyleComponent;
        if (component == null)
            return false;

        component.InitializeFromPrototype(proto, assets);
        if (component.Texture == null)
            return false;

        _style = component;
        _resolvedStyleId = proto.Id;
        _resolvedGender = gender;
        if (string.IsNullOrWhiteSpace(StyleId))
            StyleId = proto.Id;
        return true;
    }

    public void SyncAnimation(PrototypeManager prototypes, AssetManager assets, Entity owner, SpriteComponent ownerSprite, float deltaTime)
    {
        var gender = owner.GetComponent<IdentityComponent>()?.Gender ?? Gender.Male;
        if (!Resolve(prototypes, assets, gender) || _style?.AnimationSet == null || _style.AnimationPlayer == null)
            return;

        var desiredClip = ownerSprite.AnimationPlayer?.CurrentClipName;
        if (string.IsNullOrWhiteSpace(desiredClip) || !_style.AnimationSet.HasClip(desiredClip))
        {
            desiredClip = _style.AnimationSet.HasClip("idle_down")
                ? "idle_down"
                : _style.AnimationSet.GetAllClips().FirstOrDefault()?.Name;
        }

        if (!string.IsNullOrWhiteSpace(desiredClip))
            _style.AnimationPlayer.Play(_style.AnimationSet, desiredClip);

        _style.AnimationPlayer.Update(deltaTime);
    }

    public bool TryGetDrawData(PrototypeManager prototypes, AssetManager assets, Entity owner, out HairDrawData data)
    {
        data = default;
        var gender = owner.GetComponent<IdentityComponent>()?.Gender ?? Gender.Male;
        if (!Resolve(prototypes, assets, gender) || _style?.Texture == null)
            return false;

        data = new HairDrawData(
            _style.Texture,
            _style.AnimationPlayer?.GetSourceRect() ?? _style.SourceRect,
            _style.Origin,
            _style.Offset,
            Color);
        return true;
    }

    public static EntityPrototype? FindStylePrototype(PrototypeManager prototypes, string? styleId, Gender gender)
    {
        if (!string.IsNullOrWhiteSpace(styleId)
            && prototypes.GetEntity(styleId.Trim()) is { } requested
            && IsHairStylePrototype(requested, gender))
        {
            return requested;
        }

        return prototypes.GetAllEntities()
            .Where(proto => IsHairStylePrototype(proto, gender))
            .OrderBy(proto => proto.Id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    public static bool IsHairStylePrototype(EntityPrototype proto, Gender? gender = null)
    {
        if (proto.Components?["hairStyle"] is not System.Text.Json.Nodes.JsonObject styleData)
            return false;

        if (gender == null)
            return true;

        var styleGender = ReadGender(styleData);
        return styleGender == HairStyleGender.Unisex
               || (styleGender == HairStyleGender.Male && gender == Gender.Male)
               || (styleGender == HairStyleGender.Female && gender == Gender.Female);
    }

    private static HairStyleGender ReadGender(System.Text.Json.Nodes.JsonObject styleData)
    {
        var raw = styleData["gender"]?.GetValue<string>() ?? "";
        return Enum.TryParse<HairStyleGender>(raw, true, out var gender)
            ? gender
            : HairStyleGender.Unisex;
    }
}

public readonly record struct HairDrawData(
    Texture2D Texture,
    Rectangle SourceRect,
    Vector2 Origin,
    Vector2 Offset,
    Color Color);
