using SIL.Motif.App;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

public sealed class ProposalsPageModelTests
{
    private const string ProjectPath = @"C:\projects\one.fwdata";

    private static readonly BaselineToken Token = new("project-1", "sha256:" + new string('a', 64), "1",
        "2026-09-05T11:02:00Z", "sha256:" + new string('b', 64));

    [Fact]
    public async Task GroupsFollowTheResponse()
    {
        var (fake, context, page) = await OpenAsync(Standings(
            Card("p/waiting", "Waiting one", ProposalGroup.WaitingForYou),
            Card("p/stale", "Stale one", ProposalGroup.NeedsAnotherTry, standing: Stale),
            Card("p/new", "New one", ProposalGroup.NotTried),
            Card("p/mine", "Pending", ProposalGroup.NotAppliedYet, pending: true)));

        Assert.Equal(["Waiting for you", "Needs another try", "Not tried yet", "Your changes not applied yet"],
            page.Groups.Select(group => group.Title));
        Assert.Equal("Waiting one", Assert.Single(page.Groups[0].Cards).Label);
        Assert.False(page.IsEmpty);
        Assert.Equal(ProjectPath, Assert.Single(fake.ProposalStandingsRequests).ProjectPath);
        Assert.Equal(WorkspacePage.Overview, context.CurrentPage);
    }

    [Fact]
    public async Task OpenEntersProposalMode()
    {
        var (fake, context, page) = await OpenAsync(Standings(Card("p/waiting", "Waiting one",
            ProposalGroup.WaitingForYou)));
        fake.ReadTrialCompletesWith(Trial("p/waiting", TrialFreshness.Current));
        context.OpenPage(WorkspacePage.Proposals);

        await page.OpenCommand.ExecuteAsync(page.Groups[0].Cards[0]);

        Assert.Equal("p/waiting", context.Lens?.ProposalId);
        Assert.Equal(WorkspacePage.Overview, context.CurrentPage);
    }

    [Fact]
    public async Task TryAgainGoesThroughTheWindowsOneTryAgainAndFollowsTheTrial()
    {
        var (fake, _, page) = await OpenAsync(Standings(Card("p/stale", "Stale one",
            ProposalGroup.NeedsAnotherTry, kinds: [new("rule", 1, "1 rule")], standing: Stale)));
        fake.ProposalStandingsCompleteWith(Standings(Card("p/stale", "Stale one", ProposalGroup.WaitingForYou,
            standing: TrialStanding.Current)));

        await page.TryAgainCommand.ExecuteAsync(page.Groups[0].Cards[0]);

        var request = Assert.Single(fake.EnqueueTrialRequests);
        Assert.Equal("p/stale", request.ProposalId);
        Assert.Null(request.Words);
        Assert.False(request.AllWords);
        Assert.Single(fake.WaitForJobRequests);
        Assert.Equal(ProposalGroup.WaitingForYou, Assert.Single(page.Groups).Group);
    }

    [Fact]
    public async Task AGrammarProposalAndAnAnalysisProposalAskForTheSameThingFromThePage()
    {
        var (fake, _, page) = await OpenAsync(Standings(
            Card("p/rule", "Rule", ProposalGroup.NeedsAnotherTry, kinds: [new("rule", 1, "1 rule")], standing: Stale),
            Card("p/parse", "Parse", ProposalGroup.NeedsAnotherTry, kinds: [new("parse-approved", 1, "1 parse approved")],
                standing: Stale)));

        foreach (var card in page.Groups[0].Cards) await page.TryAgainCommand.ExecuteAsync(card);

        Assert.All(fake.EnqueueTrialRequests, request => Assert.False(request.AllWords));
        Assert.All(fake.EnqueueTrialRequests, request => Assert.Null(request.Words));
    }

    [Fact]
    public async Task ACardSaysTryingWhileTheWindowsTryAgainRunsAndOffersNoSecondTry()
    {
        var (fake, _, page) = await OpenAsync(Standings(Card("p/a", "A", ProposalGroup.NeedsAnotherTry,
            standing: Stale)));
        var waiting = new TaskCompletionSource<CommandOutcome<JobStatusResponse>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        fake.OnWaitForJob((_, _, _) => waiting.Task);

        var trying = page.TryAgainCommand.ExecuteAsync(page.Groups[0].Cards[0]);
        var card = page.Groups[0].Cards[0];

        Assert.Equal(TrialStanding.Trying, card.Standing);
        Assert.Equal("Trying…", card.StatusText);
        Assert.False(card.CanTry);
        waiting.SetResult(CommandOutcome<JobStatusResponse>.Success(Status(JobStatus.Completed)));
        await trying;
        Assert.NotEqual(TrialStanding.Trying, page.Groups[0].Cards[0].Standing);
    }

    [Fact]
    public async Task AFailedTryAgainShowsTheWindowsSentenceOnTheCard()
    {
        var (fake, _, page) = await OpenAsync(Standings(Card("p/a", "A", ProposalGroup.NeedsAnotherTry,
            standing: Stale)));
        fake.OnWaitForJob((_, _, _) =>
            Task.FromResult(CommandOutcome<JobStatusResponse>.Success(Status(JobStatus.Failed))));

        await page.TryAgainCommand.ExecuteAsync(page.Groups[0].Cards[0]);

        Assert.Equal("The check did not finish. Try it again.", page.Groups[0].Cards[0].ShownRefusal?.Sentence);
    }

    [Fact]
    public async Task TheBadgeCountsWaitingForYou()
    {
        var (_, _, page) = await OpenAsync(Standings(
            Card("p/a", "A", ProposalGroup.WaitingForYou), Card("p/b", "B", ProposalGroup.WaitingForYou),
            Card("p/c", "C", ProposalGroup.NeedsAnotherTry)));

        Assert.Equal("2", page.Badge);
    }

    [Fact]
    public void KindsTextUsesTheCommandsCountsInTheRightNumber()
    {
        Assert.Equal("2 rules · 1 allomorph · 3 parses approved", ProposalCardViewModel.KindsIn(
            [new("rule", 2, "2 rules"), new("allomorph", 1, "1 allomorph"),
                new("parse-approved", 3, "3 parses approved")]));
    }

    [Fact]
    public async Task NumbersShowBeforeAndAfterAndAChipNamesWhatWasLost()
    {
        var trial = Summary(TrialFreshness.Current);
        var (_, _, page) = await OpenAsync(Standings(Card("p/a", "A", ProposalGroup.WaitingForYou, trial: trial,
            also: ["p/other"], standing: TrialStanding.Current), Card("p/other", "Other",
            ProposalGroup.NeedsAnotherTry)));

        var card = page.Groups[0].Cards[0];

        Assert.Equal("60% → 64.5%", card.Numbers[0].Value);
        Assert.True(card.Numbers[0].Moved);
        Assert.Equal("10 of 12 → 9 of 12", card.Numbers[1].Value);
        Assert.Equal(["Coverage", "Approved kept", "Word time", "Warnings"], card.Numbers.Select(n => n.Label));
        Assert.Equal("1 lost", card.LostChip);
        Assert.Null(card.FitChip);
        Assert.Equal("also changes words in Other", card.AlsoChip);
        Assert.False(card.HasStatus);
        Assert.False(card.NumbersAreOld);
    }

    [Fact]
    public async Task NumbersFromAnEarlierTrySayWhatMadeThemOldInsteadOfRepeatingTheGroup()
    {
        var (_, _, page) = await OpenAsync(Standings(
            Card("p/a", "A", ProposalGroup.NeedsAnotherTry, trial: Summary(TrialFreshness.BaselineMoved),
                standing: Stale),
            Card("p/b", "B", ProposalGroup.NeedsAnotherTry, trial: Summary(TrialFreshness.ProposalChanged),
                standing: TrialStanding.NeedsAnotherTry(TrialStaleReason.ProposalChanged))));

        var baselineMoved = page.Groups[0].Cards[0];

        Assert.Equal("From the last try, before FieldWorks changed", baselineMoved.StatusText);
        Assert.Equal("From the last try, before this Proposal changed", page.Groups[0].Cards[1].StatusText);
        Assert.True(baselineMoved.NumbersAreOld);
        Assert.Equal("Try again", baselineMoved.TryText);
        Assert.True(baselineMoved.CanTry);
        Assert.DoesNotContain("Needs another try", baselineMoved.StatusText);
    }

    [Fact]
    public async Task AFailedTrialSaysSoAndOffersTryAgain()
    {
        var (_, _, page) = await OpenAsync(Standings(Card("p/a", "A", ProposalGroup.NeedsAnotherTry,
            standing: TrialStanding.Failed)));

        var card = page.Groups[0].Cards[0];

        Assert.Equal("The last try did not finish. Try again.", card.StatusText);
        Assert.Equal("Try again", card.TryText);
        Assert.True(card.CanTry);
    }

    [Fact]
    public async Task ButtonIdsFollowTheProposalNotItsPlace()
    {
        var (_, _, page) = await OpenAsync(Standings(Card("p/a", "A", ProposalGroup.WaitingForYou),
            Card("p/b", "B", ProposalGroup.NeedsAnotherTry, standing: Stale)));

        Assert.Equal(AutomationIds.ForProposalOpen("p/b"), page.Groups[1].Cards[0].OpenAutomationId);
        Assert.NotEqual(page.Groups[0].Cards[0].OpenAutomationId, page.Groups[1].Cards[0].OpenAutomationId);
    }

    [Fact]
    public async Task ANeverTriedProposalIsOfferedTryItUnderItsOwnGroupWithoutRepeatingTheGroupName()
    {
        var (_, _, page) = await OpenAsync(Standings(Card("p/a", "A", ProposalGroup.NotTried)));

        var card = page.Groups[0].Cards[0];

        Assert.Equal("Not tried yet", page.Groups[0].Title);
        Assert.False(card.HasStatus);
        Assert.Equal("Try it", card.TryText);
        Assert.True(card.CanTry);
        Assert.False(card.HasNumbers);
    }

    [Fact]
    public async Task YourChangesNotYetTriedSayNotTriedAndOfferTryIt()
    {
        var (_, _, page) = await OpenAsync(Standings(Card("p/mine", "Your changes", ProposalGroup.NotAppliedYet,
            pending: true)));

        var card = page.Groups[0].Cards[0];

        Assert.Equal("Not tried yet", card.StatusText);
        Assert.Equal("Try it", card.TryText);
        Assert.True(card.CanTry);
    }

    [Fact]
    public async Task AnAppliedProposalOffersNoTry()
    {
        var (_, _, page) = await OpenAsync(Standings(Card("p/done", "Done", ProposalGroup.SetAside)));

        Assert.False(page.Groups[0].Cards[0].CanTry);
    }

    [Fact]
    public async Task ARefusedReadShowsTheWindowsSentenceNotTheCommandsAndNeverSaysNoProposalsYet()
    {
        var fake = new FakeCommandClient();
        fake.OnProposalStandings((_, _) => Task.FromResult(CommandOutcome<ProposalStandings>.Refused(
            new Refusal("store.unavailable", FailureReason.Refused, "motif proposals failed for Draft 'pending'"))));
        var context = WorkspaceContextTests.NewContext(fake);
        var page = new ProposalsPageModel(context);

        await context.OpenProjectAsync(ProjectPath);

        Assert.Equal(ProposalsPageState.Refused, page.State);
        Assert.False(page.IsEmpty);
        Assert.False(page.IsLoading);
        Assert.NotNull(page.ShownRefusal);
        Assert.DoesNotContain("motif proposals", page.ShownRefusal.Sentence);
    }

    [Fact]
    public async Task ThePageIsLoadingNotEmptyUntilTheFirstReadReturns()
    {
        var fake = new FakeCommandClient();
        var read = new TaskCompletionSource<CommandOutcome<ProposalStandings>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        fake.OnProposalStandings((_, _) => read.Task);
        var context = WorkspaceContextTests.NewContext(fake);
        var page = new ProposalsPageModel(context);
        Assert.True(page.IsLoading);
        Assert.False(page.IsEmpty);

        var opening = context.OpenProjectAsync(ProjectPath);
        Assert.True(page.IsLoading);
        read.SetResult(CommandOutcome<ProposalStandings>.Success(new ProposalStandings([], 0)));
        await opening;

        Assert.True(page.IsEmpty);
        Assert.False(page.IsLoading);
    }

    [Fact]
    public async Task RefreshRegroupsTheOpenProposalUnderNeedsAnotherTry()
    {
        var (fake, context, page) = await OpenAsync(Standings(Card("p/a", "A", ProposalGroup.WaitingForYou)));
        fake.ReadTrialCompletesWith(Trial("p/a", TrialFreshness.Current));
        await context.OpenProposalAsync("p/a", CancellationToken.None);
        fake.ReadTrialCompletesWith(Trial("p/a", TrialFreshness.BaselineMoved));
        fake.ProposalStandingsCompleteWith(Standings(Card("p/a", "A", ProposalGroup.NeedsAnotherTry,
            trial: Summary(TrialFreshness.BaselineMoved), standing: Stale)));

        await context.PublishBaselineCapturedAsync(openFirstRunSetup: false);

        Assert.Equal(ProposalGroup.NeedsAnotherTry, Assert.Single(page.Groups).Group);
        Assert.Equal("From the last try, before FieldWorks changed", page.Groups[0].Cards[0].StatusText);
        Assert.Empty(fake.EnqueueTrialRequests);
    }

    [Theory]
    [InlineData("proposed", false, "Ready")]
    [InlineData("proposed", true, "Draft")]
    [InlineData("deferred", false, "Deferred")]
    [InlineData("applied", false, "Applied")]
    [InlineData("rejected", false, "Declined")]
    [InlineData("superseded", false, "Superseded")]
    [InlineData("withdrawn", false, "Withdrawn")]
    public void EachCardNamesItsStatusInTheWindowsWords(string status, bool isDraft, string word)
    {
        var card = new ProposalCardViewModel(Card("p/1", "One", ProposalGroup.SetAside) with
        {
            Status = status, IsDraft = isDraft,
        });

        Assert.Equal(word, card.StatusWord);
    }

    [Fact]
    public void YourOwnChangesCarryNoStatusWord() =>
        Assert.False(new ProposalCardViewModel(Card("p/mine", "Mine", ProposalGroup.NotAppliedYet, pending: true))
            .HasStatusWord);

    [Theory]
    [InlineData("rejected", true)]
    [InlineData("superseded", true)]
    [InlineData("withdrawn", true)]
    [InlineData("applied", false)]
    [InlineData("proposed", false)]
    public void OnlyAClosedProposalThatNeverAppliedOffersDelete(string status, bool offered) =>
        Assert.Equal(offered, new ProposalCardViewModel(Card("p/1", "One", ProposalGroup.SetAside) with
        {
            Status = status,
        }).CanDelete);

    [Theory]
    [InlineData("rejected", true)]
    [InlineData("withdrawn", true)]
    [InlineData("superseded", false)]
    [InlineData("applied", false)]
    [InlineData("proposed", false)]
    public void OnlyADeclinedOrWithdrawnProposalOffersReopen(string status, bool offered) =>
        Assert.Equal(offered, new ProposalCardViewModel(Card("p/1", "One", ProposalGroup.SetAside) with
        {
            Status = status,
        }).CanReopen);

    [Fact]
    public async Task ReopenOpensTheProposalAgainAndReadsTheListAgain()
    {
        var (fake, _, page) = await OpenAsync(new ProposalStandings(
            [Card("p/declined", "Declined", ProposalGroup.SetAside) with { Status = "rejected" }], 1));
        var reads = fake.ProposalStandingsRequests.Count;

        await page.ReopenCommand.ExecuteAsync(page.Groups[0].Cards[0]);

        Assert.Equal("p/declined", Assert.Single(fake.ReopenProposalRequests).ProposalId);
        Assert.Equal(reads + 1, fake.ProposalStandingsRequests.Count);
    }

    [Fact]
    public async Task DeleteRemovesTheProposalAndReadsTheListAgain()
    {
        var (fake, _, page) = await OpenAsync(new ProposalStandings(
            [Card("p/declined", "Declined", ProposalGroup.SetAside) with { Status = "rejected" }], 1));
        var reads = fake.ProposalStandingsRequests.Count;

        await page.DeleteCommand.ExecuteAsync(page.Groups[0].Cards[0]);

        Assert.Equal("p/declined", Assert.Single(fake.DeleteProposalRequests).ProposalId);
        Assert.Equal(reads + 1, fake.ProposalStandingsRequests.Count);
        Assert.Null(page.ShownRefusal);
    }

    [Fact]
    public async Task ShowClosedTogglesTheClosedProposals()
    {
        var (fake, _, page) = await OpenAsync(new ProposalStandings([], 2));
        Assert.Equal("Show closed (2)", page.SetAsideLinkText);

        await page.ShowSetAsideCommand.ExecuteAsync(null);
        Assert.True(fake.ProposalStandingsRequests[^1].IncludeSetAside);
        Assert.Equal("Hide closed", page.SetAsideLinkText);

        await page.ShowSetAsideCommand.ExecuteAsync(null);
        Assert.False(fake.ProposalStandingsRequests[^1].IncludeSetAside);
        Assert.Equal("Show closed (2)", page.SetAsideLinkText);
    }

    private static readonly TrialStanding Stale = TrialStanding.NeedsAnotherTry(TrialStaleReason.BaselineMoved);

    private static async Task<(FakeCommandClient Fake, WorkspaceContext Context, ProposalsPageModel Page)> OpenAsync(
        ProposalStandings standings)
    {
        var fake = new FakeCommandClient();
        fake.ProposalStandingsCompleteWith(standings);
        var context = WorkspaceContextTests.NewContext(fake);
        var page = new ProposalsPageModel(context);
        await context.OpenProjectAsync(ProjectPath);
        return (fake, context, page);
    }

    private static JobStatusResponse Status(JobStatus status) =>
        new("job/test", "project/test", true, "trial", status, 1, null, false, null, 1);

    private static TrialSummary Summary(TrialFreshness freshness) => new("job/1", DateTimeOffset.UtcNow, freshness,
        TrialWordScope.AllWords,
        new Paired<double?>(60, 64.5, true, true, true, null), new Paired<int>(10, 9, true, true, true, null),
        new Paired<int>(12, 12, true, true, false, null), new Paired<double?>(2500, 2600, true, true, false, null),
        new Paired<int?>(3, 3, true, true, false, null), 1, 0);

    private static ProposalStandings Standings(params ProposalCard[] cards) => new(cards, 0);

    private static ProposalCard Card(string id, string label, ProposalGroup group, bool pending = false,
        IReadOnlyList<ChangeKindCount>? kinds = null, TrialSummary? trial = null, string[]? also = null,
        TrialStanding? standing = null) =>
        new(id, label, "proposed", group, pending, kinds ?? [], standing ?? TrialStanding.NotTried, trial, also ?? []);

    private static TrialRecord Trial(string id, TrialFreshness freshness) => new("job/trial", id, "sha256:intent",
        Token, null, new Dictionary<string, string> { [AssessmentKinds.ParseTime] = "assessment/trial" },
        ["dogs"], true, null, JobStatus.Completed, DateTimeOffset.UtcNow, freshness);
}
