using SIL.Motif.Contract.Responses;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Host.Parser;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Commands;

/// <summary>Compares completed per-word parser work on the Trial's word list.</summary>
public static class TrialWorkComparison
{
    /// <summary>Compares Assessments only when their parser and deterministic limits match.</summary>
    public static TrialWork Compare(AssessmentRecord? baseline, AssessmentRecord trial)
    {
        var before = baseline?.Invocation;
        var after = trial.Invocation;
        var compatible = baseline is not null && baseline.Assessor == trial.Assessor &&
            baseline.BaselineToken == trial.BaselineToken && before is not null && after is not null &&
            !string.IsNullOrWhiteSpace(before.ExecutableBytesSha256) &&
            before.ExecutableBytesSha256 == after.ExecutableBytesSha256 &&
            before.PerWordStepLimit == after.PerWordStepLimit;
        return Compare(compatible ? baseline!.Words ?? [] : [], trial.Words ?? []);
    }

    /// <summary>Returns unknown totals if any requested word lacks complete, comparable evidence.</summary>
    public static TrialWork Compare(IReadOnlyList<AssessedWord> baseline, IReadOnlyList<AssessedWord> trial)
    {
        var before = baseline.ToDictionary(word => word.Word, StringComparer.Ordinal);
        var stops = trial.Select(word => (word.Word, Limit: StopLimit.From(word.Outcome, word.Morphology))).ToArray();
        var workWords = stops.Where(stop => stop.Limit is StopLimit.Work or StopLimit.Both).Select(stop => stop.Word).ToArray();
        var timeWords = stops.Where(stop => stop.Limit is StopLimit.Time).Select(stop => stop.Word).ToArray();
        var workStops = workWords.Length;
        var timeStops = timeWords.Length;
        var pairs = trial.Select(word => (After: word, Before: before.GetValueOrDefault(word.Word))).ToArray();
        // Time is secondary and only comparable over the same words; it never decides the work comparison.
        var beforeTime = pairs.Select(pair => pair.Before is null ? null : TimingAggregation.WordTimeMs(pair.Before)).ToArray();
        var afterTime = pairs.Select(pair => TimingAggregation.WordTimeMs(pair.After)).ToArray();
        var timed = pairs.Length > 0 && beforeTime.All(time => time is not null) && afterTime.All(time => time is not null);
        TrialWork WithStops(TrialWork result) => result with
        {
            WorkLimitWords = workWords,
            TimeSafetyNetWords = timeWords,
            BaselineTimeMs = timed ? beforeTime.Sum(time => time!.Value) : null,
            TrialTimeMs = timed ? afterTime.Sum(time => time!.Value) : null,
        };
        if (pairs.Length == 0 || pairs.Any(pair => pair.Before is null ||
                pair.Before.IsIncomplete || pair.After.IsIncomplete ||
                StopLimit.From(pair.Before.Outcome, pair.Before.Morphology) is not null ||
                StopLimit.From(pair.After.Outcome, pair.After.Morphology) is not null ||
                pair.Before.Outcome is "skipped" || pair.After.Outcome is "skipped" ||
                pair.Before.WorkSteps is null or < 0 || pair.After.WorkSteps is null or < 0))
            return WithStops(new TrialWork(trial.Count, null, null, null, [], workStops, timeStops));

        decimal oldTotal = pairs.Sum(pair => (decimal)pair.Before!.WorkSteps!.Value);
        decimal newTotal = pairs.Sum(pair => (decimal)pair.After.WorkSteps!.Value);
        if (oldTotal > long.MaxValue || newTotal > long.MaxValue)
            return WithStops(new TrialWork(trial.Count, null, null, null, [], workStops, timeStops));
        var increased = pairs.Where(pair => pair.Before!.WorkSteps > 0 &&
                (decimal)pair.After.WorkSteps!.Value > (decimal)pair.Before.WorkSteps.Value * 1.05m)
            .Select(pair => new TrialWorkIncrease(pair.After.Word, pair.Before!.WorkSteps!.Value,
                pair.After.WorkSteps!.Value, (double)(100m * (pair.After.WorkSteps.Value - (decimal)pair.Before.WorkSteps.Value) /
                    pair.Before.WorkSteps.Value)))
            .OrderByDescending(word => word.PercentMore).ThenBy(word => word.Word, StringComparer.Ordinal).Take(5).ToArray();
        return WithStops(new TrialWork(trial.Count, (long)oldTotal, (long)newTotal,
            oldTotal == 0 ? null : (double)(100m * (newTotal - oldTotal) / oldTotal), increased, workStops, timeStops));
    }
}
