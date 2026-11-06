using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using SIL.Motif.App;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Pins the rest of the Proposal bar: Esc leaves only when nothing nearer uses it, the view switch drives the
/// open Proposal's view, and the sentence says when it was tried and on which Baseline.
/// </summary>
[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class ProposalBarViewTests
{
    private const string ProposalId = "proposal/sound";

    [Fact]
    public void EscLeavesTheProposalWhenNothingElseHoldsIt()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await OpenedAsync(Trial(TrialFreshness.Current));
            try
            {
                PressEscape(window);

                Assert.Null(workspace.Context.Lens);
            }
            finally { window.Close(); }
        }, TimeSpan.FromMinutes(3));
    }

    [Fact]
    public void EscInATextBoxLeavesTheProposalOpen()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await OpenedAsync(Trial(TrialFreshness.Current));
            try
            {
                var box = new TextBox();
                window.FindInWindowOrWorkspace<Panel>("PageHost")!.Children.Add(box);
                PageScreenshots.Settle(window);
                Assert.True(box.Focus());

                PressEscape(window);

                Assert.NotNull(workspace.Context.Lens);
            }
            finally { window.Close(); }
        }, TimeSpan.FromMinutes(3));
    }

    [Fact]
    public void EscWithAMenuOpenLeavesTheProposalOpen()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await OpenedAsync(Trial(TrialFreshness.Current));
            try
            {
                window.FindInWindowOrWorkspace<MenuItem>("EditMenu")!.IsSubMenuOpen = true;
                PageScreenshots.Settle(window);

                PressEscape(window);

                Assert.NotNull(workspace.Context.Lens);
            }
            finally { window.Close(); }
        }, TimeSpan.FromMinutes(3));
    }

    [Fact]
    public void EscWithNoProposalOpenDoesNothing()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await OpenedAsync(Trial(TrialFreshness.Current));
            try
            {
                await workspace.ProposalBar.LeaveCommand.ExecuteAsync(null);

                PressEscape(window);

                Assert.Null(workspace.Context.Lens);
            }
            finally { window.Close(); }
        }, TimeSpan.FromMinutes(3));
    }

    [Fact]
    public void TheThreeViewsFormOneOutlinedSegmentedSwitch()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await OpenedAsync(Trial(TrialFreshness.Current));
            try
            {
                PageScreenshots.Settle(window);
                var list = Switch(window);
                var items = list.Items.OfType<ListBoxItem>().ToArray();
                Assert.Equal(3, items.Length);
                Assert.True(list.BorderThickness.Left > 0);
                Assert.All(items, item => Assert.Equal(new CornerRadius(0), item.CornerRadius));
                for (var index = 1; index < items.Length; index++)
                {
                    var previousRight = items[index - 1].TranslatePoint(new Point(items[index - 1].Bounds.Width, 0), list)!.Value.X;
                    var nextLeft = items[index].TranslatePoint(default, list)!.Value.X;
                    Assert.InRange(Math.Abs(nextLeft - previousRight), 0, 0.5);
                }
            }
            finally
            {
                window.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromMinutes(3));
    }

    [Fact]
    public void TheViewSwitchOffersThreeViewsAndDrivesTheLens()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await OpenedAsync(Trial(TrialFreshness.Current));
            try
            {
                var views = Switch(window);
                Assert.Equal(["Today", "Today → Proposal", "Proposal"], Choices(views).Select(item => TextOf(item)));
                Assert.Equal(1, views.SelectedIndex);

                foreach (var view in new[] { MarkupView.Proposal, MarkupView.Today, MarkupView.TodayToProposal })
                {
                    views.SelectedIndex = (int)view;
                    PageScreenshots.Settle(window);
                    Assert.Equal(view, workspace.Context.Lens!.View);
                    Assert.Equal(view, workspace.Context.EvidenceLens.View);
                }
            }
            finally { window.Close(); }
        }, TimeSpan.FromMinutes(3));
    }

    [Fact]
    public void TheViewSwitchFollowsTheLensAndItsNamesContainTheirVisibleText()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await OpenedAsync(Trial(TrialFreshness.Current));
            try
            {
                workspace.Context.ShowProposalView(MarkupView.Proposal);
                PageScreenshots.Settle(window);
                var views = Switch(window);
                Assert.Equal(2, views.SelectedIndex);
                foreach (var item in Choices(views))
                    Assert.StartsWith(TextOf(item), AutomationProperties.GetName(item));
            }
            finally { window.Close(); }
        }, TimeSpan.FromMinutes(3));
    }

    [Fact]
    public void TheViewTooltipNamesTheOverviewAndSaysOtherPagesShowToday()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await OpenedAsync(Trial(TrialFreshness.Current));
            try
            {
                Assert.Contains(MotifAppStrings.Overview, workspace.ProposalBar.ViewTooltip);
                Assert.Contains("today's", workspace.ProposalBar.ViewTooltip);
                Assert.Equal(workspace.ProposalBar.ViewTooltip, ToolTip.GetTip(Switch(window)));
            }
            finally { window.Close(); }
        }, TimeSpan.FromMinutes(3));
    }

    [Fact]
    public void TheSentenceSaysWhenItWasTriedOnWhichBaselineAndWhetherThatStillHolds()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await OpenedAsync(Trial(TrialFreshness.BaselineMoved));
            try
            {
                var clock = workspace.Context.Clock;
                var tried = WindowTimeText.Format(Completed, clock);
                var saved = WindowTimeText.Format(DateTimeOffset.Parse(PageScreenshots.Token().CapturedUtc,
                    CultureInfo.InvariantCulture), clock);

                Assert.Equal($"Tried {tried} on Baseline of {saved} · {MotifAppStrings.TrialBaselineMovedSentence}",
                    workspace.ProposalBar.Sentence);
            }
            finally { window.Close(); }
        }, TimeSpan.FromMinutes(3));
    }

    [Fact]
    public void ABaselineWithNoKnownSaveTimeReadsTruthfully()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var known = PageScreenshots.Token();
            var unknown = new BaselineToken(known.ProjectIdentity, known.SemanticSnapshotDigest, known.ProjectionVersion,
                "0001-01-01T00:00:00Z", known.BundleDigest);
            var trial = Trial(TrialFreshness.Current) with { Baseline = unknown };
            var (workspace, window) = await OpenedAsync(trial);
            try
            {
                var tried = WindowTimeText.Format(Completed, workspace.Context.Clock);

                Assert.Equal($"Tried {tried} on a Baseline with no recorded save time · {MotifAppStrings.TrialCurrentStatus}",
                    workspace.ProposalBar.Sentence);
                Assert.DoesNotContain("0001", workspace.ProposalBar.Sentence);
            }
            finally { window.Close(); }
        }, TimeSpan.FromMinutes(3));
    }

    [Fact]
    public void AnUntriedProposalHasNoTriedSentence()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await OpenedAsync(null);
            try
            {
                Assert.Equal(MotifAppStrings.TrialNotTriedSentence, workspace.ProposalBar.Sentence);
                Assert.DoesNotContain("Tried", workspace.ProposalBar.Sentence);
            }
            finally { window.Close(); }
        }, TimeSpan.FromMinutes(3));
    }

    [Fact]
    public void ATrialStillRunningHasNoTriedSentence()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await OpenedAsync(Trial(TrialFreshness.Incomplete) with { Status = JobStatus.Running });
            try
            {
                Assert.Equal(MotifAppStrings.TrialTrying, workspace.ProposalBar.Sentence);
            }
            finally { window.Close(); }
        }, TimeSpan.FromMinutes(3));
    }

    [Fact]
    public void TheBarStaysOneRowAt1280By720WithEverythingInsideIt()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await OpenedAsync(Trial(TrialFreshness.BaselineMoved), "A Proposal with a rather long name indeed");
            try
            {
                LayoutInvariantFixtures.Apply(window, new LayoutCell(1280, 720, 100));
                PageScreenshots.Settle(window);
                var bar = Assert.IsType<Border>(window.GetVisualDescendants().OfType<Control>().First(
                    control => AutomationProperties.GetAutomationId(control) == AutomationIds.ProposalBar));
                var barBox = Box(bar, window);
                Assert.False(bar.GetVisualDescendants().OfType<ProposalBarPanel>().Single().IsWrapped);
                foreach (var id in new[] { AutomationIds.ProposalBarView, AutomationIds.ProposalBarTryAgain, AutomationIds.ProposalBarLeave })
                {
                    var part = window.GetVisualDescendants().OfType<Control>().First(
                        control => AutomationProperties.GetAutomationId(control) == id);
                    var box = Box(part, window);
                    Assert.True(box.Top >= barBox.Top && box.Bottom <= barBox.Bottom, $"{id} spans {box.Top} to {box.Bottom}");
                    Assert.True(box.Right <= barBox.Right, $"{id} ends at {box.Right}, past the bar's {barBox.Right}");
                }
                Assert.All(bar.GetVisualDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible),
                    text => Assert.True(Box(text, window).Bottom <= barBox.Bottom, $"{text.Text} wraps below the bar"));
            }
            finally { window.Close(); }
        }, TimeSpan.FromMinutes(3));
    }

    private static readonly DateTimeOffset Completed = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    private static async Task<(WorkspaceShellViewModel Workspace, MainWindow Window)> OpenedAsync(
        TrialRecord? trial, string? label = null)
    {
        var fake = new FakeCommandClient();
        fake.ReadTrialCompletesWith(trial);
        var (workspace, window) = await ReviewScreenshots.OpenAsync(fake);
        await workspace.Context.OpenProposalAsync(ProposalId, CancellationToken.None, label);
        PageScreenshots.Settle(window);
        return (workspace, window);
    }

    private static void PressEscape(MainWindow window)
    {
        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        PageScreenshots.Settle(window);
    }

    private static ListBoxItem[] Choices(ListBox views) => [.. views.Items.OfType<ListBoxItem>()];

    private static string TextOf(ListBoxItem item) => Assert.IsType<TextBlock>(item.Content).Text!;

    private static ListBox Switch(MainWindow window) => Assert.IsType<ProposalViewSwitch>(window.GetVisualDescendants().OfType<Control>()
        .Single(control => AutomationProperties.GetAutomationId(control) == AutomationIds.ProposalBarView));

    private static Rect Box(Control control, Visual window) =>
        new Rect(control.Bounds.Size).TransformToAABB(control.TransformToVisual(window)!.Value);

    private static TrialRecord Trial(TrialFreshness freshness) =>
        new("job/trial", ProposalId, "sha256:intent", PageScreenshots.Token(), null,
            new Dictionary<string, string> { [AssessmentKinds.ParseTime] = "assessment/trial" },
            ["kitabu"], true, null, JobStatus.Completed, Completed, freshness)
        { Label = "Sound change" };
}
