using SIL.Motif.App.Services;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Handoff;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;
using SIL.Motif.Host.Store;
using SIL.Motif.Tests.App.Walkthrough;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Pins how the production <see cref="CommandClient"/> answers a cancelled call. A call that has to wait for
/// the project stops waiting as soon as it is cancelled and never starts its work; a call handed a token that
/// is already cancelled comes back as an outcome, never as a <see cref="TaskCanceledException"/>; and a
/// store-only read does not wait behind project work at all.
/// </summary>
[Collection(LcmCacheTestCollection.Name)]
public sealed class AdapterCancellationTests(PristineProjectFixture pristine)
{
    private static readonly string ProductVersion = MotifProductVersion.CurrentText;

    [Fact]
    public async Task EveryMethodCalledWithACancelledTokenReturnsAnOutcomeInsteadOfThrowing()
    {
        using var project = new WalkthroughProject(pristine);
        var outputDirectory = Path.Combine(project.ManagedRoot, "handoff");
        var client = RealCommandClient.Create(project.ManagedRoot, FakeParser.ExecutablePath);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var token = cancelled.Token;

        await client.ListKnownProjectsAsync(token);
        foreach (var call in EveryOutcomeCall(client, project.FwDataPath, outputDirectory, "revision/absent"))
        {
            var outcome = await call.Run(token);
            Assert.True(outcome.Succeeded || outcome.Refusal is not null, $"{call.Name} returned no outcome.");
        }
    }

    [Fact]
    public async Task ACallWaitingForTheProjectIsCancelledPromptlyAndNeverStarts()
    {
        using var project = new WalkthroughProject(pristine);
        var outputDirectory = Path.Combine(project.ManagedRoot, "handoff");
        var client = RealCommandClient.Create(project.ManagedRoot, FakeParser.ExecutablePath);
        CaptureBaselineWithAWaitingParser(project, out var heartbeat);
        var loaded = await client.LoadPendingChangesAsync(
            new PendingChangesRequest(project.FwDataPath, ProductVersion), CancellationToken.None);
        Assert.True(loaded.Succeeded, loaded.Refusal?.Message);

        await using (var hold = await HoldTheProjectAsync(client, project, heartbeat))
        {
            using var queuedCancellation = new CancellationTokenSource();
            var queued = GatedCalls(client, project.FwDataPath, outputDirectory, loaded.Value!.Revision)
                .Select(call => (call.Name, Outcome: call.Run(queuedCancellation.Token)))
                .ToArray();
            await Task.Delay(100);
            Assert.All(queued, call => Assert.False(call.Outcome.IsCompleted, $"{call.Name} did not wait."));

            queuedCancellation.Cancel();

            foreach (var (name, outcome) in queued)
            {
                var refused = await outcome.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.False(refused.Succeeded, $"{name} ran after it was cancelled.");
                Assert.Equal(FailureReason.Cancelled, refused.Refusal!.Reason);
                Assert.Equal("project.wait-cancelled", refused.Refusal.Code);
            }
        }

        Assert.Empty(WalkthroughStoreAssertions.ListInvocations(project.FwDataPath));
        Assert.False(Directory.Exists(outputDirectory));
        var pending = await client.LoadPendingChangesAsync(
            new PendingChangesRequest(project.FwDataPath, ProductVersion), CancellationToken.None);
        Assert.Empty(pending.Value!.Changes);
        var selection = await client.ReadDefaultSelectionAsync(
            new ReadDefaultSelectionRequest(project.FwDataPath), CancellationToken.None);
        Assert.Null(selection.Value!.Selection);
        Assert.False(selection.Value.SetupSkipped);
    }

    [Fact]
    public async Task AStoreOnlyReadDoesNotWaitForTheProject()
    {
        using var project = new WalkthroughProject(pristine);
        var client = RealCommandClient.Create(project.ManagedRoot, FakeParser.ExecutablePath);
        CaptureBaselineWithAWaitingParser(project, out var heartbeat);

        await using var hold = await HoldTheProjectAsync(client, project, heartbeat);
        var path = project.FwDataPath;
        var reads = new (string Name, Task Read)[]
        {
            ("ListTextWords", client.ListTextWordsAsync(new TextWordsRequest(path, []), CancellationToken.None)),
            ("ReadCurrentEvidence", client.ReadCurrentEvidenceAsync(path, CancellationToken.None)),
            ("ReadStoredGrammarCheck", client.ReadStoredGrammarCheckAsync(
                new GrammarCheckRequest(path), CancellationToken.None)),
            ("ReadWordState", client.ReadWordStateAsync(
                new WordReadStateRequest(path, Guid.NewGuid()), CancellationToken.None)),
            ("GetCurrentBaseline", client.GetCurrentBaselineAsync(
                new CurrentBaselineRequest(path), CancellationToken.None)),
            ("Overview", client.OverviewAsync(new OverviewRequest(path), CancellationToken.None)),
            ("Timing", client.TimingAsync(new TimingRequest(path), CancellationToken.None)),
            ("GetProjectHistory", client.GetProjectHistoryAsync(
                new ProjectHistoryRequest(path), CancellationToken.None)),
            ("ReadDefaultSelection", client.ReadDefaultSelectionAsync(
                new ReadDefaultSelectionRequest(path), CancellationToken.None)),
            ("ShowConfig", client.ShowConfigAsync(new ShowConfigRequest(path, ProductVersion), CancellationToken.None)),
            ("ListKnownProjects", client.ListKnownProjectsAsync(CancellationToken.None)),
        };

        foreach (var (name, read) in reads)
        {
            var finished = await Task.WhenAny(read, Task.Delay(TimeSpan.FromSeconds(10)));
            Assert.True(ReferenceEquals(finished, read), $"{name} waited for the project.");
        }
    }

    [Fact]
    public async Task ACallCancelledAsTheProjectIsHandedToItDoesNotStartAndGivesTheProjectBack()
    {
        var root = Path.Combine(Path.GetTempPath(), "SIL.Motif.AdapterCancellationTests", Guid.NewGuid().ToString("N"));
        using var cancellation = new CancellationTokenSource();
        var gate = new CancelledOnEntryGate(cancellation);
        var client = new CommandClient(new CommandClientOptions(root, FakeParser.ExecutablePath,
            new NoRunnerLauncher(new JobRunnerLaunchOptions(root, FakeParser.ExecutablePath))), gate);

        var outcome = await client.SkipSetupAsync(
            new SkipSetupRequest(Path.Combine(root, "absent.fwdata")), cancellation.Token);

        Assert.Equal("project.wait-cancelled", outcome.Refusal?.Code);
        Assert.Equal(1, gate.Exits);
    }

    // The project is busy when asked, then handed over just as the caller cancels.
    private sealed class CancelledOnEntryGate(CancellationTokenSource cancellation) : IProjectGate
    {
        public int Exits { get; private set; }

        public bool TryEnter() => false;

        public Task EnterAsync(CancellationToken cancellationToken)
        {
            cancellation.Cancel();
            return Task.CompletedTask;
        }

        public void Exit() => Exits++;
    }

    private sealed record OutcomeCall(string Name, Func<CancellationToken, Task<Observed>> Run);

    private sealed record Observed(bool Succeeded, Refusal? Refusal);

    private static Func<CancellationToken, Task<Observed>> Observe<T>(
        Func<CancellationToken, Task<CommandOutcome<T>>> call) where T : class =>
        async token =>
        {
            var outcome = await call(token);
            return new Observed(outcome.Succeeded, outcome.Refusal);
        };

    private static IEnumerable<OutcomeCall> GatedCalls(
        CommandClient client, string path, string outputDirectory, string revision)
    {
        var words = new SelectionRequest(false, [], ["motifa"], false, null);
        yield return new("CaptureBaseline", Observe(token =>
            client.CaptureBaselineAsync(new BaselineCaptureRequest(path), token)));
        yield return new("ListTexts", Observe(token =>
            client.ListTextsAsync(new TextInventoryRequest(path), token)));
        yield return new("Assess", Observe(token =>
            client.AssessAsync(new AssessRequest(path, words), new Progress<AssessmentProgress>(), token)));
        yield return new("Handoff", Observe(token => client.HandoffAsync(
            new HandoffRequest(path, outputDirectory, words, Assess: true),
            new Progress<AssessmentProgress>(), token)));
        yield return new("ApplyPending", Observe(token => client.ApplyPendingAsync(
            new ApplyPendingRequest(path, "draft/absent", revision, "test-user"), token)));
        yield return new("LoadPendingChanges", Observe(token => client.LoadPendingChangesAsync(
            new PendingChangesRequest(path, ProductVersion), token)));
        yield return new("PutPendingChange", Observe(token => client.PutPendingChangeAsync(
            new PutPendingChangeRequest(path, ProductVersion, revision, new ChangeIntent(
                CanonicalId.Mint().Value, "incorrect-spelling", CanonicalId.Mint().Value, "motifa")), token)));
        yield return new("RemoveAnalysis", Observe(token => client.RemoveAnalysisAsync(
            new RemoveAnalysisRequest(path, ProductVersion, revision, ChangeId: CanonicalId.Mint().Value,
                WordformId: CanonicalId.Mint().Value, Word: "motifa", AnalysisId: CanonicalId.Mint().Value), token)));
        yield return new("AcceptNewSet", Observe(token => client.AcceptNewSetAsync(
            new AcceptNewSetRequest(path, ProductVersion, revision, "assessment/test",
                WordformId: CanonicalId.Mint().Value), token)));
        yield return new("RemovePendingChange", Observe(token => client.RemovePendingChangeAsync(
            new RemovePendingChangeRequest(path, ProductVersion, revision, CanonicalId.Mint().Value), token)));
        yield return new("RecheckPendingChanges", Observe(token => client.RecheckPendingChangesAsync(
            new RecheckPendingChangesRequest(path, ProductVersion, revision), token)));
        yield return new("SetDefaultSelection", Observe(token => client.SetDefaultSelectionAsync(
            new SetDefaultSelectionRequest(path, "cancelled", [], ["motifa"]), token)));
        yield return new("SkipSetup", Observe(token =>
            client.SkipSetupAsync(new SkipSetupRequest(path), token)));
        yield return new("CheckGrammar", Observe(token =>
            client.CheckGrammarAsync(new GrammarCheckRequest(path), token)));
        yield return new("TraceWord", Observe(token =>
            client.TraceWordAsync(new WordTraceRequest(path, "motifa"), token)));
    }

    private static IEnumerable<OutcomeCall> EveryOutcomeCall(
        CommandClient client, string path, string outputDirectory, string revision)
    {
        foreach (var call in GatedCalls(client, path, outputDirectory, revision)) yield return call;
        yield return new("MeasurePending", Observe(token => client.MeasurePendingAsync(
            new MeasurePendingRequest(path, "draft/absent", revision, ["motifa"]),
            new Progress<MeasureProgress>(), token)));
        yield return new("GetCurrentBaseline", Observe(token =>
            client.GetCurrentBaselineAsync(new CurrentBaselineRequest(path), token)));
        yield return new("Stats", Observe(token => client.StatsAsync(
            new StatsRequest(path, null, StatsOutputKind.Text, []), token)));
        yield return new("ReadDefaultSelection", Observe(token =>
            client.ReadDefaultSelectionAsync(new ReadDefaultSelectionRequest(path), token)));
        yield return new("ShowConfig", Observe(token =>
            client.ShowConfigAsync(new ShowConfigRequest(path, ProductVersion), token)));
        yield return new("Overview", Observe(token => client.OverviewAsync(new OverviewRequest(path), token)));
        yield return new("ReadCurrentEvidence", Observe(token => client.ReadCurrentEvidenceAsync(path, token)));
        yield return new("Timing", Observe(token => client.TimingAsync(new TimingRequest(path), token)));
        yield return new("GetProjectHistory", Observe(token =>
            client.GetProjectHistoryAsync(new ProjectHistoryRequest(path), token)));
        yield return new("ReadStoredGrammarCheck", Observe(token =>
            client.ReadStoredGrammarCheckAsync(new GrammarCheckRequest(path), token)));
        yield return new("ListTextWords", Observe(token =>
            client.ListTextWordsAsync(new TextWordsRequest(path, []), token)));
        yield return new("ReadWordState", Observe(token =>
            client.ReadWordStateAsync(new WordReadStateRequest(path, Guid.NewGuid()), token)));
    }

    private static void CaptureBaselineWithAWaitingParser(WalkthroughProject project, out string heartbeat)
    {
        heartbeat = Path.Combine(project.ManagedRoot, "grammar-heartbeat");
        var captured = BaselineCaptureCommand.Capture(
            new BaselineCaptureRequest(project.FwDataPath), project.ManagedRoot);
        Assert.True(captured.Succeeded, captured.Refusal?.Message);
        var locator = new ProjectLocator(Path.GetFullPath(project.FwDataPath),
            Path.GetFileNameWithoutExtension(project.FwDataPath));
        using var database = ProjectMotifDatabase.Open(project.FwDataPath);
        var baseline = new BaselineRepository(database).GetCurrent(ProjectWorkspaceKey.Compute(locator))!;
        FakeParser.Behave(Path.GetDirectoryName(baseline.FwDataPath)!, new { heartbeatPath = heartbeat });
    }

    // A grammar check against a parser that never finishes holds the project until it is cancelled.
    private static async Task<ProjectHold> HoldTheProjectAsync(
        CommandClient client, WalkthroughProject project, string heartbeat)
    {
        var cancellation = new CancellationTokenSource();
        var checking = client.CheckGrammarAsync(new GrammarCheckRequest(project.FwDataPath), cancellation.Token);
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(20);
        while (!File.Exists(heartbeat) && DateTimeOffset.UtcNow < deadline)
            await Task.Delay(20);
        Assert.True(File.Exists(heartbeat), "The grammar check did not reach the fake parser.");
        return new ProjectHold(cancellation, checking);
    }

    private sealed class ProjectHold(
        CancellationTokenSource cancellation, Task<CommandOutcome<GrammarCheckResponse>> checking) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            cancellation.Cancel();
            var released = await checking.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal("grammarcheck.cancelled", released.Refusal?.Code);
            cancellation.Dispose();
        }
    }
}
