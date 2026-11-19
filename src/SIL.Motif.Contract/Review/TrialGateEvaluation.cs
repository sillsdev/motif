using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Contract.Review;

/// <summary>The shared verdict over a Trial's captured facts, independent of retained detail pages.</summary>
public static class TrialGateEvaluation
{
    /// <summary>Identifies reports that cannot determine whether an attested word gained an unapproved second Reading.</summary>
    public const string HoldIdentityUnavailable = "reading-hold-identity-unavailable";

    /// <summary>Named losses and Baseline-parsed words whose candidate search stopped.</summary>
    public sealed record GateWords(IReadOnlyList<string>? LostWords, IReadOnlyList<string>? StoppedWords);

    /// <summary>
    /// The gate of a report. <c>loss</c> when a measured word lost its analysis or a confirmed reading and is not in
    /// <paramref name="expectedLosses"/>; otherwise <c>unmeasured</c> when evidence is incomplete, no words were
    /// measured, a word that had a Baseline analysis stopped before it finished, or an attested word has a
    /// confirmed or unresolved hold for an unapproved second Reading. Otherwise the gate is <c>green</c>.
    /// </summary>
    /// <param name="result">The report to judge.</param>
    /// <param name="expectedLosses">Words the author intends to lose.</param>
    /// <param name="words">The rows behind the flags, when the report can list them.</param>
    public static TrialGate GateOf(AvailableTrialResult result, IReadOnlyCollection<string> expectedLosses, GateWords words)
    {
        ArgumentNullException.ThrowIfNull(result);
        var summary = result.Report.Summary;
        long Flagged(ReviewFlagId id) => summary.Flags.Where(flag => flag.Id == id).Sum(flag => flag.WordCount ?? 0);
        var flaggedCount = result.Report.Losses is not null ? 0 : Math.Max(Flagged(ReviewFlagId.NewWordFailures), Flagged(ReviewFlagId.ConfirmedReadingsLost));
        var lost = (result.Report.Losses?.Words ?? words.LostWords)?.Distinct(StringComparer.Ordinal).ToArray();
        var lostCount = lost is { } named && named.Length >= flaggedCount ? named.Length : flaggedCount;
        var stopped = (words.StoppedWords ?? []).Order(StringComparer.Ordinal).ToArray();
        // Stopped words that cannot be listed are assumed to matter: an unlisted stop must not look green.
        var stoppedUnknown = words.StoppedWords is null &&
            ((summary.Incomplete.Before.Stopped ?? 0) + (summary.Incomplete.After.Stopped ?? 0)) > 0;
        var expected = expectedLosses.Select(Key).ToHashSet(StringComparer.Ordinal);
        // A loss the report cannot name stays unexpected: only a listed word is excused.
        var listed = lost is { } all && all.Length >= flaggedCount ? all : null;
        var matched = listed?.Where(word => expected.Contains(Key(word))).Order(StringComparer.Ordinal).ToArray() ?? [];
        var unexpected = listed?.Where(word => !expected.Contains(Key(word))).Order(StringComparer.Ordinal).ToArray() ?? [];
        var unexpectedCount = lostCount == 0 ? 0 : listed is null ? lostCount : unexpected.Length;
        if (lostCount > 0 && listed is null)
            return new TrialGate(TrialGateState.Unmeasured,
                "Exact named losses are unavailable. Refresh the report from retained evidence or rerun the Trial.",
                lostCount, [], [], stopped);
        if (unexpectedCount > 0)
            return new TrialGate(TrialGateState.Loss,
                unexpectedCount + " measured word(s) lost their analysis or a confirmed reading and were not listed as expected losses.",
                lostCount, matched, unexpected, stopped);
        if (UnusableReason(result) is { } unusable)
            return new TrialGate(TrialGateState.Unmeasured, unusable, lostCount, matched, unexpected, stopped);
        if (stopped.Length > 0 || stoppedUnknown)
            return new TrialGate(TrialGateState.Unmeasured,
                (stopped.Length > 0 ? stopped.Length + " word(s)" : "Some words") +
                " that had an analysis in the Baseline were stopped before they finished, so their loss is not measured.",
                lostCount, matched, [], stopped);
        if (result.Report.HeldWords is { Count: > 0 } held)
            return new TrialGate(TrialGateState.Unmeasured,
                held.Count + " attested word(s) have a new second reading held for review.", lostCount, matched, [], stopped);
        if (result.Report.Limitations.FirstOrDefault(limitation => limitation.Code == HoldIdentityUnavailable) is { } unmeasuredHold)
            return new TrialGate(TrialGateState.Unmeasured, unmeasuredHold.Text, lostCount, matched, [], stopped);
        return new TrialGate(TrialGateState.Green, null, lostCount, matched, [], []);
    }

    private static string Key(string word) => word.Normalize(System.Text.NormalizationForm.FormC);

    /// <summary>
    /// Why a finished Trial's report cannot gate a script as a pass, or <see langword="null"/> when it measured
    /// words and every required case finished: nothing was requested, or evidence is incomplete or interrupted.
    /// </summary>
    public static string? UnusableReason(TrialReviewResult result)
    {
        if (result is not AvailableTrialResult { Report.Summary: { } summary }) return null;
        if (summary.Wordforms.Requested == 0) return "The Trial measured no words.";
        var notMeasured = (summary.Incomplete.Before.NotMeasured ?? 0) + (summary.Incomplete.After.NotMeasured ?? 0);
        if (summary.Incomplete.RunInterruptions.Count > 0 || notMeasured > 0)
            return "The Trial finished with incomplete evidence: " + notMeasured +
                   " required case(s) were not measured" +
                   (summary.Incomplete.RunInterruptions.Count > 0 ? ", and the run was interrupted." : ".");
        return null;
    }

}
