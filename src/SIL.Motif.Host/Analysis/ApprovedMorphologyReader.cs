using SIL.LCModel;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Host.Analysis;

/// <summary>Freezes human-decided morphology in the loaded source without changing the project.</summary>
public static class ApprovedMorphologyReader
{
    /// <summary>Every human-approved analysis, keyed by its wordform's surface text.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<ApprovedMorphology>> Read(LcmCache cache) =>
        ReadFrom(cache, word => word.HumanApprovedAnalyses);

    /// <summary>
    /// Every human-disapproved analysis, keyed the same way as <see cref="Read"/> — the negative half of the
    /// same human judgement, read for grading a produced reading against what the project explicitly rejected.
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<ApprovedMorphology>> ReadDisapproved(LcmCache cache) =>
        ReadFrom(cache, word => word.HumanDisapprovedParses);

    private static IReadOnlyDictionary<string, IReadOnlyList<ApprovedMorphology>> ReadFrom(
        LcmCache cache, Func<IWfiWordform, IEnumerable<IWfiAnalysis>> select)
    {
        var result = new Dictionary<string, List<ApprovedMorphology>>(StringComparer.Ordinal);
        foreach (var word in cache.ServiceLocator.GetInstance<IWfiWordformRepository>().AllInstances())
        {
            var form = word.Form.VernacularDefaultWritingSystem?.Text ?? string.Empty;
            if (!result.TryGetValue(form, out var expected)) result[form] = expected = [];
            foreach (var analysis in select(word))
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
