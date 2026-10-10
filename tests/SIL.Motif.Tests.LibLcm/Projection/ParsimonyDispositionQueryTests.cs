using System.Text.Json;
using SIL.Motif.Contract.HumanJudgments;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Host.Parsimony;
using SIL.Motif.Projection.HumanJudgments;
using Xunit;

namespace SIL.Motif.Tests.LibLcm.Projection;

public sealed class ParsimonyDispositionQueryTests
{
    private static readonly string ProjectId = Id();

    [Theory]
    [InlineData(ParsimonyDispositionKind.Keep, "suppressed")]
    [InlineData(ParsimonyDispositionKind.Defer, "suppressed")]
    [InlineData(ParsimonyDispositionKind.Fix, "active")]
    [InlineData(ParsimonyDispositionKind.Ask, "active")]
    public void EffectiveDispositionPartitionsOnlyKeepAndDefer(ParsimonyDispositionKind kind, string state)
    {
        var judgment = Disposition([], kind, reason: null, evidence: FindingDigest);

        var result = Query([judgment], Finding());

        var row = Assert.Single(result.Findings);
        Assert.Equal(state, row.State);
        Assert.Equal(HumanJudgmentCodec.LogicalDigest(judgment), Assert.Single(row.CurrentHeads!).ContentDigest);
        Assert.Equal(kind is ParsimonyDispositionKind.Keep or ParsimonyDispositionKind.Defer ? 1 : 0,
            result.SuppressedCount);
        Assert.Null(Assert.Single(result.Findings).Reason);
    }

    [Fact]
    public void DuplicateAllomorphGroupMatchesAffixProcessMembers()
    {
        var first = Guid.Parse("04490809-320d-47c7-9efa-4633fdbe95de");
        var second = Guid.Parse("f125c7bd-edc3-4282-aa43-c1f48f026caa");
        var measureId = "P-allo-duplicate-form";
        var finding = new ParsimonyFinding(
            measureId + ":duplicate",
            measureId,
            ParsimonyAxis.Parsimony,
            ParsimonyTier.Static,
            new ParsimonyFindingAttachment(ParsimonyAttachmentKind.Group,
                "entry/00000000-0000-0000-0000-000000000001/duplicate-forms/example",
                GroupKind: ParsimonyGroupKind.AllomorphDuplicateForms),
            "entry/00000000-0000-0000-0000-000000000001",
            new ParsimonyMeasureNumber(1, 2, "allomorphs"),
            new ParsimonyMeasureThreshold(ParsimonyThresholdOperator.RankedOnly, null, "1"),
            FindingDigest,
            [EvidenceReference(first), EvidenceReference(second)],
            "P-test",
            [],
            ParsimonyVerification.NotRun);
        var group = new GroupJudgmentSubject(measureId, JudgmentGroupRole.DuplicatePair,
        [
            new JudgmentObject("MoAffixProcess", CanonicalId.FromGuid(first).Value),
            new JudgmentObject("MoAffixAllomorph", CanonicalId.FromGuid(second).Value),
        ]);
        var judgment = NewJudgment(Id(), [], new DispositionJudgment(group, measureId,
            ParsimonyDispositionKind.Keep, FindingDigest, "contract-v1", "duplicate forms", "Duplicate forms"),
            "reviewed duplicate pair");

        var result = Query([judgment], finding);

        Assert.Equal("suppressed", Assert.Single(result.Findings).State);
        Assert.Equal("reviewed duplicate pair", Assert.Single(result.Findings).Reason);
        Assert.Equal(1, result.SuppressedCount);
        Assert.Equal("suppressed", Assert.Single(result.History).State);
    }

    [Fact]
    public void RelevantEvidenceChangeResurfacesThePriorReason()
    {
        var old = Disposition([], ParsimonyDispositionKind.Keep, "reviewed before", evidence: "sha256:" + new string('1', 64));
        var candidate = Finding("sha256:" + new string('2', 64));

        var result = Query([old], candidate);

        var row = Assert.Single(result.Findings);
        Assert.Equal("resurfaced", row.State);
        Assert.Equal("reviewed before", row.Reason);
        Assert.Equal(old.RevisionId, row.RevisionId);
        Assert.Equal(1, result.ResurfacedCount);
        Assert.Equal("resurfaced", Assert.Single(result.History).State);
    }

    [Fact]
    public void ReasonOnlyRevisionAndUnrelatedEvidenceLeaveExactKeepSuppressed()
    {
        var old = Disposition([], ParsimonyDispositionKind.Keep, "original reason", evidence: FindingDigest);
        var edited = Disposition([Predecessor(old)], ParsimonyDispositionKind.Keep, "updated reason",
            old.JudgmentId, FindingDigest);

        var result = Query([old, edited], Finding());

        var row = Assert.Single(result.Findings);
        Assert.Equal("suppressed", row.State);
        Assert.Equal("updated reason", row.Reason);
        Assert.Equal(1, result.SuppressedCount);
    }

    [Fact]
    public void ConflictingHeadsStayVisibleAndCannotSuppress()
    {
        var root = Disposition([], ParsimonyDispositionKind.Keep, "first", evidence: FindingDigest);
        var first = Disposition([Predecessor(root)], ParsimonyDispositionKind.Keep, "left", root.JudgmentId,
            FindingDigest);
        var second = Disposition([Predecessor(root)], ParsimonyDispositionKind.Defer, "right", root.JudgmentId,
            FindingDigest);

        var result = Query([root, first, second], Finding());

        Assert.Equal("active", Assert.Single(result.Findings).State);
        Assert.Equal(0, result.SuppressedCount);
        Assert.Equal(1, result.UnresolvedJudgmentCount);
        var heads = Assert.Single(result.Findings).CurrentHeads!;
        Assert.Equal(2, heads.Count);
        Assert.Contains(heads, item => item.ContentDigest == HumanJudgmentCodec.LogicalDigest(first));
        Assert.Contains(heads, item => item.ContentDigest == HumanJudgmentCodec.LogicalDigest(second));
        Assert.All(result.History.Where(item => item.State == "conflict"),
            item => Assert.Equal("conflict", item.State));
    }

    [Fact]
    public void RetractionReturnsTheCandidateToActiveAndKeepsHistory()
    {
        var keep = Disposition([], ParsimonyDispositionKind.Keep, "keep reason", evidence: FindingDigest);
        var retract = NewJudgment(keep.JudgmentId, [Predecessor(keep)], new RetractionJudgment());

        var result = Query([keep, retract], Finding());

        Assert.Equal("active", Assert.Single(result.Findings).State);
        Assert.Equal("retracted", Assert.Single(result.History).State);
    }

    [Fact]
    public void MissingReportKeepsSavedDecisionsVisibleAsEvidenceUnavailable()
    {
        var keep = Disposition([], ParsimonyDispositionKind.Keep, "saved reason", evidence: FindingDigest);

        var result = Query([keep], reportAvailable: false);

        Assert.Empty(result.Findings);
        var history = Assert.Single(result.History);
        Assert.Equal("evidence-unavailable", history.State);
        Assert.Equal("saved reason", history.Reason);
        Assert.Equal(0, result.SuppressedCount);
        Assert.Null(history.ReportId);
        Assert.Null(history.BaselineBundleDigest);
    }

    [Fact]
    public void MissingArtifactQueryReturnsSavedDecisionWithoutClaimingMembership()
    {
        var keep = Disposition([], ParsimonyDispositionKind.Keep, "saved reason", evidence: FindingDigest);
        var judgments = Projection([keep]);
        var request = new ParsimonyNamedViewRequest("missing-bundle", "parsimony-suppressed",
            new ParsimonyViewFilters());

        var response = ParsimonyViewsQuery.ExecuteUnavailableEvidence(request, judgments);

        var row = Assert.IsType<ParsimonySuppressionHistoryViewRow>(Assert.Single(response.Rows));
        Assert.Equal("evidence-unavailable", row.State);
        Assert.Equal("saved reason", row.Reason);
        Assert.Equal(HumanJudgmentCodec.LogicalDigest(keep), row.ContentDigest);
        Assert.Equal(ParsimonyMeasureStatus.Inconclusive, response.Status);
        Assert.Null(response.SuppressedCount);
        Assert.Contains("unavailable", response.Detail);
    }

    [Fact]
    public void SavedDecisionViewsFilterExactSubjectAndSearchCaptions()
    {
        var judgment = Disposition([], ParsimonyDispositionKind.Keep, "saved reason", evidence: FindingDigest);
        var disposition = Assert.IsType<DispositionJudgment>(judgment.Body) with
        {
            SubjectCaption = "the exact subject caption",
            MeasureCaption = "the exact measure caption",
        };
        var judgments = Projection([judgment with { Body = disposition }]);
        var subjectKey = HumanJudgmentCodec.SubjectKey(disposition.Subject);
        var filtered = ParsimonyViewsQuery.ExecuteUnavailableEvidence(
            new ParsimonyNamedViewRequest("missing-bundle", "parsimony-suppressed",
                new ParsimonyViewFilters(SubjectKey: subjectKey)), judgments);
        var searched = ParsimonyViewsQuery.ExecuteUnavailableEvidence(
            new ParsimonyNamedViewRequest("missing-bundle", "parsimony-suppressed",
                new ParsimonyViewFilters(Search: "exact measure caption")), judgments);

        Assert.Equal(subjectKey, Assert.IsType<ParsimonySuppressionHistoryViewRow>(Assert.Single(filtered.Rows))
            .SubjectKey);
        Assert.Single(searched.Rows);
    }

    private static ParsimonyDispositionProjection Query(IReadOnlyList<HumanJudgment> values,
        ParsimonyFinding? finding = null, bool reportAvailable = true)
    {
        var projection = Projection(values);
        return ParsimonyDispositionQuery.Project("bundle/one", "report/one", "sha256:" + new string('b', 64),
            finding is null ? [] : [finding], projection, reportAvailable);
    }

    private static HumanJudgmentLineageProjection Projection(IReadOnlyList<HumanJudgment> values)
    {
        var stored = values.Select(value => new StoredHumanJudgment(value.RevisionId, value,
            HumanJudgmentCodec.LogicalDigest(value), HumanJudgmentCodec.Format(value))).ToArray();
        var snapshot = new HumanJudgmentProjectSnapshot(ProjectId, HumanJudgmentFieldCapability.Available,
            "available", stored, [], "sha256:" + new string('a', 64));
        return JudgmentLineageResolver.Resolve(snapshot);
    }

    private static ParsimonyFinding Finding(string evidence = FindingDigest) => new(
        "P-test:item", "P-test", ParsimonyAxis.Parsimony, ParsimonyTier.Static,
        new ParsimonyFindingAttachment(ParsimonyAttachmentKind.Project, ProjectId), null,
        new ParsimonyMeasureNumber(1, 1, "items"),
        new ParsimonyMeasureThreshold(ParsimonyThresholdOperator.RankedOnly, null, "1"),
        evidence, [], "P-test", [], ParsimonyVerification.NotRun);

    private static ParsimonyEvidenceReference EvidenceReference(Guid objectGuid) => new("bundle/one",
        "allomorph-context", JsonSerializer.SerializeToElement(new { objectGuid = objectGuid.ToString("D") }));

    private static HumanJudgment Disposition(IReadOnlyList<JudgmentPredecessor> replaces,
        ParsimonyDispositionKind disposition, string? reason, string? judgmentId = null,
        string evidence = FindingDigest) => NewJudgment(judgmentId ?? Id(), replaces,
        new DispositionJudgment(new ProjectJudgmentSubject(ProjectId), "P-test", disposition, evidence,
            "contract-v1", "sample subject", "Sample measure",
            disposition == ParsimonyDispositionKind.Ask ? "Should this remain active?" : null), reason);

    private static HumanJudgment NewJudgment(string judgmentId, IReadOnlyList<JudgmentPredecessor> replaces,
        HumanJudgmentBody body, string? reason = null) => new(ProjectId, judgmentId, Id(), replaces, body, reason);

    private static JudgmentPredecessor Predecessor(HumanJudgment judgment) =>
        new(judgment.RevisionId, HumanJudgmentCodec.LogicalDigest(judgment));

    private const string FindingDigest = "sha256:" + "1111111111111111111111111111111111111111111111111111111111111111";
    private static string Id() => CanonicalId.FromGuid(Guid.NewGuid()).Value;
}
