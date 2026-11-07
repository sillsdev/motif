using SIL.Motif.App.ViewModels;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

public sealed class ProposalWorkflowModelTests
{
    private const string Path = @"C:\projects\one.fwdata";
    private const string Id = "proposal/one";

    [Fact]
    public async Task SelectingAProposalReadsItsRationaleAndDescribesEveryOperation()
    {
        var fake = NewFake();
        fake.ProposalDetail = new ProposalDetailProjection(Id, "proposed", "Sound change",
            new RationaleView("Why this helps", "old", "2026-10-01", true, null, true), "new",
            [new ProposalOperationView("op/one", "grammar/phPhonData/createPhonRules", null, null, [], null),
             new ProposalOperationView("op/two", "analysis/langProject/createWordforms", null, null, [], null)]);
        var change = new PendingChange("change/one", "word/one", "word", "approve", null, null, ["op/two"]);
        fake.Review = fake.Review with
        {
            Entries = [new WordChangeEntry("change/one", 0, "Approve", ["op/two"], [], "digest", false,
                change, null, default)],
        };
        var context = WorkspaceContextTests.NewContext(fake);
        var page = new ProposalsPageModel(context);
        await context.OpenProjectAsync(Path);
        await page.DetailReading;

        Assert.Equal("Why this helps", page.Detail!.Rationale!.Markdown);
        Assert.True(page.Detail.Rationale.OutOfDate);
        Assert.Equal(["Add a phonological rule (word count not available).", "Add a wordform (1 word)."],
            page.OperationDescriptions);
    }

    [Fact]
    public async Task ASlowDetailReadCannotReplaceTheNewSelection()
    {
        var firstRead = new TaskCompletionSource<CommandOutcome<ProposalDetailProjection>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var fake = NewFake();
        fake.ProposalStandingsCompleteWith(new ProposalStandings(
        [
            new ProposalCard(Id, "First", "proposed", ProposalGroup.WaitingForYou,
                false, [], TrialStanding.Current, null, []),
            new ProposalCard("proposal/two", "Second", "proposed", ProposalGroup.WaitingForYou,
                false, [], TrialStanding.Current, null, []),
        ], 0));
        fake.ShowProposalHandler = (request, _) => request.ProposalId == Id ? firstRead.Task : Task.FromResult(
            CommandOutcome<ProposalDetailProjection>.Success(
                new ProposalDetailProjection(request.ProposalId, "proposed", "Second", null, null, [])));
        var context = WorkspaceContextTests.NewContext(fake);
        var page = new ProposalsPageModel(context);
        await context.OpenProjectAsync(Path);
        var oldRead = page.DetailReading;
        var second = page.Rows.OfType<ProposalCardViewModel>().Single(card => card.ProposalId == "proposal/two");

        page.SelectProposalCommand.Execute(second);
        await page.DetailReading;
        firstRead.SetResult(CommandOutcome<ProposalDetailProjection>.Success(
            new ProposalDetailProjection(Id, "proposed", "First", null, null, [])));
        await oldRead;

        Assert.Equal("proposal/two", page.Detail!.ProposalId);
    }

    [Fact]
    public async Task DecliningSavesTheReasonBeforeTheProposalCloses()
    {
        var fake = NewFake();
        var context = WorkspaceContextTests.NewContext(fake);
        var page = new ProposalsPageModel(context);
        await context.OpenProjectAsync(Path);
        var selected = page.SelectedProposal!;
        page.AskToDeclineCommand.Execute(selected);
        page.DeclineReason = "The analysis needs work.";

        await page.DeclineCommand.ExecuteAsync(selected);

        var request = Assert.Single(fake.DeclineProposalRequests);
        Assert.Equal("The analysis needs work.", request.Reason);
        Assert.Equal(Id, request.ProposalId);
        Assert.Equal("Declined.", page.DecisionNotice);
    }

    [Fact]
    public async Task DecidingLaterMovesTheReadyProposalToDeferred()
    {
        var fake = NewFake();
        var context = WorkspaceContextTests.NewContext(fake);
        var page = new ProposalsPageModel(context);
        await context.OpenProjectAsync(Path);

        await page.DecideLaterCommand.ExecuteAsync(page.SelectedProposal);

        Assert.Equal(Id, Assert.Single(fake.DeferProposalRequests).ProposalId);
        Assert.Empty(fake.DeclineProposalRequests);
    }

    [Fact]
    public async Task AProposalBecomesReadOnlyWhenItsPageIsViewed()
    {
        var fake = new FakeCommandClient();
        fake.ProposalStandingsCompleteWith(new ProposalStandings(
            [new ProposalCard(Id, "Sound change", "proposed", ProposalGroup.WaitingForYou,
                false, [], TrialStanding.Current, null, []) { IsUnread = true }], 0));
        var context = WorkspaceContextTests.NewContext(fake);
        var page = new ProposalsPageModel(context);
        await context.OpenProjectAsync(Path);
        await page.DetailReading;
        Assert.True(page.SelectedProposal!.IsUnread);
        Assert.Empty(fake.ReviewMarkRequests);

        context.OpenPage(WorkspacePage.Proposals);
        await page.ReadMarkWriting;

        Assert.False(page.SelectedProposal.IsUnread);
        Assert.Equal(ReviewMarkKeys.ProposalRead, Assert.Single(fake.ReviewMarkRequests).EntryKey);
    }

    private static FakeCommandClient NewFake()
    {
        var fake = new FakeCommandClient();
        fake.ProposalStandingsCompleteWith(new ProposalStandings(
            [new ProposalCard(Id, "Sound change", "proposed", ProposalGroup.WaitingForYou,
                false, [], TrialStanding.Current, null, [])], 0));
        return fake;
    }
}
