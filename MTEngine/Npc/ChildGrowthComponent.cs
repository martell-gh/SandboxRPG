using MTEngine.ECS;

namespace MTEngine.Npc;

/// <summary>
/// Зафиксированные на момент рождения "целевые" значения навыков.
/// До 18 лет фактический скилл = target * (years / 18).
/// При совершеннолетии компонент удаляется.
/// </summary>
[RegisterComponent("childGrowth")]
public class ChildGrowthComponent : Component
{
    [DataField("targetSkills")] [SaveField("targetSkills")]
    public Dictionary<string, float> TargetSkills { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>SaveId отца — для UI/событий.</summary>
    [DataField("fatherSaveId")] [SaveField("fatherSaveId")]
    public string FatherSaveId { get; set; } = "";

    [DataField("motherSaveId")] [SaveField("motherSaveId")]
    public string MotherSaveId { get; set; } = "";
}
