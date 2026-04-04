using System.Collections.Generic;

namespace MTEngine.Metabolism;

public interface ISubstanceReservoir
{
    string DisplayName { get; }
    bool HasSubstances { get; }
    IReadOnlyList<SubstanceDose> GetSubstances();
    float TransferSubstanceTo(LiquidContainerComponent target, string substanceId, float amount);
    string DescribeContents();
}
