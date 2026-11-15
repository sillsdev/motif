using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Host.Texts;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Baselines;
using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.Infrastructure;
using Xunit;

namespace SIL.Motif.Tests.Worker;

public sealed class ProgressProjectionTests
{
    [Fact]
    public void MixedPlacesUseExactTypeUnionAndSpellingTakesPrecedence()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var shared = Guid.NewGuid();
        var misspelled = Guid.NewGuid();
        var words = new[]
        {
            Word(shared, "word", false, 1),
            Word(misspelled, "wrong", true, 0),
        };
        var projection = new TextWordsProjection(
        [
            Text(first, Token(shared, true), Token(misspelled, true)),
            Text(second, Token(shared, false)),
        ], words);

        var result = ProgressProjectionBuilder.Build(projection);
        var totals = ProgressProjectionBuilder.Aggregate(result.Texts);

        Assert.Equal(3, totals.Tokens.Total);
        Assert.Equal(1, totals.Tokens.Approved);
        Assert.Equal(1, totals.Tokens.Misspelled);
        Assert.Equal(1, totals.Tokens.Unmeasured);
        Assert.Equal(2, totals.Types.Total);
        Assert.Equal(0, totals.Types.Approved);
        Assert.Equal(1, totals.Types.Misspelled);
        Assert.Equal(1, totals.Types.Unmeasured);
        Assert.Equal(1, totals.Tokens.Reasons["fieldworks-candidate-unclassified"]);
    }

    [Fact]
    public void HomographsKeepTheirWordformJudgementsAndShareOneType()
    {
        var approved = Guid.NewGuid();
        var misspelled = Guid.NewGuid();
        var projection = new TextWordsProjection(
            [Text(Guid.NewGuid(), Token(approved, true), Token(misspelled, true))],
            [Word(approved, "same", false, 0), Word(misspelled, "same", true, 0)]);

        var counts = ProgressProjectionBuilder.Aggregate(ProgressProjectionBuilder.Build(projection).Texts);

        Assert.Equal(1, counts.Tokens.Approved);
        Assert.Equal(1, counts.Tokens.Misspelled);
        Assert.Equal(1, counts.Types.Total);
        Assert.Equal(1, counts.Types.Misspelled);
    }

    private static TextWordsProjectedWordform Word(Guid id, string form, bool incorrect, int candidates) =>
        new(id, [], [], candidates, incorrect, [])
        { CoverageSpelling = form, CoverageWritingSystem = "en" };

    private static TextWordsProjectedToken Token(Guid word, bool approved) =>
        new("", [], word, approved ? InterlinearAnalysisStatus.Approved : InterlinearAnalysisStatus.Unanalysed,
            null, null, null, null, 0, null);

    private static TextWordsProjectedText Text(Guid id, params TextWordsProjectedToken[] tokens) =>
        new(id, id.ToString(), [new TextWordsProjectedLine(1, "", tokens, Guid.NewGuid(), Guid.NewGuid(), true)], []);
}

[Collection(LcmCacheParallelCollections.Group0)]
public sealed class ProgressDeltaTests(PristineProjectFixture pristine)
{
    [Fact]
    public void ApprovalAndSpellingChangeOnlyTheAffectedWordformPlaces()
    {
        using var cache = pristine.NewScratch();
        var seeded = SeededProject.SeedText(cache, pristine.Seed);
        var analysis = cache.ServiceLocator.GetInstance<IWfiAnalysisRepository>().GetObject(seeded.ApprovedAnalysisId);
        var wordform = cache.ServiceLocator.GetInstance<IWfiWordformRepository>().GetObject(seeded.AnalysedWordformId);
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor,
            () => cache.LangProject.DefaultUserAgent.SetEvaluation(analysis, Opinions.disapproves));
        var ordinary = TextWordsProjectionBuilder.Build(cache, CancellationToken.None);
        Assert.All(ordinary.Wordforms, word => Assert.Null(word.CoverageSpelling));
        var original = ProgressProjectionBuilder.Build(TextWordsProjectionBuilder.Build(cache, CancellationToken.None,
            includeCoverage: true));
        Assert.Equal(0, ProgressProjectionBuilder.Aggregate(original.Texts).Tokens.Approved);

        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor,
            () => cache.LangProject.DefaultUserAgent.SetEvaluation(analysis, Opinions.approves));
        var approved = ProgressDeltaBuilder.Apply(cache, original,
            [new OperationEnvelope(CanonicalId.Mint(), WfiAnalysisOperationKinds.AddRefEvaluations,
                target: CanonicalId.FromGuid(analysis.Guid))]);
        Assert.NotNull(approved);
        Assert.Equal(1, ProgressProjectionBuilder.Aggregate(approved.Texts).Tokens.Approved);

        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () => wordform.SpellingStatus = 2);
        var misspelled = ProgressDeltaBuilder.Apply(cache, approved,
            [new OperationEnvelope(CanonicalId.Mint(), WfiWordformSpellingStatusOperationKinds.SetSpellingStatus,
                target: CanonicalId.FromGuid(wordform.Guid))]);
        Assert.NotNull(misspelled);
        var totals = ProgressProjectionBuilder.Aggregate(misspelled.Texts);
        Assert.Equal(0, totals.Tokens.Approved);
        Assert.Equal(1, totals.Tokens.Misspelled);
        Assert.Equal(1, totals.Tokens.Unmeasured);
    }
}
