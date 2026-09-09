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
        string ProjectFilePath, IReadOnlyList<string> Words, TimeSpan PerWordLimit, string? StatsCachePath = null)
        : PanGlossRequest
    {
        public override string Subcommand => "batch";

        internal override void Validate()
        {
            if (string.IsNullOrWhiteSpace(ProjectFilePath)) throw new ArgumentException("Required.", nameof(ProjectFilePath));
            ArgumentNullException.ThrowIfNull(Words);
            if (PerWordLimit <= TimeSpan.Zero)
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
            startInfo.ArgumentList.Add("--word-timeout-ms");
            startInfo.ArgumentList.Add(((int)PerWordLimit.TotalMilliseconds).ToString(CultureInfo.InvariantCulture));
            startInfo.ArgumentList.Add("--threads");
            startInfo.ArgumentList.Add("1");
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
            return new PanGlossOutcome.Completed(tsv, standardError, elapsed);
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
