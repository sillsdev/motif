using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Gdr;
using SIL.Motif.Contract.Query;
using SIL.Motif.Contract.Query.Corpus;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Commands.Queries;

/// <summary>What one Guide block's evidence run came to.</summary>
public enum GuideEvidenceStatus
{
    /// <summary>The query ran; <see cref="GuideEvidenceResult.Table"/> holds the answer.</summary>
    Ready,
    /// <summary>The query was refused (a bad line, an unknown id, an unreadable corpus); <see cref="GuideEvidenceResult.Message"/> carries the code and reason. Never shown as an empty table.</summary>
    Refused,
    /// <summary>The query needs a parse index that is not built; nothing was built. <see cref="GuideEvidenceResult.BuildCommand"/> builds it.</summary>
    ColdIndex,
}

/// <summary>The outcome of <see cref="GuideEvidenceRunner"/>: a table, or a message that says why there is none.</summary>
/// <param name="Status">Which of the three it is.</param>
/// <param name="Table">The answer, for <see cref="GuideEvidenceStatus.Ready"/>.</param>
/// <param name="Message">What the block says: the headline, the refusal code and message, or "parse index not built: run ...".</param>
/// <param name="RefusalCode">The refusal's code, for a refusal or a cold index.</param>
/// <param name="BuildCommand">For a cold index, the exact shell line that builds it.</param>
/// <param name="Milliseconds">The whole run.</param>
/// <param name="Phases">Milliseconds per phase: corpus, project, index-load or index-build, query.</param>
/// <param name="Request">The request that ran, with paths resolved.</param>
/// <param name="Response">The raw answer.</param>
/// <param name="BaselineId">The project's current Baseline (its bundle digest) when the caller gave a project with a store and one is recorded; null otherwise. It names the Baseline the project had when the run was made; the corpus and grammar files are read as they are.</param>
public sealed record GuideEvidenceResult(GuideEvidenceStatus Status, GuideEvidenceTable? Table, string Message, string? RefusalCode, string? BuildCommand,
    double Milliseconds, IReadOnlyDictionary<string, double> Phases, CorpusFormsRequest? Request = null, CorpusFormsResponse? Response = null,
    string? BaselineId = null)
{
    /// <summary>
    /// The same ready answer shaped with other columns, sort and limit, so several <c>motif-show</c> directives of one query share one run;
    /// any other result is returned as it is. A column the query lacks gives a refused result, never a crash.
    /// </summary>
    public GuideEvidenceResult Shaped(IReadOnlyList<string>? columns, GuideSort? sort, int? limit)
    {
        if (Status != GuideEvidenceStatus.Ready || Request is null || Response is null) return this;
        var unknown = GuideEvidenceTable.Unknown(Request, [.. columns ?? [], .. sort is null ? [] : new[] { sort.Column }]);
        if (unknown.Count > 0)
            return this with
            {
                Status = GuideEvidenceStatus.Refused, Table = null, RefusalCode = RefusalCodes.GuideQueryRunInvalid,
                Message = $"unknown column {string.Join(", ", unknown)}; this query has {string.Join(", ", GuideEvidenceTable.ColumnsFor(Request))}.",
            };
        var table = GuideEvidenceTable.Build(Request, Response, columns, sort, limit);
        return this with { Table = table, Message = table.Headline };
    }
}

/// <summary>
/// Runs the <c>run</c> line of a Guide <c>motif-query</c> block (a v2 <c>motif query corpus forms</c> line) through the same read-only
/// corpus evaluator the CLI uses, and shapes the answer for the block. It never writes, and it builds a parse index only when asked (the Guide check's --build): otherwise a cold
/// index comes back as <see cref="GuideEvidenceStatus.ColdIndex"/> with the command that builds it (the CLI line itself builds, so
/// running the run line in a terminal warms it). Relative paths in the line resolve against <c>baseDirectory</c>, the folder of the project.
/// </summary>
public static class GuideEvidenceRunner
{
    /// <summary>Runs one block with the columns, sort and limit of its definition and its <c>motif-show</c>, when it has one.</summary>
    public static GuideEvidenceResult Run(GuideQueryDefinition query, GuideShowDirective? show, string baseDirectory, string? parserPath = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        return Run(query.Run, show?.Columns is { Count: > 0 } ? show.Columns : query.Columns, query.Sort, show?.Limit, baseDirectory, parserPath, cancellationToken);
    }

    /// <summary>Runs a run line and shapes its answer.</summary>
    public static GuideEvidenceResult Run(string runLine, IReadOnlyList<string>? columns, GuideSort? sort, int? limit, string baseDirectory,
        string? parserPath = null, CancellationToken cancellationToken = default) =>
        Evaluate(runLine, baseDirectory, parserPath, cancellationToken).Shaped(columns, sort, limit);

    /// <summary>Runs a run line once, shaped with the default columns; shape the result for each display with <see cref="GuideEvidenceResult.Shaped"/>.</summary>
    public static GuideEvidenceResult Evaluate(string runLine, string baseDirectory, string? parserPath = null, CancellationToken cancellationToken = default,
        string? projectPath = null, bool buildIndex = false)
    {
        var result = Evaluate(runLine, baseDirectory, parserPath, cancellationToken, null, null, buildIndex);
        return projectPath is null ? result : result with { BaselineId = BaselineIdOf(projectPath) };
    }

    /// <summary>The project's current Baseline id (the bundle digest of its recorded Baseline token), or null when it has none; read-only.</summary>
    public static string? BaselineIdOf(string projectPath) =>
        CurrentBaselineQuery.Query(new CurrentBaselineRequest(projectPath)) is { Succeeded: true, Value: { } baseline } ? baseline.Token?.BundleDigest : null;

    internal static GuideEvidenceResult Evaluate(string runLine, string baseDirectory,
        string? parserPath, CancellationToken cancellationToken, IRealizesIndexBuilder? indexBuilder, string? cacheRoot, bool allowIndexBuild = false)
    {
        ArgumentNullException.ThrowIfNull(runLine);
        ArgumentNullException.ThrowIfNull(baseDirectory);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var phases = new Dictionary<string, double>(StringComparer.Ordinal);
        GuideEvidenceResult Done(GuideEvidenceStatus status, GuideEvidenceTable? table, string message, string? code, string? command) =>
            new(status, table, message, code, command, Math.Round(clock.Elapsed.TotalMilliseconds, 1), phases);
        if (!QueryRunLine.TryParseRun(runLine, out var line, out var refusal)) return Done(GuideEvidenceStatus.Refused, null, Describe(refusal), refusal.Code, null);
        if (line.Corpus is not { } parsed)
            return Done(GuideEvidenceStatus.Refused, null, RefusalCodes.GuideQueryRunInvalid + ": this evidence runner reads motif query corpus forms lines.", RefusalCodes.GuideQueryRunInvalid, null);
        if (!CorpusRunLine.TryResolve(parsed, baseDirectory, out var request, out var outside)) return Done(GuideEvidenceStatus.Refused, null, Describe(outside), outside.Code, null);
        if (request.GroupBy is null && request.Sum is null) request = request with { Sum = "tokens" };
        CommandOutcome<CorpusFormsResponse> outcome;
        try
        {
            outcome = CorpusQueryCommand.Forms(request, parserPath, cancellationToken, indexBuilder, cacheRoot, allowIndexBuild: allowIndexBuild, phases: phases);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            return Done(GuideEvidenceStatus.Refused, null, RefusalCodes.GuideQueryRunInvalid + ": " + exception.GetType().Name + ": " + exception.Message, RefusalCodes.GuideQueryRunInvalid, null);
        }
        if (!outcome.Succeeded || outcome.Value is not { } response)
        {
            var failed = outcome.Refusal;
            if (failed?.Code == RefusalCodes.QueryParseIndexCold)
            {
                var command = BuildCommand(runLine, baseDirectory);
                return Done(GuideEvidenceStatus.ColdIndex, null, $"parse index not built: run {command}", failed.Code, command);
            }
            return failed is null ? Done(GuideEvidenceStatus.Refused, null, RefusalCodes.GuideQueryRunInvalid + ": the query gave no answer.", RefusalCodes.GuideQueryRunInvalid, null)
                : Done(GuideEvidenceStatus.Refused, null, Describe(failed), failed.Code, null);
        }
        return (Done(GuideEvidenceStatus.Ready, null, "", null, null) with { Request = request, Response = response }).Shaped(null, null, null);
    }

    /// <summary>
    /// The line that builds the parse index: the parsed run line with every token single-quoted, run from the project's folder, so
    /// pasting it into a shell cannot run anything the Guide's text hid in a value.
    /// </summary>
    public static string BuildCommand(string runLine, string baseDirectory)
    {
        if (!QueryRunLine.TryTokenize(runLine, out var tokens, out _)) tokens = [];
        return "cd " + Quote(baseDirectory) + (tokens.Count == 0 ? "" : " && " + string.Join(" ", tokens.Select((token, at) => at == 0 ? token : Quote(token))));
    }

    private static string Quote(string value) => "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";

    private static string Describe(Refusal refusal) => refusal.Code + ": " + refusal.Message;
}
