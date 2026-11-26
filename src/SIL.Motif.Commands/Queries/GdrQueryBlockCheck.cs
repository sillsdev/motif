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
/// <c>motif query</c> line. A block that is refused, failed or cold is a finding; nothing here blocks, builds an index or stores a result.
/// </summary>
public static class GdrQueryBlockCheck
{
    /// <summary>Runs (or lists as skipped) <paramref name="sources"/>; the runs come in Guide order and the findings follow them.</summary>
    public static (IReadOnlyList<GdrQueryBlockRun> Runs, IReadOnlyList<GdrSyncFinding> Findings) Check(
        IReadOnlyList<GdrQueryBlockSource> sources, string projectPath, bool run, TimeSpan timeout, string? parserPath) =>
        Check(sources, projectPath, run, timeout, parserPath, null);

    /// <summary>As the public overload; <paramref name="corpusEvaluator"/> (run line, base folder, token) stands in for the app's corpus evaluator in tests.</summary>
    internal static (IReadOnlyList<GdrQueryBlockRun> Runs, IReadOnlyList<GdrSyncFinding> Findings) Check(
        IReadOnlyList<GdrQueryBlockSource> sources, string projectPath, bool run, TimeSpan timeout, string? parserPath,
        Func<string, string, CancellationToken, GuideEvidenceResult>? corpusEvaluator)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(projectPath);
        var runs = new List<GdrQueryBlockRun>();
        var findings = new List<GdrSyncFinding>();
        foreach (var source in sources)
        {
            foreach (var diagnostic in source.Diagnostics.Where(d => d.Kind != GuideBlockDiagnosticKind.InvalidRun || source.Query is null))
                findings.Add(new GdrSyncFinding(GdrSyncCodes.QueryBlockInvalid,
                    $"{Place(source.Gdr, source.Section)}: {diagnostic.Message} (line {diagnostic.Line}). Fix the block; it is advisory and nothing was blocked.",
                    source.Gdr, source.Section, null, 1, [source.Query?.Id ?? "(no id)"]));
            if (source.Query is not { } query) continue;
            var result = Describe(source, query, run, timeout, parserPath, projectPath, corpusEvaluator);
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
        return (runs, findings);
    }

    private static GdrQueryBlockRun Describe(GdrQueryBlockSource source, GuideQueryDefinition query, bool run, TimeSpan timeout, string? parserPath, string projectPath,
        Func<string, string, CancellationToken, GuideEvidenceResult>? corpusEvaluator)
    {
        GdrQueryBlockRun Made(string status, string? code, string message, long? rows = null, bool? complete = null, double ms = 0, string? build = null) =>
            new(query.Id, source.Gdr, source.Section, status, code, message, rows, complete, ms, build);
        if (!run) return Made(GdrQueryBlockStatus.Skipped, null, "not run (--no-run)");
        // A run line the block parser already refused has a code; running it would only say the same again.
        if (source.Diagnostics.FirstOrDefault(d => d.Kind == GuideBlockDiagnosticKind.InvalidRun) is { } invalid)
            return Made(GdrQueryBlockStatus.Refused, invalid.RefusalCode ?? RefusalCodes.GuideQueryRunInvalid, invalid.Message);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        using var cancellation = new CancellationTokenSource();
        var work = Task.Run(() => Evaluate(query, projectPath, parserPath, cancellation.Token, corpusEvaluator), CancellationToken.None);
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
            return Made(GdrQueryBlockStatus.Failed, null, $"timed out after {timeout.TotalSeconds:0.#} s (--timeout)", ms: elapsed);
        }
        var outcome = work.Result;
        return Made(outcome.Status, outcome.Code, outcome.Message, outcome.Rows, outcome.Complete, elapsed, outcome.Build);
    }

    private sealed record Outcome(string Status, string? Code, string Message, long? Rows = null, bool? Complete = null, string? Build = null);

    private static Outcome Evaluate(GuideQueryDefinition query, string projectPath, string? parserPath, CancellationToken cancellationToken,
        Func<string, string, CancellationToken, GuideEvidenceResult>? corpusEvaluator)
    {
        if (!QueryRunLine.TryParseRun(query.Run, out var line, out var refusal))
            return new Outcome(GdrQueryBlockStatus.Refused, refusal.Code, refusal.Message);
        if (line.Corpus is not null)
        {
            var evidence = (corpusEvaluator?.Invoke(query.Run, BaseDirectory(projectPath), cancellationToken) ??
                    GuideEvidenceRunner.Evaluate(query.Run, BaseDirectory(projectPath), parserPath, cancellationToken, projectPath))
                .Shaped(query.Columns, query.Sort, null);
            return evidence.Status switch
            {
                GuideEvidenceStatus.Ready => new Outcome(GdrQueryBlockStatus.Ok, null, evidence.Message, evidence.Table!.Types, evidence.Table.Complete),
                GuideEvidenceStatus.ColdIndex => new Outcome(GdrQueryBlockStatus.NotBuilt, evidence.RefusalCode, evidence.Message, Build: evidence.BuildCommand),
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
        var outcome = FormsQueryCommand.Run(new FormsQueryRequest(projectPath, MotifProductVersion.CurrentText, based.Query.Record), parserPath: parserPath);
        if (!outcome.Succeeded || outcome.Value is not { } response)
            return new Outcome(GdrQueryBlockStatus.Refused, outcome.Refusal?.Code, outcome.Refusal?.Message ?? "the query gave no answer.");
        var rows = response.Sides?.Rows.Count ?? (response.Result.Groups.Count > 0 ? response.Result.Groups.Count : response.Result.Rows.Count);
        return new Outcome(GdrQueryBlockStatus.Ok, null, $"{rows} row(s)", rows, true);
    }

    private static string BaseDirectory(string projectPath) =>
        Directory.Exists(projectPath) ? projectPath : Path.GetDirectoryName(Path.GetFullPath(projectPath)) ?? ".";

    private static string Place(string gdr, string? section) => string.IsNullOrEmpty(section) ? gdr : $"{gdr}, {section}";
}
