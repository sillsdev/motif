using SIL.Motif.Host.Parsimony;

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

    /// <summary>Reports the resource limits and process-tree controls applied to this invocation.</summary>
    public PanGlossContainmentReport? Containment { get; init; }

    /// <summary>The request wrote what it promised, including a grammar-health report that contains errors.</summary>
    /// <param name="Output">What the subcommand produced: the JSONL or text rows of <c>stats</c>, the TSV rows of
    /// <c>batch</c>, nothing for <c>import</c>.</param>
    /// <param name="StandardError">Everything the parser wrote to its error stream, kept for its warnings.</param>
    /// <param name="Elapsed">Wall-clock time from process start to exit.</param>
    public sealed record Completed(string Output, string StandardError, TimeSpan Elapsed) : PanGlossOutcome
    {
        /// <summary>Retained batch evidence when the caller requested an artifact directory.</summary>
        public BatchInvocationEvidence? BatchEvidence { get; init; }
        public string? MorphologyOutput { get; init; }
        internal GrammarFactsArtifact? FactsArtifact { get; init; }

        /// <summary>The validated facts database written by a <c>facts</c> request.</summary>
        public PanGlossFactsArtifact? Facts { get; init; }

        /// <summary>Owns retained facts output until the caller copies it into a published evidence bundle.</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public Assess.AssessmentArtifactLease? FactsArtifactLease { get; init; }

        [System.Text.Json.Serialization.JsonIgnore]
        internal Assess.AssessmentArtifactLease? ArtifactLease { get; init; }

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

        /// <summary>Whether the cause is that no executable was found, rather than one that would not run.</summary>
        public bool ExecutableMissing { get; init; }
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

/// <summary>Validated identity and digest details for one PanGloss grammar-facts database.</summary>
/// <param name="Path">The retained SQLite file path.</param>
/// <param name="SchemaVersion">The exact facts schema generation in the file.</param>
/// <param name="SourceSha256">The digest of the Snapshot bytes consumed by PanGloss.</param>
/// <param name="ModelFingerprint">PanGloss's fingerprint of the compiled model.</param>
/// <param name="OutputBytes">The closed file's byte count.</param>
/// <param name="OutputSha256">The closed file's SHA-256 digest.</param>
public sealed record PanGlossFactsArtifact(string Path, int SchemaVersion, string SourceSha256,
    string ModelFingerprint, long OutputBytes, string OutputSha256);
