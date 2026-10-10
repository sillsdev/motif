using System.Collections.Generic;
using System.Linq;
using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;

namespace SIL.Motif.Runner.Snapshotting;

internal static class StringFieldSnapshotting
{
    public static Dictionary<string, string> ReadAlternatives(LcmCache cache, ITsString? value)
    {
        if (value is null || value.Length == 0) return new();
        var systems = Enumerable.Range(0, value.RunCount)
            .Select(run => value.get_Properties(run).GetIntPropValues(
                (int)FwTextPropType.ktptWs, out _))
            .Distinct().ToArray();
        var writingSystem = systems.Length == 1 && systems[0] > 0
            ? cache.WritingSystemFactory.GetStrFromWs(systems[0])
            : "mixed";
        var text = value.get_NormalizedForm(FwNormalizationMode.knmNFSC)?.Text;
        if (string.IsNullOrEmpty(text)) return new();
        return new Dictionary<string, string>
        {
            ["ws"] = writingSystem,
            ["text"] = text,
        };
    }
}
