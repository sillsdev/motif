using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.VisualTree;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Commands.Queries;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class SetupHLayoutTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SetupTextInventoryCountsMatchTheStoredSelectionSources(bool useCapturedText)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, frame) = await PageScreenshots.OpenOverSampleData(
                capturedWords: useCapturedText ? WritingSystemTestData.ArabicText() : null);
            try
            {
                var summary = workspace.Context.SelectionReads.Summary!;
                Assert.NotEmpty(summary.SourcePositions);
                var choices = workspace.Context.Setup!.Selection.Texts.Where(text => text.IsChecked).ToArray();
                Assert.Equal(summary.Texts.Count, choices.Length);
                foreach (var choice in choices)
                {
                    var text = Assert.Single(summary.Texts, text => text.TextId == choice.Id);
                    Assert.Equal(text.DistinctFormCount, choice.WordCount);
                    Assert.NotEqual("no words", choice.InterlinearizedText);
                    var interlinearized = summary.SourcePositions.Where(position =>
                            position.Location.Anchor.TextId == choice.Id && position.Location.AnalysisId is not null)
                        .Select(position => position.Word.Form).Distinct(StringComparer.Ordinal).Count();
                    Assert.Equal(interlinearized, choice.InterlinearizedWordCount);
                }
                Assert.Equal(summary.PhysicalOccurrenceCount, summary.Texts.Sum(text => text.OccurrenceCount));
            }
            finally
            {
                frame.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromMinutes(2));
    }

    [Fact]
    public void SetupButtonsFollowTheFieldWorksDialogOrderAndEnterAndEscapeAreBoundWhileOpen()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, frame) = await PageScreenshots.OpenOverSampleData();
            var setup = workspace.Context.Setup!;
            setup.IsOpen = true;
            setup.Step = 1;
            var dialog = new SetupDialog { DataContext = setup };
            dialog.FitToAvailableArea(900, 600);
            var window = new MotifTestWindow { Content = dialog, Width = 900, Height = 600 };
            try
            {
                window.Show();
                PageScreenshots.Settle(window);
                var buttons = dialog.GetVisualDescendants().OfType<Button>().Where(button => button.IsEffectivelyVisible).ToArray();
                Button Named(string id) => Assert.Single(buttons, button =>
                    Avalonia.Automation.AutomationProperties.GetAutomationId(button) == id);
                double X(Button button) => button.TranslatePoint(default, window)!.Value.X;
                var skip = Named(SIL.Motif.App.AutomationIds.SkipSetup);
                var back = Named(SIL.Motif.App.AutomationIds.SetupBack);
                var next = Named(SIL.Motif.App.AutomationIds.SetupNext);
                var cancel = Named(SIL.Motif.App.AutomationIds.SetupCancel);
                Assert.True(X(skip) < X(back) && X(back) < X(next) && X(next) < X(cancel));
                Assert.Equal(SIL.Motif.App.MotifAppStrings.Cancel, cancel.Content);
                Assert.True(next.IsDefault);
                Assert.True(cancel.IsCancel);
                Assert.False(skip.IsDefault || skip.IsCancel || back.IsDefault || back.IsCancel);

                setup.CancelCommand.Execute(null);
                Assert.False(setup.IsOpen);
                Assert.False(next.IsDefault);
                Assert.False(cancel.IsCancel);
            }
            finally
            {
                setup.IsOpen = false;
                window.Close();
                frame.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromMinutes(2));
    }

    [Theory]
    [InlineData(640, 600, 1)]
    [InlineData(1040, 480, 1)]
    [InlineData(640, 600, 1.5)]
    [InlineData(1040, 480, 1.5)]
    public void EveryStepKeepsItsPrimaryActionsOutsideTheScrollingBody(int width, int height, double zoom)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, frame) = await PageScreenshots.OpenOverSampleData();
            var setup = workspace.Context.Setup!;
            for (var index = 0; index < 16; index++)
                setup.Selection.Texts.Add(new TextChoiceViewModel(Guid.NewGuid(), $"Additional text {index + 1}", 80, 0));
            setup.IsOpen = true;
            var dialog = new SetupDialog { DataContext = setup };
            dialog.FitToAvailableArea(width / zoom, height / zoom);
            var window = new MotifTestWindow
            {
                Content = new LayoutTransformControl
                {
                    LayoutTransform = new ScaleTransform(zoom, zoom), Child = dialog,
                },
                Width = width, Height = height,
            };
            try
            {
                window.Show();
                for (var step = 0; step < 4; step++)
                {
                    setup.Step = step;
                    PageScreenshots.Settle(window);
                    var body = dialog.FindControl<ScrollViewer>("SetupBodyScroll")!;
                    Assert.NotNull(body);
                    if (step == 1)
                    {
                        var textList = dialog.FindControl<ItemsControl>("SetupDialogTextsItems")!;
                        Assert.Contains(body, textList.GetVisualAncestors());
                        Assert.True(body.Extent.Height > body.Viewport.Height);
                    }
                    var actions = dialog.GetVisualDescendants().OfType<Button>()
                        .Where(button => button.IsEffectivelyVisible &&
                            button.Classes.Any(name => name is "setupPrimary" or "setupSecondary" or "setupSkip"));
                    Assert.Single(actions, button => button.Classes.Contains("setupSkip"));
                    Assert.Single(actions, button => button.Classes.Contains("setupPrimary"));
                    // Back (from step 2 on) and Cancel, which every step shows.
                    Assert.Equal(step == 0 ? 1 : 2,
                        actions.Count(button => button.Classes.Contains("setupSecondary")));
                    foreach (var action in actions)
                    {
                        Assert.DoesNotContain(body, action.GetVisualAncestors());
                        var origin = action.TranslatePoint(default, window)!.Value;
                        var bottomRight = action.TranslatePoint(new Point(action.Bounds.Width, action.Bounds.Height), window)!.Value;
                        Assert.InRange(origin.X, 0, window.ClientSize.Width);
                        Assert.InRange(origin.Y, 0, window.ClientSize.Height);
                        Assert.InRange(bottomRight.X, 0, window.ClientSize.Width + 1);
                        Assert.InRange(bottomRight.Y, 0, window.ClientSize.Height + 1);
                    }
                    body.Offset = new Vector(0, Math.Max(0, body.Extent.Height - body.Viewport.Height));
                    PageScreenshots.Settle(window);
                    Assert.True(body.Extent.Width <= body.Viewport.Width + 1);
                    foreach (var action in actions)
                    {
                        var origin = action.TranslatePoint(default, window)!.Value;
                        var bottomRight = action.TranslatePoint(new Point(action.Bounds.Width, action.Bounds.Height), window)!.Value;
                        Assert.InRange(origin.X, 0, window.ClientSize.Width);
                        Assert.InRange(origin.Y, 0, window.ClientSize.Height);
                        Assert.InRange(bottomRight.X, 0, window.ClientSize.Width + 1);
                        Assert.InRange(bottomRight.Y, 0, window.ClientSize.Height + 1);
                    }
                }
            }
            finally
            {
                setup.IsOpen = false;
                window.Close();
                frame.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromMinutes(2));
    }
    public static TheoryData<string, int, int, bool, int> CaptureCases
    {
        get
        {
            var cases = new TheoryData<string, int, int, bool, int>();
            foreach (var (name, width, height) in new[]
                     { ("thin", 640, 600), ("half", 1040, 780), ("full", 1920, 1080), ("short", 1040, 480) })
            foreach (var dark in new[] { false, true })
            foreach (var zoom in new[] { 100, 150 })
                cases.Add(name, width, height, dark, zoom);
            return cases;
        }
    }

    [Fact]
    public void AnActuallyEmptyCapturedTextKeepsItsZeroInventoryCount()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var captured = new TextWordsResponse([], [new TextLines(Guid.NewGuid(), "Empty Text", [])], true);
            var (workspace, frame) = await PageScreenshots.OpenOverSampleData(parse: false, capturedWords: captured);
            try
            {
                var choice = Assert.Single(workspace.Context.Setup!.Selection.Texts);
                Assert.Equal(0, choice.WordCount);
                Assert.Equal("0 distinct", choice.DistinctCountText);
                Assert.Equal("no words", choice.InterlinearizedText);
            }
            finally
            {
                frame.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromMinutes(2));
    }

    [ScreenshotTheory]
    [MemberData(nameof(CaptureCases))]
    public void CaptureEverySetupStepAtFourSizes(string size, int width, int height, bool dark, int zoom)
    {
        var folder = Environment.GetEnvironmentVariable(ScreenshotFactAttribute.FolderVariable)!;
        Directory.CreateDirectory(folder);
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var previousTheme = Application.Current!.RequestedThemeVariant;
            var (workspace, frame) = await PageScreenshots.OpenOverSampleData();
            var setup = workspace.Context.Setup!;
            setup.IsOpen = true;
            var dialog = new SetupDialog { DataContext = setup };
            var window = new MotifTestWindow
            {
                Width = width, Height = height,
                Content = new LayoutTransformControl { Child = dialog,
                    LayoutTransform = new ScaleTransform(zoom / 100d, zoom / 100d) },
            };
            try
            {
                var theme = dark ? Avalonia.Styling.ThemeVariant.Dark : Avalonia.Styling.ThemeVariant.Light;
                Application.Current.RequestedThemeVariant = theme;
                dialog.FitToAvailableArea(width * 100d / zoom, height * 100d / zoom);
                window.Show();
                for (var step = 0; step < 4; step++)
                {
                    setup.Step = step;
                    PageScreenshots.Settle(window);
                    using var capture = window.CaptureRenderedFrame() ??
                        throw new InvalidOperationException("The Setup window did not render.");
                    var suffix = zoom == 100 ? string.Empty : "-150";
                    var themeName = dark ? "dark" : "light";
                    capture.Save(Path.Combine(folder, $"h-setup-step{step + 1}-{size}{suffix}-{themeName}.png"),
                        Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
                }
            }
            finally
            {
                Application.Current.RequestedThemeVariant = previousTheme;
                setup.IsOpen = false;
                window.Close();
                frame.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromMinutes(3));
    }

}
