using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Contract.Canonicalization;
using SIL.Motif.Contract.HumanJudgments;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Projection.HumanJudgments;

namespace SIL.Motif.Host.Analysis;

/// <summary>Projects the saved human evidence used by restrictiveness measures and paired verification.</summary>
public static class ParsimonyExpectationProjectionBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Reads effective Notebook negatives and native Opinions from an already-loaded project.</summary>
    public static ParsimonyExpectationProjection Build(LcmCache cache)
    {
        ArgumentNullException.ThrowIfNull(cache);
        var lineage = JudgmentLineageResolver.Resolve(HumanJudgmentReader.Read(cache));
        var revisions = lineage.Revisions.ToDictionary(item => item.RecordId, StringComparer.Ordinal);
        var issues = lineage.Unavailable.Select(item => new ParsimonyExpectationIssue(
            "notebook", item.RecordId, "judgment-unavailable", item.Reason)).ToList();
        issues.AddRange(lineage.Heads.Where(item => item.State == "conflict").Select(item =>
            new ParsimonyExpectationIssue("notebook", item.JudgmentId, "judgment-head-conflict",
                item.Issue ?? "Multiple current revisions share this logical judgment.")));
        if (lineage.Capability != nameof(HumanJudgmentFieldCapability.Available))
            issues.Add(new("notebook", lineage.ProjectId, "judgment-field-unavailable", lineage.CapabilityMessage));
        var negatives = new List<ReviewedNegativeExpectation>();

        foreach (var head in lineage.Heads.Where(item => item.State is "effective" or "conflict"))
        {
            if (!revisions.TryGetValue(head.RecordId, out var revision) ||
                revision.Judgment.Body is not ReviewedNegativeJudgment negative) continue;
            negatives.Add(new ReviewedNegativeExpectation(
                negative.CaseId,
                revision.JudgmentId,
                revision.RevisionId,
                revision.RecordId,
                revision.ContentDigest,
                negative.WritingSystem,
                negative.Form,
                negative.Context,
                negative.Target,
                negative.WordformId,
                negative.AnalysisId,
                revision.Judgment.Reason,
                revision.Judgment.Actor!,
                revision.Judgment.JudgedAtUtc,
                revision.Judgment.Source,
                head.State == "conflict" ? "conflict" : "eligible",
                head.Issue));
        }

        MarkDuplicateCaseConflicts(negatives, issues);
        var approved = ApprovedMorphologyReader.ReadApprovedExpectations(cache);
        var disapproved = ApprovedMorphologyReader.ReadDisapprovedExpectations(cache);
        MarkNativeOpinionConflicts(approved, disapproved, issues);
        MarkApprovedNegativeConflicts(negatives, approved, issues);
        AddNativeIdentityIssues(approved, "approved", issues);
        AddNativeIdentityIssues(disapproved, "disapproved", issues);

        var orderedNegatives = negatives.OrderBy(item => item.CaseId, StringComparer.Ordinal)
            .ThenBy(item => item.RevisionId, StringComparer.Ordinal).ToArray();
        var orderedIssues = issues.Distinct().OrderBy(item => item.SourceKind, StringComparer.Ordinal)
            .ThenBy(item => item.SourceId, StringComparer.Ordinal).ThenBy(item => item.Code, StringComparer.Ordinal)
            .ThenBy(item => item.Detail, StringComparer.Ordinal).ToArray();
        var projectionBody = JsonSerializer.Serialize(new
        {
            contract = ParsimonyExpectationProjection.ContractVersion,
            projectId = lineage.ProjectId,
            reviewedNegatives = orderedNegatives,
            approvedReadings = approved,
            disapprovedReadings = disapproved,
            issues = orderedIssues,
        }, JsonOptions);
        var digest = Hash(CanonicalJson.Canonicalize(projectionBody));
        return new ParsimonyExpectationProjection(ParsimonyExpectationProjection.ContractVersion,
            lineage.ProjectId, digest, Array.AsReadOnly(orderedNegatives), Array.AsReadOnly(approved.ToArray()),
            Array.AsReadOnly(disapproved.ToArray()), Array.AsReadOnly(orderedIssues));
    }

    private static void MarkDuplicateCaseConflicts(
        IList<ReviewedNegativeExpectation> negatives, ICollection<ParsimonyExpectationIssue> issues)
    {
        foreach (var group in negatives.GroupBy(item => item.CaseId, StringComparer.Ordinal))
        {
            var rows = group.ToArray();
            if (rows.Select(CaseIdentity).Distinct(StringComparer.Ordinal).Count() <= 1) continue;
            foreach (var row in rows)
            {
                var index = negatives.IndexOf(row);
                negatives[index] = row with
                {
                    Status = "conflict",
                    Issue = "The same case id names incompatible negative identities.",
                };
            }
            issues.Add(new("reviewed-negative", group.Key, "case-identity-conflict",
                "The same case id names incompatible writing system, form, context or reading identity."));
        }
    }

    private static void MarkApprovedNegativeConflicts(
        IList<ReviewedNegativeExpectation> negatives,
        IReadOnlyList<NativeReadingExpectation> approved,
        ICollection<ParsimonyExpectationIssue> issues)
    {
        for (var index = 0; index < negatives.Count; index++)
        {
            var negative = negatives[index];
            if (negative.Status != "eligible") continue;
            var conflict = approved.Any(reading =>
                reading.UnavailableReason is null &&
                reading.WritingSystem == negative.WritingSystem &&
                reading.Form == negative.Form &&
                (negative.WordformId is null || reading.SourceWordformIds.Contains(negative.WordformId,
                    StringComparer.Ordinal)) &&
                (negative.AnalysisId is not null && reading.SourceAnalysisIds.Contains(negative.AnalysisId,
                    StringComparer.Ordinal) || Matches(negative.Target, reading.Morphs)));
            if (!conflict) continue;
            negatives[index] = negative with
            {
                Status = "conflict",
                Issue = "A native Approved Opinion conflicts with this reviewed negative.",
            };
            issues.Add(new("reviewed-negative", negative.CaseId, "approved-opinion-conflict",
                "A native Approved reading has the same surface or ordered morphology."));
        }
    }

    private static bool Matches(NegativeJudgmentTarget target, IReadOnlyList<ParseMorph> nativeMorphs)
    {
        if (target is SurfaceNegativeTarget) return true;
        if (target is not ReadingNegativeTarget reading || reading.Morphs.Count != nativeMorphs.Count) return false;
        for (var index = 0; index < nativeMorphs.Count; index++)
        {
            var expected = reading.Morphs[index].Identity;
            var actual = nativeMorphs[index];
            if (expected.GuessedString is not null ||
                !SameId(expected.Form, actual.Form) ||
                !SameId(expected.Msa, actual.Msa) ||
                !SameOptionalId(expected.InflType, actual.InflType)) return false;
        }
        return true;
    }

    private static void MarkNativeOpinionConflicts(
        IReadOnlyList<NativeReadingExpectation> approved,
        IReadOnlyList<NativeReadingExpectation> disapproved,
        ICollection<ParsimonyExpectationIssue> issues)
    {
        var approvedKeys = approved.Where(item => item.UnavailableReason is null)
            .Select(NativeSignature).ToHashSet(StringComparer.Ordinal);
        foreach (var row in disapproved.Where(item => item.UnavailableReason is null &&
                     approvedKeys.Contains(NativeSignature(item))))
            issues.Add(new("native-opinion", string.Join(",", row.SourceAnalysisIds), "opposing-opinions",
                "Native Approved and Disapproved Opinions share one ordered morphology and surface."));
    }

    private static void AddNativeIdentityIssues(
        IReadOnlyList<NativeReadingExpectation> readings, string opinion,
        ICollection<ParsimonyExpectationIssue> issues)
    {
        foreach (var row in readings.Where(item => item.UnavailableReason is not null))
            issues.Add(new("native-" + opinion, string.Join(",", row.SourceAnalysisIds),
                "morphology-identity-incomplete", row.UnavailableReason!));
    }

    private static string CaseIdentity(ReviewedNegativeExpectation item) => string.Join('\0',
        item.WritingSystem, item.Form, item.Context,
        item.WordformId is null ? "" : Identity(item.WordformId),
        item.AnalysisId is null ? "" : Identity(item.AnalysisId), TargetIdentity(item.Target));

    private static string TargetIdentity(NegativeJudgmentTarget target) => target switch
    {
        SurfaceNegativeTarget => "surface",
        ReadingNegativeTarget reading => "reading\0" + string.Join('\n', reading.Morphs.Select(morph => string.Join('\0',
            OptionalIdentity(morph.Identity.Form), OptionalIdentity(morph.Identity.Msa),
            OptionalIdentity(morph.Identity.InflType), morph.Identity.GuessedString ?? "",
            morph.GuessedWritingSystem ?? ""))),
        _ => "unknown",
    };

    private static string NativeSignature(NativeReadingExpectation item) => string.Join('\0',
        item.WritingSystem, item.Form, string.Join('\n', item.Morphs.Select(morph => string.Join('\0',
            OptionalIdentity(morph.Form), OptionalIdentity(morph.Msa), OptionalIdentity(morph.InflType)))));

    private static bool SameId(string? left, string? right) =>
        left is not null && right is not null && Identity(left) == Identity(right);

    private static bool SameOptionalId(string? left, string? right) =>
        left is null ? right is null : right is not null && Identity(left) == Identity(right);

    private static string OptionalIdentity(string? value) => value is null ? "" : Identity(value);

    private static string Identity(string value) => CanonicalId.FromGuid(CanonicalId.Parse(value).ToGuid()).Suffix;

    private static string Hash(string value) => "sha256:" + Convert.ToHexStringLower(
        SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
