using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using SIL.Motif.App;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// A Proposal document never sits on "Reading Proposal" for good: past a bound it says why and offers Read Again,
/// whichever action started the read (Try It, Make Difference, the Dry Run record, Decide Later, Apply).
/// </summary>
[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class ProposalReadStuckTests
{
    private const string Project = @"C:\projects\one.fwdata";
    private static readonly TimeSpan Short = TimeSpan.FromMilliseconds(150);

    private static async Task Until(Func<bool> condition)
    {
        var until = DateTime.UtcNow.AddSeconds(15);
        while (!condition())
        {
            if (DateTime.UtcNow > until) throw new TimeoutException("The condition never became true.");
            await Task.Delay(20);
        }
    }

    // A read that ends only when told to, and ignores its token as a hung call would.
    private static Func<ShowProposalRequest, CancellationToken, Task<CommandOutcome<ProposalDetailProjection>>> Hung(
        TaskCompletionSource gate) => async (_, _) =>
    {
        await gate.Task;
        return default!;
    };

    private static void Reads(FakeCommandClient fake) => fake.ShowProposalHandler = (request, _) =>
        Task.FromResult(CommandOutcome<ProposalDetailProjection>.Success(
            new(request.ProposalId, "proposed", "Clarify the gloss", null, "intent/one", [])));

    private static async Task<(FakeCommandClient Fake, WorkspaceContext Context, OpenProposalDocuments Documents,
        ProposalDocumentViewModel Document)> OpenAsync(TrialStanding standing)
    {
        var fake = new FakeCommandClient();
        var trial = new TrialRecord("job/one", "one", "intent/one", WorkspaceContextTests.Assessment().Baseline.Token,
            null, new Dictionary<string, string>(), [], false, null, SIL.Motif.Contract.Jobs.JobStatus.Completed,
            DateTimeOffset.UtcNow, TrialFreshness.Current);
        fake.ReadTrialCompletesWith(trial);
        fake.Review = new ProposalReviewResponse("one", "intent/one", "revision/one",
            [new GrammarChangeEntry("entry", 0, "A change", ["op/one"], [], "content", false,
                "composer", "A change", [], [], [])], trial, new(true, false, false, null, [], false), [], 0);
        ProposalStatusFakes.GiveItASavedDifference(fake);
        fake.PendingChangesIs(new PendingChangesSnapshot("one", "revision/one", [], []));
        Reads(fake);
        var context = WorkspaceContextTests.NewContext(fake);
        await context.OpenProjectAsync("project.fwdata");
        var documents = new OpenProposalDocuments(context);
        var document = documents.Open("one");
        document.Card = new ProposalCardViewModel(new("one", "Clarify the gloss", "proposed",
            ProposalGroup.NotAppliedYet, false, [], standing, null, []));
        await document.RefreshAsync();
        document.StuckReadAfter = Short;
        return (fake, context, documents, document);
    }

    private static async Task Settle(WorkspaceContext context, OpenProposalDocuments documents)
    {
        await documents.StopAsync();
        await context.StopProjectWorkAsync();
    }

    private static async Task AssertStuckThenReadAgain(FakeCommandClient fake, ProposalDocumentViewModel document,
        TaskCompletionSource gate)
    {
        await Until(() => document.ShowsReadStuck);
        Assert.False(document.ShowsReadingLine);
        Assert.True(document.ReadAgainCommand.CanExecute(null));
        var requests = 0;
        fake.ShowProposalHandler = (request, _) =>
        {
            requests++;
            return Task.FromResult(CommandOutcome<ProposalDetailProjection>.Success(
                new(request.ProposalId, "proposed", "Clarify the gloss", null, "intent/one", [])));
        };

        document.ReadAgainCommand.Execute(null);
        await document.Reading;

        Assert.True(requests > 0);
        Assert.False(document.IsLoading);
        Assert.False(document.ShowsReadStuck);
        Assert.NotNull(document.Detail);
        gate.TrySetCanceled();
    }

    [Fact]
    public async Task ASlowReadSaysItIsReadingFirstAndThenOffersReadAgain()
    {
        var (fake, context, documents, document) = await OpenAsync(TrialStanding.Current);
        var gate = new TaskCompletionSource();
        fake.ShowProposalHandler = Hung(gate);
        try
        {
            _ = document.RefreshAsync();
            Assert.True(document.ShowsReadingLine);
            Assert.False(document.ShowsReadStuck);

            await AssertStuckThenReadAgain(fake, document, gate);
        }
        finally { gate.TrySetCanceled(); await Settle(context, documents); }
    }

    [Fact]
    public async Task AReadThatFinishesInTimeNeverOffersReadAgain()
    {
        var (_, context, documents, document) = await OpenAsync(TrialStanding.Current);
        try
        {
            await document.RefreshAsync();
            await Task.Delay(Short * 3);

            Assert.False(document.ShowsReadStuck);
            Assert.False(document.ReadTimedOut);
        }
        finally { await Settle(context, documents); }
    }

    [Fact]
    public async Task AfterTryItTheStuckReadOffersReadAgain()
    {
        var fake = new FakeCommandClient();
        Reads(fake);
        fake.ProposalStandingsCompleteWith(new ProposalStandings([new ProposalCard("p/one", "One", "proposed",
            ProposalGroup.NeedsAnotherTry, false, [], TrialStanding.NotTried, null, [])], 0));
        var context = WorkspaceContextTests.NewContext(fake);
        var page = new ProposalsPageModel(context);
        await context.OpenProjectAsync(Project);
        var gate = new TaskCompletionSource();
        try
        {
            page.SelectedProposal = page.Groups[0].Cards[0];
            await page.DetailReading;
            var document = page.SelectedDocument!;
            document.StuckReadAfter = Short;
            fake.ShowProposalHandler = Hung(gate);

            _ = page.TryAgainCommand.ExecuteAsync(page.Groups[0].Cards[0]);

            await AssertStuckThenReadAgain(fake, document, gate);
        }
        finally { gate.TrySetCanceled(); await page.StopWorkAsync(); await context.StopProjectWorkAsync(); }
    }

    [Fact]
    public async Task AfterDecideLaterTheStuckReadOffersReadAgain()
    {
        var fake = new FakeCommandClient();
        Reads(fake);
        fake.ProposalStandingsCompleteWith(new ProposalStandings([new ProposalCard("p/one", "One", "proposed",
            ProposalGroup.WaitingForYou, false, [], TrialStanding.Current, null, [])], 0));
        var context = WorkspaceContextTests.NewContext(fake);
        var page = new ProposalsPageModel(context);
        await context.OpenProjectAsync(Project);
        var gate = new TaskCompletionSource();
        try
        {
            page.SelectedProposal = page.Groups[0].Cards[0];
            await page.DetailReading;
            var document = page.SelectedDocument!;
            document.StuckReadAfter = Short;
            fake.ShowProposalHandler = Hung(gate);

            _ = page.DecideLaterCommand.ExecuteAsync(page.SelectedProposal);

            await AssertStuckThenReadAgain(fake, document, gate);
        }
        finally { gate.TrySetCanceled(); await page.StopWorkAsync(); await context.StopProjectWorkAsync(); }
    }

    [Fact]
    public async Task AfterTheDryRunRecordAReadThatIgnoresItsTokenStillLetsReadAgainFinishTheRun()
    {
        var (fake, context, documents, document) = await OpenAsync(TrialStanding.Current);
        var gate = new TaskCompletionSource();
        var ready = fake.Review;
        fake.Review = ready with { Readiness = ready.Readiness! with { MissingBoundDryRun = true } };
        await document.RefreshAsync();
        try
        {
            fake.WaitForDryRunHandler = (request, _, _) =>
            {
                fake.ShowProposalHandler = Hung(gate);
                return Task.FromResult(CommandOutcome<DryRunProjection>.Success(
                    new(request.ProposalId, "intent/one", "", [], "effect", "footprint") { EvidenceCurrent = true }));
            };
            var running = document.RunDryRunCommand.ExecuteAsync(null);

            await Until(() => document.ShowsReadStuck);
            Reads(fake);
            document.ReadAgainCommand.Execute(null);
            await running.WaitAsync(TimeSpan.FromSeconds(15));
            await document.Reading;

            Assert.False(document.IsLoading);
            Assert.False(document.IsDryRunActive);
        }
        finally { gate.TrySetCanceled(); await Settle(context, documents); }
    }

    [Fact]
    public async Task ReadAgainStaysPressableWhenTheNewReadHangsToo()
    {
        var (fake, context, documents, document) = await OpenAsync(TrialStanding.Current);
        var gate = new TaskCompletionSource();
        fake.ShowProposalHandler = Hung(gate);
        try
        {
            _ = document.RefreshAsync();
            await Until(() => document.ShowsReadStuck);
            document.ReadAgainCommand.Execute(null);
            Assert.False(document.ShowsReadStuck);

            await Until(() => document.ShowsReadStuck);
            Assert.True(document.ReadAgainCommand.CanExecute(null));
            Reads(fake);
            document.ReadAgainCommand.Execute(null);
            await document.Reading;

            Assert.False(document.IsLoading);
            Assert.NotNull(document.Detail);
        }
        finally { gate.TrySetCanceled(); await Settle(context, documents); }
    }

    [Fact]
    public async Task ClosingADocumentWhoseReadIgnoresItsTokenStillCloses()
    {
        var (fake, context, documents, document) = await OpenAsync(TrialStanding.Current);
        var gate = new TaskCompletionSource();
        fake.ShowProposalHandler = Hung(gate);
        try
        {
            _ = document.RefreshAsync();
            await Until(() => document.ShowsReadStuck);

            await documents.StopAsync().WaitAsync(TimeSpan.FromSeconds(10));

            Assert.False(document.IsLoading);
        }
        finally { gate.TrySetCanceled(); await context.StopProjectWorkAsync(); }
    }

    [Fact]
    public async Task RepeatedReadAgainDoesNotGrowTheTrackedReads()
    {
        var (fake, context, documents, document) = await OpenAsync(TrialStanding.Current);
        var gate = new TaskCompletionSource();
        fake.ShowProposalHandler = Hung(gate);
        try
        {
            for (var i = 0; i < 6; i++) document.ReadAgainCommand.Execute(null);

            await Until(() => document.TrackedReads <= 1);
        }
        finally { gate.TrySetCanceled(); await Settle(context, documents); }
    }

    [Fact]
    public async Task ADayOfSlowReadsLeavesNoWatchRunningAfterTheReadEnds()
    {
        var (_, context, documents, document) = await OpenAsync(TrialStanding.Current);
        try
        {
            document.StuckReadAfter = TimeSpan.FromMilliseconds(100);
            await document.RefreshAsync();
            await Task.Delay(300);

            Assert.False(document.ReadTimedOut);
        }
        finally { await Settle(context, documents); }
    }

    [Fact]
    public async Task AfterApplyTheStuckReadOffersReadAgain()
    {
        var (fake, context, documents, document) = await OpenAsync(TrialStanding.Current);
        var gate = new TaskCompletionSource();
        try
        {
            var receipt = new ApplyProjection("one", false, "Saved", [], "effect",
                new AppliedLogEntrySummary("one", "now", "Person", "intent/one"));
            fake.ApplyProposalHandler = (_, _) =>
            {
                fake.ShowProposalHandler = Hung(gate);
                return Task.FromResult(CommandOutcome<ApplyPendingResult>.Success(
                    ApplyPendingResult.AppliedWith(receipt, "Saved")));
            };
            await document.PrepareApplyCommand.ExecuteAsync(null);
            Assert.True(document.IsConfirmingApply);

            _ = document.ConfirmApplyCommand.ExecuteAsync(null);

            await AssertStuckThenReadAgain(fake, document, gate);
        }
        finally { gate.TrySetCanceled(); await Settle(context, documents); }
    }

    [Fact]
    public async Task MakeDifferenceComesBackAfterReadAgainWhenTheReadHadStuck()
    {
        var (fake, context, documents, document) = await OpenAsync(TrialStanding.Current);
        var gate = new TaskCompletionSource();
        fake.ShowProposalHandler = Hung(gate);
        try
        {
            _ = document.RefreshAsync();
            await Until(() => document.ShowsReadStuck);
            Assert.False(document.ShowsDifferenceAction);

            Reads(fake);
            document.ReadAgainCommand.Execute(null);
        await document.Reading;

            Assert.False(document.IsLoading);
            Assert.True(document.ShowsReadiness);
            Assert.True(document.Difference.IsShown || document.Difference.ShowsMakeControls);
        }
        finally { gate.TrySetCanceled(); await Settle(context, documents); }
    }

    [Fact]
    public async Task ReopeningAStuckDocumentReadsAgainInsteadOfJoiningTheStuckRead()
    {
        var (fake, context, documents, document) = await OpenAsync(TrialStanding.Current);
        var gate = new TaskCompletionSource();
        fake.ShowProposalHandler = Hung(gate);
        try
        {
            _ = document.RefreshAsync();
            await Until(() => document.ReadTimedOut);
            Reads(fake);

            await document.EnsureLoadedAsync().WaitAsync(TimeSpan.FromSeconds(15));

            Assert.False(document.IsLoading);
            Assert.NotNull(document.Detail);
        }
        finally { gate.TrySetCanceled(); await Settle(context, documents); }
    }

    [Fact]
    public void ReadAgainShowsWithItsReasonAndIsAnEnabledButton()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (fake, context, documents, document) = await OpenAsync(TrialStanding.Current);
            var gate = new TaskCompletionSource();
            var page = new ProposalDocument { DataContext = document };
            var window = new MotifTestWindow { Width = 1000, Height = 900, Content = new ScrollViewer { Content = page } };
            try
            {
                window.Show();
                fake.ShowProposalHandler = Hung(gate);
                document.StuckReadAfter = TimeSpan.FromSeconds(2);
                _ = document.RefreshAsync();
                PageScreenshots.Settle(window);
                Assert.Contains(page.GetVisualDescendants().OfType<TextBlock>(),
                    block => block.IsEffectivelyVisible && block.Text == MotifAppStrings.ProposalReading);

                await Until(() => document.ShowsReadStuck);
                PageScreenshots.Settle(window);

                Assert.DoesNotContain(page.GetVisualDescendants().OfType<TextBlock>(),
                    block => block.IsEffectivelyVisible && block.Text == MotifAppStrings.ProposalReading);
                Assert.Contains(page.GetVisualDescendants().OfType<TextBlock>(),
                    block => block.IsEffectivelyVisible && block.Text == MotifAppStrings.ProposalReadStuck);
                var button = Assert.Single(page.GetVisualDescendants().OfType<Button>(),
                    b => AutomationProperties.GetAutomationId(b) == AutomationIds.ProposalReadAgain);
                Assert.True(button.IsEffectivelyEnabled && button.IsEffectivelyVisible);
                Assert.Equal(MotifAppStrings.ProposalReadAgain, button.Content);
            }
            finally { gate.TrySetCanceled(); window.Close(); await Settle(context, documents); }
        }, TimeSpan.FromMinutes(2));
    }
}
