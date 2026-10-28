using Avalonia.Automation;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using SIL.Motif.App;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.App.Views.Shell;
using SIL.Motif.Tests.App.Walkthrough;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class FooterHeightBudgetTests(AvaloniaHeadlessFixture avalonia)
{
    private const double FooterHeightCap = 240;

    public static TheoryData<int, int, string> MinimumWindowCases => new()
    {
        { 600, 100, "light" },
        { 600, 100, "dark" },
        { 600, 150, "light" },
        { 600, 150, "dark" },
        { 480, 100, "light" },
        { 480, 100, "dark" },
        { 480, 150, "light" },
        { 480, 150, "dark" },
    };

    [Theory]
    [MemberData(nameof(MinimumWindowCases))]
    public void LongFooterLeavesThePageUsableAndAnOverlongFooterStillScrolls(int height, int zoom, string theme)
    {
        avalonia.Invoke(() =>
        {
            var application = Application.Current!;
            var originalTheme = application.RequestedThemeVariant;
            Window? window = null;
            try
            {
                application.RequestedThemeVariant = theme == "dark" ? ThemeVariant.Dark : ThemeVariant.Light;
                var scale = zoom / 100d;
                var host = new WorkspaceLayoutHost { Page = new Border { Child = new TextBlock { Text = "Working page" } } };
                window = Show(host, 640, height, scale);
                host.UpdateViewport(window.ClientSize.Width / scale);
                var footer = CreateFooter(false, out var apply, out var reason, out var context);
                host.SetPagePanels(new PaneProvider(footer));
                PageScreenshots.Settle(window);

                var caseName = $"640-{height}-{zoom}-{theme}";
                SaveCaptureIfRequested(window, $"footer-at-top-{caseName}.png");
                var availableHeight = window.ClientSize.Height / scale;
                var expectedBudget = Math.Min(FooterHeightCap, Math.Max(0, (availableHeight - (double)host.FindResource("Component.Shell.WorkingPageMinHeight")!) / 2));
                var pageViewport = host.FindControl<ContentControl>("WorkingPageHost")!;
                Assert.True(pageViewport.Bounds.Height >= expectedBudget - 2,
                    $"The working page has {pageViewport.Bounds.Height:0.#} DIP beside a {footer.Bounds.Height:0.#} DIP footer at " +
                    $"{window.ClientSize.Width}×{window.ClientSize.Height}, {zoom}%.");

                var pageFooterViewport = Assert.IsType<ScrollViewer>(host.FindControl<ScrollViewer>("PageActionFooterViewport"));
                var inspectorFooterViewport = Assert.IsType<ScrollViewer>(host.FindControl<ScrollViewer>("InspectorFooterViewport"));
                Assert.Equal(expectedBudget, pageFooterViewport.MaxHeight);
                Assert.InRange(pageFooterViewport.Bounds.Height, expectedBudget - 2, expectedBudget + 2);
                Assert.True(pageFooterViewport.IsEffectivelyVisible);
                Assert.False(inspectorFooterViewport.IsEffectivelyVisible);
                Assert.Same(footer, host.FindControl<ContentControl>("PageActionFooterHost")!.Content);
                Assert.Null(host.FindControl<ContentControl>("InspectorFooterHost")!.Content);
                Assert.Same(context, apply.DataContext);
                Assert.Same(context, reason.DataContext);
                Assert.False(apply.IsEffectivelyEnabled);
                Assert.Equal(ScrollBarVisibility.Disabled,
                    ScrollViewer.GetHorizontalScrollBarVisibility(pageFooterViewport));
                Assert.Equal(ScrollBarVisibility.Auto,
                    ScrollViewer.GetVerticalScrollBarVisibility(pageFooterViewport));
                Assert.True(pageFooterViewport.Extent.Height > pageFooterViewport.Viewport.Height);

                pageFooterViewport.Offset = new Vector(pageFooterViewport.Offset.X, pageFooterViewport.Extent.Height);
                PageScreenshots.Settle(window);
                SaveCaptureIfRequested(window, $"footer-apply-reason-{caseName}.png");
                Assert.True(pageFooterViewport.Offset.Y > 0);
                Assert.True(apply.IsEffectivelyVisible);
                Assert.True(reason.IsEffectivelyVisible);
                AssertInside(pageFooterViewport, apply, window);
                AssertInside(pageFooterViewport, reason, window);
                LayoutAssertions.AssertCurrent(window);

                HeadlessClick.Click(window, host.FindControl<Button>("InspectorStrip")!, "Show Inspector");
                PageScreenshots.Settle(window);
                AssertFooterInOneHost(host, footer, inspectorVisible: true);
                Assert.True(inspectorFooterViewport.IsEffectivelyVisible);
                Assert.False(pageFooterViewport.IsEffectivelyVisible);
                var inspectorBudget = Math.Min(FooterHeightCap, availableHeight - (double)host.FindResource("Component.Shell.PaneHeaderHeight")! -
                    (double)host.FindResource("Component.Shell.InspectorBodyMinHeight")!);
                Assert.Equal(inspectorBudget, inspectorFooterViewport.MaxHeight);
                Assert.InRange(inspectorFooterViewport.Bounds.Height, Math.Min(inspectorBudget, expectedBudget) - 2, inspectorBudget + 2);
                Assert.Equal(ScrollBarVisibility.Auto,
                    ScrollViewer.GetVerticalScrollBarVisibility(inspectorFooterViewport));
                var inspectorPane = host.FindControl<Border>("InspectorPane")!;
                var header = Assert.Single(inspectorPane.GetVisualDescendants().OfType<Border>(),
                    border => border.Classes.Contains("workspacePaneHeader"));
                Assert.True(inspectorPane.Bounds.Height - header.Bounds.Height - inspectorFooterViewport.Bounds.Height >= 80,
                    "The Inspector footer leaves no useful body below its header.");

                HeadlessClick.Click(window, host.FindControl<Button>("InspectorHideButton")!, "Hide Inspector");
                PageScreenshots.Settle(window);
                AssertFooterInOneHost(host, footer, inspectorVisible: false);
                Assert.Same(context, apply.DataContext);
            }
            finally
            {
                window?.Close();
                application.RequestedThemeVariant = originalTheme;
            }
        });
    }

    [Theory]
    [InlineData(640, 600, 150)]
    [InlineData(640, 600, 100)]
    [InlineData(1240, 800, 100)]
    public void ReviewApplyAndItsReasonAreVisibleWithoutScrollingBesideTheFirstRunNotice(int width, int height, int zoom)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var fake = new FakeCommandClient();
            fake.PendingChangesIs(ReviewScreenshots.Blocked());
            var (workspace, window) = await ReviewScreenshots.OpenAsync(fake);
            try
            {
                workspace.CurrentPage = WorkspacePage.Review;
                LayoutInvariantFixtures.Apply(window, new LayoutCell(width, height, zoom));
                PageScreenshots.Settle(window);
                SaveCaptureIfRequested(window, $"review-apply-{width}-{height}-{zoom}.png");
                var apply = Assert.Single(window.GetVisualDescendants().OfType<Button>(), button =>
                    AutomationProperties.GetAutomationId(button) == AutomationIds.ApplyChanges);
                var reason = Assert.Single(window.GetVisualDescendants().OfType<ScrollViewer>(),
                    scroll => scroll.Name is "PageActionFooterViewport" or "InspectorFooterViewport" && scroll.IsEffectivelyVisible);
                foreach (var control in new Control[] { apply, reason })
                {
                    Assert.True(control.IsEffectivelyVisible, $"{control.GetType().Name} is not visible.");
                    var box = new Rect(control.Bounds.Size).TransformToAABB(control.TransformToVisual(window)!.Value);
                    Assert.True(box.Top >= 0 && box.Bottom <= window.ClientSize.Height + 1, $"Outside the window: {box}.");
                    foreach (var scroller in control.GetVisualAncestors().OfType<ScrollViewer>())
                    {
                        var presenter = scroller.GetVisualDescendants().OfType<ScrollContentPresenter>().First();
                        var view = new Rect(presenter.Bounds.Size).TransformToAABB(presenter.TransformToVisual(window)!.Value);
                        Assert.True(view.Top <= box.Top + 1 && view.Bottom >= box.Bottom - 1,
                            $"{control.GetType().Name} needs scrolling inside {scroller.Name}: {box} against {view}.");
                    }
                }
                var minimum = (double)window.FindResource("Component.Shell.WorkingPageMinHeight")!;
                Assert.True(window.FindInWindowOrWorkspace<Panel>("PageHost")!.Bounds.Height >= minimum - 0.5,
                    $"The page is {window.FindInWindowOrWorkspace<Panel>("PageHost")!.Bounds.Height} tall; the minimum is {minimum}. " +
                    $"Host {window.FindInWindowOrWorkspace<WorkspaceLayoutHost>("WorkspaceLayout")!.Bounds.Height}, notice {window.FindInWindowOrWorkspace<ScrollViewer>("TechDemoNoticeViewport")!.Bounds.Height}/{window.FindInWindowOrWorkspace<ScrollViewer>("TechDemoNoticeViewport")!.MaxHeight}, footer {window.GetVisualDescendants().OfType<ScrollViewer>().First(v => v.Name == "PageActionFooterViewport").Bounds.Height}/{window.GetVisualDescendants().OfType<ScrollViewer>().First(v => v.Name == "PageActionFooterViewport").MaxHeight}.");
            }
            finally
            {
                window.Close();
            }
        }, TimeSpan.FromMinutes(3));
    }

    [Theory]
    [InlineData(640, 600, 150)]
    [InlineData(960, 780, 150)]
    public void ReviewShowsTheFullReasonApplyIsUnavailableUntruncatedInItsFocusableDetails(int width, int height, int zoom)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var fake = new FakeCommandClient();
            fake.PendingChangesIs(ReviewScreenshots.Blocked());
            var (workspace, window) = await ReviewScreenshots.OpenAsync(fake);
            try
            {
                workspace.CurrentPage = WorkspacePage.Review;
                LayoutInvariantFixtures.Apply(window, new LayoutCell(width, height, zoom));
                PageScreenshots.Settle(window);
                var full = Assert.Single(window.GetVisualDescendants().OfType<TextBlock>(), text =>
                    AutomationProperties.GetName(text) == "Why Apply is unavailable");
                Assert.True(full.IsEffectivelyVisible, "The full reason is not shown.");
                Assert.Equal(TextWrapping.Wrap, full.TextWrapping);
                Assert.Equal(TextTrimming.None, full.TextTrimming);
                Assert.True(full.DesiredSize.Width <= full.Bounds.Width + 1, "The full reason is cut off sideways.");
                Assert.Contains(workspace.PageModel<ReviewPageModel>().ApplyDisabledReason!, full.Text);
                var details = Assert.Single(window.GetVisualDescendants().OfType<ScrollViewer>(),
                    scroll => scroll.Name is "PageActionFooterViewport" or "InspectorFooterViewport" && scroll.IsEffectivelyVisible);
                Assert.True(details.Focusable && details.IsTabStop, "The details cannot take keyboard focus.");
                Assert.Equal("Details", AutomationProperties.GetName(details));
                var presenter = details.GetVisualDescendants().OfType<ScrollContentPresenter>().First();
                var view = new Rect(presenter.Bounds.Size).TransformToAABB(presenter.TransformToVisual(window)!.Value);
                var line = new Rect(0, 0, full.Bounds.Width, Math.Min(full.Bounds.Height, full.FontSize * 1.6))
                    .TransformToAABB(full.TransformToVisual(window)!.Value);
                Assert.True(line.Top >= view.Top - 1 && line.Bottom <= view.Bottom + 1,
                    $"The reason's first line {line} is outside the details viewport {view} without scrolling.");
                var top = line.Top;
                Assert.All(details.GetVisualDescendants().OfType<TextBlock>().Where(text => !ReferenceEquals(text, full) &&
                    text.IsEffectivelyVisible && !string.IsNullOrWhiteSpace(text.Text) && !text.GetVisualAncestors().Contains(full)),
                    text => Assert.True(text.TransformToVisual(window)!.Value.Transform(default).Y >= top - 1,
                        $"'{text.Text}' comes before the reason."));
                var minimum = (double)window.FindResource("Component.Shell.FooterDetailsMinHeight")!;
                Assert.True(details.Bounds.Height >= minimum - 0.5, $"The details are {details.Bounds.Height} tall.");
                details.ScrollToEnd();
                PageScreenshots.Settle(window);
                var shortReason = Assert.Single(window.GetVisualDescendants().OfType<CopyableTextBlock>(), text =>
                    text.Classes.Contains("applyShortReason") && text.IsEffectivelyVisible);
                var apply = Assert.Single(window.GetVisualDescendants().OfType<Button>(), button =>
                    AutomationProperties.GetAutomationId(button) == AutomationIds.ApplyChanges);
                var client = new Rect(window.ClientSize);
                var reasonBounds = new Rect(shortReason.Bounds.Size).TransformToAABB(shortReason.TransformToVisual(window)!.Value);
                var applyBounds = new Rect(apply.Bounds.Size).TransformToAABB(apply.TransformToVisual(window)!.Value);
                Assert.True(client.Contains(reasonBounds) && client.Contains(applyBounds) &&
                    !shortReason.GetVisualAncestors().Contains(details) && !string.IsNullOrWhiteSpace(shortReason.Text),
                    $"The reason {reasonBounds} and Apply {applyBounds} must remain visible after scrolling {view}.");
                var under = reasonBounds.Top >= applyBounds.Bottom - 1 && reasonBounds.Left < applyBounds.Right &&
                    reasonBounds.Right > applyBounds.Left;
                var beside = reasonBounds.Left >= applyBounds.Right - 1 && reasonBounds.Top < applyBounds.Bottom &&
                    reasonBounds.Bottom > applyBounds.Top;
                Assert.True(under || beside, $"The reason {reasonBounds} must sit beside or under Apply {applyBounds}.");
            }
            finally
            {
                window.Close();
            }
        }, TimeSpan.FromMinutes(3));
    }

    [Fact]
    public void LongApplyBlockerKeepsItsReasonAndButtonVisible()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var fake = new FakeCommandClient();
            fake.PendingChangesIs(new SIL.Motif.Contract.Responses.PendingChangesSnapshot("draft/one", "revision/one",
                [new SIL.Motif.Contract.Responses.PendingChange("one", "wordform/one", "kitabu", "approve",
                    "assessment/one", "book", ["op/one"])],
                [new SIL.Motif.Contract.Responses.ChangeFit("one", true, [])]));
            fake.MeasurePendingCompletesWith(
                SIL.Motif.Tests.Support.TestFixtures.PendingMeasureResponseFactory.Create("job/one", "revision/one",
                    FakeCommandClient.CompleteNumbers with
                    {
                        WordsLosingApprovedAnalysis = Enumerable.Range(0, 80).Select(i => $"word{i}").ToArray(),
                    }));
            var (workspace, window) = await ReviewScreenshots.OpenAsync(fake);
            try
            {
                workspace.CurrentPage = WorkspacePage.Review;
                var page = workspace.PageModel<ReviewPageModel>();
                PageScreenshots.Settle(window);
                Assert.True(page.MeasureCommand.CanExecute(null));
                await page.MeasureCommand.ExecuteAsync(null);
                LayoutInvariantFixtures.Apply(window, new LayoutCell(640, 600, 150));
                PageScreenshots.Settle(window);
                Assert.Equal(ApplyBlockerKind.LosesApprovedAnalysis, page.ApplyBlockers[0].Kind);
                var reason = Assert.Single(window.GetVisualDescendants().OfType<CopyableTextBlock>(), text =>
                    text.Classes.Contains("applyShortReason") && text.IsEffectivelyVisible);
                var apply = Assert.Single(window.GetVisualDescendants().OfType<Button>(), button =>
                    AutomationProperties.GetAutomationId(button) == AutomationIds.ApplyChanges);
                var client = new Rect(window.ClientSize);
                var reasonBounds = new Rect(reason.Bounds.Size).TransformToAABB(reason.TransformToVisual(window)!.Value);
                var applyBounds = new Rect(apply.Bounds.Size).TransformToAABB(apply.TransformToVisual(window)!.Value);
                Assert.True(client.Contains(reasonBounds) && client.Contains(applyBounds),
                    $"Long blocker: reason {reasonBounds}, Apply {applyBounds}, client {client}.");
                Assert.Equal(MotifAppStrings.ReviewApplyLosesAnalysis, reason.Text);
                Assert.Contains("word79", page.ApplyDisabledReason);
            }
            finally { window.Close(); }
        }, TimeSpan.FromMinutes(3));
    }

    [Fact]
    public void EveryApplyBlockerShowsItsLocalizedShortReason()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, main) = await ReviewScreenshots.OpenAsync(ReviewDenseTableTests.MixedChanges());
            var window = new MotifTestWindow { Width = 640, Height = 600 };
            try
            {
                var panel = new ReviewPanel(workspace.PageModel<ReviewPageModel>());
                Assert.True(panel.Resources.ContainsKey("ApplyShortReasonTemplate"));
                var template = Assert.IsAssignableFrom<IDataTemplate>(panel.Resources["ApplyShortReasonTemplate"]);
                var resources = new System.Resources.ResourceManager("SIL.Motif.App.MotifAppStrings", typeof(MotifAppStrings).Assembly);
                window.Show();
                foreach (var kind in Enum.GetValues<ApplyBlockerKind>())
                {
                    var resourceName = kind switch
                    {
                        ApplyBlockerKind.NoLongerFits => "ReviewApplyDoesNotFit",
                        ApplyBlockerKind.LosesApprovedAnalysis => "ReviewApplyLosesAnalysis",
                        ApplyBlockerKind.NotReady => "ReviewApplyNotReady",
                        _ => "ReviewApply" + kind,
                    };
                    var expected = resources.GetString("MotifApp." + resourceName);
                    Assert.False(string.IsNullOrWhiteSpace(expected), $"Missing resource for {kind}.");
                    var blocker = new ApplyBlocker(kind, "unlocalized sentinel");
                    var content = Assert.IsAssignableFrom<Control>(template.Build(blocker));
                    content.DataContext = blocker;
                    window.Content = content;
                    PageScreenshots.Settle(window);
                    var reason = Assert.Single(content.GetVisualDescendants().OfType<CopyableTextBlock>(),
                        text => text.Classes.Contains("applyShortReason") && text.IsEffectivelyVisible);
                    Assert.Equal(expected, reason.Text);
                }
            }
            finally
            {
                window.Close();
                main.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromMinutes(3));
    }

    [Fact]
    public void EmptyAndApplyingStatesShowLocalizedShortReasons()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var empty = new FakeCommandClient();
            empty.PendingChangesIs(new SIL.Motif.Contract.Responses.PendingChangesSnapshot("draft/one", "revision/empty", [], []));
            var (emptyWorkspace, emptyWindow) = await ReviewScreenshots.OpenAsync(empty);
            try
            {
                emptyWorkspace.CurrentPage = WorkspacePage.Review;
                PageScreenshots.Settle(emptyWindow);
                AssertPinnedReason(emptyWindow, MotifAppStrings.ReviewApplyChooseChange);
            }
            finally
            {
                emptyWindow.Close();
                await emptyWorkspace.DisposeAsync();
            }

            var fake = new FakeCommandClient();
            fake.PendingChangesIs(new SIL.Motif.Contract.Responses.PendingChangesSnapshot("draft/one", "revision/one",
                [new SIL.Motif.Contract.Responses.PendingChange("one", "wordform/one", "kitabu", "approve",
                    "assessment/one", "book", ["op/one"])],
                [new SIL.Motif.Contract.Responses.ChangeFit("one", true, [])]));
            fake.MeasurePendingCompletesWith(
                SIL.Motif.Tests.Support.TestFixtures.PendingMeasureResponseFactory.Create("job/one", "revision/one",
                    FakeCommandClient.CompleteNumbers));
            var completion = new TaskCompletionSource<SIL.Motif.Contract.Commands.CommandOutcome<SIL.Motif.Contract.Responses.ApplyPendingResult>>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            fake.ApplyPendingHandler = (_, _) => completion.Task;
            var (workspace, window) = await ReviewScreenshots.OpenAsync(fake);
            Task? applying = null;
            try
            {
                workspace.CurrentPage = WorkspacePage.Review;
                PageScreenshots.Settle(window);
                var page = workspace.PageModel<ReviewPageModel>();
                await page.MeasureCommand.ExecuteAsync(null);
                applying = page.ApplyCommand.ExecuteAsync(null);
                PageScreenshots.Settle(window);
                Assert.True(page.IsApplying);
                AssertPinnedReason(window, MotifAppStrings.ReviewApplyApplying);
            }
            finally
            {
                completion.TrySetResult(SIL.Motif.Contract.Commands.CommandOutcome<SIL.Motif.Contract.Responses.ApplyPendingResult>.Refused(
                    new SIL.Motif.Contract.Commands.Refusal("job.wait-cancelled", SIL.Motif.Contract.Responses.FailureReason.Cancelled, "Cancelled.")));
                if (applying is not null) await applying.WaitAsync(TimeSpan.FromSeconds(15));
                window.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromMinutes(3));
    }

    private static void AssertPinnedReason(Window window, string expected)
    {
        var reason = Assert.Single(window.GetVisualDescendants().OfType<CopyableTextBlock>(), text =>
            text.Classes.Contains("applyShortReason") && text.IsEffectivelyVisible);
        Assert.Equal(expected, reason.Text);
    }

    [Fact]
    public void ARefusalFlowsInItsHostButScrollsForItselfElsewhere()
    {
        avalonia.Invoke(() =>
        {
            var refusal = WindowRefusal.From(new SIL.Motif.Contract.Commands.Refusal(
                SIL.Motif.Contract.Commands.RefusalCodes.StoreOtherVersion,
                SIL.Motif.Contract.Responses.FailureReason.Refused, "Store belongs to another version."));
            Assert.True(refusal.HasDetails, "The refusal has no details to fold.");
            var flowing = new RefusalBlock { DataContext = refusal };
            flowing.Classes.Add("refusalFlow");
            var own = new RefusalBlock { DataContext = refusal };
            var window = new MotifTestWindow { Width = 400, Height = 300, Content = new StackPanel { Children = { flowing, own } } };
            window.Show();
            try
            {
                PageScreenshots.Settle(window);
                foreach (var viewer in flowing.GetVisualDescendants().OfType<ScrollViewer>())
                {
                    Assert.Equal(ScrollBarVisibility.Disabled, viewer.VerticalScrollBarVisibility);
                    Assert.True(double.IsPositiveInfinity(viewer.MaxHeight), "A flowing refusal keeps a height cap.");
                }
                Assert.All(own.GetVisualDescendants().OfType<ScrollViewer>(),
                    viewer => Assert.False(double.IsPositiveInfinity(viewer.MaxHeight)));
                var token = (double)window.FindResource("Component.Refusal.HeaderHeight")!;
                var header = Assert.Single(own.GetVisualDescendants().OfType<ToggleButton>(), toggle => toggle.Name == "ExpanderHeader");
                Assert.InRange(header.Bounds.Height, token, token + 1);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Theory]
    [InlineData(100)]
    [InlineData(150)]
    public void EnabledApplyReceivesInputAfterScrollingToTheEnd(int zoom)
    {
        avalonia.Invoke(() =>
        {
            var host = new WorkspaceLayoutHost { Page = new Border() };
            var window = Show(host, 640, 600, zoom / 100d);
            try
            {
                host.UpdateViewport(window.ClientSize.Width / (zoom / 100d));
                var footer = CreateFooter(true, out var apply, out _, out _);
                var applied = false;
                apply.Click += (_, _) => applied = true;
                host.SetPagePanels(new PaneProvider(footer));
                PageScreenshots.Settle(window);

                var viewport = Assert.IsType<ScrollViewer>(host.FindControl<ScrollViewer>("PageActionFooterViewport"));
                viewport.Offset = new Vector(viewport.Offset.X, viewport.Extent.Height);
                PageScreenshots.Settle(window);
                AssertInside(viewport, apply, window);
                HeadlessClick.Click(window, apply, "Apply after scrolling");
                Assert.True(applied);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void WideInspectorFooterStopsAtItsTokenCap()
    {
        avalonia.Invoke(() =>
        {
            var host = new WorkspaceLayoutHost { Page = new Border() };
            var window = Show(host, 1920, 1080, 1);
            try
            {
                host.UpdateViewport(window.ClientSize.Width);
                var footer = CreateFooter(false, out var apply, out var reason, out _);
                host.SetPagePanels(new PaneProvider(footer));
                PageScreenshots.Settle(window);

                var viewport = Assert.IsType<ScrollViewer>(host.FindControl<ScrollViewer>("InspectorFooterViewport"));
                Assert.True(viewport.IsEffectivelyVisible);
                Assert.Equal(240d, viewport.MaxHeight);
                Assert.Equal(240d, viewport.Bounds.Height);
                viewport.Offset = new Vector(viewport.Offset.X, viewport.Extent.Height);
                PageScreenshots.Settle(window);
                SaveCaptureIfRequested(window, "footer-wide-1920-1080.png");
                AssertInside(viewport, apply, window);
                AssertInside(viewport, reason, window);
                LayoutAssertions.AssertCurrent(window);
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static Window Show(WorkspaceLayoutHost host, int width, int height, double scale)
    {
        var window = new MotifTestWindow
        {
            Width = width,
            Height = height,
            Content = new LayoutTransformControl
            {
                LayoutTransform = new ScaleTransform(scale, scale),
                Child = host,
            },
        };
        window.Show();
        PageScreenshots.Settle(window);
        Assert.Equal(width, window.ClientSize.Width);
        Assert.Equal(height, window.ClientSize.Height);
        return window;
    }

    private static StackPanel CreateFooter(bool enableApply, out Button apply, out TextBlock reason, out object context)
    {
        context = new object();
        var footer = new StackPanel { DataContext = context };
        const string refusal = "The project has changed since these checks were prepared. Review the explanation and " +
            "confirm that the selected grammar and current project still describe the same work. ";
        for (var index = 0; index < 14; index++)
            footer.Children.Add(new TextBlock
            {
                Text = string.Concat(Enumerable.Repeat(refusal, 3)),
                TextWrapping = TextWrapping.Wrap,
            });
        apply = new Button { Name = "FooterApply", Content = "Apply", IsEnabled = enableApply };
        reason = new TextBlock
        {
            Name = "FooterApplyReason",
            Text = "Apply is disabled until every explanation has been reviewed and the project is checked again.",
            TextWrapping = TextWrapping.Wrap,
        };
        footer.Children.Add(apply);
        footer.Children.Add(reason);
        return footer;
    }

    private static void AssertFooterInOneHost(WorkspaceLayoutHost host, Control footer, bool inspectorVisible)
    {
        var pageContent = host.FindControl<ContentControl>("PageActionFooterHost")!.Content;
        var inspectorContent = host.FindControl<ContentControl>("InspectorFooterHost")!.Content;
        Assert.Equal(inspectorVisible, ReferenceEquals(footer, inspectorContent));
        Assert.Equal(!inspectorVisible, ReferenceEquals(footer, pageContent));
        Assert.NotSame(pageContent, inspectorContent);
    }

    private static void AssertInside(ScrollViewer viewport, Control control, Window window)
    {
        Assert.True(control.IsEffectivelyVisible, $"{control.Name ?? control.GetType().Name} is not visible.");
        var presenter = Assert.Single(viewport.GetVisualDescendants().OfType<ScrollContentPresenter>());
        AssertRectContains(BoundsInWindow(presenter, window), BoundsInWindow(control, window), control.Name);
        var bounds = BoundsInWindow(control, window);
        Assert.True(bounds.Left >= -1 && bounds.Top >= -1 && bounds.Right <= window.ClientSize.Width + 1 &&
            bounds.Bottom <= window.ClientSize.Height + 1,
            $"{control.Name ?? control.GetType().Name} is outside the window client area: {bounds}.");
    }

    private static Rect BoundsInWindow(Control control, Window window)
    {
        var points = new[]
        {
            new Point(0, 0),
            new Point(control.Bounds.Width, 0),
            new Point(0, control.Bounds.Height),
            new Point(control.Bounds.Width, control.Bounds.Height),
        }.Select(point => control.TranslatePoint(point, window)
            ?? throw new InvalidOperationException($"{control.Name ?? control.GetType().Name} is detached."))
            .ToArray();
        var left = points.Min(point => point.X);
        var top = points.Min(point => point.Y);
        var right = points.Max(point => point.X);
        var bottom = points.Max(point => point.Y);
        return new Rect(left, top, right - left, bottom - top);
    }

    private static void AssertRectContains(Rect container, Rect content, string? name)
    {
        Assert.True(container.Left <= content.Left + 1 && container.Top <= content.Top + 1 &&
            container.Right >= content.Right - 1 && container.Bottom >= content.Bottom - 1,
            $"{name ?? "Control"} is outside the clipped footer viewport: {content}; viewport: {container}.");
    }

    private static void SaveCaptureIfRequested(Window window, string fileName)
    {
        var folder = Environment.GetEnvironmentVariable(ScreenshotFactAttribute.FolderVariable);
        if (string.IsNullOrWhiteSpace(folder)) return;
        Directory.CreateDirectory(folder);
        using var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException($"No frame rendered for {fileName}.");
        frame.Save(Path.Combine(folder, fileName), PngBitmapEncoderOptions.Default);
    }

    private sealed class PaneProvider(Control footer) : IWorkspacePaneProvider
    {
        public string SidePanelTitle => "Details";
        public Control? SidePanel => null;
        public Control InspectorFooter => footer;
    }
}
