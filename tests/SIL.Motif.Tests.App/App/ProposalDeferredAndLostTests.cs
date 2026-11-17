using Avalonia.Controls;
using Avalonia.VisualTree;
using SIL.Motif.App;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views.Shell;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>Pins the Deferred filter and the lost-approvals list at the top of a Proposal's evidence.</summary>
[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class ProposalDeferredAndLostTests
{
    [Fact]
    public void ADeferredProposalIsListedUnderDeferredWithACountAndNotUnderWaitingForYou()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var fake = new FakeCommandClient();
            fake.ProposalStandingsCompleteWith(new ProposalStandings(
            [
                Card("proposal/ready", "Ready one", "proposed", ProposalGroup.WaitingForYou),
                Card("proposal/later", "Put off one", "deferred", ProposalGroup.NotTried),
                Card("proposal/later2", "Put off two", "deferred", ProposalGroup.WaitingForYou),
            ], 0));
            var (workspace, window) = await ReviewScreenshots.OpenAsync(fake);
            try
            {
                workspace.CurrentPage = WorkspacePage.Proposals;
                PageScreenshots.Settle(window);
                var model = workspace.PageModel<ProposalsPageModel>();

                Assert.Equal(["Waiting for You", "Deferred"], model.ListGroups.Select(group => group.Title));
                Assert.Equal(["proposal/later", "proposal/later2"],
                    model.ListGroups[1].Cards.Select(card => card.ProposalId));
                var chip = ChipTexts(window);
                Assert.Contains("Deferred (2)", chip);
                Assert.Contains("Waiting for You (1)", chip);

                var deferred = window.GetVisualDescendants().OfType<Button>().Single(button =>
                    button.IsEffectivelyVisible && AutomationIdOf(button) == AutomationIds.ForProposalsFilter(ProposalListFilter.Deferred));
                deferred.Command!.Execute(deferred.CommandParameter);
                PageScreenshots.Settle(window);

                Assert.Equal(["Deferred"], model.ListGroups.Select(group => group.Title));
                Assert.Equal(["Put off one", "Put off two"],
                    model.Rows.OfType<ProposalCardViewModel>().Select(card => card.Label));
            }
            finally { window.Close(); }
        }, TimeSpan.FromMinutes(3));
    }

    [Fact]
    public void DecidingLaterMovesAProposalIntoTheDeferredFilter()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var fake = new FakeCommandClient();
            var cards = new List<ProposalCard> { Card("proposal/a", "A", "proposed", ProposalGroup.WaitingForYou) };
            fake.OnProposalStandings((_, _) => Task.FromResult(
                SIL.Motif.Contract.Commands.CommandOutcome<ProposalStandings>.Success(new ProposalStandings([.. cards], 0))));
            var (workspace, window) = await ReviewScreenshots.OpenAsync(fake);
            try
            {
                workspace.CurrentPage = WorkspacePage.Proposals;
                PageScreenshots.Settle(window);
                var model = workspace.PageModel<ProposalsPageModel>();
                Assert.Equal(0, model.GroupFilters.Single(filter => filter.Filter == ProposalListFilter.Deferred).Count);

                cards[0] = cards[0] with { Status = "deferred" };
                await model.DecideLaterCommand.ExecuteAsync(model.SelectedProposal);
                PageScreenshots.Settle(window);

                Assert.Equal(1, model.GroupFilters.Single(filter => filter.Filter == ProposalListFilter.Deferred).Count);
                Assert.Equal(["Deferred"], model.ListGroups.Select(group => group.Title));
            }
            finally { window.Close(); }
        }, TimeSpan.FromMinutes(3));
    }

    [Fact]
    public void ATrialWithTwoLostApprovalsListsBothFirstAndTheApplyCountAgrees()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var fake = new FakeCommandClient();
            fake.ProposalStandingsCompleteWith(new ProposalStandings(
                [Card("proposal/loses", "Loses two", "proposed", ProposalGroup.WaitingForYou, Trial(["kata", "lima"], 2))], 0));
            var (workspace, window) = await ReviewScreenshots.OpenAsync(fake);
            try
            {
                workspace.CurrentPage = WorkspacePage.Proposals;
                PageScreenshots.Settle(window);
                var model = workspace.PageModel<ProposalsPageModel>();
                model.SelectedProposal = model.Rows.OfType<ProposalCardViewModel>().Single();
                PageScreenshots.Settle(window);

                var section = window.GetVisualDescendants().OfType<StackPanel>().Single(panel =>
                    panel.Name == "ProposalLostApproved");
                Assert.True(section.IsEffectivelyVisible, "the lost list is hidden");
                var shown = section.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text).ToArray();
                Assert.Equal("Approved Words That Would Stop Parsing", shown[0]);
                Assert.Contains("2 approved words would stop parsing", shown[1]);
                Assert.True(shown.ToList().IndexOf("kata") is >= 0 and var first &&
                    shown.ToList().IndexOf("lima") is >= 0 and var second && first < second);

                var evidenceTop = section.GetVisualParent()!.GetVisualChildren().First();
                Assert.Same(section, evidenceTop);

                var document = model.SelectedDocument!;
                Assert.Contains("approved analysis of 2 words", document.ApplyLossNotice, StringComparison.Ordinal);
                Assert.Contains("listed at the top", document.ApplyLossNotice, StringComparison.Ordinal);
            }
            finally { window.Close(); }
        }, TimeSpan.FromMinutes(3));
    }

    [Fact]
    public void NoLostListShowsWhenNothingWouldStopParsing()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var fake = new FakeCommandClient();
            fake.ProposalStandingsCompleteWith(new ProposalStandings(
                [Card("proposal/safe", "Loses none", "proposed", ProposalGroup.WaitingForYou, Trial([], 0))], 0));
            var (workspace, window) = await ReviewScreenshots.OpenAsync(fake);
            try
            {
                workspace.CurrentPage = WorkspacePage.Proposals;
                PageScreenshots.Settle(window);
                var model = workspace.PageModel<ProposalsPageModel>();
                model.SelectedProposal = model.Rows.OfType<ProposalCardViewModel>().Single();
                PageScreenshots.Settle(window);

                var section = window.GetVisualDescendants().OfType<StackPanel>().Single(panel =>
                    panel.Name == "ProposalLostApproved");
                Assert.False(section.IsEffectivelyVisible);
            }
            finally { window.Close(); }
        }, TimeSpan.FromMinutes(3));
    }

    [Fact]
    public void LostWordsBeyondTheNamedLimitAreCountedAndSaid()
    {
        var evidence = new ProposalEvidenceColumnsViewModel(new ProposalDifferenceViewModel(
            (_, _) => throw new InvalidOperationException(), _ => { }));
        evidence.SetLostApproved(Trial(["kata", "lima"], 7));

        Assert.True(evidence.HasLostApproved);
        Assert.Equal("7 approved words would stop parsing if you apply this Proposal.", evidence.LostApprovedCount);
        Assert.Equal("5 more words are counted but not listed.", evidence.LostApprovedMore);
        Assert.Equal(["kata", "lima"], evidence.LostApproved.Select(word => word.Word));
        Assert.All(evidence.LostApproved, word => Assert.False(word.HasReason));
    }

    private static string[] ChipTexts(Control window) => [.. window.GetVisualDescendants().OfType<Button>()
        .Where(button => button.IsEffectivelyVisible && AutomationIdOf(button)?.StartsWith("motif-proposals-filter-",
            StringComparison.Ordinal) == true)
        .Select(button => button.GetVisualDescendants().OfType<TextBlock>().First().Text!)];

    private static string? AutomationIdOf(Control control) => Avalonia.Automation.AutomationProperties.GetAutomationId(control);

    private static ProposalCard Card(string id, string label, string status, ProposalGroup group,
        TrialSummary? trial = null) =>
        new(id, label, status, group, false, [], trial is null ? TrialStanding.NotTried : TrialStanding.Current, trial, []);

    private static TrialSummary Trial(string[] named, int lost) => new("job/t",
        new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero), TrialFreshness.Current, TrialWordScope.AllWords,
        new Paired<double?>(60, 64.5, true, true, true, null), new Paired<int>(10, 8, true, true, true, null),
        new Paired<int>(12, 12, true, true, false, null), new Paired<double?>(2500, 2600, true, true, false, null),
        new Paired<int?>(3, 7, true, true, true, null), lost, 0)
    {
        LostApprovedKnown = true,
        LostApprovedWords = [.. named.Select(word => new LostApprovedWord(word))],
    };
}
