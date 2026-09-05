namespace SIL.Motif.Contract.Responses;

/// <summary>
/// One command-owned boundary a synchronous <c>assess</c> or <c>handoff</c> run passes through. Never a
/// per-word tick — PanGloss exposes no such signal, so reporting one would be invented, not observed.
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
/// How much of this stage's own, command-known work is done, or <c>0</c> when the stage has no such count.
/// </param>
/// <param name="Total">This stage's own, command-known total, or <c>null</c> when there is none to report.</param>
public sealed record AssessmentProgress(AssessmentStage Stage, int Completed, int? Total, string Message);
