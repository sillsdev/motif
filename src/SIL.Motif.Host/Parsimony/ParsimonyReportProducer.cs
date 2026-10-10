using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Assess;

namespace SIL.Motif.Host.Parsimony;

/// <summary>Produces the stored Parsimony report from its frozen grammar facts and Motif evidence files.</summary>
public sealed class ParsimonyReportProducer : IReportProducer
{
    /// <summary>The report registry key for the Parsimony report.</summary>
    public const string KindName = "parsimony";

    /// <inheritdoc />
    public string Kind => KindName;

    /// <inheritdoc />
    public string Description => "Advisory findings from captured Baseline or candidate grammar evidence.";

    /// <inheritdoc />
    public RenderedReport Produce(ReportInput input, ReportQuery query, IAssessorCatalog assessors)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(assessors);
        if (input is not ReportInput.Parsimony parsimony)
            throw new ReportRefusalException(KindName,
                "A Parsimony report needs a frozen Baseline evidence bundle, not an Assessment input.");
        if (parsimony.GrammarFactsPath is null || parsimony.EvidencePath is null)
            throw new ReportRefusalException(KindName,
                "A Parsimony report needs the closed grammar-facts and evidence files from its bundle.");
        using var session = new ParsimonyQuerySession(parsimony.GrammarFactsPath, parsimony.EvidencePath,
            parsimony.Inputs);
        var joinQuality = session.ReadJoinQuality(parsimony.Inputs.EvidenceScope);
        var result = MeasureRunner.Execute(parsimony.MeasureId, session, parsimony.Inputs.BundleId);
        var findings = result.Findings;
        var dispositions = ParsimonyDispositionQuery.Project(session, findings, reportId: null);
        var familyDescriptions = parsimony.MeasureId == "P-allo-alternation-family" && findings.Count > 0
            ? session.ReadAlternationFamilies().Families.ToDictionary(item => item.Key, item => item.Description,
                StringComparer.Ordinal)
            : new Dictionary<string, string>(StringComparer.Ordinal);
        var text = new System.Text.StringBuilder();
        text.AppendLine("Parsimony advisory report");
        text.AppendLine($"{(parsimony.Inputs.InputKind == "candidate" ? "Candidate" : "Baseline")} bundle: " +
            parsimony.Inputs.BundleId);
        text.AppendLine($"Evidence scope: {joinQuality.Scope}" +
            (joinQuality.ScopeAvailable ? string.Empty : " (unavailable)"));
        text.AppendLine($"Project denominators: {joinQuality.ProjectWordforms} wordforms, " +
            $"{joinQuality.ProjectApprovedReadings} Approved readings, " +
            $"{joinQuality.ProjectDisapprovedReadings} Disapproved readings, " +
            $"{joinQuality.ProjectTextOccurrences} Text occurrences.");
        if (joinQuality.ScopeAvailable)
        {
            text.AppendLine($"Scope denominators: {joinQuality.ScopeForms} forms, " +
                $"{joinQuality.ScopeWordforms} wordforms, {joinQuality.ScopeApprovedReadings} Approved readings, " +
                $"{joinQuality.ScopeLexemes} lexemes" +
                (joinQuality.ScopeTextOccurrences is { } occurrences ? $", {occurrences} Text occurrences." : "."));
        }
        // A check that looked and found nothing says nothing; a check that could not look says so as a note.
        var note = ParsimonyNotes.NotChecked(result);
        if (note is not null)
            text.AppendLine(note.Text);
        // Authored gates the compiler ignored are information only: they never count, rank or take a disposition.
        var noEffect = session.ReadNoEffectNotes();
        foreach (var item in noEffect)
            text.AppendLine(item.Text);
        var notes = new List<ParsimonyNote>();
        if (note is not null) notes.Add(note);
        notes.AddRange(noEffect);
        // A note replaces the status line unless the measure computed findings, whose count is still shown.
        var showStatus = note is null
            ? result.Run.Status != ParsimonyMeasureStatus.Computed || result.Run.FindingItems != 0
            : result.Run.Status == ParsimonyMeasureStatus.Computed && result.Run.FindingItems != 0;
        if (showStatus)
        {
            text.AppendLine($"{parsimony.MeasureId}: {result.Run.Status}; " +
                (result.Run.Number is { } number
                    ? $"{number.Numerator}/{number.Denominator} {number.Unit}."
                    : result.Run.Detail ?? "Required facts are unavailable."));
            if (result.Run.Detail is not null && result.Run.Number is not null)
                text.AppendLine($"  Detail: {result.Run.Detail}");
        }
        if (dispositions.ActiveCount + dispositions.SuppressedCount + dispositions.ResurfacedCount +
            dispositions.UnresolvedJudgmentCount > 0)
            text.AppendLine($"Presentation: {dispositions.ActiveCount} Active, {dispositions.SuppressedCount} Suppressed, " +
                $"{dispositions.ResurfacedCount} resurfaced, {dispositions.UnresolvedJudgmentCount} unresolved judgment(s).");
        foreach (var presentation in dispositions.Findings.Where(item => item.State is "active" or "resurfaced"))
        {
            var finding = presentation.Finding;
            text.AppendLine($"  {Caption(session, finding, familyDescriptions)}: " +
                $"{finding.Number.Numerator}/{finding.Number.Denominator} {finding.Number.Unit}.");
            if (presentation.State == "resurfaced")
            {
                text.AppendLine($"    Previously {presentation.Disposition}; relevant evidence changed from " +
                    $"{presentation.PreviousEvidenceDigest}.");
                if (presentation.Reason is not null) text.AppendLine($"    Previous reason: {presentation.Reason}");
            }
            var items = finding.EvidenceRefs.Select(reference => ObjectGuid(reference.Arguments))
                .OfType<string>().Select(session.DescribeObject).OfType<string>().ToArray();
            if (items.Length > 0)
                text.AppendLine($"    Items: {string.Join(", ", items.Select(item => $"\"{item}\""))}.");
            text.AppendLine($"    Identity: {finding.AttachesTo.Identity}");
            foreach (var limitation in finding.Limitations)
                text.AppendLine($"    Limit: {limitation}");
        }
        return new RenderedReport(KindName, text.ToString())
        {
            ParsimonyFindings = [.. findings.Select(finding => finding with
            {
                ItemNames = ItemNamesFor(finding, session.DescribeObject),
            })],
            ParsimonyMeasureRuns = [result.Run],
            ParsimonyJoinQuality = joinQuality,
            ParsimonyDispositionProjection = dispositions,
            ParsimonyNotes = notes,
        };
    }

    private static string Caption(ParsimonyQuerySession session, ParsimonyFinding finding,
        IReadOnlyDictionary<string, string> familyDescriptions)
    {
        if (finding.GroupKey is { } groupKey && familyDescriptions.TryGetValue(groupKey, out var description))
            return description;
        return DescribeFinding(session, finding);
    }

    /// <summary>
    /// Names a finding's subject in words: the kind segment that precedes the first identity segment the facts
    /// can describe, then those words, such as <c>environment "unused"</c>. Falls back to the raw identity.
    /// </summary>
    internal static string DescribeFinding(ParsimonyQuerySession session, ParsimonyFinding finding)
    {
        foreach (var owner in new[] { finding.GroupKey, finding.AttachesTo.Identity }.OfType<string>())
        {
            var parts = owner.Split('/');
            for (var index = 0; index < parts.Length; index++)
            {
                if (!Guid.TryParse(parts[index], out _))
                    continue;
                var words = finding.AttachesTo.AuthoredObjectKind == ParsimonyAuthoredObjectKind.InflectionalMsa
                    ? session.DescribeMsa(parts[index])
                    : session.DescribeObject(parts[index]);
                if (words is null) continue;
                return index > 0 ? $"{parts[index - 1]} \"{words}\"" : $"\"{words}\"";
            }
        }
        return finding.AttachesTo.Identity;
    }

    /// <summary>
    /// The names of the items a finding concerns, as the facts name them. An item the facts cannot name is left out rather
    /// than shown by its identity, so an empty list means the Report names none.
    /// </summary>
    internal static IReadOnlyList<string> ItemNamesFor(ParsimonyFinding finding, Func<string, string?> describeObject)
    {
        var names = finding.EvidenceRefs.Select(reference => ObjectGuid(reference.Arguments)).OfType<string>()
            .Select(describeObject).OfType<string>().Distinct(StringComparer.Ordinal).ToList();
        if (names.Count == 0 && Guid.TryParse(finding.AttachesTo.Identity, out _) &&
            describeObject(finding.AttachesTo.Identity) is { } attached)
            names.Add(attached);
        return names;
    }

    private static string? ObjectGuid(System.Text.Json.JsonElement arguments) =>
        arguments.ValueKind == System.Text.Json.JsonValueKind.Object &&
        arguments.TryGetProperty("objectGuid", out var value) && value.ValueKind == System.Text.Json.JsonValueKind.String
            ? value.GetString()
            : null;
}
