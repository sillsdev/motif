using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Texts;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Projects;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary>
/// Pins that <see cref="TextWordsQuery"/> answers from the words stored with the current Baseline: once the
/// managed Baseline copy is moved away, the query still returns the response it gave before, whether that
/// Baseline came from interactive capture or from the runner's refresh. It also pins the stored words against the
/// live readers over the same Baseline bytes: forms, canonicalization, word glosses, categories, morphs and links.
/// </summary>
[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group2)]
public sealed class TextWordsReadNoProjectFileTests : IDisposable
{
    private readonly PristineProjectFixture _pristine;
    private readonly string _managedRootsParent =
        Path.Combine(Path.GetTempPath(), "SIL.Motif.TextWordsReadNoProjectFileTests", Guid.NewGuid().ToString("N"));

    public TextWordsReadNoProjectFileTests(PristineProjectFixture pristine)
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
    public void CapturedTextWordsAreReadWithoutTheManagedBaselineProjectFile()
    {
        using var cache = _pristine.NewScratch();
        var firstText = SeededProject.SeedText(cache, _pristine.Seed);
        var secondText = SeededProject.SeedText(cache, _pristine.Seed);
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var secondSegment = cache.ServiceLocator.GetInstance<ISegmentRepository>()
                .GetObject(secondText.FirstSegmentId);
            var sharedAnalysis = cache.ServiceLocator.GetInstance<IWfiAnalysisRepository>()
                .GetObject(firstText.ApprovedAnalysisId);
            secondSegment.AnalysesRS[0] = sharedAnalysis;
        });
        secondText = secondText with { AnalysedWordformId = firstText.AnalysedWordformId };
        new FwDataProjectLoader().Save(cache);
        var fwDataPath = cache.ProjectId.Path;
        var captured = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(fwDataPath), NewManagedRoot());
        Assert.True(captured.Succeeded, captured.Refusal?.Message);
        var request = new TextWordsRequest(fwDataPath, [secondText.TextId, firstText.TextId]);

        var before = TextWordsQuery.Query(request);

        Assert.True(before.Succeeded, before.Refusal?.Message);
        var merged = Assert.Single(before.Value!.Words, word => word.Form == SeededProject.AnalysedWordForm);
        Assert.Equal(secondText.AnalysedWordformId, Guid.Parse(merged.WordformGuid!));
        Assert.Equal([secondText.TextId, firstText.TextId], merged.Occurrences.Select(occurrence => occurrence.TextId));
        Assert.Equal([secondText.TextId, firstText.TextId], before.Value.Texts.Select(text => text.TextId));
        AssertSameResponseWithoutFile(captured.Value!.FwDataPath, request, before.Value);
    }

    [Fact]
    public void RefreshedTextWordsReplaceCapturedOnesAndAreReadWithoutTheManagedBaselineProjectFile()
    {
        using var cache = _pristine.NewScratch();
        var firstText = SeededProject.SeedText(cache, _pristine.Seed);
        new FwDataProjectLoader().Save(cache);
        var fwDataPath = cache.ProjectId.Path;
        var captured = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(fwDataPath), NewManagedRoot());
        Assert.True(captured.Succeeded, captured.Refusal?.Message);
        var secondText = SeededProject.SeedText(cache, _pristine.Seed);
        new FwDataProjectLoader().Save(cache);
        var request = new TextWordsRequest(fwDataPath, [firstText.TextId, secondText.TextId]);
        Assert.Equal([firstText.TextId],
            TextWordsQuery.Query(request).Value!.Texts.Select(text => text.TextId));

        var refreshRoot = NewManagedRoot();
        var refreshed = ProjectStoreCommand.Run(fwDataPath, MotifProductVersion.CurrentText, (database, project) =>
        {
            var token = new BaselineRefresh(new BaselineRepository(database), refreshRoot)
                .RefreshAsync(cache, project, CancellationToken.None).GetAwaiter().GetResult();
            var current = new BaselineRepository(database).GetCurrent(ProjectWorkspaceKey.Compute(project))!;
            Assert.Equal(token.BundleDigest, current.Token.BundleDigest);
            return CommandOutcome<BaselineRecord>.Success(current);
        });
        Assert.True(refreshed.Succeeded, refreshed.Refusal?.Message);
        Assert.NotEqual(captured.Value!.FwDataPath, refreshed.Value!.FwDataPath);

        var before = TextWordsQuery.Query(request);

        Assert.True(before.Succeeded, before.Refusal?.Message);
        Assert.Equal([firstText.TextId, secondText.TextId], before.Value!.Texts.Select(text => text.TextId));
        AssertSameResponseWithoutFile(refreshed.Value.FwDataPath, request, before.Value);
    }

    [Fact]
    public void StoredTextWordsMatchWhatTheLiveReadersSayAboutTheSameBaseline()
    {
        using var cache = _pristine.NewScratch();
        var scenario = SeedOracleText(cache, _pristine.Seed);
        new FwDataProjectLoader().Save(cache);
        var fwDataPath = cache.ProjectId.Path;
        var projectName = Path.GetFileNameWithoutExtension(fwDataPath);
        var captured = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(fwDataPath), NewManagedRoot());
        Assert.True(captured.Succeeded, captured.Refusal?.Message);

        var outcome = TextWordsQuery.Query(new TextWordsRequest(fwDataPath, [scenario.TextId]));

        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        var response = outcome.Value!;
        using var baseline = new FwDataProjectLoader().LoadScratchCache(captured.Value!.FwDataPath);
        var objects = baseline.ServiceLocator.ObjectRepository;
        var wordform = (IWfiWordform)objects.GetObject(scenario.WordformId);
        var analysis = (IWfiAnalysis)objects.GetObject(scenario.AnalysisId);
        var rawForms = wordform.Form.AvailableWritingSystemIds.OrderBy(ws => ws)
            .Select(ws => wordform.Form.get_String(ws).Text).Where(text => !string.IsNullOrEmpty(text)).ToArray();
        var canonical = rawForms.Select(raw => raw.Trim().Normalize(NormalizationForm.FormD)).ToArray();
        var expectedMorphs = ParserReadingReader.ReadMorphs(baseline, projectName, analysis.MorphBundlesOS
            .Select(bundle => new ParseMorph(bundle.MorphRA?.Guid.ToString("D"), bundle.MsaRA?.Guid.ToString("D"),
                bundle.InflTypeRA?.Guid.ToString("D"), GuessedString: null)).ToArray());

        Assert.Equal(2, rawForms.Length);
        Assert.Contains(PrecomposedForm.Trim().Normalize(NormalizationForm.FormD), canonical);
        Assert.DoesNotContain(PrecomposedForm, canonical);
        Assert.Equal([.. canonical, PlainForm], response.Words.Select(word => word.Form));
        Assert.All(response.Words.Take(2), word => Assert.Equal(scenario.WordformId.ToString("D"), word.WordformGuid));

        var line = Assert.Single(Assert.Single(response.Texts).Lines, candidate => candidate.Tokens.Count > 0);
        var glossed = line.Tokens[0];
        Assert.Equal(rawForms[0], glossed.Text);
        Assert.Equal(canonical[0], glossed.Form);
        Assert.Equal(InterlinearAnalysisStatus.Approved, glossed.Status);
        Assert.Equal(WordGlossText, glossed.WordGloss);
        Assert.Equal(CategoryAbbreviation, glossed.Category);
        Assert.Equal(FieldWorksLinks.For(baseline, projectName, wordform), glossed.WordLink);
        Assert.Equal(JsonSerializer.Serialize(expectedMorphs), JsonSerializer.Serialize(glossed.Analysis!.Morphs));
        Assert.NotNull(expectedMorphs[1].FieldWorksLink);
        Assert.Equal(analysis.MorphBundlesOS.Select(bundle => bundle.MorphRA?.Guid.ToString("D")),
            glossed.Analysis.Morphs.Select(morph => morph.AllomorphId));
        Assert.Equal(analysis.MorphBundlesOS.Select(bundle => bundle.MsaRA?.Guid.ToString("D")),
            glossed.Analysis.Morphs.Select(morph => morph.GrammaticalInfoId));
        Assert.Equal(string.Join(" ", expectedMorphs.Select(morph => morph.Gloss.Length == 0 ? "?" : morph.Gloss)),
            glossed.Gloss);

        var plain = line.Tokens[1];
        var plainLink = FieldWorksLinks.ForWordform(baseline, projectName, PlainForm);
        Assert.NotNull(plainLink);
        Assert.Equal(plainLink, plain.WordLink);
        Assert.Equal(InterlinearAnalysisStatus.Unanalysed, plain.Status);

        var approved = Assert.Single(response.Words[0].Approved);
        Assert.Equal(JsonSerializer.Serialize(expectedMorphs), JsonSerializer.Serialize(approved.Morphs));
        Assert.Equal(glossed.Analysis.Key, approved.Key);
    }

    [Fact]
    public void AWordLinksItsOwnWordformEvenWhenAnotherIsSpelledTheSameInTheDefaultVernacular()
    {
        using var cache = _pristine.NewScratch();
        var services = cache.ServiceLocator;
        var secondVernWs = services.WritingSystemManager.Get(NewLangProjFixture.SecondVernacularTag).Handle;
        Guid textId = default, ownId = default, namesakeId = default;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var own = services.GetInstance<IWfiWordformFactory>().Create();
            own.Form.set_String(secondVernWs, SharedSpelling);
            var namesake = services.GetInstance<IWfiWordformFactory>()
                .Create(TsStringUtils.MakeString(SharedSpelling, cache.DefaultVernWs));
            var text = services.GetInstance<ITextFactory>().Create();
            text.Name.set_String(cache.DefaultAnalWs, "Namesake Text");
            var contents = services.GetInstance<IStTextFactory>().Create();
            text.ContentsOA = contents;
            var paragraph = services.GetInstance<IStTxtParaFactory>().Create();
            contents.ParagraphsOS.Add(paragraph);
            paragraph.Contents = TsStringUtils.MakeString(SharedSpelling, secondVernWs);
            var segment = services.GetInstance<ISegmentFactory>().Create();
            paragraph.SegmentsOS.Add(segment);
            segment.AnalysesRS.Add(own);
            (textId, ownId, namesakeId) = (text.Guid, own.Guid, namesake.Guid);
        });
        new FwDataProjectLoader().Save(cache);
        var fwDataPath = cache.ProjectId.Path;
        var captured = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(fwDataPath), NewManagedRoot());
        Assert.True(captured.Succeeded, captured.Refusal?.Message);

        var outcome = TextWordsQuery.Query(new TextWordsRequest(fwDataPath, [textId]));

        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        var token = Assert.Single(Assert.Single(outcome.Value!.Texts).Lines.SelectMany(line => line.Tokens));
        Assert.Equal(SharedSpelling, token.Text);
        Assert.NotNull(token.WordLink);
        Assert.Contains(ownId.ToString("D"), Uri.UnescapeDataString(token.WordLink!), StringComparison.Ordinal);
        Assert.DoesNotContain(namesakeId.ToString("D"), Uri.UnescapeDataString(token.WordLink!), StringComparison.Ordinal);
    }

    private const string SharedSpelling = "namesake";
    private const string PrecomposedForm = " motiéa";
    private const string SecondAlternativeForm = "motieb";
    private const string PlainForm = "plainword";
    private const string WordGlossText = "word gloss";
    private const string CategoryAbbreviation = "orcl";

    private sealed record OracleScenario(Guid TextId, Guid WordformId, Guid AnalysisId);

    // One line: a two-spelling word chosen by an IWfiGloss (whole and MSA-only morphs), then a bare word.
    private static OracleScenario SeedOracleText(LcmCache cache, SeededProject seed)
    {
        var services = cache.ServiceLocator;
        var vernWs = cache.DefaultVernWs;
        var secondVernWs = services.WritingSystemManager.Get(NewLangProjFixture.SecondVernacularTag).Handle;
        var analWs = cache.DefaultAnalWs;
        var entries = services.GetInstance<ILexEntryRepository>();
        var firstEntry = entries.GetObject(seed.FirstEntryId);
        var secondEntry = entries.GetObject(seed.SecondEntryId);
        OracleScenario scenario = null!;

        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var category = services.GetInstance<IPartOfSpeechFactory>().Create();
            cache.LangProject.PartsOfSpeechOA.PossibilitiesOS.Add(category);
            category.Abbreviation.set_String(analWs, CategoryAbbreviation);

            var wordform = services.GetInstance<IWfiWordformFactory>()
                .Create(TsStringUtils.MakeString(PrecomposedForm, vernWs));
            wordform.Form.set_String(secondVernWs, SecondAlternativeForm);
            var analysis = services.GetInstance<IWfiAnalysisFactory>().Create();
            wordform.AnalysesOC.Add(analysis);
            analysis.CategoryRA = category;
            var full = services.GetInstance<IWfiMorphBundleFactory>().Create();
            analysis.MorphBundlesOS.Add(full);
            full.MorphRA = firstEntry.LexemeFormOA;
            full.MsaRA = firstEntry.MorphoSyntaxAnalysesOC.First();
            full.SenseRA = firstEntry.SensesOS[0];
            var msaOnly = services.GetInstance<IWfiMorphBundleFactory>().Create();
            analysis.MorphBundlesOS.Add(msaOnly);
            msaOnly.MsaRA = secondEntry.MorphoSyntaxAnalysesOC.First();
            var gloss = services.GetInstance<IWfiGlossFactory>().Create();
            analysis.MeaningsOC.Add(gloss);
            gloss.Form.set_String(analWs, WordGlossText);
            cache.LangProject.DefaultUserAgent.SetEvaluation(analysis, Opinions.approves);
            var plain = services.GetInstance<IWfiWordformFactory>().Create(TsStringUtils.MakeString(PlainForm, vernWs));

            var text = services.GetInstance<ITextFactory>().Create();
            text.Name.set_String(analWs, "Oracle Text");
            var contents = services.GetInstance<IStTextFactory>().Create();
            text.ContentsOA = contents;
            var paragraph = services.GetInstance<IStTxtParaFactory>().Create();
            contents.ParagraphsOS.Add(paragraph);
            paragraph.Contents = TsStringUtils.MakeString(PrecomposedForm.Trim() + " " + PlainForm, vernWs);
            var segment = services.GetInstance<ISegmentFactory>().Create();
            paragraph.SegmentsOS.Add(segment);
            segment.AnalysesRS.Add(gloss);
            segment.AnalysesRS.Add(plain);
            scenario = new OracleScenario(text.Guid, wordform.Guid, analysis.Guid);
        });

        return scenario;
    }

    private static void AssertSameResponseWithoutFile(
        string baselineFwDataPath, TextWordsRequest request, TextWordsResponse expected)
    {
        var moved = baselineFwDataPath + ".unavailable";
        File.Move(baselineFwDataPath, moved);
        try
        {
            var after = TextWordsQuery.Query(request);

            Assert.True(after.Succeeded, after.Refusal?.Message);
            Assert.Equal(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(after.Value));
        }
        finally
        {
            File.Move(moved, baselineFwDataPath);
        }
    }

    private string NewManagedRoot()
    {
        var root = Path.Combine(_managedRootsParent, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
