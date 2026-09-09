namespace SIL.Motif.Host.PanGloss;

/// <summary>
/// What one PanGloss invocation came to. Exactly one case, never an exception: a caller pattern-matches
/// and maps the failure cases to its own Refusal codes.
/// </summary>
public abstract record PanGlossOutcome
{
    private PanGlossOutcome() { }

    /// <summary>One human sentence saying what happened, suitable for a Refusal's message.</summary>
    public abstract string Message { get; }

    /// <summary>The parser exited zero and wrote what the request promised.</summary>
    /// <param name="Output">What the subcommand produced: the JSONL or text rows of <c>stats</c>, the TSV rows of
    /// <c>batch</c>, nothing for <c>import</c>.</param>
    /// <param name="StandardError">Everything the parser wrote to its error stream, kept for its warnings.</param>
    /// <param name="Elapsed">Wall-clock time from process start to exit.</param>
    public sealed record Completed(string Output, string StandardError, TimeSpan Elapsed) : PanGlossOutcome
    {
        public override string Message => "The parser completed.";
    }

    /// <summary>The parser exited nonzero. Its own words are in <see cref="StandardError"/>.</summary>
    public sealed record Refused(int ExitCode, string StandardError, string StandardOutput, string Detail)
        : PanGlossOutcome
    {
        public override string Message => Detail;
    }

    /// <summary>The parser exited zero but did not write what the request promised — never read as success.</summary>
    public sealed record Incomplete(string Detail, string StandardError) : PanGlossOutcome
    {
        public override string Message => Detail;
    }

    /// <summary>No parser ran: the executable is absent or would not start.</summary>
    public sealed record Unavailable(string Detail) : PanGlossOutcome
    {
        public override string Message => Detail;
    }

    /// <summary>The wall-clock cap expired and the process tree was killed.</summary>
    public sealed record TimedOut(TimeSpan Cap, string Detail) : PanGlossOutcome
    {
        public override string Message => Detail;
    }

    /// <summary>The caller cancelled; if a process had started, its tree was killed.</summary>
    public sealed record Cancelled : PanGlossOutcome
    {
        public override string Message => "The parser run was cancelled.";
    }
}
