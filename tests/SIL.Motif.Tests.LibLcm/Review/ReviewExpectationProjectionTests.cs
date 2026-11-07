using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Contract.Review;
using SIL.Motif.Contract.HumanJudgments;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Projection.Review;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.LibLcm.Review;

[Collection(LcmCacheTestCollection.Name)]
public sealed class ReviewExpectationProjectionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), nameof(ReviewExpectationProjectionTests), Guid.NewGuid().ToString("N"));
    private readonly LcmCache _cache;
    private readonly SeededProject _seed;

    public ReviewExpectationProjectionTests()
    {
        Directory.CreateDirectory(_root);
        _cache = NewLangProjFixture.CreateCache(_root);
        _seed = SeededProject.Seed(_cache);
    }

    [Fact]
    public void AllApprovedMorphologiesAreCaptured()
    {
        IWfiWordform word = null!;
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            word = _cache.ServiceLocator.GetInstance<IWfiWordformFactory>()
                .Create(TsStringUtils.MakeString("example", _cache.DefaultVernWs));
            AddAnalysis(word, _seed.FirstLexemeFormId, _seed.FirstSenseId, Opinions.approves);
            AddAnalysis(word, _seed.SecondLexemeFormId, _seed.SecondSenseId, Opinions.approves);
            AddAnalysis(word, _seed.FirstLexemeFormId, _seed.FirstSenseId, Opinions.disapproves);
        });

        var assertions = ReviewExpectationProjection.ReadApproved(_cache)[word.Guid];

        Assert.Equal(2, assertions.Count);
        Assert.All(assertions, assertion =>
        {
            Assert.Equal(ReferenceAuthority.Human, assertion.Authority);
            Assert.True(assertion.Approved);
            Assert.True(assertion.Eligible);
        });
        Assert.Equal(2, assertions.Select(item => item.ReadingIdentity).Distinct().Count());
    }

    [Fact]
    public void MissingIdentityIsVisibleNotGuessed()
    {
        IWfiWordform word = null!;
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            word = _cache.ServiceLocator.GetInstance<IWfiWordformFactory>()
                .Create(TsStringUtils.MakeString("example", _cache.DefaultVernWs));
            var analysis = _cache.ServiceLocator.GetInstance<IWfiAnalysisFactory>().Create();
            word.AnalysesOC.Add(analysis);
            analysis.MorphBundlesOS.Add(_cache.ServiceLocator.GetInstance<IWfiMorphBundleFactory>().Create());
            _cache.LangProject.DefaultUserAgent.SetEvaluation(analysis, Opinions.approves);
        });

        var assertion = Assert.Single(ReviewExpectationProjection.ReadApproved(_cache)[word.Guid]);
        Assert.False(assertion.Eligible);
        Assert.Null(Assert.Single(assertion.Morphology!.Morphs).Form);
    }

    [Fact]
    public void SameSpellingDifferentWritingSystemsKeepsDistinctWords()
    {
        _cache.ServiceLocator.WritingSystemManager.GetOrSet("qaa", out var alternate);
        IWfiWordform word = null!;
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            _cache.ServiceLocator.WritingSystems.VernacularWritingSystems.Add(alternate);
            word = _cache.ServiceLocator.GetInstance<IWfiWordformFactory>()
                .Create(TsStringUtils.MakeString("example", _cache.DefaultVernWs));
            word.Form.set_String(alternate.Handle, TsStringUtils.MakeString("example", alternate.Handle));
        });
        var captured = ReviewWordIdentityProjection.Read(_cache)
            .Where(item => item.WordformId == SIL.Motif.Contract.Ids.CanonicalId.FromGuid(word.Guid).Value).ToArray();

        Assert.Equal(2, captured.Length);
        Assert.Equal(2, captured.Select(item => item.WordKey).Distinct().Count());
        Assert.All(captured, item => Assert.NotNull(item.WritingSystemId));
    }

    [Fact]
    public void NfdDoesNotFoldCaseStressOrQuantity()
    {
        var surfaces = new[] { "Á", "á", "a", "ā" };
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            foreach (var surface in surfaces)
                _cache.ServiceLocator.GetInstance<IWfiWordformFactory>()
                    .Create(TsStringUtils.MakeString(surface, _cache.DefaultVernWs));
        });
        var captured = ReviewWordIdentityProjection.Read(_cache);
        foreach (var surface in surfaces)
        {
            var match = Assert.Single(captured, item => item.Word == surface.Normalize(System.Text.NormalizationForm.FormD));
            Assert.NotNull(match.WordKey);
            Assert.Equal(_cache.WritingSystemFactory.GetStrFromWs(_cache.DefaultVernWs), match.WritingSystemId);
        }
    }

    [Fact]
    public void SourceValidatedActorIsNeverHuman()
    {
        var source = NegativeProjection(new JudgmentActor(JudgmentActorKind.Agent, "agent"));
        Assert.Empty(ReviewExpectationProjection.ReadReviewedNegatives(source));

        IWfiWordform word = null!;
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            word = _cache.ServiceLocator.GetInstance<IWfiWordformFactory>()
                .Create(TsStringUtils.MakeString("example", _cache.DefaultVernWs));
            AddAnalysis(word, _seed.FirstLexemeFormId, _seed.FirstSenseId, Opinions.approves);
        });
        var agent = Guid.NewGuid();
        var decision = new ReviewSourceDecision(Assert.Single(word.AnalysesOC).Guid, agent, true,
            "proposal", "revision", "operation");
        var validated = Assert.Single(ReviewExpectationProjection.ReadSourceValidated(_cache, [decision])[word.Guid]);
        Assert.Equal(ReferenceAuthority.SourceValidated, validated.Authority);
        Assert.Equal(SIL.Motif.Contract.Ids.CanonicalId.FromGuid(agent).Value, validated.ActorId);
        Assert.True(validated.Eligible);
        Assert.False(validated.Approved);
    }

    [Fact]
    public void ReviewedSurfaceNegativeKeepsItsRevisionAndContext()
    {
        var source = NegativeProjection(new JudgmentActor(JudgmentActorKind.Human, "person"));
        var negative = Assert.Single(ReviewExpectationProjection.ReadReviewedNegatives(source));
        Assert.Equal("revision", negative.Assertion.RevisionId);
        Assert.Equal("dialect", negative.Context);
        Assert.True(negative.Assertion.Eligible);
        Assert.True(negative.Assertion.WholeWordNegative);
    }

    [Fact]
    public void OpposedSourceDecisionsDoNotConfirmAReading()
    {
        IWfiWordform word = null!;
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            word = _cache.ServiceLocator.GetInstance<IWfiWordformFactory>()
                .Create(TsStringUtils.MakeString("example", _cache.DefaultVernWs));
            AddAnalysis(word, _seed.FirstLexemeFormId, _seed.FirstSenseId, Opinions.approves);
        });
        var analysis = Assert.Single(word.AnalysesOC).Guid;
        ReviewSourceDecision[] decisions =
        [
            new(analysis, Guid.NewGuid(), true, "proposal-1", "revision-1", "operation-1"),
            new(analysis, Guid.NewGuid(), false, "proposal-2", "revision-2", "operation-2"),
        ];

        var assertions = ReviewExpectationProjection.ReadSourceValidated(_cache, decisions)[word.Guid];
        Assert.Equal(2, assertions.Count);
        Assert.All(assertions, assertion => Assert.False(assertion.Eligible));
    }

    private static ParsimonyExpectationProjection NegativeProjection(JudgmentActor actor) => new(
        ParsimonyExpectationProjection.ContractVersion, "project", "digest",
        [new ReviewedNegativeExpectation("case", "judgment", "revision", "record", "content", "qaa", "á",
            "dialect", new SurfaceNegativeTarget(), null, null, null, actor, null, null, "eligible", null)],
        [], [], []);

    private void AddAnalysis(IWfiWordform word, Guid formId, Guid senseId, Opinions opinion)
    {
        var analysis = _cache.ServiceLocator.GetInstance<IWfiAnalysisFactory>().Create();
        word.AnalysesOC.Add(analysis);
        var bundle = _cache.ServiceLocator.GetInstance<IWfiMorphBundleFactory>().Create();
        analysis.MorphBundlesOS.Add(bundle);
        bundle.MorphRA = _cache.ServiceLocator.GetInstance<IMoFormRepository>().GetObject(formId);
        bundle.MsaRA = _cache.ServiceLocator.GetInstance<ILexSenseRepository>().GetObject(senseId).MorphoSyntaxAnalysisRA;
        _cache.LangProject.DefaultUserAgent.SetEvaluation(analysis, opinion);
    }

    public void Dispose()
    {
        if (!_cache.IsDisposed) _cache.Dispose();
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
