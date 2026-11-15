using Avalonia.Input;
using SIL.Motif.App;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Pins the page shell's state on <see cref="WorkspaceShellViewModel"/>: which page is showing, the sidebar's
/// entries, badges and collapsed mode, the project menu, the freshness line, and the one changes list every page
/// shares.
/// </summary>
[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class WorkspacePageTests
{
    private static readonly string ProjectPath = Path.Combine(Path.GetTempPath(), "projects", "one.fwdata");

    private static readonly DateTimeOffset Saved = new(2026, 9, 5, 10, 58, 0, TimeSpan.Zero);

    private static readonly BaselineToken Token = new(
        "project-1", "sha256:" + new string('a', 64), "1", "2026-09-05T11:02:00Z", "sha256:" + new string('b', 64));

    private static (FakeCommandClient Fake, FakeProjectPicker ProjectPicker, WorkspaceShellViewModel Workspace)
        NewWorkspace()
    {
        var fake = new FakeCommandClient();
        var projectPicker = new FakeProjectPicker();
        var selection = new SelectionViewModel(fake);
        var workspace = new WorkspaceShellViewModel(
            new ProjectViewModel(fake, projectPicker),
            new BaselineViewModel(fake),
            selection,
            new AssessViewModel(fake, selection),
            new FakeFolderPicker(), new FakeDragSource(),
            fake);
        return (fake, projectPicker, workspace);
    }

    private static async Task ChooseProjectAsync(
        FakeCommandClient fake, FakeProjectPicker projectPicker, WorkspaceShellViewModel workspace,
        DateTimeOffset? projectLastWriteUtc = null, bool fieldWorksHeldProject = false)
    {
        fake.CurrentBaselineCompletesWith(new CurrentBaselineResponse(Token, Saved, fieldWorksHeldProject)
        {
            ProjectLastWriteUtc = projectLastWriteUtc ?? Saved,
        });
        fake.ListTextsCompletesWith(new TextInventoryResponse([], HasBaseline: true));
        projectPicker.PathToReturn = ProjectPath;
        await workspace.Project.BrowseCommand.ExecuteAsync(null);
    }

    private static AssessCommandResponse NewAssessResponse(DateTimeOffset? saved = null) => new(
        new BaselineCaptureResponse(Token, ProjectPath, saved ?? Saved, false, false),
        new SelectionProjection([], []), [], "summary")
    {
        InvocationId = "invocation/one",
        CompletionSummary = "3 searches completed",
    };

    private static AssessmentWordResult Word(string word, string outcome, string standing) =>
        new(word, outcome, false, "Search completed", 10, null) { ProjectStanding = standing };

    [Fact]
    public void TheWorkspaceRegistersEveryPageInOrderAndOpensOnProgress()
    {
        var (_, _, workspace) = NewWorkspace();

        Assert.Equal(
            [WorkspacePage.Progress, WorkspacePage.Texts, WorkspacePage.Words, WorkspacePage.GrammarGuide,
                WorkspacePage.Proposals, WorkspacePage.Diagnostics, WorkspacePage.Settings],
            workspace.Pages.Select(page => page.Page));
        Assert.Equal(
            ["Progress", "Texts", "Words", "Grammar Guide", "Proposals", "Diagnostics", "Settings"],
            workspace.Pages.Select(page => page.Title));
        Assert.DoesNotContain(workspace.VisiblePages, page => page.Page == WorkspacePage.Settings);
        Assert.Equal(workspace.Pages.Where(page => page.IsAvailable && page.Page != WorkspacePage.Settings),
            workspace.VisiblePages);
        Assert.Equal(WorkspacePage.Progress, workspace.CurrentPage);
        Assert.All(workspace.Pages, page => Assert.False(page.HasBadge));
    }

    [Theory]
    [InlineData(WorkspacePage.Progress)]
    [InlineData(WorkspacePage.Texts)]
    [InlineData(WorkspacePage.Words)]
    [InlineData(WorkspacePage.GrammarGuide)]
    [InlineData(WorkspacePage.Proposals)]
    [InlineData(WorkspacePage.Diagnostics)]
    [InlineData(WorkspacePage.Settings)]
    public void ShowingAPageMovesTheCurrentMarkerToThatPageAlone(WorkspacePage page)
    {
        var (_, _, workspace) = NewWorkspace();
        workspace.Context.Diagnostics = true;

        workspace.ShowPageCommand.Execute(page);

        Assert.Equal(page, workspace.CurrentPage);
        Assert.Same(workspace.PageOf(page), workspace.SelectedPage);
        Assert.Equal(workspace.Pages.Select(entry => entry.Page == page),
            workspace.Pages.Select(entry => entry.IsCurrent));
    }

    [Fact]
    public void SelectingASidebarEntryOpensItsPageAndClearingTheSelectionChangesNothing()
    {
        var (_, _, workspace) = NewWorkspace();

        workspace.SelectedPage = workspace.PageOf(WorkspacePage.Words);
        workspace.SelectedPage = null!;

        Assert.Equal(WorkspacePage.Words, workspace.CurrentPage);
    }

    [Fact]
    public void TextsAndWordsShareTheCurrentSelectionsWordRows()
    {
        var (_, _, workspace) = NewWorkspace();
        var texts = workspace.PageModel<TextsPageModel>();
        var words = workspace.PageModel<WordsPageModel>();

        Assert.Same(texts.Words, words.Words);
        Assert.NotNull(texts.ResultsInText);
    }

    [Fact]
    public async Task AssessmentPublishesItsRecordedWordToTheSharedEvidence()
    {
        var (fake, projectPicker, workspace) = NewWorkspace();
        await ChooseProjectAsync(fake, projectPicker, workspace);
        workspace.Selection.AllWordforms = true;
        fake.AssessCompletesWith(NewAssessResponse() with
        {
            Words = [Word("kitabu", "no-analysis", ProjectStanding.Approved)],
        });

        await workspace.Assess.RunCommand.ExecuteAsync(null);
        await workspace.Context.EvidencePublication;

        Assert.Equal(["kitabu"], workspace.Assess.Compare.Words.Select(word => word.Word));
        Assert.Equal(WorkspacePage.Progress, workspace.CurrentPage);
    }

    [Fact]
    public async Task ChoosingAProjectFromAnotherPageNamesItAndOpensProgress()
    {
        var (fake, projectPicker, workspace) = NewWorkspace();
        workspace.ShowPageCommand.Execute(WorkspacePage.Settings);

        await ChooseProjectAsync(fake, projectPicker, workspace);

        Assert.True(workspace.HasProject);
        Assert.Equal("one.fwdata", workspace.ProjectName);
        Assert.Equal(WorkspacePage.Progress, workspace.CurrentPage);
    }

    [Theory]
    [InlineData(true, WorkspacePage.Progress)]
    [InlineData(false, WorkspacePage.Progress)]
    public async Task OpeningAProjectStartsOnProgressRegardlessOfStandings(bool ready, WorkspacePage expected)
    {
        var (fake, picker, workspace) = NewWorkspace();
        var cards = ready
            ? new[] { new ProposalCard("p/ready", "Ready", "proposed", ProposalGroup.NotTried, false,
                [], TrialStanding.NotTried, null, []) }
            : Array.Empty<ProposalCard>();
        fake.ProposalStandingsCompleteWith(new ProposalStandings(cards, 0));

        await ChooseProjectAsync(fake, picker, workspace);

        Assert.Equal(expected, workspace.CurrentPage);
    }

    [Fact]
    public async Task StartingARunKeepsTheCurrentPage()
    {
        var (fake, projectPicker, workspace) = NewWorkspace();
        await ChooseProjectAsync(fake, projectPicker, workspace);
        workspace.Selection.AllWordforms = true;
        fake.AssessBlocksUntilCancelled(new Refusal("assess.cancelled", FailureReason.Cancelled, "Cancelled."));

        var running = workspace.Assess.RunCommand.ExecuteAsync(null);

        Assert.Equal(WorkspacePage.Progress, workspace.CurrentPage);
        workspace.Assess.CancelCommand.Execute(null);
        await running;
    }

    [Fact]
    public async Task RerunningPublishesTheDifferenceWithoutChangingPages()
    {
        var (fake, projectPicker, workspace) = NewWorkspace();
        await ChooseProjectAsync(fake, projectPicker, workspace);
        workspace.Selection.AllWordforms = true;
        AssessmentWordResult Timed(string outcome, bool incomplete) =>
            new("alimpiga", outcome, incomplete, "Search completed", 10, null) { ProjectStanding = ProjectStanding.NotPresent };
        fake.AssessCompletesWith(NewAssessResponse() with { Words = [Timed("timed-out", true)] });
        await workspace.Assess.RunCommand.ExecuteAsync(null);
        Assert.Equal(WorkspacePage.Progress, workspace.CurrentPage);

        fake.AssessCompletesWith(NewAssessResponse() with { Words = [Timed("no-analysis", false)] });
        await workspace.Assess.RerunAsync(["alimpiga"], 30_000);

        Assert.Equal(WorkspacePage.Progress, workspace.CurrentPage);
        Assert.Equal(MoveKind.Settled, workspace.Assess.Difference.SelectedMove!.Kind);

        fake.AssessCompletesWith(NewAssessResponse() with { Words = [Timed("no-analysis", false)] });
        await workspace.Assess.RunCommand.ExecuteAsync(null);

        Assert.Equal(WorkspacePage.Progress, workspace.CurrentPage);
    }

    [Fact]
    public async Task StoredGrammarFindingsAndProposalBadgeKeepTheirOwnCounts()
    {
        var (fake, projectPicker, workspace) = NewWorkspace();
        var stored = new GrammarCheckResponse(
            [
                new GrammarWarning(GrammarDiagnosticLevel.Warning, "Entry", [], [new GrammarWarningPart("dropped", GrammarWarningPartRole.Text)], "warning: dropped")
                { Group = "Dropped entry", Code = "fwdata.dropped-entry" },
                new GrammarWarning(GrammarDiagnosticLevel.Warning, "Entry", [], [new GrammarWarningPart("again", GrammarWarningPartRole.Text)], "warning: again")
                { Group = "Dropped entry", Code = "fwdata.dropped-entry" },
            ],
            HasBaseline: true);
        fake.StoredGrammarCheckIs(stored);
        fake.CheckGrammarCompletesWith(stored);

        await ChooseProjectAsync(fake, projectPicker, workspace);

        Assert.Equal(2, workspace.Context.GrammarSummary!.Findings.Count);
        var proposals = workspace.PageOf(WorkspacePage.Proposals);
        proposals.BadgeCount = 2;
        Assert.Equal("2 Proposals waiting for your decision", proposals.BadgeTip);
        Assert.Empty(fake.AssessRequests);
    }

    [Fact]
    public async Task StagedWordChangesShareOneOwnerAndCount()
    {
        var (fake, projectPicker, workspace) = NewWorkspace();
        await ChooseProjectAsync(fake, projectPicker, workspace);
        workspace.Selection.AllWordforms = true;
        fake.AssessCompletesWith(NewAssessResponse() with
        {
            Words = [Word("kitabu", "no-analysis", ProjectStanding.Approved), Word("mwalimu", "no-analysis", ProjectStanding.NotPresent)],
        });
        await workspace.Assess.RunCommand.ExecuteAsync(null);
        Assert.Same(workspace.Context.Changes, workspace.Assess.Compare.Changes);
        foreach (var word in workspace.Assess.Compare.Words) word.IsChecked = true;
        await workspace.Assess.Compare.ProposeCommand.ExecuteAsync(ChangeKinds.IncorrectSpelling);

        Assert.Equal(2, workspace.Context.Changes.Items.Count);
        Assert.Equal("2 word changes not applied yet", workspace.Context.Changes.CountText);

        await workspace.Context.Changes.RemoveCommand.ExecuteAsync(workspace.Context.Changes.Items[0]);

        Assert.Equal("1 word change not applied yet", workspace.Context.Changes.CountText);
    }

    [Theory]
    [InlineData(900, true)]
    [InlineData(999, true)]
    [InlineData(1000, false)]
    [InlineData(1280, false)]
    [InlineData(1400, false)]
    public void TheSidebarCollapsesToIconsBelowItsWidthThreshold(double width, bool collapsed)
    {
        var (_, _, workspace) = NewWorkspace();

        workspace.UpdateWindowWidth(width);

        Assert.Equal(collapsed, workspace.IsSidebarCollapsed);
        Assert.Equal(!collapsed, workspace.IsSidebarExpanded);
    }

    [Fact]
    public async Task TheProjectMenuSelectsANewProjectThroughThePicker()
    {
        var (fake, projectPicker, workspace) = NewWorkspace();
        fake.CurrentBaselineCompletesWith(new CurrentBaselineResponse(Token, Saved, false));
        fake.ListTextsCompletesWith(new TextInventoryResponse([], HasBaseline: true));
        projectPicker.PathToReturn = ProjectPath;

        await workspace.SelectNewProjectCommand.ExecuteAsync(null);

        Assert.Equal("one.fwdata", workspace.ProjectName);
    }

    [Fact]
    public void OpenRecentListsTheKnownProjectsOtherThanTheOpenOneAndOpensTheChosenOne()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (fake, projectPicker, workspace) = NewWorkspace();
            await using var ownedWorkspace = workspace;
            var other = Path.Combine(Path.GetTempPath(), "projects", "two.fwdata");
            fake.KnownProjectsListIs(
            [
                new KnownProjectSummary(other, Saved),
                new KnownProjectSummary(ProjectPath, Saved),
            ]);
            await workspace.Project.LoadKnownProjectsAsync();
            await ChooseProjectAsync(fake, projectPicker, workspace);
            await workspace.RefreshKnownProjectsAsync();

            var recent = Assert.Single(workspace.RecentProjects);
            Assert.Equal(other, recent.FullFwDataPath);
            Assert.Equal("two", recent.Name);
            Assert.Equal("two", workspace.RecentProjectsText);

            await workspace.OpenRecentProjectCommand.ExecuteAsync(recent);

            Assert.Equal("two.fwdata", workspace.ProjectName);
            Assert.Equal(["one"], workspace.RecentProjects.Select(project => project.Name));
        }, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void TheProjectMenuOpensAnotherKnownProjectFromTheOverview()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (fake, projectPicker, workspace) = NewWorkspace();
            await using var ownedWorkspace = workspace;
            var other = Path.Combine(Path.GetTempPath(), "projects", "two.fwdata");
            fake.KnownProjectsListIs([new KnownProjectSummary(other, Saved)]);
            await workspace.Project.LoadKnownProjectsAsync();
            await ChooseProjectAsync(fake, projectPicker, workspace);
            await workspace.RefreshKnownProjectsAsync();

            var recent = Assert.Single(workspace.RecentProjects);
            Assert.Equal(other, recent.FullFwDataPath);
            await workspace.OpenRecentProjectCommand.ExecuteAsync(recent);

            Assert.Equal("two.fwdata", workspace.ProjectName);
            Assert.Equal(other, workspace.Context.ProjectPath);
        }, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task ConfigureOpensTheSetupDialogUnlessSomethingElseTakesTheHook()
    {
        var (fake, projectPicker, workspace) = NewWorkspace();
        await ChooseProjectAsync(fake, projectPicker, workspace);
        workspace.Context.Setup!.SkipCommand.Execute(null);
        workspace.CurrentPage = WorkspacePage.Progress;

        workspace.ConfigureCommand.Execute(null);

        Assert.Equal(WorkspacePage.Progress, workspace.CurrentPage);
        Assert.True(workspace.Context.Setup.IsOpen);
        Assert.Equal(0, workspace.Context.Setup.Step);

        var opened = 0;
        workspace.Context.Setup.SkipCommand.Execute(null);
        workspace.ShowPageCommand.Execute(WorkspacePage.Progress);
        workspace.OpenConfiguration = () => opened++;
        workspace.ConfigureCommand.Execute(null);

        Assert.Equal(1, opened);
        Assert.Equal(WorkspacePage.Progress, workspace.CurrentPage);
    }

    [Fact]
    public void BeforeAProjectIsChosenTheFreshnessLineSaysNothingAndRefreshIsOff()
    {
        var (_, _, workspace) = NewWorkspace();

        Assert.Equal(ProjectFreshness.NoProject, workspace.Freshness);
        Assert.False(workspace.HasFreshness);
        Assert.False(workspace.RefreshCommand.CanExecute(null));
    }

    [Fact]
    public async Task ABaselineOfTheLastSaveIsCurrent()
    {
        var (fake, projectPicker, workspace) = NewWorkspace();

        await ChooseProjectAsync(fake, projectPicker, workspace);

        Assert.Equal(ProjectFreshness.Current, workspace.Freshness);
        Assert.Equal("Current", workspace.FreshnessLabel);
        Assert.StartsWith("Baseline of ", workspace.FreshnessDetail);
        Assert.DoesNotContain("saved", workspace.FreshnessDetail);
        Assert.DoesNotContain("FieldWorks does not currently hold this project.", workspace.FreshnessDetail);
        Assert.True(workspace.RefreshCommand.CanExecute(null));
    }

    [Fact]
    public async Task ApplyingChangesMakesTheNumbersStaleUntilRefresh()
    {
        var (fake, projectPicker, workspace) = NewWorkspace();
        await ChooseProjectAsync(fake, projectPicker, workspace);

        workspace.Context.RecordApplied();

        Assert.Equal(ProjectFreshness.SavedSince, workspace.Freshness);
        Assert.Equal("Changes applied", workspace.FreshnessLabel);
        Assert.Contains("Baseline of", workspace.FreshnessDetail);
        Assert.Contains("changes applied after it", workspace.FreshnessDetail);
    }

    [Fact]
    public async Task ARefusedRefreshKeepsItsReasonAndFieldWorksHoldStatusVisible()
    {
        var (fake, projectPicker, workspace) = NewWorkspace();
        await ChooseProjectAsync(fake, projectPicker, workspace, fieldWorksHeldProject: true);
        Assert.Contains("FieldWorks holds this project open right now.", workspace.FreshnessDetail);
        var refusal = new Refusal("baseline.busy", FailureReason.Busy, "FieldWorks still has the project open.");
        fake.CaptureBaselineRefusesWith(refusal);

        await workspace.RefreshCommand.ExecuteAsync(null);

        Assert.Equal("Refresh refused", workspace.FreshnessLabel);
        Assert.Equal(string.Empty, workspace.FreshnessDetail);
        Assert.Contains(refusal.Message, workspace.Baseline.ShownRefusal!.Details);
        Assert.Equal("FieldWorks holds this project open right now.", workspace.Baseline.HeldStatusText);
        Assert.Empty(fake.AssessRequests);
    }

    [Fact]
    public async Task ASaveAfterTheBaselineShowsFieldWorksSavedSinceAndNothingReruns()
    {
        var (fake, projectPicker, workspace) = NewWorkspace();
        await ChooseProjectAsync(fake, projectPicker, workspace);
        workspace.Selection.AllWordforms = true;

        fake.CurrentBaselineCompletesWith(new CurrentBaselineResponse(Token, Saved, false)
        {
            ProjectLastWriteUtc = Saved.AddHours(3),
        });
        await workspace.CheckFreshnessAsync();

        Assert.Equal(ProjectFreshness.SavedSince, workspace.Freshness);
        Assert.Equal("FieldWorks saved since", workspace.FreshnessLabel);
        Assert.Empty(fake.CaptureBaselineRequests);
        Assert.Empty(fake.AssessRequests);
    }

    [Fact]
    public async Task ChoosingAProjectSavedSinceItsBaselineSaysSoAtOnce()
    {
        var (fake, projectPicker, workspace) = NewWorkspace();

        await ChooseProjectAsync(fake, projectPicker, workspace, Saved.AddMinutes(5));

        Assert.Equal(ProjectFreshness.SavedSince, workspace.Freshness);
    }

    [Fact]
    public async Task AProjectWithNoBaselineSaysSo()
    {
        var (fake, projectPicker, workspace) = NewWorkspace();
        var root = Directory.CreateTempSubdirectory("motif-existing-store-");
        var projectPath = Path.Combine(root.FullName, "one.fwdata");
        File.WriteAllBytes(Path.ChangeExtension(projectPath, ".motif.db"), []);
        fake.CurrentBaselineCompletesWith(new CurrentBaselineResponse(null, null, false) { ProjectLastWriteUtc = Saved });
        fake.ListTextsCompletesWith(new TextInventoryResponse([], HasBaseline: false));
        projectPicker.PathToReturn = projectPath;

        try
        {
            await workspace.Project.BrowseCommand.ExecuteAsync(null);

            Assert.Equal(ProjectFreshness.NoBaseline, workspace.Freshness);
            Assert.Equal("No Baseline yet", workspace.FreshnessLabel);
            Assert.True(workspace.RefreshCommand.CanExecute(null));
            Assert.Empty(fake.CaptureBaselineRequests);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task RefreshCapturesOnlyAndTheNextParseSaysWhatChanged()
    {
        var (fake, projectPicker, workspace) = NewWorkspace();
        fake.DefaultSelectionCompletesWith(new NamedSelectionProjection(
            "Default", [], ["kitabu"], string.Empty, string.Empty, 1000, ParserWorkLimit.Default));
        await ChooseProjectAsync(fake, projectPicker, workspace, Saved.AddHours(3));
        workspace.Selection.AllWordforms = true;
        fake.AssessCompletesWith(NewAssessResponse() with { Words = [Word("kitabu", "timed-out", ProjectStanding.Approved)] });
        await workspace.Assess.RunCommand.ExecuteAsync(null);
        workspace.ShowPageCommand.Execute(WorkspacePage.Progress);

        fake.CaptureBaselineCompletesWith(new BaselineCaptureResponse(Token, ProjectPath, Saved.AddHours(3), false, false));
        await workspace.RefreshCommand.ExecuteAsync(null);

        Assert.Single(fake.CaptureBaselineRequests);
        Assert.Single(fake.AssessRequests);
        Assert.Equal(ProjectFreshness.Refreshed, workspace.Freshness);
        Assert.Equal("Refreshed", workspace.FreshnessLabel);
        Assert.True(workspace.Context.NeedsAssessment);
        Assert.True(workspace.ShowsParseAllWordsAction);

        fake.AssessCompletesWith(NewAssessResponse(Saved.AddHours(3)) with
            { Words = [Word("kitabu", "analysed", ProjectStanding.Approved)] });
        await workspace.ParseAllWordsCommand.ExecuteAsync(null);

        Assert.False(workspace.Context.NeedsAssessment);
        Assert.Equal(WorkspacePage.Progress, workspace.CurrentPage);
    }

    [Fact]
    public async Task WhileRefreshingTheTopRowHidesBothActionsUntilCaptureFinishes()
    {
        var (fake, projectPicker, workspace) = NewWorkspace();
        await ChooseProjectAsync(fake, projectPicker, workspace);
        var captured = new TaskCompletionSource<CommandOutcome<BaselineCaptureResponse>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        fake.OnCaptureBaseline((_, _) => captured.Task);

        var refreshing = workspace.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(ProjectFreshness.Refreshing, workspace.Freshness);
        Assert.Equal("Refreshing", workspace.FreshnessLabel);
        Assert.False(workspace.ShowsRefreshAction);
        Assert.False(workspace.ShowsParseAllWordsAction);
        Assert.False(workspace.RefreshCommand.CanExecute(null));
        Assert.True(workspace.ProjectSwitchEnabled);

        captured.SetResult(CommandOutcome<BaselineCaptureResponse>.Success(
            new BaselineCaptureResponse(Token, ProjectPath, Saved, false, false)));
        await refreshing;

        Assert.NotEqual(ProjectFreshness.Refreshing, workspace.Freshness);
    }

    [Fact]
    public async Task RefreshWithNothingToAssessOnlyCapturesTheBaseline()
    {
        var (fake, projectPicker, workspace) = NewWorkspace();
        await ChooseProjectAsync(fake, projectPicker, workspace, Saved.AddHours(3));
        fake.CaptureBaselineCompletesWith(new BaselineCaptureResponse(Token, ProjectPath, Saved.AddHours(3), false, false));

        await workspace.RefreshCommand.ExecuteAsync(null);

        Assert.Single(fake.CaptureBaselineRequests);
        Assert.Empty(fake.AssessRequests);
        Assert.Equal(ProjectFreshness.Refreshed, workspace.Freshness);
        Assert.True(workspace.Context.NeedsAssessment);
    }

    [Fact]
    public async Task RefreshingTheBaselineChecksItsGrammarAgain()
    {
        var (fake, projectPicker, workspace) = NewWorkspace();
        await ChooseProjectAsync(fake, projectPicker, workspace);
        Assert.Empty(fake.CheckGrammarRequests);

        fake.CaptureBaselineCompletesWith(new BaselineCaptureResponse(Token, ProjectPath, Saved, false, false));
        await workspace.Baseline.RefreshCommand.ExecuteAsync(null);

        Assert.Single(fake.CheckGrammarRequests);
        Assert.Empty(fake.AssessRequests);
    }

    [Fact]
    public async Task OpeningWaitsForOtherParticipantsWithoutStartingGrammar()
    {
        using var testLifetime = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        var (fake, projectPicker, workspace) = NewWorkspace();
        var remainingOpen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = new OpeningGatePage(workspace.Context, remainingOpen.Task);
        var checking = new TaskCompletionSource<CommandOutcome<GrammarCheckResponse>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        fake.OnCheckGrammar((_, _) => checking.Task);
        try
        {
            fake.CurrentBaselineCompletesWith(new CurrentBaselineResponse(Token, Saved, false));
            fake.ListTextsCompletesWith(new TextInventoryResponse([], HasBaseline: true));
            var opening = workspace.SetProjectAsync(ProjectPath, testLifetime.Token, captureFreshProject: false);
            Assert.False(opening.IsCompleted);
            Assert.Empty(fake.CheckGrammarRequests);

            remainingOpen.SetResult();
            await opening.WaitAsync(testLifetime.Token);

            Assert.Empty(fake.CheckGrammarRequests);
            Assert.False(workspace.Context.GrammarCheck.IsLoading);
            Assert.False(workspace.Context.IsOpeningProject);
            Assert.Equal(WorkspacePage.Progress, workspace.CurrentPage);
            Assert.Empty(fake.AssessRequests);
        }
        finally
        {
            remainingOpen.TrySetResult();
            checking.TrySetResult(CommandOutcome<GrammarCheckResponse>.Success(new([], HasBaseline: true)));
            await workspace.DisposeAsync();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedOrCancelledProjectOpeningNeverStartsAutomaticGrammar(bool cancel)
    {
        using var testLifetime = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        using var openingCancellation = new CancellationTokenSource();
        var (fake, _, workspace) = NewWorkspace();
        fake.CurrentBaselineCompletesWith(new CurrentBaselineResponse(Token, Saved, false));
        var remainingOpen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = new OpeningGatePage(workspace.Context, remainingOpen.Task);
        try
        {
            var opening = workspace.SetProjectAsync(ProjectPath, openingCancellation.Token,
                captureFreshProject: false);
            Assert.False(opening.IsCompleted);
            if (cancel)
            {
                openingCancellation.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => opening.WaitAsync(testLifetime.Token));
            }
            else
            {
                remainingOpen.SetException(new InvalidOperationException("The project could not finish opening."));
                await Assert.ThrowsAsync<InvalidOperationException>(() => opening.WaitAsync(testLifetime.Token));
            }

            Assert.False(workspace.Context.IsOpeningProject);
            Assert.Empty(fake.CheckGrammarRequests);
            Assert.Empty(fake.AssessRequests);
        }
        finally
        {
            remainingOpen.TrySetResult();
            await workspace.DisposeAsync();
        }
    }

    private sealed class OpeningGatePage(WorkspaceContext context, Task remainingOpen) : PageModel(context)
    {
        protected override Task OnProjectOpenedAsync(string projectPath, CancellationToken cancellationToken) =>
            remainingOpen.WaitAsync(cancellationToken);
    }

    [Fact]
    public async Task OpeningAnUncheckedBaselineReadsOnlyUntilAnExplicitGrammarCheck()
    {
        using var testLifetime = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        var (fake, projectPicker, workspace) = NewWorkspace();
        var checking = new TaskCompletionSource<CommandOutcome<GrammarCheckResponse>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var published = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fake.OnCheckGrammar((_, _) => checking.Task);
        var grammar = workspace.Context.GrammarCheck;
        fake.OnOverview((_, _) =>
        {
            if (grammar.HasChecked) published.TrySetResult();
            return Task.FromResult(CommandOutcome<OverviewResponse>.Refused(
                new Refusal("overview.not-configured", FailureReason.Refused, "No Overview configured.")));
        });
        var finding = new GrammarWarning(GrammarDiagnosticLevel.Warning, "Entry", [],
            [new GrammarWarningPart("dropped", GrammarWarningPartRole.Text)], "warning: dropped")
        { Group = "Dropped entry", Code = "fwdata.dropped-entry" };
        var response = CommandOutcome<GrammarCheckResponse>.Success(new([finding], HasBaseline: true));
        try
        {
            await ChooseProjectAsync(fake, projectPicker, workspace).WaitAsync(testLifetime.Token);
            Assert.True(workspace.Baseline.HasBaseline);
            Assert.Equal(WorkspacePage.Progress, workspace.CurrentPage);
            Assert.Empty(fake.CheckGrammarRequests);
            var check = grammar.CheckCommand.ExecuteAsync(null);
            Assert.Equal(ProjectPath, Assert.Single(fake.CheckGrammarRequests).ProjectPath);
            Assert.True(grammar.IsLoading);
            Assert.False(grammar.HasChecked);
            Assert.Empty(fake.CaptureBaselineRequests);
            Assert.Empty(fake.AssessRequests);
            Assert.Null(workspace.Context.Evidence.Assessment);

            checking.SetResult(response);
            await check.WaitAsync(testLifetime.Token);
            await published.Task.WaitAsync(testLifetime.Token);

            Assert.True(grammar.HasChecked);
            Assert.False(grammar.IsLoading);
            Assert.Equal("fwdata.dropped-entry", Assert.Single(grammar.Warnings.Findings).Code);
            Assert.Equal("1", workspace.Context.GrammarSummary!.Findings.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Assert.Equal("fwdata.dropped-entry", Assert.Single(workspace.Context.GrammarSummary!.Findings).Code);
            Assert.Equal(WorkspacePage.Progress, workspace.CurrentPage);
            Assert.Empty(fake.AssessRequests);
            Assert.Null(workspace.Context.Evidence.Assessment);
        }
        finally
        {
            checking.TrySetResult(response);
            await workspace.DisposeAsync();
        }
    }

    [Fact]
    public async Task SwitchingProjectsCancelsAndDrainsGrammarWithoutPublishingItsOldResult()
    {
        using var testLifetime = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        var (fake, projectPicker, workspace) = NewWorkspace();
        var otherPath = Path.Combine(Path.GetTempPath(), "projects", "two.fwdata");
        var checking = new TaskCompletionSource<CommandOutcome<GrammarCheckResponse>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken checkCancellation = default;
        fake.OnCheckGrammar((_, cancellation) =>
        {
            checkCancellation = cancellation;
            return checking.Task;
        });
        var current = new GrammarCheckResponse([], HasBaseline: true);
        fake.StoredGrammarCheckHandler = (request, _) => Task.FromResult(
            CommandOutcome<StoredGrammarCheckResponse>.Success(new(
                request.ProjectPath == otherPath ? current : null)));
        var oldFinding = new GrammarWarning(GrammarDiagnosticLevel.Warning, "Old project", [],
            [new GrammarWarningPart("obsolete", GrammarWarningPartRole.Text)], "warning: obsolete");
        var oldResult = CommandOutcome<GrammarCheckResponse>.Success(new([oldFinding], HasBaseline: true));
        try
        {
            await ChooseProjectAsync(fake, projectPicker, workspace).WaitAsync(testLifetime.Token);
            Assert.Empty(fake.CheckGrammarRequests);
            var check = workspace.Context.GrammarCheck.CheckCommand.ExecuteAsync(null);
            Assert.Single(fake.CheckGrammarRequests);
            Assert.True(workspace.Context.GrammarCheck.IsLoading);
            var oldOverviewReads = fake.OverviewRequests.Count(request => request.ProjectPath == ProjectPath);

            var switching = workspace.SetProjectAsync(otherPath, captureFreshProject: false);

            Assert.True(checkCancellation.IsCancellationRequested);
            Assert.False(switching.IsCompleted);
            checking.SetResult(oldResult);
            await check.WaitAsync(testLifetime.Token);
            await switching.WaitAsync(testLifetime.Token);

            Assert.Equal(otherPath, workspace.Context.ProjectPath);
            Assert.Equal(WorkspacePage.Progress, workspace.CurrentPage);
            Assert.Equal(oldOverviewReads,
                fake.OverviewRequests.Count(request => request.ProjectPath == ProjectPath));
            var grammar = workspace.Context.GrammarCheck;
            Assert.True(grammar.HasChecked);
            Assert.False(grammar.IsLoading);
            Assert.Empty(grammar.Warnings.Findings);
            Assert.Empty(workspace.Context.GrammarSummary!.Findings);
            Assert.Single(fake.CheckGrammarRequests);
            Assert.Empty(fake.AssessRequests);
            Assert.Null(workspace.Context.Evidence.Assessment);
        }
        finally
        {
            checking.TrySetResult(oldResult);
            await workspace.DisposeAsync();
        }
    }

    [Fact]
    public async Task TheNestedGrammarRetryPublishesOverviewAndStopsWithTheWorkspace()
    {
        using var testLifetime = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        var (fake, projectPicker, workspace) = NewWorkspace();
        fake.StoredGrammarCheckIs(new GrammarCheckResponse([], HasBaseline: true));
        await ChooseProjectAsync(fake, projectPicker, workspace).WaitAsync(testLifetime.Token);
        var grammar = workspace.Context.GrammarCheck;
        var beforeRetry = fake.OverviewRequests.Count;

        await grammar.CheckCommand.ExecuteAsync(null);

        Assert.Equal(beforeRetry + 1, fake.OverviewRequests.Count);
        var checking = new TaskCompletionSource<CommandOutcome<GrammarCheckResponse>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken checkCancellation = default;
        fake.OnCheckGrammar((_, cancellation) =>
        {
            checkCancellation = cancellation;
            return checking.Task;
        });
        var response = CommandOutcome<GrammarCheckResponse>.Success(new([], HasBaseline: true));
        try
        {
            var retrying = grammar.CheckCommand.ExecuteAsync(null);
            Assert.True(grammar.IsLoading);
            Assert.False(workspace.Context.GrammarCheck.CheckCommand.CanExecute(null));
            var beforeStop = fake.OverviewRequests.Count;

            var stopping = workspace.Context.StopPageWorkAsync();

            Assert.True(checkCancellation.IsCancellationRequested);
            Assert.False(stopping.IsCompleted);
            checking.SetResult(response);
            await stopping.WaitAsync(testLifetime.Token);
            await retrying.WaitAsync(testLifetime.Token);
            Assert.Equal(beforeStop, fake.OverviewRequests.Count);
            Assert.False(grammar.IsLoading);
            Assert.Empty(fake.AssessRequests);
        }
        finally
        {
            checking.TrySetResult(response);
            await workspace.DisposeAsync();
        }
    }

    [Fact]
    public async Task OpeningAProjectWithAStoredCheckShowsItAndLeavesReloadWorking()
    {
        var (fake, projectPicker, workspace) = NewWorkspace();
        var stored = new GrammarCheckResponse(
            [new GrammarWarning(GrammarDiagnosticLevel.Warning, "Entry", [], [new GrammarWarningPart("dropped", GrammarWarningPartRole.Text)], "warning: dropped")
            { Group = "Dropped entry", Code = "fwdata.dropped-entry" }],
            HasBaseline: true);
        fake.StoredGrammarCheckIs(stored);
        fake.CheckGrammarCompletesWith(stored);

        await ChooseProjectAsync(fake, projectPicker, workspace);

        Assert.True(workspace.Context.GrammarCheck.HasChecked);
        Assert.Empty(fake.CheckGrammarRequests);
        Assert.Equal("1 finding", workspace.Context.GrammarCheck.SummaryText);
        Assert.True(workspace.Context.GrammarCheck.CheckCommand.CanExecute(null));
    }

    [Fact]
    public async Task ClearingAProjectImmediatelyRemovesItsGrammarFindings()
    {
        var (fake, projectPicker, workspace) = NewWorkspace();
        var stored = new GrammarCheckResponse(
            [new GrammarWarning(GrammarDiagnosticLevel.Warning, "Entry", [],
                [new GrammarWarningPart("dropped", GrammarWarningPartRole.Text)], "warning: dropped")],
            HasBaseline: true);
        fake.StoredGrammarCheckIs(stored);
        fake.CheckGrammarCompletesWith(stored);
        await ChooseProjectAsync(fake, projectPicker, workspace);
        Assert.Equal("1", workspace.Context.GrammarSummary!.Findings.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));

        workspace.Context.ClearProject();

        Assert.Empty(workspace.Context.GrammarSummary!.Findings);
        Assert.Equal("Not checked yet", workspace.Context.GrammarSummary?.SummaryText);
        Assert.False(workspace.Context.GrammarCheck.HasChecked);
    }

    [Fact]
    public async Task RefreshClearsAnOlderAssessmentAndMakesTheNewBaselineReadyToParse()
    {
        var (fake, projectPicker, workspace) = NewWorkspace();
        await ChooseProjectAsync(fake, projectPicker, workspace);
        workspace.Selection.AllWordforms = true;
        fake.AssessCompletesWith(NewAssessResponse() with { Words = [Word("kitabu", "analysed", ProjectStanding.Approved)] });
        await workspace.Assess.RunCommand.ExecuteAsync(null);
        Assert.Equal(ProjectFreshness.Current, workspace.Freshness);

        var later = Saved.AddHours(3);
        fake.CaptureBaselineCompletesWith(new BaselineCaptureResponse(Token, ProjectPath, later, false, false));
        await workspace.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(ProjectFreshness.Refreshed, workspace.Freshness);
        Assert.True(workspace.Context.NeedsAssessment);
        Assert.True(workspace.Context.HasNoEvidence);
        Assert.Equal("Refreshed. Parse all words to update the numbers.", workspace.FreshnessDetail);
    }

    [Fact]
    public async Task RefreshClearsOldNumbersAndLeavesTheNewBaselineUnmeasured()
    {
        var (fake, projectPicker, workspace) = NewWorkspace();
        await ChooseProjectAsync(fake, projectPicker, workspace);
        workspace.Selection.AllWordforms = true;
        fake.AssessCompletesWith(NewAssessResponse() with { Words = [Word("kitabu", "analysed", ProjectStanding.Approved)] });
        await workspace.Assess.RunCommand.ExecuteAsync(null);

        fake.CaptureBaselineCompletesWith(new BaselineCaptureResponse(Token, ProjectPath, Saved.AddHours(3), false, false));
        await workspace.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(ProjectFreshness.Refreshed, workspace.Freshness);
        Assert.Null(workspace.Context.Evidence.Assessment);
        Assert.True(workspace.Context.NeedsAssessment);
    }

    [Fact]
    public async Task EvidencePublishedFromAnOlderSaveThanTheBaselineIsNotCurrent()
    {
        var (fake, projectPicker, workspace) = NewWorkspace();
        await ChooseProjectAsync(fake, projectPicker, workspace);
        Assert.Equal(ProjectFreshness.Current, workspace.Freshness);

        var stored = NewAssessResponse(Saved.AddHours(-3));
        workspace.Context.PublishEvidence(new WorkspaceEvidence(stored, Saved.AddHours(-2), WasRerun: false));

        Assert.Equal(Saved.AddHours(-3), workspace.Context.Evidence.MeasuredSaveUtc);
        Assert.False(workspace.Context.NeedsAssessment);
        Assert.Equal(ProjectFreshness.SavedSince, workspace.Freshness);
        Assert.Contains("Baseline of", workspace.FreshnessDetail);
        Assert.Contains("saved", workspace.FreshnessDetail);
    }

    [Fact]
    public async Task RecapturingTheSameSaveUnderAnAssessmentStaysCurrent()
    {
        var (fake, projectPicker, workspace) = NewWorkspace();
        await ChooseProjectAsync(fake, projectPicker, workspace);
        workspace.Selection.AllWordforms = true;
        fake.AssessCompletesWith(NewAssessResponse() with { Words = [Word("kitabu", "analysed", ProjectStanding.Approved)] });
        await workspace.Assess.RunCommand.ExecuteAsync(null);

        fake.CaptureBaselineCompletesWith(new BaselineCaptureResponse(Token, ProjectPath, Saved, false, false));
        await workspace.Baseline.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(ProjectFreshness.Current, workspace.Freshness);
    }

    [Fact]
    public void TheAiHandoffActionOffersARewriteOnlyOnceFilesExist()
    {
        var (_, _, workspace) = NewWorkspace();
        Assert.Equal("Write the AI Handoff", workspace.PageModel<SettingsPageModel>().Handoff.HandoffActionText);

        workspace.PageModel<SettingsPageModel>().Handoff.Handoff.Files.Add(new HandoffFileViewModel("handoff.md", @"C:\handoff\handoff.md"));
        workspace.PageModel<SettingsPageModel>().Handoff.Handoff.State = RunState.Completed;

        Assert.Equal("Write the AI Handoff again", workspace.PageModel<SettingsPageModel>().Handoff.HandoffActionText);
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
