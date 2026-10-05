using System;
using System.IO;
using System.Linq;
using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.DomainServices;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary>
/// Pins <see cref="TextWordsQuery"/> over a real, file-backed seeded project: the empty response before a
/// Baseline exists, the seeded Text's distinct forms, occurrences and approved analysis once one has been
/// captured, and — over a hand-built second Text — occurrences of the same form carrying different chosen
/// analyses, analyses equal by content despite a different sense, and the project's disapproved analyses.
/// </summary>
[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group1)]
public sealed class TextWordsQueryTests : IDisposable
{
    private readonly PristineProjectFixture _pristine;
    private readonly string _managedRootsParent =
        Path.Combine(Path.GetTempPath(), "SIL.Motif.TextWordsQueryTests", Guid.NewGuid().ToString("N"));

    public TextWordsQueryTests(PristineProjectFixture pristine)
    {
        _pristine = pristine;
        Directory.CreateDirectory(_managedRootsParent);
    }

    public void Dispose()
    {
        try { Directory.Delete(_managedRootsParent, recursive: true); }
        catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public void BeforeAnyCaptureTheResponseIsEmptyWithNoBaseline()
    {
        var fwDataPath = _pristine.CopyProjectFile();

        var outcome = TextWordsQuery.Query(new TextWordsRequest(fwDataPath, []));

        Assert.True(outcome.Succeeded);
        Assert.False(outcome.Value!.HasBaseline);
        Assert.Empty(outcome.Value.Words);
        Assert.Empty(outcome.Value.Texts);
    }

    [Fact]
    public void TheSeededTextsWordsAreDistinctWithOccurrencesAndTheApprovedAnalysis()
    {
        using var cache = _pristine.NewScratch();
        var seededText = SeededProject.SeedText(cache, _pristine.Seed);
        new FwDataProjectLoader().Save(cache);
        var fwDataPath = cache.ProjectId.Path;
        Capture(fwDataPath);

        var outcome = TextWordsQuery.Query(new TextWordsRequest(fwDataPath, [seededText.TextId]));

        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        var response = outcome.Value!;
        Assert.True(response.HasBaseline);
        Assert.Equal(response.Words.Sum(word => word.Occurrences.Count), response.OccurrenceCount);

        // Punctuation contributes no TextWord; only the analysed and unanalysed forms do.
        Assert.Equal(2, response.Words.Count);
        var analysed = Assert.Single(response.Words, word => word.Form == SeededProject.AnalysedWordForm);
        var unanalysed = Assert.Single(response.Words, word => word.Form == SeededProject.UnanalysedWordForm);

        var occurrence = Assert.Single(analysed.Occurrences);
        Assert.Equal("approved", occurrence.Status);
        Assert.NotNull(occurrence.Analysis);
        Assert.Equal(2, occurrence.Analysis!.Morphs.Count);
        Assert.Equal(SeededProject.FirstForm, occurrence.Analysis.Morphs[0].Form);
        Assert.Equal(SeededProject.FirstGloss, occurrence.Analysis.Morphs[0].Gloss);
        Assert.Equal(SeededProject.SecondForm, occurrence.Analysis.Morphs[1].Form);
        Assert.Single(analysed.Approved);
        Assert.Equal(analysed.Approved[0].Key, occurrence.Analysis.Key);
        Assert.Empty(analysed.Disapproved);
        var storedAnalysis = Assert.Single(analysed.Analyses);
        Assert.Equal(CanonicalId.FromGuid(seededText.ApprovedAnalysisId).Value, storedAnalysis.StoredAnalysisId);
        Assert.Equal("approved", storedAnalysis.StoredAnalysisOpinion);
        Assert.Equal(2, storedAnalysis.Identity!.Morphs.Count);
        Assert.False(string.IsNullOrWhiteSpace(storedAnalysis.Morphs[0].Entry));

        var unanalysedOccurrence = Assert.Single(unanalysed.Occurrences);
        Assert.Equal("unanalysed", unanalysedOccurrence.Status);
        Assert.Null(unanalysedOccurrence.Analysis);
        Assert.Empty(unanalysed.Approved);

        var text = Assert.Single(response.Texts);
        Assert.Equal(SeededProject.TextTitle, text.Title);
        Assert.Equal(2, text.Lines.Count);
        Assert.Equal([1, 2], text.Lines.Select(line => line.Number));
        var firstLineTokens = text.Lines[0].Tokens;
        Assert.Equal(2, firstLineTokens.Count);
        Assert.Equal(SeededProject.AnalysedWordForm, firstLineTokens[0].Form);
        Assert.Null(firstLineTokens[1].Form);
        Assert.Equal(SeededProject.PunctuationForm, firstLineTokens[1].Text);

        var word = firstLineTokens[0];
        Assert.Equal(seededText.AnalysedWordformId, word.WordformId);
        Assert.Equal(CanonicalId.FromGuid(seededText.ApprovedAnalysisId).Value, word.StoredAnalysisId);
        Assert.False(text.Lines[0].ParseIsCurrent);
        Assert.Equal(seededText.FirstParagraphId, text.Lines[0].ParagraphId);
        Assert.Equal(seededText.FirstSegmentId, text.Lines[0].SegmentId);
        Assert.Equal(0, word.OccurrenceIndex);
        Assert.Equal(occurrence.Analysis.Key, word.Analysis!.Key);
        Assert.Equal(
            [SeededProject.FirstGloss], word.Analysis.Morphs.Take(1).Select(morph => morph.Gloss));
        Assert.StartsWith("silfw://", word.WordLink, StringComparison.Ordinal);
        Assert.All(word.Analysis.Morphs, morph => Assert.StartsWith("silfw://", morph.FieldWorksLink, StringComparison.Ordinal));
        Assert.Null(firstLineTokens[1].Analysis);
        Assert.Null(firstLineTokens[1].WordLink);
    }

    [Fact]
    public void ASubstantialSeededTextRetainsEveryOccurrenceAndItsLastLocation()
    {
        using var cache = _pristine.NewScratch();
        var seededText = SeededProject.SeedText(cache, _pristine.Seed);
        var workload = SubstantialTextSeed.AppendLines(cache, seededText);
        new FwDataProjectLoader().Save(cache);
        var fwDataPath = cache.ProjectId.Path;
        Capture(fwDataPath);

        var outcome = TextWordsQuery.Query(new TextWordsRequest(fwDataPath, [workload.TextId]));

        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        var response = outcome.Value!;
        Assert.True(response.HasBaseline);
        Assert.Equal(2_402, response.OccurrenceCount);
        Assert.Equal(33, response.Words.Count);
        Assert.All(response.Words.Where(word => word.Form.StartsWith("scale-word-", StringComparison.Ordinal)),
            word => Assert.True(word.Occurrences.Count > 1));
        Assert.Single(response.Words, word => word.Form == SeededProject.UnanalysedWordForm);

        var text = Assert.Single(response.Texts);
        Assert.Equal(workload.TextId, text.TextId);
        Assert.Equal(302, text.Lines.Count);
        Assert.Equal(seededText.FirstParagraphId, text.Lines[0].ParagraphId);
        Assert.Equal(seededText.FirstSegmentId, text.Lines[0].SegmentId);
        Assert.All(text.Lines.Skip(2), line =>
            Assert.Equal(Enumerable.Range(0, SubstantialTextSeed.WordsPerLine),
                line.Tokens.Select(token => token.OccurrenceIndex)));

        var finalLine = text.Lines[^1];
        Assert.Equal(302, finalLine.Number);
        Assert.Equal(workload.LastParagraphId, finalLine.ParagraphId);
        Assert.Equal(workload.LastSegmentId, finalLine.SegmentId);
        var finalOccurrence = finalLine.Tokens[^1];
        Assert.Equal(SeededProject.AnalysedWordForm, finalOccurrence.Form);
        Assert.Equal(seededText.AnalysedWordformId, finalOccurrence.WordformId);
        Assert.Equal(7, finalOccurrence.OccurrenceIndex);
        Assert.Equal(CanonicalId.FromGuid(seededText.ApprovedAnalysisId).Value, finalOccurrence.StoredAnalysisId);

        var firstOccurrence = text.Lines[0].Tokens[0];
        var projectWord = Assert.Single(response.Words,
            word => word.WordformGuid == seededText.AnalysedWordformId.ToString("D"));
        Assert.Same(firstOccurrence.StoredAnalyses, finalOccurrence.StoredAnalyses);
        Assert.Same(firstOccurrence.Analysis, finalOccurrence.Analysis);
        Assert.Same(firstOccurrence.StoredAnalyses, projectWord.Analyses);
        Assert.Same(firstOccurrence.WordLink, finalOccurrence.WordLink);
        Assert.Same(firstOccurrence.Analysis!.Morphs[0].FieldWorksLink, projectWord.Approved[0].Morphs[0].FieldWorksLink);
    }

    [Fact]
    public void LinesAndTokensRetainTheOccurrenceIdentityForAnAnalyzeTextsDecision()
    {
        using var cache = _pristine.NewScratch();
        var seededText = SeededProject.SeedText(cache, _pristine.Seed);
        new FwDataProjectLoader().Save(cache);
        var fwDataPath = cache.ProjectId.Path;
        Capture(fwDataPath);

        var outcome = TextWordsQuery.Query(new TextWordsRequest(fwDataPath, [seededText.TextId]));

        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        var line = Assert.Single(outcome.Value!.Texts).Lines[0];
        var word = line.Tokens.First(token => token.Form is not null);
        Assert.Equal(seededText.FirstParagraphId, line.ParagraphId);
        Assert.Equal(seededText.FirstSegmentId, line.SegmentId);
        Assert.Equal(0, word.OccurrenceIndex);
        Assert.Equal(seededText.AnalysedWordformId, word.WordformId);
    }

    [Fact]
    public void OccurrencesOfTheSameFormKeepTheirOwnAnalysis_KeysAgreeAcrossSenseAndDifferAcrossMorphology()
    {
        using var cache = _pristine.NewScratch();
        var seed = _pristine.Seed;
        var scenario = SeedDualAnalysisText(cache, seed);
        new FwDataProjectLoader().Save(cache);
        var fwDataPath = cache.ProjectId.Path;
        Capture(fwDataPath);

        var outcome = TextWordsQuery.Query(new TextWordsRequest(fwDataPath, [scenario.TextId]));

        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        var word = Assert.Single(outcome.Value!.Words, item => item.Form == DualForm);
        Assert.Equal(3, word.Occurrences.Count);

        // Occurrences 1 and 2 chose analyses that differ only by sense: same key.
        Assert.Equal(word.Occurrences[0].Analysis!.Key, word.Occurrences[1].Analysis!.Key);
        Assert.NotSame(word.Occurrences[0].Analysis, word.Occurrences[1].Analysis);
        Assert.NotEqual(word.Occurrences[0].Analysis!.StoredAnalysisId, word.Occurrences[1].Analysis!.StoredAnalysisId);
        // Occurrence 3 chose a genuinely different morphology: a different key.
        Assert.NotEqual(word.Occurrences[0].Analysis!.Key, word.Occurrences[2].Analysis!.Key);

        // The project approves the first analysis and disapproves the fourth, unused one.
        Assert.Single(word.Approved);
        Assert.Equal(word.Occurrences[0].Analysis!.Key, word.Approved[0].Key);
        Assert.Single(word.Disapproved);
        Assert.NotEqual(word.Approved[0].Key, word.Disapproved[0].Key);

        // The second and third analyses carry no human opinion either way: they are candidates.
        Assert.Equal(2, word.CandidateCount);
        Assert.False(word.IncorrectSpelling);

        Assert.Equal(4, word.Analyses.Count);
        Assert.Equal("approved", Assert.Single(word.Analyses,
            analysis => analysis.StoredAnalysisId == CanonicalId.FromGuid(scenario.ApprovedAnalysisId).Value)
            .StoredAnalysisOpinion);
        Assert.Equal("disapproved", Assert.Single(word.Analyses,
            analysis => analysis.StoredAnalysisId == CanonicalId.FromGuid(scenario.DisapprovedAnalysisId).Value)
            .StoredAnalysisOpinion);
        Assert.Equal(ReadingGrade.Candidate, Assert.Single(word.Analyses,
            analysis => analysis.StoredAnalysisId == CanonicalId.FromGuid(scenario.UnknownSameAnalysisId).Value)
            .StoredAnalysisOpinion);
        Assert.Equal(ReadingGrade.Candidate, Assert.Single(word.Analyses,
            analysis => analysis.StoredAnalysisId == CanonicalId.FromGuid(scenario.UnknownDifferentAnalysisId).Value)
            .StoredAnalysisOpinion);
        Assert.All(word.Analyses, analysis => Assert.NotNull(analysis.Identity));
    }

    [Fact]
    public void AWordformFieldWorksMarksAsMisspelledIsReportedAsIncorrectSpelling()
    {
        using var cache = _pristine.NewScratch();
        var scenario = SeedDualAnalysisText(cache, _pristine.Seed);
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var wordform = cache.ServiceLocator.GetInstance<IWfiWordformRepository>().AllInstances()
                .Single(candidate => candidate.Form.VernacularDefaultWritingSystem.Text == DualForm);
            wordform.SpellingStatus = 2;
        });
        new FwDataProjectLoader().Save(cache);
        var fwDataPath = cache.ProjectId.Path;
        Capture(fwDataPath);

        var outcome = TextWordsQuery.Query(new TextWordsRequest(fwDataPath, [scenario.TextId]));

        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        Assert.True(Assert.Single(outcome.Value!.Words, item => item.Form == DualForm).IncorrectSpelling);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SameSpellingWordformsKeepTheirOwnFactsInEitherEncounterOrder(bool reverse)
    {
        using var cache = _pristine.NewScratch();
        var services = cache.ServiceLocator;
        Guid textId = default, approvedId = default, otherId = default;
        Guid approvedAnalysisId = default, disapprovedAnalysisId = default;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var entry = services.GetInstance<ILexEntryRepository>().GetObject(_pristine.Seed.FirstEntryId);
            var approved = services.GetInstance<IWfiWordformFactory>().Create();
            var other = services.GetInstance<IWfiWordformFactory>().Create();
            approved.Form.set_String(cache.DefaultVernWs, "homograph");
            other.Form.set_String(cache.DefaultVernWs, "homograph");
            other.SpellingStatus = 2;
            var approvedAnalysis = MakeSingleBundleAnalysis(cache, approved, entry.LexemeFormOA!,
                entry.MorphoSyntaxAnalysesOC.First(), entry.SensesOS[0]);
            var disapprovedAnalysis = MakeSingleBundleAnalysis(cache, other, entry.LexemeFormOA!,
                entry.MorphoSyntaxAnalysesOC.First(), entry.SensesOS[0]);
            MakeSingleBundleAnalysis(cache, other, entry.LexemeFormOA!,
                entry.MorphoSyntaxAnalysesOC.First(), entry.SensesOS[0]);
            cache.LangProject.DefaultUserAgent.SetEvaluation(approvedAnalysis, Opinions.approves);
            cache.LangProject.DefaultUserAgent.SetEvaluation(disapprovedAnalysis, Opinions.disapproves);
            var text = services.GetInstance<ITextFactory>().Create();
            text.Name.set_String(cache.DefaultAnalWs, "Homographs");
            var contents = services.GetInstance<IStTextFactory>().Create();
            text.ContentsOA = contents;
            foreach (var analysis in reverse ? new[] { disapprovedAnalysis, approvedAnalysis }
                         : new[] { approvedAnalysis, disapprovedAnalysis })
                AddOneWordLine(cache, contents, "homograph", cache.DefaultVernWs, analysis);
            (textId, approvedId, otherId) = (text.Guid, approved.Guid, other.Guid);
            (approvedAnalysisId, disapprovedAnalysisId) = (approvedAnalysis.Guid, disapprovedAnalysis.Guid);
        });
        new FwDataProjectLoader().Save(cache);
        Capture(cache.ProjectId.Path);

        var outcome = TextWordsQuery.Query(new TextWordsRequest(cache.ProjectId.Path, [textId]));

        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        var words = outcome.Value!.Words;
        Assert.Equal(2, words.Count);
        Assert.All(words, word => Assert.Equal("homograph", word.Form));
        var own = Assert.Single(words, word => word.WordformGuid == approvedId.ToString("D"));
        Assert.Equal(CanonicalId.FromGuid(approvedAnalysisId).Value, Assert.Single(own.Approved).StoredAnalysisId);
        Assert.Empty(own.Disapproved);
        Assert.False(own.IncorrectSpelling);
        Assert.Equal(0, own.CandidateCount);
        Assert.Single(own.Occurrences);
        var otherWord = Assert.Single(words, word => word.WordformGuid == otherId.ToString("D"));
        Assert.Empty(otherWord.Approved);
        Assert.Equal(CanonicalId.FromGuid(disapprovedAnalysisId).Value,
            Assert.Single(otherWord.Disapproved).StoredAnalysisId);
        Assert.True(otherWord.IncorrectSpelling);
        Assert.Equal(1, otherWord.CandidateCount);
        Assert.Single(otherWord.Occurrences);
        Assert.Equal(2, outcome.Value.OccurrenceCount);
        var tokens = Assert.Single(outcome.Value.Texts).Lines.SelectMany(line => line.Tokens).ToArray();
        var approvedToken = Assert.Single(tokens, token => token.WordformId == approvedId);
        var disapprovedToken = Assert.Single(tokens, token => token.WordformId == otherId);
        Assert.Equal(CanonicalId.FromGuid(approvedAnalysisId).Value, approvedToken.Analysis!.StoredAnalysisId);
        Assert.Equal(ReadingGrade.Approved, approvedToken.Analysis.StoredAnalysisOpinion);
        Assert.Equal(CanonicalId.FromGuid(approvedId).Value, approvedToken.Analysis.Identity!.SourceWordformGuid);
        Assert.Equal(CanonicalId.FromGuid(disapprovedAnalysisId).Value, disapprovedToken.Analysis!.StoredAnalysisId);
        Assert.Equal(ReadingGrade.Disapproved, disapprovedToken.Analysis.StoredAnalysisOpinion);
        Assert.Equal(CanonicalId.FromGuid(otherId).Value, disapprovedToken.Analysis.Identity!.SourceWordformGuid);
    }

    private const string DualForm = "dualword";

    // Three occurrences of one wordform: the first two differ only by sense; the third differs by MSA.
    private DualAnalysisScenario SeedDualAnalysisText(LcmCache cache, SeededProject seed)
    {
        var services = cache.ServiceLocator;
        var vernWs = cache.DefaultVernWs;
        var analWs = cache.DefaultAnalWs;
        var entryRepository = services.GetInstance<ILexEntryRepository>();
        var firstEntry = entryRepository.GetObject(seed.FirstEntryId);
        var secondEntry = entryRepository.GetObject(seed.SecondEntryId);
        var firstMsa = firstEntry.MorphoSyntaxAnalysesOC.First();

        IText text = null!;
        Guid textId = default;
        IWfiAnalysis sameKeyFirst = null!;
        IWfiAnalysis sameKeySecond = null!;
        IWfiAnalysis differentKey = null!;
        IWfiAnalysis unused = null!;

        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var secondSense = services.GetInstance<ILexSenseFactory>().Create();
            firstEntry.SensesOS.Add(secondSense);
            secondSense.Gloss.set_String(analWs, "second sense on the same entry");
            secondSense.MorphoSyntaxAnalysisRA = firstMsa;

            var wordform = services.GetInstance<IWfiWordformFactory>()
                .Create(TsStringUtils.MakeString(DualForm, vernWs));

            sameKeyFirst = MakeSingleBundleAnalysis(cache, wordform, firstEntry.LexemeFormOA!, firstMsa, firstEntry.SensesOS[0]);
            sameKeySecond = MakeSingleBundleAnalysis(cache, wordform, firstEntry.LexemeFormOA!, firstMsa, secondSense);
            differentKey = MakeSingleBundleAnalysis(
                cache, wordform, secondEntry.LexemeFormOA!, secondEntry.MorphoSyntaxAnalysesOC.First(), secondEntry.SensesOS[0]);
            unused = MakeSingleBundleAnalysis(
                cache, wordform, secondEntry.LexemeFormOA!, secondEntry.MorphoSyntaxAnalysesOC.First(), null);

            cache.LangProject.DefaultUserAgent.SetEvaluation(sameKeyFirst, Opinions.approves);
            cache.LangProject.DefaultUserAgent.SetEvaluation(unused, Opinions.disapproves);

            text = services.GetInstance<ITextFactory>().Create();
            text.Name.set_String(analWs, "Dual Analysis Text");
            var contents = services.GetInstance<IStTextFactory>().Create();
            text.ContentsOA = contents;

            AddOneWordLine(cache, contents, DualForm, vernWs, sameKeyFirst);
            AddOneWordLine(cache, contents, DualForm, vernWs, sameKeySecond);
            AddOneWordLine(cache, contents, DualForm, vernWs, differentKey);

            textId = text.Guid;
        });

        return new DualAnalysisScenario(textId, sameKeyFirst.Guid, unused.Guid, sameKeySecond.Guid, differentKey.Guid);
    }

    private static IWfiAnalysis MakeSingleBundleAnalysis(
        LcmCache cache, IWfiWordform wordform, IMoForm morph, IMoMorphSynAnalysis msa, ILexSense? sense)
    {
        var services = cache.ServiceLocator;
        var analysis = services.GetInstance<IWfiAnalysisFactory>().Create();
        wordform.AnalysesOC.Add(analysis);
        var bundle = services.GetInstance<IWfiMorphBundleFactory>().Create();
        analysis.MorphBundlesOS.Add(bundle);
        bundle.MorphRA = morph;
        bundle.MsaRA = msa;
        if (sense is not null) bundle.SenseRA = sense;
        return analysis;
    }

    private static void AddOneWordLine(
        LcmCache cache, IStText contents, string form, int vernWs, IWfiAnalysis analysis)
    {
        var services = cache.ServiceLocator;
        var paragraph = services.GetInstance<IStTxtParaFactory>().Create();
        contents.ParagraphsOS.Add(paragraph);
        paragraph.Contents = TsStringUtils.MakeString(form, vernWs);
        var segment = services.GetInstance<ISegmentFactory>().Create();
        paragraph.SegmentsOS.Add(segment);
        segment.AnalysesRS.Add(analysis);
    }

    private sealed record DualAnalysisScenario(Guid TextId, Guid ApprovedAnalysisId, Guid DisapprovedAnalysisId,
        Guid UnknownSameAnalysisId, Guid UnknownDifferentAnalysisId);

    private void Capture(string fwDataPath)
    {
        var captured = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(fwDataPath), NewManagedRoot());
        Assert.True(captured.Succeeded, captured.Refusal?.Message);
    }

    private string NewManagedRoot()
    {
        var root = Path.Combine(_managedRootsParent, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
