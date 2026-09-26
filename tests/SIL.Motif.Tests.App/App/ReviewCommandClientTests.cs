using SIL.Motif.App.Services;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Store;
using SIL.Motif.Host.Corpus;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Worker;
using SIL.Motif.Tests.TestFixtures;
using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(LcmCacheTestCollection.Name)]
public sealed class ReviewCommandClientTests(PristineProjectFixture pristine)
{
    [Fact]
    public async Task ReviewApplyRunsItsDryRunInProcessWithoutWorkerEnvironmentOverrides()
    {
        using var project = new WalkthroughProject(pristine);
        var productVersion = SIL.Motif.Host.MotifProductVersion.CurrentText;
        const string form = "review-apply-word";
        Guid wordformId = Guid.Empty;
        new FieldWorksSimulator(project.FwDataPath).SaveEdit(cache =>
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
                wordformId = cache.ServiceLocator.GetInstance<IWfiWordformFactory>()
                    .Create(TsStringUtils.MakeString(form, cache.DefaultVernWs)).Guid));
        var baseline = BaselineCaptureCommand.Capture(
            new BaselineCaptureRequest(project.FwDataPath), project.ManagedRoot);
        Assert.True(baseline.Succeeded, baseline.Refusal?.Message);

        Assert.Null(Environment.GetEnvironmentVariable(RunnerOptions.RootVariable));
        var client = new CommandClient(new CommandClientOptions(
            project.ManagedRoot, null, new InProcessRunnerLauncher()));
        var loaded = await client.LoadPendingChangesAsync(
            new PendingChangesRequest(project.FwDataPath, productVersion), CancellationToken.None);
        Assert.True(loaded.Succeeded, loaded.Refusal?.Message);
        var put = await client.PutPendingChangeAsync(new PutPendingChangeRequest(
            project.FwDataPath, productVersion, loaded.Value!.Revision,
            new ChangeIntent(CanonicalId.Mint().Value, "incorrect-spelling",
                CanonicalId.FromGuid(wordformId).Value, form)), CancellationToken.None);
        Assert.True(put.Succeeded, put.Refusal?.Message);
        var finalized = ProposalCommands.Finalize(new FinalizeRequest(
            project.FwDataPath, productVersion, PendingChanges.DraftName, put.Value!.Revision));
        Assert.True(finalized.Succeeded, finalized.Refusal?.Message);
        RecordCandidateAssessment(project.FwDataPath, baseline.Value!.Token,
            finalized.Value!.ProposalId, finalized.Value.IntentDigest, form);
        var reopened = ProposalCommands.Reopen(new ReopenRequest(
            project.FwDataPath, productVersion, PendingChanges.DraftName, finalized.Value.ProposalId));
        Assert.True(reopened.Succeeded, reopened.Refusal?.Message);
        var current = await client.LoadPendingChangesAsync(
            new PendingChangesRequest(project.FwDataPath, productVersion), CancellationToken.None);
        Assert.True(current.Succeeded, current.Refusal?.Message);

        var applied = await client.ApplyPendingAsync(new ApplyPendingRequest(
            project.FwDataPath, current.Value!.DraftId!, current.Value.Revision, "test-user"), CancellationToken.None);

        Assert.True(applied.Succeeded, applied.Refusal?.Message);
        Assert.True(applied.Value!.Applied);
        Assert.Null(Environment.GetEnvironmentVariable(RunnerOptions.RootVariable));
    }

    private static void RecordCandidateAssessment(
        string projectPath, SIL.Motif.Contract.Baselines.BaselineToken baselineToken,
        string proposalId, string intentDigest, string word)
    {
        using var database = ProjectMotifDatabase.Open(projectPath);
        new AssessmentRepository(database).Record(new NewAssessmentRecord(
            AssessmentId: CanonicalId.Mint("assessment/").Value,
            ProposalId: CanonicalId.Parse(proposalId),
            ProposalIntentDigest: intentDigest,
            Assessor: "pangloss",
            Kind: "Correctness",
            ScopeJson: System.Text.Json.JsonSerializer.Serialize(new
            {
                query = "words",
                words = new[] { word },
                collect = new[] { "Correctness" },
                perWordLimitMs = 1000,
                perWordStepLimit = new { steps = 200000, isUnbounded = false },
            }, SIL.Motif.Contract.MotifJson.CreateOptions()),
            ScopeDigest: "sha256:" + new string('a', 64),
            TokeniserName: "none",
            TokeniserVersion: "1",
            BaselineToken: System.Text.Json.JsonSerializer.Serialize(
                baselineToken, SIL.Motif.Contract.MotifJson.CreateOptions()),
            Selection: Selection.Create("test", [word]),
            OutcomeDigest: "sha256:" + new string('b', 64),
            SemanticDigest: "sha256:" + new string('c', 64),
            GrammarSourceSha256: "sha256:" + new string('d', 64),
            ModelFingerprint: "test-model",
            Pipeline: "test-pipeline",
            DiagnosticCount: 0,
            Words: [CorrectnessFixture.Word(word, matched: true)]));
    }

    [Fact]
    public async Task ApplyWaitsForProjectWorkAndCanBeCancelledWhileQueued()
    {
        using var project = new WalkthroughProject(pristine);
        var heartbeat = Path.Combine(project.ManagedRoot, "grammar-heartbeat");
        var captured = BaselineCaptureCommand.Capture(
            new BaselineCaptureRequest(project.FwDataPath), project.ManagedRoot);
        Assert.True(captured.Succeeded, captured.Refusal?.Message);
        var locator = new ProjectLocator(Path.GetFullPath(project.FwDataPath),
            Path.GetFileNameWithoutExtension(project.FwDataPath));
        using var database = ProjectMotifDatabase.Open(project.FwDataPath);
        var baseline = new BaselineRepository(database).GetCurrent(ProjectWorkspaceKey.Compute(locator))!;
        FakeParser.Behave(Path.GetDirectoryName(baseline.FwDataPath)!, new { heartbeatPath = heartbeat });
        Assert.Null(Environment.GetEnvironmentVariable(RunnerOptions.RootVariable));
        try
        {
            var client = new CommandClient(new CommandClientOptions(
                project.ManagedRoot, FakeParser.ExecutablePath, new InProcessRunnerLauncher()));
            using var grammarCancellation = new CancellationTokenSource();
            var checking = client.CheckGrammarAsync(
                new GrammarCheckRequest(project.FwDataPath), grammarCancellation.Token);
            try
            {
                await WaitForHeartbeatAsync(heartbeat);
                using var applyCancellation = new CancellationTokenSource();
                var applying = client.ApplyPendingAsync(new ApplyPendingRequest(
                    project.FwDataPath, "draft/absent", "revision/absent", "test-user"), applyCancellation.Token);
                await Task.Delay(100);
                Assert.False(applying.IsCompleted);
                applyCancellation.Cancel();
                var refused = await applying.WaitAsync(TimeSpan.FromSeconds(5));

                Assert.False(refused.Succeeded);
                Assert.Equal(FailureReason.Cancelled, refused.Refusal!.Reason);
                Assert.Equal("project.wait-cancelled", refused.Refusal.Code);
            }
            finally
            {
                grammarCancellation.Cancel();
            }

            var grammarCheck = await checking.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal("grammarcheck.cancelled", grammarCheck.Refusal?.Code);
        }
        finally
        {
            Assert.Null(Environment.GetEnvironmentVariable(RunnerOptions.RootVariable));
        }
    }

    private static async Task WaitForHeartbeatAsync(string heartbeat)
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(20);
        while (!File.Exists(heartbeat) && DateTimeOffset.UtcNow < deadline)
            await Task.Delay(20);
        Assert.True(File.Exists(heartbeat), "The grammar check did not reach the fake parser.");
    }
}
