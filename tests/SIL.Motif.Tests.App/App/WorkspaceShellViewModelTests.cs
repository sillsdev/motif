using System.Text.Json;
using Avalonia.Input;
using CommunityToolkit.Mvvm.Input;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Pins <see cref="WorkspaceShellViewModel"/>'s composition: choosing a project loads Baseline and Text
/// state and propagates the project to every child; a completed Assessment feeds
/// <see cref="WorkspaceContext.NeedsAssessment"/> and <see cref="StatisticsViewModel.SummaryMarkdown"/>; a
/// Refresh leaves a Parse all words action, and the saved Default Selection runs with its stored limits;
/// and choosing another project clears what the previous one displayed. The last test exercises the whole
/// agreed workflow end to end.
/// </summary>
[Collection(AvaloniaHeadlessCollection.Name)]
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
        FakeDragSource DragSource, WorkspaceShellViewModel Workspace) NewWorkspace(TimeProvider? clock = null)
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
            fake, clock);
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProjectShutdownAwaitsTraceUnwindAndCancelsContextReads(bool switchProject)
    {
        var (fake, picker, _, _, workspace) = NewWorkspace();
        await ChooseProjectAsync(fake, picker, workspace, ProjectPath);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var unwind = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fake.OnTraceWord(async (_, token) =>
        {
            using var registration = token.Register(() => cancelled.TrySetResult());
            await unwind.Task;
            return CommandOutcome<WordTraceResponse>.Refused(new Refusal("trace.cancelled",
                FailureReason.Cancelled, "Cancelled"));
        });
        var contextReads = new List<CancellationToken>();
        fake.WordContextHandler = async (_, token) =>
        {
            contextReads.Add(token);
            await Task.Delay(Timeout.Infinite, token);
            return CommandOutcome<WordContextResponse>.Success(new("motifa", false));
        };
        var trace = workspace.Assess.Trace;
        trace.Result = WordTraceQuery.LoadDiagnostic(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "TestFixtures", "trace-details-v3-hawajafika.json"))).Value!;
        trace.SetWord("motifa");
        var running = trace.TryCommand.ExecuteAsync(null);
        var oldReads = contextReads.ToArray();
        Assert.NotEmpty(oldReads);
        var stopping = switchProject
            ? workspace.Context.OpenProjectAsync(@"C:\projects\two.fwdata")
            : workspace.DisposeAsync().AsTask();
        try
        {
            await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(stopping.IsCompleted, "Shutdown returned before the trace invocation unwound.");
            unwind.TrySetResult();
            await stopping.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(running.IsCompletedSuccessfully);
            Assert.False(trace.ParseProgress.IsActive);
            Assert.All(oldReads, token => Assert.True(token.IsCancellationRequested));
            Assert.All(contextReads, token => Assert.True(token.IsCancellationRequested));
        }
        finally
        {
            trace.CancelCommand.Execute(null);
            unwind.TrySetResult();
            await running.WaitAsync(TimeSpan.FromSeconds(5));
            await stopping.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisposalWaitsForTheProjectMenuReadEvenWhenItFails(bool failRead)
    {
        var response = new TaskCompletionSource<IReadOnlyList<KnownProjectSummary>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var (client, _, _, _, workspace) = NewWorkspace();
        client.OnListKnownProjects(_ => response.Task);
        var refresh = workspace.RefreshKnownProjectsAsync();
        var disposal = workspace.DisposeAsync().AsTask();

        try
        {
            Assert.False(refresh.IsCompleted);
            Assert.False(disposal.IsCompleted,
                "Disposal completed while the project menu's database read still owned its resources.");
        }
        finally
        {
            if (failRead) response.TrySetException(new IOException("Project list refused."));
            else response.TrySetResult([]);
            await refresh.WaitAsync(TimeSpan.FromSeconds(5));
            await disposal.WaitAsync(TimeSpan.FromSeconds(5));
        }

        Assert.True(disposal.IsCompletedSuccessfully);
        Assert.Empty(workspace.Project.KnownProjects);
    }

    [Fact]
    public async Task RefreshRequestedDuringAReadRunsAgainWithTheLatestKnownProjects()
    {
        var firstRead = new TaskCompletionSource<IReadOnlyList<KnownProjectSummary>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var firstReadStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var latest = new KnownProjectSummary(@"C:\projects\latest.fwdata", DateTimeOffset.UtcNow);
        var (client, _, _, _, workspace) = NewWorkspace();
        var reads = 0;
        client.OnListKnownProjects(_ =>
        {
            if (Interlocked.Increment(ref reads) == 1)
            {
                firstReadStarted.TrySetResult();
                return firstRead.Task;
            }

            return Task.FromResult<IReadOnlyList<KnownProjectSummary>>([latest]);
        });

        var refresh = workspace.RefreshKnownProjectsAsync();
        await firstReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var refreshAfterChange = workspace.RefreshKnownProjectsAsync();
        firstRead.TrySetResult([]);

        await refreshAfterChange.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(2, reads);
        Assert.Equal(latest.FullFwDataPath, Assert.Single(workspace.Project.KnownProjects).FullFwDataPath);
        await workspace.DisposeAsync();
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
        var usage = Assert.Single(fake.UsageEntries);
        Assert.Equal("project browse", usage.Command);
        Assert.Equal(["fwDataPath:text"], usage.ArgumentShape);
    }

    [Fact]
    public async Task ConfigureOpensSetupWithoutNavigatingAwayFromTheCurrentPage()
    {
        var (fake, projectPicker, _, _, workspace) = NewWorkspace();
        fake.DefaultSelectionResponseIs(SavedSelection(["one", "two"], 1250, 987));
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
        Assert.Collection(fake.UsageEntries,
            entry => Assert.Equal("project browse", entry.Command),
            entry => Assert.Equal("setup skip", entry.Command));
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
        var savedLimits = savedJson.RootElement.GetProperty("Limits");
        Assert.Equal((int)SelectionTimeLimitMode.Estimated, savedLimits.GetProperty("TimeMode").GetInt32());
        Assert.True(savedLimits.TryGetProperty("ExplicitPerWordLimitMs", out var explicitLimit));
        Assert.Equal(JsonValueKind.Null, explicitLimit.ValueKind);
        Assert.Equal(SIL.Motif.Contract.Assess.StepCap.DefaultSteps,
            savedLimits.GetProperty("PerWordStepLimit").GetProperty("steps").GetInt64());
        var assess = Assert.Single(fake.AssessRequests);
        Assert.Null(assess.Selection);
        Assert.Equal(8_000, assess.PerWordLimitMs);
        Assert.Equal(StepCap.Default, assess.PerWordStepLimit);
        Assert.Collection(fake.UsageEntries,
            entry => Assert.Equal("project browse", entry.Command),
            entry => Assert.Equal("selection set-default", entry.Command));
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
        Assert.Collection(fake.UsageEntries,
            entry => Assert.Equal("project browse", entry.Command),
            entry => Assert.Equal("selection set-default", entry.Command));
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
        Assert.Contains("No analysis attempt limit", workspace.Context.Setup!.StepLimitEstimateText, StringComparison.Ordinal);
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
    public async Task ChoosingAProjectWithoutABaselineCapturesItOnFirstOpen()
    {
        var (fake, projectPicker, _, _, workspace) = NewWorkspace();
        fake.CurrentBaselineCompletesWith(new CurrentBaselineResponse(null, null, false));
        var textReads = 0;
        fake.OnListTexts((_, _) => Task.FromResult(CommandOutcome<TextInventoryResponse>.Success(
            Interlocked.Increment(ref textReads) == 1
                ? new TextInventoryResponse([], HasBaseline: false)
                : new TextInventoryResponse([new TextChoiceSummary(TextId, "Alpha")], HasBaseline: true))));
        fake.CaptureBaselineCompletesWith(new BaselineCaptureResponse(
            NewToken(), ProjectPath, DateTimeOffset.UtcNow, false, false));
        projectPicker.PathToReturn = ProjectPath;
        await workspace.Project.BrowseCommand.ExecuteAsync(null);

        Assert.Single(fake.CaptureBaselineRequests);
        Assert.True(workspace.Baseline.HasBaseline);
        var setup = Assert.IsType<SetupViewModel>(workspace.Context.Setup);
        Assert.True(setup.IsOpen);
        Assert.Equal(0, setup.Step);
        Assert.Equal("Alpha", Assert.Single(workspace.Selection.Texts).Title);
        Assert.Null(workspace.Selection.TextsEmptyMessage);
        Assert.Equal(2, textReads);
        Assert.All(fake.ListTextsRequests, request => Assert.Equal(ProjectPath, request.ProjectPath));
        Assert.True(workspace.Context.NeedsAssessment);
    }

    [Fact]
    public async Task BaselineDatesFromEarlierYearsIncludeTheYear()
    {
        using var culture = new CultureScope(System.Globalization.CultureInfo.GetCultureInfo("en-US"));
        var clock = new FixedClock(new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.Zero));
        var (fake, projectPicker, _, _, workspace) = NewWorkspace(clock);
        fake.CurrentBaselineCompletesWith(new CurrentBaselineResponse(
            NewToken("2024-09-05T00:00:00Z"), DateTimeOffset.Parse("2024-09-05T00:00:00Z"), false));
        fake.ListTextsCompletesWith(new TextInventoryResponse([], HasBaseline: true));
        projectPicker.PathToReturn = ProjectPath;

        await workspace.Project.BrowseCommand.ExecuteAsync(null);

        Assert.Contains("2024", workspace.FreshnessDetail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SidebarShowsLoadingWhileAProjectOpens()
    {
        var (fake, projectPicker, _, _, workspace) = NewWorkspace();
        var currentBaseline = new TaskCompletionSource<CommandOutcome<CurrentBaselineResponse>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var readStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fake.OnGetCurrentBaseline((_, _) =>
        {
            readStarted.TrySetResult();
            return currentBaseline.Task;
        });
        fake.ListTextsCompletesWith(new TextInventoryResponse([], HasBaseline: true));
        projectPicker.PathToReturn = ProjectPath;

        var opening = workspace.SetProjectAsync(ProjectPath);
        await readStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(workspace.Context.IsOpeningProject);
        Assert.Equal("Loading project...", workspace.SelectionSummaryText);

        currentBaseline.SetResult(CommandOutcome<CurrentBaselineResponse>.Success(
            new CurrentBaselineResponse(NewToken(), DateTimeOffset.UtcNow, false)));
        await opening.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(workspace.Context.IsOpeningProject);
        Assert.Equal(workspace.Selection.SummaryText, workspace.SelectionSummaryText);
    }

    [Fact]
    public async Task FreshnessRequestedAgainDuringAReadPublishesTheLaterResultBeforeItCompletes()
    {
        var (fake, projectPicker, _, _, workspace) = NewWorkspace();
        await ChooseProjectAsync(fake, projectPicker, workspace, ProjectPath);
        var staleRead = new TaskCompletionSource<CommandOutcome<CurrentBaselineResponse>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var readStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reads = 0;
        var latest = new CurrentBaselineResponse(NewToken("2026-10-03T00:00:00Z"), DateTimeOffset.UtcNow, false);
        fake.OnGetCurrentBaseline((_, _) =>
        {
            if (Interlocked.Increment(ref reads) == 1)
            {
                readStarted.TrySetResult();
                return staleRead.Task;
            }

            return Task.FromResult(CommandOutcome<CurrentBaselineResponse>.Success(latest));
        });

        var firstActivation = workspace.CheckFreshnessAsync();
        await readStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var secondActivation = workspace.CheckFreshnessAsync();
        staleRead.SetResult(CommandOutcome<CurrentBaselineResponse>.Success(
            new CurrentBaselineResponse(NewToken("2026-10-02T00:00:00Z"), DateTimeOffset.UtcNow, false)));
        await firstActivation.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Same(firstActivation, secondActivation);
        Assert.Equal(2, reads);
        Assert.Equal(latest.Token, workspace.Baseline.Token);
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
    public async Task FirstRunSetupOpensAfterTheRefreshLineStopsSayingItIsCapturing()
    {
        var (fake, _, _, _, workspace) = NewWorkspace();
        fake.CurrentBaselineCompletesWith(new CurrentBaselineResponse(null, null, false));
        fake.CaptureBaselineCompletesWith(new BaselineCaptureResponse(
            NewToken(), ProjectPath, DateTimeOffset.UtcNow, false, false));
        fake.ListTextsCompletesWith(new TextInventoryResponse(
            [new TextChoiceSummary(TextId, "Alpha")], HasBaseline: true));
        var refreshWasRunningWhenSetupOpened = true;
        workspace.Context.Setup!.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SetupViewModel.IsOpen) && workspace.Context.Setup.IsOpen)
                refreshWasRunningWhenSetupOpened = workspace.Freshness == ProjectFreshness.Refreshing;
        };

        await workspace.SetProjectAsync(ProjectPath);

        Assert.True(workspace.Context.Setup.IsOpen);
        Assert.False(refreshWasRunningWhenSetupOpened);
        Assert.NotEqual(ProjectFreshness.Refreshing, workspace.Freshness);
    }

    [Fact]
    public async Task ConfigureOpensSetupAfterTheFirstOpenCapturedItsBaseline()
    {
        var (fake, projectPicker, _, _, workspace) = NewWorkspace();
        Assert.False(workspace.ConfigureCommand.CanExecute(null));
        Assert.Equal("Texts, added words and limits", workspace.ConfigureDetailText);
        fake.CurrentBaselineCompletesWith(new CurrentBaselineResponse(null, null, false));
        fake.ListTextsCompletesWith(new TextInventoryResponse([new TextChoiceSummary(TextId, "Alpha")], true));
        fake.CaptureBaselineCompletesWith(new BaselineCaptureResponse(
            NewToken(), ProjectPath, DateTimeOffset.UtcNow, false, false));
        projectPicker.PathToReturn = ProjectPath;
        await workspace.Project.BrowseCommand.ExecuteAsync(null);

        Assert.True(workspace.Baseline.HasBaseline);
        Assert.True(workspace.ConfigureCommand.CanExecute(null));
        Assert.Equal("Texts, added words and limits", workspace.ConfigureDetailText);
        var page = workspace.CurrentPage;
        workspace.ConfigureCommand.Execute(null);
        Assert.True(workspace.Context.Setup!.IsOpen);
        Assert.Equal(0, workspace.Context.Setup.Step);
        Assert.Equal(page, workspace.CurrentPage);
    }

    [Fact]
    public async Task EachProjectWithoutAStoreIsCapturedOnItsFirstOpenOnly()
    {
        var (fake, projectPicker, _, _, workspace) = NewWorkspace();
        fake.CurrentBaselineCompletesWith(new CurrentBaselineResponse(NewToken(), DateTimeOffset.UtcNow, false));
        fake.ListTextsCompletesWith(new TextInventoryResponse([], HasBaseline: true));
        projectPicker.PathToReturn = ProjectPath;
        await workspace.Project.BrowseCommand.ExecuteAsync(null);

        fake.CurrentBaselineCompletesWith(new CurrentBaselineResponse(null, null, false));
        fake.ListTextsCompletesWith(new TextInventoryResponse([], HasBaseline: false));
        await workspace.OpenRecentProjectCommand.ExecuteAsync(new RecentProjectViewModel(ProjectPath));

        Assert.Equal(ProjectPath, workspace.Context.ProjectPath);
        Assert.False(workspace.Baseline.HasBaseline);
        Assert.Empty(fake.CaptureBaselineRequests);

        const string nextProject = @"C:\projects\two.fwdata";
        fake.OnCaptureBaseline((request, _) => Task.FromResult(
            CommandOutcome<BaselineCaptureResponse>.Success(new BaselineCaptureResponse(
                NewToken(), request.ProjectPath, DateTimeOffset.UtcNow, false, false))));
        await workspace.OpenRecentProjectCommand.ExecuteAsync(new RecentProjectViewModel(nextProject));

        Assert.Equal(nextProject, workspace.Context.ProjectPath);
        Assert.True(workspace.Baseline.HasBaseline);
        Assert.Equal(nextProject, Assert.Single(fake.CaptureBaselineRequests).ProjectPath);

        fake.CurrentBaselineCompletesWith(new CurrentBaselineResponse(null, null, false));
        await workspace.OpenRecentProjectCommand.ExecuteAsync(new RecentProjectViewModel(nextProject));

        Assert.Equal(nextProject, workspace.Context.ProjectPath);
        Assert.False(workspace.Baseline.HasBaseline);
        Assert.Single(fake.CaptureBaselineRequests);
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
        fake.DefaultSelectionResponseIs(SavedSelection(["one"], 1250, 987));
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
        fake.DefaultSelectionResponseIs(SavedSelection(["one"], 1250, 987));
        fake.ListTextsCompletesWith(new TextInventoryResponse([new TextChoiceSummary(TextId, "Alpha")], true));
        await ChooseProjectAsync(fake, projectPicker, workspace, ProjectPath, NewToken());
        await ChooseProjectAsync(fake, projectPicker, workspace, @"C:\projects\two.fwdata", NewToken());
        await ChooseProjectAsync(fake, projectPicker, workspace, ProjectPath, NewToken());
        Assert.False(workspace.Context.Setup!.IsOpen);

        Assert.True(workspace.ConfigureCommand.CanExecute(null));
        workspace.ConfigureCommand.Execute(null);

        AssertConfigureShows(workspace, "one", 1.25m, 987m);
    }

    private static DefaultSelectionResponse SavedSelection(
        IReadOnlyList<string> addedWords, int timeLimitMs, long stepLimit) => new(
        new NamedSelectionProjection("Default", [TextId], addedWords, "created", "updated",
            new SelectionParsingLimits(new StepCap(stepLimit), SelectionTimeLimitMode.Explicit, timeLimitMs), "revision"));

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
        fake.DefaultSelectionResponseIs(SavedSelection([], 450, 1234));
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
        Assert.Equal($"1 text, 1 pasted word, analysis attempt limit {StepCap.DefaultSteps:N0}", workspace.Selection.SummaryText);
    }

    [Fact]
    public async Task ACompletedAssessmentFeedsCurrentEvidenceAndTheStatisticsSummary()
    {
        var (fake, projectPicker, _, _, workspace) = NewWorkspace();
        await ChooseProjectAsync(fake, projectPicker, workspace, ProjectPath, NewToken());
        workspace.Selection.AllWordforms = true;
        fake.AssessCompletesWith(NewAssessResponse("(summary)"));

        await workspace.Assess.RunCommand.ExecuteAsync(null);

        Assert.False(workspace.Context.NeedsAssessment);
        Assert.Equal("(summary)", workspace.PageModel<TimingPageModel>().Statistics.SummaryMarkdown);
        Assert.Equal("assessment/one", workspace.PageModel<TimingPageModel>().Statistics.AssessmentId);
        Assert.Equal("invocation/one", workspace.PageModel<AiHandoffPageModel>().Handoff.InvocationId);
        Assert.True(workspace.Context.HasEvidence);
        Assert.False(workspace.Context.HasNoEvidence);
    }

    [Fact]
    public async Task RefreshOffersParseAllWordsAndRunsTheSavedSelectionWithItsSavedLimits()
    {
        var (fake, projectPicker, _, _, workspace) = NewWorkspace();
        fake.DefaultSelectionCompletesWith(new NamedSelectionProjection(
            "Default", [], ["word"], string.Empty, string.Empty, 1500, new StepCap(6600)));
        await ChooseProjectAsync(fake, projectPicker, workspace, ProjectPath, NewToken());
        fake.AssessCompletesWith(NewAssessResponse("(first)"));
        await workspace.Assess.RunCommand.ExecuteAsync(null);

        fake.CaptureBaselineCompletesWith(new BaselineCaptureResponse(
            NewToken("2026-09-06T00:00:00Z"), ProjectPath, DateTimeOffset.UtcNow, false, false));
        await workspace.RefreshCommand.ExecuteAsync(null);

        Assert.True(workspace.Context.NeedsAssessment);
        Assert.True(workspace.ShowsParseAllWordsAction);
        Assert.False(workspace.ShowsRefreshAction);
        Assert.Single(fake.AssessRequests);

        fake.AssessCompletesWith(NewAssessResponse("(second)"));
        await workspace.ParseAllWordsCommand.ExecuteAsync(null);

        Assert.False(workspace.Context.NeedsAssessment);
        Assert.True(workspace.ShowsRefreshAction);
        Assert.False(workspace.ShowsParseAllWordsAction);
        Assert.Equal(2, fake.AssessRequests.Count);
        Assert.Null(fake.AssessRequests[1].Selection);
        Assert.Equal(1500, fake.AssessRequests[1].PerWordLimitMs);
        Assert.Equal(new StepCap(6600), fake.AssessRequests[1].PerWordStepLimit);
        Assert.Equal("(second)", workspace.PageModel<TimingPageModel>().Statistics.SummaryMarkdown);
    }

    [Fact]
    public async Task CancellingParseLeavesTheParseActionAvailable()
    {
        var (fake, projectPicker, _, _, workspace) = NewWorkspace();
        fake.DefaultSelectionCompletesWith(new NamedSelectionProjection(
            "Default", [], ["word"], string.Empty, string.Empty, 1000, StepCap.Default));
        await ChooseProjectAsync(fake, projectPicker, workspace, ProjectPath, NewToken());
        fake.CaptureBaselineCompletesWith(new BaselineCaptureResponse(
            NewToken("2026-09-06T00:00:00Z"), ProjectPath, DateTimeOffset.UtcNow, false, false));
        await workspace.RefreshCommand.ExecuteAsync(null);
        fake.AssessBlocksUntilCancelled(new Refusal("assess.cancelled", FailureReason.Cancelled, "Cancelled."),
            new AssessmentProgress(AssessmentStage.Parsing, 312, 1040, "Parsing words")
            {
                CurrentWord = "word-312",
                PerWordLimitMs = 700,
                SlowestWord = new ParseWordTiming("word-200", 54000),
                StoppedWords = [new StoppedParseWord("word-201", "TIMEOUT", 700)],
            });

        var parsing = workspace.ParseAllWordsCommand.ExecuteAsync(null);
        Assert.True(workspace.ShowsParseAllWordsProgress);
        Assert.Equal("Parsing 312 of 1,040 words", workspace.ParseAllWordsProgressText);
        Assert.Same(workspace.Assess.ParseProgress, workspace.ActiveParseProgress);
        Assert.Equal("312 of 1,040 words done · Parsing word-312", workspace.ActiveParseProgress.ProgressText);
        Assert.Equal("Slowest so far: word-200 · 54 s", workspace.ActiveParseProgress.SlowestText);
        Assert.Equal("1 word timed out after 0.7 s (word-201)", workspace.ActiveParseProgress.StoppedText);
        Assert.True(workspace.PageModel<TextsPageModel>().ShowParsePrompt);
        workspace.Assess.CancelCommand.Execute(null);
        await parsing;

        Assert.True(workspace.Context.NeedsAssessment);
        Assert.True(workspace.ShowsParseAllWordsAction);
        var texts = workspace.PageModel<TextsPageModel>();
        Assert.True(texts.ShowCentredParsePrompt);
        texts.Tab = TextsTab.AnalyzeTexts;
        Assert.True(texts.ShowAnalyzeParsePrompt);
        Assert.Single(fake.AssessRequests);
    }

    [Fact]
    public async Task ARefusedParseLeavesTheParseActionAvailable()
    {
        var (fake, projectPicker, _, _, workspace) = NewWorkspace();
        fake.DefaultSelectionCompletesWith(new NamedSelectionProjection(
            "Default", [], ["word"], string.Empty, string.Empty, 1000, StepCap.Default));
        await ChooseProjectAsync(fake, projectPicker, workspace, ProjectPath, NewToken());
        fake.CaptureBaselineCompletesWith(new BaselineCaptureResponse(
            NewToken("2026-09-06T00:00:00Z"), ProjectPath, DateTimeOffset.UtcNow, false, false));
        await workspace.RefreshCommand.ExecuteAsync(null);
        fake.AssessRefusesWith(new Refusal("assess.refused", FailureReason.Refused, "The parser declined."));

        await workspace.ParseAllWordsCommand.ExecuteAsync(null);

        Assert.Equal(RunState.Refused, workspace.Assess.State);
        Assert.True(workspace.Context.NeedsAssessment);
        Assert.True(workspace.ShowsParseAllWordsAction);
        var texts = workspace.PageModel<TextsPageModel>();
        Assert.True(texts.ShowAssessRefusal);
        Assert.False(texts.ShowParsePrompt);
        Assert.False(texts.ShowCentredParsePrompt);
        texts.Tab = TextsTab.AnalyzeTexts;
        Assert.False(texts.ShowAnalyzeParsePrompt);
        Assert.True(texts.ShowAnalyzeTextsContent);

        await workspace.RefreshCommand.ExecuteAsync(null);

        Assert.True(texts.ShowAssessRefusal);
        Assert.False(texts.ShowParsePrompt);
        fake.CaptureBaselineCompletesWith(new BaselineCaptureResponse(
            NewToken("2026-09-07T00:00:00Z"), ProjectPath, DateTimeOffset.UtcNow, false, false));
        await workspace.RefreshCommand.ExecuteAsync(null);
        fake.ReadCurrentEvidenceCompletesWith(new CurrentEvidenceSnapshot("project", DateTimeOffset.UtcNow,
            null, EvidenceFreshness.Current,
            new("project-1", workspace.Baseline.Token!, "root", ProjectPath, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow),
            null, null, null, null));
        await workspace.Context.ReadStoredEvidenceAsync();

        Assert.False(texts.ShowAssessRefusal);
        Assert.True(texts.ShowParsePrompt);
        Assert.True(texts.ShowAnalyzeParsePrompt);
    }

    [Fact]
    public async Task RefreshDoesNotRunAnAssessmentAndLeavesTheNewBaselineUnmeasured()
    {
        var (fake, projectPicker, _, _, workspace) = NewWorkspace();
        await ChooseProjectAsync(fake, projectPicker, workspace, ProjectPath, NewToken());
        workspace.Selection.AllWordforms = true;
        fake.AssessCompletesWith(NewAssessResponse("before refresh"));
        await workspace.Assess.RunCommand.ExecuteAsync(null);

        fake.CaptureBaselineCompletesWith(new BaselineCaptureResponse(
            NewToken("2026-09-06T00:00:00Z"), ProjectPath, DateTimeOffset.UtcNow, false, false));
        await workspace.RefreshCommand.ExecuteAsync(null);

        Assert.Single(fake.AssessRequests);
        Assert.True(workspace.Context.NeedsAssessment);
    }

    [Fact]
    public async Task ParseDependentPagesShareThePromptAndKeepIndependentContentAvailable()
    {
        var (fake, projectPicker, _, _, workspace) = NewWorkspace();
        fake.DefaultSelectionCompletesWith(new NamedSelectionProjection(
            "Default", [TextId], [], string.Empty, string.Empty, 1000, StepCap.Default));
        fake.ListTextsCompletesWith(new TextInventoryResponse([new TextChoiceSummary(TextId, "Alpha")], HasBaseline: true));
        await ChooseProjectAsync(fake, projectPicker, workspace, ProjectPath, NewToken());

        var texts = workspace.PageModel<TextsPageModel>();
        Assert.True(workspace.Context.NeedsAssessment);
        Assert.True(texts.ShowParsePrompt);
        Assert.False(texts.ShowMatrixContent);
        Assert.Same(workspace.ParseAllWordsCommand, workspace.Context.ParseAllWordsCommand);

        texts.Tab = TextsTab.AnalyzeTexts;
        Assert.True(texts.ShowParsePrompt);
        Assert.True(texts.ShowAnalyzeParsePrompt);
        Assert.False(texts.ShowCentredParsePrompt);
        Assert.True(texts.ShowAnalyzeTextsContent);
        texts.AnalyzeView = AnalyzeTextsView.WordList;
        Assert.False(texts.ShowAnalyzeParsePrompt);
        texts.AnalyzeView = AnalyzeTextsView.TextReader;
        Assert.True(texts.ShowAnalyzeParsePrompt);
        texts.Tab = TextsTab.Lists;
        Assert.True(texts.ShowLists);
        Assert.False(texts.ShowParsePrompt);
        texts.Tab = TextsTab.WhatChanged;
        Assert.True(texts.ShowWhatChanged);
        Assert.False(texts.ShowParsePrompt);

        Assert.True(workspace.PageModel<TimingPageModel>().ShowNoEvidence);
        Assert.False(workspace.PageModel<TimingPageModel>().ShowStatistics);
        Assert.True(workspace.PageModel<OverviewPageModel>().ShowNoAssessment);
        Assert.False(workspace.PageModel<OverviewPageModel>().ShowTiles);
        Assert.True(workspace.PageModel<WarningsPageModel>().IsGrammarNotChecked);

        workspace.CurrentPage = WorkspacePage.TryAWord;
        Assert.Equal(WorkspacePage.TryAWord, workspace.CurrentPage);
        workspace.CurrentPage = WorkspacePage.AiHandoff;
        Assert.Equal(WorkspacePage.AiHandoff, workspace.CurrentPage);
        workspace.CurrentPage = WorkspacePage.Review;
        Assert.Equal(WorkspacePage.Review, workspace.CurrentPage);
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
        fake.DefaultSelectionCompletesWith(new NamedSelectionProjection(
            "Default", [TextId], [], string.Empty, string.Empty, 1000, StepCap.Default));
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
        Assert.True(workspace.Context.NeedsAssessment);
        fake.AssessCompletesWith(NewAssessResponse("(rerun)"));
        await workspace.ParseAllWordsCommand.ExecuteAsync(null);
        Assert.False(workspace.Context.NeedsAssessment);

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
