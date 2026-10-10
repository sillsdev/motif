using System.Text.Json;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Assess;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Commands.SelectionReading;
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
    public void RemoveAnalysisUsesTheRealCommandClientAndStagesTheExactStoredAnalysis()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            await using var project = await CreateProjectAsync();
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
        }, TimeSpan.FromSeconds(180));
    }

    [Fact]
    public void AcceptingTheNewSetForAWordUsesTheRealCommandClientAndAddsOnlyMissingReadings()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            await using var project = await CreateProjectAsync("unknown");
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
        }, TimeSpan.FromSeconds(180));
    }

    [Theory]
    [InlineData(false, ChangeKinds.AddCandidate)]
    [InlineData(true, ChangeKinds.Approve)]
    public void MarkingAddCommandsReachTheRealCommandClient(bool addAsApproved, string expectedKind)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            await using var project = await CreateProjectAsync();
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
        }, TimeSpan.FromSeconds(180));
    }

    [Fact]
    public void StoredOpinionChoiceStagesWithoutAnAssessmentIdThroughTheRealCommandClient()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            await using var project = await CreateProjectAsync("approved");
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
        }, TimeSpan.FromSeconds(180));
    }

    [Theory]
    [InlineData("same", "analysed", false, AnalysisMarkingClass.Same, CompareColumnKind.Match, "Kept", 0)]
    [InlineData("different", "analysed", false, AnalysisMarkingClass.Conflict, CompareColumnKind.NoMatch, "Built something else", 1)]
    [InlineData("extra", "analysed", false, AnalysisMarkingClass.Extra, CompareColumnKind.NoMatch, "Built something else", 1)]
    [InlineData("none", "no-analysis", false, AnalysisMarkingClass.None, CompareColumnKind.NoParse, "Lost", 1)]
    [InlineData("capped", "capped", true, AnalysisMarkingClass.Capped, CompareColumnKind.Timeout, "Unknown yet", 1)]
    [InlineData("incorrect-no-parse", "no-analysis", false, AnalysisMarkingClass.None, CompareColumnKind.NoParse, "Correct", 0)]
    [InlineData("incorrect-capped", "capped", true, AnalysisMarkingClass.Conflict, CompareColumnKind.Timeout, "Unknown yet", 1)]
    [InlineData("rebuilt-disapproved", "analysed", false, AnalysisMarkingClass.Conflict, CompareColumnKind.Match, "Built anyway", 1)]
    public void AStoredAssessmentReadThroughTheCommandClientBuildsTheMarkingClass(
        string name, string outcome, bool capped, AnalysisMarkingClass expected, CompareColumnKind expectedColumn,
        string expectedLabel, int expectedListCount)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            await using var project = await CreateProjectAsync(
                name == "rebuilt-disapproved" ? "disapproved" : null, name.StartsWith("incorrect", StringComparison.Ordinal));
            var token = AnalyzedToken(project);
            var stored = Assert.Single(token.StoredAnalyses);
            var exact = StoredReading(stored);
            var readings = name switch
            {
                "same" or "rebuilt-disapproved" => new[] { exact },
                "different" => [Guessed("different reading")],
                "extra" => new[] { exact, Guessed("extra reading") },
                "none" or "incorrect-no-parse" => Array.Empty<ParseAnalysis>(),
                _ => [Guessed("partial reading")],
            };
            RecordAssessment(project, [new AssessedWord(token.Form!, outcome, [])
            {
                ProjectStanding = name.StartsWith("incorrect", StringComparison.Ordinal)
                    ? ProjectStanding.IncorrectSpelling
                    : name == "rebuilt-disapproved" ? ProjectStanding.Rejected : ProjectStanding.Approved,
                IsIncomplete = capped,
                Morphology = new ParseWordEvidence("v1", 0, token.Form!, 1, capped, false, false, readings, []),
            }]);

            var storedEvidence = await project.Client.ReadCurrentEvidenceAsync(project.Project.FwDataPath,
                CancellationToken.None);
            Assert.True(storedEvidence.Succeeded, storedEvidence.Refusal?.Message);
            var assessment = Assert.IsType<AssessCommandResponse>(storedEvidence.Value!.Assessment);
            var result = Assert.Single(assessment.Words);

            Assert.Equal(expected, AnalysisMarkingState.Create(token, result).PanGlossClass);
            var table = new AssessWordsViewModel();
            table.Load([result]);
            var compare = new CompareViewModel();
            compare.Load(table.AllRows);
            var row = Assert.Single(compare.Words);
            var lists = new TextsListsViewModel(compare);
            var inText = await LoadInTextAsync(project, assessment, null);
            var card = ResultsToken(inText, token.Form!);
            var placement = CompareViewModel.Place(Assert.Single(table.AllRows));

            Assert.Equal(expected, row.Marking.PanGlossClass);
            Assert.Equal(expected, card.Marking.PanGlossClass);
            Assert.Equal(expectedLabel, card.PanGlossSummary);
            Assert.Equal(expectedLabel, card.VerdictLabel);
            Assert.Equal(expectedColumn, placement.Item2);
            Assert.Equal(expectedListCount,
                lists.Lists.Count(list => list.HasWords && list.Cells.Contains(new TextsListCell(
                    placement.Item1, placement.Item2))));
        }, TimeSpan.FromSeconds(180));
    }

    [Fact]
    public void AnalyzeTextsKeepsTheStoredIdentityWhenPanGlossBuildsTheSameReading()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            await using var project = await CreateProjectAsync();
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
        }, TimeSpan.FromSeconds(180));
    }

    private async Task<ResultsInTextViewModel> LoadInTextAsync(MarkingCommandProject project,
        AssessCommandResponse assessment, string? assessmentId)
    {
        var fake = new FakeCommandClient();
        var selection = new SelectionViewModel(fake) { AllWordforms = true };
        var reads = new WorkspaceSelection(project.Client);
        var texts = new TextWordsViewModel(fake, selection, reads);
        var assess = new AssessViewModel(fake, selection) { ProjectPath = project.Project.FwDataPath };
        var changes = new ChangesViewModel(project.Client);
        await changes.OpenProjectAsync(project.Project.FwDataPath);
        fake.ReadWordStateCompletesWith(new WordReadStateResponse([], true));
        var inText = new ResultsInTextViewModel(texts, assess, _ => { }, _ => { }, changes, fake, reads);
        await texts.SetProjectAsync(project.Project.FwDataPath);
        fake.AssessCompletesWith(assessment);
        await assess.RunCommand.ExecuteAsync(null);
        changes.AssessmentId = assessmentId;
        await reads.ReloadAsync(project.Project.FwDataPath, [project.Project.Text.TextId], [],
            shownAssessment: assessmentId is null ? null : new SelectionAssessmentEvidence(
                assessment.Baseline.Token, assessmentId, assessment.TimingOverrideAssessmentIds,
                assessment.Measurements.Where(item => item.Kind is AssessmentKinds.Correctness or AssessmentKinds.ObjectTiming)
                    .GroupBy(item => item.Kind, StringComparer.Ordinal).OrderBy(group => group.Key, StringComparer.Ordinal)
                    .Select(group => group.Last().AssessmentId).ToArray()));
        Assert.Null(reads.Refusal);
        Assert.NotNull(reads.Summary);
        await SelectionModelFixture.RealizeAsync(inText);
        project.Models.Add((inText, texts, reads));
        return inText;
    }

    private static async Task<AssessCommandResponse> ReadAssessmentAsync(MarkingCommandProject project)
    {
        var outcome = await project.Client.ReadCurrentEvidenceAsync(project.Project.FwDataPath, CancellationToken.None);
        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        return Assert.IsType<AssessCommandResponse>(outcome.Value!.Assessment);
    }

    private static ResultsTokenViewModel ResultsToken(ResultsInTextViewModel inText, string form) =>
        Assert.Single(SelectionModelFixture.VisibleLines(inText).SelectMany(line => line.Tokens),
            token => token.Form == form);

    private async Task<MarkingCommandProject> CreateProjectAsync(string? opinion = null, bool incorrectSpelling = false)
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
                if (incorrectSpelling) ((IWfiWordform)analysis.Owner).SpellingStatus = 2;
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
        var opened = await client.OpenSelectionReaderAsync(new OpenSelectionReaderRequest(
            project.FwDataPath, [project.Text.TextId], []), CancellationToken.None);
        Assert.True(opened.Succeeded, opened.Refusal?.Message);
        await using var reader = opened.Value!;
        var summaryResult = await reader.ReadSummaryAsync(new SelectionViewRequest());
        Assert.True(summaryResult.Succeeded, summaryResult.Refusal?.Message);
        using var summary = summaryResult.Value!;
        var header = Assert.Single(summary.Value.Texts);
        Assert.InRange(header.LineCount, 1, 32);
        Assert.InRange(header.OccurrenceCount, 1, 512);
        var lines = await reader.ReadLinesAsync(header.TextId, new TextLineRange());
        Assert.True(lines.Succeeded, lines.Refusal?.Message);
        using var read = lines.Value!;
        var source = new TextWordsResponse([], [new TextLines(header.TextId, header.Title,
            SelectionDisplayProjection.Lines(read.Value))], true);
        return new MarkingCommandProject(project, client, captured.Value!.Token, source);
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
        SIL.Motif.Contract.Baselines.BaselineToken BaselineToken, TextWordsResponse Words) : IAsyncDisposable
    {
        public List<(ResultsInTextViewModel Reader, TextWordsViewModel Words, WorkspaceSelection Reads)> Models { get; } = [];

        public async ValueTask DisposeAsync()
        {
            foreach (var model in Models)
            {
                await model.Reader.StopAsync();
                await model.Words.StopAsync();
                await model.Reads.StopAsync();
            }
            Project.Dispose();
        }
    }
}
