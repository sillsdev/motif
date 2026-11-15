using Avalonia.Controls;
using Avalonia.Automation;
using Avalonia.VisualTree;
using SIL.Motif.App;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.App.Walkthrough;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class ProposalDiagnosticsRowTests
{
    private const string Digest = "sha256:intent";
    private static readonly BaselineToken Baseline = new("project", "sha256:" + new string('a', 64), "1",
        "2026-11-01T00:00:00Z", "sha256:" + new string('b', 64));

    private static TrialRecord Trial(string id, string job = "job/trial") => new(job, id, Digest, Baseline, null,
        new Dictionary<string, string>(), [], true, null, JobStatus.Completed, DateTimeOffset.UtcNow, TrialFreshness.Current)
        { Work = new TrialWork(2, 20, 12, -40, [], 0, 0) };

    private static TrialSummary Summary(string job = "job/trial") => new(job, DateTimeOffset.UtcNow, TrialFreshness.Current,
        TrialWordScope.AllWords, default, default, default, default,
        new Paired<int?>(5, 3, true, true, true, null), 0, 0);

    private static ProposalReviewResponse Review(string id, TrialRecord? trial) => new(id, Digest, Digest,
        [new GrammarChangeEntry("operation", 0, "Exact operation", ["op"], [], "content", false, "compose",
            "The actual grammar change", [], [], [])], trial, null, [], 0);

    [Fact]
    public async Task MissingOrMismatchedTrialsNeverBecomeZeroChange()
    {
        var fake = new FakeCommandClient();
        var context = WorkspaceContextTests.NewContext(fake);
        context.ProjectPath = "project.fwdata";
        context.Diagnostics = true;
        using var model = new ProposalDiagnosticsViewModel(context);
        fake.Review = Review("p/one", Trial("p/one"));
        await model.SetProposalAsync("p/one", Digest, Summary("another-trial"));
        Assert.Equal(MotifAppStrings.DiagnosticsTrialUnavailable, model.AvailabilityText);
        Assert.Empty(model.WarningsText);
        Assert.Empty(model.StepsText);
        Assert.Empty(fake.ProposalParsimonyStatusRequests);
        await model.SetProposalAsync("p/one", Digest, null);
        Assert.Empty(model.WarningsText);
        fake.Review = Review("p/other", Trial("p/other"));
        await model.SetProposalAsync("p/one", Digest, Summary());
        Assert.Empty(model.Operations);
        await context.StopProjectWorkAsync();
    }

    [Fact]
    public async Task IndependentCandidatesKeepTheirOwnWarningWorkAndParsimonyFacts()
    {
        var fake = new FakeCommandClient();
        fake.ProposalReviewHandler = (request, _) => Task.FromResult(CommandOutcome<ProposalReviewResponse>.Success(
            Review(request.ProposalId!, Trial(request.ProposalId!))));
        fake.OnProposalParsimonyStatus((request, _) => Task.FromResult(CommandOutcome<ProposalParsimonyStatus>.Success(
            ProposalParsimonyStatusFixtures.NotYetRun(request.ProposalId))));
        var context = WorkspaceContextTests.NewContext(fake);
        context.ProjectPath = "project.fwdata";
        context.Diagnostics = true;
        using var first = new ProposalDiagnosticsViewModel(context);
        using var second = new ProposalDiagnosticsViewModel(context);
        await first.SetProposalAsync("p/one", Digest, Summary());
        await second.SetProposalAsync("p/two", Digest, Summary() with { WarningCount = new(1, 4, true, true, true, null) });
        Assert.Equal("Warnings: 5 → 3 (-2)", first.WarningsText);
        Assert.Equal("Warnings: 1 → 4 (+3)", second.WarningsText);
        Assert.Contains("10.00 → 6.00", first.StepsText);
        Assert.Equal("p/one", first.Parsimony.ProposalId);
        Assert.Equal("p/two", second.Parsimony.ProposalId);
        Assert.Equal(["The actual grammar change"], first.Operations);
        Assert.Empty(fake.EnqueueParsimonyRequests);
        Assert.Empty(fake.AssessRequests);
        await context.StopProjectWorkAsync();
    }

    [Fact]
    public void TurningDiagnosticsOffRemovesDetailsFromTheTreeAndCancelsPolling()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var fake = new FakeCommandClient();
            fake.Review = Review("p/one", Trial("p/one"));
            fake.ProposalParsimonyStatusIs(ProposalParsimonyStatusFixtures.Running("p/one"));
            var context = WorkspaceContextTests.NewContext(fake);
            context.ProjectPath = "project.fwdata";
            context.Diagnostics = true;
            using var model = new ProposalDiagnosticsViewModel(context);
            var watching = model.SetProposalAsync("p/one", Digest, Summary());
            await Wait.UntilAsync(() => model.Parsimony.Status is not null, "The stored status was not read.");
            var row = new ProposalDiagnosticsRow { DataContext = model };
            var window = new MotifTestWindow { Width = 500, Height = 700, Content = row };
            try
            {
                window.Show();
                PageScreenshots.Settle(window);
                Assert.NotEmpty(row.GetVisualDescendants().OfType<TextBlock>());
                context.Diagnostics = false;
                await watching;
                PageScreenshots.Settle(window);
                Assert.False(model.IsVisible);
                Assert.DoesNotContain(row.GetVisualDescendants(), child => child is TextBlock);
                Assert.Null(row.FindControl<ContentControl>("DiagnosticSurface")!.Content);
                Assert.Null(model.Parsimony.Status);
                Assert.Single(fake.ProposalParsimonyStatusRequests);
                Assert.Empty(fake.EnqueueParsimonyRequests);
            }
            finally { window.Close(); await context.StopProjectWorkAsync(); }
        }, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void CompareRowsHaveIndependentAutomationIdentitiesAndDisappearTogether()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var fake = new FakeCommandClient();
            fake.ProposalReviewHandler = (request, _) => Task.FromResult(CommandOutcome<ProposalReviewResponse>.Success(
                Review(request.ProposalId!, Trial(request.ProposalId!))));
            fake.OnProposalParsimonyStatus((request, _) => Task.FromResult(CommandOutcome<ProposalParsimonyStatus>.Success(
                ProposalParsimonyStatusFixtures.NotYetRun(request.ProposalId))));
            var context = WorkspaceContextTests.NewContext(fake);
            context.ProjectPath = "project.fwdata";
            context.Diagnostics = true;
            using var first = new ProposalDiagnosticsViewModel(context);
            using var second = new ProposalDiagnosticsViewModel(context);
            var firstRow = new ProposalDiagnosticsRow { DataContext = first };
            var secondRow = new ProposalDiagnosticsRow { DataContext = second };
            var window = new MotifTestWindow
            {
                Width = 640, Height = 800,
                Content = new StackPanel { Children = { firstRow, secondRow } },
            };
            try
            {
                window.Show();
                await first.SetProposalAsync("p/one", Digest, Summary());
                await second.SetProposalAsync("p/two", Digest, Summary());
                PageScreenshots.Settle(window);
                var ids = window.GetVisualDescendants().OfType<Control>().Select(AutomationProperties.GetAutomationId)
                    .Where(id => !string.IsNullOrEmpty(id)).ToArray();
                Assert.Contains(first.AutomationId, ids);
                Assert.Contains(second.AutomationId, ids);
                Assert.Contains(first.ParsimonyFoldAutomationId, ids);
                Assert.Contains(second.ParsimonyFoldAutomationId, ids);
                Assert.Equal(ids.Length, ids.Distinct().Count());
                Assert.NotEqual(first.Parsimony.PanelAutomationId, second.Parsimony.PanelAutomationId);
                context.Diagnostics = false;
                PageScreenshots.Settle(window);
                Assert.Empty(firstRow.GetVisualDescendants().OfType<TextBlock>());
                Assert.Empty(secondRow.GetVisualDescendants().OfType<TextBlock>());
            }
            finally { window.Close(); await context.StopProjectWorkAsync(); }
        }, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task SwitchingAProposalCancelsTheReadAndRejectsItsLateResult()
    {
        var fake = new FakeCommandClient();
        var oldRead = new TaskCompletionSource<CommandOutcome<ProposalReviewResponse>>();
        CancellationToken oldToken = default;
        fake.ProposalReviewHandler = (request, token) =>
        {
            if (request.ProposalId == "p/old")
            {
                oldToken = token;
                return oldRead.Task;
            }
            return Task.FromResult(CommandOutcome<ProposalReviewResponse>.Success(Review("p/new", null)));
        };
        var context = WorkspaceContextTests.NewContext(fake);
        context.ProjectPath = "project.fwdata";
        context.Diagnostics = true;
        using var model = new ProposalDiagnosticsViewModel(context);
        var pending = model.SetProposalAsync("p/old", Digest, Summary());
        await model.SetProposalAsync("p/new", Digest, null);
        Assert.True(oldToken.IsCancellationRequested);
        oldRead.SetResult(CommandOutcome<ProposalReviewResponse>.Success(Review("p/old", Trial("p/old"))));
        await pending;
        Assert.Empty(model.WarningsText);
        Assert.Empty(fake.ProposalParsimonyStatusRequests);
        context.Diagnostics = false;
        Assert.False(model.IsVisible);
        Assert.Empty(model.Operations);
        await context.StopProjectWorkAsync();
    }

    [Fact]
    public async Task ProjectShutdownCancelsAndJoinsTheDocumentRead()
    {
        var fake = new FakeCommandClient();
        var pending = new TaskCompletionSource<CommandOutcome<ProposalReviewResponse>>();
        CancellationToken readToken = default;
        fake.ProposalReviewHandler = (_, token) =>
        {
            readToken = token;
            return pending.Task;
        };
        var context = WorkspaceContextTests.NewContext(fake);
        context.ProjectPath = "project.fwdata";
        context.Diagnostics = true;
        using var model = new ProposalDiagnosticsViewModel(context);
        var read = model.SetProposalAsync("p/one", Digest, Summary());
        var stopped = context.StopProjectWorkAsync();
        Assert.True(readToken.IsCancellationRequested);
        Assert.False(stopped.IsCompleted);
        pending.SetResult(CommandOutcome<ProposalReviewResponse>.Success(Review("p/one", Trial("p/one"))));
        await Task.WhenAll(read, stopped);
        Assert.False(model.IsVisible);
        Assert.Empty(model.Operations);
        Assert.Empty(fake.ProposalParsimonyStatusRequests);
    }

    [Fact]
    public async Task AStatusForTheWrongTrialCannotPublishFindings()
    {
        var fake = new FakeCommandClient();
        fake.ProposalParsimonyStatusIs(ProposalParsimonyStatusFixtures.Ready("p/one") with { TrialJobId = "old-trial" });
        var model = new ProposalParsimonyViewModel(fake.ReadProposalParsimonyStatusAsync,
            readReport: fake.ReadParsimonyReportAsync, readView: fake.ReadParsimonyViewAsync);
        await model.WatchPinned("project.fwdata", "p/one", Digest, "job/trial");
        Assert.Null(model.Status);
        Assert.Empty(model.Groups);
        Assert.Equal(MotifAppStrings.DiagnosticsTrialUnavailable, model.Sentence);
        model.Stop();
    }
}
