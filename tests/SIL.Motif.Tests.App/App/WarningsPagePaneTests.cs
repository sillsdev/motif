using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using SIL.Motif.App;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.App.Views.Shell;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class WarningsPagePaneTests
{
    [Fact]
    public void WarningsPageExportsSeparatePanesAndKeepsItsFallback()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            var pageModel = PageModel();
            pageModel.Grammar.LoadStored("warnings-pane.fwdata", new GrammarCheckResponse(
            [new(GrammarDiagnosticLevel.Warning, "Unused affix", [], [], "The affix is not used")
                { Code = "grammar.affix.unused", Group = "Unused affix" }], HasBaseline: true));
            var page = new WarningsPage(pageModel);

            var panes = Assert.IsAssignableFrom<IWorkspacePaneProvider>(page);
            Assert.Equal("Warnings", panes.SidePanelTitle);
            var sidePanel = Assert.IsAssignableFrom<Control>(panes.SidePanel);
            var inspector = Assert.IsAssignableFrom<WarningsInspectorPanel>(panes.InspectorContent);
            Assert.NotSame(sidePanel, inspector);
            Assert.Null(panes.InspectorFooter);
            Assert.Null(sidePanel.Parent);
            Assert.Null(inspector.Parent);
            var host = WorkspaceLayoutHost.ForPage(page);
            Assert.Same(page, host.Page);
            Assert.Same(sidePanel, host.FindControl<ContentControl>("SidePanelContent")!.Content);
            Assert.Same(inspector, host.FindControl<ContentControl>("PageInspectorHost")!.Content);
            Assert.Same(pageModel, sidePanel.DataContext);
            Assert.Same(pageModel, inspector.Page);

            var fallback = Assert.IsType<GrammarPanel>(page.FindControl<ContentControl>("GrammarHost")!.Content);
            Assert.Same(pageModel.Grammar, fallback.Grammar);
            Assert.IsType<ListBox>(fallback.FindControl<ListBox>("GrammarPanelRowsItems"));
            return Task.CompletedTask;
        }, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void AreaFilterUpdatesTheWarningRowsAndInspectorFollowsTheSelectedFinding()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            var pageModel = PageModel();
            pageModel.Grammar.LoadStored("warnings-area-filter.fwdata", new GrammarCheckResponse(
            [Warning("grammar.environment.unused", "PhEnvironment", "Unused environment"),
             Warning("grammar.affix.unused", "MoAffixForm", "Unused affix")], HasBaseline: true));
            var page = new WarningsPage(pageModel);
            var panes = Assert.IsAssignableFrom<IWorkspacePaneProvider>(page);
            var sidePanel = Assert.IsAssignableFrom<Control>(panes.SidePanel);
            var inspector = Assert.IsAssignableFrom<Control>(panes.InspectorContent);
            Assert.Null(sidePanel.Parent);
            Assert.Null(inspector.Parent);
            var host = WorkspaceLayoutHost.ForPage(page);
            Assert.Same(sidePanel, host.FindControl<ContentControl>("SidePanelContent")!.Content);
            Assert.Same(inspector, host.FindControl<ContentControl>("PageInspectorHost")!.Content);
            var window = new Window { Content = host, Width = 1600, Height = 780 };
            try
            {
                window.Show();
                PageScreenshots.Settle(window);
                LayoutAssertions.AssertCurrent(window);
                Assert.Contains(sidePanel.GetVisualDescendants().OfType<FilterChip>(), chip => chip.Label == "Affixes");

                var affixes = Assert.Single(sidePanel.GetVisualDescendants().OfType<FilterChip>(),
                    chip => chip.Label == "Affixes");
                Assert.Same(pageModel.SetAreaCommand, affixes.Command);
                affixes.Command!.Execute(affixes.CommandParameter);
                PageScreenshots.Settle(window);

                var panel = Assert.Single(page.GetVisualDescendants().OfType<GrammarPanel>());
                var rows = panel.FindControl<ListBox>("GrammarPanelRowsItems")!;
                var visible = rows.ItemsSource!.Cast<object>().ToArray();
                Assert.True(pageModel.AreaFilters.Single(option => option.Area == "Affixes").IsActive);
                Assert.Equal("Affixes", Assert.IsType<DenseTableGroup>(Assert.Single(visible.OfType<DenseTableGroup>())).Label);
                var selected = Assert.IsType<GrammarWarningRowViewModel>(Assert.Single(visible.OfType<GrammarWarningRowViewModel>()));
                Assert.Equal("grammar.affix.unused", selected.GroupCode);

                rows.SelectedItem = selected;
                PageScreenshots.Settle(window);
                Assert.Same(selected, Assert.Single(pageModel.Grammar.Warnings.SelectedDetails));
                var inspectorRows = Assert.Single(inspector.GetVisualDescendants().OfType<ItemsControl>(),
                    control => control.Name == "WarningsInspectorSelectedDetails");
                Assert.Same(pageModel.Grammar.Warnings.SelectedDetails, inspectorRows.ItemsSource);
                Assert.Single(inspectorRows.ItemsSource!.Cast<object>());
                var inlineRows = panel.FindControl<ItemsControl>("GrammarPanelSelectionDetailsItems")!;
                var inlineDetailsHost = panel.FindControl<ScrollViewer>("GrammarPanelSelectionDetailsHost")!;
                Assert.False(inlineDetailsHost.IsEffectivelyVisible);
                Assert.Empty(inlineRows.ItemsSource!.Cast<object>());
                Assert.True(inspector.IsEffectivelyVisible);
            }
            finally
            {
                window.Close();
            }

            return Task.CompletedTask;
        }, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void PageBodyKeepsCountSortAndCheckWhileSemanticFiltersStayInTheSidePanel()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            var pageModel = PageModel();
            pageModel.Grammar.LoadStored("warnings-filter-location.fwdata", new GrammarCheckResponse(
            [Warning("grammar.environment.unused", "PhEnvironment", "Unused environment"),
             Warning("grammar.affix.unused", "MoAffixForm", "Unused affix")], HasBaseline: true));
            var page = new WarningsPage(pageModel);
            var filterPanel = Assert.IsType<WarningsFilterPanel>(Assert.IsAssignableFrom<IWorkspacePaneProvider>(page).SidePanel);
            var host = WorkspaceLayoutHost.ForPage(page);
            var window = new Window { Content = host, Width = 1600, Height = 780 };
            try
            {
                window.Show();
                PageScreenshots.Settle(window);
                var panel = Assert.Single(page.GetVisualDescendants().OfType<GrammarPanel>());
                var bodyControls = Assert.Single(panel.GetVisualDescendants().OfType<WrapPanel>(), controls =>
                    controls.Classes.Contains("warningControls"));
                var count = bodyControls.GetVisualDescendants().OfType<CopyableTextBlock>()
                    .SingleOrDefault(text => text.Text == pageModel.Grammar.Warnings.CountSummary);
                Assert.NotNull(count);
                Assert.Equal("2 findings", count.Text);
                Assert.True(count.IsEffectivelyVisible);
                Assert.DoesNotContain(bodyControls.GetVisualDescendants().OfType<FilterChip>(), chip =>
                    chip.IsEffectivelyVisible);
                Assert.False(Assert.Single(bodyControls.GetVisualDescendants().OfType<ToggleButton>()).IsEffectivelyVisible);

                var visibleButtons = bodyControls.GetVisualDescendants().OfType<Button>()
                    .Where(button => button.IsEffectivelyVisible)
                    .Select(button => button.Content?.ToString()).ToArray();
                Assert.Contains(pageModel.Grammar.Warnings.SortLabel, visibleButtons);
                Assert.Contains("Check the grammar again", visibleButtons);

                Assert.True(filterPanel.IsEffectivelyVisible);
                var warningFilter = Assert.Single(filterPanel.GetVisualDescendants().OfType<FilterChip>(), chip =>
                    chip.Label == "Warnings");
                Assert.True(warningFilter.IsEffectivelyVisible);
                Assert.NotNull(warningFilter.Command);
                Assert.True(Assert.Single(filterPanel.GetVisualDescendants().OfType<ToggleButton>())
                    .IsEffectivelyVisible);

                window.Width = 640;
                PageScreenshots.Settle(window);
                Assert.False(filterPanel.IsEffectivelyVisible);
                var reveal = host.FindControl<Button>("SidePanelRevealButton")!;
                Assert.True(reveal.IsEffectivelyVisible);
                host.ToggleSidePanel();
                PageScreenshots.Settle(window);
                Assert.True(filterPanel.IsEffectivelyVisible);
                Assert.True(warningFilter.IsEffectivelyVisible);
            }
            finally
            {
                window.Close();
            }

            return Task.CompletedTask;
        }, TimeSpan.FromSeconds(30));
    }

    [ScreenshotFact]
    public void CaptureWarningsPageAtFourWidthsBothThemesAndZooms()
    {
        var folder = Environment.GetEnvironmentVariable(ScreenshotFactAttribute.FolderVariable)!;
        Directory.CreateDirectory(folder);
        var states = new List<string>();
        var failures = new List<string>();

        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var response = new GrammarCheckResponse(DistinctWarnings(), HasBaseline: true);
            var (workspace, window) = await PageScreenshots.OpenOverSampleData(parse: false, configure: (fake, _) =>
            {
                fake.StoredGrammarCheckIs(response);
                fake.CheckGrammarCompletesWith(response);
            });
            var previousTheme = Application.Current!.RequestedThemeVariant;
            try
            {
                workspace.CurrentPage = WorkspacePage.Warnings;
                PageScreenshots.Settle(window);
                var page = workspace.PageModel<WarningsPageModel>();
                var warnings = page.Grammar.Warnings;
                var rows = warnings.Rows.Cast<GrammarWarningRowViewModel>().ToArray();
                Assert.Equal(24, warnings.TotalCount);
                Assert.Equal(24, rows.Length);
                Assert.Equal(24, rows.Sum(row => row.RepeatCount));

                var panel = Assert.Single(window.GetVisualDescendants().OfType<GrammarPanel>());
                var list = panel.FindControl<ListBox>("GrammarPanelRowsItems")!;
                var layoutHost = Assert.IsType<WorkspaceLayoutHost>(window.FindControl<WorkspaceLayoutHost>("WorkspaceLayout"));
                var inspectorPane = layoutHost.FindControl<Border>("InspectorPane")!;
                var inspectorStrip = layoutHost.FindControl<Button>("InspectorStrip")!;
                var inspectorHost = layoutHost.FindControl<ContentControl>("PageInspectorHost")!;
                var selected = rows[0];
                list.SelectedItem = selected;
                list.ScrollIntoView(selected);
                PageScreenshots.Settle(window);
                var settings = SettingsTestAccess.Model(window);
                foreach (var (themeName, theme) in new[] { ("light", ThemeVariant.Light), ("dark", ThemeVariant.Dark) })
                foreach (var zoom in new[] { 100, 150 })
                foreach (var (width, height) in new[] { (640, 600), (960, 780), (1920, 1080), (960, 600) })
                {
                    Application.Current.RequestedThemeVariant = theme;
                    settings.ZoomPercent = zoom;
                    window.Width = width;
                    window.Height = height;
                    list.SelectedItem = selected;
                    list.ScrollIntoView(selected);
                    PageScreenshots.Settle(window);
                    var inspectorVisible = inspectorPane.IsVisible;
                    Assert.Equal(inspectorVisible, inspectorHost.IsEffectivelyVisible);
                    Assert.Equal(!inspectorVisible, inspectorStrip.IsVisible);
                    var inspector = Assert.IsType<WarningsInspectorPanel>(inspectorHost.Content);
                    var inspectorRows = inspector.FindControl<ItemsControl>("WarningsInspectorSelectedDetails")!;
                    Assert.Equal(24, rows.Sum(row => row.RepeatCount));
                    Assert.Same(selected, Assert.Single(inspectorRows.ItemsSource!.Cast<object>()));
                    var state = $"warnings-page-{width}x{height}-{themeName}-{zoom}";
                    PageScreenshots.Settle(window);
                    Assert.Equal(width, window.ClientSize.Width);
                    Assert.Equal(height, window.ClientSize.Height);
                    using var frame = window.CaptureRenderedFrame() ??
                        throw new InvalidOperationException($"No frame rendered for {state}.");
                    frame.Save(Path.Combine(folder, state + ".png"), PngBitmapEncoderOptions.Default);
                    try { LayoutAssertions.AssertCurrent(window); }
                    catch (Xunit.Sdk.XunitException failure) { failures.Add($"{state}: {failure.Message}"); }
                    states.Add($"{state}.png\t24 distinct findings, 24 displayed rows, one selected detail; Inspector {(inspectorVisible ? "visible" : "collapsed") }.");
                }

                File.WriteAllLines(Path.Combine(folder, "warnings-page-capture-states.txt"), states);
                File.WriteAllLines(Path.Combine(folder, "warnings-page-capture-failures.txt"), failures);
                Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
            }
            finally
            {
                Application.Current!.RequestedThemeVariant = previousTheme;
                WindowZoomPolicy.Apply(Application.Current, ZoomPolicy.DefaultPercent);
                window.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromMinutes(4));
    }

    [Fact]
    public void ExplicitDetailsActionRevealsTheInspectorWhileSelectionAloneKeepsItCollapsed()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            var pageModel = PageModel();
            pageModel.Grammar.LoadStored("warnings-explicit-details.fwdata", new GrammarCheckResponse(
                [Warning("grammar.affix.unused", "MoAffixForm", "Unused affix")], HasBaseline: true));
            var page = new WarningsPage(pageModel);
            var host = WorkspaceLayoutHost.ForPage(page);
            var inspectorPane = host.FindControl<Border>("InspectorPane")!;
            var pageInspector = host.FindControl<ContentControl>("PageInspectorHost")!;
            var window = new Window { Content = host, Width = 640, Height = 780 };
            try
            {
                window.Show();
                PageScreenshots.Settle(window);
                Assert.False(inspectorPane.IsVisible);
                Assert.False(pageInspector.IsEffectivelyVisible);

                var panel = Assert.Single(page.GetVisualDescendants().OfType<GrammarPanel>());
                var rows = panel.FindControl<ListBox>("GrammarPanelRowsItems")!;
                var selected = Assert.Single(rows.ItemsSource!.Cast<object>().OfType<GrammarWarningRowViewModel>());
                rows.SelectedItem = selected;
                PageScreenshots.Settle(window);
                Assert.False(inspectorPane.IsVisible);
                Assert.False(pageInspector.IsEffectivelyVisible);

                var details = Assert.Single(rows.GetVisualDescendants().OfType<Button>(),
                    button => button.Content?.ToString() == "Details");
                Assert.Same(selected, details.CommandParameter);
                Assert.NotNull(details.Command);
                details.Command!.Execute(details.CommandParameter);
                PageScreenshots.Settle(window);

                Assert.True(inspectorPane.IsVisible);
                Assert.True(pageInspector.IsEffectivelyVisible);
                var inspector = Assert.IsType<WarningsInspectorPanel>(pageInspector.Content);
                var detailRows = inspector.FindControl<ItemsControl>("WarningsInspectorSelectedDetails")!;
                Assert.Same(selected, Assert.Single(detailRows.ItemsSource!.Cast<object>()));
                var inlineDetails = panel.FindControl<ScrollViewer>("GrammarPanelSelectionDetailsHost")!;
                Assert.False(inlineDetails.IsEffectivelyVisible);
            }
            finally
            {
                window.Close();
            }

            return Task.CompletedTask;
        }, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void FilterAndInspectorPanesStayHiddenForEmptyAndRefusedReports()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            var pageModel = PageModel();
            var page = new WarningsPage(pageModel);
            var panes = Assert.IsAssignableFrom<IWorkspacePaneProvider>(page);
            var sidePanel = Assert.IsAssignableFrom<Control>(panes.SidePanel);
            var inspector = Assert.IsAssignableFrom<Control>(panes.InspectorContent);
            var host = WorkspaceLayoutHost.ForPage(page);
            Assert.Same(sidePanel, host.FindControl<ContentControl>("SidePanelContent")!.Content);
            Assert.Same(inspector, host.FindControl<ContentControl>("PageInspectorHost")!.Content);
            var window = new Window { Content = host, Width = 1600, Height = 780 };
            try
            {
                pageModel.Grammar.LoadStored("warnings-empty.fwdata", new GrammarCheckResponse([], HasBaseline: true));
                window.Show();
                PageScreenshots.Settle(window);
                Assert.False(sidePanel.IsVisible);
                Assert.False(inspector.IsVisible);

                pageModel.Grammar.LoadStored("warnings-refused.fwdata", null,
                    new Refusal("grammar.check.refused", FailureReason.Refused, "The grammar could not be checked."));
                PageScreenshots.Settle(window);
                Assert.True(pageModel.Grammar.ShowRefused);
                Assert.False(sidePanel.IsVisible);
                Assert.False(inspector.IsVisible);
            }
            finally
            {
                window.Close();
            }

            return Task.CompletedTask;
        }, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void FilterAndInspectorPanesStayHiddenWhileTheGrammarCheckLoads()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var completion = new TaskCompletionSource<CommandOutcome<GrammarCheckResponse>>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var fake = new FakeCommandClient();
            fake.OnCheckGrammar((_, _) => completion.Task);
            var pageModel = new WarningsPageModel(WorkspaceContextTests.NewContext(fake));
            var check = pageModel.Grammar.SetProjectAsync("warnings-loading.fwdata");
            var page = new WarningsPage(pageModel);
            var panes = Assert.IsAssignableFrom<IWorkspacePaneProvider>(page);
            var sidePanel = Assert.IsAssignableFrom<Control>(panes.SidePanel);
            var inspector = Assert.IsAssignableFrom<Control>(panes.InspectorContent);
            var host = WorkspaceLayoutHost.ForPage(page);
            Assert.Same(sidePanel, host.FindControl<ContentControl>("SidePanelContent")!.Content);
            Assert.Same(inspector, host.FindControl<ContentControl>("PageInspectorHost")!.Content);
            var window = new Window { Content = host, Width = 1600, Height = 780 };
            try
            {
                window.Show();
                PageScreenshots.Settle(window);
                Assert.True(pageModel.Grammar.ShowLoading);
                Assert.False(sidePanel.IsVisible);
                Assert.False(inspector.IsVisible);

                completion.SetResult(CommandOutcome<GrammarCheckResponse>.Success(new GrammarCheckResponse(
                    [Warning("grammar.affix.unused", "MoAffixForm", "Unused affix")], HasBaseline: true)));
                await check;
                PageScreenshots.Settle(window);
                Assert.True(sidePanel.IsVisible);
                Assert.True(inspector.IsVisible);
            }
            finally
            {
                window.Close();
            }
        }, TimeSpan.FromSeconds(30));
    }

    private static GrammarWarning Warning(string code, string fieldWorksKind, string description) =>
        new(GrammarDiagnosticLevel.Warning, description,
            [new("unused", GrammarWarningPartRole.Object, Guid.NewGuid().ToString("N"), fieldWorksKind)],
            [new(description, GrammarWarningPartRole.Text)], description)
        {
            Code = code,
            Group = description,
            Description = description,
        };

    private static GrammarWarning[] DistinctWarnings() => Enumerable.Range(1, 24).Select(index =>
    {
        var fieldWorksKind = (index % 4) switch
        {
            0 => "PhEnvironment",
            1 => "MoAffixForm",
            2 => "PhPhonRule",
            _ => "LexEntry",
        };
        var family = fieldWorksKind switch
        {
            "PhEnvironment" => "environment",
            "MoAffixForm" => "affix",
            "PhPhonRule" => "rule",
            _ => "entry",
        };
        var severity = (index % 3) switch
        {
            0 => GrammarDiagnosticLevel.Error,
            1 => GrammarDiagnosticLevel.Warning,
            _ => GrammarDiagnosticLevel.Information,
        };
        var title = $"{fieldWorksKind} finding {index:D2}";
        var description = $"This distinct finding explains the issue on resource {index:D2}.";
        return new GrammarWarning(severity, title,
            [new($"Resource {index:D2}", GrammarWarningPartRole.Object,
                new Guid(index, 0, 0, new byte[8]).ToString("D"), fieldWorksKind)],
            [new(description, GrammarWarningPartRole.Text)], description)
        {
            Code = $"grammar.{family}.capture-{index:D2}",
            Group = title,
            Title = title,
            Description = description,
            Explanation = $"PanGloss reports a distinct condition for resource {index:D2}.",
            Guidance = $"Review resource {index:D2} in FieldWorks and check the affected forms.",
        };
    }).ToArray();

    private static WarningsPageModel PageModel()
    {
        return new WarningsPageModel(WorkspaceContextTests.NewContext());
    }
}
