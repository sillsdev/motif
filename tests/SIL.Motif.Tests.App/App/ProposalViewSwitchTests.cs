using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using SIL.Motif.App;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Commands;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Pins that choosing a view on the Proposal bar is not an evidence change, that the switch shows only on pages that
/// follow it, that it survives a Ctrl-click and keeps a visible focus, and that the bar wraps instead of losing a button.
/// </summary>
[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class ProposalViewSwitchTests
{
    private const string ProposalId = "proposal/sound";

    [Fact]
    public async Task AFlipAnnouncesOnlyTheViewAndLeavesTheEvidenceLensTheSameObject()
    {
        var (_, context, _) = await OverviewProposalViewTests.OpenedAsync(OverviewProposalViewTests.Trial());
        _ = new TextsPageModel(context);
        await context.OpenProposalAsync("proposal/one", CancellationToken.None);
        var lens = context.EvidenceLens;
        var assessment = context.Changes.AssessmentId;
        var announced = new List<string?>();
        context.PropertyChanged += (_, args) => announced.Add(args.PropertyName);

        foreach (var view in new[] { MarkupView.Proposal, MarkupView.Today, MarkupView.TodayToProposal })
            context.ShowProposalView(view);

        Assert.Equal([nameof(WorkspaceContext.ProposalView), nameof(WorkspaceContext.ProposalView),
            nameof(WorkspaceContext.ProposalView)], announced);
        Assert.Same(lens, context.EvidenceLens);
        Assert.Equal(MarkupView.TodayToProposal, context.EvidenceLens.View);
        Assert.Equal(assessment, context.Changes.AssessmentId);
    }

    [Fact]
    public async Task ChoosingTheViewAlreadyShownAnnouncesNothing()
    {
        var (_, context, _) = await OverviewProposalViewTests.OpenedAsync(OverviewProposalViewTests.Trial());
        await context.OpenProposalAsync("proposal/one", CancellationToken.None);
        var announced = new List<string?>();
        context.PropertyChanged += (_, args) => announced.Add(args.PropertyName);

        context.ShowProposalView(MarkupView.TodayToProposal);

        Assert.Empty(announced);
    }

    [Fact]
    public void AFlipKeepsATryAgainRefusalOnTheBar()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var fake = new FakeCommandClient();
            fake.ReadTrialCompletesWith(Trial());
            var (workspace, window) = await ReviewScreenshots.OpenAsync(fake);
            try
            {
                await workspace.Context.OpenProposalAsync(ProposalId, CancellationToken.None);
                var refusal = new Refusal("trial.queue-busy", FailureReason.Refused, "The Trial could not be queued.");
                fake.EnqueueTrialRefusesWith(refusal);
                await workspace.ProposalBar.TryAgainCommand.ExecuteAsync(null);
                var sentence = workspace.ProposalBar.Sentence;
                Assert.Equal(WindowRefusal.From(refusal).Sentence, sentence);

                workspace.Context.ShowProposalView(MarkupView.Proposal);
                workspace.Context.ShowProposalView(MarkupView.Today);

                Assert.Equal(sentence, workspace.ProposalBar.Sentence);
            }
            finally { window.Close(); }
        }, TimeSpan.FromMinutes(3));
    }

    [Fact]
    public void TheSwitchShowsOnlyOnPagesThatFollowItAndTheChoiceComesBackOnTheOverview()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await OpenedAsync();
            try
            {
                workspace.Context.ShowProposalView(MarkupView.Proposal);
                PageScreenshots.Settle(window);
                Assert.True(Switch(window).IsEffectivelyVisible);

                foreach (var page in new[] { WorkspacePage.Texts, WorkspacePage.Timing, WorkspacePage.Warnings, WorkspacePage.Review })
                {
                    workspace.Context.OpenPage(page);
                    PageScreenshots.Settle(window);
                    var follows = ProposalViewScope.Pages.Contains(page);
                    Assert.True(follows == Switch(window).IsEffectivelyVisible, $"{page}: the switch shows iff the page follows it");
                    Assert.Equal(follows, workspace.ProposalBar.ShowsViewSwitch);
                    Assert.Equal(MarkupView.Proposal, workspace.Context.Lens!.View);
                }

                workspace.Context.OpenPage(WorkspacePage.Overview);
                PageScreenshots.Settle(window);
                Assert.True(Switch(window).IsEffectivelyVisible);
                Assert.Equal((int)MarkupView.Proposal, Switch(window).SelectedIndex);
            }
            finally { window.Close(); }
        }, TimeSpan.FromMinutes(3));
    }

    [Fact]
    public void ACtrlClickOnTheChosenViewDoesNotClearIt()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await OpenedAsync();
            try
            {
                var views = Switch(window);
                var chosen = views.Items.OfType<ListBoxItem>().ElementAt(views.SelectedIndex);
                var centre = chosen.TranslatePoint(new Point(chosen.Bounds.Width / 2, chosen.Bounds.Height / 2), window)!.Value;

                window.MouseDown(centre, MouseButton.Left, RawInputModifiers.Control);
                window.MouseUp(centre, MouseButton.Left, RawInputModifiers.Control);
                PageScreenshots.Settle(window);

                Assert.Equal((int)MarkupView.TodayToProposal, views.SelectedIndex);
                Assert.Equal(MarkupView.TodayToProposal, workspace.Context.Lens!.View);
            }
            finally { window.Close(); }
        }, TimeSpan.FromMinutes(3));
    }

    [Fact]
    public void ArrowKeysMoveTheChoiceWithTheFocusSoFocusNeverSitsOnAnUnchosenView()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await OpenedAsync();
            try
            {
                var views = Switch(window);
                var chosen = views.Items.OfType<ListBoxItem>().ElementAt(views.SelectedIndex);
                Assert.True(chosen.Focus(NavigationMethod.Tab));

                window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
                window.KeyRelease(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
                PageScreenshots.Settle(window);

                var focused = Assert.IsType<ListBoxItem>(window.FocusManager!.GetFocusedElement());
                Assert.True(focused.IsSelected);
                Assert.Equal(MarkupView.Proposal, workspace.Context.Lens!.View);
            }
            finally { window.Close(); }
        }, TimeSpan.FromMinutes(3));
    }

    [Theory]
    [InlineData(640, 100)]
    [InlineData(1280, 150)]
    [InlineData(640, 150)]
    public void NarrowWindowsKeepTheButtonsInsideAndNeverDropLeaveProposalOrTryAgain(int width, int zoom)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (_, window) = await OpenedAsync("A Proposal with a rather long name indeed");
            try
            {
                LayoutInvariantFixtures.Apply(window, new LayoutCell(width, 720, zoom));
                PageScreenshots.Settle(window);
                var bar = Bar(window);
                var panel = bar.GetVisualDescendants().OfType<ProposalBarPanel>().Single();
                foreach (var id in new[] { AutomationIds.ProposalBarTryAgain, AutomationIds.ProposalBarLeave, AutomationIds.ProposalBarView })
                {
                    var part = Part(window, id);
                    var box = Box(part, window);
                    Assert.True(part.IsEffectivelyVisible, $"{id} is hidden");
                    Assert.True(box.Left >= 0 && box.Right <= window.Bounds.Width, $"{id} spans {box.Left} to {box.Right}");
                    Assert.True(box.Bottom <= Box(bar, window).Bottom, $"{id} ends below the bar");
                }
                Assert.Equal(width == 640, panel.IsWrapped);
            }
            finally { window.Close(); }
        }, TimeSpan.FromMinutes(3));
    }

    [Theory]
    [InlineData(640, true)]
    [InlineData(1280, false)]
    public void TabVisitsTheBarInReadingOrderWhetherOrNotItWraps(int width, bool wrapped)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (_, window) = await OpenedAsync("A Proposal with a rather long name indeed");
            try
            {
                LayoutInvariantFixtures.Apply(window, new LayoutCell(width, 720, 150));
                PageScreenshots.Settle(window);
                var bar = Bar(window);
                var panel = bar.GetVisualDescendants().OfType<ProposalBarPanel>().Single();
                Assert.Equal(wrapped, panel.IsWrapped);
                window.Focus();

                // A stop is the panel child that holds the focus, so the view switch counts once however many items it has.
                var stops = new List<Control>();
                for (var press = 0; press < 80; press++)
                {
                    window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
                    window.KeyRelease(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
                    var child = window.FocusManager!.GetFocusedElement() is Visual focused
                        ? focused.GetSelfAndVisualAncestors().OfType<Control>().FirstOrDefault(c => c.GetVisualParent() == panel)
                        : null;
                    if (child is null) continue;
                    if (stops.Contains(child)) break;
                    stops.Add(child);
                }

                Assert.Contains(stops, stop => AutomationProperties.GetAutomationId(stop) == AutomationIds.ProposalBarView);
                var inReadingOrder = stops.OrderBy(stop => Box(stop, window).Center.Y > Box(bar, window).Center.Y)
                    .ThenBy(stop => Box(stop, window).Left).ToArray();
                Assert.Equal(inReadingOrder.Select(Describe), stops.Select(Describe));
            }
            finally { window.Close(); }
        }, TimeSpan.FromMinutes(3));
    }

    [Fact]
    public void TheNameTrimsBeforeTheButtonsAndKeepsAToolTipOfItsFullText()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await OpenedAsync("A Proposal with a rather long name indeed, long enough to trim");
            try
            {
                LayoutInvariantFixtures.Apply(window, new LayoutCell(640, 720, 150));
                PageScreenshots.Settle(window);
                var name = Bar(window).GetVisualDescendants().OfType<TextBlock>().Single(text => text.Classes.Contains("proposalBarName"));

                Assert.Equal(workspace.ProposalBar.Name, ToolTip.GetTip(name));
                Assert.Equal(TextTrimming.CharacterEllipsis, name.TextTrimming);
            }
            finally { window.Close(); }
        }, TimeSpan.FromMinutes(3));
    }

    [Fact]
    public void TheSentenceAndNameToolTipsCarryTheirFullText()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await OpenedAsync();
            try
            {
                var texts = Bar(window).GetVisualDescendants().OfType<TextBlock>().ToArray();
                var sentence = texts.Single(text => text.Text == workspace.ProposalBar.Sentence);

                Assert.Equal(workspace.ProposalBar.Sentence, ToolTip.GetTip(sentence));
            }
            finally { window.Close(); }
        }, TimeSpan.FromMinutes(3));
    }

    [Fact]
    public void TheProposalViewReplacesTodaysBreakdownWithOneLineOnEachTile()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await OpenedAsync();
            try
            {
                // This project has no Assessment, so the Today view keeps its no-result page and the others show tiles.
                foreach (var (view, lines, tiles) in new[]
                    { (MarkupView.Today, 0, false), (MarkupView.TodayToProposal, 0, true), (MarkupView.Proposal, 4, true) })
                {
                    workspace.Context.ShowProposalView(view);
                    PageScreenshots.Settle(window);
                    var shown = window.GetVisualDescendants().OfType<TextBlock>().Count(text =>
                        text.IsEffectivelyVisible && text.Text == MotifAppStrings.OverviewTodayBreakdownNote);
                    var rows = window.GetVisualDescendants().OfType<ItemsControl>().Any(
                        list => list.IsEffectivelyVisible && list.Name == "OverviewCoverageRows");
                    Assert.True(lines == shown, $"{view} shows {shown} breakdown lines, expected {lines}");
                    Assert.True(rows == (view == MarkupView.TodayToProposal), $"{view}: today's rows visible is {rows}");
                    Assert.Equal(tiles, workspace.PageModel<OverviewPageModel>().ShowTiles);
                }
            }
            finally { window.Close(); }
        }, TimeSpan.FromMinutes(3));
    }

    private static async Task<(WorkspaceShellViewModel Workspace, MainWindow Window)> OpenedAsync(string? label = null)
    {
        var fake = new FakeCommandClient();
        fake.ReadTrialCompletesWith(Trial());
        fake.OnOverview((request, _) => Task.FromResult(CommandOutcome<OverviewResponse>.Success(
            request.ProposalId is null ? OverviewProposalViewTests.Today() : OverviewProposalViewTests.Proposal())));
        var (workspace, window) = await ReviewScreenshots.OpenAsync(fake);
        await workspace.Context.OpenProposalAsync(ProposalId, CancellationToken.None, label);
        PageScreenshots.Settle(window);
        return (workspace, window);
    }

    private static string Describe(Control stop) =>
        AutomationProperties.GetAutomationId(stop) is { Length: > 0 } id ? id : ProposalBarPanel.GetPart(stop).ToString();

    private static ListBox Switch(MainWindow window) => Assert.IsType<ProposalViewSwitch>(Part(window, AutomationIds.ProposalBarView));

    private static Border Bar(MainWindow window) => Assert.IsType<Border>(Part(window, AutomationIds.ProposalBar));

    private static Control Part(MainWindow window, string id) => window.GetVisualDescendants().OfType<Control>()
        .First(control => AutomationProperties.GetAutomationId(control) == id);

    private static Rect Box(Control control, Visual window) =>
        new Rect(control.Bounds.Size).TransformToAABB(control.TransformToVisual(window)!.Value);

    private static TrialRecord Trial() =>
        new("job/trial", ProposalId, "sha256:intent", PageScreenshots.Token(), null,
            new Dictionary<string, string> { [AssessmentKinds.ParseTime] = "assessment/trial" },
            ["kitabu"], true, null, JobStatus.Completed, new DateTimeOffset(2026, 10, 9, 12, 0, 0, TimeSpan.Zero),
            TrialFreshness.BaselineMoved)
        { Label = "Sound change" };
}
