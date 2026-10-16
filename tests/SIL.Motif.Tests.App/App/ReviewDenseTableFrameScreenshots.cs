using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using SIL.Motif.App.Controls;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.App.Views.Shell;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class ReviewDenseTableFrameScreenshots
{
    [ScreenshotFact]
    public void CaptureActualReviewFrameAndReachApplyAcrossRequiredSizes()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var folder = Environment.GetEnvironmentVariable(ScreenshotFactAttribute.FolderVariable)!;
            Directory.CreateDirectory(folder);
            var fake = ReviewDenseTableTests.MixedChanges();
            var (workspace, window) = await ReviewScreenshots.OpenAsync(fake);
            var prior = Application.Current!.RequestedThemeVariant;
            var failures = new List<string>();
            try
            {
                workspace.CurrentPage = WorkspacePage.Review;
                var page = workspace.PageModel<ReviewPageModel>();
                await page.LoadReviewAsync();
                PageScreenshots.Settle(window);
                var panel = Assert.Single(window.GetVisualDescendants().OfType<ReviewPanel>());
                var list = panel.FindControl<ListBox>("ReviewItems")!;
                var selected = page.TableEntries.OfType<ReviewListEntryViewModel>().Single(entry => entry.Change is not null);
                list.SelectedItem = selected;
                var settings = SettingsTestAccess.Model(window);
                var host = window.FindControl<WorkspaceLayoutHost>("WorkspaceLayout")!;
                foreach (var (width, height) in new[] { (640, 600), (960, 780), (1920, 1080), (960, 600) })
                foreach (var zoom in new[] { 100, 150 })
                foreach (var (theme, variant) in new[] { ("light", ThemeVariant.Light), ("dark", ThemeVariant.Dark) })
                {
                    var state = $"review-frame-{width}-{height}-{zoom}-{theme}";
                    window.Width = width;
                    window.Height = height;
                    settings.ZoomPercent = zoom;
                    Application.Current.RequestedThemeVariant = variant;
                    PageScreenshots.Settle(window);
                    list.ScrollIntoView(selected);
                    PageScreenshots.Settle(window);
                    Save(state);
                    try
                    {
                        Assert.Equal(width, window.ClientSize.Width);
                        Assert.Equal(height, window.ClientSize.Height);
                        var row = Assert.IsType<ListBoxItem>(list.ContainerFromItem(selected));
                        var frame = Assert.Single(row.GetVisualDescendants().OfType<Border>(), border => border.Classes.Contains("denseTableRow"));
                        var grid = Assert.Single(frame.GetVisualDescendants().OfType<Grid>(), candidate => DenseTable.GetColumns(candidate) is not null);
                        var header = panel.FindControl<Grid>("ReviewTableHeader")!;
                        Assert.Equal(header.ColumnDefinitions.Select(column => column.Width), grid.ColumnDefinitions.Select(column => column.Width));
                        var minimum = (double)Application.Current.FindResource("Component.DenseTable.Column.NarrowWidth")!;
                        Assert.True(grid.ColumnDefinitions[0].ActualWidth >= minimum,
                            $"Primary track {grid.ColumnDefinitions[0].ActualWidth}, minimum {minimum}; list {list.Bounds.Size}.");
                        var word = Assert.Single(frame.GetVisualDescendants().OfType<CopyableTextBlock>(), text => text.Text == "kitabu" && text.IsEffectivelyVisible);
                        Assert.True(word.IsEffectivelyVisible && word.Bounds.Width >= 32 && word.Bounds.Height > 0);
                        var actions = frame.GetVisualDescendants().OfType<Button>().Where(button =>
                            button.Classes.Contains("denseTableAction") || button.Classes.Contains("denseTableMenu")).ToArray();
                        Assert.Equal(3, actions.Length);
                        var scroll = list.GetVisualDescendants().OfType<ScrollViewer>().Single(viewer => viewer.Name == "PART_ScrollViewer");
                        foreach (var action in actions) AssertReachable(action, scroll);
                        LayoutAssertions.AssertCurrent(window);
                    }
                    catch (Exception failure) { failures.Add($"{state}: {failure.Message}"); }
                    var footer = host.FindControl<ScrollViewer>(host.FindControl<Border>("InspectorPane")!.IsVisible
                        ? "InspectorFooterViewport" : "PageActionFooterViewport")!;
                    footer.ScrollToEnd();
                    PageScreenshots.Settle(window);
                    Save(state + "-apply");
                    try
                    {
                        Assert.Same(panel.InspectorFooter, host.FindControl<ContentControl>(host.FindControl<Border>("InspectorPane")!.IsVisible
                            ? "InspectorFooterHost" : "PageActionFooterHost")!.Content);
                        var apply = Assert.Single(panel.InspectorActions.GetVisualDescendants().OfType<Button>(), button =>
                            AutomationProperties.GetAutomationId(button) == "motif-apply-changes");
                        var reason = Assert.Single(panel.InspectorFooter.GetVisualDescendants().OfType<CopyableTextBlock>(), text =>
                            AutomationProperties.GetName(text) == "Why Apply is unavailable");
                        AssertReachable(apply, panel.InspectorActions);
                        AssertReachable(reason, footer);
                        LayoutAssertions.AssertCurrent(window);
                    }
                    catch (Exception failure) { failures.Add($"{state}-apply: {failure.Message}"); }
                    footer.ScrollToHome();
                    PageScreenshots.Settle(window);
                }
                File.WriteAllLines(Path.Combine(folder, "review-frame-failures.txt"), failures);
                Assert.Empty(fake.ReviewMarkRequests);
                Assert.Empty(fake.ApplyPendingRequests);
                Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));

                void Save(string state)
                {
                    using var image = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("Review frame missing.");
                    image.Save(Path.Combine(folder, state + ".png"), PngBitmapEncoderOptions.Default);
                }

                void AssertReachable(Control control, Control viewport)
                {
                    Assert.True(control.IsEffectivelyVisible);
                    var bounds = InWindow(control);
                    var clip = InWindow(viewport);
                    Assert.True(bounds.Width > 0 && bounds.Height > 0 && bounds.Left >= clip.Left - 1 &&
                        bounds.Right <= clip.Right + 1 && bounds.Top >= clip.Top - 1 && bounds.Bottom <= clip.Bottom + 1,
                        $"{control.GetType().Name} {bounds} outside viewport {clip}.");
                    Assert.True(bounds.Left >= 0 && bounds.Top >= 0 && bounds.Right <= window.ClientSize.Width + 1 &&
                        bounds.Bottom <= window.ClientSize.Height + 1, $"{control.GetType().Name} {bounds} outside window {window.ClientSize}.");
                }

                Rect InWindow(Control control) => new(control.TranslatePoint(default, window)!.Value,
                    control.TranslatePoint(new Point(control.Bounds.Width, control.Bounds.Height), window)!.Value);
            }
            finally
            {
                Application.Current.RequestedThemeVariant = prior;
                window.Close();
            }
        }, TimeSpan.FromMinutes(3));
    }
}
