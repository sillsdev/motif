namespace SIL.Motif.Contract.Responses;

/// <summary>Stored parse-time percentiles and PanGloss timing rows aggregated for the requested word set.</summary>
public sealed record TimingResponse(
    string AssessmentId,
    string WordSet,
    string By,
    int WordCount,
    double? MedianMs,
    double? Percentile95Ms,
    IReadOnlyList<SlowWordTiming> SlowestWords,
    IReadOnlyList<TimingAggregateRow> Aggregates,
    IReadOnlyList<WordRuleTiming> CostliestWords)
{
    /// <summary>Whether the FieldWorks file has changed since the current Baseline.</summary>
    public bool IsStale { get; init; }

    /// <summary>The exact words the command selected, with their recorded completion and cost.</summary>
    public IReadOnlyList<TimingWordRow> Words { get; init; } = [];
}

/// <summary>The reasons a timed word's search ended, as <see cref="TimingWordRow.Completion"/> carries them.</summary>
public static class TimingCompletion
{
    /// <summary>The search ran out of steps before it finished.</summary>
    public const string StepLimit = "Step limit";

    /// <summary>The word was left out of the run.</summary>
    public const string Skipped = "Skipped";

    /// <summary>The search finished.</summary>
    public const string Finished = "Finished";
}

/// <summary>One selected word's stored time, attempts and reason its search stopped.</summary>
public sealed record TimingWordRow(string Word, int? ElapsedMs, int Attempts, string Completion);

/// <summary>One aggregate by rule kind or named rule.</summary>
public sealed record TimingAggregateRow(
    string Name,
    double ElapsedMs,
    double ShareOfTotal,
    int Attempts,
    int WordsTouched)
{
    /// <summary>The rule kind when grouped by rule; the name itself when grouped by kind.</summary>
    public string Kind { get; init; } = string.Empty;
}

/// <summary>One word's cost under a selected rule.</summary>
public sealed record WordRuleTiming(string Word, double ElapsedMs, int Attempts);
