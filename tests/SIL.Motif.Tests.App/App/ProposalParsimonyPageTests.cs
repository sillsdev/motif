using SIL.Motif.App.ViewModels;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Pins when the Proposals page asks for a Proposal's Parsimony status, that a finishing Trial hands over to it with no
/// click, and that nothing couples a status to Apply or Readiness: those tests show no accidental link, they are not
/// a check of Proposal-mode Review.
/// </summary>
public sealed class ProposalParsimonyPageTests
{
    private const string ProjectPath = @"C:\projects\advice.fwdata";

    [Fact]
    public async Task ChoosingAProposalReadsItsStatusOnceAndChoosingAnotherReadsTheOther()
    {
        var (fake, _, page) = await OpenAsync(Card("p/one"), Card("p/two"));

        await page.ParsimonyWatching;
        Assert.Equal(["p/one"], fake.ProposalParsimonyStatusRequests.Select(request => request.ProposalId));
        Assert.True(page.Parsimony.IsVisible);
        Assert.Equal("Not Yet Run", page.Parsimony.StateLabel);

        page.SelectProposalCommand.Execute(page.Groups.SelectMany(group => group.Cards).Single(card => card.ProposalId == "p/two"));
        await page.ParsimonyWatching;

        Assert.Equal(["p/one", "p/two"], fake.ProposalParsimonyStatusRequests.Select(request => request.ProposalId));
        Assert.Equal("p/two", page.Parsimony.ProposalId);
        Assert.All(fake.ProposalParsimonyStatusRequests, request => Assert.Equal(ProjectPath, request.FwDataPath));
    }

    [Fact]
    public async Task APendingDraftIsNeverReadBecauseItHasNoTrial()
    {
        var (fake, _, page) = await OpenAsync(Card("p/mine", pending: true));

        Assert.Equal("p/mine", page.SelectedProposal?.ProposalId);
        await page.ParsimonyWatching;
        Assert.Empty(fake.ProposalParsimonyStatusRequests);
        Assert.False(page.Parsimony.IsVisible);
    }

    [Fact]
    public async Task ClearingTheProjectClosesTheSectionAndStopsReadingARunningJob()
    {
        var fake = new FakeCommandClient();
        fake.ProposalStandingsCompleteWith(new ProposalStandings([Card("p/one")], 0));
        var reads = 0;
        fake.OnProposalParsimonyStatus((request, _) =>
        {
            reads++;
            return Task.FromResult(Contract.Commands.CommandOutcome<ProposalParsimonyStatus>.Success(
                ProposalParsimonyStatusFixtures.Running(request.ProposalId)));
        });
        var context = WorkspaceContextTests.NewContext(fake);
        var page = new ProposalsPageModel(context);
        await context.OpenProjectAsync(ProjectPath);
        while (reads == 0) await Task.Yield();
        Assert.Equal("Running", page.Parsimony.StateLabel);

        page.ProjectCleared();
        await page.ParsimonyWatching;

        Assert.False(page.Parsimony.IsVisible);
        Assert.Null(page.Parsimony.ProposalId);
        Assert.Equal(string.Empty, page.Parsimony.Sentence);
        Assert.Equal(1, reads);
    }

    [Fact]
    public async Task StoppingThePageWorkStopsTheWatch()
    {
        var (_, _, page) = await OpenAsync(Card("p/one"));
        await page.ParsimonyWatching;

        await page.StopWorkAsync();

        Assert.False(page.Parsimony.IsVisible);
        Assert.Null(page.Parsimony.ProposalId);
    }

    [Theory]
    [InlineData(ProposalParsimonyFailureCategory.TimedOut)]
    [InlineData(ProposalParsimonyFailureCategory.ParserUnavailable)]
    [InlineData(ProposalParsimonyFailureCategory.Internal)]
    public async Task AFailedStatusIsNotCoupledToApplyOrReadiness(ProposalParsimonyFailureCategory category)
    {
        // Review changes in a state where Apply is open, then the same page while advice failed for the Proposal.
        var fake = new FakeCommandClient();
        fake.PendingChangesIs(Snapshot("kept"));
        fake.MeasurePendingCompletesWith(SIL.Motif.Tests.Support.TestFixtures.PendingMeasureResponseFactory.Create(
            "job/one", "revision/one", FakeCommandClient.CompleteNumbers));
        fake.ProposalStandingsCompleteWith(new ProposalStandings([Card("p/one")], 0));
        var context = WorkspaceContextTests.NewContext(fake);
        var review = new ReviewPageModel(context);
        var proposals = new ProposalsPageModel(context);
        await context.OpenProjectAsync(ProjectPath);
        await review.MeasureCommand.ExecuteAsync(null);
        await proposals.ParsimonyWatching;
        var before = ApplyFacts(review);
        Assert.StartsWith("True|", before);

        fake.ProposalParsimonyStatusIs(ProposalParsimonyStatusFixtures.Failed("p/one", category));
        await proposals.Parsimony.Watch(ProjectPath, "p/one");

        Assert.Equal("Did Not Finish", proposals.Parsimony.StateLabel);
        Assert.Equal(before, ApplyFacts(review));
        Assert.True(review.ApplyCommand.CanExecute(null));
    }

    [Fact]
    public async Task AFailedStatusIsNotCoupledToABlockedApply()
    {
        var fake = new FakeCommandClient();
        fake.PendingChangesIs(Snapshot("kept"));
        fake.ProposalStandingsCompleteWith(new ProposalStandings([Card("p/one")], 0));
        var context = WorkspaceContextTests.NewContext(fake);
        var review = new ReviewPageModel(context);
        var proposals = new ProposalsPageModel(context);
        await context.OpenProjectAsync(ProjectPath);
        var before = ApplyFacts(review);
        Assert.StartsWith("False|", before);
        Assert.NotEmpty(review.ApplyBlockers);

        fake.ProposalParsimonyStatusIs(ProposalParsimonyStatusFixtures.Failed(
            "p/one", ProposalParsimonyFailureCategory.TimedOut, code: "status-skipped-timeout"));
        await proposals.Parsimony.Watch(ProjectPath, "p/one");

        Assert.Equal("Did Not Finish", proposals.Parsimony.StateLabel);
        Assert.Equal(before, ApplyFacts(review));
    }

    [Fact]
    public async Task ATrialThatFinishesHandsOverToTheSectionThroughQueuedAndRunningToReadyWithNoClick()
    {
        // The section is single-thread by design; on pool threads two watch continuations can interleave.
        await SingleThreadPump.RunAsync(async () =>
        {
            var fake = new FakeCommandClient();
            fake.ProposalStandingsCompleteWith(new ProposalStandings([Card("p/one")], 0));
            var afterTrial = new Queue<ProposalParsimonyStatus>(
            [
                ProposalParsimonyStatusFixtures.Waiting("p/one", "trial"),
                ProposalParsimonyStatusFixtures.Running("p/one"),
                ProposalParsimonyStatusFixtures.Ready("p/one"),
            ]);
            var trialDone = false;
            fake.OnProposalParsimonyStatus((request, _) => Task.FromResult(Contract.Commands.CommandOutcome<ProposalParsimonyStatus>.Success(
                !trialDone ? ProposalParsimonyStatusFixtures.NotYetRun(request.ProposalId)
                    : afterTrial.Count > 1 ? afterTrial.Dequeue() : afterTrial.Peek())));
            var finishing = new TaskCompletionSource<Contract.Commands.CommandOutcome<JobStatusResponse>>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            fake.OnWaitForJob((_, _, _) => finishing.Task);
            var context = WorkspaceContextTests.NewContext(fake);
            var page = new ProposalsPageModel(context, (_, _) => Task.CompletedTask);
            var seen = new List<string>();
            page.Parsimony.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ProposalParsimonyViewModel.StateLabel) && page.Parsimony.StateLabel.Length > 0 &&
                    (seen.Count == 0 || seen[^1] != page.Parsimony.StateLabel)) seen.Add(page.Parsimony.StateLabel);
            };
            await context.OpenProjectAsync(ProjectPath);
            await page.ParsimonyWatching;
            Assert.Equal("Not Yet Run", page.Parsimony.StateLabel);
            Assert.Contains("no Trial", page.Parsimony.Sentence, StringComparison.OrdinalIgnoreCase);

            var trying = page.TryAgainCommand.ExecuteAsync(page.SelectedProposal);
            trialDone = true;
            finishing.SetResult(Contract.Commands.CommandOutcome<JobStatusResponse>.Success(
                new JobStatusResponse("job/test", "project/test", true, "trial", JobStatus.Completed, 1, null, false, null, 1)));
            await trying;
            await page.ParsimonyWatching;

            Assert.Equal(["Not Yet Run", "Queued", "Running", "Ready"], seen);
            Assert.Equal("Ready", page.Parsimony.StateLabel);
            Assert.Empty(fake.EnqueueParsimonyRequests);
            Assert.Single(fake.EnqueueTrialRequests);
        });
    }

    [Fact]
    public void OnlyTheProposalsPageAndItsSectionKnowAboutParsimonyStatus()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Motif.sln"))) root = root.Parent;
        Assert.NotNull(root);
        var allowed = new[] { "ProposalParsimony", "ProposalsPageModel", "ICommandClient", "CommandClient", "MotifAppStrings", "AutomationIds" };
        var users = Directory.EnumerateFiles(Path.Combine(root.FullName, "src", "SIL.Motif.App"), "*.cs", SearchOption.AllDirectories)
            .Where(path => File.ReadAllText(path).Contains("ProposalParsimony", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .Where(name => !allowed.Any(prefix => name!.StartsWith(prefix, StringComparison.Ordinal)));

        Assert.Empty(users);
    }

    // Everything Review changes says about Apply, as one string so a change anywhere shows in a comparison.
    private static string ApplyFacts(ReviewPageModel review) => string.Join("|", review.CanApply,
        string.Join(",", review.ApplyBlockers.Select(blocker => blocker.Kind)), review.ApplyBlockReason,
        review.ApplyDisabledReason, string.Join(",", review.ApplyChecks.Select(check => check.Text)));

    private static async Task<(FakeCommandClient Fake, WorkspaceContext Context, ProposalsPageModel Page)> OpenAsync(
        params ProposalCard[] cards)
    {
        var fake = new FakeCommandClient();
        fake.ProposalStandingsCompleteWith(new ProposalStandings(cards, 0));
        var context = WorkspaceContextTests.NewContext(fake);
        var page = new ProposalsPageModel(context);
        await context.OpenProjectAsync(ProjectPath);
        return (fake, context, page);
    }

    private static ProposalCard Card(string id, bool pending = false) => new(id, id, "proposed",
        pending ? ProposalGroup.NotAppliedYet : ProposalGroup.WaitingForYou, pending, [], TrialStanding.Current, null, []);

    private static PendingChangesSnapshot Snapshot(string id) => new("draft/one", "revision/one",
        [new PendingChange(id, "wordform/" + id, id, "approve", "assessment/one", "reading", ["operation/" + id])],
        [new ChangeFit(id, true, [])]);
}
