using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using SIL.Motif.App;
using SIL.Motif.App.Controls;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.App.Views.Shell;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.App.Walkthrough;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
[Trait("MotifTestLevel", "Unit")]
public sealed class ParsimonyWorkspacePaneTests
{
    [Theory]
    [InlineData("details")]
    [InlineData("ask")]
    public void SelectingAFindingKeepsPanesCollapsedUntilAnExplicitRequest(string action)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await PageScreenshots.OpenOverSampleData(parse: false, advancedAiModeEnabled: true,
                configure: (fake, _) => ParsimonyStateScreenshots.Configure(fake, "dense-review"));
            try
            {
                workspace.CurrentPage = WorkspacePage.Parsimony;
                window.Width = 640;
                window.Height = 600;
                PageScreenshots.Settle(window);
                var model = workspace.PageModel<ParsimonyPageModel>();
                await ParsimonyStateScreenshots.DriveDenseReviewAsync(model);
                var active = model.Rows.First(row => !row.IsHeader && row.Items == "ka- + -ni");
                var asked = model.Rows.First(row => !row.IsHeader && row.Items == "ma- + -tu");

                var host = window.FindInWindowOrWorkspace<WorkspaceLayoutHost>("WorkspaceLayout")!;
                var page = window.PageView<ParsimonyPage>();
                Assert.False(host.FindControl<Border>("SidePanelBorder")!.IsVisible);
                Assert.False(host.FindControl<Border>("InspectorPane")!.IsVisible);
                var list = page.FindControl<ListBox>("ParsimonyRowsItems")!;
                list.SelectedItem = asked;
                PageScreenshots.Settle(window);
                Assert.Same(asked, model.SelectedTableEntry);
                Assert.False(host.FindControl<Border>("InspectorPane")!.IsVisible);
                list.SelectedItem = active;
                PageScreenshots.Settle(window);
                Assert.Same(active, model.SelectedTableEntry);
                model.QuestionText = "Does the finding's 2-of-4 count include this named prohibition?";

                var row = Assert.Single(list.GetVisualDescendants().OfType<ListBoxItem>(),
                    item => ReferenceEquals(item.DataContext, active));
                Flyout? detailsFlyout = null;
                if (action == "details")
                {
                    var menu = Assert.Single(row.GetVisualDescendants().OfType<Button>(),
                        button => button.Classes.Contains("denseTableMenu"));
                    detailsFlyout = Assert.IsType<Flyout>(menu.Flyout);
                    detailsFlyout.ShowAt(menu);
                    PageScreenshots.Settle(window);
                    var details = Assert.Single(Assert.IsAssignableFrom<Control>(detailsFlyout.Content)
                        .GetVisualDescendants().OfType<Button>(), button => button.Content?.ToString() == "Details");
                    HeadlessClick.Click(window, details, "Details for ka- + -ni");
                }
                else
                {
                    var ask = Assert.Single(row.GetVisualDescendants().OfType<Button>(),
                        button => button.Content?.ToString() == "Ask");
                    ask.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
                }

                PageScreenshots.Settle(window);
                Assert.True(host.FindControl<Border>("InspectorPane")!.IsVisible);
                Assert.True(host.FindControl<ContentControl>("PageInspectorHost")!.IsVisible);
                if (action == "details")
                {
                    Assert.False(detailsFlyout!.IsOpen);
                    Assert.Equal("Does the finding's 2-of-4 count include this named prohibition?", model.QuestionText);
                    Assert.Equal("ka- + -ni", model.Evidence!.Details.Single(detail => detail.Label == "Items").Value);
                }

                HeadlessClick.Click(window, host.FindControl<Button>("InspectorHideButton")!, "Hide Inspector");
                PageScreenshots.Settle(window);
                Assert.False(host.FindControl<Border>("InspectorPane")!.IsVisible);
                Assert.False(host.FindControl<ContentControl>("PageInspectorHost")!.IsEffectivelyVisible);
                Assert.Same(active, model.SelectedTableEntry);
                Assert.Equal("Does the finding's 2-of-4 count include this named prohibition?", model.QuestionText);
            }
            finally
            {
                window.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromMinutes(3));
    }

    [Fact]
    public void SuppressedDecisionsUseDenseRowsWithAlwaysVisibleReturn()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await PageScreenshots.OpenOverSampleData(parse: false, composeWindow: false, advancedAiModeEnabled: true,
                configure: (fake, _) => ParsimonyStateScreenshots.Configure(fake, "suppressed-with-reason"));
            var resultWindow = new MotifTestWindow { Width = 1280, Height = 820 };
            try
            {
                workspace.CurrentPage = WorkspacePage.Parsimony;
                var model = workspace.PageModel<ParsimonyPageModel>();
                var page = new ParsimonyPage(model);
                resultWindow.Content = WorkspaceLayoutHost.ForPage(page);
                resultWindow.Show();
                model.IsShowingSuppressed = true;
                PageScreenshots.Settle(resultWindow);

                var list = Assert.IsType<ListBox>(page.FindControl<ListBox>("ParsimonySuppressedItems"));
                Assert.Contains("denseTable", list.Classes);
                Assert.Same(model.SuppressedItems, list.ItemsSource);
                Assert.IsType<VirtualizingStackPanel>(list.ItemsPanelRoot);
                var header = Assert.Single(page.GetVisualDescendants().OfType<Border>(),
                    border => border.IsEffectivelyVisible && border.Classes.Contains("denseTableColumnHeader"));
                var headerGrid = Assert.Single(header.GetVisualDescendants().OfType<Grid>(),
                    grid => DenseTable.GetColumns(grid) is not null);
                var rows = list.GetVisualDescendants().OfType<ListBoxItem>().ToArray();
                Assert.Equal(model.SuppressedItems.Count, rows.Length);
                Assert.NotEmpty(rows);
                Assert.All(rows, container =>
                {
                    var item = Assert.IsType<ParsimonySuppressedItem>(container.DataContext);
                    var rowBorder = Assert.Single(container.GetVisualDescendants().OfType<Border>(),
                        border => border.Classes.Contains("denseTableRow"));
                    Assert.Equal(item.AutomationId, AutomationProperties.GetAutomationId(rowBorder));
                    var rowGrid = Assert.Single(rowBorder.GetVisualDescendants().OfType<Grid>(),
                        grid => DenseTable.GetColumns(grid) is not null);
                    Assert.Equal(DenseTable.GetColumns(headerGrid), DenseTable.GetColumns(rowGrid));
                    Assert.Equal(DenseTable.GetCompactColumns(headerGrid), DenseTable.GetCompactColumns(rowGrid));
                    var buttons = container.GetVisualDescendants().OfType<Button>().ToArray();
                    var action = Assert.Single(buttons, button => button.Content?.ToString() == "Return");
                    Assert.True(action.IsEffectivelyVisible);
                    Assert.True(action.IsTabStop);
                    Assert.Equal(item.ReturnAutomationId, AutomationProperties.GetAutomationId(action));
                    var menu = Assert.Single(buttons, button => button.Classes.Contains("denseTableMenu"));
                    Assert.True(menu.IsEffectivelyVisible);
                    var flyout = Assert.IsType<Flyout>(menu.Flyout);
                    flyout.ShowAt(menu);
                    PageScreenshots.Settle(resultWindow);
                    Assert.Equal(new[] { "Details", "Return to Active" }, Assert.IsAssignableFrom<Control>(flyout.Content)
                        .GetVisualDescendants().OfType<Button>().Select(button => button.Content?.ToString()));
                    flyout.Hide();
                });

                var chosen = model.SuppressedItems[0];
                list.SelectedItem = chosen;
                PageScreenshots.Settle(resultWindow);
                Assert.True(chosen.IsSelected);
                Assert.Same(chosen, model.SelectedSuppressedEntry);
                Assert.True(model.ShowSuppressedActions);
                Assert.Contains(model.Evidence!.Details, detail => detail.Label == "Reason");

                resultWindow.Width = 500;
                PageScreenshots.Settle(resultWindow);
                var cells = list.GetVisualDescendants().OfType<Control>().Where(DenseTable.GetSecondary).ToArray();
                Assert.NotEmpty(cells);
                Assert.All(cells, cell => Assert.False(cell.IsVisible));
                Assert.All(list.GetVisualDescendants().OfType<Button>().Where(button =>
                        button.Content?.ToString() == "Return"),
                    button => Assert.True(button.IsEffectivelyVisible));
            }
            finally
            {
                resultWindow.Close();
                window.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromMinutes(3));
    }

    [Fact]
    public void ActiveFindingsUseGroupedDenseRowsWithAlwaysVisibleDecisions()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await PageScreenshots.OpenOverSampleData(parse: false, composeWindow: false, advancedAiModeEnabled: true,
                configure: (fake, _) => ParsimonyStateScreenshots.Configure(fake, "listed-evidence"));
            var resultWindow = new MotifTestWindow { Width = 1280, Height = 820 };
            try
            {
                workspace.CurrentPage = WorkspacePage.Parsimony;
                var model = workspace.PageModel<ParsimonyPageModel>();
                var page = new ParsimonyPage(model);
                resultWindow.Content = WorkspaceLayoutHost.ForPage(page);
                resultWindow.Show();
                PageScreenshots.Settle(resultWindow);

                var list = Assert.IsType<ListBox>(page.FindControl<ListBox>("ParsimonyRowsItems"));
                Assert.Contains("denseTable", list.Classes);
                Assert.Same(model.TableEntries, list.ItemsSource);
                Assert.Equal(model.Groups.Select(group => group.Title), model.TableEntries
                    .OfType<DenseTableGroup>().Select(group => group.Label));
                Assert.Equal(model.Groups.Select(group => group.CountLabel), model.TableEntries
                    .OfType<DenseTableGroup>().Select(group => group.CountText));
                Assert.IsType<VirtualizingStackPanel>(list.ItemsPanelRoot);

                var header = Assert.Single(page.GetVisualDescendants().OfType<Border>(),
                    border => border.IsEffectivelyVisible && border.Classes.Contains("denseTableColumnHeader"));
                var headerGrid = Assert.Single(header.GetVisualDescendants().OfType<Grid>(),
                    grid => DenseTable.GetColumns(grid) is not null);
                var wideColumns = DenseTable.GetColumns(headerGrid)!;
                var compactColumns = DenseTable.GetCompactColumns(headerGrid)!;
                Assert.Equal(6, wideColumns.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length);
                Assert.Equal(6, compactColumns.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length);
                Assert.Contains("Component.Parsimony.Column.DecisionsWidth", wideColumns, StringComparison.Ordinal);
                Assert.Contains("Component.DenseTable.MenuSize", wideColumns, StringComparison.Ordinal);
                var app = Application.Current!;
                var compactThreshold = Assert.IsType<double>(app.FindResource("Component.DenseTable.CompactThreshold"));
                var decisionsWidth = Assert.IsType<double>(app.FindResource("Component.Parsimony.Column.DecisionsWidth"));
                var menuWidth = Assert.IsType<double>(app.FindResource("Component.DenseTable.MenuSize"));
                var widthHost = Assert.Single(headerGrid.GetVisualAncestors().OfType<Control>(),
                    DenseTable.GetIsWidthHost);
                var rows = list.GetVisualDescendants().OfType<ListBoxItem>()
                    .Where(container => container.DataContext is ParsimonyListRow { IsHeader: false }).ToArray();
                Assert.Equal(model.TableEntries.OfType<ParsimonyListRow>().Count(), rows.Length);
                Assert.All(rows, container =>
                {
                    var rowModel = Assert.IsType<ParsimonyListRow>(container.DataContext);
                    Assert.True(container.Focusable);
                    Assert.True(container.IsHitTestVisible);
                    var rowBorder = Assert.Single(container.GetVisualDescendants().OfType<Border>(),
                        border => border.Classes.Contains("denseTableRow"));
                    var rowGrid = Assert.Single(rowBorder.GetVisualDescendants().OfType<Grid>(),
                        grid => DenseTable.GetColumns(grid) is not null);
                    Assert.Equal(wideColumns, DenseTable.GetColumns(rowGrid));
                    Assert.Equal(compactColumns, DenseTable.GetCompactColumns(rowGrid));
                    Assert.NotEmpty(rowModel.StrengthOfEvidence);
                    Assert.NotEmpty(rowModel.PairedParserCheck);

                    var buttons = container.GetVisualDescendants().OfType<Button>().ToArray();
                    foreach (var name in new[] { "Keep", "Defer", "Ask" })
                    {
                        var action = Assert.Single(buttons, button => button.Content?.ToString() == name);
                        Assert.True(action.IsEffectivelyVisible);
                        Assert.True(action.IsTabStop);
                    }
                    var menu = Assert.Single(buttons, button => button.Classes.Contains("denseTableMenu"));
                    Assert.True(menu.IsEffectivelyVisible);
                    Assert.True(menu.IsTabStop);
                    var flyout = Assert.IsType<Flyout>(menu.Flyout);
                    flyout.ShowAt(menu);
                    PageScreenshots.Settle(resultWindow);
                    var menuContent = Assert.IsAssignableFrom<Control>(flyout.Content);
                    Assert.Equal(new[] { "Details", "Keep", "Defer", "Ask" }, menuContent.GetVisualDescendants()
                        .OfType<Button>().Select(button => button.Content?.ToString()));
                    flyout.Hide();
                });

                var selected = Assert.IsType<ParsimonyListRow>(rows[0].DataContext);
                model.SelectedTableEntry = selected;
                Assert.Same(selected, model.SelectedTableEntry);
                Assert.True(selected.IsSelected);
                Assert.True(model.HasEvidence);
                Assert.Contains(model.Evidence!.Details, detail => detail.Label == "Items" && detail.Value == selected.Items);
                Assert.Contains(model.Evidence.Details, detail =>
                    detail.Label == MotifAppStrings.ParsimonyDetailStrength && detail.Value == selected.StrengthOfEvidence);
                Assert.Contains(model.Evidence.Details, detail =>
                    detail.Label == MotifAppStrings.ParsimonyDetailPairedCheck && detail.Value == selected.PairedParserCheck);
                Assert.Contains(resultWindow.GetVisualDescendants().OfType<ComboBox>(), combo =>
                    AutomationProperties.GetName(combo) == "Notebook record type" && combo.IsEffectivelyVisible);
                Assert.Contains(resultWindow.GetVisualDescendants().OfType<TextBox>(), input =>
                    AutomationProperties.GetName(input) == "Reason" && input.IsEffectivelyVisible);
                Assert.Contains(resultWindow.GetVisualDescendants().OfType<TextBox>(), input =>
                    AutomationProperties.GetName(input) == "Question" && input.IsEffectivelyVisible);

                await selected.KeepCommand!.ExecuteAsync(null);
                Assert.Contains("staged", selected.Disposition, StringComparison.OrdinalIgnoreCase);
                Assert.Contains(selected, model.Rows);
                model.SelectDecisionFilterCommand.Execute("keep");
                Assert.Contains(selected, model.Rows);
                Assert.True(model.HasEvidence);
                Assert.Contains(resultWindow.GetVisualDescendants().OfType<CopyableTextBlock>(), text =>
                    text.IsEffectivelyVisible && text.Text == model.Evidence!.Title);

                resultWindow.Width = 500;
                PageScreenshots.Settle(resultWindow);
                Assert.True(widthHost.Bounds.Width < compactThreshold);
                var secondaryCells = widthHost.GetVisualDescendants().OfType<Control>()
                    .Where(DenseTable.GetSecondary).ToArray();
                Assert.NotEmpty(secondaryCells);
                Assert.All(secondaryCells, cell => Assert.False(cell.IsVisible));
                Assert.Equal(0, headerGrid.ColumnDefinitions[1].Width.Value);
                Assert.Equal(0, headerGrid.ColumnDefinitions[2].Width.Value);
                Assert.Equal(0, headerGrid.ColumnDefinitions[3].Width.Value);
                Assert.Equal(decisionsWidth, headerGrid.ColumnDefinitions[4].Width.Value);
                Assert.Equal(menuWidth, headerGrid.ColumnDefinitions[5].Width.Value);
                Assert.All(rows, row => Assert.All(row.GetVisualDescendants().OfType<Button>()
                    .Where(button => button.Content?.ToString() is "Keep" or "Defer" or "Ask"),
                    action => Assert.True(action.IsEffectivelyVisible)));
            }
            finally
            {
                resultWindow.Close();
                window.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromMinutes(3));
    }

    [Fact]
    public void ForPageHostsParsimonyPanesAndProviderGettersKeepTheirParents()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await PageScreenshots.OpenOverSampleData(parse: false, composeWindow: false, advancedAiModeEnabled: true,
                configure: (fake, _) => ParsimonyStateScreenshots.Configure(fake, "listed-evidence"));
            var hostWindow = new MotifTestWindow { Width = 1300, Height = 820 };
            try
            {
                workspace.CurrentPage = WorkspacePage.Parsimony;
                var model = workspace.PageModel<ParsimonyPageModel>();
                Assert.True(model.SelectFinding(model.Rows.First(row => !row.IsHeader).Link!));
                PageScreenshots.Settle(window);

                var page = new ParsimonyPage(model);
                var panes = Assert.IsAssignableFrom<IWorkspacePaneProvider>(page);
                var sidePanel = panes.SidePanel!;
                var inspector = panes.InspectorContent!;

                Assert.Equal("Parsimony", panes.SidePanelTitle);
                Assert.Same(model, sidePanel.DataContext);
                Assert.Same(model, inspector.DataContext);
                Assert.Null(sidePanel.Parent);
                Assert.Null(inspector.Parent);

                var host = WorkspaceLayoutHost.ForPage(page);
                hostWindow.Content = host;
                hostWindow.Show();
                PageScreenshots.Settle(hostWindow);

                Assert.Same(page, host.Page);
                Assert.Same(sidePanel, host.FindControl<ContentControl>("SidePanelContent")!.Content);
                Assert.Same(inspector, host.FindControl<ContentControl>("PageInspectorHost")!.Content);
                Assert.Contains(hostWindow.GetVisualDescendants().OfType<Button>(), button =>
                    button.IsEffectivelyVisible && AutomationProperties.GetAutomationId(button) == AutomationIds.ParsimonyActiveTab);
                Assert.Contains(hostWindow.GetVisualDescendants().OfType<Button>(), button =>
                    button.IsEffectivelyVisible && ChoiceText(button) == "All findings (2)");
                Assert.Contains(hostWindow.GetVisualDescendants().OfType<Button>(), button =>
                    button.IsEffectivelyVisible && ChoiceText(button) == "Not decided (2)");
                Assert.Contains(hostWindow.GetVisualDescendants().OfType<Border>(), border =>
                    border.IsEffectivelyVisible && AutomationProperties.GetAutomationId(border) == AutomationIds.ParsimonyEvidence);
                Assert.Contains(sidePanel.GetVisualDescendants().OfType<Button>(), button =>
                    AutomationProperties.GetAutomationId(button) == AutomationIds.ParsimonyActiveTab);
                Assert.Contains(sidePanel.GetVisualDescendants().OfType<Button>(), button =>
                    AutomationProperties.GetAutomationId(button) == AutomationIds.ParsimonySuppressedTab);
                Assert.Contains(sidePanel.GetVisualDescendants().OfType<CopyableTextBlock>(), text =>
                    text.Text == model.AxisSummary);
                Assert.Contains(page.GetVisualDescendants().OfType<ListBox>(), list =>
                    ReferenceEquals(list.ItemsSource, model.TableEntries));
                Assert.Contains(inspector.GetVisualDescendants().OfType<Border>(), border =>
                    AutomationProperties.GetAutomationId(border) == AutomationIds.ParsimonyEvidence);
                Assert.Contains(inspector.GetVisualDescendants().OfType<CopyableTextBlock>(), text =>
                    text.Text == model.Evidence!.Title);
                Assert.Contains(inspector.GetVisualDescendants().OfType<Button>(), button => button.Content?.ToString() == "Keep");
                var sideParent = sidePanel.Parent;
                var inspectorParent = inspector.Parent;
                Assert.Same(sidePanel, panes.SidePanel);
                Assert.Same(inspector, panes.InspectorContent);
                Assert.Same(sideParent, sidePanel.Parent);
                Assert.Same(inspectorParent, inspector.Parent);
            }
            finally
            {
                hostWindow.Close();
                window.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromMinutes(3));
    }

    [Fact]
    public void HostedFilterChoicesNarrowTheFindingsAndTheirCountsFollowAStagedDecision()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await PageScreenshots.OpenOverSampleData(parse: false, composeWindow: false, advancedAiModeEnabled: true,
                configure: (fake, _) => ConfigureMixedFindingKinds(fake));
            var shellWindow = new MotifTestWindow { Width = 1300, Height = 780 };
            try
            {
                workspace.CurrentPage = WorkspacePage.Parsimony;
                var model = workspace.PageModel<ParsimonyPageModel>();
                var page = new ParsimonyPage(model);
                shellWindow.Content = WorkspaceLayoutHost.ForPage(page);
                shellWindow.Show();
                PageScreenshots.Settle(shellWindow);

                var total = model.Rows.Count(row => !row.IsHeader);
                Assert.Equal(2, total);
                Assert.Equal("All findings (2)", ChoiceText(Button(shellWindow, "motif-parsimony-kind-all")));
                Assert.Equal("Not decided (2)", ChoiceText(Button(shellWindow, "motif-parsimony-decision-none")));
                Assert.DoesNotContain(shellWindow.GetVisualDescendants().OfType<Button>(), button =>
                    button.IsEffectivelyVisible && button.Content?.ToString() == "Any decision");

                var restrictivenessMeasure = ParsimonySampleReports.RestrictivenessMeasure;
                var group = Assert.Single(model.Groups, candidate =>
                    candidate.Items.Any(item => item.Link?.FindingId == "finding-2"));
                var restrictivenessFilter = "motif-parsimony-kind-" + restrictivenessMeasure;
                Assert.Equal(group.Title + " (1)", ChoiceText(Button(shellWindow, restrictivenessFilter)));

                Assert.True(model.SelectFinding(model.Rows.First(row => !row.IsHeader).Link!));
                await model.KeepCommand.ExecuteAsync(null);
                PageScreenshots.Settle(shellWindow);
                Assert.Equal("Kept (1)", ChoiceText(Button(shellWindow, "motif-parsimony-decision-keep")));
                Assert.Equal("Not decided (1)", ChoiceText(Button(shellWindow, "motif-parsimony-decision-none")));
                Assert.Equal(total, model.Rows.Count(row => !row.IsHeader));
                var selectedTitle = model.Evidence!.Title;
                Assert.Contains(shellWindow.GetVisualDescendants().OfType<CopyableTextBlock>(), text =>
                    text.IsEffectivelyVisible && text.Text == selectedTitle);

                Press(shellWindow, "motif-parsimony-decision-keep");
                Assert.Single(model.Rows, row => !row.IsHeader);
                Assert.True(model.HasEvidence);
                Assert.Contains(shellWindow.GetVisualDescendants().OfType<CopyableTextBlock>(), text =>
                    text.IsEffectivelyVisible && text.Text == selectedTitle);

                Press(shellWindow, "motif-parsimony-kind-all");
                Assert.Equal(total, model.Rows.Count(row => !row.IsHeader));
                Press(shellWindow, restrictivenessFilter);
                Assert.Single(model.Rows, row => !row.IsHeader);
                Assert.Equal("finding-2", Assert.Single(model.Rows, row => !row.IsHeader).Link!.FindingId);
            }
            finally
            {
                shellWindow.Close();
                window.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromMinutes(3));
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("refused")]
    public void HostedPaneShowsTheUnavailableReasonInsteadOfEmptyFilters(string state)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await PageScreenshots.OpenOverSampleData(parse: false, composeWindow: false, advancedAiModeEnabled: true,
                configure: (fake, _) => ParsimonyStateScreenshots.Configure(fake, state));
            var shellWindow = new MotifTestWindow { Width = 1300, Height = 780 };
            try
            {
                workspace.CurrentPage = WorkspacePage.Parsimony;
                var model = workspace.PageModel<ParsimonyPageModel>();
                var page = new ParsimonyPage(model);
                shellWindow.Content = WorkspaceLayoutHost.ForPage(page);
                shellWindow.Show();
                PageScreenshots.Settle(shellWindow);

                Assert.False(model.IsListed);
                Assert.DoesNotContain(shellWindow.GetVisualDescendants().OfType<Button>(), button =>
                    button.IsEffectivelyVisible && IsParsimonyFilter(button));
                Assert.Contains(shellWindow.GetVisualDescendants().OfType<CopyableTextBlock>(), text =>
                    text.IsEffectivelyVisible && text.Text == model.Message);
                Assert.NotEqual(string.Empty, model.Message);
            }
            finally
            {
                shellWindow.Close();
                window.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromMinutes(3));
    }

    [Fact]
    public void HostedPaneDoesNotShowEmptyFiltersWhileTheReportIsLoading()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var reports = new TaskCompletionSource<CommandOutcome<ParsimonyReportSetResponse?>>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var setReads = 0;
            var (workspace, window) = await PageScreenshots.OpenOverSampleData(parse: false, composeWindow: false, advancedAiModeEnabled: true,
                configure: (fake, _) =>
                {
                    ParsimonyWorkspacePaneTests.ConfigureMixedFindingKinds(fake);
                    var stored = fake.ParsimonySetHandler!;
                    fake.ParsimonySetHandler = (request, cancellationToken) =>
                        Interlocked.Increment(ref setReads) == 1
                            ? stored(request, cancellationToken)
                            : reports.Task.WaitAsync(cancellationToken);
                });
            var shellWindow = new MotifTestWindow { Width = 1300, Height = 780 };
            Task? reload = null;
            try
            {
                workspace.CurrentPage = WorkspacePage.Parsimony;
                var model = workspace.PageModel<ParsimonyPageModel>();
                reload = model.ReloadAsync();
                var page = new ParsimonyPage(model);
                shellWindow.Content = WorkspaceLayoutHost.ForPage(page);
                shellWindow.Show();
                PageScreenshots.Settle(shellWindow);

                Assert.True(model.IsLoading);
                Assert.DoesNotContain(shellWindow.GetVisualDescendants().OfType<Button>(), button =>
                    button.IsEffectivelyVisible && IsParsimonyFilter(button));
                Assert.Contains(shellWindow.GetVisualDescendants().OfType<CopyableTextBlock>(), text =>
                    text.IsEffectivelyVisible && text.Text == "The findings are not available yet.");
            }
            finally
            {
                reports.TrySetResult(CommandOutcome<ParsimonyReportSetResponse?>.Refused(
                    new Refusal("parsimony.no-report", FailureReason.NotFound, "No Parsimony Report yet.")));
                if (reload is not null) await reload;
                shellWindow.Close();
                window.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromMinutes(3));
    }

    // A filter choice is the shared chip, which shows its name and count as separate parts.
    private static string? ChoiceText(Button button) =>
        button is FilterChip chip ? $"{chip.Label} ({chip.Count})" : button.Content?.ToString();

    private static Button Button(Window window, string automationId) => Assert.Single(
        window.GetVisualDescendants().OfType<Button>(), candidate =>
            AutomationProperties.GetAutomationId(candidate) == automationId && candidate.IsEffectivelyVisible);

    private static bool IsParsimonyFilter(Button button)
    {
        var automationId = AutomationProperties.GetAutomationId(button) ?? string.Empty;
        return automationId == AutomationIds.ParsimonyActiveTab ||
               automationId == AutomationIds.ParsimonySuppressedTab ||
               automationId.StartsWith("motif-parsimony-kind-", StringComparison.Ordinal) ||
               automationId.StartsWith("motif-parsimony-decision-", StringComparison.Ordinal);
    }

    private static void Press(Window window, string automationId)
    {
        var button = Button(window, automationId);
        Assert.True(button.IsEffectivelyEnabled);
        button.Command!.Execute(button.CommandParameter);
        PageScreenshots.Settle(window);
    }

    internal static void ConfigureMixedFindingKinds(FakeCommandClient fake)
    {
        ParsimonyStateScreenshots.Configure(fake, "pending-keep");
        var report = ParsimonySampleReports.Listed(PageScreenshots.Token());
        var findings = report.Findings.ToArray();
        findings[1] = findings[1] with
        {
            MeasureId = ParsimonySampleReports.RestrictivenessMeasure,
            Axis = ParsimonyAxis.Restrictiveness,
            RecipeLink = "guide:parsimony/recipes/" + ParsimonySampleReports.RestrictivenessMeasure.ToLowerInvariant(),
        };
        report = report with { Findings = findings };
        fake.OnReadLatestParsimony((_, _) => Task.FromResult(CommandOutcome<ParsimonyLatestReportResponse>.Success(
            new ParsimonyLatestReportResponse(report.ReportId, report.Inputs.BundleId))));
        fake.OnReadParsimonyReport((_, _) => Task.FromResult(CommandOutcome<ParsimonyReportResponse>.Success(report)));

        var active = findings.Select(finding => new ParsimonyFindingDispositionViewRow(report.ReportId,
            report.Inputs.BundleId, "active", finding, null, null, null, null, null, null)).ToArray();
        fake.ParsimonyViewHandler = (request, _) =>
        {
            ParsimonyViewRow[] rows = request.Query.View switch
            {
                "parsimony-suppressed" or "parsimony-suppression-history" => [],
                _ => [.. active],
            };
            return Task.FromResult(CommandOutcome<ParsimonyNamedViewResponse>.Success(
                new ParsimonyNamedViewResponse(report.Inputs.BundleId, request.Query.View, 1,
                    ParsimonyMeasureStatus.Computed, [], rows.Length, rows.Length, null, false, rows, null, null)));
        };
        ParsimonyStateScreenshots.ServeCurrentReportSet(fake, report);
    }

}
