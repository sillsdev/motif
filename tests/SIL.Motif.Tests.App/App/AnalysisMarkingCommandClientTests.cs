using System.Text.Json;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Assess;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.Parser;
using SIL.Motif.Host.Store;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;
using SIL.LCModel;
using SIL.LCModel.Infrastructure;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(LcmCacheTestCollection.Name)]
public sealed class AnalysisMarkingCommandClientTests(PristineProjectFixture pristine)
{
    [Fact]
    public async Task RemoveAnalysisUsesTheRealCommandClientAndStagesTheExactStoredAnalysis()
    {
        using var project = await CreateProjectAsync();
        var token = AnalyzedToken(project);
        var stored = Assert.Single(token.StoredAnalyses);
        var loaded = await project.Client.LoadPendingChangesAsync(new PendingChangesRequest(
            project.Project.FwDataPath, MotifProductVersion.CurrentText), CancellationToken.None);
        Assert.True(loaded.Succeeded, loaded.Refusal?.Message);

        var result = await project.Client.RemoveAnalysisAsync(new RemoveAnalysisRequest(
            project.Project.FwDataPath, MotifProductVersion.CurrentText, loaded.Value!.Revision,
            ChangeId: CanonicalId.Mint().Value, WordformId: CanonicalId.FromGuid(token.WordformId!.Value).Value,
            Word: token.Form!, AnalysisId: stored.StoredAnalysisId), CancellationToken.None);

        Assert.True(result.Succeeded, result.Refusal?.Message);
        var change = Assert.Single(result.Value!.Changes);
        Assert.Equal("remove-analysis", change.Kind);
        Assert.Equal(stored.StoredAnalysisId, change.StoredAnalysisId);
        Assert.Single(change.OperationIds);
    }

    [Fact]
    public async Task AcceptingTheNewSetForAWordUsesTheRealCommandClientAndAddsOnlyMissingReadings()
    {
        using var project = await CreateProjectAsync("unknown");
        var token = AnalyzedToken(project);
        var stored = Assert.Single(token.StoredAnalyses);
        var readings = new[] { StoredReading(stored), Guessed("new parser reading") };
        var assessmentId = RecordAssessment(project, [new AssessedWord(token.Form!, "analysed", [])
        {
            Morphology = new ParseWordEvidence("v1", 0, token.Form!, 1, false, false, false, readings, []),
        }]);
        var assessment = await ReadAssessmentAsync(project);
        var inText = await LoadInTextAsync(project, assessment, assessmentId);
        var selected = ResultsToken(inText, token.Form!);
        inText.SelectToken(selected);
        var choice = Assert.Single(selected.Marking.FixChoices,
            candidate => candidate.Kind == AnalysisMarkingActionKind.AcceptNewSet);

        await inText.StageMarkingChoiceCommand.ExecuteAsync(choice);

        var staged = Assert.Single(inText.Changes.Snapshot.Changes);
        Assert.Equal(ChangeKinds.AddCandidate, staged.Kind);
        Assert.Equal(assessmentId, staged.AssessmentId);
        Assert.NotNull(staged.GroupId);
        Assert.Equal(2, staged.Analyses.Count);
        Assert.Contains(staged.Analyses, analysis => analysis.Stored);
        Assert.Contains(staged.Analyses, analysis => !analysis.Stored && analysis.Touched);
    }

    [Theory]
    [InlineData(false, ChangeKinds.AddCandidate)]
    [InlineData(true, ChangeKinds.Approve)]
    public async Task MarkingAddCommandsReachTheRealCommandClient(bool addAsApproved, string expectedKind)
    {
        using var project = await CreateProjectAsync();
        var source = AnalyzedToken(project);
        var token = TokenOf(project, SeededProject.UnanalysedWordForm);
        var reading = StoredReading(Assert.Single(source.StoredAnalyses));
        var assessmentId = RecordAssessment(project, [new AssessedWord(token.Form!, "analysed", [])
        {
            Morphology = new ParseWordEvidence("v1", 0, token.Form!, 1, false, false, false, [reading], []),
        }]);
        var assessment = await ReadAssessmentAsync(project);
        var inText = await LoadInTextAsync(project, assessment, assessmentId);
        var selected = ResultsToken(inText, token.Form!);
        inText.SelectToken(selected);

        if (addAsApproved)
        {
            var choice = Assert.Single(selected.Marking.FixChoices,
                candidate => candidate.Label == "Add as Approved" && candidate.ReadingIndex == 0);
            await inText.StageMarkingChoiceCommand.ExecuteAsync(choice);
        }
        else
            await inText.StagePrimaryMarkingActionCommand.ExecuteAsync(null);

        var staged = Assert.Single(inText.Changes.Snapshot.Changes);
        Assert.Equal(expectedKind, staged.Kind);
        Assert.Equal(assessmentId, staged.AssessmentId);
        Assert.Equal(0, staged.ReadingIndex);
        Assert.Equal(expectedKind == ChangeKinds.AddCandidate ? null : selected.Occurrence, staged.Occurrence);
    }

    [Fact]
    public async Task StoredOpinionChoiceStagesWithoutAnAssessmentIdThroughTheRealCommandClient()
    {
        using var project = await CreateProjectAsync("approved");
        var token = TokenOf(project, SeededProject.AnalysedWordForm);
        var reading = Guessed("different parser reading");
        RecordAssessment(project, [new AssessedWord(token.Form!, "analysed", [])
        {
            Morphology = new ParseWordEvidence("v1", 0, token.Form!, 1, false, false, false, [reading], []),
        }]);
        var assessment = await ReadAssessmentAsync(project);
        var inText = await LoadInTextAsync(project, assessment, null);
        var selected = ResultsToken(inText, token.Form!);
        inText.SelectToken(selected);
        var stored = Assert.Single(selected.Marking.FieldWorksAnalyses);
        var choice = Assert.Single(selected.Marking.FixChoices,
            candidate => candidate.Kind == AnalysisMarkingActionKind.Disapprove &&
                         candidate.StoredAnalysisId == stored.StoredAnalysisId);
        Assert.Null(choice.Reading);

        await inText.StageMarkingChoiceCommand.ExecuteAsync(choice);

        var staged = Assert.Single(inText.Changes.Snapshot.Changes);
        Assert.Equal(ChangeKinds.Reject, staged.Kind);
        Assert.Equal(stored.StoredAnalysisId, staged.StoredAnalysisId);
        Assert.Null(staged.AssessmentId);
        Assert.Null(staged.ReadingIndex);
        Assert.Equal(selected.Occurrence, staged.Occurrence);
    }

    [Theory]
    [InlineData("same", "analysed", false, AnalysisMarkingClass.Same)]
    [InlineData("different", "analysed", false, AnalysisMarkingClass.Conflict)]
    [InlineData("extra", "analysed", false, AnalysisMarkingClass.Extra)]
    [InlineData("none", "no-analysis", false, AnalysisMarkingClass.None)]
    [InlineData("capped", "capped", true, AnalysisMarkingClass.Capped)]
    public async Task AStoredAssessmentReadThroughTheCommandClientBuildsTheMarkingClass(
        string name, string outcome, bool capped, AnalysisMarkingClass expected)
    {
        using var project = await CreateProjectAsync();
        var token = AnalyzedToken(project);
        var stored = Assert.Single(token.StoredAnalyses);
        var exact = StoredReading(stored);
        var readings = name switch
        {
            "same" => new[] { exact },
            "different" => [Guessed("different reading")],
            "extra" => new[] { exact, Guessed("extra reading") },
            "none" => Array.Empty<ParseAnalysis>(),
            _ => [Guessed("partial reading")],
        };
        RecordAssessment(project, [new AssessedWord(token.Form!, outcome, [])
        {
            IsIncomplete = capped,
            Morphology = new ParseWordEvidence("v1", 0, token.Form!, 1, capped, false, false, readings, []),
        }]);

        var storedEvidence = await project.Client.ReadCurrentEvidenceAsync(project.Project.FwDataPath,
            CancellationToken.None);
        Assert.True(storedEvidence.Succeeded, storedEvidence.Refusal?.Message);
        var assessment = Assert.IsType<AssessCommandResponse>(storedEvidence.Value!.Assessment);
        var result = Assert.Single(assessment.Words);

        Assert.Equal(expected, AnalysisMarkingState.Create(token, result).PanGlossClass);
    }

    [Fact]
    public async Task AnalyzeTextsKeepsTheStoredIdentityWhenPanGlossBuildsTheSameReading()
    {
        using var project = await CreateProjectAsync();
        var token = AnalyzedToken(project);
        var stored = Assert.Single(token.StoredAnalyses);
        var reading = StoredReading(stored);
        var assessmentId = RecordAssessment(project, [new AssessedWord(token.Form!, "analysed", [])
        {
            Morphology = new ParseWordEvidence("v1", 0, token.Form!, 1, false, false, false, [reading], []),
        }]);
        var assessment = await ReadAssessmentAsync(project);
        var inText = await LoadInTextAsync(project, assessment, assessmentId);

        var displayed = ResultsToken(inText, token.Form!);

        var displayedAnalysis = Assert.Single(displayed.Marking.FieldWorksAnalyses);
        Assert.Equal(stored.StoredAnalysisId, displayedAnalysis.StoredAnalysisId);
        Assert.Equal(AnalysisMarkingClass.Same, displayed.Marking.PanGlossClass);
    }

    private async Task<ResultsInTextViewModel> LoadInTextAsync(MarkingCommandProject project,
        AssessCommandResponse assessment, string? assessmentId)
    {
        var fake = new FakeCommandClient();
        var selection = new SelectionViewModel(fake) { AllWordforms = true };
        var texts = new TextWordsViewModel(fake, selection);
        var assess = new AssessViewModel(fake, selection) { ProjectPath = project.Project.FwDataPath };
        var changes = new ChangesViewModel(project.Client);
        await changes.OpenProjectAsync(project.Project.FwDataPath);
        fake.ReadWordStateCompletesWith(new WordReadStateResponse([], true));
        var inText = new ResultsInTextViewModel(texts, assess, _ => { }, _ => { }, changes, fake);
        fake.ListTextWordsCompletesWith(project.Words);
        await texts.SetProjectAsync(project.Project.FwDataPath);
        fake.AssessCompletesWith(assessment);
        await assess.RunCommand.ExecuteAsync(null);
        changes.AssessmentId = assessmentId;
        return inText;
    }

    private static async Task<AssessCommandResponse> ReadAssessmentAsync(MarkingCommandProject project)
    {
        var outcome = await project.Client.ReadCurrentEvidenceAsync(project.Project.FwDataPath, CancellationToken.None);
        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        return Assert.IsType<AssessCommandResponse>(outcome.Value!.Assessment);
    }

    private static ResultsTokenViewModel ResultsToken(ResultsInTextViewModel inText, string form) =>
        Assert.Single(inText.Texts.SelectMany(text => text.Lines).SelectMany(line => line.Tokens),
            token => token.Form == form);

    private async Task<MarkingCommandProject> CreateProjectAsync(string? opinion = null)
    {
        var project = new WalkthroughProject(pristine);
        new FieldWorksSimulator(project.FwDataPath).SaveEdit(cache =>
        {
            var text = cache.ServiceLocator.GetInstance<ITextRepository>().GetObject(project.Text.TextId);
            var analysis = cache.ServiceLocator.GetInstance<IWfiAnalysisRepository>()
                .GetObject(project.Text.ApprovedAnalysisId);
            var evaluation = opinion switch
            {
                "approved" => Opinions.approves,
                "disapproved" => Opinions.disapproves,
                "unknown" => Opinions.noopinion,
                _ => (Opinions?)null,
            };
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            {
                foreach (var paragraph in text.ContentsOA!.ParagraphsOS.OfType<IStTxtPara>())
                    paragraph.ParseIsCurrent = true;
                if (evaluation is { } value)
                    cache.LangProject.DefaultUserAgent.SetEvaluation(analysis, value);
            });
        });

        var client = RealCommandClient.Create(project.ManagedRoot, FakeParser.ExecutablePath);
        var captured = await client.CaptureBaselineAsync(new BaselineCaptureRequest(project.FwDataPath),
            CancellationToken.None);
        Assert.True(captured.Succeeded, captured.Refusal?.Message);
        var selection = await client.SetDefaultSelectionAsync(new SetDefaultSelectionRequest(
            project.FwDataPath, Path.GetFileNameWithoutExtension(project.FwDataPath),
            [project.Text.TextId], []), CancellationToken.None);
        Assert.True(selection.Succeeded, selection.Refusal?.Message);
        var words = await client.ListTextWordsAsync(new TextWordsRequest(project.FwDataPath,
            [project.Text.TextId]), CancellationToken.None);
        Assert.True(words.Succeeded, words.Refusal?.Message);
        return new MarkingCommandProject(project, client, captured.Value!.Token, words.Value!);
    }

    private static string RecordAssessment(MarkingCommandProject project, IReadOnlyList<AssessedWord> words)
    {
        var locator = new ProjectLocator(Path.GetFullPath(project.Project.FwDataPath),
            Path.GetFileNameWithoutExtension(project.Project.FwDataPath));
        using var database = MotifDatabase.OpenOwned(ProjectDatabaseCatalog.DatabasePathFor(locator), locator,
            MotifSchema.CurrentSchema, new Version(1, 0));
        var baseline = new BaselineRepository(database).GetCurrent(ProjectWorkspaceKey.Compute(locator))!;
        using var baselineCache = new FwDataProjectLoader().LoadScratchCache(baseline.FwDataPath);
        var composition = SelectionComposer.Compose(baselineCache,
            new SelectionRequest(false, [project.Project.Text.TextId], [], false, null),
            new AssessmentRepository(database), JsonSerializer.Serialize(project.BaselineToken,
                MotifJson.CreateOptions()));
        Assert.True(composition.Succeeded, composition.Refusal?.Message);
        var assessmentId = "assessment/marking/" + Guid.NewGuid().ToString("N");
        new AssessmentRepository(database).Record(new NewAssessmentRecord(
            assessmentId, null, null, "test", AssessmentKind.ParseTime.ToStoredKind(), "{}", "sha256:scope",
            "whitespace-and-punctuation", "1", JsonSerializer.Serialize(project.BaselineToken,
                MotifJson.CreateOptions()), composition.Value!.Selection, "sha256:outcome", "sha256:semantic",
            "sha256:grammar", "model", "pipeline", 0, words,
            SavedUtc: DateTimeOffset.UtcNow.ToString("O")));
        return assessmentId;
    }

    private static TextToken AnalyzedToken(MarkingCommandProject project) =>
        TokenOf(project, SeededProject.AnalysedWordForm);

    private static TextToken TokenOf(MarkingCommandProject project, string form) =>
        Assert.Single(project.Words.Texts.SelectMany(text => text.Lines)
            .SelectMany(line => line.Tokens), token => token.Form == form);

    private static ParseAnalysis Guessed(string text) => new([new ParseMorph(null, null, null, text)]);

    private static ParseAnalysis StoredReading(ProjectAnalysis analysis) => new(
        analysis.Identity!.Morphs.Select(morph => new ParseMorph(morph.Form, morph.Msa, morph.InflType, null)).ToArray());

    private sealed record MarkingCommandProject(WalkthroughProject Project, CommandClient Client,
        SIL.Motif.Contract.Baselines.BaselineToken BaselineToken, TextWordsResponse Words) : IDisposable
    {
        public void Dispose() => Project.Dispose();
    }
}
