using SIL.Motif.App;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Controls.Presenters;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views.Shell;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class FrameBandsTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ThePageKeepsItsMinimumHeightBesideTheNoticeAndTheApplyFooterAtTheSmallestWindowAndZoom(bool withProjectNotice)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            var (workspace, window) = FakeComposedWindow.Create(techDemoNotice: PageScreenshots.FirstRunNotice());
            try
            {
                window.Show();
                workspace.CurrentPage = WorkspacePage.Review;
                LayoutInvariantFixtures.Apply(window, new LayoutCell(640, 600, 150));
                if (withProjectNotice)
                {
                    workspace.Context.TextStyles.MissingFontNotices.Add(
                        new WritingSystemFontNotice("x-test", "The font for this writing system is not installed."));
                    LayoutInvariantEngineTests.Settle(window);
                    Assert.True(window.FindControl<Border>("NoticeBand")!.IsEffectivelyVisible, "A project notice shows.");
                }
                var minimum = (double)window.FindResource("Component.Shell.WorkingPageMinHeight")!;

                var notice = window.FindControl<Border>("TechDemoNotice")!;
                Assert.True(notice.IsEffectivelyVisible, "The first-run notice shows.");
                var inspector = window.FindControl<WorkspaceLayoutHost>("WorkspaceLayout")!
                    .FindControl<Border>("InspectorPane")!;
                Assert.False(inspector.IsVisible, "The Inspector is collapsed.");
                var page = window.FindControl<Panel>("PageHost")!;
                Assert.True(page.Bounds.Height >= minimum - 0.5,
                    $"The page is {page.Bounds.Height} tall; the minimum is {minimum}.");
                var buttons = notice.GetVisualDescendants().OfType<Button>().ToArray();
                Assert.All(buttons, button => Assert.True(button.IsEffectivelyVisible));
            }
            finally
            {
                window.Close();
            }

            return Task.CompletedTask;
        }, TimeSpan.FromSeconds(60));
    }

    // Each page's one primary action in its first-run state, by the name a person hears.
    public static TheoryData<WorkspacePage, int, int, string> PrimaryActionCases()
    {
        var data = new TheoryData<WorkspacePage, int, int, string>();
        foreach (var (page, action) in PrimaryActions)
            foreach (var (width, height) in new[] { (640, 600), (960, 780) })
                data.Add(page, width, height, action);
        return data;
    }

    // Proposals, Timing, Warnings and Parsimony offer only reading in that state, so they have no action here.
    private static readonly (WorkspacePage Page, string Action)[] PrimaryActions =
    [
        (WorkspacePage.Overview, "Select a new project"),
        (WorkspacePage.Texts, "Parse all words in the Selection"),
        (WorkspacePage.TryAWord, "Try the word"),
        (WorkspacePage.Review, MotifAppStrings.GoToAnalyzeTexts),
        (WorkspacePage.AiHandoff, "Write the AI Handoff folder"),
        (WorkspacePage.AiAssistants, "Turn on Advanced AI mode"),
    ];

    [Theory]
    [MemberData(nameof(PrimaryActionCases))]
    public void EveryPageShowsItsPrimaryActionWithoutScrollingBesideTheFirstRunNotice(
        WorkspacePage page, int width, int height, string action)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            var (workspace, window) = FakeComposedWindow.Create(techDemoNotice: PageScreenshots.FirstRunNotice());
            try
            {
                window.Show();
                workspace.CurrentPage = page;
                LayoutInvariantFixtures.Apply(window, new LayoutCell(width, height, 150));
                var minimum = (double)window.FindResource("Component.Shell.WorkingPageMinHeight")!;
                Assert.True(window.FindControl<Border>("TechDemoNotice")!.IsEffectivelyVisible, "The first-run notice shows.");
                var host = window.FindControl<Panel>("PageHost")!;
                Assert.True(host.Bounds.Height >= minimum - 0.5,
                    $"{page}: the page is {host.Bounds.Height} tall; the minimum is {minimum}.");
                var button = window.GetVisualDescendants().OfType<Button>().Where(candidate =>
                    AutomationProperties.GetName(candidate) == action || candidate.Content as string == action)
                    .FirstOrDefault(candidate => candidate.IsEffectivelyVisible);
                Assert.True(button is not null, $"{page}: '{action}' is not on screen.");
                AssertShownWithoutScrolling(button!, window, page);
            }
            finally
            {
                window.Close();
            }

            return Task.CompletedTask;
        }, TimeSpan.FromSeconds(60));
    }

    private static void AssertShownWithoutScrolling(Control control, Window window, WorkspacePage page)
    {
        var box = InWindow(control, window);
        Assert.True(box.Left >= -1 && box.Top >= -1 && box.Right <= window.ClientSize.Width + 1 &&
            box.Bottom <= window.ClientSize.Height + 1, $"{page}: {control.GetType().Name} is outside the window: {box}.");
        foreach (var scroller in control.GetVisualAncestors().OfType<ScrollViewer>())
        {
            var presenter = scroller.GetVisualDescendants().OfType<ScrollContentPresenter>().First();
            var view = InWindow(presenter, window);
            Assert.True(view.Top <= box.Top + 1 && view.Bottom >= box.Bottom - 1,
                $"{page}: the action needs scrolling inside a {scroller.Name ?? "scroll viewer"}: {box} against {view}.");
        }
    }

    private static Rect InWindow(Control control, Window window) =>
        new Rect(control.Bounds.Size).TransformToAABB(control.TransformToVisual(window)!.Value);

    [Fact]
    public void GotItAccessKeyDismissesAndRemembersTheFirstRunNotice()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var preferences = new MemoryUserPreferencesStore();
            var notice = new TechDemoNoticeViewModel(new TechDemoNoticePreferencesAdapter(preferences));
            var (workspace, window) = FakeComposedWindow.Create(techDemoNotice: notice, preferencesStore: preferences);
            try
            {
                window.Show();
                PageScreenshots.Settle(window);
                var band = window.FindControl<Border>("TechDemoNotice")!;
                Assert.True(notice.IsVisible && band.IsEffectivelyVisible);
                Assert.False(preferences.Current.HasSeenTechDemoNotice);
                Assert.True(window.FindControl<Button>("ProjectChip")!.Focus());
                window.KeyPress(Key.LeftAlt, RawInputModifiers.Alt, PhysicalKey.AltLeft, null);
                window.KeyPressQwerty(PhysicalKey.G, RawInputModifiers.Alt);
                window.KeyReleaseQwerty(PhysicalKey.G, RawInputModifiers.Alt);
                window.KeyRelease(Key.LeftAlt, RawInputModifiers.None, PhysicalKey.AltLeft, null);
                PageScreenshots.Settle(window);
                Assert.False(notice.IsVisible, "Alt+G must acknowledge the first-run notice.");
                Assert.False(band.IsEffectivelyVisible);
                Assert.True(preferences.Current.HasSeenTechDemoNotice);
            }
            finally
            {
                window.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromSeconds(60));
    }

    [Theory]
    [InlineData(620, 100)]
    [InlineData(639, 100)]
    [InlineData(640, 100)]
    [InlineData(640, 150)]
    [InlineData(940, 150)]
    [InlineData(960, 150)]
    [InlineData(1240, 100)]
    public void TheNoticeIsOneLineWithGotItBesideItsTextAtEveryWidth(int width, int zoom)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            var (workspace, window) = FakeComposedWindow.Create(techDemoNotice: PageScreenshots.FirstRunNotice());
            try
            {
                window.Show();
                workspace.CurrentPage = WorkspacePage.Overview;
                LayoutInvariantFixtures.Apply(window, new LayoutCell(width, 600, zoom));
                var actions = window.FindControl<StackPanel>("TechDemoNoticeActions")!;
                var text = window.FindControl<Border>("TechDemoNotice")!.GetVisualDescendants()
                    .OfType<SIL.Motif.App.Views.CopyableTextBlock>().First();
                Assert.True(actions.IsEffectivelyVisible && text.IsEffectivelyVisible);
                var buttons = actions.Children.OfType<Button>().ToArray();
                var gotIt = Assert.Single(buttons);
                Assert.Equal("Got It", Assert.IsType<string>(gotIt.Content).Replace("_", "", StringComparison.Ordinal));
                Assert.Single(text.TextLayout.TextLines);
                Assert.Equal(TechDemoNoticeViewModel.NoticeText, text.Text);
                Assert.Equal(TechDemoNoticeViewModel.NoticeText, AutomationProperties.GetName(text));
                Assert.Equal(1, Grid.GetColumn(actions));
                Assert.Equal(0, Grid.GetRow(actions));
                var textBox = InWindow(text, window);
                var actionBox = InWindow(actions, window);
                Assert.False(textBox.Intersects(actionBox), $"The notice's text {textBox} overlaps its actions {actionBox}.");
                var gap = (double)window.FindResource("Component.Notice.ActionGap")!;
                Assert.True(actionBox.Left - textBox.Right >= gap * zoom / 100d - 1, "Side by side, a gap separates them.");
            }
            finally
            {
                window.Close();
            }

            return Task.CompletedTask;
        }, TimeSpan.FromSeconds(60));
    }
}
