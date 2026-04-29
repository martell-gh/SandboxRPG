using Microsoft.Xna.Framework;

namespace MTEngine.Combat;

public readonly record struct SkillCheckResult(
    SkillType Skill,
    float SkillValue,
    float Difficulty,
    float Chance,
    float Roll,
    bool Success);

public static class SkillChecks
{
    public static SkillCheckResult Roll(SkillComponent? skills, SkillType skill, float difficulty, float baseChance = 0.45f, float skillWeight = 0.008f)
    {
        var skillValue = skills?.GetSkill(skill) ?? 0f;
        var chance = MathHelper.Clamp(baseChance + (skillValue - difficulty) * skillWeight, 0.03f, 0.97f);
        var roll = Random.Shared.NextSingle();
        return new SkillCheckResult(skill, skillValue, difficulty, chance, roll, roll <= chance);
    }

    public static float GetMedicineEfficiency(float skill)
    {
        var t = MathHelper.Clamp(skill / 100f, 0f, 1f);
        return MathHelper.Lerp(0.35f, 1.20f, t);
    }

    public static float GetMedicineFumbleChance(float skill, float difficulty)
    {
        var chance = MathHelper.Clamp(0.28f - skill / 180f + difficulty / 220f, 0.02f, 0.35f);
        return chance;
    }

    public static float RollCraftQuality(float skill, float requiredSkill)
    {
        var gap = skill - requiredSkill;
        var randomOffset = MathHelper.Lerp(-0.08f, 0.08f, Random.Shared.NextSingle());
        return MathHelper.Clamp(0.72f + gap * 0.012f + skill * 0.0025f + randomOffset, 0.42f, 1.28f);
    }

    public static float GetTradeBonus(float skill)
    {
        var t = MathHelper.Clamp(skill / 100f, 0f, 1f);
        return MathHelper.Lerp(0f, 0.35f, t);
    }

    public static float GetStealChance(float thievery, float difficulty, float itemBulk = 0f)
    {
        var chance = 0.14f
            + thievery * 0.0085f
            - difficulty * 0.0075f
            - itemBulk * 0.05f;

        return MathHelper.Clamp(chance, 0.02f, 0.95f);
    }

    public static float GetSocialChance(float social, float difficulty)
    {
        return MathHelper.Clamp(0.18f + (social - difficulty) * 0.007f, 0.05f, 0.92f);
    }
}
