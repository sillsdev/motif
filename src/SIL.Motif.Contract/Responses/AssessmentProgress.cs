namespace SIL.Motif.Contract.Responses;

/// <summary>
/// One boundary a synchronous <c>assess</c> or <c>handoff</c> run passes through.
/// Parsing progress comes from the sequential parser's flushed word rows.
/// </summary>
public enum AssessmentStage
{
    /// <summary>Ensuring a current Baseline exists, capturing one first when it does not.</summary>
    Capturing,

    /// <summary>Running <c>pangloss import</c> to produce the grammar snapshot.</summary>
    ImportingGrammar,

    /// <summary>Composing the Selection from whichever of the four agreed sources were asked for.</summary>
    SelectingWords,

    /// <summary>Running the Assessor over the composed Selection.</summary>
    Parsing,

    /// <summary>Querying PanGloss's per-object statistics.</summary>
    ReadingStatistics,

    /// <summary>Every command-owned stage finished.</summary>
    Complete,
}

/// <summary>
/// One reported progress step, for a human-mode caller to print as a diagnostic line — never JSON, and
/// never console output the command itself writes (ADR 0043 decision 1).
/// </summary>
/// <param name="Completed">
/// How much of this stage's observed work is done, or <c>0</c> when the stage has no such count.
/// </param>
/// <param name="Total">This stage's known total, or <c>null</c> when there is none to report.</param>
/// <param name="Stage">The command-owned phase currently being reported.</param>
/// <param name="Message">The human-readable diagnostic associated with this progress step.</param>
public sealed record AssessmentProgress(AssessmentStage Stage, int Completed, int? Total, string Message)
{
    public string? CurrentWord { get; init; }
    public int? PerWordLimitMs { get; init; }
    public SIL.Motif.Contract.Jobs.ParseWordTiming? SlowestWord { get; init; }
    public IReadOnlyList<SIL.Motif.Contract.Jobs.StoppedParseWord> StoppedWords { get; init; } = [];
}
