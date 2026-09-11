using SIL.LCModel;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Host.Analysis;

/// <summary>Freezes every human-approved morphology in the loaded source without changing the project.</summary>
public static class ApprovedMorphologyReader
{
    public static IReadOnlyDictionary<string, IReadOnlyList<ApprovedMorphology>> Read(LcmCache cache)
    {
        var result = new Dictionary<string, List<ApprovedMorphology>>(StringComparer.Ordinal);
        foreach (var word in cache.ServiceLocator.GetInstance<IWfiWordformRepository>().AllInstances())
        {
            var form = word.Form.VernacularDefaultWritingSystem?.Text ?? string.Empty;
            if (!result.TryGetValue(form, out var expected)) result[form] = expected = [];
            foreach (var analysis in word.HumanApprovedAnalyses)
            {
                expected.Add(new ApprovedMorphology(analysis.MorphBundlesOS.Select(bundle => new ApprovedMorph(
                    bundle.MorphRA?.Guid.ToString("D"), bundle.MsaRA?.Guid.ToString("D"), bundle.InflTypeRA?.Guid.ToString("D"),
                    bundle.Form.AvailableWritingSystemIds.Select(ws => bundle.Form.get_String(ws)?.Text)
                        .OfType<string>().Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray())).ToArray())
                {
                    SourceWordformGuid = word.Guid.ToString("D"),
                    WritingSystem = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultVernWs),
                });
            }
        }
        return result.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<ApprovedMorphology>)pair.Value, StringComparer.Ordinal);
    }
}
