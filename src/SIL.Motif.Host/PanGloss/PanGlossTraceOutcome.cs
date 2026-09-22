namespace SIL.Motif.Host.PanGloss;

/// <summary>
/// What tracing one word came to. Exactly one case, never an exception, mirroring
/// <see cref="PanGlossOutcome"/>: a trace that exhausted its timeout is a result carrying whatever was
/// collected and why, not a failure a caller must remember to catch.
/// </summary>
public abstract record PanGlossTraceOutcome
{
    private PanGlossTraceOutcome() { }

    /// <summary>The word this outcome traced.</summary>
    public abstract string Word { get; init; }

    /// <summary>One human sentence saying what happened.</summary>
    public abstract string Message { get; }

    /// <summary>
    /// The parser's own account of the search besides the tree, when it wrote one: always on
    /// <see cref="Completed"/>, and on <see cref="Incomplete"/> when the parser's own cap stopped it.
    /// </summary>
    public PanGlossTraceDetails? Details { get; init; }

    /// <summary>The parsed diagnostic document, including its exact raw JSON, when one was emitted.</summary>
    public PanGlossTraceDiagnosticDocument? Document { get; init; }

    /// <summary>The parser produced a trace tree — possibly empty, when the word's shape was never traceable
    /// — and the derived summary that accompanies it.</summary>
    public sealed record Completed(string Word, PanGlossTraceNode? Tree, PanGlossTraceSummary Summary)
        : PanGlossTraceOutcome
    {
        public override string Message => "The trace completed.";
    }

    /// <summary>
    /// Motif's own trace timeout expired, or the parser exited zero without writing a trace this module
    /// recognises. Whatever the process is known to have produced travels with it; a genuine wall-clock kill
    /// typically produces nothing at all, because PanGloss writes its trace only once, after the whole
    /// derivation is built.
    /// </summary>
    public sealed record Incomplete(string Word, PanGlossTraceNode? Tree, PanGlossTraceSummary? Summary, string Reason)
        : PanGlossTraceOutcome
    {
        public override string Message => Reason;
    }

    /// <summary>The parser exited nonzero: it declined this grammar and word outright.</summary>
    public sealed record Declined(string Word, string Detail) : PanGlossTraceOutcome
    {
        public override string Message => Detail;
    }

    /// <summary>No parser ran: the executable is absent or would not start.</summary>
    public sealed record Unavailable(string Word, string Detail) : PanGlossTraceOutcome
    {
        public override string Message => Detail;
    }

    /// <summary>
    /// The parser exited zero but its standard output was not the <c>word\tsignature</c> line plus trace JSON
    /// this module requires — truncated, corrupted, or from a build this module does not understand. The raw
    /// text is kept for inspection; it is never parsed a second way to guess at a meaning.
    /// </summary>
    public sealed record Malformed(string Word, string RawOutput, string Detail) : PanGlossTraceOutcome
    {
        public override string Message => Detail;
    }

    /// <summary>The caller cancelled; if a process had started, its tree was killed.</summary>
    public sealed record Cancelled(string Word) : PanGlossTraceOutcome
    {
        public override string Message => "The trace was cancelled.";
    }
}
