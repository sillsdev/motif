using System.IO;
using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Analysis;
using SIL.Motif.Host.Corpus;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.Parser;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Store;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands;

[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group2)]
public sealed class EvidenceQueryBaselineTests : IDisposable
{
    private readonly PristineProjectFixture _pristine;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "SIL.Motif.EvidenceQueryBaselineTests",
        Guid.NewGuid().ToString("N"));

    public EvidenceQueryBaselineTests(PristineProjectFixture pristine)
    {
        _pristine = pristine;
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public void UsesAndStoredReadingsRemainReadableWhileFieldWorksHoldsTheSource()
    {
        var project = Capture();
        var selected = SelectionCommands.SetDefault(new SetDefaultSelectionRequest(
            project.Path, "Default", [project.TextId], []));
        Assert.True(selected.Succeeded, selected.Refusal?.Message);
        RecordAssessment(project, [SeededProject.AnalysedWordForm, SeededProject.UnanalysedWordForm]);
        var stamp = File.GetLastWriteTimeUtc(project.Path);
        using var held = new FileStream(project.Path + ".lock", FileMode.Create, FileAccess.ReadWrite, FileShare.None);

        var uses = ObjectUsesQuery.Query(new ObjectUsesRequest(project.Path,
            new ObjectUseRef { AllomorphId = _pristine.Seed.FirstLexemeFormId.ToString("D") }));
        var evidence = CurrentEvidenceQuery.ReadCurrentEvidence(project.Path);

        Assert.True(uses.Succeeded, uses.Refusal?.Message);
        Assert.Equal(SeededProject.AnalysedWordForm, Assert.Single(uses.Value!.Uses!.Words).Row.Word);
        Assert.True(evidence.Succeeded, evidence.Refusal?.Message);
        var reading = Assert.Single(evidence.Value!.Assessment!.Words
            .Single(word => word.Word == SeededProject.AnalysedWordForm).Readings!);
        Assert.Equal(SeededProject.FirstGloss, reading.Morphs[0].Gloss);
        Assert.Contains(Path.GetFileNameWithoutExtension(project.Path), reading.Morphs[0].FieldWorksLink!);
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(project.Path));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AddedWordsKeepBaselineAnalysesOutsideTheChosenTexts(bool chooseOtherText)
    {
        var project = Capture(analysedWordOutsideText: chooseOtherText);
        var selected = SelectionCommands.SetDefault(new SetDefaultSelectionRequest(project.Path, "Default",
            chooseOtherText ? [project.TextId] : [], [SeededProject.AnalysedWordForm]));
        Assert.True(selected.Succeeded, selected.Refusal?.Message);
        RecordAssessment(project, chooseOtherText
            ? [SeededProject.AnalysedWordForm, SeededProject.UnanalysedWordForm]
            : [SeededProject.AnalysedWordForm], parsed: false);
        using var held = new FileStream(project.Path + ".lock", FileMode.Create, FileAccess.ReadWrite, FileShare.None);

        var evidence = CurrentEvidenceQuery.ReadCurrentEvidence(project.Path);
        Assert.True(evidence.Succeeded, evidence.Refusal?.Message);
        var stored = Assert.Single(evidence.Value!.Assessment!.Words
            .Single(word => word.Word == SeededProject.AnalysedWordForm).StoredAnalyses);
        Assert.Equal(ReadingGrade.Approved, stored.StoredAnalysisOpinion);
        Assert.NotNull(stored.StoredAnalysisId);
        Assert.Equal(_pristine.Seed.FirstLexemeFormId.ToString("D"), stored.Morphs[0].AllomorphId);
        Assert.Equal(stored.StoredAnalysisId, stored.Identity!.SourceAnalysisId);
        var uses = ObjectUsesQuery.Query(new ObjectUsesRequest(project.Path,
            new ObjectUseRef { AllomorphId = _pristine.Seed.FirstLexemeFormId.ToString("D") }));
        Assert.True(uses.Succeeded, uses.Refusal?.Message);
        Assert.Equal(SeededProject.AnalysedWordForm, Assert.Single(uses.Value!.Uses!.Words).Row.Word);

        var checkedNow = GrammarCheckQuery.Query(new GrammarCheckRequest(project.Path), new FakeInvoker
        {
            Respond = _ => new PanGlossOutcome.Completed(GrammarHealthReports.With(
                ("allomorph", [new("MoForm", "motifa", _pristine.Seed.FirstLexemeFormId)])),
                string.Empty, TimeSpan.Zero),
        }, CancellationToken.None);
        Assert.True(checkedNow.Succeeded, checkedNow.Refusal?.Message);
        Assert.Equal(SeededProject.AnalysedWordForm,
            Assert.Single(Assert.Single(checkedNow.Value!.Findings).YourWords!.Words).Row.Word);
        var overview = SIL.Motif.Commands.Catalog.OverviewCommand.Overview(new OverviewRequest(project.Path));
        Assert.True(overview.Succeeded, overview.Refusal?.Message);
        Assert.Equal(1, overview.Value!.Warnings!.YourWords!.Words);
    }

    [Fact]
    public void StaleSourceKeepsTheAssessmentsBaselineReadings()
    {
        var project = Capture();
        Assert.True(SelectionCommands.SetDefault(new SetDefaultSelectionRequest(
            project.Path, "Default", [], [SeededProject.AnalysedWordForm])).Succeeded);
        RecordAssessment(project, [SeededProject.AnalysedWordForm]);
        File.WriteAllText(project.Path, File.ReadAllText(project.Path)
            .Replace(SeededProject.FirstGloss, "changed live gloss", StringComparison.Ordinal));
        File.SetLastWriteTimeUtc(project.Path, project.Baseline.SourceLastWriteUtc.UtcDateTime.AddMinutes(1));
        using var held = new FileStream(project.Path + ".lock", FileMode.Create, FileAccess.ReadWrite, FileShare.None);

        var evidence = CurrentEvidenceQuery.ReadCurrentEvidence(project.Path);

        Assert.True(evidence.Succeeded, evidence.Refusal?.Message);
        Assert.Equal(EvidenceFreshness.Stale, evidence.Value!.Freshness);
        var row = Assert.Single(evidence.Value.Assessment!.Words);
        Assert.Equal(SeededProject.FirstGloss, Assert.Single(row.Readings!).Morphs[0].Gloss);
        Assert.Equal(SeededProject.FirstGloss, Assert.Single(row.StoredAnalyses).Morphs[0].Gloss);
    }

    [Fact]
    public void MissingAssessmentBaselineRefusesInsteadOfDroppingReadings()
    {
        var project = Capture();
        Assert.True(SelectionCommands.SetDefault(new SetDefaultSelectionRequest(
            project.Path, "Default", [], [SeededProject.AnalysedWordForm])).Succeeded);
        RecordAssessment(project, [SeededProject.AnalysedWordForm]);
        File.Delete(project.Baseline.FwDataPath);

        var evidence = CurrentEvidenceQuery.ReadCurrentEvidence(project.Path);

        Assert.False(evidence.Succeeded);
        Assert.Equal("current-evidence.baseline-unavailable", evidence.Refusal!.Code);
    }

    private CapturedProject Capture(bool analysedWordOutsideText = false)
    {
        string path;
        Guid textId;
        ParseWordEvidence morphology;
        using (var cache = _pristine.NewScratch())
        {
            var text = SeededProject.SeedText(cache, _pristine.Seed);
            textId = text.TextId;
            if (analysedWordOutsideText)
                NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
                    cache.ServiceLocator.GetInstance<ITextRepository>().GetObject(text.TextId)
                        .ContentsOA.ParagraphsOS.RemoveAt(0));
            var approved = Assert.Single(ApprovedMorphologyReader.Read(cache)[SeededProject.AnalysedWordForm]);
            morphology = new ParseWordEvidence(ParseMorphEvidence.Schema, 0, SeededProject.AnalysedWordForm, 1,
                false, false, false, [new ParseAnalysis(approved.Morphs.Select(morph =>
                    new ParseMorph(morph.Form, morph.Msa, morph.InflType, null)).ToArray())], []);
            new FwDataProjectLoader().Save(cache);
            path = cache.ProjectId.Path;
        }
        var captured = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(path), _root);
        Assert.True(captured.Succeeded, captured.Refusal?.Message);
        return new CapturedProject(path, textId, captured.Value!, morphology);
    }

    private static void RecordAssessment(CapturedProject project, IReadOnlyList<string> words, bool parsed = true,
        string id = "assessment")
    {
        using var database = ProjectMotifDatabase.Open(project.Path);
        new AssessmentRepository(database).Record(new NewAssessmentRecord(
            id, null, null, "pangloss", AssessmentKinds.ParseTime, "{}", "sha256:scope",
            "whitespace", "1", JsonSerializer.Serialize(project.Baseline.Token, MotifJson.CreateOptions()),
            Selection.Create("Default", words), "sha256:outcome", "sha256:semantic", "sha256:grammar",
            "fingerprint", "pipeline", 0, words.Select(word => new AssessedWord(word,
                parsed ? "analysed" : "no-analysis", [], 1)
            {
                ProjectStanding = ProjectStanding.Approved,
                Morphology = parsed && word == SeededProject.AnalysedWordForm ? project.Morphology : null,
            }).ToArray()));
    }

    private sealed record CapturedProject(string Path, Guid TextId, BaselineCaptureResponse Baseline,
        ParseWordEvidence Morphology);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
