using System.Collections.Generic;
using MTEngine.Core;
using MTEngine.ECS;

namespace MTEngine.Metabolism;

[RegisterComponent("substanceSource")]
public class SubstanceSourceComponent : Component, IPrototypeInitializable
{
    [DataField("substances")]
    public List<SubstanceReference> SubstanceRefs { get; set; } = new();

    [DataField("yieldMultiplier")]
    public float YieldMultiplier { get; set; } = 1f;

    public List<SubstanceDose> Substances { get; private set; } = new();

    public void InitializeFromPrototype(EntityPrototype proto, AssetManager assets)
    {
        Substances = SubstanceResolver.ResolveMany(SubstanceRefs);
    }

    public List<SubstanceDose> CreateYield()
    {
        var factor = YieldMultiplier <= 0f ? 0f : YieldMultiplier;
        var result = new List<SubstanceDose>();

        foreach (var substance in Substances)
            result.Add(substance.CloneScaled(factor));

        return result;
    }
}
