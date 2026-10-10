using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Commands.SelectionReading;

/// <summary>The captured parser verdict for a physical Text occurrence.</summary>
public enum SelectionOccurrenceVerdict
{
    Matches,
    Differs,
    New,
    NoParse,
    Limit,
    NotAssessed,
}

/// <summary>Counts and parser verdicts computed without displayed lines or tokens.</summary>
public sealed record SelectionAnalyzeFacts(
    IReadOnlyDictionary<SelectionOccurrenceVerdict, int> Counts,
    IReadOnlyDictionary<TextWordKey, SelectionOccurrenceVerdict> Verdicts,
    int NeedsALookCount,
    int NamedInWarningCount);

/// <summary>Joins compact physical positions to their exact wordform comparisons and mutable presentation.</summary>
public static class SelectionAnalyzeProjection
{
    public static SelectionAnalyzeFacts Build(SelectionSummary summary,
        IReadOnlyList<PendingChange>? pending = null, SelectionPresentationState? presentation = null)
    {
        ArgumentNullException.ThrowIfNull(summary);
        var words = summary.Words.ToDictionary(word => word.Key);
        var verdicts = words.ToDictionary(pair => pair.Key, pair => Verdict(pair.Value));
        var counts = Enum.GetValues<SelectionOccurrenceVerdict>().ToDictionary(verdict => verdict, _ => 0);
        var wordforms = words.Keys.ToDictionary(key => key, key => key.WordformId is { } id ? CanonicalId.FromGuid(id).Value : null);
        var needs = 0;
        var named = 0;
        foreach (var position in summary.SourcePositions)
        {
            counts[verdicts[position.Word]]++;
            var wordform = wordforms[position.Word];
            var staged = (pending ?? []).Any(change => (change.WordformId.Length == 0 || change.WordformId == wordform) &&
                (change.Occurrence is { } anchor ? anchor == position.Location.Anchor : change.WordformId.Length > 0));
            if (words[position.Word].Actions.NeedsALook && !staged) needs++;
            if (presentation?.WarningsByWord.GetValueOrDefault(position.Word)?.Any(warning =>
                    warning.YourWords?.Match == WarningWordsMatch.Identity) == true) named++;
        }
        return new SelectionAnalyzeFacts(counts, verdicts, needs, named);
    }

    private static SelectionOccurrenceVerdict Verdict(SelectionWordSummary word)
    {
        if (word.Assessment?.Comparison is not { } comparison || comparison.MeaningCode == "refused")
            return SelectionOccurrenceVerdict.NotAssessed;
        return comparison.Outcome switch
        {
            WordRowOutcome.Same when comparison.Tone != WordRowTone.Problem => SelectionOccurrenceVerdict.Matches,
            WordRowOutcome.Stopped => SelectionOccurrenceVerdict.Limit,
            WordRowOutcome.NoParse => SelectionOccurrenceVerdict.NoParse,
            WordRowOutcome.NotParsed => SelectionOccurrenceVerdict.NotAssessed,
            WordRowOutcome.Different when word.Actions.StoredAnalysisIds.Count == 0 => SelectionOccurrenceVerdict.New,
            _ => SelectionOccurrenceVerdict.Differs,
        };
    }
}
