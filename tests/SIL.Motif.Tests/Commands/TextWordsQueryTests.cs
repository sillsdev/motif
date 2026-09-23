using System;
using System.IO;
using System.Linq;
using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.DomainServices;
using SIL.LCModel.Infrastructure;
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
[Collection(LcmCacheTestCollection.Name)]
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
        Assert.Equal(occurrence.Analysis.Key, word.Analysis!.Key);
        Assert.Equal(
            [SeededProject.FirstGloss], word.Analysis.Morphs.Take(1).Select(morph => morph.Gloss));
        Assert.StartsWith("silfw://", word.WordLink, StringComparison.Ordinal);
        Assert.All(word.Analysis.Morphs, morph => Assert.StartsWith("silfw://", morph.FieldWorksLink, StringComparison.Ordinal));
        Assert.Null(firstLineTokens[1].Analysis);
        Assert.Null(firstLineTokens[1].WordLink);
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

        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var secondSense = services.GetInstance<ILexSenseFactory>().Create();
            firstEntry.SensesOS.Add(secondSense);
            secondSense.Gloss.set_String(analWs, "second sense on the same entry");
            secondSense.MorphoSyntaxAnalysisRA = firstMsa;

            var wordform = services.GetInstance<IWfiWordformFactory>()
                .Create(TsStringUtils.MakeString(DualForm, vernWs));

            var sameKeyFirst = MakeSingleBundleAnalysis(cache, wordform, firstEntry.LexemeFormOA!, firstMsa, firstEntry.SensesOS[0]);
            var sameKeySecond = MakeSingleBundleAnalysis(cache, wordform, firstEntry.LexemeFormOA!, firstMsa, secondSense);
            var differentKey = MakeSingleBundleAnalysis(
                cache, wordform, secondEntry.LexemeFormOA!, secondEntry.MorphoSyntaxAnalysesOC.First(), secondEntry.SensesOS[0]);
            var unused = MakeSingleBundleAnalysis(
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

        return new DualAnalysisScenario(textId);
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

    private sealed record DualAnalysisScenario(Guid TextId);

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
