using Avalonia.Input;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Pins the stage stepper's state on <see cref="HandoffWorkspaceViewModel"/>: which stage is showing, what each
/// entry says and whether it counts as done, and the two moves the workspace makes on its own — a run
/// starting opens Results, and a chosen project reopens Project.
/// </summary>
public sealed class WorkflowStageTests
{
    private const string ProjectPath = @"C:\projects\one.fwdata";

    private static readonly BaselineToken Token = new(
        "project-1", "sha256:" + new string('a', 64), "1", "2026-09-05T00:00:00Z", "sha256:" + new string('b', 64));

    private static (FakeCommandClient Fake, FakeProjectPicker ProjectPicker, HandoffWorkspaceViewModel Workspace)
        NewWorkspace()
    {
        var fake = new FakeCommandClient();
        var projectPicker = new FakeProjectPicker();
        var selection = new SelectionViewModel(fake);
        var words = new TextWordsViewModel(fake, selection);
        var workspace = new HandoffWorkspaceViewModel(
            new ProjectViewModel(fake, projectPicker),
            new ProjectHistoryViewModel(fake),
            new BaselineViewModel(fake),
            new GrammarViewModel(fake),
            selection,
            words,
            new AssessViewModel(fake, selection),
            new StatisticsViewModel(fake),
            new HandoffViewModel(fake, selection, new FakeFolderPicker(), new FakeDragSource()));
        return (fake, projectPicker, workspace);
    }

    private static async Task ChooseProjectAsync(
        FakeCommandClient fake, FakeProjectPicker projectPicker, HandoffWorkspaceViewModel workspace)
    {
        fake.CurrentBaselineCompletesWith(new CurrentBaselineResponse(Token, DateTimeOffset.UtcNow, false));
        fake.ListTextsCompletesWith(new TextInventoryResponse([], HasBaseline: true));
        projectPicker.PathToReturn = ProjectPath;
        await workspace.Project.BrowseCommand.ExecuteAsync(null);
    }

    private static AssessCommandResponse NewAssessResponse() => new(
        new BaselineCaptureResponse(Token, ProjectPath, DateTimeOffset.UtcNow, false, false),
        new SelectionProjection([], []), [], "summary")
    {
        InvocationId = "invocation/one",
        CompletionSummary = "3 searches completed",
    };

    [Fact]
    public void TheStepperListsTheFiveStagesInWorkflowOrderAndOpensOnProject()
    {
        var (_, _, workspace) = NewWorkspace();

        Assert.Equal(
            [WorkflowStage.Project, WorkflowStage.Grammar, WorkflowStage.Texts, WorkflowStage.Results,
                WorkflowStage.Handoff],
            workspace.Stages.Select(stage => stage.Stage));
        Assert.Equal([1, 2, 3, 4, 5], workspace.Stages.Select(stage => stage.Number));
        Assert.Equal(WorkflowStage.Project, workspace.CurrentStage);
        Assert.True(workspace.IsProjectStage);
        Assert.Equal("Choose a project", workspace.Stages[0].Summary);
        Assert.Equal("Not checked yet", workspace.Stages[1].Summary);
        Assert.Equal("Not run yet", workspace.Stages[3].Summary);
        Assert.Equal("Not written yet", workspace.Stages[4].Summary);
    }

    [Fact]
    public void ShowingAStageMovesTheCurrentMarkerAndTheStageFlags()
    {
        var (_, _, workspace) = NewWorkspace();

        workspace.ShowStageCommand.Execute(WorkflowStage.Results);

        Assert.True(workspace.IsResultsStage);
        Assert.False(workspace.IsProjectStage);
        Assert.Same(workspace.Stages[3], workspace.SelectedStage);
        Assert.Equal([false, false, false, true, false], workspace.Stages.Select(stage => stage.IsCurrent));
    }

    [Fact]
    public void SelectingAStepperEntryOpensItsStageAndClearingTheSelectionChangesNothing()
    {
        var (_, _, workspace) = NewWorkspace();

        workspace.SelectedStage = workspace.Stages[4];
        workspace.SelectedStage = null!;

        Assert.Equal(WorkflowStage.Handoff, workspace.CurrentStage);
    }

    [Fact]
    public async Task ChoosingAProjectNamesItAndMarksTheProjectStageDoneOnceItHasABaseline()
    {
        var (fake, projectPicker, workspace) = NewWorkspace();

        await ChooseProjectAsync(fake, projectPicker, workspace);

        Assert.True(workspace.HasProject);
        Assert.Equal("one.fwdata", workspace.ProjectName);
        Assert.Equal("Baseline captured", workspace.Stages[0].Summary);
        Assert.True(workspace.Stages[0].IsDone);
        Assert.Equal($"Baseline: {workspace.Baseline.CapturedTimeText}", workspace.BaselineHeaderText);
    }

    [Fact]
    public async Task ChoosingAProjectFromAnotherStageReturnsToTheProjectStage()
    {
        var (fake, projectPicker, workspace) = NewWorkspace();
        workspace.ShowStageCommand.Execute(WorkflowStage.Handoff);

        await ChooseProjectAsync(fake, projectPicker, workspace);

        Assert.Equal(WorkflowStage.Project, workspace.CurrentStage);
    }

    [Fact]
    public async Task ARunStartingOpensResultsAndItsCompletionMarksSelectionAndResultsDone()
    {
        var (fake, projectPicker, workspace) = NewWorkspace();
        await ChooseProjectAsync(fake, projectPicker, workspace);
        workspace.ShowStageCommand.Execute(WorkflowStage.Texts);
        workspace.Selection.AllWordforms = true;
        Assert.True(workspace.Stages[2].IsDone);
        fake.AssessBlocksUntilCancelled(new Refusal("assess.cancelled", FailureReason.Cancelled, "Cancelled."));

        var running = workspace.Assess.RunCommand.ExecuteAsync(null);

        Assert.Equal(WorkflowStage.Results, workspace.CurrentStage);
        Assert.Equal("Running...", workspace.Stages[3].Summary);
        workspace.Assess.CancelCommand.Execute(null);
        await running;

        fake.AssessCompletesWith(NewAssessResponse());
        await workspace.Assess.RunCommand.ExecuteAsync(null);

        Assert.True(workspace.Stages[3].IsDone);
        Assert.Equal("3 searches completed", workspace.Stages[3].Summary);
        Assert.Equal("No findings", workspace.Stages[1].Summary);
        Assert.True(workspace.Stages[1].IsDone);
    }

    [Fact]
    public async Task ARunStartingReturnsResultsToItsWordsView()
    {
        var (fake, projectPicker, workspace) = NewWorkspace();
        await ChooseProjectAsync(fake, projectPicker, workspace);
        workspace.Selection.AllWordforms = true;
        workspace.ShowResultsViewCommand.Execute(ResultsView.Statistics);
        Assert.True(workspace.ShowResultsStatistics);
        Assert.False(workspace.ShowResultsWords);
        fake.AssessBlocksUntilCancelled(new Refusal("assess.cancelled", FailureReason.Cancelled, "Cancelled."));

        var running = workspace.Assess.RunCommand.ExecuteAsync(null);

        Assert.True(workspace.ShowResultsWords);
        workspace.Assess.CancelCommand.Execute(null);
        await running;
    }

    [Fact]
    public async Task ChoosingAProjectChecksGrammarIndependentlyOfAssessingAnything()
    {
        var (fake, projectPicker, workspace) = NewWorkspace();
        fake.CheckGrammarCompletesWith(new GrammarCheckResponse(
            [new GrammarWarning("warning", "Entry", [], [new GrammarWarningPart("dropped", "text")], "warning: dropped")],
            HasBaseline: true));

        await ChooseProjectAsync(fake, projectPicker, workspace);

        Assert.Equal("1", workspace.Stages[1].Badge);
        Assert.True(workspace.Stages[1].HasBadge);
        Assert.Equal("1 finding(s)", workspace.Stages[1].Summary);
        Assert.True(workspace.Stages[1].IsDone);
        Assert.Single(fake.CheckGrammarRequests);
        Assert.Empty(fake.AssessRequests);
    }

    [Fact]
    public async Task RefreshingTheBaselineChecksGrammarAgain()
    {
        var (fake, projectPicker, workspace) = NewWorkspace();
        await ChooseProjectAsync(fake, projectPicker, workspace);
        Assert.Single(fake.CheckGrammarRequests);

        fake.CaptureBaselineCompletesWith(new BaselineCaptureResponse(
            Token, ProjectPath, DateTimeOffset.UtcNow, false, false));
        await workspace.Baseline.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(2, fake.CheckGrammarRequests.Count);
    }

    [Fact]
    public async Task TheCurrentStageKeepsItsNumberWhileADoneStageShowsACheck()
    {
        var (fake, projectPicker, workspace) = NewWorkspace();
        await ChooseProjectAsync(fake, projectPicker, workspace);

        Assert.True(workspace.Stages[0].IsCurrent);
        Assert.False(workspace.Stages[0].ShowsCheck);

        workspace.ShowStageCommand.Execute(WorkflowStage.Texts);

        Assert.True(workspace.Stages[0].ShowsCheck);
        Assert.False(workspace.Stages[2].ShowsCheck);
    }

    [Fact]
    public void TheHandoffActionOffersARewriteOnlyOnceFilesExist()
    {
        var (_, _, workspace) = NewWorkspace();
        Assert.Equal("Write Handoff", workspace.HandoffActionText);

        workspace.Handoff.Files.Add(new HandoffFileViewModel("handoff.md", @"C:\handoff\handoff.md"));
        workspace.Handoff.State = RunState.Completed;

        Assert.Equal("Write Handoff again", workspace.HandoffActionText);
        Assert.Equal("Written: 1 file(s)", workspace.HandoffStatusText);
        Assert.True(workspace.Stages[4].IsDone);
    }

    private sealed class FakeProjectPicker : IProjectPicker
    {
        public string? PathToReturn { get; set; }

        public Task<string?> PickProjectFileAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(PathToReturn);
    }

    private sealed class FakeFolderPicker : IHandoffFolderPicker
    {
        public Task<string?> PickFolderAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(@"C:\out");
    }

    private sealed class FakeDragSource : IFileDragSource
    {
        public Task<DragDropEffects> StartDragAsync(
            PointerPressedEventArgs trigger, IReadOnlyList<string> filePaths, DragDropEffects allowedEffects) =>
            Task.FromResult(allowedEffects);
    }
}
