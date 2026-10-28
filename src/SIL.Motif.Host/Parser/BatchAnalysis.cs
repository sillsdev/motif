using System.Globalization;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Host.PanGloss;

namespace SIL.Motif.Host.Parser;

/// <summary>What happened to one word. Kept distinct because a grammar coverage figure that merges them is not a
/// figure about the grammar.</summary>
public enum WordOutcome
{
    /// <summary>The search completed with at least one analysis.</summary>
    Analysed,

    /// <summary>The parser found no analysis. **This is the real signal** — a genuine gap in the grammar.</summary>
    NoAnalysis,

    /// <summary>
    /// The per-word deadline expired. Partial findings do not establish that the search completed.
    /// </summary>
    TimedOut,

    /// <summary>The analysis-attempt or derived search-work limit was reached; findings remain partial evidence.</summary>
    Capped,

    /// <summary>The parser declined to attempt the word — not an analysis result at all.</summary>
    Skipped,
}

/// <summary>
/// The one place a <see cref="WordOutcome"/> and an <see cref="Assess.AssessedWord"/>'s stored <c>Outcome</c>
/// string ever change into each other, the same discipline <see cref="Assess.AssessmentKindNames"/> applies
/// to <see cref="Assess.AssessmentKind"/>. A writer cites <see cref="ToStoredOutcome"/> and a reader cites
/// <see cref="TryParseStoredOutcome"/>, so the two vocabularies can never drift the way two independently
/// hand-written switches could.
/// </summary>
public static class StoredWordOutcomeNames
{
    /// <summary>The spelling this outcome is written under in a stored <c>Outcome</c> value.</summary>
    public static string ToStoredOutcome(this WordOutcome outcome) => outcome switch
    {
        WordOutcome.Analysed => "analysed",
        WordOutcome.NoAnalysis => "no-analysis",
        WordOutcome.TimedOut => "timed-out",
        WordOutcome.Capped => "capped",
        WordOutcome.Skipped => "skipped",
        _ => throw new ArgumentOutOfRangeException(nameof(outcome)),
    };

    /// <summary>Reads a stored <c>Outcome</c> value back, or reports it unrecognised rather than guessing.</summary>
    public static bool TryParseStoredOutcome(this string stored, out WordOutcome outcome)
    {
        switch (stored)
        {
            case "analysed": outcome = WordOutcome.Analysed; return true;
            case "no-analysis": outcome = WordOutcome.NoAnalysis; return true;
            case "timed-out": outcome = WordOutcome.TimedOut; return true;
            case "capped": outcome = WordOutcome.Capped; return true;
            case "skipped": outcome = WordOutcome.Skipped; return true;
            default: outcome = default; return false;
        }
    }
}

/// <summary>One row of a batch run.</summary>
public sealed record WordAnalysis(int Index, string Word, int ElapsedMs, WordOutcome Outcome, string Signature)
{
    public SIL.Motif.Contract.Responses.ParseWordEvidence? Morphology { get; init; }
    public SIL.Motif.Contract.Responses.WordCorrectness? Correctness { get; init; }

    /// <summary>Whether this row replaced a timed-out first-pass row with a retry result.</summary>
    public bool WasRetried { get; init; }

    /// <summary>The retry's passing-attempt count, when its statistics cache supplied one.</summary>
    public int? Passes { get; init; }

    /// <summary>The retry's per-word statistics, when its cache supplied them.</summary>
    public SIL.Motif.Host.PanGloss.PanGlossWordStatistics? RetryStatistics { get; init; }

    /// <summary>The analysis attempts in the final per-word evidence, or null when that evidence supplied none.</summary>
    public long? Steps { get; init; }

    /// <summary>The total parser work in the final per-word evidence, or null when that evidence supplied none.</summary>
    public long? WorkSteps { get; init; }
}

/// <summary>
/// A completed batch run, with the provenance a grammar coverage figure is required to carry
/// (ADR 0032 §4).
/// </summary>
/// <remarks>Batch termination does not establish completion of every word's search.</remarks>
public sealed record BatchAnalysis(
    IReadOnlyList<WordAnalysis> Words,
    int? PerWordTimeoutMs,
    string ProjectPath,
    IReadOnlyList<string> Warnings)
{
    /// <summary>The recorded per-word analysis-attempt limit, or null when the evidence does not name one.</summary>
    public StepCap? PerWordStepLimit { get; init; }

    /// <summary>
    /// Selects final per-word statistics, removing first-pass counts for replaced rows even when the retry
    /// supplied no cache. Null means that no statistics were available for any word.
    /// </summary>
    public static IReadOnlyDictionary<string, PanGlossWordStatistics>? OverlayRetryStatistics(
        IReadOnlyDictionary<string, PanGlossWordStatistics>? firstPass, IEnumerable<WordAnalysis> words)
    {
        var result = firstPass?.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
            ?? new Dictionary<string, PanGlossWordStatistics>(StringComparer.Ordinal);
        foreach (var word in words.Where(word => word.WasRetried))
        {
            result.Remove(word.Word);
            if (word.RetryStatistics is { } retry) result[word.Word] = retry;
        }
        return result.Count == 0 ? null : result;
    }

    public int Analysed => Words.Count(w => w.Outcome == WordOutcome.Analysed);
    public int NoAnalysis => Words.Count(w => w.Outcome == WordOutcome.NoAnalysis);
    public int TimedOut => Words.Count(w => w.Outcome == WordOutcome.TimedOut);
    public int Capped => Words.Count(w => w.Outcome == WordOutcome.Capped);
    public int Skipped => Words.Count(w => w.Outcome == WordOutcome.Skipped);
    public int Incomplete => TimedOut + Capped;

    /// <summary>
    /// Words the parser actually reached a verdict on — the only honest denominator for grammar coverage, since a
    /// capped, timed-out or skipped words do not have completed searches.
    /// </summary>
    public int Adjudicated => Analysed + NoAnalysis;

    /// <summary>
    /// Whether any attempted word's search stopped at a time or parser search limit.
    /// </summary>
    public bool IsIncomplete => Incomplete > 0;
}

/// <summary>
/// Parses <c>pangloss batch</c>'s TSV output. Deliberately separate from the process invocation so the
/// mapping from the parser's vocabulary to Motif's can be tested against captured real output with no
/// executable present.
/// </summary>
public static class BatchTsvParser
{
    /// <summary>
    /// Reads <c>idx\tword\tms\tstatus\tsignature</c> rows. Unknown statuses throw rather than defaulting:
    /// silently bucketing an unrecognised status as a failure would shift a grammar coverage number
    /// without anyone noticing, so a new parser status must break the build loudly instead.
    /// </summary>
    public static IReadOnlyList<WordAnalysis> Parse(string tsv)
    {
        var results = new List<WordAnalysis>();
        int? stepsColumn = null, workStepsColumn = null;
        var hasHeader = false;

        foreach (var raw in tsv.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0) continue;

            var columns = line.Split('\t');
            if (IsHeader(columns))
            {
                // A header names its columns; the optional headerless layout appends both counters.
                hasHeader = true;
                stepsColumn = Array.IndexOf(columns, "steps") is var steps and >= 0 ? steps : null;
                workStepsColumn = Array.IndexOf(columns, "work_steps") is var work and >= 0 ? work : null;
                continue;
            }
            if (columns.Length < 5) continue; // the STARTED progress marker

            // The STARTED marker reuses the row shape with a non-numeric elapsed column.
            if (!int.TryParse(columns[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var elapsedMs))
                continue;

            if (!int.TryParse(columns[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var index))
                continue;

            results.Add(new WordAnalysis(
                Index: index,
                Word: columns[1],
                ElapsedMs: elapsedMs,
                Outcome: ToOutcome(columns[3], columns[4]),
                Signature: columns[4])
            {
                Steps = ReadCounter(columns, stepsColumn ?? (!hasHeader && columns.Length is 7 or 8 ? columns.Length - 2 : null)),
                WorkSteps = ReadCounter(columns, workStepsColumn ?? (!hasHeader && columns.Length is 7 or 8 ? columns.Length - 1 : null)),
            });
        }

        return results;
    }

    private static bool IsHeader(string[] columns) =>
        columns.Length >= 5 && columns[0] is "index" or "idx" && Array.IndexOf(columns, "word") == 1 &&
        Array.IndexOf(columns, "elapsed_ms") == 2;

    private static long? ReadCounter(string[] columns, int? column) =>
        column is { } index && index < columns.Length &&
        long.TryParse(columns[index], NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : null;

    private static WordOutcome ToOutcome(string status, string signature) => status switch
    {
        "ok" => signature == "-" ? WordOutcome.NoAnalysis : WordOutcome.Analysed,
        "TIMEOUT" => WordOutcome.TimedOut,
        "CAP" => WordOutcome.Capped,
        "SKIPPED" => WordOutcome.Skipped,
        "none" or "NONE" or "no-analysis" => WordOutcome.NoAnalysis,
        _ => throw new InvalidOperationException(
            $"Unrecognised parser status '{status}' (signature '{signature}'). Add it to " +
            $"{nameof(BatchTsvParser)} deliberately: bucketing an unknown status as a failure would move " +
            "grammar coverage numbers silently."),
    };
}
