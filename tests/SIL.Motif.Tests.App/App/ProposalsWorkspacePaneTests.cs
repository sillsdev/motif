using Avalonia;
using Avalonia.Controls;
using Avalonia.Automation;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using SIL.Motif.App;
using SIL.Motif.App.Controls;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.App.Views.Shell;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.App.Walkthrough;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class ProposalsWorkspacePaneTests
{
    [Fact]
    public void ProviderGettersStayStableWhileTheWorkspaceLayoutHostOwnsTheirControls()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            var page = new ProposalsPage(new ProposalsPageModel(
                WorkspaceContextTests.NewContext(new FakeCommandClient())));
            var provider = Assert.IsAssignableFrom<IWorkspacePaneProvider>(page);
            var sidePanel = Assert.IsAssignableFrom<Control>(provider.SidePanel);
            var inspector = Assert.IsAssignableFrom<Control>(provider.InspectorContent);
            Assert.Null(sidePanel.Parent);
            Assert.Null(inspector.Parent);
            var host = WorkspaceLayoutHost.ForPage(page);
            var window = new Window { Width = 1600, Height = 780, Content = host };
            try
            {
                window.Show();
                PageScreenshots.Settle(window);

                var sideParent = sidePanel.Parent;
                var inspectorParent = inspector.Parent;
                Assert.Same(sidePanel, host.FindControl<ContentControl>("SidePanelContent")!.Content);
                Assert.Same(inspector, host.FindControl<ContentControl>("PageInspectorHost")!.Content);
                Assert.Same(sidePanel, provider.SidePanel);
                Assert.Same(inspector, provider.InspectorContent);
                Assert.Same(sideParent, sidePanel.Parent);
                Assert.Same(inspectorParent, inspector.Parent);

                window.Content = new Border();
                PageScreenshots.Settle(window);
                window.Content = host;
                PageScreenshots.Settle(window);

                Assert.Same(sideParent, sidePanel.Parent);
                Assert.Same(inspectorParent, inspector.Parent);
            }
            finally
            {
                window.Close();
            }
            return Task.CompletedTask;
        }, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void LoadingPageShowsItsReadingMessageAndHidesFilters()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            var page = new ProposalsPage(new ProposalsPageModel(
                WorkspaceContextTests.NewContext(new FakeCommandClient())));
            var host = WorkspaceLayoutHost.ForPage(page);
            var window = new Window { Width = 1000, Height = 780, Content = host };
            try
            {
                window.Show();
                PageScreenshots.Settle(window);

                Assert.Contains(host.GetVisualDescendants().OfType<CopyableTextBlock>(), text =>
                    text.IsEffectivelyVisible && text.Text == "Reading Proposals…");
                Assert.DoesNotContain(host.GetVisualDescendants().OfType<FilterChip>(), chip => chip.IsEffectivelyVisible);
            }
            finally
            {
                window.Close();
            }
            return Task.CompletedTask;
        }, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void EmptyGroupFilterIsDisabled()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var fake = new FakeCommandClient();
            fake.ProposalStandingsCompleteWith(new ProposalStandings(
                [Card("p/open", "Open", ProposalGroup.WaitingForYou)], 0));
            var context = WorkspaceContextTests.NewContext(fake);
            var model = new ProposalsPageModel(context);
            await context.OpenProjectAsync(@"C:\projects\one.fwdata");
            var page = new ProposalsPage(model);
            var host = WorkspaceLayoutHost.ForPage(page);
            var window = new Window { Width = 1600, Height = 780, Content = host };
            try
            {
                window.Show();
                PageScreenshots.Settle(window);

                var closed = Assert.Single(host.GetVisualDescendants().OfType<FilterChip>(), chip =>
                    chip.Label == "Closed");
                Assert.Equal(0, closed.Count);
                Assert.False(closed.IsEnabled);
            }
            finally
            {
                window.Close();
            }
        }, TimeSpan.FromMinutes(3));
    }

    [Fact]
    public void EmptyPageExplainsThatThereAreNoProposalsAndHidesFilters()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var fake = new FakeCommandClient();
            fake.ProposalStandingsCompleteWith(new ProposalStandings([], 0));
            var context = WorkspaceContextTests.NewContext(fake);
            var model = new ProposalsPageModel(context);
            await context.OpenProjectAsync(@"C:\projects\one.fwdata");
            var page = new ProposalsPage(model);
            var host = WorkspaceLayoutHost.ForPage(page);
            var window = new Window { Width = 1000, Height = 780, Content = host };
            try
            {
                window.Show();
                PageScreenshots.Settle(window);

                Assert.Contains(host.GetVisualDescendants().OfType<CopyableTextBlock>(), text =>
                    text.IsEffectivelyVisible && text.Text == "No Proposals yet");
                Assert.DoesNotContain(host.GetVisualDescendants().OfType<CopyableTextBlock>(), text =>
                    text.IsEffectivelyVisible && text.Text == "Select a Proposal to see its details.");
                Assert.DoesNotContain(host.GetVisualDescendants().OfType<FilterChip>(), chip => chip.IsEffectivelyVisible);
            }
            finally
            {
                window.Close();
            }
        }, TimeSpan.FromMinutes(3));
    }

    [Fact]
    public void RefusedPageHidesTheOldProposalActionsAndFilters()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var openCard = Card("p/open", "Open", ProposalGroup.WaitingForYou);
            var reads = 0;
            var fake = new FakeCommandClient();
            fake.OnProposalStandings((_, _) => Interlocked.Increment(ref reads) == 1
                ? Task.FromResult(CommandOutcome<ProposalStandings>.Success(new ProposalStandings([openCard], 1)))
                : Task.FromResult(CommandOutcome<ProposalStandings>.Refused(
                    new Refusal("store.unavailable", FailureReason.Refused, "The Proposals could not be read."))));
            var context = WorkspaceContextTests.NewContext(fake);
            var model = new ProposalsPageModel(context);
            await context.OpenProjectAsync(@"C:\projects\one.fwdata");
            var page = new ProposalsPage(model);
            var host = WorkspaceLayoutHost.ForPage(page);
            var window = new Window { Width = 1000, Height = 780, Content = host };
            try
            {
                window.Show();
                PageScreenshots.Settle(window);
                await model.SelectGroupFilterCommand.ExecuteAsync(ProposalListFilter.Closed);
                PageScreenshots.Settle(window);

                Assert.DoesNotContain(host.GetVisualDescendants().OfType<FilterChip>(), chip => chip.IsEffectivelyVisible);
                Assert.DoesNotContain(host.GetVisualDescendants().OfType<Button>(), button =>
                    button.IsEffectivelyVisible && AutomationProperties.GetAutomationId(button) is { } id &&
                    id.StartsWith("motif-proposals-", StringComparison.Ordinal));
                Assert.DoesNotContain(host.GetVisualDescendants().OfType<CopyableTextBlock>(), text =>
                    text.IsEffectivelyVisible && text.Text == "Open");
            }
            finally
            {
                window.Close();
            }
        }, TimeSpan.FromMinutes(3));
    }

    [Fact]
    public void AFilterWithNoRowsExplainsTheResultAndCanBeCleared()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var openCard = Card("p/open", "Open", ProposalGroup.WaitingForYou);
            var fake = new FakeCommandClient();
            fake.OnProposalStandings((request, _) => Task.FromResult(CommandOutcome<ProposalStandings>.Success(
                request.IncludeSetAside
                    ? new ProposalStandings([openCard], 0)
                    : new ProposalStandings([openCard], 1))));
            var context = WorkspaceContextTests.NewContext(fake);
            var model = new ProposalsPageModel(context);
            await context.OpenProjectAsync(@"C:\projects\one.fwdata");
            var page = new ProposalsPage(model);
            var host = WorkspaceLayoutHost.ForPage(page);
            var window = new Window { Width = 1000, Height = 780, Content = host };
            try
            {
                window.Show();
                PageScreenshots.Settle(window);
                await model.SelectGroupFilterCommand.ExecuteAsync(ProposalListFilter.Closed);
                PageScreenshots.Settle(window);

                Assert.Contains(host.GetVisualDescendants().OfType<CopyableTextBlock>(), text =>
                    text.IsEffectivelyVisible && text.Text == "Nothing in Closed.");
                var clearFilter = host.GetVisualDescendants().OfType<Button>().Single(button =>
                    AutomationProperties.GetAutomationId(button) == AutomationIds.ProposalsClearFilter);
                Assert.True(clearFilter.IsEffectivelyVisible);
                Assert.NotNull(clearFilter.Command);
                clearFilter.Command.Execute(clearFilter.CommandParameter);
                PageScreenshots.Settle(window);

                Assert.Null(model.SelectedGroupFilter);
                Assert.Equal("Open", Assert.Single(model.Rows.OfType<ProposalCardViewModel>()).Label);
            }
            finally
            {
                window.Close();
            }
        }, TimeSpan.FromMinutes(3));
    }

    [Fact]
    public void ProposalsPageExposesAndHostsGroupFiltersAndSelectedDetails()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var cards = new[]
            {
                Card("p/pending", "Your changes", ProposalGroup.NotAppliedYet, pending: true),
                Card("p/ready", "Nasal assimilation before /p/", ProposalGroup.WaitingForYou),
                Card("p/stale", "Reduplicated forms", ProposalGroup.NeedsAnotherTry),
                Card("p/new", "Try meN- before vowels", ProposalGroup.NotTried),
                Card("p/closed", "Old nasal rule", ProposalGroup.SetAside) with { Status = "withdrawn" },
            };
            var activeCards = cards.Where(card => card.Group != ProposalGroup.SetAside).ToArray();
            var fake = new FakeCommandClient();
            fake.OnProposalStandings((request, _) => Task.FromResult(CommandOutcome<ProposalStandings>.Success(
                new ProposalStandings(request.IncludeSetAside ? cards : activeCards, 1))));
            var (workspace, window) = await ReviewScreenshots.OpenAsync(fake);
            var standalone = new Window { Width = 1600, Height = 780 };
            try
            {
                var model = workspace.PageModel<ProposalsPageModel>();
                var page = new ProposalsPage(model);
                var standaloneHost = WorkspaceLayoutHost.ForPage(page);
                standalone.Content = standaloneHost;
                standalone.Show();
                PageScreenshots.Settle(standalone);

                var provider = Assert.IsAssignableFrom<IWorkspacePaneProvider>(page);
                Assert.Equal("Proposals", provider.SidePanelTitle);
                var sidePanel = Assert.IsAssignableFrom<Control>(provider.SidePanel);
                var inspector = Assert.IsAssignableFrom<Control>(provider.InspectorContent);
                Assert.Same(sidePanel, standaloneHost.FindControl<ContentControl>("SidePanelContent")!.Content);
                Assert.Same(inspector, standaloneHost.FindControl<ContentControl>("PageInspectorHost")!.Content);
                Assert.Contains(standaloneHost.GetVisualDescendants().OfType<FilterChip>(), chip => chip.IsEffectivelyVisible);
                Assert.Contains(standaloneHost.GetVisualDescendants().OfType<CopyableTextBlock>(), text =>
                    text.IsEffectivelyVisible && text.Text == "Nasal assimilation before /p/");
                var table = Assert.IsType<ListBox>(page.FindControl<ListBox>("ProposalsRowsItems"));
                Assert.IsType<VirtualizingStackPanel>(table.ItemsPanelRoot);
                Assert.Same(model.SelectedProposal, table.SelectedItem);
                var groupItems = table.GetVisualDescendants().OfType<ListBoxItem>()
                    .Where(item => item.DataContext is DenseTableGroup).ToArray();
                Assert.Equal(["Your changes not applied yet", "Waiting for you"], groupItems
                    .Select(item => Assert.IsType<DenseTableGroup>(item.DataContext).Label));
                Assert.Equal(["1 proposal", "3 proposals"], groupItems
                    .Select(item => Assert.IsType<DenseTableGroup>(item.DataContext).CountText));
                Assert.All(groupItems, item =>
                {
                    Assert.False(item.Focusable);
                    Assert.True(item.IsHitTestVisible);
                    Assert.True(item.IsEnabled);
                    Assert.False(item.IsSelected);
                });
                Assert.All(groupItems, item =>
                {
                    var group = Assert.IsType<DenseTableGroup>(item.DataContext);
                    var heading = Assert.Single(item.GetVisualDescendants().OfType<TextBlock>(), text =>
                        text.Classes.Contains("denseTableGroupName"));
                    Assert.Equal(group.Label, ToolTip.GetTip(heading));
                    Assert.Equal(group.Label, AutomationProperties.GetName(heading));
                });

                var columnHeader = Assert.Single(standaloneHost.GetVisualDescendants().OfType<Border>(),
                    border => border.Classes.Contains("denseTableColumnHeader"));
                var headerGrid = Assert.Single(columnHeader.GetVisualDescendants().OfType<Grid>(),
                    grid => DenseTable.GetColumns(grid) is not null);

                var allActionIds = standaloneHost.GetVisualDescendants().OfType<Button>()
                    .Select(AutomationProperties.GetAutomationId).Where(id => id is not null).ToArray();
                Assert.Equal(allActionIds.Length, allActionIds.Distinct(StringComparer.Ordinal).Count());

                var readyCard = model.Rows.OfType<ProposalCardViewModel>().Single(card => card.ProposalId == "p/ready");
                var initiallySelected = model.SelectedProposal;
                Assert.Same(readyCard, initiallySelected);
                var staleCard = model.Rows.OfType<ProposalCardViewModel>().Single(card => card.ProposalId == "p/stale");
                var staleContainer = Assert.Single(table.GetVisualDescendants().OfType<ListBoxItem>(),
                    item => ReferenceEquals(item.DataContext, staleCard));
                var staleRow = Assert.Single(staleContainer.GetVisualDescendants().OfType<Border>(), border =>
                    AutomationProperties.GetAutomationId(border) == staleCard.RowAutomationId);
                var rowGrid = Assert.Single(staleRow.GetVisualDescendants().OfType<Grid>(),
                    grid => DenseTable.GetColumns(grid) is not null);
                var proposalCell = Assert.Single(rowGrid.Children.OfType<Grid>());
                Assert.Equal(2, proposalCell.ColumnDefinitions.Count);
                Assert.Equal(GridLength.Star, proposalCell.ColumnDefinitions[0].Width);
                Assert.Equal(GridLength.Auto, proposalCell.ColumnDefinitions[1].Width);
                var staleLabel = Assert.Single(proposalCell.Children.OfType<TextBlock>(), text =>
                    text.Classes.Contains("denseTableItem"));
                Assert.Equal(staleCard.Label, ToolTip.GetTip(staleLabel));
                var tableWidthHost = page.FindControl<Grid>("ProposalsTableHost")!;
                Assert.True(DenseTable.GetIsWidthHost(tableWidthHost));
                Assert.Equal(DenseTable.GetColumns(headerGrid), DenseTable.GetColumns(rowGrid));
                Assert.False(string.IsNullOrWhiteSpace(DenseTable.GetColumns(rowGrid)));
                Assert.False(string.IsNullOrWhiteSpace(DenseTable.GetCompactColumns(headerGrid)));
                Assert.Equal(DenseTable.GetCompactColumns(headerGrid), DenseTable.GetCompactColumns(rowGrid));
                Assert.Equal(
                    headerGrid.Children.OfType<Border>().Where(DenseTable.GetSecondary).Select(Grid.GetColumn).Order(),
                    rowGrid.Children.OfType<Border>().Where(DenseTable.GetSecondary).Select(Grid.GetColumn).Order());
                var staleTryAgain = Assert.Single(staleRow.GetVisualDescendants().OfType<Button>(), button =>
                    AutomationProperties.GetAutomationId(button) == staleCard.TryAgainAutomationId);
                var staleMenu = Assert.Single(staleRow.GetVisualDescendants().OfType<Button>(),
                    button => button.Classes.Contains("denseTableMenu"));
                Assert.True(staleTryAgain.IsEffectivelyVisible);
                Assert.True(staleMenu.IsEffectivelyVisible);
                Assert.Equal(staleCard.ActionsAutomationId, AutomationProperties.GetAutomationId(staleMenu));
                RaiseLeftPointerPress(staleTryAgain, standalone);
                Assert.Same(initiallySelected, model.SelectedProposal);

                Assert.Equal(staleCard.Label, AutomationProperties.GetName(staleRow));
                HeadlessClick.Click(standalone, staleRow, staleCard.Label);
                PageScreenshots.Settle(standalone);
                Assert.Same(staleCard, table.SelectedItem);
                Assert.Same(staleCard, model.SelectedProposal);
                Assert.Equal($"{staleCard.Label}, selected", AutomationProperties.GetName(staleRow));
                Assert.DoesNotContain(standaloneHost.GetVisualDescendants().OfType<TextBlock>(), text =>
                    text.IsEffectivelyVisible && text.Text == "Opened beside");
                Assert.Contains(standaloneHost.GetVisualDescendants().OfType<Button>(), button =>
                    AutomationProperties.GetAutomationId(button) == staleCard.InspectorOpenAutomationId &&
                    Equals(button.Content, "Open in Review changes"));

                var staleFlyout = Assert.IsType<Flyout>(staleMenu.Flyout);
                staleFlyout.ShowAt(staleMenu);
                PageScreenshots.Settle(standalone);
                var repeatedTryAgain = Assert.Single(Assert.IsAssignableFrom<Control>(staleFlyout.Content)
                    .GetVisualDescendants().OfType<Button>(), button => Equals(button.Content, "Try again"));
                Assert.True(repeatedTryAgain.IsEffectivelyVisible);
                var detailsAction = Assert.Single(Assert.IsAssignableFrom<Control>(staleFlyout.Content)
                    .GetVisualDescendants().OfType<Button>(), button => Equals(button.Content, "Details"));
                Assert.Equal(staleCard.DetailsAutomationId, AutomationProperties.GetAutomationId(detailsAction));
                var inspectorPane = standaloneHost.FindControl<Border>("InspectorPane")!;
                standaloneHost.FindControl<Button>("InspectorHideButton")!
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                PageScreenshots.Settle(standalone);
                Assert.False(inspectorPane.IsVisible);
                detailsAction.Command!.Execute(detailsAction.CommandParameter);
                detailsAction.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                PageScreenshots.Settle(standalone);
                Assert.True(inspectorPane.IsVisible);
                Assert.True(standaloneHost.FindControl<ContentControl>("PageInspectorHost")!.IsVisible);
                Assert.Same(staleCard, model.SelectedProposal);
                staleFlyout.Hide();

                var openInReview = Assert.Single(inspector.GetVisualDescendants().OfType<Button>(), button =>
                    AutomationProperties.GetAutomationId(button) == staleCard.InspectorOpenAutomationId);
                standaloneHost.FindControl<Button>("InspectorHideButton")!
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                PageScreenshots.Settle(standalone);
                Assert.False(inspectorPane.IsVisible);
                openInReview.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                PageScreenshots.Settle(standalone);
                Assert.True(inspectorPane.IsVisible);
                Assert.True(standaloneHost.FindControl<ContentControl>("PageInspectorHost")!.IsVisible);

                Assert.Same(model, sidePanel.DataContext);
                Assert.Same(model, inspector.DataContext);
                var filters = sidePanel.GetVisualDescendants().OfType<FilterChip>().ToArray();
                Assert.Equal(["Your changes not applied yet", "Waiting for you", "Closed"], filters.Select(filter => filter.Label));
                Assert.Equal([1, 3, 1], filters.Select(filter => filter.Count));
                Assert.Contains(inspector.GetVisualDescendants().OfType<CopyableTextBlock>(), text =>
                    text.Text == staleCard.Label);

                var waitingFilter = Assert.Single(filters, filter => filter.Label == "Waiting for you");
                await Assert.IsAssignableFrom<IAsyncRelayCommand>(waitingFilter.Command)
                    .ExecuteAsync(waitingFilter.CommandParameter);
                Assert.Equal(["Nasal assimilation before /p/", "Reduplicated forms", "Try meN- before vowels"],
                    model.Rows.OfType<ProposalCardViewModel>().Select(card => card.Label));

                var pendingFilter = Assert.Single(model.GroupFilters,
                    filter => filter.Filter == ProposalListFilter.YourChangesNotAppliedYet);
                await Assert.IsAssignableFrom<IAsyncRelayCommand>(pendingFilter.SelectCommand)
                    .ExecuteAsync(pendingFilter.Filter);
                PageScreenshots.Settle(standalone);
                Assert.Contains(inspector.GetVisualDescendants().OfType<CopyableTextBlock>(), text =>
                    text.Text == "Your changes");

                var closedFilter = Assert.Single(model.GroupFilters,
                    filter => filter.Filter == ProposalListFilter.Closed);
                await Assert.IsAssignableFrom<IAsyncRelayCommand>(closedFilter.SelectCommand)
                    .ExecuteAsync(closedFilter.Filter);
                PageScreenshots.Settle(standalone);
                Assert.True(fake.ProposalStandingsRequests[^1].IncludeSetAside);
                Assert.Equal("Old nasal rule", Assert.Single(model.Rows.OfType<ProposalCardViewModel>()).Label);

                var sidePanelParent = sidePanel.Parent;
                var inspectorParent = inspector.Parent;
                Assert.Same(sidePanel, provider.SidePanel);
                Assert.Same(inspector, provider.InspectorContent);
                Assert.Same(sidePanelParent, sidePanel.Parent);
                Assert.Same(inspectorParent, inspector.Parent);

                standalone.Content = null;
                standalone.Content = standaloneHost;
                PageScreenshots.Settle(standalone);
                Assert.Contains(standaloneHost.GetVisualDescendants().OfType<FilterChip>(), chip => chip.IsEffectivelyVisible);
                Assert.Contains(standaloneHost.GetVisualDescendants().OfType<CopyableTextBlock>(), text =>
                    text.IsEffectivelyVisible && text.Text == "Old nasal rule");

                window.Width = 1600;
                workspace.CurrentPage = WorkspacePage.Proposals;
                PageScreenshots.Settle(window);
                var mainHost = window.FindControl<WorkspaceLayoutHost>("WorkspaceLayout")!;
                var mainPage = Assert.Single(window.GetVisualDescendants().OfType<ProposalsPage>());
                var mainProvider = Assert.IsAssignableFrom<IWorkspacePaneProvider>(mainPage);
                var mainSidePanel = Assert.IsAssignableFrom<Control>(mainProvider.SidePanel);
                var mainInspector = Assert.IsAssignableFrom<Control>(mainProvider.InspectorContent);
                Assert.Same(mainSidePanel, mainHost.FindControl<ContentControl>("SidePanelContent")!.Content);
                Assert.Same(mainInspector, mainHost.FindControl<ContentControl>("PageInspectorHost")!.Content);
                var mainSideParent = mainSidePanel.Parent;
                var mainInspectorParent = mainInspector.Parent;
                Assert.Same(mainSidePanel, mainProvider.SidePanel);
                Assert.Same(mainInspector, mainProvider.InspectorContent);
                Assert.Same(mainSideParent, mainSidePanel.Parent);
                Assert.Same(mainInspectorParent, mainInspector.Parent);
            }
            finally
            {
                standalone.Close();
                window.Close();
            }
        }, TimeSpan.FromMinutes(3));
    }

    private static ProposalCard Card(string id, string label, ProposalGroup group, bool pending = false) =>
        new(id, label, "proposed", group, pending, [new("rule", 1, "1 rule")],
            group switch
            {
                ProposalGroup.WaitingForYou => TrialStanding.Current,
                ProposalGroup.NeedsAnotherTry => TrialStanding.NeedsAnotherTry(TrialStaleReason.BaselineMoved),
                _ => TrialStanding.NotTried,
            },
            null, []);

    private static void RaiseLeftPointerPress(Control target, Window window)
    {
        using var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, isPrimary: true);
        var properties = new PointerPointProperties(RawInputModifiers.LeftMouseButton,
            PointerUpdateKind.LeftButtonPressed);
        target.RaiseEvent(new PointerPressedEventArgs(target, pointer, window, new Point(), 0, properties,
            KeyModifiers.None));
    }
}
