using SIL.LCModel;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Operations;

namespace SIL.Motif.Worker.Baselines;

internal static class ProgressDeltaBuilder
{
    private sealed record Standing(bool Misspelled, bool HasCandidate, IReadOnlySet<Guid> Approved);

    public static ProgressProjection? Apply(LcmCache cache, ProgressProjection original,
        IReadOnlyList<OperationEnvelope> operations)
    {
        var changed = new Dictionary<Guid, Standing>();
        foreach (var operation in operations)
        {
            if (operation.Target is not { } target ||
                !cache.ServiceLocator.ObjectRepository.TryGetObject(target.ToGuid(), out var entity)) return null;
            IWfiWordform? wordform = operation.Kind switch
            {
                WfiAnalysisOperationKinds.AddRefEvaluations or WfiAnalysisOperationKinds.RemoveRefEvaluations
                    when entity is IWfiAnalysis analysis => analysis.Owner as IWfiWordform,
                WfiWordformSpellingStatusOperationKinds.SetSpellingStatus or WfiWordformSpellingStatusOperationKinds.ClearSpellingStatus
                    => entity as IWfiWordform,
                _ => null,
            };
            if (wordform is null) return null;
            var approved = wordform.HumanApprovedAnalyses.ToHashSet();
            var disapproved = wordform.HumanDisapprovedParses.ToHashSet();
            changed[wordform.Guid] = new Standing(wordform.SpellingStatus == 2,
                wordform.AnalysesOC.Any(analysis => !approved.Contains(analysis) && !disapproved.Contains(analysis)),
                approved.Select(analysis => analysis.Guid).ToHashSet());
        }
        var texts = new ProgressText[original.Texts.Count];
        Parallel.For(0, texts.Length, new ParallelOptions { MaxDegreeOfParallelism = 2 }, i =>
        {
            var text = original.Texts[i];
            ProgressIncidence[]? incidence = null;
            for (var j = 0; j < text.Incidence.Count; j++)
            {
                var row = text.Incidence[j];
                if (!changed.TryGetValue(row.WordformId, out var standing)) continue;
                var updated = Update(row, standing);
                if (updated == row) continue;
                incidence ??= text.Incidence.ToArray();
                incidence[j] = updated;
            }
            texts[i] = incidence is null ? text : text with { Incidence = incidence };
        });
        return new ProgressProjection(texts);
    }

    private static ProgressIncidence Update(ProgressIncidence row, Standing standing)
    {
        var bucket = standing.Misspelled ? "misspelled" : row.AnalysisId is { } id && standing.Approved.Contains(id)
            ? "approved" : "unmeasured";
        var reason = bucket != "unmeasured" ? null : standing.HasCandidate
            ? "fieldworks-candidate-unclassified" : "parser-evidence-unavailable";
        return row with { Bucket = bucket, Reason = reason };
    }
}
