using SIL.Motif.Host;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Commands.Assess;
using SIL.Motif.Commands.Store;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Parser;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Commands.Queries;

/// <summary>
/// Checks a project's grammar as a whole through <c>pangloss grammar-health</c>, reading findings from the
/// project's current Baseline. Never involves a Text, a word, or an Assessment.
/// </summary>
/// <remarks>The report carries its own subject names and FieldWorks links and is stored with the Baseline.</remarks>
public static class GrammarCheckQuery
{
    /// <summary>Checks the project's current Baseline grammar with the installed PanGloss executable.</summary>
    public static CommandOutcome<GrammarCheckResponse> Query(
        GrammarCheckRequest request, CancellationToken cancellationToken = default) =>
        Query(request, PanGlossExecutable.TryLocate(), cancellationToken);

    /// <summary>Checks the project's current Baseline grammar with an explicitly selected parser.</summary>
    /// <param name="parserPath">The parser to run, or <see langword="null"/> when none is available.</param>
    public static CommandOutcome<GrammarCheckResponse> Query(
        GrammarCheckRequest request, string? parserPath, CancellationToken cancellationToken = default)
    {
        using var invoker = new PanGlossInvoker(parserPath);
        return Query(request, invoker, cancellationToken, ParserStamp(parserPath));
    }

    /// <summary>Checks through an explicitly supplied invoker, which allows tests to stand in for PanGloss.</summary>
    /// <param name="parserStamp">Identifies the parser build recorded with the findings.</param>
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

            var outcome = invoker.RunAsync(
                    new PanGlossRequest.GrammarHealth(baseline.FwDataPath, project.FieldWorksProjectIdentity),
                    "grammar-check:" + workspaceKey,
                    cancellationToken)
                .GetAwaiter().GetResult();
            if (outcome is PanGlossOutcome.Cancelled)
                return CommandOutcome<GrammarCheckResponse>.Refused(Cancelled(request.ProjectPath));
            var parserExitedNonzero = outcome is PanGlossOutcome.Refused;
            var output = outcome switch
            {
                PanGlossOutcome.Completed completed => completed.Output,
                PanGlossOutcome.Refused refused when !string.IsNullOrWhiteSpace(refused.StandardOutput) =>
                    refused.StandardOutput,
                _ => null,
            };
            if (output is null)
                return CommandOutcome<GrammarCheckResponse>.Refused(ParserRefusal(outcome, request.ProjectPath));

            GrammarWarning[] findings;
            GrammarWarningSummary[] summary;
            try
            {
                var report = ReadReport(output);
                findings = (report.Diagnostics ?? throw new JsonException(
                        "The grammar-health report is incomplete."))
                    .Select(diagnostic => ToGrammarWarning(diagnostic ?? throw new JsonException(
                        "A diagnostic is incomplete.")))
                    .ToArray();
                summary = (report.Summary ?? throw new JsonException(
                        "The grammar-health report is incomplete."))
                    .Select(row =>
                    {
                        var item = row ?? throw new JsonException("A summary row is incomplete or invalid.");
                        return new GrammarWarningSummary(
                            item.Code ?? throw new JsonException("A summary row is incomplete or invalid."),
                            item.GroupName, item.Level, item.Count);
                    })
                    .ToArray();
            }
            catch (UnsupportedGrammarHealthSchemaException exception)
            {
                return CommandOutcome<GrammarCheckResponse>.Refused(new Refusal(
                    "grammarcheck.unsupported-schema", FailureReason.Refused,
                    $"PanGloss grammar-health schema version {exception.Version} is unsupported; this Motif build " +
                    "expects versions 2 and 3. Update PanGloss and Motif together.",
                    Fact(("projectPath", request.ProjectPath),
                        ("actualSchemaVersion", exception.Version.ToString(CultureInfo.InvariantCulture)),
                        ("expectedSchemaVersion", "2 or 3"))));
            }
            catch (JsonException exception)
            {
                return CommandOutcome<GrammarCheckResponse>.Refused(new Refusal(
                    "grammarcheck.malformed-findings", FailureReason.Refused,
                    $"pangloss grammar-health wrote a report Motif could not read: {exception.Message}",
                    Fact(("projectPath", request.ProjectPath))));
            }

            if (parserExitedNonzero && !findings.Any(finding => finding.Severity == GrammarDiagnosticLevel.Error))
                return CommandOutcome<GrammarCheckResponse>.Refused(ParserRefusal(outcome, request.ProjectPath));

            var response = new GrammarCheckResponse(findings, HasBaseline: true)
            {
                Summary = summary,
            };
            var baselineToken = JsonSerializer.Serialize(baseline.Token, MotifJson.CreateOptions());
            var selectionSha256 = SelectionDigest(database, baseline.FwDataPath, baselineToken);
            new GrammarCheckRepository(database).Save(baselineToken, selectionSha256, parserStamp, response);
            return CommandOutcome<GrammarCheckResponse>.Success(response);
        });
    }

    private static string SelectionDigest(SIL.Motif.Host.Store.MotifDatabase database, string fwDataPath,
        string baselineToken)
    {
        var saved = new NamedSelectionRepository(database).GetDefault();
        if (saved is null) return string.Empty;
        using var cache = new FwDataProjectLoader().LoadScratchCache(fwDataPath);
        var request = new SelectionRequest(false, saved.TextIds, saved.AddedWords, false, null);
        var composed = SelectionComposer.Compose(cache, request, new AssessmentRepository(database), baselineToken);
        return composed.Succeeded ? composed.Value!.Selection.Sha256 : string.Empty;
    }

    private static string? ParserStamp(string? parserPath)
    {
        if (parserPath is not { } exe || !File.Exists(exe)) return null;
        // Path, size, and write time distinguish a rebuilt or replaced parser executable.
        var info = new FileInfo(exe);
        return string.Create(CultureInfo.InvariantCulture, $"{info.FullName}|{info.Length}|{info.LastWriteTimeUtc:O}");
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private static GrammarHealthReportJson ReadReport(string output)
    {
        using var document = JsonDocument.Parse(output);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("schema_version", out var version) ||
            !version.TryGetInt32(out var schemaVersion))
            throw new JsonException("The grammar-health report must be an object with an integer schema_version.");
        if (schemaVersion is not (2 or 3)) throw new UnsupportedGrammarHealthSchemaException(schemaVersion);
        var report = root.Deserialize<GrammarHealthReportJson>(JsonOptions)
            ?? throw new JsonException("Missing grammar-health report.");
        var fieldWorksProject = report.FieldWorksProject
            ?? throw new JsonException("The grammar-health report is incomplete.");
        var summary = report.Summary
            ?? throw new JsonException("The grammar-health report is incomplete.");
        var diagnostics = report.Diagnostics
            ?? throw new JsonException("The grammar-health report is incomplete.");
        if ((fieldWorksProject.Name is { } name && string.IsNullOrWhiteSpace(name)) ||
            (fieldWorksProject.Name is null) != (fieldWorksProject.Source is null))
            throw new JsonException("The FieldWorks project fields are invalid.");
        foreach (var row in summary)
        {
            if (row is null || string.IsNullOrWhiteSpace(row.Code) ||
                string.IsNullOrWhiteSpace(row.GroupName) || row.Count < 0)
                throw new JsonException("A summary row is incomplete or invalid.");
        }
        foreach (var diagnostic in diagnostics)
        {
            if (diagnostic is null || string.IsNullOrWhiteSpace(diagnostic.Code) ||
                string.IsNullOrWhiteSpace(diagnostic.GroupName) ||
                string.IsNullOrWhiteSpace(diagnostic.Description) || diagnostic.Subjects is null)
                throw new JsonException("A diagnostic is incomplete.");
            foreach (var subject in diagnostic.Subjects)
            {
                if (subject is null || string.IsNullOrWhiteSpace(subject.Kind) ||
                    string.IsNullOrWhiteSpace(subject.Title) ||
                    subject.FieldWorks is null)
                    throw new JsonException("A diagnostic subject is incomplete.");
                if (subject.OpensIn is not null && (string.IsNullOrWhiteSpace(subject.OpensIn.Tool) ||
                    string.IsNullOrWhiteSpace(subject.OpensIn.Guid)))
                    throw new JsonException("A diagnostic subject has an invalid open target.");
                if (subject.FieldWorks.Status == FieldWorksLinkStatus.Available &&
                    (string.IsNullOrWhiteSpace(subject.FieldWorks.Guid) ||
                     string.IsNullOrWhiteSpace(subject.FieldWorks.Tool) ||
                     string.IsNullOrWhiteSpace(subject.FieldWorks.Url)))
                    throw new JsonException("An available FieldWorks link is incomplete.");
                if (subject.FieldWorks.Status == FieldWorksLinkStatus.Unavailable &&
                    subject.FieldWorks.Reason is null)
                    throw new JsonException("An unavailable FieldWorks link has no reason.");
            }
        }
        return report;
    }

    private static GrammarWarning ToGrammarWarning(GrammarHealthDiagnosticJson finding)
    {
        var subjects = finding.Subjects!.Select(subject => SubjectPart(subject!)).ToArray();
        var description = finding.Description!;
        var problem = new[] { new GrammarWarningPart(description, GrammarWarningPartRole.Text) };
        var text = $"{finding.Level.ToWireValue()}: {finding.Code}: {description}";
        return new GrammarWarning(finding.Level, ReadableCode(finding.Code), subjects, problem, text)
        {
            Group = finding.GroupName,
            Code = finding.Code,
            Description = description,
            Guidance = finding.Guidance,
            Origin = finding.Origin,
        };
    }

    private static GrammarWarningPart SubjectPart(GrammarHealthSubjectJson part)
    {
        var title = part.Title!;
        var text = part.Subtitle is { Length: > 0 } subtitle ? $"{title} ({subtitle})" : title;
        var fieldWorks = part.FieldWorks!;
        var objectId = part.Guid ?? part.InternalId;
        return new GrammarWarningPart(
            text,
            objectId is null ? GrammarWarningPartRole.Text : GrammarWarningPartRole.Object,
            objectId,
            part.Kind,
            fieldWorks.Status == FieldWorksLinkStatus.Available ? fieldWorks.Url : null)
        {
            Title = title,
            Subtitle = part.Subtitle,
            SubjectGuid = part.Guid,
            InternalId = part.InternalId,
            FieldWorksGuid = fieldWorks.Guid,
            LinkStatus = fieldWorks.Status,
            LinkReason = fieldWorks.Reason,
            FieldWorksTool = fieldWorks.Tool,
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

    private sealed record GrammarHealthReportJson
    {
        public GrammarHealthReportJson() { }

        [JsonPropertyName("fieldworks_project")]
        public required FieldWorksProjectJson? FieldWorksProject { get; init; }
        public required IReadOnlyList<GrammarHealthSummaryJson?>? Summary { get; init; }
        public required IReadOnlyList<GrammarHealthDiagnosticJson?>? Diagnostics { get; init; }
    }

    private sealed record FieldWorksProjectJson
    {
        public FieldWorksProjectJson() { }

        public required string? Name { get; init; }
        public required GrammarFieldWorksProjectSource? Source { get; init; }
    }

    private sealed record GrammarHealthSummaryJson
    {
        public GrammarHealthSummaryJson() { }

        public required string? Code { get; init; }
        [JsonPropertyName("group_name")]
        public required string? GroupName { get; init; }
        public required GrammarDiagnosticLevel Level { get; init; }
        public required int Count { get; init; }
    }

    private sealed record GrammarHealthDiagnosticJson
    {
        public GrammarHealthDiagnosticJson() { }

        public required GrammarDiagnosticLevel Level { get; init; }
        public required string? Code { get; init; }
        [JsonPropertyName("group_name")]
        public required string? GroupName { get; init; }
        public required GrammarFindingOrigin Origin { get; init; }
        public required string? Description { get; init; }
        public required string? Guidance { get; init; }
        public required IReadOnlyList<GrammarHealthSubjectJson?>? Subjects { get; init; }
    }

    private sealed record GrammarHealthSubjectJson
    {
        public GrammarHealthSubjectJson() { }

        public required string? Kind { get; init; }
        public required string? Title { get; init; }
        public required string? Subtitle { get; init; }
        public required string? Guid { get; init; }
        [JsonPropertyName("internal_id")]
        public required string? InternalId { get; init; }
        [JsonPropertyName("opens_in")]
        public GrammarHealthOpenTargetJson? OpensIn { get; init; }
        [JsonPropertyName("fieldworks")]
        public required GrammarHealthLinkJson? FieldWorks { get; init; }
    }

    private sealed record GrammarHealthOpenTargetJson
    {
        public GrammarHealthOpenTargetJson() { }

        public required string? Tool { get; init; }
        public required string? Guid { get; init; }
    }

    private sealed record GrammarHealthLinkJson
    {
        public GrammarHealthLinkJson() { }

        public required FieldWorksLinkStatus Status { get; init; }
        public FieldWorksLinkReason? Reason { get; init; }
        public required string? Guid { get; init; }
        public string? Tool { get; init; }
        public string? Url { get; init; }
    }

    private sealed class UnsupportedGrammarHealthSchemaException(int version)
        : JsonException($"Unsupported grammar-health schema version {version}.")
    {
        public int Version { get; } = version;
    }

    private static Refusal Cancelled(string projectPath) => new(
        "grammarcheck.cancelled", FailureReason.Cancelled, "The grammar check was cancelled.",
        Fact(("projectPath", projectPath)));

    private static Refusal ParserRefusal(PanGlossOutcome outcome, string projectPath) => outcome switch
    {
        PanGlossOutcome.Unavailable unavailable => new Refusal(
            "grammarcheck.parser-unavailable", FailureReason.Refused, unavailable.Message,
            ParserNotFoundFact.Mark(Fact(("projectPath", projectPath)), unavailable.ExecutableMissing)),
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
