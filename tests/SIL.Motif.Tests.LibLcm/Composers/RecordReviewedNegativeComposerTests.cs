using System.Text.Json;
using System.Linq;
using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Contract.HumanJudgments;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Model;
using SIL.Motif.Commands.Composers;
using SIL.Motif.Projection.HumanJudgments;
using SIL.Motif.Runner.Apply;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Composers;

[Collection(LcmCacheTestCollection.Name)]
public sealed class RecordReviewedNegativeComposerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), nameof(RecordReviewedNegativeComposerTests),
        Guid.NewGuid().ToString("N"));
    private readonly LcmCache _cache;
    private readonly SeededProject _seed;
    private readonly CanonicalId _recordTypeId;

    public RecordReviewedNegativeComposerTests()
    {
        Directory.CreateDirectory(_root);
        _cache = NewLangProjFixture.CreateCache(_root);
        _seed = SeededProject.Seed(_cache);
        NotebookJudgmentFixture.InitializeReservedField(_cache);
        _recordTypeId = AddRecordType();
    }

    [Fact]
    public void SurfaceNegativeNeedsNoWordformOrEntryAndKeepsNullHumanProvenanceOptional()
    {
        var intent = new RecordReviewedNegativeIntent(_recordTypeId, Id(), VernacularTag(), "zazara",
            "standard dialect", new SurfaceNegativeTarget());

        var judgment = BuildAndRead(intent);

        var negative = Assert.IsType<ReviewedNegativeJudgment>(judgment.Body);
        Assert.IsType<SurfaceNegativeTarget>(negative.Target);
        Assert.Null(negative.WordformId);
        Assert.Null(negative.AnalysisId);
        Assert.Null(judgment.Reason);
        Assert.Null(judgment.JudgedAtUtc);
        Assert.Null(judgment.Actor!.Id);
        Assert.Equal(JudgmentActorKind.Human, judgment.Actor.Kind);
    }

    [Fact]
    public void ReadingNegativeKeepsTheCompleteOrderedMorphologyIdentity()
    {
        var first = Morphology(_seed.FirstLexemeFormId, _seed.FirstSenseId);
        var second = Morphology(_seed.SecondLexemeFormId, _seed.SecondSenseId);
        var intent = new RecordReviewedNegativeIntent(_recordTypeId, Id(), VernacularTag(), "motifa-ra",
            "standard dialect", new ReadingNegativeTarget(
            [
                new NegativeJudgmentMorph(new ParseMorph(first.Form, first.Msa, null, null), null, "motifa", "noun"),
                new NegativeJudgmentMorph(new ParseMorph(second.Form, second.Msa, null, null), null, "-ra", "plural"),
            ]), Reason: "This sequence is rejected.");

        var judgment = BuildAndRead(intent);
        var negative = Assert.IsType<ReviewedNegativeJudgment>(judgment.Body);
        var target = Assert.IsType<ReadingNegativeTarget>(negative.Target);

        Assert.Equal(2, target.Morphs.Count);
        Assert.Equal(first.Form, target.Morphs[0].Identity.Form);
        Assert.Equal(first.Msa, target.Morphs[0].Identity.Msa);
        Assert.Null(target.Morphs[0].Identity.InflType);
        Assert.Equal(second.Form, target.Morphs[1].Identity.Form);
        Assert.Equal(second.Msa, target.Morphs[1].Identity.Msa);
        Assert.Null(target.Morphs[1].Identity.InflType);
        Assert.Equal("This sequence is rejected.", judgment.Reason);
    }

    [Fact]
    public void HumanReviewedNegativeCanBeDryRunAppliedAndReadBack()
    {
        var intent = new RecordReviewedNegativeIntent(_recordTypeId, Id(), VernacularTag(), "zazara",
            "standard dialect", new SurfaceNegativeTarget());
        var proposalId = CanonicalId.Parse(Id());
        var operations = RecordReviewedNegativeComposer.Build(_cache, intent, proposalId);
        var versions = operations.Select(item => OperationKind.GetGroup(item.Kind))
            .Distinct(StringComparer.Ordinal)
            .ToDictionary(group => group, _ => "1.0", StringComparer.Ordinal);
        var proposal = new Proposal(versions, proposalId, null, operations);

        var dryRun = ScratchDryRun.Of(_cache, proposal);
        var receipt = ProposalApplier.Apply(_cache, proposal, dryRun.Anchor, "motif-tests");

        Assert.Equal(proposalId, receipt.ProposalId);
        var saved = Assert.Single(HumanJudgmentReader.Read(_cache).Judgments,
            item => item.Judgment.Body is ReviewedNegativeJudgment negative && negative.CaseId == intent.CaseId);
        Assert.Equal("zazara", Assert.IsType<ReviewedNegativeJudgment>(saved.Judgment.Body).Form);
    }

    [Fact]
    public void MissingMorphologyGuidIsRefusedBeforeAProposalCanBeStaged()
    {
        var knownMsa = Morphology(_seed.FirstLexemeFormId, _seed.FirstSenseId).Msa;
        var target = new ReadingNegativeTarget(
        [
            new NegativeJudgmentMorph(new ParseMorph(Id(), knownMsa, null, null), null, "unknown", "noun"),
        ]);
        var intent = new RecordReviewedNegativeIntent(_recordTypeId, Id(), VernacularTag(), "unknown",
            "standard dialect", target);

        var error = Assert.Throws<InvalidOperationException>(() => RecordReviewedNegativeComposer.Build(
            _cache, intent, CanonicalId.Parse(Id())));

        Assert.Contains("does not exist", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RetractionRejectsAnAncestorAndCanResolveEveryCurrentConflictingHead()
    {
        var projectId = CanonicalId.FromGuid(_cache.LangProject.Guid).Value;
        var caseId = Id();
        var original = new HumanJudgment(projectId, caseId, Id(), [],
            new ReviewedNegativeJudgment(caseId, VernacularTag(), "zazara", "standard dialect",
                new SurfaceNegativeTarget()), Actor: new JudgmentActor(JudgmentActorKind.Human));
        Store(original);
        var originalHead = Predecessor(original);
        var left = original with { RevisionId = Id(), Replaces = [originalHead] };
        var right = original with { RevisionId = Id(), Replaces = [originalHead] };
        Store(left);
        Store(right);

        var stale = new RetractReviewedNegativeIntent(_recordTypeId, caseId, [originalHead]);
        Assert.Throws<InvalidOperationException>(() => RetractReviewedNegativeComposer.Build(
            _cache, stale, CanonicalId.Parse(Id())));

        var expectedHeads = new[] { Predecessor(left), Predecessor(right) };
        var intent = new RetractReviewedNegativeIntent(_recordTypeId, caseId, expectedHeads);
        var operations = RetractReviewedNegativeComposer.Build(_cache, intent, CanonicalId.Parse(Id()));
        var create = Assert.Single(operations, item => item.Kind == RnResearchNbkRecordsOperationKinds.Create);
        var set = Assert.Single(operations, item => item.Kind == HumanJudgmentCustomFieldOperationKinds.Set);
        var physical = set.After!.Value.GetProperty("text").GetString()!;
        var retraction = HumanJudgmentCodec.Parse(physical, projectId, create.EntityId!.Value.Value);

        Assert.IsType<RetractionJudgment>(retraction.Body);
        Assert.True(retraction.ResolvesConflict);
        Assert.Equal(2, retraction.Replaces.Count);
    }

    private HumanJudgment BuildAndRead(RecordReviewedNegativeIntent intent)
    {
        var operations = RecordReviewedNegativeComposer.Build(_cache, intent, CanonicalId.Parse(Id()));
        Assert.Equal(7, operations.Count);
        var create = Assert.Single(operations, item => item.Kind == RnResearchNbkRecordsOperationKinds.Create);
        var set = Assert.Single(operations, item => item.Kind == HumanJudgmentCustomFieldOperationKinds.Set);
        var physical = set.After!.Value.GetProperty("text").GetString()!;
        return HumanJudgmentCodec.Parse(physical, CanonicalId.FromGuid(_cache.LangProject.Guid).Value,
            create.EntityId!.Value.Value);
    }

    private void Store(HumanJudgment judgment) => NotebookJudgmentFixture.AddRecord(_cache,
        CanonicalId.Parse(judgment.RevisionId).ToGuid(), NotebookJudgmentFixture.FindReservedField(_cache),
        TsStringUtils.MakeString(HumanJudgmentCodec.Format(judgment), _cache.DefaultAnalWs));

    private static JudgmentPredecessor Predecessor(HumanJudgment judgment) =>
        new(judgment.RevisionId, HumanJudgmentCodec.LogicalDigest(judgment));

    private (string Form, string Msa) Morphology(Guid formId, Guid senseId)
    {
        var form = _cache.ServiceLocator.GetInstance<IMoFormRepository>().GetObject(formId);
        var sense = _cache.ServiceLocator.GetInstance<ILexSenseRepository>().GetObject(senseId);
        return (CanonicalId.FromGuid(form.Guid).Value, CanonicalId.FromGuid(sense.MorphoSyntaxAnalysisRA.Guid).Value);
    }

    private string VernacularTag() => _cache.WritingSystemFactory.GetStrFromWs(_cache.DefaultVernWs);

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
            type.Name.set_String(_cache.DefaultAnalWs,
                TsStringUtils.MakeString("Reviewed negative", _cache.DefaultAnalWs));
        });
        return CanonicalId.FromGuid(type.Guid);
    }

    private static string Id() => CanonicalId.FromGuid(Guid.NewGuid()).Value;

    public void Dispose()
    {
        if (!_cache.IsDisposed) _cache.Dispose();
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
