using System.Text.Json;
using SIL.Motif.Contract.HumanJudgments;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Projection.HumanJudgments;

namespace SIL.Motif.Host.Parsimony;

/// <summary>Joins a frozen Report's candidates to the judgment revisions captured with its Baseline.</summary>
public static class ParsimonyDispositionQuery
{
    /// <summary>Partitions candidates and keeps every unresolved or historical judgment visible.</summary>
    public static ParsimonyDispositionProjection Project(string bundleId, string? reportId,
        string? baselineBundleDigest, IReadOnlyList<ParsimonyFinding> findings,
        HumanJudgmentLineageProjection judgments, bool reportAvailable = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bundleId);
        ArgumentNullException.ThrowIfNull(findings);
        ArgumentNullException.ThrowIfNull(judgments);

        var revisions = judgments.Revisions.ToDictionary(item => item.RevisionId, StringComparer.Ordinal);
        var heads = judgments.Heads.ToDictionary(item => item.RevisionId, StringComparer.Ordinal);
        var effective = judgments.Heads.Where(item => item.State == "effective")
            .Select(item => revisions.GetValueOrDefault(item.RevisionId)).OfType<HumanJudgmentRevisionProjection>()
            .ToArray();
        var unresolved = judgments.Unavailable.Select(item => new ParsimonyUnresolvedJudgmentViewRow(
            item.RecordId, item.JudgmentId, "unavailable", item.Reason, item.PhysicalDigest)).ToList();
        foreach (var head in judgments.Heads.Where(item => item.State is "conflict" or "unavailable"))
        {
            if (!revisions.TryGetValue(head.RevisionId, out var revision) ||
                revision.Judgment.Body is not DispositionJudgment) continue;
            unresolved.Add(new(head.RecordId, head.JudgmentId, head.State,
                head.Issue ?? "The judgment head could not be resolved.", Hash(revision.PhysicalValue)));
        }
        if (judgments.Capability != "Available")
            unresolved.Add(new("project:" + judgments.ProjectId, null, "unavailable",
                judgments.CapabilityMessage, Hash(judgments.Capability + "\0" + judgments.CapabilityMessage)));

        var output = new List<ParsimonyFindingDispositionViewRow>();
        foreach (var finding in findings.OrderBy(item => item.FindingId, StringComparer.Ordinal))
        {
            var related = effective.Where(item => item.Judgment.Body is DispositionJudgment disposition &&
                disposition.MeasureId == finding.MeasureId &&
                SubjectMatches(disposition.Subject, finding, judgments.ProjectId)).ToArray();
            var exact = related.Where(item => item.Judgment.Body is DispositionJudgment disposition &&
                SameEvidenceDigest(disposition.EvidenceDigest, finding.EvidenceDigest)).ToArray();
            var unresolvedRelated = judgments.Heads.Where(item => item.State is "conflict" or "unavailable")
                .Where(item => revisions.TryGetValue(item.RevisionId, out var revision) &&
                    revision.Judgment.Body is DispositionJudgment disposition &&
                    disposition.MeasureId == finding.MeasureId &&
                    SubjectMatches(disposition.Subject, finding, judgments.ProjectId) &&
                    SameEvidenceDigest(disposition.EvidenceDigest, finding.EvidenceDigest))
                .ToArray();

            if (reportAvailable && unresolvedRelated.Length == 0 && exact.Length == 1 &&
                exact[0].Judgment.Body is DispositionJudgment { Disposition: ParsimonyDispositionKind.Keep or
                    ParsimonyDispositionKind.Defer } suppressing)
            {
                output.Add(Row(reportId, bundleId, "suppressed", finding, suppressing, exact[0], null));
                continue;
            }

            if (reportAvailable && unresolvedRelated.Length == 0 && exact.Length == 1 &&
                exact[0].Judgment.Body is DispositionJudgment activeDecision)
            {
                output.Add(Row(reportId, bundleId, "active", finding, activeDecision, exact[0], null));
                continue;
            }

            if (unresolvedRelated.Length > 0 || exact.Length > 1)
            {
                var issue = exact.Length > 1
                    ? "More than one effective judgment names this exact finding."
                    : "A conflicting or unavailable judgment names this finding.";
                output.Add(new(reportId, bundleId, "active", finding, null, null, null, null, null, null,
                    "unresolved", issue, DispositionHeads(exact, unresolvedRelated, revisions)));
                continue;
            }

            var prior = reportAvailable
                ? related.Where(item => item.Judgment.Body is DispositionJudgment
                    { Disposition: ParsimonyDispositionKind.Keep or ParsimonyDispositionKind.Defer } disposition &&
                    !SameEvidenceDigest(disposition.EvidenceDigest, finding.EvidenceDigest))
                    .OrderBy(item => item.Judgment.JudgmentId, StringComparer.Ordinal)
                    .ThenBy(item => item.RevisionId, StringComparer.Ordinal).ToArray()
                : [];
            var previous = prior.Length == 1 && prior[0].Judgment.Body is DispositionJudgment priorDecision
                ? priorDecision : null;
            output.Add(new(reportId, bundleId, previous is null ? "active" : "resurfaced", finding,
                previous?.Disposition.ToString().ToLowerInvariant(), previous is null ? null : prior[0].Judgment.Reason,
                previous?.Question, previous is null ? null : prior[0].Judgment.JudgmentId,
                previous is null ? null : prior[0].RevisionId,
                previous is null ? null : previous.EvidenceDigest,
                previous is null ? null : "evidence-changed", null,
                DispositionHeads(related, [], revisions)));
        }

        var activeRows = output.Count(item => item.State is "active" or "resurfaced");
        var suppressedRows = output.Count(item => item.State == "suppressed");
        var resurfacedRows = output.Count(item => item.State == "resurfaced");
        var history = BuildHistory(bundleId, reportId, baselineBundleDigest, findings, judgments, revisions, heads,
            reportAvailable);
        var unresolvedRows = unresolved.DistinctBy(item => (item.RecordId, item.JudgmentId, item.Reason))
            .OrderBy(item => item.JudgmentId, StringComparer.Ordinal).ThenBy(item => item.RecordId, StringComparer.Ordinal)
            .ToArray();
        var unresolvedCount = unresolvedRows.Select(item => item.JudgmentId ?? item.RecordId)
            .Distinct(StringComparer.Ordinal).Count();
        return new ParsimonyDispositionProjection(bundleId, reportId, baselineBundleDigest, judgments.ProjectId,
            judgments.Digest, activeRows, suppressedRows, resurfacedRows, unresolvedCount,
            Array.AsReadOnly(output.ToArray()), Array.AsReadOnly(history), Array.AsReadOnly(unresolvedRows));
    }

    /// <summary>Projects current artifact rows through the same partition used by stored Reports.</summary>
    public static ParsimonyDispositionProjection Project(ParsimonyQuerySession session,
        IReadOnlyList<ParsimonyFinding> findings, string? reportId, bool reportAvailable = true)
    {
        ArgumentNullException.ThrowIfNull(session);
        return Project(session.BundleId, reportId, session.BaselineToken.BundleDigest, findings,
            session.ReadHumanJudgments(), reportAvailable);
    }

    private static ParsimonySuppressionHistoryViewRow[] BuildHistory(string bundleId, string? reportId,
        string? baselineBundleDigest, IReadOnlyList<ParsimonyFinding> findings,
        HumanJudgmentLineageProjection judgments,
        IReadOnlyDictionary<string, HumanJudgmentRevisionProjection> revisions,
        IReadOnlyDictionary<string, HumanJudgmentHeadProjection> heads, bool reportAvailable)
    {
        var rows = new List<ParsimonySuppressionHistoryViewRow>();
        foreach (var revision in judgments.Revisions)
        {
            if (revision.Judgment.Body is not DispositionJudgment disposition ||
                disposition.Disposition is not (ParsimonyDispositionKind.Keep or ParsimonyDispositionKind.Defer)) continue;

            var head = heads.GetValueOrDefault(revision.RevisionId);
            var current = revisions.Values.FirstOrDefault(item => item.Judgment.JudgmentId == revision.JudgmentId &&
                heads.GetValueOrDefault(item.RevisionId)?.State == "effective");
            var matchingSubject = findings.Where(finding => finding.MeasureId == disposition.MeasureId &&
                SubjectMatches(disposition.Subject, finding, judgments.ProjectId)).ToArray();
            var exact = matchingSubject.Any(finding => SameEvidenceDigest(disposition.EvidenceDigest, finding.EvidenceDigest));
            var changed = matchingSubject.Any(finding => !SameEvidenceDigest(disposition.EvidenceDigest, finding.EvidenceDigest));
            string state;
            if (head?.State == "conflict") state = "conflict";
            else if (head?.State == "unavailable") state = "unavailable";
            else if (current?.Judgment.Body is RetractionJudgment) state = "retracted";
            else if (revision.State == "superseded") state = "superseded";
            else if (!reportAvailable) state = "evidence-unavailable";
            else if (exact) state = "suppressed";
            else if (changed) state = "resurfaced";
            else state = "no-current-finding";

            var subjectKey = HumanJudgmentCodec.SubjectKey(disposition.Subject);
            var judgment = revision.Judgment;
            rows.Add(new(judgment.JudgmentId, revision.RevisionId, revision.RecordId, revision.ContentDigest,
                disposition.MeasureId,
                subjectKey, disposition.SubjectCaption, disposition.MeasureCaption, disposition.EvidenceDigest,
                disposition.Disposition.ToString().ToLowerInvariant(), judgment.Reason, state,
                judgment.Source?.ReportId, reportAvailable ? reportId : null,
                reportAvailable ? baselineBundleDigest : null, judgments.ProjectId,
                judgment.Source?.ProposalId, judgment.Actor?.Kind.ToString().ToLowerInvariant(),
                judgment.Actor?.Id, judgment.Actor?.Name, judgment.JudgedAtUtc?.ToString("O")));
        }
        return rows.OrderBy(item => item.JudgmentId, StringComparer.Ordinal)
            .ThenBy(item => item.RevisionId, StringComparer.Ordinal).ToArray();
    }

    private static ParsimonyFindingDispositionViewRow Row(string? reportId, string bundleId, string state,
        ParsimonyFinding finding,
        DispositionJudgment disposition, HumanJudgmentRevisionProjection revision, string? issue) =>
        new(reportId, bundleId, state, finding, disposition.Disposition.ToString().ToLowerInvariant(),
            revision.Judgment.Reason, disposition.Question, revision.Judgment.JudgmentId,
            revision.RevisionId, null, null, issue,
            [new(revision.Judgment.JudgmentId, revision.RevisionId, revision.ContentDigest, "effective")]);

    private static ParsimonyDispositionHeadView[] DispositionHeads(
        IEnumerable<HumanJudgmentRevisionProjection> effective,
        IEnumerable<HumanJudgmentHeadProjection> unresolved,
        IReadOnlyDictionary<string, HumanJudgmentRevisionProjection> revisions) =>
        effective.Select(item => new ParsimonyDispositionHeadView(item.JudgmentId, item.RevisionId,
                item.ContentDigest, "effective"))
            .Concat(unresolved.Select(item => revisions.TryGetValue(item.RevisionId, out var revision)
                ? new ParsimonyDispositionHeadView(revision.JudgmentId, revision.RevisionId,
                    revision.ContentDigest, item.State)
                : null).OfType<ParsimonyDispositionHeadView>())
            .DistinctBy(item => (item.JudgmentId, item.RevisionId))
            .OrderBy(item => item.JudgmentId, StringComparer.Ordinal)
            .ThenBy(item => item.RevisionId, StringComparer.Ordinal).ToArray();

    private static bool SubjectMatches(HumanJudgmentSubject subject, ParsimonyFinding finding, string projectId)
    {
        var attachment = finding.AttachesTo;
        switch (subject)
        {
            case ProjectJudgmentSubject project:
                return attachment.Kind == ParsimonyAttachmentKind.Project &&
                    SameIdentity(project.Id, attachment.Identity) && SameIdentity(project.Id, projectId);
            case ParsimonyFindingJudgmentSubject reportedFinding:
                return attachment.Kind == ParsimonyAttachmentKind.WordCase &&
                    reportedFinding.MeasureId == finding.MeasureId &&
                    reportedFinding.FindingId == finding.FindingId;
            case ObjectJudgmentSubject item:
                if (attachment.Kind == ParsimonyAttachmentKind.AuthoredObject &&
                    item.Object.ClassMatches(attachment.AuthoredObjectKind) &&
                    SameIdentity(item.Object.Id, attachment.Identity)) return true;
                return attachment.Kind == ParsimonyAttachmentKind.Group &&
                    attachment.GroupKind == ParsimonyGroupKind.UnusedStatement &&
                    item.Object.Class is "PhEnvironment" or "PhNCSegments" or "PhNCFeatures" &&
                    SameIdentity(item.Object.Id, LastIdentity(attachment.Identity));
            case GroupJudgmentSubject group:
                if (group.MeasureId != finding.MeasureId || attachment.Kind != ParsimonyAttachmentKind.Group ||
                    !GroupRoleMatches(group.Role, attachment.GroupKind)) return false;
                var referenced = ReferencedObjectIds(finding, attachment.GroupKind);
                if (referenced.Count == 0) referenced.Add(LastIdentity(attachment.Identity));
                var members = group.Members.Select(member => NormalizeIdentity(member.Id)).Order(StringComparer.Ordinal).ToArray();
                return members.Length == referenced.Count && members.SequenceEqual(referenced.Order(StringComparer.Ordinal)) &&
                    group.Members.All(member => GroupMemberClassMatches(member.Class, attachment.GroupKind));
            default:
                return false;
        }
    }

    private static HashSet<string> ReferencedObjectIds(ParsimonyFinding finding, ParsimonyGroupKind? groupKind)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        var evidenceView = groupKind switch
        {
            ParsimonyGroupKind.AlternationFamily or ParsimonyGroupKind.AllomorphDuplicateForms =>
                "allomorph-context",
            ParsimonyGroupKind.AdhocSlotOrder => "adhoc-context",
            _ => null,
        };
        foreach (var reference in finding.EvidenceRefs.Where(item => evidenceView is null || item.View == evidenceView))
        {
            if (reference.Arguments.ValueKind != JsonValueKind.Object ||
                !reference.Arguments.TryGetProperty("objectGuid", out var value) ||
                value.ValueKind != JsonValueKind.String || value.GetString() is not { } identity) continue;
            result.Add(NormalizeIdentity(identity));
        }
        return result;
    }

    private static bool GroupRoleMatches(JudgmentGroupRole role, ParsimonyGroupKind? kind) => (role, kind) switch
    {
        (JudgmentGroupRole.AlternationFamily, ParsimonyGroupKind.AlternationFamily) => true,
        (JudgmentGroupRole.DuplicatePair, ParsimonyGroupKind.AllomorphDuplicateForms) => true,
        (JudgmentGroupRole.AdhocSlotOrder, ParsimonyGroupKind.AdhocSlotOrder) => true,
        (JudgmentGroupRole.AdhocCluster, ParsimonyGroupKind.UnusedStatement) => true,
        _ => false,
    };

    private static bool GroupMemberClassMatches(string className, ParsimonyGroupKind? kind) => kind switch
    {
        ParsimonyGroupKind.AlternationFamily or ParsimonyGroupKind.AllomorphDuplicateForms =>
            className is "MoStemAllomorph" or "MoAffixAllomorph" or "MoAffixProcess",
        ParsimonyGroupKind.AdhocSlotOrder =>
            className is "MoAlloAdhocProhib" or "MoMorphAdhocProhib" or "MoAdhocProhibGr",
        ParsimonyGroupKind.UnusedStatement =>
            className is "PhEnvironment" or "PhNCSegments" or "PhNCFeatures",
        _ => false,
    };

    private static bool ObjectClassMatches(string className, ParsimonyAuthoredObjectKind? kind) => kind switch
    {
        ParsimonyAuthoredObjectKind.AdhocProhibition =>
            className is "MoAlloAdhocProhib" or "MoMorphAdhocProhib" or "MoAdhocProhibGr",
        ParsimonyAuthoredObjectKind.InflectionalMsa => className == "MoInflAffMsa",
        ParsimonyAuthoredObjectKind.AffixTemplate => className == "MoInflAffixTemplate",
        ParsimonyAuthoredObjectKind.AffixSlot => className == "MoInflAffixSlot",
        ParsimonyAuthoredObjectKind.Allomorph =>
            className is "MoStemAllomorph" or "MoAffixAllomorph" or "MoAffixProcess",
        ParsimonyAuthoredObjectKind.NaturalClass => className is "PhNCSegments" or "PhNCFeatures",
        ParsimonyAuthoredObjectKind.Environment => className == "PhEnvironment",
        _ => false,
    };

    private static bool SameEvidenceDigest(string first, string second) =>
        StripDigestPrefix(first) == StripDigestPrefix(second);

    private static string StripDigestPrefix(string value) => value.StartsWith("sha256:", StringComparison.Ordinal)
        ? value[7..] : value;

    private static bool SameIdentity(string first, string second) => NormalizeIdentity(first) == NormalizeIdentity(second);

    private static string LastIdentity(string value)
    {
        var segment = value.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? value;
        return NormalizeIdentity(segment);
    }

    private static string NormalizeIdentity(string value)
    {
        if (Guid.TryParse(value, out var guid)) return CanonicalId.FromGuid(guid).Value;
        return CanonicalId.TryParse(value, out var id) ? CanonicalId.FromGuid(id.ToGuid()).Value : value;
    }

    private static string Hash(string value) => Convert.ToHexStringLower(
        System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)));
}

internal static class JudgmentObjectClassExtensions
{
    public static bool ClassMatches(this JudgmentObject value, ParsimonyAuthoredObjectKind? kind) =>
        value.Class switch
        {
            "MoAlloAdhocProhib" or "MoMorphAdhocProhib" or "MoAdhocProhibGr" =>
                kind == ParsimonyAuthoredObjectKind.AdhocProhibition,
            "MoInflAffMsa" => kind == ParsimonyAuthoredObjectKind.InflectionalMsa,
            "MoInflAffixTemplate" => kind == ParsimonyAuthoredObjectKind.AffixTemplate,
            "MoInflAffixSlot" => kind == ParsimonyAuthoredObjectKind.AffixSlot,
            "MoStemAllomorph" or "MoAffixAllomorph" => kind == ParsimonyAuthoredObjectKind.Allomorph,
            "PhNCSegments" or "PhNCFeatures" => kind == ParsimonyAuthoredObjectKind.NaturalClass,
            "PhEnvironment" => kind == ParsimonyAuthoredObjectKind.Environment,
            _ => false,
        };
}
