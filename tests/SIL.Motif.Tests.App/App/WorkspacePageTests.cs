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
/// Pins the page shell's state on <see cref="HandoffWorkspaceViewModel"/>: which page is showing, the sidebar's
/// entries, badges and collapsed mode, the project menu, the freshness line, and the one changes list every page
/// shares.
/// </summary>
public sealed class WorkspacePageTests
{
    private const string ProjectPath = @"C:\projects\one.fwdata";

    private static readonly DateTimeOffset Saved = new(2026, 9, 5, 10, 58, 0, TimeSpan.Zero);

    private static readonly BaselineToken Token = new(
        "project-1", "sha256:" + new string('a', 64), "1", "2026-09-05T11:02:00Z", "sha256:" + new string('b', 64));

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
            new HandoffViewModel(fake, selection, new FakeFolderPicker(), new FakeDragSource()),
            fake);
        return (fake, projectPicker, workspace);
    }

    private static async Task ChooseProjectAsync(
        FakeCommandClient fake, FakeProjectPicker projectPicker, HandoffWorkspaceViewModel workspace,
        DateTimeOffset? projectLastWriteUtc = null)
    {
        fake.CurrentBaselineCompletesWith(new CurrentBaselineResponse(Token, Saved, false)
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
    public void TheSidebarListsTheSevenPagesInOrderAndOpensOnOverview()
    {
        var (_, _, workspace) = NewWorkspace();

        Assert.Equal(
            [WorkspacePage.Overview, WorkspacePage.Texts, WorkspacePage.TryAWord, WorkspacePage.Timing,
                WorkspacePage.Warnings, WorkspacePage.Review, WorkspacePage.AiHandoff],
            workspace.Pages.Select(page => page.Page));
        Assert.Equal(
            ["Overview", "Texts", "Try a Word", "Timing", "Warnings", "Review changes", "AI Handoff"],
            workspace.Pages.Select(page => page.Title));
        Assert.Equal(WorkspacePage.Overview, workspace.CurrentPage);
        Assert.All(workspace.Pages, page => Assert.False(page.HasBadge));
    }

    [Theory]
    [InlineData(WorkspacePage.Overview)]
    [InlineData(WorkspacePage.Texts)]
    [InlineData(WorkspacePage.TryAWord)]
    [InlineData(WorkspacePage.Timing)]
    [InlineData(WorkspacePage.Warnings)]
    [InlineData(WorkspacePage.Review)]
    [InlineData(WorkspacePage.AiHandoff)]
    public void ShowingAPageMovesTheCurrentMarkerToThatPageAlone(WorkspacePage page)
    {
        var (_, _, workspace) = NewWorkspace();

        workspace.ShowPageCommand.Execute(page);

        Assert.Equal(page, workspace.CurrentPage);
        Assert.Same(workspace.Pages[(int)page], workspace.SelectedPage);
        Assert.Equal(Enum.GetValues<WorkspacePage>().Select(each => each == page),
            workspace.Pages.Select(entry => entry.IsCurrent));
    }

    [Fact]
    public void SelectingASidebarEntryOpensItsPageAndClearingTheSelectionChangesNothing()
    {
        var (_, _, workspace) = NewWorkspace();

        workspace.SelectedPage = workspace.Pages[5];
        workspace.SelectedPage = null!;

        Assert.Equal(WorkspacePage.Review, workspace.CurrentPage);
    }

    [Fact]
    public void TheTextsPageOpensOnTheMatrixAndEachTabRaisesOnlyItsOwnFlag()
    {
        var (_, _, workspace) = NewWorkspace();
        Assert.Equal(TextsTab.Matrix, workspace.TextsPage.Tab);

        workspace.TextsPage.ShowTabCommand.Execute(TextsTab.InText);

        var texts = workspace.TextsPage;
        Assert.Equal(
            [false, false, false, false, true],
            new[] { texts.ShowMatrix, texts.ShowWhatChanged, texts.ShowWords, texts.ShowTexts, texts.ShowInText });
    }

    [Fact]
    public async Task ChoosingAProjectFromAnotherPageNamesItAndReturnsToOverview()
    {
        var (fake, projectPicker, workspace) = NewWorkspace();
        workspace.ShowPageCommand.Execute(WorkspacePage.AiHandoff);

        await ChooseProjectAsync(fake, projectPicker, workspace);

        Assert.True(workspace.HasProject);
        Assert.Equal("one.fwdata", workspace.ProjectName);
        Assert.Equal(WorkspacePage.Overview, workspace.CurrentPage);
    }

    [Fact]
    public async Task ARunStartingOpensTheMatrixOnTheTextsPage()
    {
        var (fake, projectPicker, workspace) = NewWorkspace();
        await ChooseProjectAsync(fake, projectPicker, workspace);
        workspace.Selection.AllWordforms = true;
        workspace.TextsPage.ShowTabCommand.Execute(TextsTab.InText);
        fake.AssessBlocksUntilCancelled(new Refusal("assess.cancelled", FailureReason.Cancelled, "Cancelled."));

        var running = workspace.Assess.RunCommand.ExecuteAsync(null);

        Assert.Equal(WorkspacePage.Texts, workspace.CurrentPage);
        Assert.True(workspace.TextsPage.ShowMatrix);
        workspace.Assess.CancelCommand.Execute(null);
        await running;
    }

    [Fact]
    public async Task ARerunOpensWhatChangedAndAFullRunStaysOnTheMatrix()
    {
        var (fake, projectPicker, workspace) = NewWorkspace();
        await ChooseProjectAsync(fake, projectPicker, workspace);
        workspace.Selection.AllWordforms = true;
        AssessmentWordResult Timed(string outcome, bool incomplete) =>
            new("alimpiga", outcome, incomplete, "Search completed", 10, null) { ProjectStanding = ProjectStanding.NotPresent };
        fake.AssessCompletesWith(NewAssessResponse() with { Words = [Timed("timed-out", true)] });
        await workspace.Assess.RunCommand.ExecuteAsync(null);
        Assert.True(workspace.TextsPage.ShowMatrix);

        fake.AssessCompletesWith(NewAssessResponse() with { Words = [Timed("no-analysis", false)] });
        await workspace.Assess.RerunAsync(["alimpiga"], 30_000);

        Assert.True(workspace.TextsPage.ShowWhatChanged);
        Assert.Equal(MoveKind.Settled, workspace.Assess.Difference.SelectedMove!.Kind);

        fake.AssessCompletesWith(NewAssessResponse() with { Words = [Timed("no-analysis", false)] });
        await workspace.Assess.RunCommand.ExecuteAsync(null);

        Assert.True(workspace.TextsPage.ShowMatrix);
    }

    [Fact]
    public async Task TheWarningsBadgeCountsTheStoredGrammarCheck()
    {
        var (fake, projectPicker, workspace) = NewWorkspace();
        var stored = new GrammarCheckResponse(
            [
                new GrammarWarning("warning", "Entry", [], [new GrammarWarningPart("dropped", "text")], "warning: dropped"),
                new GrammarWarning("warning", "Entry", [], [new GrammarWarningPart("again", "text")], "warning: again"),
            ],
            HasBaseline: true);
        fake.StoredGrammarCheckIs(stored);
        fake.CheckGrammarCompletesWith(stored);

        await ChooseProjectAsync(fake, projectPicker, workspace);

        var warnings = workspace.Pages[(int)WorkspacePage.Warnings];
        Assert.Equal("2", warnings.Badge);
        Assert.True(warnings.HasBadge);
        Assert.Empty(fake.AssessRequests);
    }

    [Fact]
    public async Task TheReviewBadgeCountsTheOneChangesListTheMatrixAddsTo()
    {
        var (fake, projectPicker, workspace) = NewWorkspace();
        await ChooseProjectAsync(fake, projectPicker, workspace);
        workspace.Selection.AllWordforms = true;
        fake.AssessCompletesWith(NewAssessResponse() with
        {
            Words = [Word("kitabu", "no-analysis", ProjectStanding.Approved), Word("mwalimu", "no-analysis", ProjectStanding.NotPresent)],
        });
        await workspace.Assess.RunCommand.ExecuteAsync(null);
        var review = workspace.Pages[(int)WorkspacePage.Review];
        Assert.False(review.HasBadge);

        Assert.Same(workspace.Changes, workspace.Assess.Compare.Changes);
        foreach (var word in workspace.Assess.Compare.Words) word.IsChecked = true;
        workspace.Assess.Compare.ProposeCommand.Execute(ChangeKinds.Reject);

        Assert.Equal(2, workspace.Changes.Items.Count);
        Assert.Equal("2", review.Badge);
        Assert.Equal("2 changes not applied yet", workspace.Changes.CountText);

        workspace.Changes.RemoveCommand.Execute(workspace.Changes.Items[0]);

        Assert.Equal("1", review.Badge);
    }

    [Theory]
    [InlineData(900, true)]
    [InlineData(HandoffWorkspaceViewModel.SidebarCollapseWidth - 1, true)]
    [InlineData(HandoffWorkspaceViewModel.SidebarCollapseWidth, false)]
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
    public async Task OpenRecentListsTheKnownProjectsOtherThanTheOpenOneAndOpensTheChosenOne()
    {
        var (fake, projectPicker, workspace) = NewWorkspace();
        const string other = @"C:\projects\two.fwdata";
        fake.KnownProjectsListIs(
        [
            new KnownProjectSummary(other, Saved),
            new KnownProjectSummary(ProjectPath, Saved),
        ]);
        await workspace.Project.LoadKnownProjectsAsync();
        await ChooseProjectAsync(fake, projectPicker, workspace);

        var recent = Assert.Single(workspace.RecentProjects);
        Assert.Equal(other, recent.FullFwDataPath);
        Assert.Equal("two", recent.Name);
        Assert.Equal("two", workspace.RecentProjectsText);

        await workspace.OpenRecentProjectCommand.ExecuteAsync(recent);

        Assert.Equal("two.fwdata", workspace.ProjectName);
        Assert.Equal(["one"], workspace.RecentProjects.Select(project => project.Name));
    }

    [Fact]
    public async Task ConfigureOpensTheSelectionEditorUnlessSomethingElseTakesTheHook()
    {
        var (fake, projectPicker, workspace) = NewWorkspace();
        await ChooseProjectAsync(fake, projectPicker, workspace);

        workspace.ConfigureCommand.Execute(null);

        Assert.Equal(WorkspacePage.Texts, workspace.CurrentPage);
        Assert.True(workspace.TextsPage.ShowTexts);

        var opened = 0;
        workspace.ShowPageCommand.Execute(WorkspacePage.Overview);
        workspace.OpenConfiguration = () => opened++;
        workspace.ConfigureCommand.Execute(null);

        Assert.Equal(1, opened);
        Assert.Equal(WorkspacePage.Overview, workspace.CurrentPage);
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
        Assert.Contains(" · one saved ", workspace.FreshnessDetail);
        Assert.True(workspace.RefreshCommand.CanExecute(null));
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
        fake.CurrentBaselineCompletesWith(new CurrentBaselineResponse(null, null, false) { ProjectLastWriteUtc = Saved });
        fake.ListTextsCompletesWith(new TextInventoryResponse([], HasBaseline: false));
        projectPicker.PathToReturn = ProjectPath;

        await workspace.Project.BrowseCommand.ExecuteAsync(null);

        Assert.Equal(ProjectFreshness.NoBaseline, workspace.Freshness);
        Assert.Equal("No Baseline yet", workspace.FreshnessLabel);
        Assert.True(workspace.RefreshCommand.CanExecute(null));
    }

    [Fact]
    public async Task RefreshCapturesABaselineThenAssessesTheSelectionAndSaysWhatChanged()
    {
        var (fake, projectPicker, workspace) = NewWorkspace();
        await ChooseProjectAsync(fake, projectPicker, workspace, Saved.AddHours(3));
        workspace.Selection.AllWordforms = true;
        fake.AssessCompletesWith(NewAssessResponse() with { Words = [Word("kitabu", "timed-out", ProjectStanding.Approved)] });
        await workspace.Assess.RunCommand.ExecuteAsync(null);
        workspace.ShowPageCommand.Execute(WorkspacePage.Overview);

        fake.CaptureBaselineCompletesWith(new BaselineCaptureResponse(Token, ProjectPath, Saved.AddHours(3), false, false));
        fake.AssessCompletesWith(NewAssessResponse(Saved.AddHours(3)) with
        {
            Words = [Word("kitabu", "analysed", ProjectStanding.Approved)],
        });
        await workspace.RefreshCommand.ExecuteAsync(null);

        Assert.Single(fake.CaptureBaselineRequests);
        Assert.Equal(2, fake.AssessRequests.Count);
        Assert.Equal(ProjectFreshness.Refreshed, workspace.Freshness);
        Assert.Equal("Refreshed", workspace.FreshnessLabel);
        Assert.False(workspace.RerunOffered);
        Assert.True(workspace.SeeWhatChangedCommand.CanExecute(null));

        workspace.SeeWhatChangedCommand.Execute(null);

        Assert.Equal(WorkspacePage.Texts, workspace.CurrentPage);
        Assert.True(workspace.TextsPage.ShowWhatChanged);
    }

    [Fact]
    public async Task WhileRefreshingTheLineSaysSoAndCancelStopsTheRun()
    {
        var (fake, projectPicker, workspace) = NewWorkspace();
        await ChooseProjectAsync(fake, projectPicker, workspace);
        workspace.Selection.AllWordforms = true;
        fake.CaptureBaselineCompletesWith(new BaselineCaptureResponse(Token, ProjectPath, Saved, false, false));
        fake.AssessBlocksUntilCancelled(new Refusal("assess.cancelled", FailureReason.Cancelled, "Cancelled."));

        var refreshing = workspace.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(ProjectFreshness.Refreshing, workspace.Freshness);
        Assert.Equal("Refreshing", workspace.FreshnessLabel);
        Assert.False(workspace.RerunOffered);
        Assert.True(workspace.CancelRefreshCommand.CanExecute(null));
        Assert.False(workspace.RefreshCommand.CanExecute(null));

        workspace.CancelRefreshCommand.Execute(null);
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
        Assert.Equal(ProjectFreshness.Current, workspace.Freshness);
    }

    [Fact]
    public async Task RefreshingTheBaselineChecksTheGrammarBecauseAPersonAskedForIt()
    {
        var (fake, projectPicker, workspace) = NewWorkspace();
        await ChooseProjectAsync(fake, projectPicker, workspace);
        Assert.Empty(fake.CheckGrammarRequests);

        fake.CaptureBaselineCompletesWith(new BaselineCaptureResponse(Token, ProjectPath, Saved, false, false));
        await workspace.Baseline.RefreshCommand.ExecuteAsync(null);

        Assert.Single(fake.CheckGrammarRequests);
        Assert.True(workspace.Grammar.HasChecked);
    }

    [Fact]
    public async Task OpeningAProjectWithNothingStoredNeverChecksTheGrammarAndOffersTheCheck()
    {
        var (fake, projectPicker, workspace) = NewWorkspace();

        await ChooseProjectAsync(fake, projectPicker, workspace);

        Assert.Single(fake.StoredGrammarCheckRequests);
        Assert.Empty(fake.CheckGrammarRequests);
        Assert.True(workspace.IsGrammarNotChecked);
        Assert.Equal("Not checked yet", workspace.Grammar.SummaryText);
        Assert.False(workspace.Pages[(int)WorkspacePage.Warnings].HasBadge);

        await workspace.CheckGrammarCommand.ExecuteAsync(null);

        Assert.Equal(ProjectPath, Assert.Single(fake.CheckGrammarRequests).ProjectPath);
        Assert.False(workspace.IsGrammarNotChecked);
        Assert.True(workspace.Grammar.CheckCommand.CanExecute(null));
    }

    [Fact]
    public async Task OpeningAProjectWithAStoredCheckShowsItAndLeavesReloadWorking()
    {
        var (fake, projectPicker, workspace) = NewWorkspace();
        var stored = new GrammarCheckResponse(
            [new GrammarWarning("warning", "Entry", [], [new GrammarWarningPart("dropped", "text")], "warning: dropped")],
            HasBaseline: true);
        fake.StoredGrammarCheckIs(stored);
        fake.CheckGrammarCompletesWith(stored);

        await ChooseProjectAsync(fake, projectPicker, workspace);

        Assert.False(workspace.IsGrammarNotChecked);
        Assert.Equal("1 finding", workspace.Grammar.SummaryText);
        Assert.True(workspace.Grammar.CheckCommand.CanExecute(null));
    }

    [Fact]
    public async Task ABaselineRefreshThatLeavesAnOlderAssessmentShowingIsNotCurrent()
    {
        var (fake, projectPicker, workspace) = NewWorkspace();
        await ChooseProjectAsync(fake, projectPicker, workspace);
        workspace.Selection.AllWordforms = true;
        fake.AssessCompletesWith(NewAssessResponse() with { Words = [Word("kitabu", "analysed", ProjectStanding.Approved)] });
        await workspace.Assess.RunCommand.ExecuteAsync(null);
        Assert.Equal(ProjectFreshness.Current, workspace.Freshness);

        var later = Saved.AddHours(3);
        fake.CaptureBaselineCompletesWith(new BaselineCaptureResponse(Token, ProjectPath, later, false, false));
        await workspace.Baseline.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(ProjectFreshness.SavedSince, workspace.Freshness);
        Assert.Contains("the numbers still describe", workspace.FreshnessDetail);
    }

    [Fact]
    public async Task ARefreshWhoseAssessmentIsCancelledStillSaysTheNumbersAreOlder()
    {
        var (fake, projectPicker, workspace) = NewWorkspace();
        await ChooseProjectAsync(fake, projectPicker, workspace);
        workspace.Selection.AllWordforms = true;
        fake.AssessCompletesWith(NewAssessResponse() with { Words = [Word("kitabu", "analysed", ProjectStanding.Approved)] });
        await workspace.Assess.RunCommand.ExecuteAsync(null);

        fake.CaptureBaselineCompletesWith(new BaselineCaptureResponse(Token, ProjectPath, Saved.AddHours(3), false, false));
        fake.AssessBlocksUntilCancelled(new Refusal("assess.cancelled", FailureReason.Cancelled, "Cancelled."));
        var refreshing = workspace.RefreshCommand.ExecuteAsync(null);
        workspace.CancelRefreshCommand.Execute(null);
        await refreshing;

        Assert.Equal(ProjectFreshness.SavedSince, workspace.Freshness);
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
    public async Task TryingAWordFromTimingOpensTheTryAWordPageAndTracesIt()
    {
        var (fake, projectPicker, workspace) = NewWorkspace();
        await ChooseProjectAsync(fake, projectPicker, workspace);
        workspace.ShowPageCommand.Execute(WorkspacePage.Timing);

        workspace.Statistics.TryWord!("kitabu");

        Assert.Equal(WorkspacePage.TryAWord, workspace.CurrentPage);
        Assert.Equal("kitabu", workspace.Assess.Trace.WordToTry);
    }

    [Fact]
    public void TheAiHandoffActionOffersARewriteOnlyOnceFilesExist()
    {
        var (_, _, workspace) = NewWorkspace();
        Assert.Equal("Write the AI Handoff", workspace.HandoffActionText);

        workspace.Handoff.Files.Add(new HandoffFileViewModel("handoff.md", @"C:\handoff\handoff.md"));
        workspace.Handoff.State = RunState.Completed;

        Assert.Equal("Write the AI Handoff again", workspace.HandoffActionText);
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
