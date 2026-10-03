using System.Text.Json.Serialization;
using SIL.Motif.Contract.Baselines;

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
    /// <summary>Whether the selected measurement uses a historical Baseline or predates the live file's save.</summary>
    public bool IsStale { get; init; }

    /// <summary>The selected measurement's relationship to the current project evidence.</summary>
    public TimingEvidenceRelation EvidenceRelation { get; init; }

    /// <summary>The selected measurement's exact Baseline identity, when recorded as a valid token.</summary>
    public BaselineToken? Baseline { get; init; }

    /// <summary>The selected measurement's captured FieldWorks save, when its invocation retains it.</summary>
    public DateTimeOffset? SourceLastWriteUtc { get; init; }

    /// <summary>Whether the live file has changed since the project's current Baseline, independently of this run.</summary>
    public bool CurrentProjectIsStale { get; init; }

    /// <summary>The exact words the command selected, with their recorded time and why each search stopped.</summary>
    public IReadOnlyList<TimingWordRow> Words { get; init; } = [];

    /// <summary>
    /// The selected words' total word time, which every share in <see cref="Aggregates"/> divides by, and the part
    /// of it no parser object's timer covers.
    /// </summary>
    public WordTimeAttribution Attribution { get; init; } = WordTimeAttribution.None;
}

/// <summary>Whether a selected timing measurement belongs to the current saved project evidence.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<TimingEvidenceRelation>))]
public enum TimingEvidenceRelation
{
    /// <summary>The stored provenance cannot establish a relationship to the current project.</summary>
    Unknown,
    /// <summary>The selected run measured the current Baseline and the live file has no later save.</summary>
    Current,
    /// <summary>The selected run measured a different Baseline from the current one.</summary>
    Historical,
    /// <summary>The live file has a later save than the Baseline measured by this run.</summary>
    SavedSince,
}

/// <summary>The reasons a timed word's search ended, as <see cref="TimingWordRow.Completion"/> carries them.</summary>
public static class TimingCompletion
{
    /// <summary>The search ran out of steps before it finished.</summary>
    public const string StepLimit = "Step limit";

    /// <summary>The recorded outcome is a parser refusal.</summary>
    public const string Refused = "Refused";

    /// <summary>The search finished.</summary>
    public const string Finished = "Finished";
}

/// <summary>One selected word's stored time and the reason its search stopped.</summary>
/// <param name="Word">The word form.</param>
/// <param name="ElapsedMs">The stored whole-millisecond parse time, when present.</param>
/// <param name="Completion">Why the search stopped: a step limit, a time limit, a refusal, or a finished search.</param>
public sealed record TimingWordRow(string Word, int? ElapsedMs, string Completion)
{
    /// <summary>The exact parse duration in nanoseconds, when recorded; it takes precedence over <see cref="ElapsedMs"/>.</summary>
    public long? ElapsedNs { get; init; }
    /// <summary>The producing measurement, including its recorded time.</summary>
    public WordMeasurementOrigin? Origin { get; init; }
}

/// <summary>
/// One kind of parser object, or one parser object by identity, with the time its own timers recorded and that
/// time's share of the words' total word time.
/// </summary>
/// <param name="Key">
/// The stable identity: the kind's stored name when grouped by kind, or the parser object's key when grouped by
/// rule. Two objects that share a label keep two keys and two rows.
/// </param>
/// <param name="Name">The label to show: the kind's stored name, or the object's label.</param>
/// <param name="SelfMs">The object's own recorded time, with nested objects' time taken out, in milliseconds.</param>
/// <param name="ShareOfWordTime">
/// <paramref name="SelfMs"/> divided by the total word time of the same words, from 0 to 1; null when those
/// words have no time to divide by.
/// </param>
/// <param name="WordsTouched">How many of the measured words recorded anything against this row.</param>
public sealed record TimingAggregateRow(
    string Key,
    string Name,
    double SelfMs,
    double? ShareOfWordTime,
    int WordsTouched)
{
    /// <summary>The stored parser kind: the row's own kind when grouped by rule, the key itself when by kind.</summary>
    public string Kind { get; init; } = string.Empty;

    /// <summary>
    /// How stable <see cref="Key"/> is: <c>authored</c> for a FieldWorks GUID, <c>structural</c> or
    /// <c>synthetic</c> for a parser locator. Empty for a kind.
    /// </summary>
    public string IdentityQuality { get; init; } = string.Empty;

    /// <summary>The saved grammar scope required by a grammar-local key.</summary>
    public string? Scope { get; init; }
    [JsonIgnore]
    public TraceTimingKey TimingKey => new(Kind, Key) { IdentityQuality = IdentityQuality, Scope = Scope };

    /// <summary>
    /// How many times the parser called objects of this one kind, in both directions; null when the kind does
    /// not count calls. A call to one kind is not a call to another, so no total adds calls across kinds.
    /// </summary>
    public long? Calls { get; init; }

    /// <summary>Whether some rows lack a call count; <see cref="Calls"/> then totals recorded counts only.</summary>
    public bool CallsArePartial { get; init; }
}

/// <summary>One word's time under a selected parser object.</summary>
/// <param name="Word">The word form.</param>
/// <param name="SelfMs">The object's own recorded time in this word, in milliseconds.</param>
/// <param name="Calls">How many times the parser called the object in this word; null when it does not count.</param>
public sealed record WordRuleTiming(string Word, double SelfMs, long? Calls)
{
    /// <summary>The word's whole parse time in milliseconds, or null when none was recorded.</summary>
    public double? WordTimeMs { get; init; }

    /// <summary><see cref="SelfMs"/> as a share of <see cref="WordTimeMs"/>, from 0 to 1, when it has one.</summary>
    public double? ShareOfWordTime { get; init; }

    /// <summary>Whether some rows lack a call count; <see cref="Calls"/> then totals recorded counts only.</summary>
    public bool CallsArePartial { get; init; }
}

/// <summary>
/// Some measured words' total word time and how much of it the parser recorded against its objects. Every share
/// divides by <see cref="WordTimeMs"/>, so the time no object's timer covers stays in view instead of being
/// shared out among the rules.
/// </summary>
/// <param name="MeasuredWordCount">How many words had a parse time; only their object time is counted.</param>
/// <param name="WordTimeMs">The measured words' parse times added up, in milliseconds.</param>
/// <param name="AttributedMs">The time every parser object recorded in those words, added up.</param>
/// <param name="NotAttributedMs">
/// The word time no object recorded: parser work outside every object's timer, its bookkeeping and the cost of
/// measuring. Each word contributes what is left of its own time, never less than zero. Null when no object time
/// was recorded for these words at all, since all of it would otherwise read as unattributed.
/// </param>
/// <param name="NotAttributedShare"><paramref name="NotAttributedMs"/> as a share of the word time, from 0 to 1.</param>
/// <param name="OverrunMs">
/// How much object time exceeded its own word's time, added over the words where it did. Reported apart, because
/// netting it against another word's residual would hide it.
/// </param>
/// <param name="Overrun">Whether any word's object time exceeded its own parse time.</param>
public sealed record WordTimeAttribution(
    int MeasuredWordCount,
    double WordTimeMs,
    double AttributedMs,
    double? NotAttributedMs,
    double? NotAttributedShare,
    double OverrunMs,
    bool Overrun)
{
    /// <summary>No measured words and no recorded time.</summary>
    public static WordTimeAttribution None { get; } = new(0, 0, 0, null, null, 0, false);
}
