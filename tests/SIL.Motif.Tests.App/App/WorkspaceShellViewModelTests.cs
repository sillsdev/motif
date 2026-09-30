using System.Text.Json;
using Avalonia.Input;
using CommunityToolkit.Mvvm.Input;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Pins <see cref="WorkspaceShellViewModel"/>'s composition: choosing a project loads Baseline and Text
/// state and propagates the project to every child; a completed Assessment feeds
/// <see cref="BaselineViewModel.HasAssessment"/> and <see cref="StatisticsViewModel.SummaryMarkdown"/>; a
/// Refresh that replaces an already-assessed Baseline offers a rerun, which Accept and Dismiss resolve;
/// and choosing another project clears what the previous one displayed. The last test exercises the whole
/// agreed workflow end to end.
/// </summary>
public sealed class WorkspaceShellViewModelTests
{
    private const string Digest = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string BundleDigest = "sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string ProjectPath = @"C:\projects\one.fwdata";

    private static readonly Guid TextId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static BaselineToken NewToken(string capturedUtc = "2026-09-05T00:00:00Z") =>
        new("project-1", Digest, "1", capturedUtc, BundleDigest);

    private static AssessCommandResponse NewAssessResponse(string summary) => new(
        new BaselineCaptureResponse(NewToken(), ProjectPath, DateTimeOffset.UtcNow, false, false),
        new SelectionProjection([], []), ["assessment/time", "assessment/one"], summary)
    {
        InvocationId = "invocation/one",
        Measurements = [new ProducedAssessmentReference("assessment/time", "ParseTime", "run"),
            new ProducedAssessmentReference("assessment/one", "ObjectTiming", "run")],
    };

    private static (FakeCommandClient Fake, FakeProjectPicker ProjectPicker, FakeFolderPicker FolderPicker,
        FakeDragSource DragSource, WorkspaceShellViewModel Workspace) NewWorkspace()
    {
        var fake = new FakeCommandClient();
        var projectPicker = new FakeProjectPicker();
        var folderPicker = new FakeFolderPicker();
        var dragSource = new FakeDragSource();
        var selection = new SelectionViewModel(fake);
        var workspace = new WorkspaceShellViewModel(
            new ProjectViewModel(fake, projectPicker),
            new BaselineViewModel(fake),
            selection,
            new AssessViewModel(fake, selection),
            folderPicker, dragSource,
            fake);
        return (fake, projectPicker, folderPicker, dragSource, workspace);
    }

    private static async Task ChooseProjectAsync(
        FakeCommandClient fake, FakeProjectPicker projectPicker, WorkspaceShellViewModel workspace,
        string projectPath, BaselineToken? token = null)
    {
        token ??= NewToken();
        fake.CurrentBaselineCompletesWith(new CurrentBaselineResponse(token, DateTimeOffset.UtcNow, false));
        projectPicker.PathToReturn = projectPath;
        await workspace.Project.BrowseCommand.ExecuteAsync(null);
    }

    [Fact]
    public async Task RerunningFromTimingKeepsTimingOpen()
    {
        var (fake, projectPicker, _, _, workspace) = NewWorkspace();
        await ChooseProjectAsync(fake, projectPicker, workspace, ProjectPath, NewToken());
        workspace.Context.OpenPage(WorkspacePage.Timing);
        fake.AssessCompletesWith(NewAssessResponse("rerun"));

        await workspace.Assess.RerunAsync(["word"], 1000);

        Assert.Equal(WorkspacePage.Timing, workspace.Context.CurrentPage);
    }

    // Rows are only a view over the fetched set; clearing the view alone leaves the old project's words.
    [Fact]
    public async Task SortingAfterASwitchOfProjectsCannotResurrectTheOldProjectsStatistics()
    {
        var (fake, projectPicker, _, _, workspace) = NewWorkspace();
        await ChooseProjectAsync(fake, projectPicker, workspace, @"C:\projects\one.fwdata", NewToken());
        fake.StatsCompletesWith(new StatsCommandResponse(
            "assessment/one", "grammar.json", "cache.sqlite", null,
            [JsonDocument.Parse("""{"kind":"word","form":"from-project-one"}""").RootElement.Clone()]));
        await workspace.PageModel<TimingPageModel>().Statistics.LoadCommand.ExecuteAsync(null);
        Assert.Single(workspace.PageModel<TimingPageModel>().Statistics.Rows);

        await ChooseProjectAsync(fake, projectPicker, workspace, @"C:\projects\two.fwdata", NewToken());
        workspace.PageModel<TimingPageModel>().Statistics.SortBy("word");

        Assert.Empty(workspace.PageModel<TimingPageModel>().Statistics.Rows);
    }

    [Fact]
    public async Task ChoosingAProjectLoadsBaselineAndTextStateAndPropagatesTheProjectToEveryChild()
    {
        var (fake, projectPicker, _, _, workspace) = NewWorkspace();
        fake.CurrentBaselineCompletesWith(new CurrentBaselineResponse(NewToken(), DateTimeOffset.UtcNow, false));
        fake.ListTextsCompletesWith(new TextInventoryResponse([new TextChoiceSummary(TextId, "Alpha")], HasBaseline: true));
        projectPicker.PathToReturn = ProjectPath;

        await workspace.Project.BrowseCommand.ExecuteAsync(null);

        Assert.True(workspace.Baseline.HasBaseline);
        Assert.Single(workspace.Selection.Texts);
        Assert.Equal(ProjectPath, workspace.Assess.ProjectPath);
        Assert.Equal(ProjectPath, workspace.PageModel<TimingPageModel>().Statistics.ProjectPath);
        Assert.Equal(ProjectPath, workspace.PageModel<AiHandoffPageModel>().Handoff.ProjectPath);
    }

    [Fact]
    public async Task ConfigureOpensSetupWithoutNavigatingAwayFromTheCurrentPage()
    {
        var (fake, projectPicker, _, _, workspace) = NewWorkspace();
        fake.DefaultSelectionResponseIs(JsonSerializer.Deserialize<DefaultSelectionResponse>("""
            {"Selection":{"Name":"Default","TextIds":["11111111-1111-1111-1111-111111111111"],
             "AddedWords":["one","two"],"CreatedUtc":"created","UpdatedUtc":"updated",
             "PerWordLimitMs":1250,"PerWordStepLimit":{"steps":987,"isUnbounded":false}},"SetupSkipped":false}
            """)!);
        fake.ListTextsCompletesWith(new TextInventoryResponse(
            [new TextChoiceSummary(TextId, "Alpha", 10, 6)], HasBaseline: true));
        fake.OnShowConfig((_, _) => Task.FromResult(CommandOutcome<ProjectConfigurationProjection>.Success(
            new ProjectConfigurationProjection(true, true,
                [new AssessmentScopeProjection("default", "all words", "pangloss", [], 750, new StepCap(321))]))));
        await ChooseProjectAsync(fake, projectPicker, workspace, ProjectPath, NewToken());
        Assert.False(workspace.Context.Setup!.IsOpen);
        workspace.CurrentPage = WorkspacePage.Overview;

        workspace.ConfigureCommand.Execute(null);

        Assert.Equal(WorkspacePage.Overview, workspace.CurrentPage);
        Assert.True(workspace.Context.Setup!.IsOpen);
        Assert.Equal(0, workspace.Context.Setup.Step);
        Assert.True(Assert.Single(workspace.Selection.Texts).IsChecked);
        Assert.Equal(string.Join(Environment.NewLine, "one", "two"), workspace.Selection.PastedWords);
        Assert.Equal(1.25m, workspace.Selection.PerWordTimeLimitSeconds);
        Assert.Equal(987m, workspace.Selection.PerWordStepLimit);
        Assert.Equal(new StepCap(987), workspace.Selection.BuildRequest().PerWordStepLimit);
        Assert.Equal("Use this Selection", workspace.Context.Setup.FinishButtonText);
        Assert.Equal("Use this Selection as the project default", workspace.Context.Setup.FinishTitle);
        Assert.DoesNotContain("Save", workspace.Context.Setup.FinishDescription);
    }

    [Fact]
    public async Task AProjectWithoutADefaultSelectionOpensSetupOnItsFirstStep()
    {
        var (fake, projectPicker, _, _, workspace) = NewWorkspace();
        await ChooseProjectAsync(fake, projectPicker, workspace, ProjectPath, NewToken());

        var setup = workspace.Context.Setup;
        Assert.NotNull(setup);
        Assert.True(setup.IsOpen);
        Assert.Equal(0, setup.Step);
    }

    [Fact]
    public async Task SetupDoesNotRestartWhenBaselineArrivesDuringProjectLoading()
    {
        var (fake, _, _, _, workspace) = NewWorkspace();
        var setup = workspace.Context.Setup!;
        workspace.Context.ProjectPath = ProjectPath;
        workspace.Context.Baseline = new WorkspaceBaseline(true, "captured", "saved", "at", "not held", null);
        var configStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var configGate = new TaskCompletionSource<CommandOutcome<ProjectConfigurationProjection>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        fake.OnShowConfig((_, _) =>
        {
            configStarted.TrySetResult();
            return configGate.Task;
        });

        var projectLoading = setup.ProjectOpenedAsync(ProjectPath);
        await configStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await setup.BaselineCapturedAsync();
        setup.NextCommand.Execute(null);
        Assert.Equal(1, setup.Step);

        configGate.SetResult(CommandOutcome<ProjectConfigurationProjection>.Success(
            new ProjectConfigurationProjection(true, true,
                [new AssessmentScopeProjection("default", "all words", "pangloss", [], 1000, StepCap.Default)])));
        await projectLoading;

        Assert.True(setup.IsOpen);
        Assert.Equal(1, setup.Step);
    }

    [Fact]
    public async Task SkippingFirstSetupDoesNotSaveADefaultSelection()
    {
        var (fake, projectPicker, _, _, workspace) = NewWorkspace();
        fake.ListTextsCompletesWith(new TextInventoryResponse([new TextChoiceSummary(TextId, "Alpha")], true));
        await ChooseProjectAsync(fake, projectPicker, workspace, ProjectPath, NewToken());
        workspace.Selection.Texts[0].IsChecked = true;

        var skip = Assert.IsAssignableFrom<IAsyncRelayCommand>(workspace.Context.Setup!.SkipCommand);
        await skip.ExecuteAsync(null);

        Assert.False(workspace.Context.Setup.IsOpen);
        Assert.Empty(fake.SetDefaultSelectionRequests);
    }

    [Fact]
    public async Task FirstRunSavesTheSelectionAndUsesItWithTheChosenStepLimit()
    {
        var (fake, projectPicker, _, _, workspace) = NewWorkspace();
        fake.ListTextsCompletesWith(new TextInventoryResponse([new TextChoiceSummary(TextId, "Alpha")], true));
        await ChooseProjectAsync(fake, projectPicker, workspace, ProjectPath, NewToken());
        workspace.Selection.Texts[0].IsChecked = true;
        workspace.Selection.PastedWords = "added word";
        workspace.Context.Setup!.StepLimitSteps = SIL.Motif.Contract.Assess.StepCap.DefaultSteps;
        workspace.Context.Setup.Step = 3;
        fake.AssessCompletesWith(NewAssessResponse("first run"));

        await workspace.Context.Setup.FinishCommand.ExecuteAsync(null);

        var saved = Assert.Single(fake.SetDefaultSelectionRequests);
        Assert.Equal(ProjectPath, saved.ProjectPath);
        Assert.Equal([TextId], saved.TextIds);
        Assert.Equal(["added word"], saved.AddedWords);
        using var savedJson = JsonDocument.Parse(JsonSerializer.Serialize(saved));
        Assert.Equal(40_000, savedJson.RootElement.GetProperty("PerWordLimitMs").GetInt32());
        Assert.Equal(SIL.Motif.Contract.Assess.StepCap.DefaultSteps,
            savedJson.RootElement.GetProperty("PerWordStepLimit").GetProperty("steps").GetInt64());
        var assess = Assert.Single(fake.AssessRequests);
        Assert.Null(assess.Selection);
        Assert.Equal(40_000, assess.PerWordLimitMs);
        Assert.Equal(StepCap.Default, assess.PerWordStepLimit);
        Assert.False(workspace.Context.Setup.IsOpen);
    }

    [Fact]
    public async Task FirstRunClosesSetupOnTheFirstProgressEvent()
    {
        var (fake, projectPicker, _, _, workspace) = NewWorkspace();
        fake.ListTextsCompletesWith(new TextInventoryResponse([new TextChoiceSummary(TextId, "Alpha")], true));
        await ChooseProjectAsync(fake, projectPicker, workspace, ProjectPath, NewToken());
        workspace.Selection.Texts[0].IsChecked = true;
        var setup = workspace.Context.Setup!;
        setup.Step = 3;
        var outcome = new TaskCompletionSource<CommandOutcome<AssessCommandResponse>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        fake.OnAssess((_, progress, _) =>
        {
            progress.Report(new AssessmentProgress(AssessmentStage.Capturing, 0, null, "Starting the Assessment..."));
            return outcome.Task;
        });

        var finishing = setup.FinishCommand.ExecuteAsync(null);

        Assert.True(workspace.Assess.IsActive);
        Assert.False(setup.IsOpen);

        outcome.SetResult(CommandOutcome<AssessCommandResponse>.Success(NewAssessResponse("first run")));
        await finishing;
        await workspace.Assess.RunCommand.ExecutionTask!;
        Assert.Equal(RunState.Completed, workspace.Assess.State);
    }

    [Fact]
    public async Task FirstRunKeepsSetupOpenWhenAssessmentIsRefusedBeforeStarting()
    {
        var (fake, projectPicker, _, _, workspace) = NewWorkspace();
        fake.ListTextsCompletesWith(new TextInventoryResponse([new TextChoiceSummary(TextId, "Alpha")], true));
        await ChooseProjectAsync(fake, projectPicker, workspace, ProjectPath, NewToken());
        workspace.Selection.Texts[0].IsChecked = true;
        var setup = workspace.Context.Setup!;
        setup.Step = 3;
        var refusal = new Refusal("assess.parser-unavailable", FailureReason.Refused, "The parser is unavailable.");
        fake.AssessRefusesWith(refusal);

        await setup.FinishCommand.ExecuteAsync(null);

        Assert.True(setup.IsOpen);
        Assert.Equal(refusal.Code, setup.ShownRefusal?.Code);
    }

    [Fact]
    public async Task FirstRunDoesNotReopenSetupWhenAnAssessmentFailsAfterItStarts()
    {
        var (fake, projectPicker, _, _, workspace) = NewWorkspace();
        fake.ListTextsCompletesWith(new TextInventoryResponse([new TextChoiceSummary(TextId, "Alpha")], true));
        await ChooseProjectAsync(fake, projectPicker, workspace, ProjectPath, NewToken());
        workspace.Selection.Texts[0].IsChecked = true;
        var setup = workspace.Context.Setup!;
        setup.Step = 3;
        var outcome = new TaskCompletionSource<CommandOutcome<AssessCommandResponse>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        fake.OnAssess((_, progress, _) =>
        {
            progress.Report(new AssessmentProgress(AssessmentStage.Parsing, 0, 1, "Parsing the Selection..."));
            return outcome.Task;
        });

        var finishing = setup.FinishCommand.ExecuteAsync(null);

        Assert.True(workspace.Assess.IsActive);
        Assert.False(setup.IsOpen);
        outcome.SetResult(CommandOutcome<AssessCommandResponse>.Refused(new Refusal(
            "assess.parser-refused", FailureReason.Refused, "The parser failed during the Assessment.")));
        await finishing;
        await workspace.Assess.RunCommand.ExecutionTask!;

        Assert.Equal(RunState.Refused, workspace.Assess.State);
        Assert.False(setup.IsOpen);
        Assert.Null(setup.ShownRefusal);
    }

    [Fact]
    public async Task ConfigureStaysOpenWhenAnotherAssessmentReportsProgress()
    {
        var (fake, projectPicker, _, _, workspace) = NewWorkspace();
        fake.ListTextsCompletesWith(new TextInventoryResponse([new TextChoiceSummary(TextId, "Alpha")], true));
        await ChooseProjectAsync(fake, projectPicker, workspace, ProjectPath, NewToken());
        workspace.Selection.Texts[0].IsChecked = true;

        var allowProgress = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var parsingDisplayed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelledRefusal = new Refusal(
            "assessment.cancelled", FailureReason.Cancelled, "The Assessment run was cancelled.");
        fake.OnAssess(async (_, progress, cancellationToken) =>
        {
            await allowProgress.Task.WaitAsync(cancellationToken);
            progress.Report(new AssessmentProgress(AssessmentStage.Parsing, 0, 1, "Parsing..."));
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return CommandOutcome<AssessCommandResponse>.Refused(cancelledRefusal);
            }
            throw new InvalidOperationException("The held Assessment completed before it was cancelled.");
        });

        workspace.Assess.PropertyChanged += (_, changed) =>
        {
            if (changed.PropertyName == nameof(AssessViewModel.Progress) &&
                workspace.Assess.Progress?.Stage == AssessmentStage.Parsing)
                parsingDisplayed.TrySetResult();
        };

        var running = workspace.Assess.RunCommand.ExecuteAsync(null);
        Assert.True(workspace.Assess.IsActive);
        Assert.Null(workspace.Assess.Progress);
        Assert.True(workspace.ConfigureCommand.CanExecute(null));
        workspace.ConfigureCommand.Execute(null);
        Assert.True(workspace.Context.Setup!.IsOpen);

        allowProgress.SetResult();
        await parsingDisplayed.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(AssessmentStage.Parsing, workspace.Assess.Progress?.Stage);
        Assert.True(workspace.Context.Setup.IsOpen);
        workspace.Assess.CancelCommand.Execute(null);
        await running;
        Assert.Equal(RunState.Cancelled, workspace.Assess.State);
        Assert.True(workspace.Context.Setup.IsOpen);
        await workspace.Context.Setup.SkipCommand.ExecuteAsync(null);
        Assert.False(workspace.Context.Setup.IsOpen);
    }

    [Fact]
    public async Task AStepLimitWhoseTimeEstimateDoesNotFitTheStoredLimitCanFinishWithoutATimeLimit()
    {
        var (fake, projectPicker, _, _, workspace) = NewWorkspace();
        fake.ListTextsCompletesWith(new TextInventoryResponse([new TextChoiceSummary(TextId, "Alpha")], true));
        await ChooseProjectAsync(fake, projectPicker, workspace, ProjectPath, NewToken());
        workspace.Selection.Texts[0].IsChecked = true;
        var setup = workspace.Context.Setup!;
        setup.Step = 3;
        setup.StepLimitSteps = 1_000_000_000_000m;
        fake.AssessCompletesWith(NewAssessResponse("first run"));

        Assert.True(setup.FinishCommand.CanExecute(null));
        await setup.FinishCommand.ExecuteAsync(null);

        Assert.Null(Assert.Single(fake.SetDefaultSelectionRequests).PerWordLimitMs);
        Assert.Null(Assert.Single(fake.AssessRequests).PerWordLimitMs);
    }

    [Fact]
    public async Task FirstRunCanUseNoStepLimit()
    {
        var (fake, projectPicker, _, _, workspace) = NewWorkspace();
        fake.ListTextsCompletesWith(new TextInventoryResponse([new TextChoiceSummary(TextId, "Alpha")], true));
        await ChooseProjectAsync(fake, projectPicker, workspace, ProjectPath, NewToken());
        workspace.Selection.Texts[0].IsChecked = true;
        workspace.Context.Setup!.IsStepLimitUnbounded = true;
        workspace.Context.Setup.Step = 3;
        fake.AssessCompletesWith(NewAssessResponse("first run"));

        await workspace.Context.Setup.FinishCommand.ExecuteAsync(null);

        var request = Assert.Single(fake.AssessRequests);
        Assert.Equal(StepCap.Unbounded, request.PerWordStepLimit);
        Assert.Null(request.PerWordLimitMs);
        Assert.Null(Assert.Single(fake.SetDefaultSelectionRequests).PerWordLimitMs);
        Assert.Contains("No step limit", workspace.Context.Setup!.StepLimitEstimateText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FirstRunCannotStartBeforeTheLastSetupStep()
    {
        var (fake, projectPicker, _, _, workspace) = NewWorkspace();
        fake.ListTextsCompletesWith(new TextInventoryResponse([new TextChoiceSummary(TextId, "Alpha")], true));
        await ChooseProjectAsync(fake, projectPicker, workspace, ProjectPath, NewToken());
        workspace.Selection.Texts[0].IsChecked = true;

        await workspace.Context.Setup!.FinishCommand.ExecuteAsync(null);

        Assert.Empty(fake.SetDefaultSelectionRequests);
        Assert.Empty(fake.AssessRequests);
        Assert.True(workspace.Context.Setup.IsOpen);
    }

    // The owner's first run: a project chosen before any capture showed no Texts even after Refresh succeeded.
    [Fact]
    public async Task RefreshingAProjectThatHadNoBaselineLoadsItsTextsWithoutChoosingItAgain()
    {
        var (fake, projectPicker, _, _, workspace) = NewWorkspace();
        fake.CurrentBaselineCompletesWith(new CurrentBaselineResponse(null, null, false));
        fake.ListTextsCompletesWith(new TextInventoryResponse([], HasBaseline: false));
        projectPicker.PathToReturn = ProjectPath;
        await workspace.Project.BrowseCommand.ExecuteAsync(null);
        Assert.False(workspace.Context.Setup!.IsOpen);
        Assert.False(workspace.ConfigureCommand.CanExecute(null));
        Assert.Equal("Capture a Baseline to choose Texts.", workspace.Selection.TextsEmptyMessage);

        fake.CaptureBaselineCompletesWith(new BaselineCaptureResponse(
            NewToken(), ProjectPath, DateTimeOffset.UtcNow, false, false));
        fake.ListTextsCompletesWith(new TextInventoryResponse([new TextChoiceSummary(TextId, "Alpha")], HasBaseline: true));
        await workspace.Baseline.RefreshCommand.ExecuteAsync(null);

        Assert.True(workspace.Context.Setup.IsOpen);
        Assert.Equal(0, workspace.Context.Setup.Step);
        Assert.Equal("Alpha", Assert.Single(workspace.Selection.Texts).Title);
        Assert.Null(workspace.Selection.TextsEmptyMessage);
        Assert.Equal(ProjectPath, Assert.Single(fake.ListTextsRequests.Skip(1)).ProjectPath);
        Assert.False(workspace.RerunOffered);
    }

    [Fact]
    public async Task StoredSkipSuppressesFirstOpenButConfigureCanReopenSetup()
    {
        var (fake, projectPicker, _, _, workspace) = NewWorkspace();
        fake.DefaultSelectionResponseIs(JsonSerializer.Deserialize<DefaultSelectionResponse>("""
            {"Selection":null,"SetupSkipped":true}
            """)!);
        fake.ListTextsCompletesWith(new TextInventoryResponse([new TextChoiceSummary(TextId, "Alpha")], true));
        await ChooseProjectAsync(fake, projectPicker, workspace, ProjectPath, NewToken());

        Assert.False(workspace.Context.Setup!.IsOpen);

        workspace.ConfigureCommand.Execute(null);

        Assert.True(workspace.Context.Setup.IsOpen);
        Assert.Equal(0, workspace.Context.Setup.Step);
    }

    [Fact]
    public async Task WithoutABaselineConfigureIsUnavailableAndSaysToRefreshFirst()
    {
        var (fake, projectPicker, _, _, workspace) = NewWorkspace();
        Assert.False(workspace.ConfigureCommand.CanExecute(null));
        Assert.Equal("Texts, added words and limits", workspace.ConfigureDetailText);
        fake.CurrentBaselineCompletesWith(new CurrentBaselineResponse(null, null, false));
        fake.ListTextsCompletesWith(new TextInventoryResponse([], HasBaseline: false));
        projectPicker.PathToReturn = ProjectPath;
        await workspace.Project.BrowseCommand.ExecuteAsync(null);

        Assert.Equal("Capture a Baseline to choose Texts.", workspace.Selection.TextsEmptyMessage);
        Assert.False(workspace.ConfigureCommand.CanExecute(null));
        Assert.Equal(WorkspaceShellViewModel.ConfigureNeedsBaselineText, workspace.ConfigureDetailText);

        fake.CaptureBaselineCompletesWith(new BaselineCaptureResponse(
            NewToken(), ProjectPath, DateTimeOffset.UtcNow, false, false));
        fake.ListTextsCompletesWith(new TextInventoryResponse([new TextChoiceSummary(TextId, "Alpha")], true));
        await workspace.Baseline.RefreshCommand.ExecuteAsync(null);

        Assert.True(workspace.ConfigureCommand.CanExecute(null));
        Assert.Equal("Texts, added words and limits", workspace.ConfigureDetailText);
        var page = workspace.CurrentPage;
        workspace.ConfigureCommand.Execute(null);
        Assert.True(workspace.Context.Setup!.IsOpen);
        Assert.Equal(0, workspace.Context.Setup.Step);
        Assert.Equal(page, workspace.CurrentPage);
    }

    [Fact]
    public async Task ConfigureAfterFinishingSetupShowsTheSelectionJustSaved()
    {
        var (fake, projectPicker, _, _, workspace) = NewWorkspace();
        fake.ListTextsCompletesWith(new TextInventoryResponse([new TextChoiceSummary(TextId, "Alpha")], true));
        await ChooseProjectAsync(fake, projectPicker, workspace, ProjectPath, NewToken());
        var setup = workspace.Context.Setup!;
        workspace.Selection.Texts[0].IsChecked = true;
        workspace.Selection.PastedWords = "added";
        setup.StepLimitSteps = 4321;
        setup.Step = 3;
        fake.AssessCompletesWith(NewAssessResponse("first run"));
        await setup.FinishCommand.ExecuteAsync(null);
        Assert.False(setup.IsOpen);
        workspace.Selection.Texts[0].IsChecked = false;
        workspace.Selection.PastedWords = string.Empty;

        workspace.ConfigureCommand.Execute(null);

        AssertConfigureShows(workspace, "added", 1m, 4321m);
    }

    [Fact]
    public async Task ConfigureAfterSkippingSetupInThisSessionReopensIt()
    {
        var (fake, projectPicker, _, _, workspace) = NewWorkspace();
        fake.ListTextsCompletesWith(new TextInventoryResponse([new TextChoiceSummary(TextId, "Alpha")], true));
        await ChooseProjectAsync(fake, projectPicker, workspace, ProjectPath, NewToken());
        await workspace.Context.Setup!.SkipCommand.ExecuteAsync(null);
        Assert.False(workspace.Context.Setup.IsOpen);

        Assert.True(workspace.ConfigureCommand.CanExecute(null));
        workspace.ConfigureCommand.Execute(null);

        Assert.True(workspace.Context.Setup.IsOpen);
        Assert.Equal(0, workspace.Context.Setup.Step);
        Assert.Equal("Start first run", workspace.Context.Setup.FinishButtonText);
    }

    [Fact]
    public async Task ConfigureAfterARefreshOpensWithTheSavedSelection()
    {
        var (fake, projectPicker, _, _, workspace) = NewWorkspace();
        fake.DefaultSelectionResponseIs(SavedSelection());
        fake.ListTextsCompletesWith(new TextInventoryResponse([new TextChoiceSummary(TextId, "Alpha")], true));
        await ChooseProjectAsync(fake, projectPicker, workspace, ProjectPath, NewToken());
        fake.CaptureBaselineCompletesWith(new BaselineCaptureResponse(
            NewToken(), ProjectPath, DateTimeOffset.UtcNow, false, false));
        await workspace.Baseline.RefreshCommand.ExecuteAsync(null);
        Assert.False(workspace.Context.Setup!.IsOpen);

        Assert.True(workspace.ConfigureCommand.CanExecute(null));
        workspace.ConfigureCommand.Execute(null);

        AssertConfigureShows(workspace, "one", 1.25m, 987m);
    }

    [Fact]
    public async Task ConfigureAfterReopeningTheProjectShowsTheStoredSelection()
    {
        var (fake, projectPicker, _, _, workspace) = NewWorkspace();
        fake.DefaultSelectionResponseIs(SavedSelection());
        fake.ListTextsCompletesWith(new TextInventoryResponse([new TextChoiceSummary(TextId, "Alpha")], true));
        await ChooseProjectAsync(fake, projectPicker, workspace, ProjectPath, NewToken());
        await ChooseProjectAsync(fake, projectPicker, workspace, @"C:\projects\two.fwdata", NewToken());
        await ChooseProjectAsync(fake, projectPicker, workspace, ProjectPath, NewToken());
        Assert.False(workspace.Context.Setup!.IsOpen);

        Assert.True(workspace.ConfigureCommand.CanExecute(null));
        workspace.ConfigureCommand.Execute(null);

        AssertConfigureShows(workspace, "one", 1.25m, 987m);
    }

    private static DefaultSelectionResponse SavedSelection() =>
        JsonSerializer.Deserialize<DefaultSelectionResponse>("""
            {"Selection":{"Name":"Default","TextIds":["11111111-1111-1111-1111-111111111111"],
             "AddedWords":["one"],"CreatedUtc":"created","UpdatedUtc":"updated",
             "PerWordLimitMs":1250,"PerWordStepLimit":{"steps":987,"isUnbounded":false}},"SetupSkipped":false}
            """)!;

    private static void AssertConfigureShows(
        WorkspaceShellViewModel workspace, string addedWords, decimal timeLimitSeconds, decimal stepLimit)
    {
        var setup = workspace.Context.Setup!;
        Assert.True(setup.IsOpen);
        Assert.Equal(0, setup.Step);
        Assert.True(Assert.Single(workspace.Selection.Texts).IsChecked);
        Assert.Equal(addedWords, workspace.Selection.PastedWords);
        Assert.Equal(timeLimitSeconds, workspace.Selection.PerWordTimeLimitSeconds);
        Assert.Equal(stepLimit, setup.StepLimitSteps);
        Assert.Equal("Use this Selection", setup.FinishButtonText);
    }

    [Fact]
    public async Task LaterRunsUseLimitsSavedWithTheDefaultSelection()
    {
        var (fake, projectPicker, _, _, workspace) = NewWorkspace();
        fake.DefaultSelectionResponseIs(JsonSerializer.Deserialize<DefaultSelectionResponse>("""
            {"Selection":{"Name":"Default","TextIds":["11111111-1111-1111-1111-111111111111"],
             "AddedWords":[],"CreatedUtc":"created","UpdatedUtc":"updated",
             "PerWordLimitMs":450,"PerWordStepLimit":{"steps":1234,"isUnbounded":false}},"SetupSkipped":false}
            """)!);
        fake.ListTextsCompletesWith(new TextInventoryResponse([new TextChoiceSummary(TextId, "Alpha")], true));
        fake.OnShowConfig((_, _) => Task.FromResult(CommandOutcome<ProjectConfigurationProjection>.Success(
            new ProjectConfigurationProjection(true, true,
                [new AssessmentScopeProjection("default", "all words", "pangloss", [], 2000, new StepCap(9876))]))));
        await ChooseProjectAsync(fake, projectPicker, workspace, ProjectPath, NewToken());
        fake.AssessCompletesWith(NewAssessResponse("saved limits"));

        await workspace.Assess.RunCommand.ExecuteAsync(null);

        var request = Assert.Single(fake.AssessRequests);
        Assert.Equal(450, request.PerWordLimitMs);
        Assert.Equal(new StepCap(1234), request.Selection!.PerWordStepLimit);
    }

    [Fact]
    public async Task RefreshingKeepsACheckedTextTheNewBaselineStillHoldsAndTheOtherSources()
    {
        var (fake, projectPicker, _, _, workspace) = NewWorkspace();
        var goneId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        fake.CurrentBaselineCompletesWith(new CurrentBaselineResponse(NewToken(), DateTimeOffset.UtcNow, false));
        fake.ListTextsCompletesWith(new TextInventoryResponse(
            [new TextChoiceSummary(TextId, "Alpha"), new TextChoiceSummary(goneId, "Gone")], HasBaseline: true));
        projectPicker.PathToReturn = ProjectPath;
        await workspace.Project.BrowseCommand.ExecuteAsync(null);
        workspace.Selection.Texts.Single(text => text.Id == TextId).IsChecked = true;
        workspace.Selection.Texts.Single(text => text.Id == goneId).IsChecked = true;
        workspace.Selection.PastedWords = "kept";

        fake.CaptureBaselineCompletesWith(new BaselineCaptureResponse(
            NewToken("2026-09-06T00:00:00Z"), ProjectPath, DateTimeOffset.UtcNow, false, false));
        fake.ListTextsCompletesWith(new TextInventoryResponse([new TextChoiceSummary(TextId, "Alpha")], HasBaseline: true));
        await workspace.Baseline.RefreshCommand.ExecuteAsync(null);

        Assert.Equal([TextId], workspace.Selection.ChosenTextIds);
        Assert.Equal("kept", workspace.Selection.PastedWords);
        Assert.Equal($"1 text, 1 pasted word, step cap {StepCap.DefaultSteps:N0}", workspace.Selection.SummaryText);
    }

    [Fact]
    public async Task ACompletedAssessmentFeedsBaselineHasAssessmentAndTheStatisticsSummary()
    {
        var (fake, projectPicker, _, _, workspace) = NewWorkspace();
        await ChooseProjectAsync(fake, projectPicker, workspace, ProjectPath, NewToken());
        workspace.Selection.AllWordforms = true;
        fake.AssessCompletesWith(NewAssessResponse("(summary)"));

        await workspace.Assess.RunCommand.ExecuteAsync(null);

        Assert.True(workspace.Baseline.HasAssessment);
        Assert.Equal("(summary)", workspace.PageModel<TimingPageModel>().Statistics.SummaryMarkdown);
        Assert.Equal("assessment/one", workspace.PageModel<TimingPageModel>().Statistics.AssessmentId);
        Assert.Equal("invocation/one", workspace.PageModel<AiHandoffPageModel>().Handoff.InvocationId);
        Assert.True(workspace.Context.HasEvidence);
        Assert.False(workspace.Context.HasNoEvidence);
    }

    [Fact]
    public async Task RefreshingAnAssessedBaselineOffersARerunAndAcceptingItRunsTheAssessmentAgain()
    {
        var (fake, projectPicker, _, _, workspace) = NewWorkspace();
        await ChooseProjectAsync(fake, projectPicker, workspace, ProjectPath, NewToken());
        workspace.Selection.AllWordforms = true;
        fake.AssessCompletesWith(NewAssessResponse("(first)"));
        await workspace.Assess.RunCommand.ExecuteAsync(null);

        fake.CaptureBaselineCompletesWith(new BaselineCaptureResponse(
            NewToken("2026-09-06T00:00:00Z"), ProjectPath, DateTimeOffset.UtcNow, false, false));
        await workspace.Baseline.RefreshCommand.ExecuteAsync(null);

        Assert.True(workspace.RerunOffered);

        fake.AssessCompletesWith(NewAssessResponse("(second)"));
        await workspace.AcceptRerunCommand.ExecuteAsync(null);

        Assert.False(workspace.RerunOffered);
        Assert.Equal(2, fake.AssessRequests.Count);
        Assert.Equal("(second)", workspace.PageModel<TimingPageModel>().Statistics.SummaryMarkdown);
    }

    [Fact]
    public async Task DismissingARerunOfferClearsItWithoutRunningAnything()
    {
        var (fake, projectPicker, _, _, workspace) = NewWorkspace();
        await ChooseProjectAsync(fake, projectPicker, workspace, ProjectPath, NewToken());
        workspace.Selection.AllWordforms = true;
        fake.AssessCompletesWith(NewAssessResponse("(first)"));
        await workspace.Assess.RunCommand.ExecuteAsync(null);
        fake.CaptureBaselineCompletesWith(new BaselineCaptureResponse(
            NewToken("2026-09-06T00:00:00Z"), ProjectPath, DateTimeOffset.UtcNow, false, false));
        await workspace.Baseline.RefreshCommand.ExecuteAsync(null);

        workspace.DismissRerunCommand.Execute(null);

        Assert.False(workspace.RerunOffered);
        Assert.Single(fake.AssessRequests);
    }

    [Fact]
    public async Task ChoosingAnotherProjectClearsThePreviousProjectsAssessmentAndStatisticsState()
    {
        var (fake, projectPicker, _, _, workspace) = NewWorkspace();
        await ChooseProjectAsync(fake, projectPicker, workspace, ProjectPath, NewToken());
        workspace.Selection.AllWordforms = true;
        fake.AssessCompletesWith(NewAssessResponse("(summary)"));
        await workspace.Assess.RunCommand.ExecuteAsync(null);
        fake.StatsCompletesWith(new StatsCommandResponse(
            "assessment/one", "grammar.json", "cache.sqlite", null,
            [JsonDocument.Parse("""{"kind":"word"}""").RootElement.Clone()]));
        await workspace.PageModel<TimingPageModel>().Statistics.LoadCommand.ExecuteAsync(null);

        await ChooseProjectAsync(fake, projectPicker, workspace, @"C:\projects\two.fwdata");

        Assert.False(workspace.Context.HasEvidence);
        Assert.True(workspace.Context.HasNoEvidence);
        Assert.Null(workspace.PageModel<TimingPageModel>().Statistics.SummaryMarkdown);
        Assert.Null(workspace.PageModel<TimingPageModel>().Statistics.AssessmentId);
        Assert.Null(workspace.PageModel<AiHandoffPageModel>().Handoff.InvocationId);
        Assert.Empty(workspace.PageModel<TimingPageModel>().Statistics.Rows);
        Assert.Equal(RunState.Idle, workspace.Assess.State);
        Assert.Null(workspace.Assess.Result);
        Assert.Empty(workspace.PageModel<AiHandoffPageModel>().Handoff.Files);
    }

    [Fact]
    public async Task TheFullAgreedWorkflowCompletesAndProducesDraggableHandoffFiles()
    {
        var (fake, projectPicker, folderPicker, dragSource, workspace) = NewWorkspace();
        fake.CurrentBaselineCompletesWith(new CurrentBaselineResponse(NewToken(), DateTimeOffset.UtcNow, false));
        fake.ListTextsCompletesWith(new TextInventoryResponse([new TextChoiceSummary(TextId, "Alpha")], HasBaseline: true));
        projectPicker.PathToReturn = ProjectPath;
        await workspace.Project.BrowseCommand.ExecuteAsync(null);

        workspace.Selection.Texts[0].IsChecked = true;
        workspace.Selection.PastedWords = "one\ntwo";
        Assert.True(workspace.Selection.CanAssess);

        fake.AssessCompletesWith(NewAssessResponse("(summary)"));
        await workspace.Assess.RunCommand.ExecuteAsync(null);
        Assert.Equal(RunState.Completed, workspace.Assess.State);

        fake.StatsCompletesWith(new StatsCommandResponse(
            "assessment/one", "grammar.json", "cache.sqlite", null,
            [JsonDocument.Parse("""{"kind":"word","attempts":1}""").RootElement.Clone()]));
        workspace.PageModel<TimingPageModel>().Statistics.SelectedGroup = workspace.PageModel<TimingPageModel>().Statistics.Groups[1];
        await workspace.PageModel<TimingPageModel>().Statistics.LoadCommand.ExecuteAsync(null);
        workspace.PageModel<TimingPageModel>().Statistics.FilterText = "word";
        workspace.PageModel<TimingPageModel>().Statistics.SortBy("attempts");
        Assert.Single(workspace.PageModel<TimingPageModel>().Statistics.Rows);
        Assert.Equal(workspace.PageModel<TimingPageModel>().Statistics.Groups[1], Assert.Single(fake.StatsRequests).ForwardedArguments[1]);
        Assert.Equal("assessment/one", Assert.Single(fake.StatsRequests).AssessmentId);

        fake.CaptureBaselineCompletesWith(new BaselineCaptureResponse(
            NewToken("2026-09-06T00:00:00Z"), ProjectPath, DateTimeOffset.UtcNow, false, false));
        await workspace.Baseline.RefreshCommand.ExecuteAsync(null);
        Assert.True(workspace.RerunOffered);
        fake.AssessCompletesWith(NewAssessResponse("(rerun)"));
        await workspace.AcceptRerunCommand.ExecuteAsync(null);
        Assert.False(workspace.RerunOffered);

        folderPicker.PathToReturn = @"C:\out";
        fake.HandoffCompletesWith(new HandoffCommandResponse(
            @"C:\out",
            new BaselineCaptureResponse(NewToken(), ProjectPath, DateTimeOffset.UtcNow, false, false),
            new SelectionProjection([], []), ["grammar.json"], ["assessment/two"])
        {
            InvocationId = "invocation/one",
        });
        await workspace.PageModel<AiHandoffPageModel>().Handoff.RunCommand.ExecuteAsync(null);

        Assert.Equal(RunState.Completed, workspace.PageModel<AiHandoffPageModel>().Handoff.State);
        var file = Assert.Single(workspace.PageModel<AiHandoffPageModel>().Handoff.Files);
        await workspace.PageModel<AiHandoffPageModel>().Handoff.DragFileAsync(null!, file);
        Assert.Equal([file.FullPath], dragSource.LastPaths);
    }

    private sealed class FakeProjectPicker : IProjectPicker
    {
        public string? PathToReturn { get; set; }

        public Task<string?> PickProjectFileAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(PathToReturn);
    }

    private sealed class FakeFolderPicker : IHandoffFolderPicker
    {
        public string? PathToReturn { get; set; } = @"C:\out";

        public Task<string?> PickFolderAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(PathToReturn);
    }

    private sealed class FakeDragSource : IFileDragSource
    {
        public IReadOnlyList<string>? LastPaths { get; private set; }

        public Task<DragDropEffects> StartDragAsync(
            PointerPressedEventArgs trigger, IReadOnlyList<string> filePaths, DragDropEffects allowedEffects)
        {
            LastPaths = filePaths;
            return Task.FromResult(allowedEffects);
        }
    }
}
