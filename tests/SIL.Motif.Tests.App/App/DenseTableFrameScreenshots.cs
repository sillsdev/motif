using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using SIL.Motif.App;
using SIL.Motif.App.Controls;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.App.Views.Shell;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.App.Walkthrough;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
[Trait("MotifTestLevel", "System")]
public sealed class DenseTableFrameScreenshots
{
    [ScreenshotFact]
    public void WarningsActionsFitAndActivateInTheActualFrameAtEveryViewport()
    {
        var folder = Environment.GetEnvironmentVariable(ScreenshotFactAttribute.FolderVariable)!;
        Directory.CreateDirectory(folder);
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await PageScreenshots.OpenOverSampleData(parse: false);
            var previousTheme = Application.Current!.RequestedThemeVariant;
            var failures = new List<string>();
            try
            {
                workspace.CurrentPage = WorkspacePage.Warnings;
                PageScreenshots.Settle(window);
                var settings = SettingsTestAccess.Model(window);
                var page = workspace.PageModel<WarningsPageModel>();
                var panel = Assert.Single(window.GetVisualDescendants().OfType<GrammarPanel>());
                var list = panel.FindControl<ListBox>("GrammarPanelRowsItems")!;
                var selected = page.Grammar.Warnings.TableEntries.OfType<GrammarWarningRowViewModel>().First();
                list.SelectedItem = selected;
                foreach (var (themeName, theme) in new[] { ("light", ThemeVariant.Light), ("dark", ThemeVariant.Dark) })
                foreach (var zoom in new[] { 100, 150 })
                foreach (var (name, width, height) in new[]
                         { ("thin", 640, 600), ("half", 960, 780), ("full", 1920, 1080), ("short", 960, 600) })
                {
                    Application.Current.RequestedThemeVariant = theme;
                    settings.ZoomPercent = zoom;
                    window.Width = width;
                    window.Height = height;
                    list.ScrollIntoView(selected);
                    PageScreenshots.Settle(window);
                    var state = $"warnings-frame-{name}-{width}x{height}-{themeName}-{zoom}";
                    using var frame = window.CaptureRenderedFrame() ??
                        throw new InvalidOperationException($"No frame rendered for {state}.");
                    frame.Save(Path.Combine(folder, state + ".png"), PngBitmapEncoderOptions.Default);
                    try
                    {
                        Assert.Equal(width, window.ClientSize.Width);
                        Assert.Equal(height, window.ClientSize.Height);
                        var row = Assert.IsType<ListBoxItem>(list.ContainerFromItem(selected));
                        var tracks = Assert.Single(row.GetVisualDescendants().OfType<Grid>(),
                            grid => DenseTable.GetColumns(grid) is not null);
                        var primaryWidth = tracks.ColumnDefinitions[1].ActualWidth;
                        var readableWidth = Assert.IsType<double>(Application.Current.FindResource(
                            "Component.DenseTable.Column.NarrowWidth"));
                        Assert.True(primaryWidth >= readableWidth,
                            $"The finding's identity has {primaryWidth:0.#} DIP; at least {readableWidth:0.#} DIP is needed.");
                        var actions = row.GetVisualDescendants().OfType<Button>().Where(button =>
                            button.Classes.Contains("denseTableAction") || button.Classes.Contains("denseTableMenu")).ToArray();
                        Assert.Equal(2, actions.Length);
                        foreach (var action in actions) AssertActionFits(action, list, window);
                        LayoutAssertions.AssertCurrent(window);
                    }
                    catch (Exception failure)
                    {
                        failures.Add($"{state}: {failure.Message}");
                    }
                }
                File.WriteAllLines(Path.Combine(folder, "warnings-frame-failures.txt"), failures);
                Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));

                var host = window.FindInWindowOrWorkspace<WorkspaceLayoutHost>("WorkspaceLayout")!;
                foreach (var (themeName, theme) in new[] { ("light", ThemeVariant.Light), ("dark", ThemeVariant.Dark) })
                foreach (var objectOpen in new[] { false, true })
                {
                    Application.Current.RequestedThemeVariant = theme;
                    settings.ZoomPercent = 150;
                    window.Width = objectOpen ? 1920 : 640;
                    window.Height = objectOpen ? 1080 : 600;
                    list.ScrollIntoView(selected);
                    PageScreenshots.Settle(window);
                    var state = $"warnings-details-{(objectOpen ? "object" : "compact")}-{themeName}-150";
                    if (objectOpen)
                    {
                        var morph = PageScreenshots.SampleMorph("wa-");
                        workspace.Context.OpenInspector(InspectorSubject.Morpheme(
                            morph.AllomorphId, morph.GrammaticalInfoId, morph.Form, "3PL")!);
                        await workspace.Inspector.Loading;
                        PageScreenshots.Settle(window);
                        Assert.True(workspace.Inspector.IsOpen);
                        Assert.True(host.FindControl<ContentControl>("ObjectInspectorHost")!.IsEffectivelyVisible);
                        Assert.False(host.FindControl<ContentControl>("PageInspectorHost")!.IsEffectivelyVisible);
                    }
                    else
                        Assert.False(host.FindControl<Border>("InspectorPane")!.IsEffectivelyVisible);
                    CaptureCurrent(window, folder, state + "-before");
                    var selectedRow = Assert.IsType<ListBoxItem>(list.ContainerFromItem(selected));
                    var details = Assert.Single(selectedRow.GetVisualDescendants().OfType<Button>(),
                        button => button.Classes.Contains("denseTableAction"));
                    HeadlessClick.Click(window, details, "Selected warning details");
                    PageScreenshots.Settle(window);
                    CaptureCurrent(window, folder, state + "-after");
                    try
                    {
                        Assert.Same(selected, page.Grammar.Warnings.SelectedTableEntry);
                        Assert.True(selected.IsOpen);
                        Assert.Same(selected, Assert.Single(page.Grammar.Warnings.SelectedDetails));
                        Assert.False(workspace.Inspector.IsOpen);
                        Assert.Null(workspace.Context.Inspector);
                        Assert.True(host.FindControl<Border>("InspectorPane")!.IsEffectivelyVisible);
                        Assert.False(host.FindControl<ContentControl>("ObjectInspectorHost")!.IsEffectivelyVisible);
                        var pageInspector = host.FindControl<ContentControl>("PageInspectorHost")!;
                        Assert.True(pageInspector.IsEffectivelyVisible);
                        var inspector = Assert.IsType<WarningsInspectorPanel>(pageInspector.Content);
                        var shown = inspector.FindControl<ItemsControl>("WarningsInspectorSelectedDetails")!;
                        Assert.Same(selected, Assert.Single(shown.Items));
                        var title = Assert.Single(shown.GetVisualDescendants().OfType<CopyableTextBlock>(),
                            text => ReferenceEquals(text.DataContext, selected) && text.Text == selected.TableItemText);
                        Assert.True(title.IsEffectivelyVisible);
                        var top = title.TranslatePoint(default, window)!.Value;
                        var bottom = title.TranslatePoint(new Point(title.Bounds.Width, title.Bounds.Height), window)!.Value;
                        Assert.True(title.Bounds.Width > 0 && title.Bounds.Height > 0);
                        Assert.True(top.X >= 0 && top.Y >= 0 && bottom.X <= window.ClientSize.Width + 1 &&
                            bottom.Y <= window.ClientSize.Height + 1, $"Selected finding title is outside the client: {top}..{bottom}.");
                        var center = title.TranslatePoint(new Point(title.TextLayout.Width / 2, title.Bounds.Height / 2), window)!.Value;
                        var hit = Assert.IsAssignableFrom<Visual>(window.InputHitTest(center));
                        Assert.Contains(title, hit.GetSelfAndVisualAncestors());
                    }
                    catch (Exception failure)
                    {
                        failures.Add($"{state}: {failure.Message}");
                    }
                }
                File.WriteAllLines(Path.Combine(folder, "warnings-details-failures.txt"), failures);
                Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
            }
            finally
            {
                Application.Current.RequestedThemeVariant = previousTheme;
                WindowZoomPolicy.Apply(Application.Current, ZoomPolicy.DefaultPercent);
                window.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromMinutes(4));
    }

    private static void CaptureCurrent(Window window, string folder, string state)
    {
        using var frame = window.CaptureRenderedFrame() ??
            throw new InvalidOperationException($"No frame rendered for {state}.");
        frame.Save(Path.Combine(folder, state + ".png"), PngBitmapEncoderOptions.Default);
    }

    private static void AssertActionFits(Button action, Control viewport, Window window)
    {
        Assert.True(action.IsEffectivelyVisible);
        Assert.True(action.IsEnabled);
        var bounds = new Rect(action.TranslatePoint(default, viewport)!.Value, action.Bounds.Size);
        Assert.True(bounds.Left >= 0 && bounds.Right <= viewport.Bounds.Width,
            $"{action.Content}: action {bounds} exceeds list width {viewport.Bounds.Width}.");
        var center = action.TranslatePoint(new Point(action.Bounds.Width / 2, action.Bounds.Height / 2), window)!.Value;
        var hit = Assert.IsAssignableFrom<Visual>(window.InputHitTest(center));
        Assert.Contains(action, hit.GetSelfAndVisualAncestors());
    }
}
