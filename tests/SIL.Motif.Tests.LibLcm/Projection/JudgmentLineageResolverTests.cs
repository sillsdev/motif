using SIL.Motif.Contract.HumanJudgments;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Projection.HumanJudgments;
using Xunit;

namespace SIL.Motif.Tests.LibLcm.Projection;

public sealed class JudgmentLineageResolverTests
{
    private static readonly string ProjectId = Id();

    [Fact]
    public void DivergentRevisionsRemainConflictingHeads()
    {
        var root = Disposition([], ParsimonyDispositionKind.Keep, "root reason");
        var first = Disposition([Predecessor(root)], ParsimonyDispositionKind.Keep, "first reason",
            root.JudgmentId);
        var second = Disposition([Predecessor(root)], ParsimonyDispositionKind.Defer, "second reason",
            root.JudgmentId);

        var projection = Resolve(root, first, second);

        Assert.Equal(2, projection.Heads.Count(item => item.JudgmentId == root.JudgmentId));
        Assert.All(projection.Heads.Where(item => item.JudgmentId == root.JudgmentId),
            item => Assert.Equal("conflict", item.State));
        Assert.Contains(first.RevisionId, projection.Heads.Select(item => item.RevisionId));
        Assert.Contains(second.RevisionId, projection.Heads.Select(item => item.RevisionId));
        Assert.DoesNotContain(projection.Heads, item => item.State == "effective");
    }

    [Fact]
    public void ExplicitRetractionIsTheEffectiveHeadAndRetainsItsParent()
    {
        var root = Disposition([], ParsimonyDispositionKind.Keep, "keep reason");
        var retraction = NewJudgment(root.JudgmentId, [Predecessor(root)], new RetractionJudgment());

        var projection = Resolve(root, retraction);

        var head = Assert.Single(projection.Heads);
        Assert.Equal(retraction.RevisionId, head.RevisionId);
        Assert.Equal("effective", head.State);
        Assert.Contains(projection.Revisions, item => item.RecordId == root.RevisionId && item.State == "superseded");
        Assert.Contains(projection.Revisions, item => item.RecordId == retraction.RevisionId && item.State == "head");
    }

    [Fact]
    public void MissingOrMismatchedPredecessorMakesTheLogicalJudgmentUnavailable()
    {
        var missing = NewJudgment(Id(), [new JudgmentPredecessor(Id(), Digest('a'))],
            new RetractionJudgment());

        var projection = Resolve(missing);

        Assert.DoesNotContain(projection.Heads, item => item.State == "effective");
        Assert.Contains(projection.Heads, item => item.State == "unavailable");
        Assert.Contains(projection.Unavailable, item => item.RecordId == missing.RevisionId);
    }

    [Fact]
    public void RebuildProducesTheSameOrderedProjectionDigest()
    {
        var root = Disposition([], ParsimonyDispositionKind.Keep, "reason");
        var child = Disposition([Predecessor(root)], ParsimonyDispositionKind.Keep, "edited reason", root.JudgmentId);

        var first = Resolve(root, child);
        var second = Resolve(root, child);

        Assert.Equal(first.Digest, second.Digest);
        Assert.Equal(first.Revisions.Select(item => item.RecordId), second.Revisions.Select(item => item.RecordId));
    }

    [Fact]
    public void OneLogicalJudgmentCannotChangeItsExactFindingIdentity()
    {
        var root = Disposition([], ParsimonyDispositionKind.Keep, "reason");
        var changed = Disposition([Predecessor(root)], ParsimonyDispositionKind.Keep, "reason", root.JudgmentId,
            evidenceDigest: Digest('b'));

        var projection = Resolve(root, changed);

        Assert.Contains(projection.Heads, item => item.State == "unavailable");
        Assert.Contains(projection.Unavailable, item => item.Reason.Contains("identity", StringComparison.OrdinalIgnoreCase));
    }

    private static HumanJudgmentLineageProjection Resolve(params HumanJudgment[] values)
    {
        var stored = values.Select(value => new StoredHumanJudgment(value.RevisionId, value,
            HumanJudgmentCodec.LogicalDigest(value), HumanJudgmentCodec.Format(value))).ToArray();
        var snapshot = new HumanJudgmentProjectSnapshot(ProjectId, HumanJudgmentFieldCapability.Available,
            "available", stored, [], "sha256:" + new string('f', 64));
        return JudgmentLineageResolver.Resolve(snapshot);
    }

    private static HumanJudgment Disposition(IReadOnlyList<JudgmentPredecessor> replaces,
        ParsimonyDispositionKind disposition, string? reason, string? judgmentId = null,
        string? evidenceDigest = null) => NewJudgment(judgmentId ?? Id(), replaces,
        new DispositionJudgment(new ProjectJudgmentSubject(ProjectId), "P-test", disposition,
            evidenceDigest ?? Digest('1'), "contract-v1", "sample subject", "Sample measure"), reason);

    private static HumanJudgment NewJudgment(string judgmentId, IReadOnlyList<JudgmentPredecessor> replaces,
        HumanJudgmentBody body, string? reason = null) => new(ProjectId, judgmentId, Id(), replaces, body, reason);

    private static JudgmentPredecessor Predecessor(HumanJudgment judgment) =>
        new(judgment.RevisionId, HumanJudgmentCodec.LogicalDigest(judgment));

    private static string Id() => CanonicalId.FromGuid(Guid.NewGuid()).Value;
    private static string Digest(char digit) => "sha256:" + new string(digit, 64);
}
