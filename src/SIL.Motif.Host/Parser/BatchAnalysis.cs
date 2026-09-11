using System.Globalization;

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

    /// <summary>The per-word step budget was exhausted; any findings remain partial evidence.</summary>
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
    /// <summary>The recorded per-word step budget, or null when the supplied evidence does not name one.</summary>
    public int? PerWordStepLimit { get; init; }

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
    /// Whether any attempted word's search stopped at a time or step limit.
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

        foreach (var raw in tsv.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0) continue;

            var columns = line.Split('\t');
            if (columns.Length < 5) continue; // the STARTED progress marker and any header

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
                Signature: columns[4]));
        }

        return results;
    }

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
