using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MTEngine.Components;
using MTEngine.Core;
using MTEngine.ECS;
using MTEngine.Interactions;
using MTEngine.Npc;
using MTEngine.Rendering;

namespace MTEngine.Items;

[RegisterComponent("wearable")]
public class WearableComponent : Component, IPrototypeInitializable, IInteractionSource
{
    [DataField("slot")]
    [SaveField("slot")]
    public string SlotId { get; set; } = "";

    [DataField("speedMultiplier")]
    [SaveField("speedMultiplier")]
    public float MoveSpeedMultiplier { get; set; } = 1f;

    [DataField("slashArmor")]
    [SaveField("slashArmor")]
    public float SlashArmor { get; set; }

    [DataField("bluntArmor")]
    [SaveField("bluntArmor")]
    public float BluntArmor { get; set; }

    [DataField("burnArmor")]
    [SaveField("burnArmor")]
    public float BurnArmor { get; set; }

    [DataField("maxDurability")]
    [SaveField("maxDurability")]
    public float MaxDurability { get; set; } = 100f;

    [DataField("durability")]
    [SaveField("durability")]
    public float Durability { get; set; } = 100f;

    [DataField("materials")]
    [SaveField("materials")]
    public List<string> Materials { get; set; } = new();

    /// <summary>Старое одно-полое поле — fallback, если не заданы male/female варианты.</summary>
    [DataField("equippedSprite")]
    public string? EquippedSpriteSource { get; set; }

    [DataField("equippedAnimations")]
    public string? EquippedAnimationsSource { get; set; }

    [DataField("equippedSpriteMale")]
    public string? EquippedSpriteSourceMale { get; set; }

    [DataField("equippedSpriteFemale")]
    public string? EquippedSpriteSourceFemale { get; set; }

    [DataField("equippedAnimationsMale")]
    public string? EquippedAnimationsSourceMale { get; set; }

    [DataField("equippedAnimationsFemale")]
    public string? EquippedAnimationsSourceFemale { get; set; }

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
    [SaveField("color")]
    public string TintHex
    {
        get => $"#{Tint.R:X2}{Tint.G:X2}{Tint.B:X2}{Tint.A:X2}";
        set => Tint = AssetManager.ParseHexColor(value, Color.White);
    }

    public Texture2D? IconTexture { get; private set; }
    public Rectangle? IconSourceRect { get; private set; }

    /// <summary>Текущая (gender-specific) текстура надетой вещи. Меняется в <see cref="SyncAnimation"/>.</summary>
    public Texture2D? EquippedTexture { get; private set; }
    public Rectangle? EquippedSourceRect { get; private set; }
    public Vector2 EquippedOrigin { get; private set; } = new(16f, 16f);
    public AnimationSet? EquippedAnimationSet { get; private set; }
    public AnimationPlayer? EquippedAnimationPlayer { get; private set; }

    // Кэш гендерных вариантов; null если для пола не задана отдельная картинка.
    private GenderedEquipped? _maleAssets;
    private GenderedEquipped? _femaleAssets;
    private GenderedEquipped? _fallbackAssets;
    private Gender _activeGender = Gender.Male;

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

        _fallbackAssets = LoadGenderedSet(assets, proto.DirectoryPath, EquippedSpriteSource, EquippedAnimationsSource);
        _maleAssets = LoadGenderedSet(assets, proto.DirectoryPath, EquippedSpriteSourceMale, EquippedAnimationsSourceMale)
                      ?? _fallbackAssets;
        _femaleAssets = LoadGenderedSet(assets, proto.DirectoryPath, EquippedSpriteSourceFemale, EquippedAnimationsSourceFemale)
                        ?? _fallbackAssets;

        // По умолчанию — мужской набор (или общий, если женского нет).
        SwitchTo(_maleAssets ?? _fallbackAssets);
        _activeGender = Gender.Male;
    }

    public void SyncAnimation(SpriteComponent ownerSprite, float deltaTime)
        => SyncAnimation(null, ownerSprite, deltaTime);

    /// <summary>
    /// Переключает активный гендерный спрайт под пол носителя (если задан),
    /// затем синхронизирует кадр анимации с тем, что играет на спрайте носителя.
    /// </summary>
    public void SyncAnimation(Entity? owner, SpriteComponent ownerSprite, float deltaTime)
    {
        var gender = owner?.GetComponent<IdentityComponent>()?.Gender ?? Gender.Male;
        if (gender != _activeGender)
        {
            SwitchTo(gender == Gender.Female ? _femaleAssets : _maleAssets);
            _activeGender = gender;
        }

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

    private void SwitchTo(GenderedEquipped? set)
    {
        if (set == null)
        {
            EquippedTexture = null;
            EquippedAnimationSet = null;
            EquippedAnimationPlayer = null;
            EquippedSourceRect = null;
            return;
        }

        EquippedTexture = set.Texture;
        EquippedSourceRect = set.SourceRect;
        EquippedOrigin = set.Origin;
        EquippedAnimationSet = set.AnimationSet;
        EquippedAnimationPlayer = set.AnimationPlayer;
    }

    private GenderedEquipped? LoadGenderedSet(AssetManager assets, string directory, string? spriteSource, string? animationsSource)
    {
        if (string.IsNullOrWhiteSpace(spriteSource) && string.IsNullOrWhiteSpace(animationsSource))
            return null;

        var set = new GenderedEquipped
        {
            Origin = new Vector2(EquippedWidth / 2f, EquippedHeight / 2f),
            SourceRect = new Rectangle(EquippedSrcX, EquippedSrcY, EquippedWidth, EquippedHeight)
        };

        if (!string.IsNullOrWhiteSpace(spriteSource))
        {
            var spritePath = Path.Combine(directory, spriteSource);
            set.Texture = assets.LoadFromFile(spritePath);
        }

        if (!string.IsNullOrWhiteSpace(animationsSource))
        {
            var animationsPath = Path.Combine(directory, animationsSource);
            var animSet = AnimationSet.LoadFromFile(animationsPath);
            if (animSet != null)
            {
                if (!string.IsNullOrEmpty(animSet.TexturePath))
                {
                    var animTexturePath = Path.Combine(directory, animSet.TexturePath);
                    var animTex = assets.LoadFromFile(animTexturePath);
                    if (animTex != null)
                    {
                        set.Texture = animTex;
                        animSet.TexturePath = animTexturePath;
                    }
                }

                set.AnimationSet = animSet;
                set.AnimationPlayer = new AnimationPlayer();
                var defaultClip = animSet.HasClip("idle_down")
                    ? "idle_down"
                    : animSet.GetAllClips().FirstOrDefault()?.Name;
                if (!string.IsNullOrWhiteSpace(defaultClip))
                    set.AnimationPlayer.Play(animSet, defaultClip);
            }
        }

        return set.Texture == null && set.AnimationSet == null ? null : set;
    }

    public float GetArmorResistance(Wounds.DamageType type) => type switch
    {
        Wounds.DamageType.Slash => SlashArmor,
        Wounds.DamageType.Blunt => BluntArmor,
        Wounds.DamageType.Burn => BurnArmor,
        _ => 0f
    };

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

    public IEnumerable<InteractionEntry> GetInteractions(InteractionContext ctx)
    {
        if (Owner == null || ctx.Actor != ctx.Target)
            yield break;

        // Don't pop "Надеть" into the right-click menu of a different entity.
        // Equip should only show when the user clicked on themselves or directly on the wearable.
        if (ctx.OriginalTarget != null && ctx.OriginalTarget != ctx.Actor && ctx.OriginalTarget != Owner)
            yield break;

        var item = Owner.GetComponent<ItemComponent>();
        var hands = ctx.Actor.GetComponent<HandsComponent>();
        var equipment = ctx.Actor.GetComponent<EquipmentComponent>();
        if (item?.ContainedIn != ctx.Actor || hands?.GetHandWith(Owner) == null || equipment == null)
            yield break;

        var slot = equipment.GetSlot(SlotId);
        if (slot == null)
            yield break;

        if (slot.Item == Owner)
            yield break;

        var label = slot.Item == null
            ? $"Надеть ({slot.DisplayName})"
            : $"Надеть ({slot.DisplayName}, снять текущее в руку)";

        yield return new InteractionEntry
        {
            Id = $"wearable.equip.{slot.Id}",
            Label = label,
            Priority = 28,
            Execute = _ => equipment.TryEquipOrSwapFromHands(hands, Owner, slot.Id)
        };
    }

    private sealed class GenderedEquipped
    {
        public Texture2D? Texture;
        public Rectangle? SourceRect;
        public Vector2 Origin = new(16f, 16f);
        public AnimationSet? AnimationSet;
        public AnimationPlayer? AnimationPlayer;
    }
}
