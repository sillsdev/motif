using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using SIL.Motif.App;
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
    public void PointerAndKeyboardSelectionMarkProposalsReadButAutomaticSelectionDoesNot()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var cards = new[]
            {
                Card("p/one", "First", ProposalGroup.WaitingForYou) with { IsUnread = true },
                Card("p/two", "Second", ProposalGroup.WaitingForYou) with { IsUnread = true },
                Card("p/three", "Third", ProposalGroup.WaitingForYou) with { IsUnread = true },
            };
            var fake = new FakeCommandClient();
            fake.ProposalStandingsCompleteWith(new ProposalStandings(cards, 0));
            var context = WorkspaceContextTests.NewContext(fake);
            var model = new ProposalsPageModel(context);
            await context.OpenProjectAsync("project.fwdata");
            context.OpenPage(WorkspacePage.Proposals);
            await model.DetailReading;
            var page = new ProposalsPage(model);
            var window = new MotifTestWindow { Width = 1600, Height = 780, Content = WorkspaceLayoutHost.ForPage(page) };
            try
            {
                window.Show();
                PageScreenshots.Settle(window);
                await model.ReadMarkWriting;
                Assert.Empty(fake.ReviewMarkRequests);
                var table = Assert.Single(page.SidePanel.GetVisualDescendants().OfType<ListBox>(),
                    list => list.Name == "ProposalsRowsItems");
                var rows = model.Rows.OfType<ProposalCardViewModel>().ToArray();
                var first = Assert.IsType<ListBoxItem>(table.ContainerFromItem(rows[0]));
                HeadlessClick.Click(window, first, rows[0].Label);
                await model.ReadMarkWriting;
                Assert.Equal("p/one", Assert.Single(fake.ReviewMarkRequests).ProposalId);

                first.Focus();
                window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.None, null);
                await model.DetailReading;
                await model.ReadMarkWriting;
                Assert.Equal("p/two", model.SelectedProposal!.ProposalId);
                Assert.Contains(fake.ReviewMarkRequests, request => request.ProposalId == "p/two");

                PageScreenshots.Settle(window);
                var third = Assert.IsType<ListBoxItem>(table.ContainerFromItem(rows[2]));
                HeadlessClick.Click(window, third, rows[2].Label);
                await model.DetailReading;
                await model.ReadMarkWriting;
                Assert.Equal("p/three", model.SelectedProposal!.ProposalId);
                Assert.Equal(3, fake.ReviewMarkRequests.Count);
                Assert.All(fake.ReviewMarkRequests, request => Assert.Equal(ReviewMarkKeys.ProposalRead, request.EntryKey));
            }
            finally { window.Close(); await context.StopProjectWorkAsync(); }
        }, TimeSpan.FromMinutes(3));
    }

    [Fact]
    public void ProposalListHasOneParentWhileItsPageIsHosted()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            var page = new ProposalsPage(new ProposalsPageModel(WorkspaceContextTests.NewContext()));
            var pane = page.SidePanel;
            Assert.Null(pane.Parent);
            var host = WorkspaceLayoutHost.ForPage(page);
            var window = new MotifTestWindow { Width = 1600, Height = 780, Content = host };
            try
            {
                window.Show();
                PageScreenshots.Settle(window);
                var parent = pane.Parent;
                Assert.Same(pane, host.FindControl<ContentControl>("SidePanelContent")!.Content);
                Assert.Same(pane, page.SidePanel);
                window.Content = new Border();
                PageScreenshots.Settle(window);
                window.Content = host;
                PageScreenshots.Settle(window);
                Assert.Same(parent, pane.Parent);
            }
            finally { window.Close(); }
            return Task.CompletedTask;
        }, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void ALongProposalListKeepsItsViewportAndCanReachTheLastRow()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var fake = new FakeCommandClient();
            var cards = Enumerable.Range(0, 1000)
                .Select(index => Card($"p/{index}", $"Change {index}", ProposalGroup.WaitingForYou)).ToArray();
            fake.OnProposalStandings((_, _) => Task.FromResult(CommandOutcome<ProposalStandings>.Success(
                new ProposalStandings(cards, 0))));
            var context = WorkspaceContextTests.NewContext(fake);
            var model = new ProposalsPageModel(context);
            await context.OpenProjectAsync("project.fwdata");
            var page = new ProposalsPage(model);
            var window = new MotifTestWindow { Width = 1600, Height = 780, Content = WorkspaceLayoutHost.ForPage(page) };
            try
            {
                window.Show();
                PageScreenshots.Settle(window);
                var rows = Assert.Single(page.SidePanel.GetVisualDescendants().OfType<ListBox>(),
                    list => list.Name == "ProposalsRowsItems");
                Assert.True(rows.ItemCount >= cards.Length);
                Assert.InRange(rows.GetRealizedContainers().Count(), 1, 100);
                rows.ScrollIntoView(rows.ItemCount - 1);
                PageScreenshots.Settle(window);
                Assert.NotNull(rows.ContainerFromIndex(rows.ItemCount - 1));
                Assert.InRange(rows.GetRealizedContainers().Count(), 1, 100);
            }
            finally { window.Close(); await context.StopProjectWorkAsync(); }
        }, TimeSpan.FromSeconds(60));
    }

    [Fact]
    public void UnreadableProposalsAreReportedInsteadOfShowingAnEmptyList()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var fake = new FakeCommandClient();
            fake.OnProposalStandings((_, _) => Task.FromResult(CommandOutcome<ProposalStandings>.Success(
                new ProposalStandings([], 0) { Unreadable = [new UnreadableProposal("p/one", "First", "Unavailable")] })));
            var context = WorkspaceContextTests.NewContext(fake);
            var model = new ProposalsPageModel(context);
            await context.OpenProjectAsync("project.fwdata");
            var page = new ProposalsPage(model);
            var window = new MotifTestWindow { Width = 1000, Height = 700, Content = page };
            try
            {
                window.Show();
                PageScreenshots.Settle(window);
                Assert.False(model.IsEmpty);
                Assert.Contains(page.GetVisualDescendants().OfType<TextBlock>(), text =>
                    text.IsEffectivelyVisible && text.Text == model.UnreadableText);
                Assert.Contains(page.GetVisualDescendants().OfType<TextBlock>(), text =>
                    text.IsEffectivelyVisible && text.Text == model.UnreadableNamesText &&
                    text.Text?.Contains("p/one", StringComparison.Ordinal) == true);
            }
            finally { window.Close(); await context.StopProjectWorkAsync(); }
        }, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void ARowClickOrArrowKeyKeepsTheListOnTheProposalWhoseRationaleEditIsUnsaved()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var cards = new[]
            {
                Card("p/one", "First change", ProposalGroup.WaitingForYou),
                Card("p/two", "Second change", ProposalGroup.WaitingForYou),
            };
            var fake = new FakeCommandClient();
            fake.OnProposalStandings((_, _) => Task.FromResult(CommandOutcome<ProposalStandings>.Success(
                new ProposalStandings(cards, 0))));
            fake.ShowProposalHandler = (request, _) => Task.FromResult(CommandOutcome<ProposalDetailProjection>.Success(
                new ProposalDetailProjection(request.ProposalId, "proposed", "Label",
                    new RationaleView("Why.", null, "2026-11-11T00:00:00Z", false, null, false), null, [])));
            var context = WorkspaceContextTests.NewContext(fake);
            var model = new ProposalsPageModel(context);
            await context.OpenProjectAsync(@"C:\projects\one.fwdata");
            var standalone = new MotifTestWindow { Width = 2400, Height = 780 };
            try
            {
                var page = new ProposalsPage(model);
                var host = WorkspaceLayoutHost.ForPage(page);
                standalone.Content = host;
                standalone.Show();
                PageScreenshots.Settle(standalone);
                // The list is under test, not the rich field, so the field is kept out of the drawn frames.
                var table = Assert.Single(page.SidePanel.GetVisualDescendants().OfType<ListBox>(),
                    list => list.Name == "ProposalsRowsItems");
                var first = model.Rows.OfType<ProposalCardViewModel>().Single(card => card.ProposalId == "p/one");
                var second = model.Rows.OfType<ProposalCardViewModel>().Single(card => card.ProposalId == "p/two");
                model.SelectProposalCommand.Execute(first);
                await model.DetailReading;
                PageScreenshots.Settle(standalone);
                var editor = model.RationaleEditor!;
                editor.EditCommand.Execute(null);
                editor.RestoreCommand.Execute(new RationaleRevisionChoice(
                    new RationaleRevisionView(1, "Why. Typed.", null, "A", "2026-11-10T00:00:00Z")));
                PageScreenshots.Settle(standalone);
                Assert.True(editor.HasUnsavedChanges);
                Assert.Same(first, table.SelectedItem);

                var secondContainer = Assert.Single(table.GetVisualDescendants().OfType<ListBoxItem>(),
                    item => ReferenceEquals(item.DataContext, second));
                Assert.Contains(secondContainer.GetVisualDescendants().OfType<StackPanel>(), border =>
                    AutomationProperties.GetAutomationId(border) == second.RowAutomationId);
                HeadlessClick.Click(standalone, secondContainer, second.Label);
                PageScreenshots.Settle(standalone);
                Assert.Same(first, model.SelectedProposal);
                Assert.Same(first, table.SelectedItem);
                Assert.Same(editor, model.RationaleEditor);
                Assert.Equal("Why. Typed.", editor.Text());

                var firstContainer = Assert.Single(table.GetVisualDescendants().OfType<ListBoxItem>(),
                    item => ReferenceEquals(item.DataContext, first));
                firstContainer.Focus();
                standalone.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.None, null);
                PageScreenshots.Settle(standalone);
                Assert.Same(first, model.SelectedProposal);
                Assert.Same(first, table.SelectedItem);
                Assert.Same(editor, model.RationaleEditor);
                Assert.Equal("Why. Typed.", editor.Text());
            }
            finally
            {
                standalone.Close();
                await context.StopProjectWorkAsync();
            }
        }, TimeSpan.FromMinutes(3));
    }

    [Fact]
    public void AddingACommentShowsItsPageWhenThereAreManyComments()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var fake = new FakeCommandClient();
            fake.OnProposalStandings((_, _) => Task.FromResult(CommandOutcome<ProposalStandings>.Success(
                new ProposalStandings([Card("p/one", "First", ProposalGroup.WaitingForYou)], 0))));
            fake.ShowProposalHandler = (request, _) => Task.FromResult(CommandOutcome<ProposalDetailProjection>.Success(
                new ProposalDetailProjection(request.ProposalId, "proposed", "Label", null, null, [], Comments:
                    Enumerable.Range(1, 25).Select(id => new CommentView(id, "person", null, "", "Comment", null,
                        "now", null, false, null)).ToArray())));
            var context = WorkspaceContextTests.NewContext(fake);
            var model = new ProposalsPageModel(context);
            await context.OpenProjectAsync(@"C:\projects\one.fwdata");
            var standalone = new MotifTestWindow { Width = 1600, Height = 780 };
            try
            {
                var page = new ProposalsPage(model);
                standalone.Content = WorkspaceLayoutHost.ForPage(page);
                standalone.Show();
                model.SelectProposalCommand.Execute(model.Rows.OfType<ProposalCardViewModel>().First());
                await model.DetailReading;
                PageScreenshots.Settle(standalone);
                var button = Assert.Single(page.GetVisualDescendants().OfType<Button>(),
                    item => Equals(item.Content, MotifAppStrings.CommentAdd));
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                PageScreenshots.Settle(standalone);
                var added = model.Comments.Last();
                Assert.True(added.IsEditing);
                var comments = Assert.Single(page.GetVisualDescendants().OfType<ProgressiveItemsControl>(),
                    item => item.Name == "ProposalComments");
                Assert.Contains(added, comments.Items.Cast<object>());
            }
            finally { standalone.Close(); await context.StopProjectWorkAsync(); }
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

}
