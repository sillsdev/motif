using System.Diagnostics;
using System.Globalization;

namespace SIL.Motif.Host.PanGloss;

/// <summary>
/// One typed request for one subcommand the shipped <c>pangloss</c> binary has. This file is the only place
/// Motif writes down the binary's command-line surface; the <c>assess</c> subcommand is absent because the
/// binary has none.
/// </summary>
public abstract record PanGlossRequest
{
    public const int DefaultPerWordStepLimit = 200000;

    private PanGlossRequest() { }

    /// <summary>The subcommand this request runs, as the binary spells it.</summary>
    public abstract string Subcommand { get; }

    /// <summary>Throws for a request a caller has built wrongly; this is programmer error, not an outcome.</summary>
    internal abstract void Validate();

    /// <summary>Writes whatever the subcommand must read from disk into the invocation's scratch directory.</summary>
    internal virtual Task PrepareAsync(string scratch, CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Appends the subcommand and its arguments, one item each so paths need no quoting.</summary>
    internal abstract void AddArguments(ProcessStartInfo startInfo, string scratch);

    /// <summary>Turns a zero exit into the request's output, or reports what the parser promised and did not write.</summary>
    internal abstract PanGlossOutcome Finish(string scratch, string standardOutput, string standardError, TimeSpan elapsed);

    /// <summary>
    /// <c>pangloss batch</c> over a word list, one thread, a per-word limit, and optionally the per-object
    /// statistics cache. One thread because the parser's own hazards guidance says fan-out multiplies memory
    /// on deep-truncation grammars, and the queue already serialises parsers machine-wide.
    /// </summary>
    public sealed record Batch(
        string ProjectFilePath, IReadOnlyList<string> Words, TimeSpan PerWordLimit, string? StatsCachePath = null,
        int PerWordStepLimit = DefaultPerWordStepLimit, string? ArtifactDirectory = null)
        : PanGlossRequest
    {
        public bool CollectAnalyses { get; init; }

        public override string Subcommand => "batch";

        internal override void Validate()
        {
            if (string.IsNullOrWhiteSpace(ProjectFilePath)) throw new ArgumentException("Required.", nameof(ProjectFilePath));
            ArgumentNullException.ThrowIfNull(Words);
            if (PerWordLimit <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(PerWordLimit), "A per-word limit must be positive.");
            if (PerWordStepLimit <= 0)
                throw new ArgumentOutOfRangeException(nameof(PerWordStepLimit), "A per-word step limit must be positive.");
            if (!File.Exists(ProjectFilePath))
                throw new FileNotFoundException("The project file the parser must read does not exist.", ProjectFilePath);
        }

        internal override Task PrepareAsync(string scratch, CancellationToken cancellationToken) =>
            File.WriteAllLinesAsync(Path.Combine(scratch, "words.txt"), Words, cancellationToken);

        internal override void AddArguments(ProcessStartInfo startInfo, string scratch)
        {
            startInfo.ArgumentList.Add("batch");
            startInfo.ArgumentList.Add(ProjectFilePath);
            startInfo.ArgumentList.Add(Path.Combine(scratch, "words.txt"));
            startInfo.ArgumentList.Add(Path.Combine(scratch, "out.tsv"));
            startInfo.ArgumentList.Add("--word-timeout-ms");
            startInfo.ArgumentList.Add(((int)PerWordLimit.TotalMilliseconds).ToString(CultureInfo.InvariantCulture));
            startInfo.ArgumentList.Add("--step-cap");
            startInfo.ArgumentList.Add(PerWordStepLimit.ToString(CultureInfo.InvariantCulture));
            startInfo.ArgumentList.Add("--threads");
            startInfo.ArgumentList.Add("1");
            if (CollectAnalyses)
            {
                startInfo.ArgumentList.Add("--analyses");
                startInfo.ArgumentList.Add(Path.Combine(scratch, "analyses.jsonl"));
            }
            if (StatsCachePath is null) return;
            startInfo.ArgumentList.Add("--stats");
            startInfo.ArgumentList.Add("--cache");
            startInfo.ArgumentList.Add(StatsCachePath);
        }

        internal override PanGlossOutcome Finish(string scratch, string standardOutput, string standardError, TimeSpan elapsed)
        {
            if (StatsCachePath is not null && !File.Exists(StatsCachePath))
            {
                return new PanGlossOutcome.Incomplete(
                    $"pangloss batch --stats exited 0 but wrote no cache to '{StatsCachePath}'.", standardError);
            }
            var outPath = Path.Combine(scratch, "out.tsv");
            var tsv = File.Exists(outPath) ? File.ReadAllText(outPath) : string.Empty;
            var analysesPath = Path.Combine(scratch, "analyses.jsonl");
            if (CollectAnalyses && !File.Exists(analysesPath))
                return new PanGlossOutcome.Incomplete("The batch wrote no requested morphology evidence.", standardError);
            return new PanGlossOutcome.Completed(tsv, standardError, elapsed)
            {
                MorphologyOutput = CollectAnalyses ? File.ReadAllText(analysesPath) : null,
            };
        }
    }

    /// <summary>
    /// <c>pangloss stats</c> over a cache the <see cref="Batch"/> pass wrote. Motif contributes the grammar and
    /// cache paths; everything else is forwarded in order and unchanged, because PanGloss owns that vocabulary.
    /// </summary>
    public sealed record Stats(string GrammarPath, string CachePath, IReadOnlyList<string> ForwardedArguments)
        : PanGlossRequest
    {
        public override string Subcommand => "stats";

        internal override void Validate()
        {
            if (string.IsNullOrWhiteSpace(GrammarPath)) throw new ArgumentException("Required.", nameof(GrammarPath));
            if (string.IsNullOrWhiteSpace(CachePath)) throw new ArgumentException("Required.", nameof(CachePath));
            ArgumentNullException.ThrowIfNull(ForwardedArguments);
        }

        internal override void AddArguments(ProcessStartInfo startInfo, string scratch)
        {
            startInfo.ArgumentList.Add("stats");
            startInfo.ArgumentList.Add(GrammarPath);
            startInfo.ArgumentList.Add("--cache");
            startInfo.ArgumentList.Add(CachePath);
            foreach (var argument in ForwardedArguments) startInfo.ArgumentList.Add(argument);
        }

        internal override PanGlossOutcome Finish(string scratch, string standardOutput, string standardError, TimeSpan elapsed) =>
            new PanGlossOutcome.Completed(standardOutput, standardError, elapsed);
    }

    /// <summary>
    /// <c>pangloss parse &lt;grammar&gt; &lt;word&gt; --trace --trace-format json --trace-details</c>: one word,
    /// traced and unmerged, written as one JSON document holding the tree, the search state and the effort by
    /// kind of grammar object. The only subcommand that can trace at all — <c>batch</c> carries no
    /// <c>--trace</c> flag, and tracing runs unmerged deliberately, so this must stay a single-word request
    /// rather than growing a word list. Needs PanGloss 0.3.3 or later.
    /// </summary>
    public sealed record Trace(string GrammarPath, string Word) : PanGlossRequest
    {
        public override string Subcommand => "parse";

        internal override void Validate()
        {
            if (string.IsNullOrWhiteSpace(GrammarPath)) throw new ArgumentException("Required.", nameof(GrammarPath));
            if (string.IsNullOrEmpty(Word)) throw new ArgumentException("Required.", nameof(Word));
            if (!File.Exists(GrammarPath))
                throw new FileNotFoundException("The grammar the parser must read does not exist.", GrammarPath);
        }

        internal override void AddArguments(ProcessStartInfo startInfo, string scratch)
        {
            startInfo.ArgumentList.Add("parse");
            startInfo.ArgumentList.Add(GrammarPath);
            startInfo.ArgumentList.Add(Word);
            startInfo.ArgumentList.Add("--trace");
            startInfo.ArgumentList.Add("--trace-format");
            startInfo.ArgumentList.Add("json");
            startInfo.ArgumentList.Add("--trace-details");
        }

        internal override PanGlossOutcome Finish(string scratch, string standardOutput, string standardError, TimeSpan elapsed) =>
            new PanGlossOutcome.Completed(standardOutput, standardError, elapsed);
    }

    /// <summary>
    /// <c>pangloss grammar-health &lt;grammar&gt; &lt;out.json&gt;</c>: the ported HermitCrab grammar-authoring
    /// health checks, written to a scratch file rather than read from standard output — writing to a named
    /// file keeps this request's positional shape identical to the binary's own <c>--describe</c> declaration
    /// (<c>grammar</c> plus an optional <c>out.json</c>), where reading standard output instead would supply
    /// only the first of the two and fail that conformance check on every invocation, not only this one's own.
    /// </summary>
    public sealed record GrammarHealth(string GrammarPath) : PanGlossRequest
    {
        public override string Subcommand => "grammar-health";

        internal override void Validate()
        {
            if (string.IsNullOrWhiteSpace(GrammarPath)) throw new ArgumentException("Required.", nameof(GrammarPath));
            if (!File.Exists(GrammarPath))
                throw new FileNotFoundException("The grammar the parser must read does not exist.", GrammarPath);
        }

        internal override void AddArguments(ProcessStartInfo startInfo, string scratch)
        {
            startInfo.ArgumentList.Add("grammar-health");
            startInfo.ArgumentList.Add(GrammarPath);
            startInfo.ArgumentList.Add(Path.Combine(scratch, "health.json"));
        }

        internal override PanGlossOutcome Finish(string scratch, string standardOutput, string standardError, TimeSpan elapsed)
        {
            var path = Path.Combine(scratch, "health.json");
            return File.Exists(path)
                ? new PanGlossOutcome.Completed(File.ReadAllText(path), standardError, elapsed)
                : new PanGlossOutcome.Incomplete(
                    $"pangloss grammar-health exited 0 but wrote no findings to '{path}'.", standardError);
        }
    }

    /// <summary><c>pangloss import</c>: the grammar snapshot of a saved <c>.fwdata</c>, written where the caller says.</summary>
    public sealed record Import(string FwDataPath, string GrammarJsonPath) : PanGlossRequest
    {
        public override string Subcommand => "import";

        internal override void Validate()
        {
            if (string.IsNullOrWhiteSpace(FwDataPath)) throw new ArgumentException("Required.", nameof(FwDataPath));
            if (string.IsNullOrWhiteSpace(GrammarJsonPath)) throw new ArgumentException("Required.", nameof(GrammarJsonPath));
            if (!File.Exists(FwDataPath))
                throw new FileNotFoundException("The project file the parser must read does not exist.", FwDataPath);
        }

        internal override void AddArguments(ProcessStartInfo startInfo, string scratch)
        {
            startInfo.ArgumentList.Add("import");
            startInfo.ArgumentList.Add(FwDataPath);
            startInfo.ArgumentList.Add(GrammarJsonPath);
        }

        internal override PanGlossOutcome Finish(string scratch, string standardOutput, string standardError, TimeSpan elapsed) =>
            File.Exists(GrammarJsonPath)
                ? new PanGlossOutcome.Completed(string.Empty, standardError, elapsed)
                : new PanGlossOutcome.Incomplete(
                    $"pangloss import exited 0 but wrote no grammar to '{GrammarJsonPath}'.", standardError);
    }
}
