using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using SIL.Motif.Contract.Phonology;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Host.Parsimony;

namespace SIL.Motif.Host.PanGloss;

/// <summary>
/// One typed request for a PanGloss subcommand. Command availability is checked per request so an older
/// executable can still run supported commands, pinned by `MissingProducerAndOldCapabilityPreserveOrdinaryRequests`.
/// </summary>
public abstract record PanGlossRequest
{
    public const long DefaultPerWordParserWorkLimit = ParserWorkLimit.DefaultWork;

    private PanGlossRequest() { }

    /// <summary>Receives whether this request is waiting for machine parser capacity.</summary>
    [JsonIgnore]
    public Action<bool>? OnAdmissionWait { get; init; }

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

    /// <summary>Measures explicit phonology examples under one request-wide resource ceiling.</summary>
    public sealed record PhonologyExamples(string GrammarPath, IReadOnlyList<PhonologyExample> Examples,
        PhonologyLimits Limits) : PanGlossRequest
    {
        internal PhonologyInvocationInputs? Inputs { get; init; }
        public override string Subcommand => "phonology-examples";
        internal override void Validate() => PhonologyInvocationInputs.Freeze(this);
        internal override void AddArguments(ProcessStartInfo startInfo, string scratch)
        {
            startInfo.ArgumentList.Add(Subcommand);
            startInfo.ArgumentList.Add(GrammarPath);
            startInfo.ArgumentList.Add(Path.Combine(scratch, "request.json"));
            startInfo.ArgumentList.Add(Path.Combine(scratch, "result.json"));
        }

        internal override PanGlossOutcome Finish(string scratch, string standardOutput, string standardError,
            TimeSpan elapsed, out BatchInvocationEvidence.BatchFileDigests? batchFileDigests)
        {
            batchFileDigests = null;
            try
            {
                var bytes = PhonologyInvocationInputs.ReadBounded(Path.Combine(scratch, "result.json"));
                var measurement = PhonologyExamplesReader.Read(bytes, Examples, Limits,
                    Inputs ?? throw new InvalidOperationException("Phonology inputs were not prepared."));
                return new PanGlossOutcome.Completed(string.Empty, standardError, elapsed) { Phonology = measurement };
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or
                ArgumentException or InvalidOperationException or FormatException or OverflowException)
            {
                return new PanGlossOutcome.Incomplete("Invalid phonology evidence: " + exception.Message, standardError);
            }
        }
    }

    /// <summary>
    /// <c>pangloss batch</c> over a word list, a thread budget derived from half the machine's processor count
    /// with a minimum of one, a per-word limit, and optionally the per-object statistics cache. The queue
    /// admits one job machine-wide; operating-system CPU rate limits apply where available.
    /// </summary>
    public sealed record Batch(
        string ProjectFilePath, IReadOnlyList<string> Words, TimeSpan? PerWordLimit, string? StatsCachePath = null,
        ParserWorkLimit? PerWordParserWorkLimit = null, string? ArtifactDirectory = null)
        : PanGlossRequest
    {
        public bool CollectAnalyses { get; init; }

        /// <summary>A snapshot produced from this run's project, bound to both files' exact bytes.</summary>
        public PanGlossImportedSnapshot? ImportedSnapshot { get; init; }

        internal string? SharedArtifactDirectory { get; init; }

        /// <summary>Optional provenance manifest binding the statistics cache to its exact grammar input bytes.</summary>
        public string? StatsManifestPath { get; init; }

        internal bool WorkColumns { get; init; }

        /// <summary>Allows a retry to finish without a statistics cache, leaving its unavailable counters unknown.</summary>
        internal bool AllowMissingStatsCache { get; init; }

        /// <summary>PanGloss worker threads for this batch, populated from the machine CPU budget.</summary>
        internal int ThreadCount { get; init; } = PanGlossCpuBudget.DefaultBatchThreadCount;

        /// <summary>Receives progress from the batch's flushed word rows.</summary>
        public Action<TrialWordProgress>? OnProgress { get; init; }

        /// <summary>Receives validated, attributable morphology rows from an interrupted batch.</summary>
        public Action<IReadOnlyList<SIL.Motif.Contract.Responses.ParseWordEvidence>>? OnPartial { get; init; }

        public override string Subcommand => "batch";

        internal override void Validate()
        {
            if (string.IsNullOrWhiteSpace(ProjectFilePath)) throw new ArgumentException("Required.", nameof(ProjectFilePath));
            ArgumentNullException.ThrowIfNull(Words);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ThreadCount);
            if (StatsManifestPath is not null &&
                (string.IsNullOrWhiteSpace(StatsManifestPath) || string.IsNullOrWhiteSpace(StatsCachePath)))
                throw new ArgumentException("A statistics manifest requires an output path and statistics cache.",
                    nameof(StatsManifestPath));
            if (PerWordLimit is { } timeLimit && timeLimit <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(PerWordLimit), "A per-word limit must be positive.");
            if (!File.Exists(ProjectFilePath))
                throw new FileNotFoundException("The project file the parser must read does not exist.", ProjectFilePath);
            if (ImportedSnapshot is { } snapshot && !snapshot.Matches(ProjectFilePath))
                throw new InvalidDataException("The imported snapshot or its project changed.");
        }

        internal override Task PrepareAsync(string scratch, CancellationToken cancellationToken) =>
            File.WriteAllLinesAsync(Path.Combine(scratch, "words.txt"), Words, cancellationToken);

        internal override void AddArguments(ProcessStartInfo startInfo, string scratch)
        {
            startInfo.ArgumentList.Add("batch");
            startInfo.ArgumentList.Add(ImportedSnapshot?.Path ?? ProjectFilePath);
            startInfo.ArgumentList.Add(Path.Combine(scratch, "words.txt"));
            startInfo.ArgumentList.Add(Path.Combine(scratch, "out.tsv"));
            if (PerWordLimit is { } timeLimit)
            {
                startInfo.ArgumentList.Add("--word-timeout-ms");
                startInfo.ArgumentList.Add(((int)timeLimit.TotalMilliseconds).ToString(CultureInfo.InvariantCulture));
            }
            startInfo.ArgumentList.Add("--work-cap");
            startInfo.ArgumentList.Add((PerWordParserWorkLimit ?? ParserWorkLimit.Default).ToWorkCapArgument());
            if (WorkColumns) startInfo.ArgumentList.Add("--work-columns");
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
            if (StatsManifestPath is null) return;
            startInfo.ArgumentList.Add("--stats-manifest");
            startInfo.ArgumentList.Add(StatsManifestPath);
        }

        internal override PanGlossOutcome Finish(string scratch, string standardOutput, string standardError,
            TimeSpan elapsed, out BatchInvocationEvidence.BatchFileDigests? batchFileDigests)
        {
            batchFileDigests = null;
            if (StatsManifestPath is not null && !File.Exists(StatsManifestPath))
                return new PanGlossOutcome.Incomplete("The batch wrote no requested statistics manifest.", standardError);
            if (StatsCachePath is not null && !File.Exists(StatsCachePath) && !AllowMissingStatsCache)
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
    /// <remarks>PanGloss signals a search stopped at its parser work limit, derived search-work cap, or time limit with a nonzero exit after writing its
    /// output, as an ordinary trace does from 0.5.2. A nonzero exit is accepted only when standard output holds a
    /// well-formed document reporting that stop, so the word shows as stopped; any other nonzero exit is refused.
    /// </remarks>
    public sealed record Trace(string GrammarPath, string Word) : PanGlossRequest
    {
        /// <summary>The parser's parser work limit for this word; <see langword="null"/> passes none and keeps PanGloss's own
        /// runaway guard.</summary>
        public ParserWorkLimit? ParserWorkLimit { get; init; }

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
            if (ParserWorkLimit is null) return;
            startInfo.ArgumentList.Add("--work-cap");
            startInfo.ArgumentList.Add(ParserWorkLimit.ToWorkCapArgument());
        }

        internal override PanGlossOutcome Finish(string scratch, string standardOutput, string standardError,
            TimeSpan elapsed, out BatchInvocationEvidence.BatchFileDigests? batchFileDigests)
        {
            batchFileDigests = null;
            return new PanGlossOutcome.Completed(standardOutput, standardError, elapsed);
        }
    }

    /// <summary>
    /// <c>pangloss grammar-health &lt;grammar&gt; --fw-project &lt;project&gt; --stored-analyses &lt;mode&gt;</c>: a JSON
    /// grammar-health report on standard output. Omitting its optional output path keeps the report available
    /// when PanGloss exits nonzero to signal error-level diagnostics.
    /// </summary>
    /// <remarks>PanGloss can exit nonzero after writing an error report; that report remains usable. A nonzero exit
    /// without a report is refused.</remarks>
    /// <param name="GrammarPath">The grammar, or saved project, PanGloss checks.</param>
    /// <param name="FieldWorksProjectName">The FieldWorks project name PanGloss puts in its deep links.</param>
    /// <param name="StoredAnalyses">How much of the stored-analysis comparison PanGloss runs.</param>
    public sealed record GrammarHealth(string GrammarPath, string FieldWorksProjectName,
        StoredAnalysisComparison StoredAnalyses = StoredAnalysisComparison.Budget) : PanGlossRequest
    {
        /// <summary>The current run's snapshot output, or null when only a report is needed.</summary>
        public string? SnapshotPath { get; init; }

        /// <summary>The frozen facts context; requires a snapshot output and retains the facts under a lease.</summary>
        public string? FactsContextJson { get; init; }

        /// <summary>Whether facts omit the sections no Parsimony measure reads.</summary>
        public bool FactsLight { get; init; }

        public override string Subcommand => "grammar-health";
        internal override bool AcceptsNonzeroExit(string standardOutput) => true;

        internal override void Validate()
        {
            if (string.IsNullOrWhiteSpace(GrammarPath)) throw new ArgumentException("Required.", nameof(GrammarPath));
            if (string.IsNullOrWhiteSpace(FieldWorksProjectName))
                throw new ArgumentException("Required.", nameof(FieldWorksProjectName));
            if (!Enum.IsDefined(StoredAnalyses)) throw new ArgumentException("Unknown mode.", nameof(StoredAnalyses));
            if (FactsContextJson is not null && SnapshotPath is null)
                throw new ArgumentException("Grammar facts require a snapshot output.", nameof(SnapshotPath));
            if (FactsLight && FactsContextJson is null)
                throw new ArgumentException("Light facts require a facts context.", nameof(FactsLight));
            if (SnapshotPath is not null && (string.IsNullOrWhiteSpace(SnapshotPath) ||
                Path.GetExtension(GrammarPath).ToLowerInvariant() is not (".fwdata" or ".fwbackup")))
                throw new ArgumentException("Snapshot output requires a FieldWorks project input.", nameof(SnapshotPath));
            if (!File.Exists(GrammarPath))
                throw new FileNotFoundException("The grammar the parser must read does not exist.", GrammarPath);
        }

        internal override Task PrepareAsync(string scratch, CancellationToken cancellationToken) =>
            FactsContextJson is null ? Task.CompletedTask :
                File.WriteAllTextAsync(Path.Combine(scratch, "facts-context.json"), FactsContextJson, cancellationToken);

        internal override void AddArguments(ProcessStartInfo startInfo, string scratch)
        {
            startInfo.ArgumentList.Add("grammar-health");
            startInfo.ArgumentList.Add(GrammarPath);
            startInfo.ArgumentList.Add("--fw-project");
            startInfo.ArgumentList.Add(FieldWorksProjectName);
            startInfo.ArgumentList.Add("--stored-analyses");
            startInfo.ArgumentList.Add(StoredAnalyses switch
            {
                StoredAnalysisComparison.Budget => "bounded",
                StoredAnalysisComparison.All => "all",
                _ => "off",
            });
            if (SnapshotPath is not null)
            {
                startInfo.ArgumentList.Add("--snapshot-out");
                startInfo.ArgumentList.Add(Path.GetFullPath(SnapshotPath));
            }
            if (FactsContextJson is not null)
            {
                startInfo.ArgumentList.Add("--facts-out");
                startInfo.ArgumentList.Add(Path.Combine(scratch, "grammar-facts.sqlite"));
                startInfo.ArgumentList.Add("--facts-context");
                startInfo.ArgumentList.Add(Path.Combine(scratch, "facts-context.json"));
                if (FactsLight) startInfo.ArgumentList.Add("--facts-light");
            }
        }

        internal override PanGlossOutcome Finish(string scratch, string standardOutput, string standardError,
            TimeSpan elapsed, out BatchInvocationEvidence.BatchFileDigests? batchFileDigests)
        {
            batchFileDigests = null;
            if (string.IsNullOrWhiteSpace(standardOutput))
                return new PanGlossOutcome.Incomplete(
                    "pangloss grammar-health exited 0 but wrote no findings to standard output.", standardError);
            if (SnapshotPath is not null && !File.Exists(SnapshotPath))
                return new PanGlossOutcome.Incomplete("pangloss grammar-health wrote no requested snapshot.", standardError);
            var completed = new PanGlossOutcome.Completed(standardOutput, standardError, elapsed);
            if (FactsContextJson is null) return completed;
            try
            {
                var artifact = GrammarFactsReader.ReadProduced(Path.Combine(scratch, "grammar-facts.sqlite"), SnapshotPath!);
                return completed with
                {
                    FactsArtifact = artifact,
                    Facts = new PanGlossFactsArtifact(artifact.Path, artifact.SchemaVersion,
                        artifact.SourceSha256, artifact.ModelFingerprint, artifact.OutputBytes, artifact.OutputSha256),
                };
            }
            catch (InvalidDataException exception)
            {
                return new PanGlossOutcome.Incomplete(
                    $"pangloss grammar-health wrote invalid facts: {exception.Message}", standardError);
            }
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

    /// <summary>Builds a retained grammar-facts database from one Snapshot and its frozen evidence context.</summary>
    /// <param name="SnapshotPath">The exact current pg-snapshot JSON input.</param>
    /// <param name="ContextJson">The versioned facts context passed to PanGloss.</param>
    /// <param name="ArtifactDirectory">An optional new directory to own the database and its invocation inputs.</param>
    /// <param name="Light">Whether to pass <c>--light</c>, which leaves out the tables and rows no Parsimony measure reads
    /// and marks the sections they narrow <c>partial</c> with reason <c>light</c>.</param>
    public sealed record Facts(string SnapshotPath, string ContextJson, string? ArtifactDirectory = null,
        string? StatsCachePath = null, string? StatsManifestPath = null, bool Light = false)
        : PanGlossRequest
    {
        public override string Subcommand => "facts";

        internal override bool AcceptsNonzeroExit(string standardOutput) =>
            GrammarFactsReader.ReportsCompileRefusal(standardOutput);

        internal override void Validate()
        {
            if (string.IsNullOrWhiteSpace(SnapshotPath)) throw new ArgumentException("Required.", nameof(SnapshotPath));
            ArgumentNullException.ThrowIfNull(ContextJson);
            if (Path.GetExtension(SnapshotPath) != ".json")
                throw new ArgumentException("Facts input must be a pg-snapshot JSON file.", nameof(SnapshotPath));
            if (!File.Exists(SnapshotPath))
                throw new FileNotFoundException("The Snapshot the parser must read does not exist.", SnapshotPath);
            if (ArtifactDirectory is not null && string.IsNullOrWhiteSpace(ArtifactDirectory))
                throw new ArgumentException("A supplied artifact directory must not be empty.", nameof(ArtifactDirectory));
            if ((StatsCachePath is null) != (StatsManifestPath is null))
                throw new ArgumentException("A statistics cache and manifest must be supplied together.");
            if (StatsCachePath is not null && (!File.Exists(StatsCachePath) || !File.Exists(StatsManifestPath)))
                throw new FileNotFoundException("The statistics cache and its provenance manifest must both exist.");
        }

        internal override Task PrepareAsync(string scratch, CancellationToken cancellationToken) =>
            File.WriteAllTextAsync(Path.Combine(scratch, "facts-context.json"), ContextJson, cancellationToken);

        internal override void AddArguments(ProcessStartInfo startInfo, string scratch)
        {
            startInfo.ArgumentList.Add("facts");
            startInfo.ArgumentList.Add(Path.GetFullPath(SnapshotPath));
            startInfo.ArgumentList.Add("--out");
            startInfo.ArgumentList.Add(Path.Combine(scratch, "grammar-facts.sqlite"));
            startInfo.ArgumentList.Add("--context");
            startInfo.ArgumentList.Add(Path.Combine(scratch, "facts-context.json"));
            if (StatsCachePath is not null)
            {
                startInfo.ArgumentList.Add("--stats");
                startInfo.ArgumentList.Add(Path.GetFullPath(StatsCachePath));
                startInfo.ArgumentList.Add("--stats-manifest");
                startInfo.ArgumentList.Add(Path.GetFullPath(StatsManifestPath!));
            }
            if (Light) startInfo.ArgumentList.Add("--light");
            startInfo.ArgumentList.Add("--json");
        }

        internal override PanGlossOutcome Finish(string scratch, string standardOutput, string standardError,
            TimeSpan elapsed, out BatchInvocationEvidence.BatchFileDigests? batchFileDigests)
        {
            batchFileDigests = null;
            var path = Path.Combine(scratch, "grammar-facts.sqlite");
            if (!File.Exists(path))
                return new PanGlossOutcome.Incomplete("pangloss facts exited 0 but wrote no facts database.", standardError);
            try
            {
                var artifact = GrammarFactsReader.Read(path, standardOutput, SnapshotPath);
                return new PanGlossOutcome.Completed(standardOutput, standardError, elapsed)
                {
                    FactsArtifact = artifact,
                    Facts = new PanGlossFactsArtifact(artifact.Path, artifact.SchemaVersion,
                        artifact.SourceSha256, artifact.ModelFingerprint, artifact.OutputBytes, artifact.OutputSha256),
                };
            }
            catch (InvalidDataException exception)
            {
                return new PanGlossOutcome.Incomplete(
                    $"pangloss facts wrote an invalid facts artifact: {exception.Message}", standardError);
            }
        }
    }
}
