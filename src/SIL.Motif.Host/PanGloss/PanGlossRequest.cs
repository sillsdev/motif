using System.Diagnostics;
using System.Globalization;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Jobs;

namespace SIL.Motif.Host.PanGloss;

/// <summary>
/// One typed request for one subcommand the shipped <c>pangloss</c> binary has. This file is the only place
/// Motif writes down the binary's command-line surface; the <c>assess</c> subcommand is absent because the
/// binary has none.
/// </summary>
public abstract record PanGlossRequest
{
    public const long DefaultPerWordStepLimit = StepCap.DefaultSteps;

    private PanGlossRequest() { }

    /// <summary>The subcommand this request runs, as the binary spells it.</summary>
    public abstract string Subcommand { get; }

    /// <summary>Whether a nonzero exit that wrote <paramref name="standardOutput"/> still produced a usable result.</summary>
    internal virtual bool AcceptsNonzeroExit(string standardOutput) => false;

    /// <summary>Throws for a request a caller has built wrongly; this is programmer error, not an outcome.</summary>
    internal abstract void Validate();

    /// <summary>Writes whatever the subcommand must read from disk into the invocation's scratch directory.</summary>
    internal virtual Task PrepareAsync(string scratch, CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Appends the subcommand and its arguments, one item each so paths need no quoting.</summary>
    internal abstract void AddArguments(ProcessStartInfo startInfo, string scratch);

    /// <summary>Turns process output into the request's outcome.</summary>
    internal abstract PanGlossOutcome Finish(string scratch, string standardOutput, string standardError,
        TimeSpan elapsed, out BatchInvocationEvidence.BatchFileDigests? batchFileDigests);

    /// <summary>
    /// <c>pangloss batch</c> over a word list, a thread budget derived from half the machine's processor count
    /// with a minimum of one, a per-word limit, and optionally the per-object statistics cache. The queue
    /// admits one job machine-wide; operating-system CPU rate limits apply where available.
    /// </summary>
    public sealed record Batch(
        string ProjectFilePath, IReadOnlyList<string> Words, TimeSpan? PerWordLimit, string? StatsCachePath = null,
        StepCap? PerWordStepLimit = null, string? ArtifactDirectory = null)
        : PanGlossRequest
    {
        public bool CollectAnalyses { get; init; }

        /// <summary>PanGloss worker threads for this batch, populated from the machine CPU budget.</summary>
        internal int ThreadCount { get; init; } = PanGlossCpuBudget.DefaultBatchThreadCount;

        /// <summary>Receives progress from the batch's flushed word rows.</summary>
        public Action<TrialWordProgress>? OnProgress { get; init; }

        public override string Subcommand => "batch";

        internal override void Validate()
        {
            if (string.IsNullOrWhiteSpace(ProjectFilePath)) throw new ArgumentException("Required.", nameof(ProjectFilePath));
            ArgumentNullException.ThrowIfNull(Words);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ThreadCount);
            if (PerWordLimit is { } timeLimit && timeLimit <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(PerWordLimit), "A per-word limit must be positive.");
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
            if (PerWordLimit is { } timeLimit)
            {
                startInfo.ArgumentList.Add("--word-timeout-ms");
                startInfo.ArgumentList.Add(((int)timeLimit.TotalMilliseconds).ToString(CultureInfo.InvariantCulture));
            }
            startInfo.ArgumentList.Add("--step-cap");
            startInfo.ArgumentList.Add((PerWordStepLimit ?? StepCap.Default).ToArgument());
            startInfo.ArgumentList.Add("--threads");
            startInfo.ArgumentList.Add(ThreadCount.ToString(CultureInfo.InvariantCulture));
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

        internal override PanGlossOutcome Finish(string scratch, string standardOutput, string standardError,
            TimeSpan elapsed, out BatchInvocationEvidence.BatchFileDigests? batchFileDigests)
        {
            batchFileDigests = null;
            if (StatsCachePath is not null && !File.Exists(StatsCachePath))
            {
                return new PanGlossOutcome.Incomplete(
                    $"pangloss batch --stats exited 0 but wrote no cache to '{StatsCachePath}'.", standardError);
            }
            var outPath = Path.Combine(scratch, "out.tsv");
            BatchInvocationEvidence.TextSnapshot? tsvSnapshot = null;
            var tsv = string.Empty;
            if (File.Exists(outPath))
            {
                if (ArtifactDirectory is null) tsv = File.ReadAllText(outPath);
                else
                {
                    tsvSnapshot = BatchInvocationEvidence.ReadTextWithDigest(outPath);
                    tsv = tsvSnapshot.Text.TrimStart('\uFEFF');
                }
            }
            var analysesPath = Path.Combine(scratch, "analyses.jsonl");
            if (CollectAnalyses && !File.Exists(analysesPath))
                return new PanGlossOutcome.Incomplete("The batch wrote no requested morphology evidence.", standardError);
            BatchInvocationEvidence.TextSnapshot? analysesSnapshot = null;
            string? morphology = null;
            if (CollectAnalyses)
            {
                if (ArtifactDirectory is null) morphology = File.ReadAllText(analysesPath);
                else
                {
                    analysesSnapshot = BatchInvocationEvidence.ReadTextWithDigest(analysesPath);
                    morphology = analysesSnapshot.Text.TrimStart('\uFEFF');
                }
            }
            if (ArtifactDirectory is not null)
                batchFileDigests = new BatchInvocationEvidence.BatchFileDigests(
                    tsvSnapshot?.Sha256, analysesSnapshot?.Sha256);
            return new PanGlossOutcome.Completed(tsv, standardError, elapsed)
            {
                MorphologyOutput = morphology,
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

        internal override PanGlossOutcome Finish(string scratch, string standardOutput, string standardError,
            TimeSpan elapsed, out BatchInvocationEvidence.BatchFileDigests? batchFileDigests)
        {
            batchFileDigests = null;
            return new PanGlossOutcome.Completed(standardOutput, standardError, elapsed);
        }
    }

    /// <summary>
    /// <c>pangloss parse &lt;grammar&gt; &lt;word&gt; --trace --trace-format json --trace-details</c>: one word,
    /// traced and unmerged, written as one JSON document holding the tree, the search state and the effort by
    /// kind of grammar object. The only subcommand that can trace at all — <c>batch</c> carries no
    /// <c>--trace</c> flag, and tracing runs unmerged deliberately, so this must stay a single-word request
    /// rather than growing a word list. Needs PanGloss 0.3.3 or later.
    /// </summary>
    /// <remarks>PanGloss signals a search stopped at its analysis-attempt cap, derived search-work cap, or time limit with a nonzero exit after writing its
    /// output, as an ordinary trace does from 0.5.2. A nonzero exit is accepted only when standard output holds a
    /// well-formed document reporting that stop, so the word shows as stopped; any other nonzero exit is refused.
    /// </remarks>
    public sealed record Trace(string GrammarPath, string Word) : PanGlossRequest
    {
        /// <summary>The parser's analysis-attempt cap for this word; <see langword="null"/> passes none and keeps PanGloss's own
        /// runaway guard.</summary>
        public StepCap? StepLimit { get; init; }

        public override string Subcommand => "parse";

        internal override bool AcceptsNonzeroExit(string standardOutput) =>
            PanGlossTraceOutput.TryRead(standardOutput, out var document, out _) &&
            document!.Details is { Capped: true } or { TimedOut: true };

        internal override void Validate()
        {
            if (string.IsNullOrWhiteSpace(GrammarPath)) throw new ArgumentException("Required.", nameof(GrammarPath));
            if (string.IsNullOrEmpty(Word)) throw new ArgumentException("Required.", nameof(Word));
            if (!File.Exists(GrammarPath))
                throw new FileNotFoundException("The grammar the parser must read does not exist.", GrammarPath);
        }

        internal override void AddArguments(ProcessStartInfo startInfo, string scratch)
        {
            // PanGloss's built-in 50,000,000-step runaway guard is kept for Try a Word by owner choice.
            startInfo.ArgumentList.Add("parse");
            startInfo.ArgumentList.Add(GrammarPath);
            startInfo.ArgumentList.Add(Word);
            startInfo.ArgumentList.Add("--trace");
            startInfo.ArgumentList.Add("--trace-format");
            startInfo.ArgumentList.Add("json");
            startInfo.ArgumentList.Add("--trace-details");
            if (StepLimit is null) return;
            startInfo.ArgumentList.Add("--step-cap");
            startInfo.ArgumentList.Add(StepLimit.ToArgument());
        }

        internal override PanGlossOutcome Finish(string scratch, string standardOutput, string standardError,
            TimeSpan elapsed, out BatchInvocationEvidence.BatchFileDigests? batchFileDigests)
        {
            batchFileDigests = null;
            return new PanGlossOutcome.Completed(standardOutput, standardError, elapsed);
        }
    }

    /// <summary>
    /// <c>pangloss grammar-health &lt;grammar&gt; --fw-project &lt;project&gt;</c>: a JSON grammar-health report on
    /// standard output. Omitting its optional output path keeps the report available when PanGloss exits
    /// nonzero to signal error-level diagnostics.
    /// </summary>
    /// <remarks>PanGloss can exit nonzero after writing an error report; that report remains usable. A nonzero exit
    /// without a report is refused.</remarks>
    public sealed record GrammarHealth(string GrammarPath, string FieldWorksProjectName) : PanGlossRequest
    {
        public override string Subcommand => "grammar-health";
        internal override bool AcceptsNonzeroExit(string standardOutput) => true;

        internal override void Validate()
        {
            if (string.IsNullOrWhiteSpace(GrammarPath)) throw new ArgumentException("Required.", nameof(GrammarPath));
            if (string.IsNullOrWhiteSpace(FieldWorksProjectName))
                throw new ArgumentException("Required.", nameof(FieldWorksProjectName));
            if (!File.Exists(GrammarPath))
                throw new FileNotFoundException("The grammar the parser must read does not exist.", GrammarPath);
        }

        internal override void AddArguments(ProcessStartInfo startInfo, string scratch)
        {
            startInfo.ArgumentList.Add("grammar-health");
            startInfo.ArgumentList.Add(GrammarPath);
            startInfo.ArgumentList.Add("--fw-project");
            startInfo.ArgumentList.Add(FieldWorksProjectName);
        }

        internal override PanGlossOutcome Finish(string scratch, string standardOutput, string standardError,
            TimeSpan elapsed, out BatchInvocationEvidence.BatchFileDigests? batchFileDigests)
        {
            batchFileDigests = null;
            return string.IsNullOrWhiteSpace(standardOutput)
                ? new PanGlossOutcome.Incomplete(
                    "pangloss grammar-health exited 0 but wrote no findings to standard output.", standardError)
                : new PanGlossOutcome.Completed(standardOutput, standardError, elapsed);
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

        internal override PanGlossOutcome Finish(string scratch, string standardOutput, string standardError,
            TimeSpan elapsed, out BatchInvocationEvidence.BatchFileDigests? batchFileDigests)
        {
            batchFileDigests = null;
            return File.Exists(GrammarJsonPath)
                ? new PanGlossOutcome.Completed(string.Empty, standardError, elapsed)
                : new PanGlossOutcome.Incomplete(
                    $"pangloss import exited 0 but wrote no grammar to '{GrammarJsonPath}'.", standardError);
        }
    }
}
