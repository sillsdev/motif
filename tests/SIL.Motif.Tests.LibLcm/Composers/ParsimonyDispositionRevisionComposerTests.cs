using System.Linq;
using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands.Composers;
using SIL.Motif.Contract.HumanJudgments;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Projection.HumanJudgments;
using SIL.Motif.Runner.Apply;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Composers;

[Collection(LcmCacheTestCollection.Name)]
public sealed class ParsimonyDispositionRevisionComposerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), nameof(ParsimonyDispositionRevisionComposerTests),
        Guid.NewGuid().ToString("N"));
    private readonly LcmCache _cache;
    private readonly int _fieldId;
    private readonly CanonicalId _recordTypeId;

    public ParsimonyDispositionRevisionComposerTests()
    {
        Directory.CreateDirectory(_root);
        _cache = NewLangProjFixture.CreateCache(_root);
        _fieldId = NotebookJudgmentFixture.InitializeReservedField(_cache);
        _recordTypeId = AddRecordType();
    }

    [Fact]
    public void RevisionPreservesFindingEvidenceAndAppendsAnAgentAttributedRecord()
    {
        var original = Disposition();
        Store(original);
        var revisedReason = "The forms are intentionally retained for this paradigm.";
        var intent = new ReviseParsimonyDispositionIntent(RecordId(original), [Predecessor(original)],
            ParsimonyDispositionKind.Keep, revisedReason);

        var operations = ParsimonyDispositionRevisionComposer.BuildRevision(_cache, intent, CanonicalId.Mint());
        var revision = ReadCreatedJudgment(operations);
        var body = Assert.IsType<DispositionJudgment>(revision.Body);

        Assert.Equal(original.JudgmentId, revision.JudgmentId);
        Assert.Equal([Predecessor(original)], revision.Replaces);
        Assert.Equal(JudgmentActorKind.Agent, revision.Actor!.Kind);
        Assert.Equal(revisedReason, revision.Reason);
        Assert.Equal(Assert.IsType<DispositionJudgment>(original.Body), body);
        Assert.Equal(7, operations.Count);
    }

    [Fact]
    public void RetractionRequiresEveryCurrentHeadAndAppliesWithoutDeletingHistory()
    {
        var original = Disposition();
        Store(original);
        var first = original with { RevisionId = Id(), Replaces = [Predecessor(original)], Reason = "First branch." };
        var second = original with { RevisionId = Id(), Replaces = [Predecessor(original)], Reason = "Second branch." };
        Store(first);
        Store(second);
        var heads = new[] { Predecessor(first), Predecessor(second) };

        Assert.Throws<InvalidOperationException>(() => ParsimonyDispositionRevisionComposer.BuildRetraction(
            _cache, new RetractParsimonyDispositionIntent(RecordId(first), [Predecessor(first)]), CanonicalId.Mint()));

        var proposalId = CanonicalId.Mint();
        var operations = ParsimonyDispositionRevisionComposer.BuildRetraction(_cache,
            new RetractParsimonyDispositionIntent(RecordId(first), heads), proposalId);
        var retraction = ReadCreatedJudgment(operations);

        Assert.IsType<RetractionJudgment>(retraction.Body);
        Assert.Equal(JudgmentActorKind.Agent, retraction.Actor!.Kind);
        Assert.True(retraction.ResolvesConflict);
        Assert.Equal(heads.OrderBy(item => item.RevisionId), retraction.Replaces.OrderBy(item => item.RevisionId));

        Apply(operations, proposalId);

        var lineage = JudgmentLineageResolver.Resolve(HumanJudgmentReader.Read(_cache));
        Assert.Equal(4, lineage.Revisions.Count);
        Assert.Equal("effective", Assert.Single(lineage.Heads).State);
        Assert.Equal(retraction.RevisionId, Assert.Single(lineage.Heads).RevisionId);
        Assert.Contains(lineage.Revisions, item => item.RevisionId == original.RevisionId && item.State == "superseded");
    }

    [Fact]
    public void RevisionRefusesStaleRecordsAndWrongProjectRecords()
    {
        var original = Disposition();
        Store(original);
        var current = original with { RevisionId = Id(), Replaces = [Predecessor(original)] };
        Store(current);

        var stale = new ReviseParsimonyDispositionIntent(RecordId(original), [Predecessor(original)],
            ParsimonyDispositionKind.Keep, "A stale edit.");
        Assert.Throws<InvalidOperationException>(() => ParsimonyDispositionRevisionComposer.BuildRevision(
            _cache, stale, CanonicalId.Mint()));

        var foreignRecord = CanonicalId.Mint();
        var wrongProject = new ReviseParsimonyDispositionIntent(foreignRecord, [Predecessor(current)],
            ParsimonyDispositionKind.Keep, "A record from another project.");
        Assert.Throws<InvalidOperationException>(() => ParsimonyDispositionRevisionComposer.BuildRevision(
            _cache, wrongProject, CanonicalId.Mint()));
    }

    private HumanJudgment Disposition(string? judgmentId = null, string? revisionId = null) => new(
        CanonicalId.FromGuid(_cache.LangProject.Guid).Value,
        judgmentId ?? Id(), revisionId ?? Id(), [],
        new DispositionJudgment(new ProjectJudgmentSubject(CanonicalId.FromGuid(_cache.LangProject.Guid).Value),
            "P-allo-duplicate-form", ParsimonyDispositionKind.Keep,
            "sha256:" + new string('a', 64), "P-allo-duplicate-form/v1", "the grammar", "Duplicate forms"),
        Reason: "The current reason.", Actor: new JudgmentActor(JudgmentActorKind.Agent));

    private void Store(HumanJudgment judgment)
    {
        var record = NotebookJudgmentFixture.AddRecord(_cache,
            CanonicalId.Parse(judgment.RevisionId).ToGuid(), _fieldId,
            TsStringUtils.MakeString(HumanJudgmentCodec.Format(judgment), _cache.DefaultAnalWs));
        var recordType = _cache.ServiceLocator.GetInstance<ICmPossibilityRepository>()
            .GetObject(_recordTypeId.ToGuid());
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () => record.TypeRA = recordType);
    }

    private HumanJudgment ReadCreatedJudgment(IReadOnlyList<OperationEnvelope> operations)
    {
        var create = Assert.Single(operations, item => item.Kind == RnResearchNbkRecordsOperationKinds.Create);
        var set = Assert.Single(operations, item => item.Kind == HumanJudgmentCustomFieldOperationKinds.Set);
        var text = set.After!.Value.GetProperty("text").GetString()!;
        return HumanJudgmentCodec.Parse(text, CanonicalId.FromGuid(_cache.LangProject.Guid).Value,
            create.EntityId!.Value.Value);
    }

    private void Apply(IReadOnlyList<OperationEnvelope> operations, CanonicalId proposalId)
    {
        var versions = operations.Select(item => OperationKind.GetGroup(item.Kind)).Distinct(StringComparer.Ordinal)
            .ToDictionary(group => group, _ => "1.0", StringComparer.Ordinal);
        var proposal = new Proposal(versions, proposalId, null, operations);
        var dryRun = ScratchDryRun.Of(_cache, proposal);
        ProposalApplier.Apply(_cache, proposal, dryRun.Anchor, "reviewer");
    }

    private CanonicalId AddRecordType()
    {
        ICmPossibility type = null!;
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            var notebook = _cache.LangProject.ResearchNotebookOA;
            var list = notebook.RecTypesOA ?? _cache.ServiceLocator.GetInstance<ICmPossibilityListFactory>().Create();
            notebook.RecTypesOA = list;
            type = _cache.ServiceLocator.GetInstance<ICmPossibilityFactory>().Create();
            list.PossibilitiesOS.Add(type);
            type.Name.set_String(_cache.DefaultAnalWs, TsStringUtils.MakeString("Motif judgment", _cache.DefaultAnalWs));
        });
        return CanonicalId.FromGuid(type.Guid);
    }

    private static CanonicalId RecordId(HumanJudgment judgment) => CanonicalId.Parse(judgment.RevisionId);
    private static JudgmentPredecessor Predecessor(HumanJudgment judgment) =>
        new(judgment.RevisionId, HumanJudgmentCodec.LogicalDigest(judgment));
    private static string Id() => CanonicalId.FromGuid(Guid.NewGuid()).Value;

    public void Dispose()
    {
        if (!_cache.IsDisposed) _cache.Dispose();
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
