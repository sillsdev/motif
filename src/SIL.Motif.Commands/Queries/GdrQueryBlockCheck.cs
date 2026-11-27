using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Gdr;
using SIL.Motif.Contract.Query;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;
using SIL.Motif.Host.Gdr;

namespace SIL.Motif.Commands.Queries;

/// <summary>
/// The part of the Guide check that runs the Guide's <c>motif-query</c> blocks (ADR 0005). Each block goes through the evaluator
/// the app uses: <see cref="GuideEvidenceRunner"/> for a corpus line and <see cref="FormsQueryCommand"/> (as of the Baseline) for a
/// <c>motif query</c> line. A block that is refused, failed or cold is a finding; nothing here blocks or stores a result, and it builds a
/// missing or stale parse index only when <c>buildIndex</c> is set (the check's <c>--build</c>).
/// </summary>
public static class GdrQueryBlockCheck
{
    /// <summary>Runs (or lists as skipped) <paramref name="sources"/>; the runs come in Guide order and the findings follow them.</summary>
    public static (IReadOnlyList<GdrQueryBlockRun> Runs, IReadOnlyList<GdrSyncFinding> Findings) Check(
        IReadOnlyList<GdrQueryBlockSource> sources, string projectPath, bool run, TimeSpan timeout, string? parserPath, bool buildIndex = false) =>
        Check(sources, projectPath, run, timeout, parserPath, (Seams?)null, buildIndex);

    /// <summary>As the public overload, with only the corpus evaluator stood in.</summary>
    internal static (IReadOnlyList<GdrQueryBlockRun> Runs, IReadOnlyList<GdrSyncFinding> Findings) Check(
        IReadOnlyList<GdrQueryBlockSource> sources, string projectPath, bool run, TimeSpan timeout, string? parserPath,
        Func<string, string, CancellationToken, GuideEvidenceResult> corpusEvaluator) =>
        Check(sources, projectPath, run, timeout, parserPath, new Seams(corpusEvaluator));

    /// <summary>As the public overload; <paramref name="seams"/> stand in for the evaluators in tests.</summary>
    internal static (IReadOnlyList<GdrQueryBlockRun> Runs, IReadOnlyList<GdrSyncFinding> Findings) Check(
        IReadOnlyList<GdrQueryBlockSource> sources, string projectPath, bool run, TimeSpan timeout, string? parserPath,
        Seams? seams, bool buildIndex = false)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(projectPath);
        var runs = new List<GdrQueryBlockRun>();
        var findings = new List<GdrSyncFinding>();
        var abandoned = new List<Task>();
        foreach (var source in sources)
        {
            foreach (var diagnostic in source.Diagnostics.Where(d => d.Kind != GuideBlockDiagnosticKind.InvalidRun || source.Query is null))
                findings.Add(new GdrSyncFinding(GdrSyncCodes.QueryBlockInvalid,
                    $"{Place(source.Gdr, source.Section)}: {diagnostic.Message} (line {diagnostic.Line}). Fix the block; it is advisory and nothing was blocked.",
                    source.Gdr, source.Section, null, 1, [source.Query?.Id ?? "(no id)"]));
            if (source.Query is not { } query) continue;
            if (!PatternNormalization.IsNormal(query.Run))
                findings.Add(new GdrSyncFinding(GdrSyncCodes.QueryNotNormalized,
                    $"{Place(source.Gdr, source.Section)}, query {query.Id}: the run line is not in NFC, the form Query matches in. Query composes the pattern and each corpus form to NFC before matching, so the count is the same, but a pattern that is decomposed (such as a Tamil two-part vowel sign as two code points) can be refused when it cannot be composed safely. Save the line composed (NFC).",
                    source.Gdr, source.Section, null, 1, [query.Id]));
            var result = Describe(source, query, run, timeout, parserPath, projectPath, seams, abandoned, buildIndex);
            runs.Add(result);
            if (result.Status is GdrQueryBlockStatus.Ok or GdrQueryBlockStatus.Skipped) continue;
            var code = result.Status switch
            {
                GdrQueryBlockStatus.NotBuilt => GdrSyncCodes.QueryNotBuilt,
                GdrQueryBlockStatus.Failed => GdrSyncCodes.QueryFailed,
                _ => GdrSyncCodes.QueryRefused,
            };
            findings.Add(new GdrSyncFinding(code,
                $"{Place(source.Gdr, source.Section)}, query {query.Id}: {(result.Code is { } c && result.Status == GdrQueryBlockStatus.Refused ? c + ": " : "")}{result.Message}",
                source.Gdr, source.Section, null, 1, [query.Id]));
        }
        // A v1 query takes no token and runs on; waiting briefly lets it free its Baseline copy.
        if (abandoned.Count > 0) Task.WaitAll([.. abandoned.Select(task => task.ContinueWith(_ => { }, TaskScheduler.Default))], AbandonedGrace);
        return (runs, findings);
    }

    private static readonly TimeSpan AbandonedGrace = TimeSpan.FromSeconds(5);

    private static GdrQueryBlockRun Describe(GdrQueryBlockSource source, GuideQueryDefinition query, bool run, TimeSpan timeout, string? parserPath, string projectPath,
        Seams? seams, List<Task> abandoned, bool buildIndex)
    {
        GdrQueryBlockRun Made(string status, string? code, string message, long? rows = null, bool? complete = null, double ms = 0, string? build = null,
            long? words = null, long? tokens = null, bool built = false) =>
            new(query.Id, source.Gdr, source.Section, status, code, message, rows, complete, ms, build, words, tokens, built);
        if (!run) return Made(GdrQueryBlockStatus.Skipped, null, "not run (--no-run)");
        // A run line the block parser already refused has a code; running it would only say the same again.
        if (source.Diagnostics.FirstOrDefault(d => d.Kind == GuideBlockDiagnosticKind.InvalidRun) is { } invalid)
            return Made(GdrQueryBlockStatus.Refused, invalid.RefusalCode ?? RefusalCodes.GuideQueryRunInvalid, invalid.Message);
        // Answer a line the parser turns down before starting the timed task, so load cannot turn it into a timeout.
        if (RefusedBeforeRunning(query) is { } early) return Made(early.Status, early.Code, early.Message);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        using var cancellation = new CancellationTokenSource();
        var work = Task.Run(() => Evaluate(query, projectPath, parserPath, cancellation.Token, seams), CancellationToken.None);
        bool finished;
        try { finished = work.Wait(timeout); }
        catch (AggregateException exception)
        {
            var cause = exception.GetBaseException();
            return Made(GdrQueryBlockStatus.Failed, null, cause.GetType().Name + ": " + cause.Message, ms: Math.Round(clock.Elapsed.TotalMilliseconds, 1));
        }
        var elapsed = Math.Round(clock.Elapsed.TotalMilliseconds, 1);
        if (!finished)
        {
            // The evaluator watches the token between phases; a phase already running is left to end on its own.
            cancellation.Cancel();
            abandoned.Add(work);
            return Made(GdrQueryBlockStatus.Failed, null, $"timed out after {timeout.TotalSeconds:0.#} s (--timeout)", ms: elapsed);
        }
        var outcome = work.Result;
        var built = false;
        if (buildIndex && outcome.Status == GdrQueryBlockStatus.NotBuilt)
        {
            // The build is the CLI line's own; the timeout bounds queries, not it.
            try { outcome = Evaluate(query, projectPath, parserPath, CancellationToken.None, seams, build: true); built = true; }
            catch (Exception exception)
            {
                return Made(GdrQueryBlockStatus.Failed, null, "building the parse index failed: " + exception.GetType().Name + ": " + exception.Message,
                    ms: Math.Round(clock.Elapsed.TotalMilliseconds, 1));
            }
            elapsed = Math.Round(clock.Elapsed.TotalMilliseconds, 1);
        }
        return Made(outcome.Status, outcome.Code, built && outcome.Status == GdrQueryBlockStatus.Ok ? "built the parse index; " + outcome.Message : outcome.Message,
            outcome.Rows, outcome.Complete, elapsed, outcome.Build, outcome.Words, outcome.Tokens, built);
    }

    /// <summary>Test stand-ins: the corpus evaluator (run line, base folder, token) and the v1 forms runner.</summary>
    internal sealed record Seams(Func<string, string, CancellationToken, GuideEvidenceResult>? Corpus = null,
        Func<FormsQueryRequest, CommandOutcome<FormsQueryResponse>>? Forms = null,
        Func<string, string, CancellationToken, GuideEvidenceResult>? CorpusBuilding = null);

    private sealed record Outcome(string Status, string? Code, string Message, long? Rows = null, bool? Complete = null, string? Build = null,
        long? Words = null, long? Tokens = null);

    private static Outcome? RefusedBeforeRunning(GuideQueryDefinition query)
    {
        if (!QueryRunLine.TryParseRun(query.Run, out var line, out var refusal))
            return new Outcome(GdrQueryBlockStatus.Refused, refusal.Code, refusal.Message);
        if (line.Corpus is not null) return null;
        if (line.Verb != QueryRunLine.QueryVerb || line.Query is null)
            return new Outcome(GdrQueryBlockStatus.Refused, RefusalCodes.GuideQueryRunInvalid,
                "a motif group line is not run by the check; use a motif query line.");
        return line.AsOf is not null
            ? new Outcome(GdrQueryBlockStatus.Refused, RefusalCodes.GuideQueryRunInvalid, "a Guide query reads the Baseline and takes no --as-of of its own.")
            : null;
    }

    private static Outcome Evaluate(GuideQueryDefinition query, string projectPath, string? parserPath, CancellationToken cancellationToken,
        Seams? seams, bool build = false)
    {
        if (!QueryRunLine.TryParseRun(query.Run, out var line, out var refusal))
            return new Outcome(GdrQueryBlockStatus.Refused, refusal.Code, refusal.Message);
        if (line.Corpus is not null)
        {
            var standIn = build ? seams?.CorpusBuilding ?? seams?.Corpus : seams?.Corpus;
            var evidence = (standIn?.Invoke(query.Run, BaseDirectory(projectPath), cancellationToken) ??
                    GuideEvidenceRunner.Evaluate(query.Run, BaseDirectory(projectPath), parserPath, cancellationToken, projectPath, buildIndex: build))
                .Shaped(query.Columns, query.Sort, null);
            return evidence.Status switch
            {
                // A group-by table lists groups: its rows are the groups, its evidence size the words and tokens.
                GuideEvidenceStatus.Ready when evidence.Request?.GroupBy is not null => new Outcome(GdrQueryBlockStatus.Ok, null, evidence.Message,
                    evidence.Table!.Rows.Count + evidence.Table.Truncated, evidence.Table.Complete, Words: evidence.Table.Types, Tokens: evidence.Table.Tokens),
                GuideEvidenceStatus.Ready => new Outcome(GdrQueryBlockStatus.Ok, null, evidence.Message, evidence.Table!.Types, evidence.Table.Complete, Tokens: evidence.Table.Tokens),
                GuideEvidenceStatus.ColdIndex => new Outcome(GdrQueryBlockStatus.NotBuilt, evidence.RefusalCode,
                    evidence.Message + $"; or run {GuideCheckBuildCommand(projectPath)} to build every missing index", Build: evidence.BuildCommand),
                _ => new Outcome(GdrQueryBlockStatus.Refused, evidence.RefusalCode, evidence.Message),
            };
        }
        if (line.Verb != QueryRunLine.QueryVerb || line.Query is null)
            return new Outcome(GdrQueryBlockStatus.Refused, RefusalCodes.GuideQueryRunInvalid,
                "a motif group line is not run by the check; use a motif query line.");
        if (line.AsOf is not null)
            return new Outcome(GdrQueryBlockStatus.Refused, RefusalCodes.GuideQueryRunInvalid, "a Guide query reads the Baseline and takes no --as-of of its own.");
        if (!QueryRunLine.TryParseRun(query.Run.Trim() + " --as-of baseline", out var based, out var baseRefusal) || based.Query is null)
            return new Outcome(GdrQueryBlockStatus.Refused, baseRefusal?.Code ?? RefusalCodes.GuideQueryRunInvalid, baseRefusal?.Message ?? "the line cannot be read as of the Baseline.");
        var formsRequest = new FormsQueryRequest(projectPath, MotifProductVersion.CurrentText, based.Query.Record);
        var outcome = seams?.Forms is { } forms ? forms(formsRequest) : FormsQueryCommand.Run(formsRequest, parserPath: parserPath);
        if (!outcome.Succeeded || outcome.Value is not { } response)
            return new Outcome(GdrQueryBlockStatus.Refused, outcome.Refusal?.Code, outcome.Refusal?.Message ?? "the query gave no answer.");
        var rows = response.Sides?.Rows.Count ?? (response.Result.Groups.Count > 0 ? response.Result.Groups.Count : response.Result.Rows.Count);
        return new Outcome(GdrQueryBlockStatus.Ok, null, $"{rows} row(s)", rows, true);
    }

    /// <summary>The command that builds every missing parse index and then checks the blocks.</summary>
    internal static string GuideCheckBuildCommand(string projectPath) => $"motif guide check --project '{projectPath.Replace("'", "'\\''", StringComparison.Ordinal)}' --build";

    private static string BaseDirectory(string projectPath) =>
        Directory.Exists(projectPath) ? projectPath : Path.GetDirectoryName(Path.GetFullPath(projectPath)) ?? ".";

    private static string Place(string gdr, string? section) => string.IsNullOrEmpty(section) ? gdr : $"{gdr}, {section}";
}
