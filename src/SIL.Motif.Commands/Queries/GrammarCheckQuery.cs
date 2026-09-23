using SIL.Motif.Host;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using SIL.LCModel;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Parser;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Projects;

namespace SIL.Motif.Commands.Queries;

/// <summary>Reads a project's grammar-health report from its current Baseline.</summary>
public static class GrammarCheckQuery
{
    /// <summary>Checks the project's current Baseline grammar with the installed PanGloss executable.</summary>
    public static CommandOutcome<GrammarCheckResponse> Query(
        GrammarCheckRequest request, CancellationToken cancellationToken = default)
    {
        using var invoker = new PanGlossInvoker();
        return Query(request, invoker, cancellationToken, ParserStamp());
    }

    /// <summary>The file beside a Baseline's scratch copy that holds its last grammar check.</summary>
    public const string CacheFileName = "grammar-check.json";

    /// <summary>Checks through an explicitly supplied invoker, which allows tests to stand in for PanGloss.</summary>
    /// <param name="parserStamp">Identifies the parser build, or disables caching when null.</param>
    internal static CommandOutcome<GrammarCheckResponse> Query(
        GrammarCheckRequest request,
        IPanGlossInvoker invoker,
        CancellationToken cancellationToken,
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
            var stamp = parserStamp is null ? null
                : "grammar-health-v2|" + baseline.Token.BundleDigest + "|" + parserStamp;
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

            GrammarHealthReportJson report;
            try
            {
                report = ReadReport(completed.Output);
            }
            catch (JsonException exception)
            {
                return CommandOutcome<GrammarCheckResponse>.Refused(new Refusal(
                    "grammarcheck.malformed-findings", FailureReason.Refused,
                    $"pangloss grammar-health wrote a report Motif could not read: {exception.Message}",
                    Fact(("projectPath", request.ProjectPath))));
            }

            using var cache = new FwDataProjectLoader().LoadScratchCache(baseline.FwDataPath);
            var projectName = Path.GetFileNameWithoutExtension(request.ProjectPath);
            var warningLines = completed.StandardError.Split('\n').Select(line => line.Trim())
                .Where(line => line.StartsWith("warning:", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("capability:", StringComparison.OrdinalIgnoreCase)).ToArray();
            var loadWarnings = GrammarWarningReader.Read(cache, projectName, warningLines);
            var findings = report.Diagnostics
                .Select(finding => ToGrammarWarning(finding, report.Summary))
                .ToArray();
            var response = new GrammarCheckResponse([.. loadWarnings, .. findings], HasBaseline: true)
            {
                Summary = report.Summary.Select(row => new GrammarWarningSummary(
                    row.Code!, row.GroupName, row.Level!, row.Count!.Value)).ToArray(),
            };
            if (stamp is not null) WriteCache(cachePath, stamp, response);
            return CommandOutcome<GrammarCheckResponse>.Success(response);
        });
    }

    private sealed record CachedCheck(string Stamp, GrammarCheckResponse Response);

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

    private static string? ParserStamp()
    {
        if (PanGlossExecutable.TryLocate() is not { } exe || !File.Exists(exe)) return null;
        var info = new FileInfo(exe);
        return string.Create(CultureInfo.InvariantCulture, $"{info.FullName}|{info.Length}|{info.LastWriteTimeUtc:O}");
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private static GrammarHealthReportJson ReadReport(string output)
    {
        using var document = JsonDocument.Parse(output);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("schema_version", out var version) ||
            !version.TryGetInt32(out var schemaVersion) || schemaVersion != 2)
            throw new JsonException("Expected a grammar-health report with schema_version 2.");
        if (!root.TryGetProperty("fieldworks_project", out var project) || project.ValueKind != JsonValueKind.Object)
            throw new JsonException("Missing fieldworks_project object.");
        if (!HasTextOrNull(project, "name") || !HasTextOrNull(project, "source"))
            throw new JsonException("The fieldworks_project fields are incomplete.");
        if (!root.TryGetProperty("summary", out var summary) || summary.ValueKind != JsonValueKind.Array)
            throw new JsonException("Missing summary array.");
        if (!root.TryGetProperty("diagnostics", out var diagnostics) || diagnostics.ValueKind != JsonValueKind.Array)
            throw new JsonException("Missing diagnostics array.");

        var report = root.Deserialize<GrammarHealthReportJson>(JsonOptions)
            ?? throw new JsonException("Missing grammar-health report.");
        if (report.SchemaVersion != 2 || report.FieldWorksProject is null || report.Summary is null ||
            report.Diagnostics is null)
            throw new JsonException("The grammar-health report is incomplete.");
        var summaryRows = summary.EnumerateArray().ToArray();
        if (summaryRows.Length != report.Summary.Count)
            throw new JsonException("The summary rows could not be read.");
        for (var index = 0; index < report.Summary.Count; index++)
        {
            var row = report.Summary[index];
            var wireRow = summaryRows[index];
            if (string.IsNullOrWhiteSpace(row.Code) || string.IsNullOrWhiteSpace(row.GroupName) ||
                !IsLevel(row.Level) || row.Count is null ||
                !HasText(wireRow, "code") || !HasText(wireRow, "group_name") ||
                !HasText(wireRow, "level") || !wireRow.TryGetProperty("count", out var count) ||
                !count.TryGetInt32(out _))
                throw new JsonException("A summary row is incomplete or has an unsupported level.");
        }
        var diagnosticRows = diagnostics.EnumerateArray().ToArray();
        if (diagnosticRows.Length != report.Diagnostics.Count)
            throw new JsonException("The diagnostics rows could not be read.");
        for (var index = 0; index < report.Diagnostics.Count; index++)
        {
            var diagnostic = report.Diagnostics[index];
            var wireDiagnostic = diagnosticRows[index];
            if (!IsLevel(diagnostic.Level) || string.IsNullOrWhiteSpace(diagnostic.Code) ||
                string.IsNullOrWhiteSpace(diagnostic.GroupName) || !IsOrigin(diagnostic.Origin) ||
                string.IsNullOrWhiteSpace(diagnostic.Description) || diagnostic.Subjects is null ||
                !HasText(wireDiagnostic, "level") || !HasText(wireDiagnostic, "code") ||
                !HasText(wireDiagnostic, "group_name") || !HasText(wireDiagnostic, "origin") ||
                !HasText(wireDiagnostic, "description") ||
                !wireDiagnostic.TryGetProperty("guidance", out var guidance) ||
                guidance.ValueKind is not (JsonValueKind.String or JsonValueKind.Null) ||
                !wireDiagnostic.TryGetProperty("subjects", out var subjects) ||
                subjects.ValueKind != JsonValueKind.Array)
                throw new JsonException("A diagnostic is incomplete or has an unsupported level or origin.");
            var subjectRows = subjects.EnumerateArray().ToArray();
            if (subjectRows.Length != diagnostic.Subjects.Count)
                throw new JsonException("The diagnostic subjects could not be read.");
            for (var subjectIndex = 0; subjectIndex < diagnostic.Subjects.Count; subjectIndex++)
            {
                var subject = diagnostic.Subjects[subjectIndex];
                var wireSubject = subjectRows[subjectIndex];
                if (string.IsNullOrWhiteSpace(subject.Kind) || string.IsNullOrWhiteSpace(subject.Title) ||
                    subject.FieldWorks is null || !IsLinkState(subject.FieldWorks.Status) ||
                    !HasText(wireSubject, "kind") || !HasText(wireSubject, "title") ||
                    !HasTextOrNull(wireSubject, "subtitle") || !HasTextOrNull(wireSubject, "guid") ||
                    !HasTextOrNull(wireSubject, "internal_id") ||
                    !wireSubject.TryGetProperty("fieldworks", out var fieldworks) ||
                    fieldworks.ValueKind != JsonValueKind.Object || !HasText(fieldworks, "status"))
                    throw new JsonException("A diagnostic subject is incomplete or has an unsupported link state.");
                if (subject.OpensIn is not null &&
                    (!wireSubject.TryGetProperty("opens_in", out var opensIn) ||
                     opensIn.ValueKind != JsonValueKind.Object || !HasText(opensIn, "tool") ||
                     !HasText(opensIn, "guid")))
                    throw new JsonException("A diagnostic subject has an invalid open target.");
                if (subject.FieldWorks.Status == "available" &&
                    (!HasText(fieldworks, "guid") || !HasText(fieldworks, "tool") || !HasText(fieldworks, "url")))
                    throw new JsonException("An available FieldWorks link is incomplete.");
                if (subject.FieldWorks.Status == "unavailable" && !HasText(fieldworks, "reason"))
                    throw new JsonException("An unavailable FieldWorks link has no reason.");
            }
        }
        return report;
    }

    private static bool HasText(JsonElement value, string property) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(property, out var field) &&
        field.ValueKind == JsonValueKind.String;

    private static bool HasTextOrNull(JsonElement value, string property) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(property, out var field) &&
        field.ValueKind is JsonValueKind.String or JsonValueKind.Null;

    private static bool IsLevel(string? level) => level is "warning" or "info";
    private static bool IsOrigin(string? origin) => origin is "check" or "import";
    private static bool IsLinkState(string? status) => status is "available" or "unavailable";

    private static GrammarWarning ToGrammarWarning(
        GrammarHealthDiagnosticJson finding,
        IReadOnlyList<GrammarHealthSummaryJson> summary)
    {
        var subjects = finding.Subjects!.Select(SubjectPart).ToArray();
        var description = finding.Description!;
        var problem = new[] { new GrammarWarningPart(description, "text") };
        var text = $"{finding.Level}: {finding.Code}: {description}";
        var group = summary.FirstOrDefault(row => row.Code == finding.Code)?.GroupName ?? finding.GroupName;
        return new GrammarWarning(finding.Level!, ReadableCode(finding.Code), subjects, problem, text)
        {
            Group = group,
            Code = finding.Code,
            Description = description,
            Guidance = finding.Guidance,
            Origin = finding.Origin!,
            Audience = finding.Audience ?? "linguist",
        };
    }

    private static GrammarWarningPart SubjectPart(GrammarHealthSubjectJson part)
    {
        var title = part.Title!;
        var text = part.Subtitle is { Length: > 0 } subtitle ? $"{title} ({subtitle})" : title;
        var fieldWorks = part.FieldWorks!;
        var objectId = part.Guid ?? part.InternalId;
        var openTarget = part.OpensIn;
        return new GrammarWarningPart(
            text,
            objectId is null ? "text" : "object",
            objectId,
            part.Kind,
            fieldWorks.Status == "available" ? fieldWorks.Url : null)
        {
            Title = title,
            Subtitle = part.Subtitle,
            SubjectGuid = part.Guid,
            InternalId = part.InternalId,
            FieldWorksGuid = fieldWorks.Guid,
            LinkStatus = fieldWorks.Status,
            LinkReason = fieldWorks.Reason,
            FieldWorksTool = fieldWorks.Tool,
            OpenTargetTool = openTarget?.Tool,
            OpenTargetGuid = openTarget?.Guid,
        };
    }

    private static string ReadableCode(string? code)
    {
        if (string.IsNullOrEmpty(code)) return string.Empty;
        var body = code.StartsWith("hc-", StringComparison.Ordinal) ? code[3..] : code;
        var words = body.Split('-', StringSplitOptions.RemoveEmptyEntries);
        return words.Length == 0 ? code
            : char.ToUpperInvariant(words[0][0]) + words[0][1..] +
              (words.Length > 1 ? " " + string.Join(' ', words.Skip(1)) : string.Empty);
    }

    private sealed record GrammarHealthReportJson(
        [property: JsonPropertyName("schema_version")] int? SchemaVersion,
        [property: JsonPropertyName("fieldworks_project")] FieldWorksProjectJson? FieldWorksProject,
        IReadOnlyList<GrammarHealthSummaryJson>? Summary,
        IReadOnlyList<GrammarHealthDiagnosticJson>? Diagnostics);

    private sealed record FieldWorksProjectJson(string? Name, string? Source);

    private sealed record GrammarHealthSummaryJson(
        string? Code,
        [property: JsonPropertyName("group_name")] string? GroupName,
        string? Level,
        int? Count);

    private sealed record GrammarHealthDiagnosticJson(
        string? Level,
        string? Code,
        [property: JsonPropertyName("group_name")] string? GroupName,
        string? Origin,
        string? Description,
        string? Guidance,
        IReadOnlyList<GrammarHealthSubjectJson>? Subjects,
        string? Audience = null);

    private sealed record GrammarHealthSubjectJson(
        string? Kind,
        string? Title,
        string? Subtitle,
        string? Guid,
        [property: JsonPropertyName("internal_id")] string? InternalId,
        [property: JsonPropertyName("opens_in")] GrammarHealthOpenTargetJson? OpensIn,
        [property: JsonPropertyName("fieldworks")] GrammarHealthLinkJson? FieldWorks);

    private sealed record GrammarHealthOpenTargetJson(string? Tool, string? Guid);

    private sealed record GrammarHealthLinkJson(
        string? Status, string? Reason, string? Guid, string? Tool, string? Url);

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
