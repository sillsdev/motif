using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.App.Views.Shell;
using SIL.Motif.Tests.App.Walkthrough;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class TryWordHLayoutTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InputAndRunTargetsStaySeparateAtTheMinimumWidthAtEnlargedZoom(bool hasResult)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, frame) = await PageScreenshots.OpenOverSampleData();
            var model = workspace.PageModel<TryWordPageModel>();
            if (hasResult) model.Trace.Result = PageScreenshots.TraceWithIdentities();
            var page = PageRegistry.For(WorkspacePage.TryAWord).CreateView(model);
            var window = new Window { Content = Zoom(page, 150), Width = 640, Height = 600 };
            try
            {
                window.Show();
                PageScreenshots.Settle(window);
                var input = Named<TextBox>(page, "Word to try");
                var run = Named<Button>(page, "Try the word");
                var inputBounds = new Rect(input.TranslatePoint(default, page)!.Value, input.Bounds.Size);
                var runBounds = new Rect(run.TranslatePoint(default, page)!.Value, run.Bounds.Size);
                Assert.False(inputBounds.Intersects(runBounds));
                foreach (var bounds in new[] { inputBounds, runBounds })
                {
                    Assert.InRange(bounds.Left, 0, page.Bounds.Width);
                    Assert.InRange(bounds.Right, 0, page.Bounds.Width);
                    Assert.InRange(bounds.Top, 0, page.Bounds.Height);
                    Assert.InRange(bounds.Bottom, 0, page.Bounds.Height);
                }
                LayoutAssertions.AssertCurrent(page);
                if (Environment.GetEnvironmentVariable(ScreenshotFactAttribute.FolderVariable) is { Length: > 0 } folder)
                {
                    using var capture = window.CaptureRenderedFrame() ??
                        throw new InvalidOperationException("The Try a Word input did not render.");
                    capture.Save(Path.Combine(folder, $"try-input-minimum-150-result-{hasResult}.png"),
                        Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
                }
            }
            finally
            {
                window.Close();
                frame.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromMinutes(2));
    }

    [Theory]
    [InlineData(820, 600, 150)]
    [InlineData(1040, 780, 150)]
    [InlineData(1040, 480, 150)]
    public void OpeningRecordedAttemptsKeepsTheirFirstRowInTheInitialViewport(int width, int height, int zoom)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, frame) = await PageScreenshots.OpenOverSampleData();
            var model = workspace.PageModel<TryWordPageModel>();
            model.Trace.Result = PageScreenshots.TraceWithIdentities();
            model.Trace.WordToTry = model.Trace.Result.Word;
            model.Trace.ShowDroppedPaths = true;
            var page = PageRegistry.For(WorkspacePage.TryAWord).CreateView(model);
            var window = new Window { Content = Zoom(page, zoom), Width = width, Height = height };
            try
            {
                window.Show();
                PageScreenshots.Settle(window);
                var panel = Assert.Single(page.GetVisualDescendants().OfType<TryWordPanel>());
                var scroll = panel.GetVisualDescendants().OfType<ScrollViewer>()
                    .First(viewer => viewer.Content is StackPanel);
                Assert.Equal(0, scroll.Offset.Y);
                var attempts = Named<ListBox>(page, "Attempts that got furthest");
                Assert.NotEmpty(model.Trace.ClosestAttempts);
                var first = Assert.IsType<ListBoxItem>(attempts.ContainerFromIndex(0));
                if (Environment.GetEnvironmentVariable(ScreenshotFactAttribute.FolderVariable) is { Length: > 0 } folder)
                {
                    using var capture = window.CaptureRenderedFrame() ??
                        throw new InvalidOperationException("The Try a Word page did not render.");
                    capture.Save(Path.Combine(folder, $"try-initial-attempts-{width}-{height}-{zoom}.png"),
                        Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
                }
                var presenter = Assert.Single(scroll.GetVisualDescendants().OfType<ScrollContentPresenter>(),
                    candidate => ReferenceEquals(candidate.TemplatedParent, scroll));
                var origin = first.TranslatePoint(default, presenter)!.Value;
                Assert.InRange(origin.Y, 0, presenter.Bounds.Height);
                Assert.True(origin.Y + first.Bounds.Height <= presenter.Bounds.Height + 1,
                    $"First attempt bottom {origin.Y + first.Bounds.Height} exceeds viewport {presenter.Bounds.Height}.");
                Assert.Single(page.GetVisualDescendants().OfType<TextBlock>().Where(block =>
                    block.IsEffectivelyVisible && block.Text?.StartsWith("Parsed", StringComparison.Ordinal) == true));
                var analyses = panel.FindControl<ListBox>("TryWordParserAnalysesItems")!;
                Assert.Same(model.Trace.Analyses, analyses.ItemsSource);
                var details = Named<Expander>(page, "Full parser analysis details");
                Assert.False(details.IsExpanded);
                details.IsExpanded = true;
                PageScreenshots.Settle(window);
                var full = Assert.Single(details.GetVisualDescendants().OfType<TraceAnalysesView>());
                Assert.Same(model.Trace, full.DataContext);
                Assert.Same(model.Trace.Analyses, full.FindControl<ItemsControl>("TraceAnalysesViewAnalysesItems")!.ItemsSource);
                details.IsExpanded = false;
                scroll.Offset = default;
                PageScreenshots.Settle(window);
                var paths = Named<ToggleButton>(page, "Paths the parser dropped");
                paths.BringIntoView();
                PageScreenshots.Settle(window);
                HeadlessClick.Click(window, paths, "Paths the parser dropped");
                Assert.False(model.Trace.ShowDroppedPaths);
                Assert.False(attempts.IsEffectivelyVisible);
                Assert.True(paths.Focus());
                window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);
                window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);
                PageScreenshots.Settle(window);
                Assert.True(model.Trace.ShowDroppedPaths);
                Assert.True(attempts.IsEffectivelyVisible);


            }
            finally
            {
                window.Close();
                frame.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromMinutes(2));
    }

    [Theory]
    [InlineData(820, 600, 100)]
    [InlineData(1040, 780, 100)]
    [InlineData(1920, 1080, 100)]
    [InlineData(1040, 480, 100)]
    [InlineData(820, 600, 150)]
    [InlineData(1040, 780, 150)]
    public void RegisteredPagePinsInputAndSuppliesSelectedAttemptDetails(int width, int height, int zoom)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, frame) = await PageScreenshots.OpenOverSampleData();
            var model = workspace.PageModel<TryWordPageModel>();
            model.Trace.Result = PageScreenshots.TraceWithIdentities();
            model.Trace.WordToTry = model.Trace.Result.Word;
            var page = PageRegistry.For(WorkspacePage.TryAWord).CreateView(model);
            SIL.Motif.App.WindowZoomPolicy.EnablePopupTransforms(page);
            var window = new Window { Content = Zoom(page, zoom), Width = width, Height = height };
            try
            {
                window.Show();
                PageScreenshots.Settle(window);
                var input = Named<TextBox>(page, "Word to try");
                var run = Named<Button>(page, "Try the word");
                var tools = Named<Button>(page, "Try a Word tools");
                var scroll = page.GetVisualDescendants().OfType<ScrollViewer>().First(viewer => viewer.Content is StackPanel);
                var before = input.TranslatePoint(default, window)!.Value;
                scroll.Offset = new Vector(0, scroll.Extent.Height);
                PageScreenshots.Settle(window);
                Assert.Equal(before, input.TranslatePoint(default, window)!.Value);
                input.Focus();
                window.KeyTextInput("word edited after tracing");
                Assert.Contains("word edited after tracing", model.Trace.WordToTry);
                Assert.Equal(PageScreenshots.TraceWithIdentities().Word, model.Trace.Result!.Word);
                foreach (var control in new Control[] { input, run, tools })
                {
                    Assert.True(control.IsEffectivelyVisible);
                    var bounds = new Rect(control.TranslatePoint(default, page)!.Value, control.Bounds.Size);
                    Assert.True(page.Bounds.Size.Width + 1 >= bounds.Right && page.Bounds.Size.Height + 1 >= bounds.Bottom);
                }
                Assert.Equal(1, tools.Opacity);
                Assert.True(tools.Focus());
                Assert.True(scroll.Extent.Width <= scroll.Viewport.Width + 1);
                var panel = page.GetVisualDescendants().OfType<TryWordPanel>().Single();
                var parser = panel.FindControl<Border>("TryWordParserComparison")!;
                Assert.Equal(panel.Bounds.Width < 600 ? 1 : 0, Grid.GetRow(parser));
                Assert.Equal(panel.Bounds.Width < 600 ? 0 : 1, Grid.GetColumn(parser));
                var provider = Assert.IsAssignableFrom<IWorkspacePaneProvider>(page);
                var inspector = Assert.IsAssignableFrom<Control>(provider.InspectorContent);
                Assert.Same(model.Trace, inspector.DataContext);
                Assert.Null(inspector.GetVisualParent());
                model.Trace.ShowDroppedPaths = true;
                PageScreenshots.Settle(window);
                var attempts = Named<ListBox>(page, "Attempts that got furthest");
                var attempt = model.Trace.ClosestAttempts.First();
                attempts.SetCurrentValue(ListBox.SelectedItemProperty, attempt);
                PageScreenshots.Settle(window);
                Assert.Same(attempt, model.Trace.SelectedCandidate);
                var expertOccurrence = model.Trace.ExpertEvents.Single(step =>
                    step.RecordedStep.StepId == attempt.Steps.Last().RecordedStep.StepId);
                model.Trace.SelectedStep = expertOccurrence;
                window.Content = Zoom(inspector, zoom);
                PageScreenshots.Settle(window);
                Assert.True(inspector.IsEffectivelyVisible);
                Assert.Equal(model.Trace.Result!.Word, Named<CopyableTextBlock>(inspector, "Recorded search word").Text);
                var steps = Named<ListBox>(inspector, "Steps on the selected attempt");
                Assert.Same(attempt.Steps, steps.ItemsSource);
                Assert.Same(expertOccurrence, model.Trace.SelectedStep);
                Assert.Same(attempt.Steps.Last(), steps.SelectedItem);
                var otherStep = attempt.Steps.First();
                Assert.NotEqual(expertOccurrence.RecordedStep.StepId, otherStep.RecordedStep.StepId);
                var selectedRow = Assert.IsType<ListBoxItem>(steps.ContainerFromItem(attempt.Steps.Last()));
                selectedRow.BringIntoView();
                Assert.True(selectedRow.Focus());
                window.KeyPress(Key.Home, RawInputModifiers.None, PhysicalKey.Home, null);
                PageScreenshots.Settle(window);
                Assert.Same(otherStep, steps.SelectedItem);
                Assert.Same(otherStep, model.Trace.SelectedStep);
                Assert.Same(otherStep.RecordedStep, model.Trace.SelectedStep!.RecordedStep);
                Assert.Contains(inspector.GetVisualDescendants().OfType<TextBlock>(), block =>
                    block.IsEffectivelyVisible && block.Text == model.Trace.SelectedStep.ReadableText);
                model.Trace.Result = PageScreenshots.TraceWithIdentities();
                model.Trace.WordToTry = model.Trace.Result.Word;
                PageScreenshots.Settle(window);
                Assert.Null(model.Trace.SelectedCandidate);
                Assert.Null(model.Trace.SelectedStep);
            }
            finally
            {
                window.Close();
                frame.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromMinutes(2));
    }

    [Theory]
    [InlineData(1040, 100)]
    [InlineData(1240, 100)]
    [InlineData(1040, 150)]
    [InlineData(1240, 150)]
    public void PlainBuildingSentencesFitTheRecordedTrace(int width, int zoom)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, frame) = await PageScreenshots.OpenOverSampleData();
            var model = workspace.PageModel<TryWordPageModel>();
            model.Trace.Result = SIL.Motif.Commands.Queries.WordTraceQuery.LoadDiagnostic(File.ReadAllText(
                Path.Combine(AppContext.BaseDirectory, "TestFixtures", "trace-details-v3-matinlu.json"))).Value!;
            var page = PageRegistry.For(WorkspacePage.TryAWord).CreateView(model);
            SIL.Motif.App.WindowZoomPolicy.EnablePopupTransforms(page);
            var window = new Window { Content = Zoom(page, zoom), Width = width, Height = 780 };
            try
            {
                window.Show();
                PageScreenshots.Settle(window);
                model.Trace.SelectedStep = model.Trace.ExpertEvents.Single(step => step.RecordedStep.StepId == "0.2.0.3.1");
                PageScreenshots.Settle(window);
                var list = Named<ListBox>(page, "Plain trace steps");
                list.SelectedItem = model.Trace.PlainSelectedStep;
                list.BringIntoView();
                PageScreenshots.Settle(window);
                LayoutAssertions.AssertCurrent(page);
            }
            finally
            {
                window.Close();
                frame.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromMinutes(2));
    }

    [Theory]
    [InlineData(1, "1 attempt")]
    [InlineData(2, "2 attempts")]
    public void ReasonCountsNameTheirRecordedAttempts(int count, string expected)
    {
        var group = new TraceStopGroupViewModel(null, "PartialParse", null, count);
        Assert.Equal(expected, group.CountText);
    }

    [ScreenshotTheory]
    [InlineData("thin", 820, 600, "Light", 100)]
    [InlineData("thin", 820, 600, "Light", 150)]
    [InlineData("thin", 820, 600, "Dark", 100)]
    [InlineData("thin", 820, 600, "Dark", 150)]
    [InlineData("half", 1040, 780, "Light", 100)]
    [InlineData("half", 1040, 780, "Light", 150)]
    [InlineData("half", 1040, 780, "Dark", 100)]
    [InlineData("half", 1040, 780, "Dark", 150)]
    [InlineData("full", 1920, 1080, "Light", 100)]
    [InlineData("full", 1920, 1080, "Light", 150)]
    [InlineData("full", 1920, 1080, "Dark", 100)]
    [InlineData("full", 1920, 1080, "Dark", 150)]
    [InlineData("short", 1040, 480, "Light", 100)]
    [InlineData("short", 1040, 480, "Light", 150)]
    [InlineData("short", 1040, 480, "Dark", 100)]
    [InlineData("short", 1040, 480, "Dark", 150)]
    public void CaptureRegisteredPageAndHostedInspectorMatrix(string size, int width, int height, string themeName, int zoom)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var folder = Environment.GetEnvironmentVariable(ScreenshotFactAttribute.FolderVariable)!;
            Directory.CreateDirectory(folder);
            var (workspace, frame) = await PageScreenshots.OpenOverSampleData();
            var model = workspace.PageModel<TryWordPageModel>();
            model.Trace.Result = PageScreenshots.TraceWithIdentities();
            model.Trace.WordToTry = model.Trace.Result.Word;
            model.RecentWords.Add("matinlu");
            var page = PageRegistry.For(WorkspacePage.TryAWord).CreateView(model);
            SIL.Motif.App.WindowZoomPolicy.EnablePopupTransforms(page);
            var window = new Window();
            try
            {
                var theme = themeName == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
                if (window.Content is LayoutTransformControl previous) previous.Child = null;
                window.Content = Zoom(page, zoom);
                window.Width = width;
                window.Height = height;
                window.RequestedThemeVariant = theme;
                window.Show();
                model.Trace.IsExpert = false;
                model.Trace.ShowDroppedPaths = true;
                model.Trace.SelectedStopGroup = null;
                model.Trace.SelectedCandidate = null;
                model.Trace.SelectedStep = null;
                PageScreenshots.Settle(window);
                foreach (var scroll in page.GetVisualDescendants().OfType<ScrollViewer>()) scroll.Offset = default;
                PageScreenshots.Settle(window);
                Save($"try-{size}-{theme}-{zoom}");
                var input = Named<TextBox>(page, "Word to try");
                Assert.True(input.Focus());
                window.KeyPress(Key.A, RawInputModifiers.Control, PhysicalKey.None, null);
                window.KeyTextInput("new word to try");
                PageScreenshots.Settle(window);
                Assert.Equal("new word to try", model.Trace.WordToTry);
                Save($"typing-{size}-{theme}-{zoom}");
                model.Trace.WordToTry = model.Trace.Result!.Word;
                var tools = Named<Button>(page, "Try a Word tools");
                tools.Focus();
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                PageScreenshots.Settle(window);
                Save($"tools-{size}-{theme}-{zoom}");
                tools.Flyout!.Hide();
                var provider = Assert.IsAssignableFrom<IWorkspacePaneProvider>(page);
                var reasons = page.GetVisualDescendants().OfType<Button>().First(button =>
                    button.DataContext is TraceStopGroupViewModel);
                reasons.BringIntoView();
                reasons.Focus();
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                PageScreenshots.Settle(window);
                Assert.NotNull(model.Trace.SelectedStopGroup);
                var attempts = Named<ListBox>(page, "Attempts that got furthest");
                attempts.SelectedIndex = 0;
                attempts.BringIntoView();
                var firstAttempt = Assert.IsType<ListBoxItem>(attempts.ContainerFromIndex(0));
                Assert.True(firstAttempt.Focus());
                window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
                PageScreenshots.Settle(window);
                Assert.NotNull(model.Trace.SelectedCandidate);
                Assert.Same(attempts.SelectedItem, model.Trace.SelectedCandidate);
                Save($"reason-attempt-{size}-{theme}-{zoom}");
                var inspector = provider.InspectorContent!;
                ((LayoutTransformControl)window.Content!).Child = null;
                window.Content = Zoom(inspector, zoom);
                PageScreenshots.Settle(window);
                var steps = Named<ListBox>(inspector, "Steps on the selected attempt");
                steps.SelectedIndex = 0;
                PageScreenshots.Settle(window);
                var firstStep = Assert.IsType<ListBoxItem>(steps.ContainerFromIndex(0));
                Assert.True(firstStep.Focus());
                window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
                model.Trace.SelectedCandidate!.IsTreeContextExpanded = true;
                PageScreenshots.Settle(window);
                Assert.NotNull(model.Trace.SelectedStep);
                Assert.Same(steps.SelectedItem, model.Trace.SelectedAttemptStep);
                var details = inspector.GetVisualDescendants().OfType<CopyableTextBlock>().Single(block =>
                    block.IsEffectivelyVisible && AutomationProperties.GetName(block) == "Selected recorded step details");
                Assert.Equal(model.Trace.SelectedAttemptStep!.ReadableText, details.Text);
                details.BringIntoView();
                PageScreenshots.Settle(window);
                Assert.True(details.IsEffectivelyVisible);
                var detailsTop = details.TranslatePoint(default, window)!.Value;
                var detailsBottom = details.TranslatePoint(new Point(0, details.Bounds.Height), window)!.Value;
                var fits = detailsTop.Y >= 0 && detailsBottom.Y <= window.ClientSize.Height + 1;
                if (!fits) Save($"failed-inspector-{size}-{theme}-{zoom}");
                var inspectorScroll = inspector.GetVisualDescendants().OfType<ScrollViewer>().Single();
                Assert.True(fits, $"{size}/{theme}/{zoom}: details {detailsTop.Y}..{detailsBottom.Y}, " +
                    $"client {window.ClientSize}, inspector {inspector.Bounds}, " +
                    $"viewport {inspectorScroll.Viewport}, offset {inspectorScroll.Offset}");
                LayoutAssertions.AssertCurrent(inspector);
                Save($"inspector-{size}-{theme}-{zoom}");
                if (width >= 1040 && size != "short")
                {
                    ((LayoutTransformControl)window.Content!).Child = null;
                    page.RenderTransform = null;
                    inspector.RenderTransform = null;
                    var split = new Grid { ColumnDefinitions = new ColumnDefinitions("2*,*") };
                    Grid.SetColumn(inspector, 1);
                    split.Children.Add(page);
                    split.Children.Add(inspector);
                    window.Content = Zoom(split, zoom);
                    PageScreenshots.Settle(window);
                    LayoutAssertions.AssertCurrent(split);
                    Save($"page-inspector-{size}-{theme}-{zoom}");
                    split.Children.Clear();
                    window.Content = null;
                }
            }
            finally
            {
                window.Close();
                frame.Close();
                await workspace.DisposeAsync();
            }

            void Save(string name)
            {
                using var bitmap = window.CaptureRenderedFrame();
                Assert.NotNull(bitmap);
                bitmap.Save(Path.Combine(folder, name + ".png"));
            }
        }, TimeSpan.FromMinutes(5));
    }

    internal static Control Zoom(Control child, int zoom) => new LayoutTransformControl
    {
        Child = child, LayoutTransform = new ScaleTransform(zoom / 100d, zoom / 100d),
    };

    internal static T Named<T>(Control root, string name) where T : Control => root.GetVisualDescendants()
        .OfType<T>().Single(control => AutomationProperties.GetName(control) == name);
}
