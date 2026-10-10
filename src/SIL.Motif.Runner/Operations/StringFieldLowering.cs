using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.Core.Text;

namespace SIL.Motif.Runner.Operations;

internal static class StringFieldLowering
{
    public static ITsString Create(LcmCache cache, string writingSystemTag, string text, string kind)
    {
        var handle = cache.WritingSystemFactory.GetWsFromStr(writingSystemTag);
        if (handle == 0)
            throw new InvalidOperationException($"'{kind}': writing system '{writingSystemTag}' is unknown.");
        return TsStringUtils.MakeString(text, handle);
    }
}
