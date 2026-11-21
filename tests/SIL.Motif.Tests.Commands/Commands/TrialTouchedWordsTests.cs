using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Commands;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Commands.Commands;

/// <summary>
/// Pins how <c>--touched</c> follows a Proposal's targets through a real Baseline project copy to ids, matches stored
/// analyses by id, names what it could not follow, and bounds what it adds.
/// </summary>
[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group2)]
public sealed class TrialTouchedWordsTests(PristineProjectFixture pristine)
{
    private static OperationEnvelope Operation(Guid target) => new(CanonicalId.Mint("op/"), "lexicon/entry.set",
        target: CanonicalId.FromGuid(target));

    private static Proposal ProposalOf(params Guid[] targets) => new(new Dictionary<string, string> { ["lexicon"] = "1.0" },
        CanonicalId.Mint("proposal/"), null, targets.Select(Operation).ToArray());

    private static AssessmentWordResult Word(string word, string opinion, Guid allomorph) =>
        new(word, "analysed", false, "Search completed", 1, null)
        {
            StoredAnalyses =
            [
                new ParserReading([new ParserReadingMorph("x", "y", "v", null, false, null) { AllomorphId = allomorph.ToString() }])
                {
                    StoredAnalysisId = "analysis-" + word, StoredAnalysisOpinion = opinion,
                },
            ],
        };

    [Fact]
    public void AnEntryExpandsToItsLexemeFormAndMsasASenseToItsMsaAndAFormToItself()
    {
        using var cache = pristine.NewScratch();
        var seed = pristine.Seed;
        var (morphs, infos) = (new HashSet<Guid>(), new HashSet<Guid>());
        Assert.Null(TrialTouchedWords.Expand(cache, seed.FirstEntryId, morphs, infos));
        Assert.Contains(seed.FirstLexemeFormId, morphs);
        Assert.NotEmpty(infos);
        Assert.DoesNotContain(seed.SecondLexemeFormId, morphs);

        var sense = cache.ServiceLocator.GetInstance<ILexSenseRepository>().GetObject(seed.SecondSenseId);
        var (senseMorphs, senseInfos) = (new HashSet<Guid>(), new HashSet<Guid>());
        Assert.Null(TrialTouchedWords.Expand(cache, seed.SecondSenseId, senseMorphs, senseInfos));
        Assert.Empty(senseMorphs);
        Assert.Equal([sense.MorphoSyntaxAnalysisRA.Guid], senseInfos);

        var (formMorphs, formInfos) = (new HashSet<Guid>(), new HashSet<Guid>());
        Assert.Null(TrialTouchedWords.Expand(cache, seed.SecondLexemeFormId, formMorphs, formInfos));
        Assert.Equal([seed.SecondLexemeFormId], formMorphs);
        Assert.Empty(formInfos);
    }

    [Fact]
    public void AnObjectItCannotFollowIsNamedByKindAndAnAbsentOneIsNamedAsAbsent()
    {
        using var cache = pristine.NewScratch();
        var (morphs, infos) = (new HashSet<Guid>(), new HashSet<Guid>());
        var kind = cache.ServiceLocator.ObjectRepository.GetObject(pristine.Seed.PartOfSpeechId).ClassName;
        Assert.Equal(kind, TrialTouchedWords.Expand(cache, pristine.Seed.PartOfSpeechId, morphs, infos));
        Assert.Equal("not in Baseline", TrialTouchedWords.Expand(cache, Guid.NewGuid(), morphs, infos));
        Assert.Empty(morphs);
        Assert.Empty(infos);
    }

    [Fact]
    public void WordsMatchByIdSkipDisapprovedAnalysesAndCountWhatTheBaselineCovers()
    {
        using var cache = pristine.NewScratch();
        var seed = pristine.Seed;
        var words = new[]
        {
            Word("uses", ReadingGrade.Approved, seed.FirstLexemeFormId),
            Word("onlyDisapproved", ReadingGrade.Disapproved, seed.FirstLexemeFormId),
            Word("other", ReadingGrade.Approved, seed.SecondLexemeFormId),
        };
        var found = TrialTouchedWords.Find(ProposalOf(seed.FirstEntryId, seed.PartOfSpeechId), words, 10, cache);
        var touched = found.Value!;
        Assert.Equal(["uses"], touched.Words);
        Assert.Equal(1, touched.Matched);
        Assert.Equal(3, touched.BaselineWords);
        var unexpanded = Assert.Single(touched.Unexpanded);
        Assert.Equal(1, unexpanded.Count);
        var line = touched.Describe();
        Assert.Contains("added 1 word(s)", line);
        Assert.Contains("matched against 3 Baseline word(s) of 10 attested", line);
        Assert.Contains(unexpanded.Kind + " x1", line);
        Assert.Contains("may be incomplete; add --words", line);
    }

    [Fact]
    public void ACoveringBaselineWithEveryTargetFollowedSaysNothingAboutGaps()
    {
        using var cache = pristine.NewScratch();
        var words = new[] { Word("uses", ReadingGrade.Approved, pristine.Seed.FirstLexemeFormId) };
        var line = TrialTouchedWords.Find(ProposalOf(pristine.Seed.FirstEntryId), words, 1, cache).Value!.Describe();
        Assert.DoesNotContain("attested", line);
        Assert.DoesNotContain("incomplete", line);
    }

    [Fact]
    public void ATargetThatNeverReachesWordsIsRefusedAndNamed()
    {
        using var cache = pristine.NewScratch();
        var refused = TrialTouchedWords.Find(ProposalOf(pristine.Seed.PartOfSpeechId),
            [Word("uses", ReadingGrade.Approved, pristine.Seed.FirstLexemeFormId)], null, cache);
        Assert.Equal(RefusalCodes.TrialTouchedNoObjects, refused.Refusal!.Code);
        Assert.Contains("not traced to words", refused.Refusal.Message);
        Assert.Contains("Next:", refused.Refusal.Message);
    }

    [Fact]
    public void WithoutAMatchingBaselineAssessmentTheTrialIsRefused()
    {
        var refused = TrialTouchedWords.Find(ProposalOf(Guid.NewGuid()), null, null, null);
        Assert.Equal(RefusalCodes.TrialTouchedNoBaseline, refused.Refusal!.Code);
        Assert.Contains("Next:", refused.Refusal.Message);
    }

    [Fact]
    public void ThereAreAtMostFiveHundredTouchedWordsAndTheCallersOwnComeFirst()
    {
        using var cache = pristine.NewScratch();
        var words = Enumerable.Range(0, 600).Select(i => Word("w" + i, ReadingGrade.Approved, pristine.Seed.FirstLexemeFormId)).ToArray();
        var touched = TrialTouchedWords.Find(ProposalOf(pristine.Seed.FirstEntryId), words, null, cache).Value!;
        Assert.Equal(TrialTouchedWords.Cap, touched.Words.Count);
        Assert.Equal(600, touched.Matched);
        Assert.Equal(100, touched.NotAdded);
        Assert.Contains("100 more matched and were not added (cap 500)", touched.Describe());

        var merged = TrialTouchedWords.Merge(["mine", "w3", "mine2"], touched.Words);
        Assert.Equal(["mine", "w3", "mine2", "w0", "w1", "w2", "w4"], merged.Take(7));
        Assert.Equal(TrialTouchedWords.Cap + 2, merged.Length);
    }
}
