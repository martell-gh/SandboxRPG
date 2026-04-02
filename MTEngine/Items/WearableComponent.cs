using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MTEngine.Components;
using MTEngine.Core;
using MTEngine.ECS;
using MTEngine.Rendering;

namespace MTEngine.Items;

[RegisterComponent("wearable")]
public class WearableComponent : Component, IPrototypeInitializable
{
    [DataField("slot")]
    public string SlotId { get; set; } = "";

    [DataField("speedMultiplier")]
    public float MoveSpeedMultiplier { get; set; } = 1f;

    [DataField("maxDurability")]
    public float MaxDurability { get; set; } = 100f;

    [DataField("durability")]
    public float Durability { get; set; } = 100f;

    [DataField("materials")]
    public List<string> Materials { get; set; } = new();

    [DataField("equippedSprite")]
    public string? EquippedSpriteSource { get; set; }

    [DataField("equippedAnimations")]
    public string? EquippedAnimationsSource { get; set; }

    [DataField("equippedSrcX")]
    public int EquippedSrcX { get; set; }

    [DataField("equippedSrcY")]
    public int EquippedSrcY { get; set; }

    [DataField("equippedWidth")]
    public int EquippedWidth { get; set; } = 32;

    [DataField("equippedHeight")]
    public int EquippedHeight { get; set; } = 32;

    [DataField("icon")]
    public string? IconSource { get; set; }

    [DataField("iconSrcX")]
    public int IconSrcX { get; set; }

    [DataField("iconSrcY")]
    public int IconSrcY { get; set; }

    [DataField("iconWidth")]
    public int IconWidth { get; set; } = 32;

    [DataField("iconHeight")]
    public int IconHeight { get; set; } = 32;

    public Color Tint { get; set; } = Color.White;

    [DataField("color")]
    public string TintHex
    {
        get => $"#{Tint.R:X2}{Tint.G:X2}{Tint.B:X2}{Tint.A:X2}";
        set => Tint = AssetManager.ParseHexColor(value, Color.White);
    }

    public Texture2D? IconTexture { get; private set; }
    public Rectangle? IconSourceRect { get; private set; }
    public Texture2D? EquippedTexture { get; private set; }
    public Rectangle? EquippedSourceRect { get; private set; }
    public Vector2 EquippedOrigin { get; private set; } = new(16f, 16f);
    public AnimationSet? EquippedAnimationSet { get; private set; }
    public AnimationPlayer? EquippedAnimationPlayer { get; private set; }

    public bool IsBroken => MaxDurability > 0f && Durability <= 0f;

    public void InitializeFromPrototype(EntityPrototype proto, AssetManager assets)
    {
        if (proto.DirectoryPath == null)
            return;

        if (!string.IsNullOrWhiteSpace(IconSource))
        {
            var iconPath = Path.Combine(proto.DirectoryPath, IconSource);
            IconTexture = assets.LoadFromFile(iconPath);
            IconSourceRect = new Rectangle(IconSrcX, IconSrcY, IconWidth, IconHeight);
        }

        if (!string.IsNullOrWhiteSpace(EquippedSpriteSource))
        {
            var spritePath = Path.Combine(proto.DirectoryPath, EquippedSpriteSource);
            EquippedTexture = assets.LoadFromFile(spritePath);
            EquippedSourceRect = new Rectangle(EquippedSrcX, EquippedSrcY, EquippedWidth, EquippedHeight);
            EquippedOrigin = new Vector2(EquippedWidth / 2f, EquippedHeight / 2f);
        }

        if (!string.IsNullOrWhiteSpace(EquippedAnimationsSource))
        {
            var animationsPath = Path.Combine(proto.DirectoryPath, EquippedAnimationsSource);
            var set = AnimationSet.LoadFromFile(animationsPath);
            if (set != null)
            {
                if (!string.IsNullOrEmpty(set.TexturePath))
                {
                    var animTexturePath = Path.Combine(proto.DirectoryPath, set.TexturePath);
                    var texture = assets.LoadFromFile(animTexturePath);
                    if (texture != null)
                    {
                        EquippedTexture = texture;
                        set.TexturePath = animTexturePath;
                    }
                }

                EquippedAnimationSet = set;
                EquippedAnimationPlayer = new AnimationPlayer();
                var defaultClip = set.HasClip("idle_down")
                    ? "idle_down"
                    : set.GetAllClips().FirstOrDefault()?.Name;

                if (!string.IsNullOrWhiteSpace(defaultClip))
                    EquippedAnimationPlayer.Play(set, defaultClip);
            }
        }
    }

    public void SyncAnimation(SpriteComponent ownerSprite, float deltaTime)
    {
        if (EquippedAnimationSet == null || EquippedAnimationPlayer == null)
            return;

        var desiredClip = ownerSprite.AnimationPlayer?.CurrentClipName;
        if (string.IsNullOrWhiteSpace(desiredClip) || !EquippedAnimationSet.HasClip(desiredClip))
        {
            desiredClip = EquippedAnimationSet.HasClip("idle_down")
                ? "idle_down"
                : EquippedAnimationSet.GetAllClips().FirstOrDefault()?.Name;
        }

        if (!string.IsNullOrWhiteSpace(desiredClip))
            EquippedAnimationPlayer.Play(EquippedAnimationSet, desiredClip);

        EquippedAnimationPlayer.Update(deltaTime);
    }

    public Rectangle? GetEquippedSourceRect()
        => EquippedAnimationPlayer?.GetSourceRect() ?? EquippedSourceRect;

    public Color GetRenderColor(Entity itemEntity)
    {
        var spriteColor = itemEntity.GetComponent<SpriteComponent>()?.Color ?? Color.White;
        return MultiplyColor(Tint, spriteColor);
    }

    private static Color MultiplyColor(Color a, Color b)
    {
        return new Color(
            (byte)(a.R * b.R / 255),
            (byte)(a.G * b.G / 255),
            (byte)(a.B * b.B / 255),
            (byte)(a.A * b.A / 255)
        );
    }
}
