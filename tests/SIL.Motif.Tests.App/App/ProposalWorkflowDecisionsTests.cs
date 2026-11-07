using Avalonia.Controls;
using Avalonia.VisualTree;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class ProposalWorkflowDecisionsTests
{
    [Fact]
    public void SelectedProposalShowsItsRationaleInTheCentrePane() =>
        AssertProposalText("Rationale");

    [Fact]
    public void SelectedProposalNamesWhatItChangesInTheCentrePane() =>
        AssertProposalText("What It Changes");

    [Fact]
    public void SelectedProposalOffersOptionalChecks() =>
        AssertProposalText("More checks (optional)");

    [Fact]
    public void AReadyProposalOffersDecisionsOnTheProposalsPage()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var fake = new FakeCommandClient();
            fake.ProposalStandingsCompleteWith(new ProposalStandings(
                [new ProposalCard("proposal/ready", "Sound change", "proposed", ProposalGroup.WaitingForYou,
                    false, [], TrialStanding.Current, null, [])], 0));
            var (workspace, window) = await ReviewScreenshots.OpenAsync(fake);
            try
            {
                workspace.CurrentPage = WorkspacePage.Proposals;
                PageScreenshots.Settle(window);
                var buttons = window.GetVisualDescendants().OfType<Button>()
                    .Where(button => button.IsEffectivelyVisible).Select(button => button.Content).ToArray();
                Assert.Contains("Decline...", buttons);
                Assert.Contains("Decide Later", buttons);
            }
            finally { window.Close(); }
        }, TimeSpan.FromMinutes(3));
    }

    [Fact]
    public void ALongChangeListDisplaysOnePageAtATime()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var fake = new FakeCommandClient();
            fake.ProposalStandingsCompleteWith(new ProposalStandings(
                [new ProposalCard("proposal/ready", "Sound change", "proposed", ProposalGroup.WaitingForYou,
                    false, [], TrialStanding.Current, null, [])], 0));
            var (workspace, window) = await ReviewScreenshots.OpenAsync(fake);
            try
            {
                workspace.CurrentPage = WorkspacePage.Proposals;
                var page = workspace.PageModel<ProposalsPageModel>();
                await page.DetailReading;
                page.OperationDescriptions = Enumerable.Range(0, 60).Select(i => $"Change {i}.").ToArray();
                PageScreenshots.Settle(window);
                var list = Assert.Single(window.GetVisualDescendants().OfType<ProgressiveItemsControl>(),
                    control => control.Name == "ProposalOperationDescriptions");
                Assert.Equal(21, list.Items.Count);

                await list.ShowSourceIndexAsync(20);
                PageScreenshots.Settle(window);

                Assert.Contains(list.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Change 20.");
                Assert.Equal(22, list.Items.Count);
            }
            finally { window.Close(); await workspace.DisposeAsync(); }
        }, TimeSpan.FromMinutes(3));
    }

    private static void AssertProposalText(string expected)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var fake = new FakeCommandClient();
            fake.ProposalStandingsCompleteWith(new ProposalStandings(
                [new ProposalCard("proposal/ready", "Sound change", "proposed", ProposalGroup.WaitingForYou,
                    false, [], TrialStanding.Current, null, [])], 0));
            var (workspace, window) = await ReviewScreenshots.OpenAsync(fake);
            try
            {
                workspace.CurrentPage = WorkspacePage.Proposals;
                PageScreenshots.Settle(window);
                Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(),
                    text => text.IsEffectivelyVisible && text.Text == expected);
            }
            finally { window.Close(); }
        }, TimeSpan.FromMinutes(3));
    }
}
