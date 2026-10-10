using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Parsimony;
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

    /// <summary>Reads every native default-human Approved morphology with its surface and writing-system tag.</summary>
    public static IReadOnlyList<NativeReadingExpectation> ReadApprovedExpectations(LcmCache cache) =>
        ReadExpectations(cache, word => word.HumanApprovedAnalyses, "approved");

    /// <summary>Reads every native default-human Disapproved morphology with its surface and writing-system tag.</summary>
    public static IReadOnlyList<NativeReadingExpectation> ReadDisapprovedExpectations(LcmCache cache) =>
        ReadExpectations(cache, word => word.HumanDisapprovedParses, "disapproved");

    /// <summary>
    /// Every candidate analysis — one no person has approved or rejected, whoever produced it — keyed the same way as
    /// <see cref="Read"/>, for grading a produced reading against what the project holds without a human verdict.
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<ApprovedMorphology>> ReadCandidates(LcmCache cache) =>
        ReadFrom(cache, word =>
        {
            var withOpinion = word.HumanApprovedAnalyses.Concat(word.HumanDisapprovedParses).ToHashSet();
            return word.AnalysesOC.Where(analysis => !withOpinion.Contains(analysis));
        });

    /// <summary>The surface text of every wordform FieldWorks marks as incorrectly spelled.</summary>
    public static IReadOnlySet<string> ReadIncorrectSpellings(LcmCache cache) =>
        cache.ServiceLocator.GetInstance<IWfiWordformRepository>().AllInstances()
            .Where(word => word.SpellingStatus == IncorrectSpellingStatus)
            .Select(word => word.Form.VernacularDefaultWritingSystem?.Text ?? string.Empty)
            .ToHashSet(StringComparer.Ordinal);

    // SpellingStatus's Incorrect member (0 = Undecided, 1 = Correct, 2 = Incorrect).
    private const int IncorrectSpellingStatus = 2;

    private static IReadOnlyDictionary<string, IReadOnlyList<ApprovedMorphology>> ReadFrom(
        LcmCache cache, Func<IWfiWordform, IEnumerable<IWfiAnalysis>> select)
    {
        var result = new Dictionary<string, List<ApprovedMorphology>>(StringComparer.Ordinal);
        foreach (var word in cache.ServiceLocator.GetInstance<IWfiWordformRepository>().AllInstances())
        {
            foreach (var ws in word.Form.AvailableWritingSystemIds.Order())
            {
                var form = word.Form.get_String(ws)?.Text?.Normalize(System.Text.NormalizationForm.FormD);
                if (string.IsNullOrEmpty(form)) continue;
                if (!result.TryGetValue(form, out var expected)) result[form] = expected = [];
                foreach (var analysis in select(word))
                {
                    expected.Add(new ApprovedMorphology(analysis.MorphBundlesOS.Select(bundle => new ApprovedMorph(
                        bundle.MorphRA?.Guid.ToString("D"), bundle.MsaRA?.Guid.ToString("D"), bundle.InflTypeRA?.Guid.ToString("D"),
                        bundle.Form.AvailableWritingSystemIds.Select(bundleWs => bundle.Form.get_String(bundleWs)?.Text)
                            .OfType<string>().Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray())).ToArray())
                    {
                        SourceAnalysisId = CanonicalId.FromGuid(analysis.Guid).Value,
                        SourceWordformGuid = word.Guid.ToString("D"),
                        WritingSystem = cache.WritingSystemFactory.GetStrFromWs(ws),
                    });
                }
            }
        }
        return result.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<ApprovedMorphology>)pair.Value, StringComparer.Ordinal);
    }

    private static IReadOnlyList<NativeReadingExpectation> ReadExpectations(
        LcmCache cache, Func<IWfiWordform, IEnumerable<IWfiAnalysis>> select, string opinion)
    {
        ArgumentNullException.ThrowIfNull(cache);
        var captured = new List<NativeReadingExpectation>();
        foreach (var wordform in cache.ServiceLocator.GetInstance<IWfiWordformRepository>().AllInstances())
        {
            var wordformId = CanonicalId.FromGuid(wordform.Guid).Value;
            foreach (var writingSystem in wordform.Form.AvailableWritingSystemIds.Order())
            {
                var form = wordform.Form.get_String(writingSystem)?.Text?.Normalize(
                    System.Text.NormalizationForm.FormD);
                if (string.IsNullOrEmpty(form)) continue;
                var tag = cache.WritingSystemFactory.GetStrFromWs(writingSystem);
                foreach (var analysis in select(wordform))
                {
                    var morphs = analysis.MorphBundlesOS.Select(bundle => new ParseMorph(
                        bundle.MorphRA is null ? null : CanonicalId.FromGuid(bundle.MorphRA.Guid).Value,
                        bundle.MsaRA is null ? null : CanonicalId.FromGuid(bundle.MsaRA.Guid).Value,
                        bundle.InflTypeRA is null ? null : CanonicalId.FromGuid(bundle.InflTypeRA.Guid).Value,
                        null)).ToArray();
                    var unavailable = morphs.Any(morph => morph.Form is null || morph.Msa is null)
                        ? "At least one stored morph lacks an exact Form or MSA identity."
                        : null;
                    captured.Add(new NativeReadingExpectation(opinion, tag, form, morphs,
                        [wordformId], [CanonicalId.FromGuid(analysis.Guid).Value], unavailable));
                }
            }
        }

        return captured.GroupBy(SignatureKey, StringComparer.Ordinal)
            .Select(group =>
            {
                var first = group.First();
                return first with
                {
                    SourceWordformIds = group.SelectMany(item => item.SourceWordformIds)
                        .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
                    SourceAnalysisIds = group.SelectMany(item => item.SourceAnalysisIds)
                        .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
                };
            })
            .OrderBy(item => item.WritingSystem, StringComparer.Ordinal)
            .ThenBy(item => item.Form, StringComparer.Ordinal)
            .ThenBy(item => SignatureKey(item), StringComparer.Ordinal)
            .ToArray();
    }

    private static string SignatureKey(NativeReadingExpectation item) =>
        System.Text.Json.JsonSerializer.Serialize(new
        {
            item.WritingSystem,
            item.Form,
            item.Morphs,
            item.UnavailableReason,
        });
}
