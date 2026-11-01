using SIL.Motif.App.ViewModels;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

public sealed class ProposalsPageUnreadableTests
{
    private const string ProjectPath = @"C:\projects\one.fwdata";

    [Fact]
    public async Task TheWindowSaysHowManyProposalsCouldNotBeReadAndNamesThemBesideTheReadableOnes()
    {
        var card = new ProposalCard("p/ok", "Fine", "proposed", ProposalGroup.NotTried, false, [],
            TrialStanding.NotTried, null, []);
        var page = await OpenAsync(new ProposalStandings([card], 0)
        {
            Unreadable =
            [
                new UnreadableProposal("p/one", "Plural fix", "Unknown operation kind."),
                new UnreadableProposal("p/two", "p/two", "Not JSON."),
            ],
        });

        Assert.True(page.IsListed);
        Assert.True(page.HasUnreadable);
        Assert.Equal("2 Proposals could not be read, so they are not listed. Nothing was changed or deleted.",
            page.UnreadableText);
        Assert.Equal("Not read: Plural fix (p/one), p/two.", page.UnreadableNamesText);
        Assert.Equal("Fine", Assert.Single(page.Groups.SelectMany(group => group.Cards)).Label);
    }

    [Fact]
    public async Task OneUnreadableProposalAloneStillShowsTheLineInsteadOfTheEmptyPage()
    {
        var page = await OpenAsync(new ProposalStandings([], 0)
        {
            Unreadable = [new UnreadableProposal("p/one", "Plural fix", "Unknown operation kind.")],
        });

        Assert.False(page.IsEmpty);
        Assert.True(page.HasUnreadable);
        Assert.StartsWith("1 Proposal could not be read, so it is not listed.", page.UnreadableText,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadableProposalsOnlyShowNoLine()
    {
        var page = await OpenAsync(new ProposalStandings([], 0));

        Assert.False(page.HasUnreadable);
        Assert.Equal(string.Empty, page.UnreadableText);
        Assert.Equal(string.Empty, page.UnreadableNamesText);
    }

    private static async Task<ProposalsPageModel> OpenAsync(ProposalStandings standings)
    {
        var fake = new FakeCommandClient();
        fake.ProposalStandingsCompleteWith(standings);
        var context = WorkspaceContextTests.NewContext(fake);
        var page = new ProposalsPageModel(context);
        await context.OpenProjectAsync(ProjectPath);
        return page;
    }
}
