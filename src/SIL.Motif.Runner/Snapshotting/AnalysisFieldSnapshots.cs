using System.Text.Json;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Model.Effects;
using SIL.Motif.Model.Snapshot;
using SIL.LCModel;

namespace SIL.Motif.Runner.Snapshotting;

/// <summary>Reads the analysis fields used by human opinions and parser candidate creation.</summary>
public static class AnalysisFieldSnapshots
{
    public static ObjectSnapshot Read(IWfiAnalysis analysis) => new(
        CanonicalId.FromGuid(analysis.Guid),
        new Dictionary<string, IReadOnlyDictionary<string, string>>
        {
            [SnapshotFields.WfiAnalysisEvaluations] =
                ReferenceCollectionFieldSnapshotting.ReadAlternatives(analysis.EvaluationsRC),
        });

    public static ObjectSnapshot Read(IWfiWordform wordform) => new(
        CanonicalId.FromGuid(wordform.Guid),
        new Dictionary<string, IReadOnlyDictionary<string, string>>
        {
            [SnapshotFields.WfiWordformAnalyses] = wordform.AnalysesOC.ToDictionary(
                analysis => CanonicalId.FromGuid(analysis.Guid).Value,
                analysis => JsonSerializer.Serialize(new
                {
                    morphs = analysis.MorphBundlesOS.Select(bundle => new
                    {
                        form = bundle.MorphRA is null ? null : CanonicalId.FromGuid(bundle.MorphRA.Guid).Value,
                        msa = bundle.MsaRA is null ? null : CanonicalId.FromGuid(bundle.MsaRA.Guid).Value,
                        inflType = bundle.InflTypeRA is null ? null : CanonicalId.FromGuid(bundle.InflTypeRA.Guid).Value,
                        formText = bundle.Form.VernacularDefaultWritingSystem?.Text,
                    }).ToArray(),
                    evaluations = ReferenceCollectionFieldSnapshotting.ReadAlternatives(analysis.EvaluationsRC)
                        .Keys.OrderBy(id => id, StringComparer.Ordinal).ToArray(),
                })),
        });

    /// <summary>Compares the canonical snapshots of one analysis object before and after a mutation.</summary>
    public static IReadOnlyList<ExpectedEffect> Diff(ObjectSnapshot before, ObjectSnapshot after)
    {
        if (before.CanonicalId != after.CanonicalId)
            throw new ArgumentException("Snapshots must identify the same object.");
        var effects = new List<ExpectedEffect>();
        foreach (var field in before.AlternativesFields.Keys.Union(after.AlternativesFields.Keys))
        {
            var oldValue = before.AlternativesFields.GetValueOrDefault(field) ??
                new Dictionary<string, string>();
            var newValue = after.AlternativesFields.GetValueOrDefault(field) ??
                new Dictionary<string, string>();
            if (oldValue.Count == newValue.Count && oldValue.All(pair =>
                    newValue.TryGetValue(pair.Key, out var value) && value == pair.Value)) continue;
            effects.Add(new ExpectedEffect(before.CanonicalId, field, oldValue, newValue));
        }
        return effects;
    }
}
