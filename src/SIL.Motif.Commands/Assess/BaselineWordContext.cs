using SIL.LCModel;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Analysis;
using SIL.Motif.Host.PanGloss;

namespace SIL.Motif.Commands.Assess;

/// <summary>The complete analysis context of selected forms in one already loaded Baseline.</summary>
internal sealed record BaselineWordContext(
    IReadOnlyDictionary<string, IReadOnlyList<ParserReading>> Analyses,
    IReadOnlyDictionary<string, string> WordLinks,
    IReadOnlySet<string> PresentWords)
{
    /// <summary>Projects all opinions and links before the caller disposes its Baseline cache.</summary>
    internal static BaselineWordContext Read(LcmCache cache, SavedProjectNavigation navigation, IReadOnlyList<string> words)
    {
        var approved = ApprovedMorphologyReader.Read(cache);
        var rejected = ApprovedMorphologyReader.ReadDisapproved(cache);
        var candidates = ApprovedMorphologyReader.ReadCandidates(cache);
        var analyses = words.ToDictionary(word => word, word => ReadAnalyses(cache, navigation,
            approved.GetValueOrDefault(word) ?? [], rejected.GetValueOrDefault(word) ?? [],
            candidates.GetValueOrDefault(word) ?? []), StringComparer.Ordinal);
        var forms = new Dictionary<string, HashSet<Guid>>(StringComparer.Ordinal);
        foreach (var wordform in cache.ServiceLocator.GetInstance<IWfiWordformRepository>().AllInstances())
            foreach (var ws in wordform.Form.AvailableWritingSystemIds)
            {
                var form = wordform.Form.get_String(ws)?.Text?.Normalize(System.Text.NormalizationForm.FormD);
                if (string.IsNullOrEmpty(form)) continue;
                if (!forms.TryGetValue(form, out var identities)) forms[form] = identities = [];
                identities.Add(wordform.Guid);
            }
        var links = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var word in words)
            if (forms.TryGetValue(word, out var identities) && identities.Count == 1 &&
                navigation.LinkFor(new("Analyses", identities.Single())) is { } link) links[word] = link;
        var present = forms.Keys.ToHashSet(StringComparer.Ordinal);
        return new(analyses, links, present);
    }

    /// <summary>Names the stored opinions in the same order for fresh and reopened rows.</summary>
    internal static IReadOnlyList<ParserReading> ReadAnalyses(LcmCache cache, SavedProjectNavigation navigation,
        IReadOnlyList<ApprovedMorphology> approved, IReadOnlyList<ApprovedMorphology> rejected,
        IReadOnlyList<ApprovedMorphology> candidates) =>
        approved.Select(analysis => AssessCommand.ReadStoredAnalysis(cache, string.Empty, analysis, ReadingGrade.Approved, navigation.LinkFor))
            .Concat(rejected.Select(analysis =>
                AssessCommand.ReadStoredAnalysis(cache, string.Empty, analysis, ReadingGrade.Disapproved, navigation.LinkFor)))
            .Concat(candidates.Select(analysis =>
                AssessCommand.ReadStoredAnalysis(cache, string.Empty, analysis, ReadingGrade.Candidate, navigation.LinkFor))).ToArray();
}
