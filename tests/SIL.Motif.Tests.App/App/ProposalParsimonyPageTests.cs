using SIL.Motif.App.ViewModels;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Pins explicit Proposal Parsimony reads, their lifecycle and their independence from Apply and readiness.
/// </summary>
public sealed class ProposalParsimonyPageTests
{
    private const string ProjectPath = @"C:\projects\advice.fwdata";

    [Fact]
    public async Task ChoosingAProposalReadsItsStatusOnceAndChoosingAnotherReadsTheOther()
    {
        var (fake, _, page) = await OpenAsync(Card("p/one"), Card("p/two"));

        Assert.Empty(fake.ProposalParsimonyStatusRequests);
        await page.Parsimony.Watch(ProjectPath, "p/one");
        Assert.Equal(["p/one"], fake.ProposalParsimonyStatusRequests.Select(request => request.ProposalId));
        Assert.True(page.Parsimony.IsVisible);
        Assert.Equal("Not Yet Run", page.Parsimony.StateLabel);

        page.SelectProposalCommand.Execute(page.Groups.SelectMany(group => group.Cards).Single(card => card.ProposalId == "p/two"));
        Assert.Single(fake.ProposalParsimonyStatusRequests);
        await page.Parsimony.Watch(ProjectPath, "p/two");

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
        var watching = page.Parsimony.Watch(ProjectPath, "p/one");
        await Wait.UntilAsync(() => reads > 0, "The explicit Parsimony watch did not start.");
        Assert.Equal("Running", page.Parsimony.StateLabel);

        page.ProjectCleared();
        await watching.WaitAsync(TimeSpan.FromSeconds(10));

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
        var proposals = new ProposalsPageModel(context);
        await context.OpenProjectAsync(ProjectPath);
        await context.PendingMeasurement.MeasureCommand.ExecuteAsync(null);
        await proposals.ParsimonyWatching;
        var before = ApplyFacts(context);
        Assert.StartsWith("True|", before);

        fake.ProposalParsimonyStatusIs(ProposalParsimonyStatusFixtures.Failed("p/one", category));
        await proposals.Parsimony.Watch(ProjectPath, "p/one");

        Assert.Equal("Did Not Finish", proposals.Parsimony.StateLabel);
        Assert.Equal(before, ApplyFacts(context));
        Assert.True(context.ProposalApply.CanApply(null, null, null, null));
    }

    [Fact]
    public async Task AFailedStatusIsNotCoupledToABlockedApply()
    {
        var fake = new FakeCommandClient();
        fake.PendingChangesIs(Snapshot("kept"));
        fake.ProposalStandingsCompleteWith(new ProposalStandings([Card("p/one")], 0));
        var context = WorkspaceContextTests.NewContext(fake);
        var proposals = new ProposalsPageModel(context);
        await context.OpenProjectAsync(ProjectPath);
        var before = ApplyFacts(context);
        Assert.StartsWith("False|", before);
        Assert.NotEmpty(context.ProposalApply.Blockers(null, null, null, null));

        fake.ProposalParsimonyStatusIs(ProposalParsimonyStatusFixtures.Failed(
            "p/one", ProposalParsimonyFailureCategory.TimedOut, code: "status-skipped-timeout"));
        await proposals.Parsimony.Watch(ProjectPath, "p/one");

        Assert.Equal("Did Not Finish", proposals.Parsimony.StateLabel);
        Assert.Equal(before, ApplyFacts(context));
    }

    [Fact]
    public async Task AnExplicitWatchAfterATrialShowsQueuedRunningAndReady()
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
            await context.OpenProjectAsync(ProjectPath);
            page.Parsimony.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ProposalParsimonyViewModel.StateLabel) && page.Parsimony.StateLabel.Length > 0 &&
                    (seen.Count == 0 || seen[^1] != page.Parsimony.StateLabel)) seen.Add(page.Parsimony.StateLabel);
            };
            await page.Parsimony.Watch(ProjectPath, "p/one");
            Assert.Equal("Not Yet Run", page.Parsimony.StateLabel);
            Assert.Contains("no Trial", page.Parsimony.Sentence, StringComparison.OrdinalIgnoreCase);

            var trying = page.TryAgainCommand.ExecuteAsync(page.SelectedProposal);
            trialDone = true;
            finishing.SetResult(Contract.Commands.CommandOutcome<JobStatusResponse>.Success(
                new JobStatusResponse("job/test", "project/test", true, "trial", JobStatus.Completed, 1, null, false, null, 1)));
            await trying;
            await page.Parsimony.Watch(ProjectPath, "p/one");

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
        var allowed = new[] { "ProposalDocumentViewModel", "ProposalParsimony", "ProposalDiagnostics", "ProposalsPageModel", "ICommandClient", "CommandClient", "MotifAppStrings", "AutomationIds" };
        var users = Directory.EnumerateFiles(Path.Combine(root.FullName, "src", "SIL.Motif.App"), "*.cs", SearchOption.AllDirectories)
            .Where(path => File.ReadAllText(path).Contains("ProposalParsimony", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .Where(name => !allowed.Any(prefix => name!.StartsWith(prefix, StringComparison.Ordinal)));

        Assert.Empty(users);
    }

    private static string ApplyFacts(WorkspaceContext context) => string.Join("|",
        context.ProposalApply.CanApply(null, null, null, null),
        string.Join(",", context.ProposalApply.Blockers(null, null, null, null).Select(blocker => blocker.Kind)));

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
