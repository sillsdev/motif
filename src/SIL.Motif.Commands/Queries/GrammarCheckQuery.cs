using SIL.Motif.Host;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Parser;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Projects;

namespace SIL.Motif.Commands.Queries;

/// <summary>
/// Checks a project's grammar as a whole through <c>pangloss grammar-health</c>, read from the project's
/// current Baseline scratch copy: the warnings every parser run can print while loading the grammar, plus
/// the ported HermitCrab grammar-authoring findings. Never involves a Text, a word, or an Assessment.
/// </summary>
/// <remarks>
/// A check reads the whole project twice — once in the parser, once through LibLCM to name what the findings
/// refer to — and so takes tens of seconds on a real project. Its answer depends only on the Baseline and
/// the parser, so it is kept beside the Baseline in <see cref="CacheFileName"/>, stamped with both, and a
/// later check of the same Baseline by the same parser answers from it at once.
/// </remarks>
public static class GrammarCheckQuery
{
    /// <summary>Checks the project's current Baseline grammar, resolving the parser the real installation uses.</summary>
    public static CommandOutcome<GrammarCheckResponse> Query(
        GrammarCheckRequest request, CancellationToken cancellationToken = default)
    {
        using var invoker = new PanGlossInvoker();
        return Query(request, invoker, cancellationToken, ParserStamp());
    }

    /// <summary>The file beside a Baseline's scratch copy that holds its last grammar check.</summary>
    public const string CacheFileName = "grammar-check.json";

    /// <summary>
    /// Checks through an explicitly supplied invoker — a fake stands in for PanGloss in tests. Admission and
    /// containment are the invoker's, so this query holds no queue and no governor.
    /// </summary>
    /// <param name="parserStamp">
    /// Names the parser build, so a cached answer from another build is not reused; <see langword="null"/>
    /// neither reads nor writes the cache.
    /// </param>
    internal static CommandOutcome<GrammarCheckResponse> Query(
        GrammarCheckRequest request, IPanGlossInvoker invoker, CancellationToken cancellationToken,
        string? parserStamp = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(invoker);

        return ProjectStoreCommand.Run(request.ProjectPath, ResolveProductVersion(), (database, project) =>
        {
            var workspaceKey = ProjectWorkspaceKey.Compute(project);
            var baseline = new BaselineRepository(database).GetCurrent(workspaceKey);
            if (baseline is null)
                return CommandOutcome<GrammarCheckResponse>.Success(
                    new GrammarCheckResponse(Array.Empty<GrammarWarning>(), HasBaseline: false));

            var cachePath = Path.Combine(Path.GetDirectoryName(baseline.FwDataPath)!, CacheFileName);
            var stamp = parserStamp is null ? null : baseline.Token.BundleDigest + "|" + parserStamp;
            if (stamp is not null && ReadCache(cachePath, stamp) is { } cached)
                return CommandOutcome<GrammarCheckResponse>.Success(cached);

            var outcome = invoker.RunAsync(
                    new PanGlossRequest.GrammarHealth(baseline.FwDataPath), "grammar-check:" + workspaceKey,
                    cancellationToken)
                .GetAwaiter().GetResult();
            if (outcome is PanGlossOutcome.Cancelled)
                return CommandOutcome<GrammarCheckResponse>.Refused(Cancelled(request.ProjectPath));
            if (outcome is not PanGlossOutcome.Completed completed)
                return CommandOutcome<GrammarCheckResponse>.Refused(ParserRefusal(outcome, request.ProjectPath));

            IReadOnlyList<GrammarHealthFindingJson> findings;
            try
            {
                findings = JsonSerializer.Deserialize<IReadOnlyList<GrammarHealthFindingJson>>(
                    completed.Output, JsonOptions) ?? throw new JsonException("Missing findings array.");
            }
            catch (JsonException exception)
            {
                return CommandOutcome<GrammarCheckResponse>.Refused(new Refusal(
                    "grammarcheck.malformed-findings", FailureReason.Refused,
                    $"pangloss grammar-health wrote findings Motif could not read: {exception.Message}",
                    Fact(("projectPath", request.ProjectPath))));
            }

            using var cache = new FwDataProjectLoader().LoadScratchCache(baseline.FwDataPath);
            var projectName = Path.GetFileNameWithoutExtension(request.ProjectPath);

            var warningLines = completed.StandardError.Split('\n').Select(line => line.Trim())
                .Where(line => line.StartsWith("warning:", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("capability:", StringComparison.OrdinalIgnoreCase)).ToArray();
            var loadWarnings = GrammarWarningReader.Read(cache, projectName, warningLines);
            var healthFindings = findings.Select(ToGrammarWarning).ToArray();

            var response = new GrammarCheckResponse([.. loadWarnings, .. healthFindings], HasBaseline: true);
            if (stamp is not null) WriteCache(cachePath, stamp, response);
            return CommandOutcome<GrammarCheckResponse>.Success(response);
        });
    }

    private sealed record CachedCheck(string Stamp, GrammarCheckResponse Response);

    // An unreadable or differently stamped cache is simply not used; the check runs again.
    private static GrammarCheckResponse? ReadCache(string path, string stamp)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var cached = JsonSerializer.Deserialize<CachedCheck>(File.ReadAllText(path));
            return cached is not null && cached.Stamp == stamp ? cached.Response : null;
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // A cache that cannot be written only costs the next check its speed, never its answer.
    private static void WriteCache(string path, string stamp, GrammarCheckResponse response)
    {
        try
        {
            File.WriteAllText(path, JsonSerializer.Serialize(new CachedCheck(stamp, response)));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    // The resolved executable's path, size and write time: a rebuilt or replaced parser changes it.
    private static string? ParserStamp()
    {
        if (PanGlossExecutable.TryLocate() is not { } exe || !File.Exists(exe)) return null;
        var info = new FileInfo(exe);
        return string.Create(CultureInfo.InvariantCulture, $"{info.FullName}|{info.Length}|{info.LastWriteTimeUtc:O}");
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    // Subjects carry pg-grammar's own internal indices, never a FieldWorks GUID, so they stay plain text.
    private static GrammarWarning ToGrammarWarning(GrammarHealthFindingJson finding)
    {
        var subject = (finding.Subjects ?? Array.Empty<GrammarHealthSubjectJson>())
            .Where(part => !string.IsNullOrEmpty(part.Name))
            .Select(part => new GrammarWarningPart(part.Name!, "text"))
            .ToArray();
        var problem = new[] { new GrammarWarningPart(finding.Message ?? string.Empty, "text") };
        var text = $"{finding.Severity}: {finding.Code}: {finding.Message}";
        return new GrammarWarning(finding.Severity ?? string.Empty, ReadableCode(finding.Code), subject, problem, text);
    }

    // "hc-undeclared-segment" -> "Undeclared segment": humanises PanGloss's stable wire code for display.
    private static string ReadableCode(string? code)
    {
        if (string.IsNullOrEmpty(code)) return string.Empty;
        var body = code.StartsWith("hc-", StringComparison.Ordinal) ? code[3..] : code;
        var words = body.Split('-', StringSplitOptions.RemoveEmptyEntries);
        return words.Length == 0 ? code
            : char.ToUpperInvariant(words[0][0]) + words[0][1..] +
              (words.Length > 1 ? " " + string.Join(' ', words.Skip(1)) : string.Empty);
    }

    private sealed record GrammarHealthFindingJson(
        string? Severity, string? Code, string? Message, IReadOnlyList<GrammarHealthSubjectJson>? Subjects);

    private sealed record GrammarHealthSubjectJson(string? Kind, string? Name);

    private static Refusal Cancelled(string projectPath) => new(
        "grammarcheck.cancelled", FailureReason.Cancelled, "The grammar check was cancelled.",
        Fact(("projectPath", projectPath)));

    private static Refusal ParserRefusal(PanGlossOutcome outcome, string projectPath) => outcome switch
    {
        PanGlossOutcome.Unavailable unavailable => new Refusal(
            "grammarcheck.parser-unavailable", FailureReason.Refused, unavailable.Message,
            Fact(("projectPath", projectPath))),
        PanGlossOutcome.TimedOut timedOut => new Refusal(
            "grammarcheck.timed-out", FailureReason.Refused, timedOut.Message,
            Fact(("projectPath", projectPath))),
        PanGlossOutcome.Refused refused => new Refusal(
            "grammarcheck.parser-refused", FailureReason.Refused, refused.Message,
            Fact(("projectPath", projectPath), ("exitCode", refused.ExitCode.ToString(CultureInfo.InvariantCulture)))),
        _ => new Refusal(
            "grammarcheck.parser-unavailable", FailureReason.Refused, outcome.Message,
            Fact(("projectPath", projectPath))),
    };

    private static Dictionary<string, string> Fact(params (string Key, string Value)[] facts)
    {
        var dictionary = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in facts) dictionary[key] = value;
        return dictionary;
    }

    private static string ResolveProductVersion() => MotifProductVersion.CurrentText;
}
