using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Host.PanGloss;

/// <summary>Reads a PanGloss grammar-health report into Motif's stored response shape.</summary>
public static class GrammarHealthReportReader
{
    /// <summary>Validates and converts one grammar-health JSON report.</summary>
    public static GrammarCheckResponse ReadResponse(string output)
    {
        var report = ReadReport(output);
        var locale = report.Locale!;
        var findings = (report.Diagnostics ?? throw new JsonException("The grammar-health report is incomplete."))
            .Select(diagnostic => ToGrammarWarning(diagnostic ?? throw new JsonException("A diagnostic is incomplete."), locale))
            .ToArray();
        var summary = (report.Summary ?? throw new JsonException("The grammar-health report is incomplete."))
            .Select(row =>
            {
                var item = row ?? throw new JsonException("A summary row is incomplete or invalid.");
                return new GrammarWarningSummary(item.Code!, item.GroupName, item.Level, item.Count);
            }).ToArray();
        return new GrammarCheckResponse(findings, HasBaseline: true) { Summary = summary, Locale = locale };
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private static GrammarHealthReportJson ReadReport(string output)
    {
        using var document = JsonDocument.Parse(output);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("schema_version", out var version) ||
            !version.TryGetInt32(out var schemaVersion))
            throw new JsonException("The grammar-health report must be an object with an integer schema_version.");
        if (schemaVersion != PanGlossInterfaceVersions.GrammarHealthSchemaVersion)
            throw new UnsupportedGrammarHealthSchemaException(schemaVersion);
        var report = root.Deserialize<GrammarHealthReportJson>(JsonOptions)
            ?? throw new JsonException("Missing grammar-health report.");
        if (string.IsNullOrWhiteSpace(report.Locale)) throw new JsonException("The report locale is missing.");
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
                string.IsNullOrWhiteSpace(diagnostic.Description) || string.IsNullOrWhiteSpace(diagnostic.Title) ||
                diagnostic.Subjects is null || diagnostic.FieldWorksPlaces is null)
                throw new JsonException("A diagnostic is incomplete.");
            if (diagnostic.HelpPath is { } helpPath &&
                helpPath != $"docs/diagnostics/{diagnostic.Code}.md")
                throw new JsonException("A diagnostic help path is invalid.");
            foreach (var place in diagnostic.FieldWorksPlaces)
                if (place is null || string.IsNullOrWhiteSpace(place.Tool) || string.IsNullOrWhiteSpace(place.Field))
                    throw new JsonException("A FieldWorks place is incomplete.");
            var scope = diagnostic.Subjects.Any(subject => subject?.Status == GrammarSubjectStatus.Object)
                ? GrammarSubjectStatus.Object : diagnostic.Subjects.Any(subject => subject?.Status == GrammarSubjectStatus.UnresolvedReference)
                    ? GrammarSubjectStatus.UnresolvedReference : GrammarSubjectStatus.ProjectSettings;
            if (diagnostic.Scope != scope) throw new JsonException("A diagnostic scope contradicts its subjects.");
            foreach (var subject in diagnostic.Subjects)
            {
                if (subject is null || string.IsNullOrWhiteSpace(subject.Kind) ||
                    string.IsNullOrWhiteSpace(subject.Title) ||
                    subject.FieldWorks is null)
                    throw new JsonException("A diagnostic subject is incomplete.");
                if (subject.Status != GrammarSubjectStatus.Object &&
                    (subject.FieldWorks.Status != FieldWorksLinkStatus.Unavailable || subject.OpensIn is not null ||
                     subject.FieldWorks.Tool is not null || subject.FieldWorks.Url is not null ||
                     subject.FieldWorks.Reason != (subject.Status == GrammarSubjectStatus.UnresolvedReference
                         ? FieldWorksLinkReason.UnresolvedReference : FieldWorksLinkReason.ProjectSettings)))
                    throw new JsonException("An unresolved reference or project setting cannot have live navigation.");
                if (subject.Status == GrammarSubjectStatus.Object &&
                    subject.FieldWorks.Reason is FieldWorksLinkReason.UnresolvedReference or FieldWorksLinkReason.ProjectSettings)
                    throw new JsonException("An object has a non-object navigation reason.");
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

    private const string BudgetReachedGuidance =
        "Nothing to change in FieldWorks. The check compared stored analyses up to a fixed amount of work, so the " +
        "words named above were not compared. They are checked when they are parsed.";

    private static GrammarWarning ToGrammarWarning(GrammarHealthDiagnosticJson finding, string locale)
    {
        var subjects = finding.Subjects!.Select(subject => SubjectPart(subject!)).ToArray();
        var description = finding.Description!;
        var guidance = finding.Guidance;
        if (finding.Code == PanGlossFindingCodes.StoredAnalysisBudgetReached)
        {
            // The parser's own sentence and advice name its command-line flag, which neither front end passes.
            var flag = description.IndexOf(" Run grammar-health", StringComparison.Ordinal);
            if (flag >= 0) description = description[..flag];
            guidance = BudgetReachedGuidance;
        }
        var problem = new[] { new GrammarWarningPart(description, GrammarWarningPartRole.Text) };
        var text = $"{finding.Level.ToWireValue()}: {finding.Code}: {description}";
        return new GrammarWarning(finding.Level, ReadableCode(finding.Code), subjects, problem, text)
        {
            Group = finding.GroupName,
            Code = finding.Code,
            Description = description,
            Guidance = guidance,
            Title = finding.Title,
            Explanation = finding.Explanation,
            HelpPath = finding.HelpPath,
            HelpBody = finding.HelpBody,
            HelpUrl = finding.HelpPath is { } path ? $"https://github.com/sillsdev/PanGloss/blob/{PanGlossInterfaceVersions.SourceTag}/{path}" : null,
            Locale = locale,
            Scope = finding.Scope,
            FieldWorksPlaces = finding.FieldWorksPlaces!.Select(place => place!).ToArray(),
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
            part.Status == GrammarSubjectStatus.UnresolvedReference ? GrammarWarningPartRole.Missing
                : part.Status == GrammarSubjectStatus.ProjectSettings || objectId is null
                    ? GrammarWarningPartRole.Text : GrammarWarningPartRole.Object,
            objectId,
            part.Kind,
            fieldWorks.Status == FieldWorksLinkStatus.Available ? fieldWorks.Url : null)
        {
            Title = title,
            Status = part.Status,
            Field = part.Field,
            SourceClass = part.SourceClass,
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
        public required string? Locale { get; init; }
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
        public required string? Title { get; init; }
        public required string? Explanation { get; init; }
        public required GrammarSubjectStatus Scope { get; init; }
        [JsonPropertyName("help_path")]
        public required string? HelpPath { get; init; }
        [JsonPropertyName("help_body")]
        public required string? HelpBody { get; init; }
        [JsonPropertyName("fieldworks_places")]
        public required IReadOnlyList<GrammarFieldWorksPlace?>? FieldWorksPlaces { get; init; }
        public required IReadOnlyList<GrammarHealthSubjectJson?>? Subjects { get; init; }
    }

    private sealed record GrammarHealthSubjectJson
    {
        public GrammarHealthSubjectJson() { }

        public required string? Kind { get; init; }
        public required GrammarSubjectStatus Status { get; init; }
        public required string? Field { get; init; }
        [JsonPropertyName("source_class")]
        public required string? SourceClass { get; init; }
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

    public sealed class UnsupportedGrammarHealthSchemaException(int version)
        : JsonException($"Unsupported grammar-health schema version {version}.")
    {
        public int Version { get; } = version;
    }

}
