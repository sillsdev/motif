using System.Text.Json;
using Avalonia.Input;
using CommunityToolkit.Mvvm.Input;
using SIL.Motif.App;
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
/// state and propagates the project to its participants; a completed Assessment feeds
/// <see cref="WorkspaceContext.NeedsAssessment"/> and the current evidence; a
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
        workspace.Context.ViewIdentityReader = _ => Task.FromResult<string?>(null);
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

    private static async Task ChooseSettledProjectAsync(
        FakeCommandClient fake, FakeProjectPicker projectPicker, WorkspaceShellViewModel workspace,
        string projectPath, BaselineToken? token = null)
    {
        await ChooseProjectAsync(fake, projectPicker, workspace, projectPath, token);
        await Wait.UntilAsync(() => !workspace.IsProjectOpening, "The project never finished opening.");
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
        _ = workspace.Context.WordTraceSession;
        var trace = workspace.Assess.Trace;
        trace.Result = WordTraceQuery.LoadDiagnostic(SIL.Motif.Tests.TestFixtures.TestTraceFixture.Read(Path.Combine(AppContext.BaseDirectory,
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

    [UiThreadTheory]
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

    [UiThreadFact]
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

    [UiThreadFact]
    public async Task RerunningFromDiagnosticsKeepsDiagnosticsOpen()
    {
        var (fake, projectPicker, _, _, workspace) = NewWorkspace();
        workspace.Context.Diagnostics = true;
        await ChooseProjectAsync(fake, projectPicker, workspace, ProjectPath, NewToken());
        workspace.Context.OpenPage(WorkspacePage.Diagnostics);
        fake.AssessCompletesWith(NewAssessResponse("rerun"));

        await workspace.Assess.RerunAsync(["word"], 1000);

        Assert.Equal(WorkspacePage.Diagnostics, workspace.Context.CurrentPage);
    }

    [UiThreadFact]
    public async Task SwitchingProjectsClearsTheOldProjectsDiagnosticRows()
    {
        var (fake, projectPicker, _, _, workspace) = NewWorkspace();
        await ChooseSettledProjectAsync(fake, projectPicker, workspace, @"C:\projects\one.fwdata", NewToken());
        workspace.Context.Diagnostics = true;
        fake.TimingCompletesWith(new TimingResponse("assessment-parse", "all", "rule", 1, null, null, [], [], [])
        {
            ParserWorkAggregates = [new TimingWorkAggregate("rule", "Recorded rule", "morph_rule", 4, false)],
        });
        workspace.Context.PublishEvidence(new WorkspaceEvidence(WorkspaceContextTests.Assessment(), DateTimeOffset.Now, false));
        var speed = workspace.PageModel<DiagnosticsPageModel>().SpeedByRule;
        await speed.ReadAsync(@"C:\projects\one.fwdata", CancellationToken.None);
        Assert.Single(speed.Rows);

        await ChooseSettledProjectAsync(fake, projectPicker, workspace, @"C:\projects\two.fwdata", NewToken());
        Assert.Empty(speed.Rows);
    }

    [UiThreadFact]
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
        Assert.Equal(ProjectPath, workspace.PageModel<SettingsPageModel>().Handoff.Handoff.ProjectPath);
        var usage = Assert.Single(fake.UsageEntries);
        Assert.Equal("project browse", usage.Command);
        Assert.Equal(["fwDataPath:text"], usage.ArgumentShape);
    }

    [UiThreadFact]
    public async Task ConfigureOpensSetupWithoutNavigatingAwayFromTheCurrentPage()
    {
        var (fake, projectPicker, _, _, workspace) = NewWorkspace();
        fake.DefaultSelectionResponseIs(SavedSelection(["one", "two"], 1250, 987));
        fake.ListTextsCompletesWith(new TextInventoryResponse(
            [new TextChoiceSummary(TextId, "Alpha", 10, 6)], HasBaseline: true));
        fake.OnShowConfig((_, _) => Task.FromResult(CommandOutcome<ProjectConfigurationProjection>.Success(
            new ProjectConfigurationProjection(true, true,
                [new AssessmentScopeProjection("default", "all words", "pangloss", [], 750, new ParserWorkLimit(321))]))));
        await ChooseProjectAsync(fake, projectPicker, workspace, ProjectPath, NewToken());
        Assert.False(workspace.Context.Setup!.IsOpen);
        workspace.CurrentPage = WorkspacePage.Progress;

        workspace.ConfigureCommand.Execute(null);

        Assert.Equal(WorkspacePage.Progress, workspace.CurrentPage);
        Assert.True(workspace.Context.Setup!.IsOpen);
        Assert.Equal(0, workspace.Context.Setup.Step);
        Assert.True(Assert.Single(workspace.Selection.Texts).IsChecked);
        Assert.Equal(string.Join(Environment.NewLine, "one", "two"), workspace.Selection.PastedWords);
        Assert.Equal(1.25m, workspace.Selection.PerWordTimeLimitSeconds);
        Assert.Equal(987m, workspace.Selection.PerWordParserWorkLimit);
        Assert.Equal(new ParserWorkLimit(987), workspace.Selection.BuildRequest().PerWordParserWorkLimit);
        Assert.Equal(SIL.Motif.App.MotifAppStrings.UseThisSelection, workspace.Context.Setup.FinishButtonText);
        Assert.Equal("Use this Selection as the project default", workspace.Context.Setup.FinishTitle);
        Assert.DoesNotContain("Save", workspace.Context.Setup.FinishDescription);
        Assert.EndsWith(".fwdata", workspace.Context.Setup.ProjectName);
        workspace.Context.Setup.Step = 2;
        Assert.Equal("Next: Use This Selection", workspace.Context.Setup.NextButtonText);
    }

    [UiThreadFact]
    public async Task AProjectWithoutADefaultSelectionOpensSetupOnItsFirstStep()
    {
        var (fake, projectPicker, _, _, workspace) = NewWorkspace();
        await ChooseProjectAsync(fake, projectPicker, workspace, ProjectPath, NewToken());

        var setup = workspace.Context.Setup;
        Assert.NotNull(setup);
        Assert.True(setup.IsOpen);
        Assert.Equal(0, setup.Step);
    }

    [UiThreadFact]
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
                [new AssessmentScopeProjection("default", "all words", "pangloss", [], 1000, ParserWorkLimit.Default)])));
        await projectLoading;

        Assert.True(setup.IsOpen);
        Assert.Equal(1, setup.Step);
    }

    [UiThreadFact]
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

    [UiThreadFact]
    public async Task FirstRunSavesTheSelectionAndUsesItWithTheChosenParserWorkLimit()
    {
        var (fake, projectPicker, _, _, workspace) = NewWorkspace();
        fake.ListTextsCompletesWith(new TextInventoryResponse([new TextChoiceSummary(TextId, "Alpha")], true));
        await ChooseProjectAsync(fake, projectPicker, workspace, ProjectPath, NewToken());
        workspace.Selection.Texts[0].IsChecked = true;
        workspace.Selection.PastedWords = "added word";
        workspace.Context.Setup!.ParserWorkLimitValue = SIL.Motif.Contract.Assess.ParserWorkLimit.DefaultWork;
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
        Assert.Equal(SIL.Motif.Contract.Assess.ParserWorkLimit.DefaultWork,
            savedLimits.GetProperty("PerWordParserWorkLimit").GetProperty("work").GetInt64());
        var assess = Assert.Single(fake.AssessRequests);
        Assert.Null(assess.Selection);
        // The estimated time is not sent as a limit, so the command sizes the safety net the way the CLI does.
        Assert.Null(assess.PerWordLimitMs);
        Assert.Equal(ParserWorkLimit.Default, assess.PerWordParserWorkLimit);
        Assert.Collection(fake.UsageEntries,
            entry => Assert.Equal("project browse", entry.Command),
            entry => Assert.Equal("selection set-default", entry.Command));
        Assert.False(workspace.Context.Setup.IsOpen);
    }

    [UiThreadFact]
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

    [UiThreadFact]
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

    [UiThreadFact]
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

    [UiThreadFact]
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

    [UiThreadFact]
    public async Task AParserWorkLimitWhoseTimeEstimateDoesNotFitTheStoredLimitCanFinishWithoutATimeLimit()
    {
        var (fake, projectPicker, _, _, workspace) = NewWorkspace();
        fake.ListTextsCompletesWith(new TextInventoryResponse([new TextChoiceSummary(TextId, "Alpha")], true));
        await ChooseProjectAsync(fake, projectPicker, workspace, ProjectPath, NewToken());
        workspace.Selection.Texts[0].IsChecked = true;
        var setup = workspace.Context.Setup!;
        setup.Step = 3;
        setup.ParserWorkLimitValue = 1_000_000_000_000m;
        fake.AssessCompletesWith(NewAssessResponse("first run"));

        Assert.True(setup.FinishCommand.CanExecute(null));
        await setup.FinishCommand.ExecuteAsync(null);

        Assert.Null(Assert.Single(fake.SetDefaultSelectionRequests).PerWordLimitMs);
        Assert.Null(Assert.Single(fake.AssessRequests).PerWordLimitMs);
    }

    [Theory]
    [InlineData(1, "Motif stops a word after 1 parser step.")]
    [InlineData(20_000_000, "Motif stops a word after 20,000,000 parser steps.")]
    public void SetupLimitTextStatesParserWorkWithoutTime(int limit, string expected)
    {
        var (_, _, _, _, workspace) = NewWorkspace();
        var setup = workspace.Context.Setup!;
        setup.ParserWorkLimitValue = limit;

        Assert.Equal(expected, setup.ParserWorkLimitEstimateText);
        Assert.DoesNotContain("ms", setup.ParserWorkLimitEstimateText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("millisecond", setup.ParserWorkLimitEstimateText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SetupUnlimitedTextDoesNotMentionTime()
    {
        var (_, _, _, _, workspace) = NewWorkspace();
        var setup = workspace.Context.Setup!;
        setup.IsParserWorkLimitUnbounded = true;

        Assert.Equal("No parser step limit.", setup.ParserWorkLimitEstimateText);
        Assert.DoesNotContain("time", setup.ParserWorkLimitEstimateText, StringComparison.OrdinalIgnoreCase);
    }

    [UiThreadFact]
    public async Task FirstRunCanUseNoParserWorkLimit()
    {
        var (fake, projectPicker, _, _, workspace) = NewWorkspace();
        fake.ListTextsCompletesWith(new TextInventoryResponse([new TextChoiceSummary(TextId, "Alpha")], true));
        await ChooseProjectAsync(fake, projectPicker, workspace, ProjectPath, NewToken());
        workspace.Selection.Texts[0].IsChecked = true;
        workspace.Context.Setup!.IsParserWorkLimitUnbounded = true;
        workspace.Context.Setup.Step = 3;
        fake.AssessCompletesWith(NewAssessResponse("first run"));

        await workspace.Context.Setup.FinishCommand.ExecuteAsync(null);

        var request = Assert.Single(fake.AssessRequests);
        Assert.Equal(ParserWorkLimit.Unbounded, request.PerWordParserWorkLimit);
        Assert.Null(request.PerWordLimitMs);
        Assert.Null(Assert.Single(fake.SetDefaultSelectionRequests).PerWordLimitMs);
        Assert.Contains("No parser step limit", workspace.Context.Setup!.ParserWorkLimitEstimateText, StringComparison.Ordinal);
    }

    [UiThreadFact]
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
    [UiThreadFact]
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

    [UiThreadFact]
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

    [UiThreadFact]
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
        Assert.Equal(MotifAppStrings.LoadingProject, workspace.SelectionSummaryText);

        currentBaseline.SetResult(CommandOutcome<CurrentBaselineResponse>.Success(
            new CurrentBaselineResponse(NewToken(), DateTimeOffset.UtcNow, false)));
        await opening.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(workspace.Context.IsOpeningProject);
        Assert.Equal($"Parse Selection: {workspace.Selection.SummaryText}", workspace.SelectionSummaryText);
    }

    [UiThreadFact]
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

    [UiThreadFact]
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

    [UiThreadFact]
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

    [UiThreadFact]
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

    [UiThreadFact]
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

    [UiThreadFact]
    public async Task ConfigureAfterFinishingSetupShowsTheSelectionJustSaved()
    {
        var (fake, projectPicker, _, _, workspace) = NewWorkspace();
        fake.ListTextsCompletesWith(new TextInventoryResponse([new TextChoiceSummary(TextId, "Alpha")], true));
        await ChooseProjectAsync(fake, projectPicker, workspace, ProjectPath, NewToken());
        var setup = workspace.Context.Setup!;
        workspace.Selection.Texts[0].IsChecked = true;
        workspace.Selection.PastedWords = "added";
        setup.ParserWorkLimitValue = 4321;
        setup.Step = 3;
        fake.AssessCompletesWith(NewAssessResponse("first run"));
        await setup.FinishCommand.ExecuteAsync(null);
        Assert.False(setup.IsOpen);
        workspace.Selection.Texts[0].IsChecked = false;
        workspace.Selection.PastedWords = string.Empty;

        workspace.ConfigureCommand.Execute(null);

        AssertConfigureShows(workspace, "added", 1m, 4321m);
    }

    [UiThreadFact]
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
        Assert.Equal(SIL.Motif.App.MotifAppStrings.StartFirstRun, workspace.Context.Setup.FinishButtonText);
    }

    [UiThreadFact]
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

    [UiThreadFact]
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
        IReadOnlyList<string> addedWords, int timeLimitMs, long parserWorkLimit) => new(
        new NamedSelectionProjection("Default", [TextId], addedWords, "created", "updated",
            new SelectionParsingLimits(new ParserWorkLimit(parserWorkLimit), SelectionTimeLimitMode.Explicit, timeLimitMs), "revision"));

    private static void AssertConfigureShows(
        WorkspaceShellViewModel workspace, string addedWords, decimal timeLimitSeconds, decimal parserWorkLimit)
    {
        var setup = workspace.Context.Setup!;
        Assert.True(setup.IsOpen);
        Assert.Equal(0, setup.Step);
        Assert.True(Assert.Single(workspace.Selection.Texts).IsChecked);
        Assert.Equal(addedWords, workspace.Selection.PastedWords);
        Assert.Equal(timeLimitSeconds, workspace.Selection.PerWordTimeLimitSeconds);
        Assert.Equal(parserWorkLimit, setup.ParserWorkLimitValue);
        Assert.Equal(SIL.Motif.App.MotifAppStrings.UseThisSelection, setup.FinishButtonText);
    }

    [UiThreadTheory]
    [InlineData(SelectionTimeLimitMode.Estimated, null)]
    [InlineData(SelectionTimeLimitMode.Explicit, 450)]
    public async Task RunningTheSavedSelectionSendsATimeLimitOnlyWhenTheSelectionChoseOne(
        SelectionTimeLimitMode mode, int? expectedLimitMs)
    {
        var (fake, projectPicker, _, _, workspace) = NewWorkspace();
        fake.DefaultSelectionResponseIs(new DefaultSelectionResponse(new NamedSelectionProjection("Default", [TextId], [],
            "created", "updated", new SelectionParsingLimits(new ParserWorkLimit(1234), mode, mode == SelectionTimeLimitMode.Explicit ? 450 : null),
            "revision")));
        fake.ListTextsCompletesWith(new TextInventoryResponse([new TextChoiceSummary(TextId, "Alpha")], true));
        await ChooseProjectAsync(fake, projectPicker, workspace, ProjectPath, NewToken());
        fake.AssessCompletesWith(NewAssessResponse("saved selection"));

        await workspace.Context.Setup!.RunDefaultSelectionAsync();

        var request = Assert.Single(fake.AssessRequests);
        Assert.Equal(expectedLimitMs, request.PerWordLimitMs);
        Assert.Equal(new ParserWorkLimit(1234), request.PerWordParserWorkLimit);
    }

    [UiThreadFact]
    public async Task LaterRunsAfterSetupLeaveAnEstimatedTimeLimitToTheCommandsSafetyNet()
    {
        var (fake, projectPicker, _, _, workspace) = NewWorkspace();
        fake.DefaultSelectionResponseIs(new DefaultSelectionResponse(new NamedSelectionProjection("Default", [TextId], [],
            "created", "updated", new SelectionParsingLimits(new ParserWorkLimit(1234), SelectionTimeLimitMode.Estimated, null),
            "revision")));
        fake.ListTextsCompletesWith(new TextInventoryResponse([new TextChoiceSummary(TextId, "Alpha")], true));
        await ChooseProjectAsync(fake, projectPicker, workspace, ProjectPath, NewToken());
        fake.AssessCompletesWith(NewAssessResponse("estimated limits"));
        Assert.NotNull(workspace.Selection.PerWordTimeLimitSeconds);

        await workspace.Assess.RunCommand.ExecuteAsync(null);

        var request = Assert.Single(fake.AssessRequests);
        Assert.Null(request.PerWordLimitMs);
        var shapes = string.Join(" ", Assert.Single(fake.UsageEntries, entry => entry.Command == "assess").ArgumentShape);
        Assert.DoesNotContain("perWordLimitMs", shapes);
        Assert.DoesNotContain("perWordTimeLimitSeconds", shapes);
    }

    [UiThreadFact]
    public async Task LaterRunsUseLimitsSavedWithTheDefaultSelection()
    {
        var (fake, projectPicker, _, _, workspace) = NewWorkspace();
        fake.DefaultSelectionResponseIs(SavedSelection([], 450, 1234));
        fake.ListTextsCompletesWith(new TextInventoryResponse([new TextChoiceSummary(TextId, "Alpha")], true));
        fake.OnShowConfig((_, _) => Task.FromResult(CommandOutcome<ProjectConfigurationProjection>.Success(
            new ProjectConfigurationProjection(true, true,
                [new AssessmentScopeProjection("default", "all words", "pangloss", [], 2000, new ParserWorkLimit(9876))]))));
        await ChooseProjectAsync(fake, projectPicker, workspace, ProjectPath, NewToken());
        fake.AssessCompletesWith(NewAssessResponse("saved limits"));

        await workspace.Assess.RunCommand.ExecuteAsync(null);

        var request = Assert.Single(fake.AssessRequests);
        Assert.Equal(450, request.PerWordLimitMs);
        Assert.Equal(new ParserWorkLimit(1234), request.Selection!.PerWordParserWorkLimit);
        var shapes = string.Join(" ", Assert.Single(fake.UsageEntries, entry => entry.Command == "assess").ArgumentShape);
        Assert.Contains("perWordLimitMs", shapes);
        Assert.DoesNotContain("perWordTimeLimitSeconds", shapes);
    }

    [UiThreadFact]
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
        Assert.Equal($"1 text, 1 pasted word, parser step limit {ParserWorkLimit.DefaultWork:N0}", workspace.Selection.SummaryText);
    }

    [UiThreadFact]
    public async Task ACompletedAssessmentFeedsCurrentEvidenceAndHandoff()
    {
        var (fake, projectPicker, _, _, workspace) = NewWorkspace();
        await ChooseProjectAsync(fake, projectPicker, workspace, ProjectPath, NewToken());
        workspace.Selection.AllWordforms = true;
        fake.AssessCompletesWith(NewAssessResponse("(summary)"));

        await workspace.Assess.RunCommand.ExecuteAsync(null);

        Assert.False(workspace.Context.NeedsAssessment);
        Assert.Equal("(summary)", workspace.Context.Evidence.Assessment!.Assessment.SummaryMarkdown);
        Assert.Equal("assessment/one", workspace.Context.Evidence.ObjectTimingAssessmentId);
        Assert.Equal("invocation/one", workspace.PageModel<SettingsPageModel>().Handoff.Handoff.InvocationId);
        Assert.True(workspace.Context.HasEvidence);
        Assert.False(workspace.Context.HasNoEvidence);
    }

    [UiThreadFact]
    public async Task RefreshOffersParseAllWordsAndRunsTheSavedSelectionWithItsSavedLimits()
    {
        var (fake, projectPicker, _, _, workspace) = NewWorkspace();
        fake.DefaultSelectionCompletesWith(new NamedSelectionProjection(
            "Default", [], ["word"], string.Empty, string.Empty, 1500, new ParserWorkLimit(6600)));
        await ChooseProjectAsync(fake, projectPicker, workspace, ProjectPath, NewToken());
        fake.AssessCompletesWith(NewAssessResponse("(first)"));
        await workspace.Assess.RunCommand.ExecuteAsync(null);
        await workspace.Context.Changes.PutAsync(new ChangeIntent(
            "history/refresh", ChangeKinds.Approve, "wordform/refresh", "refreshed"));

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
        Assert.True(workspace.ShowsParseAllWordsAction);
        Assert.Equal(2, fake.AssessRequests.Count);
        Assert.Null(fake.AssessRequests[1].Selection);
        Assert.Equal(1500, fake.AssessRequests[1].PerWordLimitMs);
        Assert.Equal(new ParserWorkLimit(6600), fake.AssessRequests[1].PerWordParserWorkLimit);
        Assert.Equal("(second)", workspace.Context.Evidence.Assessment!.Assessment.SummaryMarkdown);
    }

    [UiThreadFact]
    public async Task CancellingParseLeavesTheParseActionAvailable()
    {
        var (fake, projectPicker, _, _, workspace) = NewWorkspace();
        fake.DefaultSelectionCompletesWith(new NamedSelectionProjection(
            "Default", [], ["word"], string.Empty, string.Empty, 1000, ParserWorkLimit.Default));
        await ChooseProjectAsync(fake, projectPicker, workspace, ProjectPath, NewToken());
        fake.CaptureBaselineCompletesWith(new BaselineCaptureResponse(
            NewToken("2026-09-06T00:00:00Z"), ProjectPath, DateTimeOffset.UtcNow, false, false));
        await workspace.RefreshCommand.ExecuteAsync(null);
        fake.AssessBlocksUntilCancelled(new Refusal("assess.cancelled", FailureReason.Cancelled, "Cancelled."),
            new AssessmentProgress(AssessmentStage.Parsing, 312, 1040, "Parsing words")
            {
                CurrentWord = "word-312",
                PerWordLimitMs = 700,
                SlowestWord = new ParseWordTiming("word-200", 54000) { ParserWork = 90_000 },
                StoppedWords = [new StoppedParseWord("word-201", "TIMEOUT", 700)],
            });

        var parsing = workspace.ParseAllWordsCommand.ExecuteAsync(null);
        Assert.True(workspace.ShowsParseAllWordsProgress);
        Assert.Equal("Parsing 312 of 1,040 words", workspace.ParseAllWordsProgressText);
        Assert.Same(workspace.Assess.ParseProgress, workspace.ActiveParseProgress);
        Assert.Equal("312 of 1,040 words done · Parsing word-312", workspace.ActiveParseProgress.ProgressText);
        Assert.Equal("Slowest so far: word-200 · 90,000 parser steps", workspace.ActiveParseProgress.SlowestText);
        Assert.Equal("1 word stopped at the time safety net after 0.7 s (word-201)", workspace.ActiveParseProgress.StoppedText);
        Assert.True(workspace.PageModel<TextsPageModel>().ShowParsePrompt);
        workspace.Assess.CancelCommand.Execute(null);
        await parsing;

        Assert.True(workspace.Context.NeedsAssessment);
        Assert.True(workspace.ShowsParseAllWordsAction);
        var texts = workspace.PageModel<TextsPageModel>();

        Assert.True(texts.ShowParsePrompt);
        Assert.Single(fake.AssessRequests);
    }

    [UiThreadFact]
    public async Task ARefusedParseLeavesTheParseActionAvailable()
    {
        var (fake, projectPicker, _, _, workspace) = NewWorkspace();
        fake.DefaultSelectionCompletesWith(new NamedSelectionProjection(
            "Default", [], ["word"], string.Empty, string.Empty, 1000, ParserWorkLimit.Default));
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

        Assert.False(texts.ShowParsePrompt);

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
        Assert.True(texts.ShowParsePrompt);
    }

    [UiThreadFact]
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
        Assert.Equal("There is nothing to undo.", await workspace.Context.Changes.UndoStagingActionAsync());
        Assert.Equal("There is nothing to redo.", await workspace.Context.Changes.RedoStagingActionAsync());
    }

    [UiThreadFact]
    public async Task ParseDependentPagesShareThePromptAndKeepIndependentContentAvailable()
    {
        var (fake, projectPicker, _, _, workspace) = NewWorkspace();
        fake.DefaultSelectionCompletesWith(new NamedSelectionProjection(
            "Default", [TextId], [], string.Empty, string.Empty, 1000, ParserWorkLimit.Default));
        fake.ListTextsCompletesWith(new TextInventoryResponse([new TextChoiceSummary(TextId, "Alpha")], HasBaseline: true));
        fake.StoredGrammarCheckIs(new GrammarCheckResponse([], HasBaseline: true));
        await ChooseProjectAsync(fake, projectPicker, workspace, ProjectPath, NewToken());

        var texts = workspace.PageModel<TextsPageModel>();
        Assert.True(workspace.Context.NeedsAssessment);
        Assert.True(texts.ShowParsePrompt);
        Assert.Same(workspace.ParseAllWordsCommand, workspace.Context.ParseAllWordsCommand);

        Assert.True(texts.ShowParsePrompt);
        Assert.True(workspace.Context.GrammarCheck.HasChecked);
        workspace.Context.Diagnostics = true;
        workspace.CurrentPage = WorkspacePage.Diagnostics;
        Assert.Empty(workspace.PageModel<DiagnosticsPageModel>().SpeedByRule.Rows);
        workspace.CurrentPage = WorkspacePage.Settings;
        Assert.Equal(WorkspacePage.Settings, workspace.CurrentPage);
        workspace.CurrentPage = WorkspacePage.Proposals;
        Assert.Equal(WorkspacePage.Proposals, workspace.CurrentPage);
    }

    [Fact]
    public async Task ChoosingAnotherProjectClearsThePreviousProjectsAssessmentAndDiagnosticState()
    {
        var (fake, projectPicker, _, _, workspace) = NewWorkspace();
        await ChooseProjectAsync(fake, projectPicker, workspace, ProjectPath, NewToken());
        workspace.Selection.AllWordforms = true;
        fake.AssessCompletesWith(NewAssessResponse("(summary)"));
        await workspace.Assess.RunCommand.ExecuteAsync(null);
        workspace.Context.Diagnostics = true;
        var diagnostics = workspace.PageModel<DiagnosticsPageModel>();

        await ChooseProjectAsync(fake, projectPicker, workspace, @"C:\projects\two.fwdata");

        Assert.False(workspace.Context.HasEvidence);
        Assert.True(workspace.Context.HasNoEvidence);
        Assert.Empty(diagnostics.SpeedByRule.Rows);
        Assert.Null(workspace.PageModel<SettingsPageModel>().Handoff.Handoff.InvocationId);
        Assert.Equal(RunState.Idle, workspace.Assess.State);
        Assert.Null(workspace.Assess.Result);
        Assert.Empty(workspace.PageModel<SettingsPageModel>().Handoff.Handoff.Files);
    }

    [UiThreadFact]
    public async Task TheFullAgreedWorkflowCompletesAndProducesDraggableHandoffFiles()
    {
        var (fake, projectPicker, folderPicker, dragSource, workspace) = NewWorkspace();
        fake.DefaultSelectionCompletesWith(new NamedSelectionProjection(
            "Default", [TextId], [], string.Empty, string.Empty, 1000, ParserWorkLimit.Default));
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
        var statistics = new StatisticsViewModel(fake) { ProjectPath = ProjectPath, AssessmentId = "assessment/one" };
        statistics.SelectedGroup = statistics.Groups[1];
        await statistics.LoadCommand.ExecuteAsync(null);
        statistics.FilterText = "word";
        statistics.SortBy("attempts");
        Assert.Single(statistics.Rows);
        Assert.Equal(statistics.Groups[1], Assert.Single(fake.StatsRequests).ForwardedArguments[1]);
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
        await workspace.PageModel<SettingsPageModel>().Handoff.Handoff.RunCommand.ExecuteAsync(null);

        Assert.Equal(RunState.Completed, workspace.PageModel<SettingsPageModel>().Handoff.Handoff.State);
        var file = Assert.Single(workspace.PageModel<SettingsPageModel>().Handoff.Handoff.Files);
        await workspace.PageModel<SettingsPageModel>().Handoff.Handoff.DragFileAsync(null!, file);
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

    private const string IdentityA = "6f1c2d3e-0000-4000-8000-00000000abcd";
    private const string PathOne = @"C:\projects\one.fwdata";
    private const string PathTwo = @"C:\projects\two.fwdata";

    private static string KeyOf(string? id, string path) => ProjectPreferenceKey.For(id, path);

    private static CountingPreferencesStore SeedWarnings(string key)
    {
        var store = new CountingPreferencesStore(new MemoryUserPreferencesStore());
        store.SetViewState(key, "Warnings", new ViewState("ReportOrder", false, null));
        return store;
    }

    [UiThreadFact]
    public async Task TheViewKeyResolvesAfterTheOpenAndOnlyThenDoTheViewsRestore()
    {
        var (fake, picker, _, _, workspace) = NewWorkspace();
        var store = SeedWarnings(KeyOf(IdentityA, PathOne));
        var identity = new TaskCompletionSource<string?>();
        workspace.Context.ViewMemory.UseStore(store);
        workspace.Context.ViewIdentityReader = _ => identity.Task;

        await ChooseProjectAsync(fake, picker, workspace, PathOne);
        var warnings = workspace.Context.GrammarCheck.Warnings;

        Assert.True(workspace.Context.ViewMemory.IsPending);
        Assert.Null(workspace.Context.ViewMemory.ProjectKey);
        Assert.True(warnings.MostYourWordsFirst);

        identity.SetResult(IdentityA);

        Assert.Equal(KeyOf(IdentityA, PathOne), workspace.Context.ViewMemory.ProjectKey);
        Assert.False(warnings.MostYourWordsFirst);
    }

    [UiThreadFact]
    public async Task AProjectSwitchBeforeTheIdentityResolvesNeverRestoresIntoTheNewProject()
    {
        var (fake, picker, _, _, workspace) = NewWorkspace();
        var store = SeedWarnings(KeyOf(IdentityA, PathOne));
        var readers = new Dictionary<string, TaskCompletionSource<string?>>
        {
            [PathOne] = new(),
            [PathTwo] = new(),
        };
        workspace.Context.ViewMemory.UseStore(store);
        workspace.Context.ViewIdentityReader = path => readers[path].Task;

        await ChooseProjectAsync(fake, picker, workspace, PathOne);
        await ChooseProjectAsync(fake, picker, workspace, PathTwo);
        var warnings = workspace.Context.GrammarCheck.Warnings;

        readers[PathOne].SetResult(IdentityA);

        Assert.Null(workspace.Context.ViewMemory.ProjectKey);
        Assert.True(warnings.MostYourWordsFirst);

        readers[PathTwo].SetResult(null);

        Assert.Equal(KeyOf(null, PathTwo), workspace.Context.ViewMemory.ProjectKey);
        Assert.True(warnings.MostYourWordsFirst);
    }

    [UiThreadFact]
    public async Task AFailedIdentityReadKeysTheProjectByItsPath()
    {
        var (fake, picker, _, _, workspace) = NewWorkspace();
        var store = SeedWarnings(KeyOf(null, PathOne));
        workspace.Context.ViewMemory.UseStore(store);
        workspace.Context.ViewIdentityReader = _ => Task.FromException<string?>(new IOException("unreadable"));

        await ChooseProjectAsync(fake, picker, workspace, PathOne);

        Assert.Equal(KeyOf(null, PathOne), workspace.Context.ViewMemory.ProjectKey);
        Assert.False(workspace.Context.GrammarCheck.Warnings.MostYourWordsFirst);
    }

    [UiThreadFact]
    public async Task AChangeMadeBeforeTheKeyResolvesIsHeldAndSavedUnderTheFinalKey()
    {
        var (fake, picker, _, _, workspace) = NewWorkspace();
        var store = new CountingPreferencesStore(new MemoryUserPreferencesStore());
        var identity = new TaskCompletionSource<string?>();
        workspace.Context.ViewMemory.UseStore(store);
        workspace.Context.ViewIdentityReader = _ => identity.Task;
        await ChooseProjectAsync(fake, picker, workspace, PathOne);
        var warnings = workspace.Context.GrammarCheck.Warnings;
        var seeded = store.Updates;

        warnings.MostYourWordsFirst = false;
        Assert.Equal(seeded, store.Updates);

        identity.SetResult(IdentityA);

        Assert.False(warnings.MostYourWordsFirst);
        Assert.Equal(seeded + 1, store.Updates);
        Assert.Equal("ReportOrder", store.GetViewState(KeyOf(IdentityA, PathOne), "Warnings")!.SortColumn);
        Assert.Null(store.GetViewState(KeyOf(null, PathOne), "Warnings"));
    }

    private static string FilterOf(WorkspaceShellViewModel workspace, string view) => view switch
    {
        "AssessWords" => workspace.Assess.Words.SelectedFilter.ToString(),
        "Trace" => workspace.Assess.Trace.RuleFilter,
        _ => throw new ArgumentException(view),
    };

    private static void ChooseViewFilter(WorkspaceShellViewModel workspace, string view)
    {
        switch (view)
        {
            case "AssessWords": workspace.Assess.Words.SelectedFilter = ResultsWordFilter.Missed; break;
            case "Trace": workspace.Assess.Trace.RuleFilter = "rule"; break;
        }
    }

    [UiThreadTheory]
    [InlineData("AssessWords", "Missed", "All")]
    [InlineData("Trace", "rule", "")]
    public async Task ViewFiltersFollowProjectsAndRestoreInFreshWorkspace(string view, string chosen, string fallback)
    {
        var (fake, picker, _, _, workspace) = NewWorkspace();
        var store = new CountingPreferencesStore(new MemoryUserPreferencesStore());
        workspace.Context.ViewMemory.UseStore(store);
        workspace.Context.ViewIdentityReader = _ => Task.FromResult<string?>(null);
        await ChooseProjectAsync(fake, picker, workspace, PathOne);
        ChooseViewFilter(workspace, view);
        Assert.Equal(chosen, FilterOf(workspace, view));
        var writes = store.Updates;
        await ChooseProjectAsync(fake, picker, workspace, PathTwo);
        Assert.Equal(fallback, FilterOf(workspace, view));
        await ChooseProjectAsync(fake, picker, workspace, PathOne);
        Assert.Equal(chosen, FilterOf(workspace, view));

        var (fake2, picker2, _, _, fresh) = NewWorkspace();
        fresh.Context.ViewMemory.UseStore(store);
        fresh.Context.ViewIdentityReader = _ => Task.FromResult<string?>(null);
        await ChooseProjectAsync(fake2, picker2, fresh, PathOne);
        Assert.Equal(chosen, FilterOf(fresh, view));
        Assert.Equal(writes, store.Updates);
    }

    private static string SortOf(WorkspaceShellViewModel workspace, string view) => view switch
    {
        "Compare" => workspace.Assess.Compare.Sort.ToString(),
        "Warnings" => workspace.Context.GrammarCheck.Warnings.MostYourWordsFirst ? "MostWords" : "ReportOrder",
        _ => throw new ArgumentException(view),
    };

    private static void ChooseViewSort(WorkspaceShellViewModel workspace, string view)
    {
        switch (view)
        {
            case "Compare": workspace.Assess.Compare.Sort = CompareSort.Slowest; break;
            case "Warnings": workspace.Context.GrammarCheck.Warnings.MostYourWordsFirst = false; break;
        }
    }

    [UiThreadTheory]
    [InlineData("Compare", "Slowest", "MostFrequent")]
    [InlineData("Warnings", "ReportOrder", "MostWords")]
    public async Task ViewSortsFollowProjectsSurviveLeavingAndRestoreInFreshWorkspace(string view, string chosen, string fallback)
    {
        var (fake, picker, _, _, workspace) = NewWorkspace();
        var store = new CountingPreferencesStore(new MemoryUserPreferencesStore());
        workspace.Context.ViewMemory.UseStore(store);
        workspace.Context.ViewIdentityReader = _ => Task.FromResult<string?>(null);
        await ChooseProjectAsync(fake, picker, workspace, PathOne);
        Assert.Null(store.GetViewState(KeyOf(null, PathOne), view));
        ChooseViewSort(workspace, view);
        Assert.Equal(chosen, SortOf(workspace, view));
        Assert.Equal(new ViewState(chosen, false, null), store.GetViewState(KeyOf(null, PathOne), view));
        var writes = store.Updates;

        workspace.CurrentPage = WorkspacePage.Settings;
        workspace.CurrentPage = WorkspacePage.Progress;
        Assert.Equal(chosen, SortOf(workspace, view));

        await ChooseProjectAsync(fake, picker, workspace, PathTwo);
        Assert.Equal(fallback, SortOf(workspace, view));
        await ChooseProjectAsync(fake, picker, workspace, PathOne);
        Assert.Equal(chosen, SortOf(workspace, view));

        var (fake2, picker2, _, _, fresh) = NewWorkspace();
        fresh.Context.ViewMemory.UseStore(store);
        fresh.Context.ViewIdentityReader = _ => Task.FromResult<string?>(null);
        await ChooseProjectAsync(fake2, picker2, fresh, PathOne);
        Assert.Equal(chosen, SortOf(fresh, view));
        Assert.Equal(writes, store.Updates);
    }

    [UiThreadFact]
    public async Task ACompareSortChosenForYouWhenThereAreNoPlaceCountsIsNotRemembered()
    {
        var (fake, picker, _, _, workspace) = NewWorkspace();
        var store = new CountingPreferencesStore(new MemoryUserPreferencesStore());
        workspace.Context.ViewMemory.UseStore(store);
        workspace.Context.ViewIdentityReader = _ => Task.FromResult<string?>(null);
        await ChooseProjectAsync(fake, picker, workspace, PathOne);
        var writes = store.Updates;

        workspace.Assess.Compare.Load([new AssessWordRowViewModel(
            new AssessmentWordResult("motifa", "analysed", false, "Search completed", 10, null))]);

        Assert.Equal(CompareSort.Alphabetical, workspace.Assess.Compare.Sort);
        Assert.Equal(writes, store.Updates);
        Assert.Null(store.GetViewState(KeyOf(null, PathOne), "Compare"));
    }

    [UiThreadFact]
    public async Task AnUnknownSavedSortFallsBackToTheDefault()
    {
        var (fake, picker, _, _, workspace) = NewWorkspace();
        var store = new CountingPreferencesStore(new MemoryUserPreferencesStore());
        store.SetViewState(KeyOf(null, PathOne), "Compare", new ViewState("Fastest", false, null));
        store.SetViewState(KeyOf(null, PathOne), "Warnings", new ViewState("Newest", false, null));
        workspace.Context.ViewMemory.UseStore(store);
        workspace.Context.ViewIdentityReader = _ => Task.FromResult<string?>(null);
        var writes = store.Updates;

        await ChooseProjectAsync(fake, picker, workspace, PathOne);

        Assert.Equal("MostFrequent", SortOf(workspace, "Compare"));
        Assert.Equal("MostWords", SortOf(workspace, "Warnings"));
        Assert.Equal(writes, store.Updates);
    }

    [UiThreadTheory]
    [InlineData("AssessWords", "All")]
    public async Task UnknownSavedViewFilterFallsBack(string view, string fallback)
    {
        var (fake, picker, _, _, workspace) = NewWorkspace();
        var store = new CountingPreferencesStore(new MemoryUserPreferencesStore());
        store.SetViewState(KeyOf(null, PathOne), view, new ViewState(null, false, "2"));
        workspace.Context.ViewMemory.UseStore(store);
        workspace.Context.ViewIdentityReader = _ => Task.FromResult<string?>(null);
        var writes = store.Updates;
        await ChooseProjectAsync(fake, picker, workspace, PathOne);
        Assert.Equal(fallback, FilterOf(workspace, view));
        Assert.Equal(writes, store.Updates);
    }

    [UiThreadTheory]
    [InlineData("AssessWords", "Missed")]
    [InlineData("Trace", "rule")]
    public async Task ViewFilterChangedBeforeIdentityIsHeld(string view, string chosen)
    {
        var (fake, picker, _, _, workspace) = NewWorkspace();
        var store = new CountingPreferencesStore(new MemoryUserPreferencesStore());
        store.SetViewState(KeyOf(IdentityA, PathOne), view, new ViewState(null, false, "older"));
        var identity = new TaskCompletionSource<string?>();
        workspace.Context.ViewMemory.UseStore(store);
        workspace.Context.ViewIdentityReader = _ => identity.Task;
        await ChooseProjectAsync(fake, picker, workspace, PathOne);
        var writes = store.Updates;
        ChooseViewFilter(workspace, view);
        Assert.Equal(writes, store.Updates);
        identity.SetResult(IdentityA);
        Assert.Equal(chosen, FilterOf(workspace, view));
        Assert.Equal(writes + 1, store.Updates);
        Assert.NotEqual("older", store.GetViewState(KeyOf(IdentityA, PathOne), view)!.Filter);
    }

    [UiThreadFact]
    public async Task TextWordsFilterChangedBeforeIdentityIsHeld()
    {
        var (fake, picker, _, _, workspace) = NewWorkspace();
        var store = new CountingPreferencesStore(new MemoryUserPreferencesStore());
        var identity = new TaskCompletionSource<string?>();
        workspace.Context.ViewMemory.UseStore(store);
        workspace.Context.ViewIdentityReader = _ => identity.Task;
        await ChooseProjectAsync(fake, picker, workspace, PathOne);
        var words = workspace.PageModel<TextsPageModel>().Words;
        var writes = store.Updates;
        words.SetStatusFilterCommand.Execute(WordProjectStatus.Approved);
        Assert.Equal(writes, store.Updates);
        identity.SetResult(IdentityA);
        Assert.Equal(WordProjectStatus.Approved, words.StatusFilter);
        Assert.Equal(writes + 1, store.Updates);
        Assert.Equal("Approved", store.GetViewState(KeyOf(IdentityA, PathOne), "TextWords")!.Filter);
    }

    [UiThreadFact]
    public async Task AnOlderIdentityReadCannotResolveAReopenedPath()
    {
        var (fake, picker, _, _, workspace) = NewWorkspace();
        var reads = new Queue<TaskCompletionSource<string?>>(new[] { new TaskCompletionSource<string?>(), new TaskCompletionSource<string?>() });
        var first = reads.Peek();
        workspace.Context.ViewIdentityReader = _ => reads.Dequeue().Task;
        await ChooseProjectAsync(fake, picker, workspace, PathOne);
        var second = reads.Peek();
        await ChooseProjectAsync(fake, picker, workspace, PathOne);
        first.SetResult(IdentityA);
        Assert.True(workspace.Context.ViewMemory.IsPending);
        second.SetResult(null);
        Assert.Equal(KeyOf(null, PathOne), workspace.Context.ViewMemory.ProjectKey);
    }


    [UiThreadFact]
    public async Task TraceOnlySavesTheRuleFilterInViewMemory()
    {
        var (fake, picker, _, _, workspace) = NewWorkspace();
        var store = new CountingPreferencesStore(new MemoryUserPreferencesStore());
        workspace.Context.ViewMemory.UseStore(store);
        workspace.Context.ViewIdentityReader = _ => Task.FromResult<string?>(null);
        await ChooseProjectAsync(fake, picker, workspace, PathOne);
        var writes = store.Updates;
        workspace.Assess.Trace.SearchText = "private search";
        workspace.Assess.Trace.MorphFilter = "morph";
        workspace.Assess.Trace.OutcomeFilter = TraceOutcomeFilter.Failed;
        Assert.Equal(writes, store.Updates);
        Assert.Null(store.GetViewState(KeyOf(null, PathOne), "Trace"));
        workspace.Assess.Trace.RuleFilter = "rule";
        Assert.Equal(writes + 1, store.Updates);
        workspace.Assess.Trace.SearchText = "another search";
        workspace.Assess.Trace.MorphFilter = "another morph";
        workspace.Assess.Trace.OutcomeFilter = TraceOutcomeFilter.Succeeded;
        Assert.Equal(writes + 1, store.Updates);
        Assert.Equal(new ViewState(null, false, "rule"), store.GetViewState(KeyOf(null, PathOne), "Trace"));
        var (fake2, picker2, _, _, fresh) = NewWorkspace();
        fresh.Context.ViewMemory.UseStore(store);
        fresh.Context.ViewIdentityReader = _ => Task.FromResult<string?>(null);
        await ChooseProjectAsync(fake2, picker2, fresh, PathOne);
        Assert.Equal("rule", fresh.Assess.Trace.RuleFilter);
        Assert.Equal(string.Empty, fresh.Assess.Trace.SearchText);
        Assert.Equal(string.Empty, fresh.Assess.Trace.MorphFilter);
        Assert.Equal(TraceOutcomeFilter.All, fresh.Assess.Trace.OutcomeFilter);
    }

}
