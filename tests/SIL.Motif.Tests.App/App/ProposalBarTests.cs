using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.VisualTree;
using SIL.Motif.App;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Commands;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Pins the bar across the window while a Proposal is open: it names the Proposal on every page, says where its
/// Trial stands, leaves Proposal mode, and costs the first view nothing when no Proposal is open.
/// </summary>
[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class ProposalBarTests
{
    private const string ProposalId = "proposal/sound";

    [Fact]
    public void TheBarIsAbsentUntilAProposalIsOpenAndNamesItThenOnEveryPage()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var fake = new FakeCommandClient();
            fake.ReadTrialCompletesWith(Trial(JobStatus.Completed, TrialFreshness.Current));
            var (workspace, window) = await ReviewScreenshots.OpenAsync(fake);
            try
            {
                PageScreenshots.Settle(window);
                Assert.Null(VisibleBar(window));

                await workspace.Context.OpenProposalAsync(ProposalId, CancellationToken.None);
                foreach (var page in new[] { WorkspacePage.Overview, WorkspacePage.Texts, WorkspacePage.Timing,
                             WorkspacePage.Warnings, WorkspacePage.Proposals })
                {
                    workspace.CurrentPage = page;
                    PageScreenshots.Settle(window);
                    var bar = Assert.IsType<Border>(VisibleBar(window));
                    Assert.True(Top(bar, window) < Top(PageHost(window), window), $"{page}: the bar sits above the page");
                    Assert.Contains("Sound change", Text(bar));
                    Assert.Contains(MotifAppStrings.TrialCurrentStatus, Text(bar));
                }
            }
            finally { window.Close(); }
        }, TimeSpan.FromMinutes(3));
    }

    [Fact]
    public void LeaveReturnsToTodayAndTheBarGoesAway()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var fake = new FakeCommandClient();
            fake.ReadTrialCompletesWith(Trial(JobStatus.Completed, TrialFreshness.Current));
            var (workspace, window) = await ReviewScreenshots.OpenAsync(fake);
            try
            {
                await workspace.Context.OpenProposalAsync(ProposalId, CancellationToken.None);
                PageScreenshots.Settle(window);
                var leave = Assert.Single(window.GetVisualDescendants().OfType<Button>(),
                    button => AutomationProperties.GetAutomationId(button) == AutomationIds.ProposalBarLeave);
                Assert.Equal(MotifAppStrings.ProposalBarLeave, leave.Content);

                await workspace.ProposalBar.LeaveCommand.ExecuteAsync(null);
                PageScreenshots.Settle(window);

                Assert.Null(workspace.Context.Lens);
                Assert.False(workspace.Context.EvidenceLens.InProposalMode);
                Assert.Null(VisibleBar(window));
            }
            finally { window.Close(); }
        }, TimeSpan.FromMinutes(3));
    }

    [Fact]
    public void AProposalWithNoTrialSaysSoAndOffersToTryIt()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var fake = new FakeCommandClient();
            fake.ReadTrialCompletesWith(null);
            var (workspace, window) = await ReviewScreenshots.OpenAsync(fake);
            try
            {
                await workspace.Context.OpenProposalAsync(ProposalId, CancellationToken.None);
                PageScreenshots.Settle(window);

                var bar = workspace.ProposalBar;
                Assert.Equal(MotifAppStrings.TrialNotTriedSentence, bar.Sentence);
                Assert.Equal(MotifAppStrings.TryIt, bar.TryAgainText);
                Assert.True(bar.TryAgainCommand.CanExecute(null));
                Assert.NotNull(VisibleBar(window));
            }
            finally { window.Close(); }
        }, TimeSpan.FromMinutes(3));
    }

    [Fact]
    public void AnUntriedProposalIsNamedByTheLabelPassedAtOpen()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var fake = new FakeCommandClient();
            fake.ReadTrialCompletesWith(null);
            var (workspace, window) = await ReviewScreenshots.OpenAsync(fake);
            try
            {
                await workspace.Context.OpenProposalAsync(ProposalId, CancellationToken.None, "Sound change");
                PageScreenshots.Settle(window);

                Assert.Equal("Proposal: Sound change", workspace.ProposalBar.Name);
                Assert.Contains("Sound change", Text(Assert.IsType<Border>(VisibleBar(window))));
            }
            finally { window.Close(); }
        }, TimeSpan.FromMinutes(3));
    }

    [Fact]
    public void TheButtonsAccessibleNamesContainTheirVisibleText()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var fake = new FakeCommandClient();
            fake.ReadTrialCompletesWith(null);
            var (workspace, window) = await ReviewScreenshots.OpenAsync(fake);
            try
            {
                await workspace.Context.OpenProposalAsync(ProposalId, CancellationToken.None, "Sound change");
                PageScreenshots.Settle(window);

                foreach (var id in new[] { AutomationIds.ProposalBarLeave, AutomationIds.ProposalBarTryAgain })
                {
                    var button = Assert.Single(window.GetVisualDescendants().OfType<Button>(),
                        candidate => AutomationProperties.GetAutomationId(candidate) == id);
                    var visible = button.Content as string ?? Assert.IsType<TextBlock>(button.Content).Text!;
                    Assert.Contains(visible, AutomationProperties.GetName(button));
                }
            }
            finally { window.Close(); }
        }, TimeSpan.FromMinutes(3));
    }

    [Fact]
    public void ATryAgainTheQueueRefusesShowsItsSentenceOnTheBar()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var fake = new FakeCommandClient();
            fake.ReadTrialCompletesWith(Trial(JobStatus.Completed, TrialFreshness.Current));
            var (workspace, window) = await ReviewScreenshots.OpenAsync(fake);
            try
            {
                await workspace.Context.OpenProposalAsync(ProposalId, CancellationToken.None);
                var refusal = new Refusal("trial.queue-busy", FailureReason.Refused, "The Trial could not be queued.");
                fake.EnqueueTrialRefusesWith(refusal);

                await workspace.ProposalBar.TryAgainCommand.ExecuteAsync(null);

                Assert.Equal(WindowRefusal.From(refusal).Sentence, workspace.ProposalBar.Sentence);
            }
            finally { window.Close(); }
        }, TimeSpan.FromMinutes(3));
    }

    [Fact]
    public void ARefusalTheTrialRunKeepsNoStateForIsRememberedByTheBar()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var fake = new FakeCommandClient();
            fake.ReadTrialCompletesWith(Trial(JobStatus.Completed, TrialFreshness.Current));
            var (workspace, window) = await ReviewScreenshots.OpenAsync(fake);
            try
            {
                await workspace.Context.OpenProposalAsync(ProposalId, CancellationToken.None);
                var refusal = new Refusal("trial.queue-busy", FailureReason.Refused, "The Trial could not be queued.");
                var queued = new TaskCompletionSource<CommandOutcome<TrialEnqueued>>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                fake.OnEnqueueTrial((_, _) => queued.Task);
                var trying = workspace.ProposalBar.TryAgainCommand.ExecuteAsync(null);
                workspace.Context.TrialRun.CancelAll();
                queued.SetResult(CommandOutcome<TrialEnqueued>.Refused(refusal));
                await trying;

                Assert.Null(workspace.Context.TrialRun.StateFor(ProposalId).Refusal);
                Assert.Equal(WindowRefusal.From(refusal).Sentence, workspace.ProposalBar.Sentence);
            }
            finally { window.Close(); }
        }, TimeSpan.FromMinutes(3));
    }

    [Fact]
    public void ARefusalForAProposalThatIsNoLongerOpenIsNotShownOnTheNewOne()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var fake = new FakeCommandClient();
            fake.ReadTrialCompletesWith(Trial(JobStatus.Completed, TrialFreshness.Current));
            var (workspace, window) = await ReviewScreenshots.OpenAsync(fake);
            try
            {
                await workspace.Context.OpenProposalAsync(ProposalId, CancellationToken.None);
                var refusal = new Refusal("trial.queue-busy", FailureReason.Refused, "The Trial could not be queued.");
                var queued = new TaskCompletionSource<CommandOutcome<TrialEnqueued>>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                fake.OnEnqueueTrial((_, _) => queued.Task);
                var trying = workspace.ProposalBar.TryAgainCommand.ExecuteAsync(null);
                workspace.Context.TrialRun.CancelAll();
                await workspace.Context.OpenProposalAsync("proposal/other", CancellationToken.None, "Other");
                queued.SetResult(CommandOutcome<TrialEnqueued>.Refused(refusal));
                await trying;

                Assert.Equal("proposal/other", workspace.Context.Lens?.ProposalId);
                Assert.NotEqual(WindowRefusal.From(refusal).Sentence, workspace.ProposalBar.Sentence);
            }
            finally { window.Close(); }
        }, TimeSpan.FromMinutes(3));
    }

    [Theory]
    [InlineData("", "Sound change")]
    [InlineData("  ", "Sound change")]
    public void AnEmptyTrialLabelFallsThroughToTheLabelPassedAtOpen(string trialLabel, string expected)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var fake = new FakeCommandClient();
            fake.ReadTrialCompletesWith(Trial(JobStatus.Completed, TrialFreshness.Current) with { Label = trialLabel });
            var (workspace, window) = await ReviewScreenshots.OpenAsync(fake);
            try
            {
                await workspace.Context.OpenProposalAsync(ProposalId, CancellationToken.None, "Sound change");

                Assert.Equal("Proposal: " + expected, workspace.ProposalBar.Name);
            }
            finally { window.Close(); }
        }, TimeSpan.FromMinutes(3));
    }

    [Fact]
    public void AnEmptyLabelEverywhereNamesTheBarJustProposal()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var fake = new FakeCommandClient();
            fake.ReadTrialCompletesWith(null);
            var (workspace, window) = await ReviewScreenshots.OpenAsync(fake);
            try
            {
                await workspace.Context.OpenProposalAsync(ProposalId, CancellationToken.None, "");

                Assert.Equal(MotifAppStrings.Proposal, workspace.ProposalBar.Name);
            }
            finally { window.Close(); }
        }, TimeSpan.FromMinutes(3));
    }

    [Fact]
    public void LeavingMovesFocusToThePageRailOnceTheBarHasGone()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var fake = new FakeCommandClient();
            fake.ReadTrialCompletesWith(Trial(JobStatus.Completed, TrialFreshness.Current));
            var (workspace, window) = await ReviewScreenshots.OpenAsync(fake);
            try
            {
                await workspace.Context.OpenProposalAsync(ProposalId, CancellationToken.None);
                PageScreenshots.Settle(window);
                var leave = Assert.Single(window.GetVisualDescendants().OfType<Button>(),
                    button => AutomationProperties.GetAutomationId(button) == AutomationIds.ProposalBarLeave);
                leave.Focus();

                await workspace.ProposalBar.LeaveCommand.ExecuteAsync(null);
                PageScreenshots.Settle(window);

                Assert.Null(VisibleBar(window));
                var focused = Assert.IsAssignableFrom<Visual>(window.FocusManager?.GetFocusedElement());
                Assert.Contains(focused.GetSelfAndVisualAncestors(), ancestor => ancestor is ListBox { Name: "PageList" });
            }
            finally { window.Close(); }
        }, TimeSpan.FromMinutes(3));
    }

    [Theory]
    [InlineData(TrialFreshness.BaselineMoved)]
    [InlineData(TrialFreshness.ProposalChanged)]
    public void AStaleTrialSaysItNeedsAnotherTry(TrialFreshness freshness)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var fake = new FakeCommandClient();
            fake.ReadTrialCompletesWith(Trial(JobStatus.Completed, freshness));
            var (workspace, window) = await ReviewScreenshots.OpenAsync(fake);
            try
            {
                await workspace.Context.OpenProposalAsync(ProposalId, CancellationToken.None);

                var expected = freshness == TrialFreshness.BaselineMoved
                    ? MotifAppStrings.TrialBaselineMovedSentence : MotifAppStrings.TrialProposalChangedSentence;
                Assert.EndsWith(" · " + expected, workspace.ProposalBar.Sentence);
                Assert.Equal(MotifAppStrings.TryAgain, workspace.ProposalBar.TryAgainText);
            }
            finally { window.Close(); }
        }, TimeSpan.FromMinutes(3));
    }

    [Fact]
    public void ATrialThatCannotBeReadSaysSoInsteadOfNotTried()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var fake = new FakeCommandClient();
            fake.ReadTrialRefusesWith(new Refusal("trial.scratch-gone", FailureReason.NotFound, "The Trial scratch is gone."));
            var (workspace, window) = await ReviewScreenshots.OpenAsync(fake);
            try
            {
                await workspace.Context.OpenProposalAsync(ProposalId, CancellationToken.None);

                Assert.NotEqual(MotifAppStrings.TrialNotTriedSentence, workspace.ProposalBar.Sentence);
                Assert.NotEmpty(workspace.ProposalBar.Sentence);
            }
            finally { window.Close(); }
        }, TimeSpan.FromMinutes(3));
    }

    [Fact]
    public void ARunningTrialCannotBeStartedAgainFromTheBar()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var fake = new FakeCommandClient();
            fake.ReadTrialCompletesWith(Trial(JobStatus.Running, TrialFreshness.Incomplete, withAssessment: false));
            var (workspace, window) = await ReviewScreenshots.OpenAsync(fake);
            try
            {
                await workspace.Context.OpenProposalAsync(ProposalId, CancellationToken.None);

                Assert.Equal(MotifAppStrings.TrialTrying, workspace.ProposalBar.Sentence);
                Assert.False(workspace.ProposalBar.TryAgainCommand.CanExecute(null));
            }
            finally { window.Close(); }
        }, TimeSpan.FromMinutes(3));
    }

    [Fact]
    public void TryAgainStaysInsideTheInspectorBodyAt720WithTheBarShowing()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var fake = new FakeCommandClient();
            fake.PendingChangesIs(ReviewScreenshots.Blocked());
            fake.ReadTrialCompletesWith(Trial(JobStatus.Completed, TrialFreshness.Current));
            var (workspace, window) = await ReviewScreenshots.OpenAsync(fake);
            try
            {
                await workspace.Context.OpenProposalAsync(ProposalId, CancellationToken.None);
                workspace.CurrentPage = WorkspacePage.Review;
                LayoutInvariantFixtures.Apply(window, new LayoutCell(1280, 720, 100));
                PageScreenshots.Settle(window);
                Assert.NotNull(VisibleBar(window));
                var page = workspace.PageModel<ReviewPageModel>();
                var noLongerFits = page.ReviewGroups.SelectMany(group => group.Items).Single(change => change.Word == "kitabu");
                page.SelectEntry(Assert.Single(page.Entries, entry => ReferenceEquals(entry.Change, noLongerFits)));
                PageScreenshots.Settle(window);

                var body = Assert.Single(window.GetVisualDescendants().OfType<ReviewInspector>())
                    .GetVisualDescendants().OfType<ScrollViewer>().First();
                var viewport = Box(body, window);
                var tryAgain = Assert.Single(body.GetVisualDescendants().OfType<Button>(),
                    candidate => AutomationProperties.GetName(candidate) == "Try loading word details again");
                var box = Box(tryAgain, window);
                Assert.True(box.Top >= viewport.Top && box.Bottom <= viewport.Bottom,
                    $"Try again spans {box.Top:0.#} to {box.Bottom:0.#}, outside the body's {viewport.Top:0.#} to {viewport.Bottom:0.#}.");
            }
            finally { window.Close(); }
        }, TimeSpan.FromMinutes(3));
    }

    private static TrialRecord Trial(JobStatus status, TrialFreshness freshness, bool withAssessment = true) =>
        new("job/trial", ProposalId, "sha256:intent", PageScreenshots.Token(), null,
            withAssessment
                ? new Dictionary<string, string> { [AssessmentKinds.ParseTime] = "assessment/trial" }
                : new Dictionary<string, string>(),
            ["kitabu"], true, null, status, new DateTimeOffset(2026, 10, 9, 12, 0, 0, TimeSpan.Zero), freshness)
        { Label = "Sound change" };

    private static Control? VisibleBar(Window window) => window.GetVisualDescendants().OfType<Control>().FirstOrDefault(
        control => control.IsEffectivelyVisible && AutomationProperties.GetAutomationId(control) == AutomationIds.ProposalBar);

    private static Control PageHost(Window window) => Assert.Single(window.GetVisualDescendants().OfType<MotifWorkspaceView>());

    private static string Text(Control bar) => string.Join(" ",
        bar.GetVisualDescendants().OfType<TextBlock>().Select(block => block.Text));

    private static double Top(Control control, Visual window) => Box(control, window).Top;

    private static Rect Box(Control control, Visual window) =>
        new Rect(control.Bounds.Size).TransformToAABB(control.TransformToVisual(window)!.Value);
}
