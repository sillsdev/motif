using System.Text.Json.Serialization;

namespace SIL.Motif.Contract.Responses;

/// <summary>One timed step of a Trial, as its receipt records it.</summary>
/// <param name="Name">One of <see cref="TrialPhaseNames"/>; the names are stable so later measurements compare.</param>
/// <param name="Seconds">Wall seconds the step took, or 0 when it did not run.</param>
public sealed record TrialPhase(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("seconds")] double Seconds)
{
    /// <summary>Why the step cost what it did: <see cref="TrialPhaseNames.Reused"/> or <see cref="TrialPhaseNames.NotRun"/>.</summary>
    [JsonPropertyName("note"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Note { get; init; }

    /// <summary>Where a value the step used came from, such as the time limit an earlier measurement of this Baseline set.</summary>
    [JsonPropertyName("detail"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Detail { get; init; }
}

/// <summary>The stable names of a Trial's phases, in the order they happen.</summary>
public static class TrialPhaseNames
{
    /// <summary>The CLI's work before the Trial is queued, without the before-side parse.</summary>
    public const string CliPrequeue = "cli-prequeue";

    /// <summary>The before-side parse, or its reuse from an earlier Trial on the same Baseline, words and parser.</summary>
    public const string ParseBefore = "parse-before";

    /// <summary>From the queued job's creation to the worker starting it.</summary>
    public const string QueueWait = "queue-wait";

    /// <summary>The worker's scratch copy of the Baseline, applying the Proposal, and saving it.</summary>
    public const string ScratchPrepare = "scratch-prepare";

    /// <summary>The grammar-health check of the candidate.</summary>
    public const string GrammarHealthCandidate = "grammar-health-candidate";

    /// <summary>The grammar-health check of the Baseline.</summary>
    public const string GrammarHealthBaseline = "grammar-health-baseline";

    /// <summary>Staging the candidate for the after-side parse, up to the parser starting.</summary>
    public const string Export = "export";

    /// <summary>The after-side parse, from the parser starting to its result.</summary>
    public const string ParseAfter = "parse-after";

    /// <summary>Building the Assessment records, evidence and result, up to the commit.</summary>
    public const string Report = "report";

    /// <summary>The worker's work after the commit: releasing superseded Trials and pruning copies.</summary>
    public const string Cleanup = "cleanup";

    /// <summary>The note on a before-side parse that an earlier Trial's result served.</summary>
    public const string Reused = "reused";

    /// <summary>The note on a phase that did not run for this Trial.</summary>
    public const string NotRun = "not-run";

    /// <summary>Every phase name, in the order a receipt lists them.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        CliPrequeue, ParseBefore, QueueWait, ScratchPrepare, GrammarHealthCandidate, GrammarHealthBaseline,
        Export, ParseAfter, Report, Cleanup,
    ];
}
