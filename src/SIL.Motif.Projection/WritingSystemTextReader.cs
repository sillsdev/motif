using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.Core.Text;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Projection;

/// <summary>Pairs the selected string with its actual run tag; mixed runs have no single writing system.</summary>
public static class WritingSystemTextReader
{
    public static string? SingleId(LcmCache cache, ITsString? text)
    {
        if (text is null || text.Length == 0 || text.Text == "***") return null;
        var systems = Enumerable.Range(0, text.RunCount)
            .Select(run => text.get_Properties(run).GetIntPropValues((int)FwTextPropType.ktptWs, out _))
            .Distinct().ToArray();
        return systems.Length == 1 && systems[0] > 0 ? cache.WritingSystemFactory.GetStrFromWs(systems[0]) : null;
    }

    public static WritingSystemText Read(LcmCache cache, ITsString? text) =>
        new(text?.Text is { } value && value != "***" ? value : string.Empty, SingleId(cache, text));

    public static WritingSystemText BestAnalysis(LcmCache cache, IMultiAccessorBase? accessor) =>
        Read(cache, accessor?.BestAnalysisAlternative);

    public static WritingSystemText First(LcmCache cache, IMultiAccessorBase accessor) =>
        accessor.AvailableWritingSystemIds.Order().Select(ws => Read(cache, accessor.get_String(ws)))
            .FirstOrDefault(value => value.Text.Length > 0) ?? new(string.Empty, null);
}
