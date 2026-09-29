using System.Diagnostics;
using System.Text.Json;
using Avalonia.Threading;
using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using SIL.Motif.App;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Tests.App.Walkthrough;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.Parser;
using SIL.Motif.Host.Store;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.App.RealClient;

[Collection(LcmCacheTestCollection.Name)]
public sealed class ExternalApplyActivationRealClientTests(PristineProjectFixture pristine)
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task ReturningFromFieldWorksReloadsTheApplyOutcome(int expectedExitCode)
    {
        using var project = new WalkthroughProject(pristine);
        var runner = IsolatedRunner.Process(project.ManagedRoot);
        var parser = FakeParser.Copy(project.ManagedRoot);
        Guid analysisId = Guid.Empty;

        if (expectedExitCode == 2)
            AddApprovalChange(project, pristine, out analysisId);
        else
            PendingChangeFixture.AddIncorrectSpelling(
                project.FwDataPath, project.ManagedRoot, "external-apply-word");

        var pending = PendingChanges.Load(new PendingChangesRequest(project.FwDataPath, "1.0"));
        Assert.True(pending.Succeeded, pending.Refusal?.Message);
        if (expectedExitCode is 0 or 3 or 4)
        {
            var change = Assert.Single(pending.Value!.Changes);
            var measured = await PendingChangesWorkflow.Measure(new MeasurePendingRequest(
                    project.FwDataPath, pending.Value.DraftId!, pending.Value.Revision, [change.Word]),
                new Progress<MeasureProgress>(), CancellationToken.None, runnerLauncher: runner);
            Assert.True(measured.Succeeded, measured.Refusal?.Message);
            Assert.True(measured.Value!.EvidenceComplete);
        }

        using var held = expectedExitCode == 3 ? new FieldWorksSimulator(project.FwDataPath).Hold() : null;
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var noRunner = new NoRunnerLauncher(new JobRunnerLaunchOptions(
                project.ManagedRoot, parser));
            using var walkthrough = new WalkthroughWindow(project.ManagedRoot, project.FwDataPath,
                parserPath: parser, runnerLauncher: noRunner);
            walkthrough.Show();
            walkthrough.ChooseNewProject();
            walkthrough.WaitUntil(
                () => walkthrough.Workspace.Baseline.HasBaseline &&
                    walkthrough.Workspace.Context.Changes.Items.Count == 1 &&
                    walkthrough.Workspace.Context.Setup?.IsOpen == true &&
                    walkthrough.Workspace.Baseline.FieldWorksHeldProject == (expectedExitCode == 3),
                TimeSpan.FromSeconds(45), "the window did not load the pending change and project state");
            walkthrough.SkipSetup();
            await walkthrough.Workspace.CheckFreshnessAsync();
            var originalBaselineToken = walkthrough.Workspace.Baseline.Token;
            if (expectedExitCode == 0)
            {
                var deadline = Stopwatch.GetTimestamp() + 90 * Stopwatch.Frequency;
                var heartbeat = Path.Combine(project.ManagedRoot, "external-apply-assessment-heartbeat");
                walkthrough.SetFakeParserBehavior(new
                {
                    subcommands = new Dictionary<string, object>
                    {
                        ["batch"] = new { heartbeatPath = heartbeat },
                    },
                });
                WalkthroughSteps.StartAssessmentOverPastedWords(walkthrough, deadline);
                walkthrough.WaitUntil(
                    () => File.Exists(heartbeat) && walkthrough.Workspace.Assess.State == RunState.Running,
                    WalkthroughSteps.Remaining(deadline), "the Assessment did not reach the held fake parser");
                walkthrough.Click("Cancel the running Assessment");
                walkthrough.WaitUntil(
                    () => walkthrough.Workspace.Assess.State == RunState.Cancelled,
                    WalkthroughSteps.Remaining(deadline), "the held Assessment did not cancel");
                walkthrough.SetFakeParserBehavior(new
                {
                    subcommands = new Dictionary<string, object>
                    {
                        ["batch"] = new { words = new[] { new { word = "motifa", outcome = "complete" } } },
                    },
                });
                WalkthroughSteps.StartAssessmentOverPastedWords(walkthrough, deadline);
                walkthrough.WaitUntil(
                    () => walkthrough.Workspace.Assess.State == RunState.Completed &&
                        walkthrough.Workspace.Context.EvidencePublication.IsCompleted,
                    WalkthroughSteps.Remaining(deadline), "the Assessment did not complete");
                Assert.True(walkthrough.Workspace.Context.Evidence.HasAssessment);
            }

            if (expectedExitCode == 2)
            {
                new FieldWorksSimulator(project.FwDataPath).SaveEdit(cache =>
                    NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
                    {
                        var analysis = cache.ServiceLocator.GetInstance<IWfiAnalysisRepository>()
                            .GetObject(analysisId);
                        cache.LangProject.DefaultUserAgent.SetEvaluation(analysis, Opinions.disapproves);
                    }));
                var captured = BaselineCaptureCommand.Capture(
                    new BaselineCaptureRequest(project.FwDataPath), project.ManagedRoot);
                Assert.True(captured.Succeeded, captured.Refusal?.Message);
            }

            var outcome = expectedExitCode == 4
                ? await RunReconciliationFailureAsync(project.FwDataPath, runner.Options)
                : expectedExitCode == 0
                    ? await RunApplyWithWorkerAsync(project.FwDataPath, runner.Options)
                : await RunCliAsync(CliStart(runner.Options, project.FwDataPath));
            Assert.True(outcome.ExitCode == expectedExitCode,
                $"Expected CLI exit {expectedExitCode}, got {outcome.ExitCode}. stderr: {outcome.Error} stdout: {outcome.Output}");

            if (expectedExitCode == 0)
            {
                Assert.Empty(outcome.Error);
                using var response = JsonDocument.Parse(outcome.Output);
                Assert.True(response.RootElement.GetProperty("ok").GetBoolean());
                Assert.True(response.RootElement.GetProperty("applied").GetBoolean());
            }
            else
            {
                Assert.Empty(outcome.Output);
                using var failure = JsonDocument.Parse(outcome.Error);
                var expectedCode = expectedExitCode switch
                {
                    2 => "apply.change-no-longer-fits",
                    3 => "project.in-use",
                    4 => "apply.reconciliation-needed",
                    _ => throw new ArgumentOutOfRangeException(nameof(expectedExitCode)),
                };
                Assert.Equal(expectedCode, failure.RootElement.GetProperty("code").GetString());
            }

            held?.Dispose();
            walkthrough.Window.Hide();
            walkthrough.Window.Show();
            walkthrough.Window.Activate();
            Dispatcher.UIThread.RunJobs();

            switch (expectedExitCode)
            {
                case 0:
                    walkthrough.WaitUntil(
                        () => walkthrough.Workspace.Context.Changes.Items.Count == 0,
                        TimeSpan.FromSeconds(45),
                        $"activation did not clear the applied change; freshness='{walkthrough.Workspace.Freshness}', " +
                        $"applied='{walkthrough.Workspace.Context.Evidence.AppliedSinceRefresh}', " +
                        $"changes='{walkthrough.Workspace.Context.Changes.Items.Count}'");
                    Assert.Equal(ProjectFreshness.SavedSince, walkthrough.Workspace.Freshness);
                    break;
                case 2:
                    walkthrough.WaitUntil(
                        () => walkthrough.Workspace.Baseline.Token != originalBaselineToken,
                        TimeSpan.FromSeconds(45), "activation did not reload the changed FieldWorks Baseline");
                    Assert.Single(walkthrough.Workspace.Context.Changes.Items);
                    Assert.False(Assert.Single(walkthrough.Workspace.Context.Changes.Items).Fit?.StillFits);
                    break;
                case 3:
                    walkthrough.WaitUntil(
                        () => !walkthrough.Workspace.Baseline.FieldWorksHeldProject,
                        TimeSpan.FromSeconds(45), "activation did not clear the released project's held status");
                    Assert.Single(walkthrough.Workspace.Context.Changes.Items);
                    break;
                case 4:
                    walkthrough.WaitUntil(
                        () => walkthrough.Workspace.Context.Changes.Items.Count == 1,
                        TimeSpan.FromSeconds(45),
                        $"activation did not reload the unreconciled pending change; " +
                        $"freshness='{walkthrough.Workspace.Freshness}', " +
                        $"applied='{walkthrough.Workspace.Context.Evidence.AppliedSinceRefresh}', " +
                        $"changes='{walkthrough.Workspace.Context.Changes.Items.Count}'");
                    Assert.False(walkthrough.Workspace.Context.Evidence.AppliedSinceRefresh);
                    break;
            }

            return;
        }, TimeSpan.FromMinutes(3));
    }

    private static void AddApprovalChange(
        WalkthroughProject project, PristineProjectFixture pristine, out Guid analysisId)
    {
        var word = "external-analysis-drift";
        Guid wordformId = Guid.Empty;
        var storedAnalysisId = Guid.Empty;
        analysisId = Guid.Empty;
        new FieldWorksSimulator(project.FwDataPath).SaveEdit(cache =>
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            {
                var wordform = cache.ServiceLocator.GetInstance<IWfiWordformFactory>()
                    .Create(TsStringUtils.MakeString(word, cache.DefaultVernWs));
                wordformId = wordform.Guid;
                var analysis = cache.ServiceLocator.GetInstance<IWfiAnalysisFactory>().Create();
                wordform.AnalysesOC.Add(analysis);
                storedAnalysisId = analysis.Guid;
                var entry = cache.ServiceLocator.GetInstance<ILexEntryRepository>()
                    .GetObject(pristine.Seed.FirstEntryId);
                var bundle = cache.ServiceLocator.GetInstance<IWfiMorphBundleFactory>().Create();
                analysis.MorphBundlesOS.Add(bundle);
                bundle.MorphRA = entry.LexemeFormOA;
                bundle.MsaRA = entry.MorphoSyntaxAnalysesOC.First();
            }));
        analysisId = storedAnalysisId;
        var captured = BaselineCaptureCommand.Capture(
            new BaselineCaptureRequest(project.FwDataPath), project.ManagedRoot);
        Assert.True(captured.Succeeded, captured.Refusal?.Message);
        var initial = PendingChanges.Load(new PendingChangesRequest(project.FwDataPath, "1.0"));
        Assert.True(initial.Succeeded, initial.Refusal?.Message);
        var added = PendingChanges.Put(new PutPendingChangeRequest(
            project.FwDataPath, "1.0", initial.Value!.Revision,
            new ChangeIntent(CanonicalId.Mint().Value, "approve",
                CanonicalId.FromGuid(wordformId).Value, word,
                StoredAnalysisId: CanonicalId.FromGuid(analysisId).Value)));
        Assert.True(added.Succeeded, added.Refusal?.Message);
    }

    private static ProcessStartInfo CliStart(JobRunnerLaunchOptions runner, string projectPath)
    {
        var start = new ProcessStartInfo(BuildOutput.Cli)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in new[] { "apply", "--all-pending", "--project", projectPath, "--json" })
            start.ArgumentList.Add(argument);
        start.Environment[RunnerOptions.RootVariable] = runner.Root;
        start.Environment[ProcessRunnerLauncher.ExecutableVariable] = runner.WorkerExecutable;
        start.Environment[PanGlossExecutable.PathVariable] = runner.ParserPath;
        start.Environment[RunnerOptions.NamespaceVariable] = runner.OwnerNamespace;
        start.Environment[RunnerOptions.IdleVariable] = "1";
        start.Environment[ProcessRunnerLauncher.SuppressVariable] = "1";
        start.Environment.Remove(CommandSurfacePolicy.DeveloperCommandsEnvironmentVariable);
        return start;
    }

    private static async Task<(int ExitCode, string Output, string Error)> RunCliAsync(ProcessStartInfo start)
    {
        using var process = Process.Start(start)!;
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, await outputTask, await errorTask);
    }

    private static async Task<(int ExitCode, string Output, string Error)> RunApplyWithWorkerAsync(
        string projectPath, JobRunnerLaunchOptions runner)
    {
        using var process = Process.Start(CliStart(runner, projectPath))!;
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        using var worker = await StartQueuedWorkerAsync(projectPath, runner, process);
        await process.WaitForExitAsync();
        await worker.WaitForExitAsync();
        return (process.ExitCode, await outputTask, await errorTask);
    }

    private static async Task<(int ExitCode, string Output, string Error)> RunReconciliationFailureAsync(
        string projectPath, JobRunnerLaunchOptions runner)
    {
        string databasePath;
        using (var database = ProjectMotifDatabase.Open(projectPath))
            databasePath = database.FullPath;
        var start = CliStart(runner, projectPath);
        using var process = Process.Start(start)!;
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        using var worker = await StartQueuedWorkerAsync(projectPath, runner, process);
        try
        {
            await WaitForPendingProposalAnchorAsync(projectPath);
            File.SetAttributes(databasePath, File.GetAttributes(databasePath) | FileAttributes.ReadOnly);
            await process.WaitForExitAsync();
            return (process.ExitCode, await outputTask, await errorTask);
        }
        finally
        {
            File.SetAttributes(databasePath, File.GetAttributes(databasePath) & ~FileAttributes.ReadOnly);
            await worker.WaitForExitAsync();
        }
    }

    private static async Task<Process> StartQueuedWorkerAsync(
        string projectPath, JobRunnerLaunchOptions options, Process cli)
    {
        while (!cli.HasExited)
        {
            using var database = ProjectMotifDatabase.Open(projectPath);
            if (new JobRepository(database).ListActive().Any(job => job.Kind == JobCommands.DryRunKind))
            {
                var start = new ProcessStartInfo(options.WorkerExecutable!) { UseShellExecute = false };
                foreach (var argument in ProcessRunnerLauncher.LaunchArguments(options))
                    start.ArgumentList.Add(argument);
                return Process.Start(start)!;
            }
            await Task.Delay(20);
        }
        throw new InvalidOperationException(
            $"The CLI exited with {cli.ExitCode} before queueing a Dry Run for pending changes.");
    }

    private static async Task WaitForPendingProposalAnchorAsync(string projectPath)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            using var database = ProjectMotifDatabase.Open(projectPath);
            using var connection = database.OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT AnchorJson FROM Proposals ORDER BY rowid DESC LIMIT 1;";
            if (command.ExecuteScalar() is string) return;
            await Task.Delay(20);
        }
        throw new TimeoutException("The CLI did not bind its completed Dry Run to the pending Proposal.");
    }
}
