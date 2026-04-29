using System;
using Microsoft.Xna.Framework;
using MTEngine.Components;
using MTEngine.Core;
using MTEngine.ECS;
using MTEngine.Items;

namespace MTEngine.Crafting;

[RegisterComponent("qualityTier")]
public class QualityTierComponent : Component, IPrototypeInitializable
{
    [DataField("tier")]
    [SaveField("tier")]
    public int Tier { get; set; } = 4;

    [DataField("label")]
    [SaveField("label")]
    public string Label { get; set; } = "";

    [DataField("shortLabel")]
    [SaveField("shortLabel")]
    public string ShortLabel { get; set; } = "";

    public Color Tint { get; set; } = new Color(143, 106, 69);

    [DataField("tint")]
    [SaveField("tint")]
    public string TintHex
    {
        get => $"#{Tint.R:X2}{Tint.G:X2}{Tint.B:X2}{Tint.A:X2}";
        set => Tint = AssetManager.ParseHexColor(value, Tint);
    }

    public void InitializeFromPrototype(EntityPrototype proto, AssetManager assets)
    {
        ApplyPresentation(Owner);
    }

    public void ApplyPresentation(Entity? entity)
    {
        EnsureDefaults();
        if (entity == null)
            return;

        if (entity.GetComponent<SpriteComponent>() is { } sprite)
            sprite.Color = Tint;

        if (entity.GetComponent<WearableComponent>() is { } wearable)
            wearable.Tint = Tint;
    }

    public string GetDisplayLabel()
    {
        EnsureDefaults();
        return string.IsNullOrWhiteSpace(Label) ? $"Тир {Tier}" : Label;
    }

    public string GetShortLabel()
    {
        EnsureDefaults();
        return string.IsNullOrWhiteSpace(ShortLabel) ? $"T{Tier}" : ShortLabel;
    }

    private void EnsureDefaults()
    {
        Tier = Math.Clamp(Tier, 1, 4);
        if (string.IsNullOrWhiteSpace(Label))
            Label = Tier switch
            {
                1 => "Тир 1",
                2 => "Тир 2",
                3 => "Тир 3",
                _ => "Тир 4"
            };

        if (string.IsNullOrWhiteSpace(ShortLabel))
            ShortLabel = $"T{Tier}";
    }
}
