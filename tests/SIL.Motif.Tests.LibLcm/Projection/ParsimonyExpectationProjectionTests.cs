using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Contract.HumanJudgments;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Retirement;
using SIL.Motif.Commands.Parsimony;
using SIL.Motif.Host.Analysis;
using SIL.Motif.Projection.HumanJudgments;
using SIL.Motif.LiveHost.Baselines;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.LibLcm.Projection;

[Collection(LcmCacheTestCollection.Name)]
public sealed class ParsimonyExpectationProjectionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), nameof(ParsimonyExpectationProjectionTests),
        Guid.NewGuid().ToString("N"));
    private readonly LcmCache _cache;
    private readonly SeededProject _seed;

    public ParsimonyExpectationProjectionTests()
    {
        Directory.CreateDirectory(_root);
        _cache = NewLangProjFixture.CreateCache(_root);
        _seed = SeededProject.Seed(_cache);
    }

    [Fact]
    public void SurfaceAndReadingCasesKeepTheirScopeAndNativeOpinionsStaySeparate()
    {
        var fieldId = NotebookJudgmentFixture.InitializeReservedField(_cache);
        var form = _cache.ServiceLocator.GetInstance<IMoFormRepository>().GetObject(_seed.FirstLexemeFormId);
        var msa = _cache.ServiceLocator.GetInstance<IMoMorphSynAnalysisRepository>()
            .GetObject(_cache.ServiceLocator.GetInstance<ILexSenseRepository>().GetObject(_seed.FirstSenseId)
                .MorphoSyntaxAnalysisRA.Guid);
        var projectId = CanonicalId.FromGuid(_cache.LangProject.Guid).Value;
        var ws = _cache.WritingSystemFactory.GetStrFromWs(_cache.DefaultVernWs);
        var surfaceCaseId = Id();
        var readingCaseId = Id();

        AddNegative(fieldId, projectId, surfaceCaseId, ws, "zazara", "standard dialect",
            new SurfaceNegativeTarget());
        AddNegative(fieldId, projectId, readingCaseId, ws, "motifa", "standard dialect",
            new ReadingNegativeTarget([
                new NegativeJudgmentMorph(
                    new ParseMorph(CanonicalId.FromGuid(form.Guid).Value, CanonicalId.FromGuid(msa.Guid).Value,
                        null, null), null, "motifa", "N")
            ]));

        var wordform = AddNativeAnalysis("motifa", _cache.DefaultVernWs, form, msa, Opinions.disapproves);
        AddNativeAnalysis("motifa", _cache.DefaultVernWs, form, msa, Opinions.approves, wordform);

        var projection = ParsimonyExpectationProjectionBuilder.Build(_cache);

        Assert.Equal(ParsimonyExpectationProjection.ContractVersion, projection.Contract);
        Assert.Equal(projectId, projection.ProjectId);
        Assert.Equal("eligible", Assert.Single(projection.ReviewedNegatives,
            item => item.CaseId == surfaceCaseId).Status);
        var reading = Assert.Single(projection.ReviewedNegatives,
            item => item.CaseId == readingCaseId);
        Assert.Equal("conflict", reading.Status);
        Assert.Contains("Approved", reading.Issue, StringComparison.Ordinal);
        Assert.Single(projection.ApprovedReadings);
        Assert.Single(projection.DisapprovedReadings);
        Assert.Equal(CanonicalId.FromGuid(wordform.Guid).Value,
            Assert.Single(projection.DisapprovedReadings).SourceWordformIds.Single());
        Assert.Equal("disapproved", Assert.Single(projection.DisapprovedReadings).Opinion);
        Assert.Equal("approved", Assert.Single(projection.ApprovedReadings).Opinion);
        Assert.NotEqual(projection.Digest, string.Empty);
        Assert.Equal(projection.Digest, ParsimonyExpectationProjectionBuilder.Build(_cache).Digest);
    }

    [Fact]
    public void FrozenCaptureKeepsOriginalReadingsIndependentOfLaterScratchChanges()
    {
        var form = _cache.ServiceLocator.GetInstance<IMoFormRepository>().GetObject(_seed.FirstLexemeFormId);
        var firstMsa = _cache.ServiceLocator.GetInstance<IMoMorphSynAnalysisRepository>()
            .GetObject(_cache.ServiceLocator.GetInstance<ILexSenseRepository>().GetObject(_seed.FirstSenseId)
                .MorphoSyntaxAnalysisRA.Guid);
        var secondForm = _cache.ServiceLocator.GetInstance<IMoFormRepository>().GetObject(_seed.SecondLexemeFormId);
        var secondMsa = _cache.ServiceLocator.GetInstance<IMoMorphSynAnalysisRepository>()
            .GetObject(_cache.ServiceLocator.GetInstance<ILexSenseRepository>().GetObject(_seed.SecondSenseId)
                .MorphoSyntaxAnalysisRA.Guid);
        var wordform = AddNativeAnalysis("motifa", _cache.DefaultVernWs, form, firstMsa, Opinions.approves);
        AddNativeAnalysis("motifa", _cache.DefaultVernWs, secondForm, secondMsa, Opinions.disapproves, wordform);
        var writingSystem = _cache.WritingSystemFactory.GetStrFromWs(_cache.DefaultVernWs);
        var selection = new HashSet<string>(StringComparer.Ordinal)
        {
            FrozenExpectationCapture.CaseKey(CanonicalId.FromGuid(wordform.Guid).Value, writingSystem, "motifa"),
        };
        var baseline = new BaselineToken(_cache.LangProject.Guid.ToString("D"),
            BaselineSemanticDigest.Compute(_cache), BaselineSemanticDigest.ProjectionVersion,
            "2026-10-07T12:00:00Z", "sha256:" + new string('b', 64));

        var frozen = FrozenExpectationCapture.Capture(_cache, baseline, [wordform.Guid], selection,
            new HashSet<string>(StringComparer.Ordinal));
        var before = FrozenExpectationCodec.Parse(FrozenExpectationCodec.ToJson(frozen));
        AddNativeAnalysis("motifa", _cache.DefaultVernWs, form, firstMsa, Opinions.approves, wordform);

        var capturedCase = Assert.Single(before.Cases);
        Assert.True(capturedCase.InSelection);
        Assert.Equal(CanonicalId.FromGuid(wordform.Guid).Value, capturedCase.Wordform);
        Assert.Equal(2, capturedCase.Readings.Count);
        Assert.Contains(capturedCase.Readings, item => item.Opinion == "approved");
        Assert.Contains(capturedCase.Readings, item => item.Opinion == "disapproved");
        Assert.NotEqual("sha256:" + new string('0', 64), capturedCase.Readings[0].ProvenanceDigest);
        Assert.Contains(before.Unavailable, item => item.Contains("judgment-field-unavailable", StringComparison.Ordinal));
        Assert.Equal(2, before.Cases[0].Readings.Count);
    }

    [Fact]
    public void SameSpellingInDifferentWritingSystemsRemainsTwoNativeCases()
    {
        var form = _cache.ServiceLocator.GetInstance<IMoFormRepository>().GetObject(_seed.FirstLexemeFormId);
        var msa = _cache.ServiceLocator.GetInstance<IMoMorphSynAnalysisRepository>()
            .GetObject(_cache.ServiceLocator.GetInstance<ILexSenseRepository>().GetObject(_seed.FirstSenseId)
                .MorphoSyntaxAnalysisRA.Guid);
        _cache.ServiceLocator.WritingSystemManager.GetOrSet("qaa", out var alternate);
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            _cache.ServiceLocator.WritingSystems.VernacularWritingSystems.Add(alternate);
            _cache.ServiceLocator.WritingSystems.CurrentVernacularWritingSystems.Add(alternate);
        });
        var first = AddNativeAnalysis("homograph", _cache.DefaultVernWs, form, msa, Opinions.disapproves);
        AddNativeAnalysis("homograph", alternate.Handle, form, msa, Opinions.disapproves);

        var expectations = ApprovedMorphologyReader.ReadDisapprovedExpectations(_cache);

        var homographs = expectations.Where(item => item.Form == "homograph").ToArray();
        Assert.Equal(2, homographs.Length);
        Assert.Equal(2, homographs.Select(item => item.WritingSystem).Distinct(StringComparer.Ordinal).Count());
        Assert.Contains(CanonicalId.FromGuid(first.Guid).Value,
            homographs.SelectMany(item => item.SourceWordformIds));
    }

    [Fact]
    public void RetractionChangesTheDigestAndCompetingNegativeHeadsRemainConflicts()
    {
        var fieldId = NotebookJudgmentFixture.InitializeReservedField(_cache);
        var projectId = CanonicalId.FromGuid(_cache.LangProject.Guid).Value;
        var writingSystem = _cache.WritingSystemFactory.GetStrFromWs(_cache.DefaultVernWs);
        var retractedCase = Id();
        var firstRecordId = AddNegative(fieldId, projectId, retractedCase, writingSystem, "zazara",
            "standard dialect", new SurfaceNegativeTarget());
        var beforeRetraction = ParsimonyExpectationProjectionBuilder.Build(_cache);
        var original = HumanJudgmentReader.Read(_cache).Judgments.Single(item => item.RecordId == firstRecordId);
        var predecessor = new JudgmentPredecessor(original.Judgment.RevisionId, original.ContentDigest);
        var retraction = new HumanJudgment(projectId, original.Judgment.JudgmentId, Id(), [predecessor],
            new RetractionJudgment(), Actor: new JudgmentActor(JudgmentActorKind.Human));
        NotebookJudgmentFixture.AddRecord(_cache, CanonicalId.Parse(retraction.RevisionId).ToGuid(), fieldId,
            TsStringUtils.MakeString(HumanJudgmentCodec.Format(retraction), _cache.DefaultAnalWs));

        var afterRetraction = ParsimonyExpectationProjectionBuilder.Build(_cache);

        Assert.Empty(afterRetraction.ReviewedNegatives);
        Assert.NotEqual(beforeRetraction.Digest, afterRetraction.Digest);

        var forkedCase = Id();
        AddNegative(fieldId, projectId, forkedCase, writingSystem, "motifb-ra", "standard dialect",
            new SurfaceNegativeTarget(), judgmentId: forkedCase, revisionId: Id());
        AddNegative(fieldId, projectId, forkedCase, writingSystem, "motifb-ra", "standard dialect",
            new SurfaceNegativeTarget(), judgmentId: forkedCase, revisionId: Id());
        var forked = ParsimonyExpectationProjectionBuilder.Build(_cache);

        Assert.Equal(2, forked.ReviewedNegatives.Count(item => item.CaseId == forkedCase));
        Assert.All(forked.ReviewedNegatives.Where(item => item.CaseId == forkedCase),
            item => Assert.Equal("conflict", item.Status));
        Assert.Contains(forked.Issues, item => item.Code == "judgment-head-conflict" ||
            item.Code == "case-identity-conflict");
    }

    [Fact]
    public void IncorrectSpellingDoesNotBecomeAReviewedNegative()
    {
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            var wordform = _cache.ServiceLocator.GetInstance<IWfiWordformFactory>()
                .Create(TsStringUtils.MakeString("spellbad", _cache.DefaultVernWs));
            wordform.SpellingStatus = 2;
        });

        var projection = ParsimonyExpectationProjectionBuilder.Build(_cache);

        Assert.Contains("spellbad", ApprovedMorphologyReader.ReadIncorrectSpellings(_cache));
        Assert.DoesNotContain(projection.ReviewedNegatives, item => item.Form == "spellbad");
    }

    private string AddNegative(int fieldId, string projectId, string caseId, string writingSystem,
        string form, string context, NegativeJudgmentTarget target, string? judgmentId = null,
        string? revisionId = null)
    {
        revisionId ??= Id();
        var judgment = new HumanJudgment(projectId, judgmentId ?? caseId, revisionId, [],
            new ReviewedNegativeJudgment(caseId, writingSystem, form, context, target),
            Actor: new JudgmentActor(JudgmentActorKind.Human));
        NotebookJudgmentFixture.AddRecord(_cache, CanonicalId.Parse(revisionId).ToGuid(), fieldId,
            TsStringUtils.MakeString(HumanJudgmentCodec.Format(judgment), _cache.DefaultAnalWs));
        return revisionId;
    }

    private static string Id() => CanonicalId.FromGuid(Guid.NewGuid()).Value;

    private IWfiWordform AddNativeAnalysis(string surface, int ws, IMoForm form, IMoMorphSynAnalysis msa,
        Opinions opinion, IWfiWordform? existingWordform = null)
    {
        IWfiWordform wordform = existingWordform!;
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            wordform ??= _cache.ServiceLocator.GetInstance<IWfiWordformFactory>()
                .Create(TsStringUtils.MakeString(surface, ws));
            wordform.Form.set_String(ws, TsStringUtils.MakeString(surface, ws));
            var analysis = _cache.ServiceLocator.GetInstance<IWfiAnalysisFactory>().Create();
            wordform.AnalysesOC.Add(analysis);
            var bundle = _cache.ServiceLocator.GetInstance<IWfiMorphBundleFactory>().Create();
            analysis.MorphBundlesOS.Add(bundle);
            bundle.MorphRA = form;
            bundle.MsaRA = msa;
            _cache.LangProject.DefaultUserAgent.SetEvaluation(analysis, opinion);
        });
        return wordform;
    }

    public void Dispose()
    {
        if (!_cache.IsDisposed) _cache.Dispose();
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
