#nullable enable
using System;
using System.Collections.Generic;

namespace SandboxGame.UI;

public sealed class CharacterCreatorHairOption
{
    public string Id { get; init; } = "";
    public string Label { get; init; } = "";
    public string Gender { get; init; } = "Unisex";
}

public sealed class PlayerCharacterDraft
{
    public const int MinAgeYears = 18;
    public const int MaxAgeYears = 120;

    public string Gender { get; set; } = "Male";
    public int AgeYears { get; set; } = 25;
    public string HairStyleId { get; set; } = "";
    public string HairColor { get; set; } = "#4C311FFF";
    public string SkinColor { get; set; } = "#F0B99DFF";

    public PlayerCharacterDraft Clone()
        => new()
        {
            Gender = Gender,
            AgeYears = Math.Clamp(AgeYears, MinAgeYears, MaxAgeYears),
            HairStyleId = HairStyleId,
            HairColor = HairColor,
            SkinColor = SkinColor
        };
}

public static class CharacterCreatorDefaults
{
    public static readonly string[] HairColors =
    {
        "#16100BFF", "#2B1A11FF", "#4C311FFF", "#6D4327FF",
        "#8E5A33FF", "#B8733DFF", "#D7A35DFF", "#E8D6A1FF",
        "#F0E7D2FF", "#8A8A8AFF", "#3C3C3CFF", "#7B2B22FF"
    };

    public static readonly string[] SkinColors =
    {
        "#F7D7C4FF", "#F0B99DFF", "#D99A78FF", "#BA765AFF",
        "#8D563FFF", "#5E3529FF", "#FFE1CEFF", "#C58B6CFF"
    };

    public static IReadOnlyList<CharacterCreatorHairOption> EmptyHairOptions { get; }
        = new[] { new CharacterCreatorHairOption { Id = "", Label = "Без волос", Gender = "Unisex" } };
}
