using SIL.LCModel.Core.KernelInterfaces;
using SIL.Motif.Contract.HumanJudgments;

namespace SIL.Motif.Projection.HumanJudgments;

/// <summary>Adapts LibLCM rich strings to the pure human-judgment Contract codec.</summary>
public static class ReadableJudgmentCodec
{
    /// <summary>Parses visible rich-string text while leaving its run formatting out of logical identity.</summary>
    public static HumanJudgment Parse(ITsString value, string projectId, string recordId)
    {
        ArgumentNullException.ThrowIfNull(value);
        return HumanJudgmentCodec.Parse(value.Text, projectId, recordId);
    }
}
