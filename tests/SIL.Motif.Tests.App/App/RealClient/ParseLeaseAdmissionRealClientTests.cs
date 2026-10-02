using System.Diagnostics;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Host;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker;
using Xunit;

namespace SIL.Motif.Tests.App.RealClient;

[Collection(LcmCacheTestCollection.Name)]
[Trait("MotifTestLevel", "Integration")]
public sealed class ParseLeaseAdmissionRealClientTests(PristineProjectFixture pristine)
{
    [Fact]
    public async Task AWorkerAndDirectAssessmentRefuseTheOtherOwnersParse()
    {
        using var project = await GrammarClientProject.OpenAsync(pristine);
        var parser = FakeParser.CopyRecordingInvocations(project.ManagedRoot);
        var request = AssessRequest(project.FwDataPath);
        var proposalId = CreateTrialProposal(project.FwDataPath, pristine.Seed.FirstSenseId);
        var options = WorkerOptions(project.ManagedRoot, parser);

        var workerStarted = Path.Combine(project.ManagedRoot, "worker-parse-started");
        var workerRelease = Path.Combine(project.ManagedRoot, "worker-parse-release");
        SetHeldBatch(parser, workerStarted, workerRelease);
        var firstJob = EnqueueTrial(project.FwDataPath, proposalId);
        await using (var worker = StartWorker(options, project.FwDataPath))
        {
            try
            {
                await WaitForFile(workerStarted, TimeSpan.FromSeconds(90));
                var refused = await project.Client.AssessAsync(request, new Progress<AssessmentProgress>(),
                    CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
                Assert.False(refused.Succeeded);
                Assert.Equal(FailureReason.Busy, refused.Refusal!.Reason);
                Assert.Single(FakeParser.Invocations(parser), command => command == "batch");
                File.WriteAllText(workerRelease + ".0", string.Empty);
                var finished = JobProgress.WaitUntilFinished(project.FwDataPath, firstJob,
                    "the worker Trial did not finish after its parser row was released");
                Assert.Equal(SIL.Motif.Contract.Jobs.JobStatus.Completed, finished.Status);
            }
            finally
            {
                File.WriteAllText(workerRelease + ".0", string.Empty);
                await worker.StopAsync();
            }
        }

        var clientStarted = Path.Combine(project.ManagedRoot, "client-parse-started");
        var clientRelease = Path.Combine(project.ManagedRoot, "client-parse-release");
        SetHeldBatch(parser, clientStarted, clientRelease);
        var direct = project.Client.AssessAsync(request, new Progress<AssessmentProgress>(),
            CancellationToken.None);
        await WaitForFile(clientStarted, TimeSpan.FromSeconds(30));
        var secondJob = EnqueueTrial(project.FwDataPath, proposalId);
        await using (var worker = StartWorker(options, project.FwDataPath))
        {
            try
            {
                var refused = JobProgress.WaitUntil(project.FwDataPath, secondJob,
                    job => job.Status == SIL.Motif.Contract.Jobs.JobStatus.Failed,
                    "the Trial worker did not return while the direct Assessment held the lease");
                Assert.Equal(SIL.Motif.Contract.Jobs.JobStatus.Failed, refused.Status);
                Assert.Contains("already running", refused.ResultJson, StringComparison.OrdinalIgnoreCase);
                Assert.Equal(2, FakeParser.Invocations(parser).Count(command => command == "batch"));
            }
            finally
            {
                File.WriteAllText(clientRelease + ".0", string.Empty);
                var completed = await direct.WaitAsync(TimeSpan.FromSeconds(30));
                Assert.True(completed.Succeeded, completed.Refusal?.Message);
                await worker.StopAsync();
            }
        }
    }

    [Fact]
    public async Task ADeadCliOwnerLeavesItsProjectLeaseAvailable()
    {
        using var project = await GrammarClientProject.OpenAsync(pristine);
        var parser = project.ParserPath;
        var wordsPath = Path.Combine(project.ManagedRoot, "lease-words.txt");
        File.WriteAllText(wordsPath, "motifa\n");
        var started = Path.Combine(project.ManagedRoot, "dead-owner-started");
        var release = Path.Combine(project.ManagedRoot, "dead-owner-release");
        project.Behave(new { startedPath = started, holdUntilPath = release });
        using var child = InterruptibleCli.Start(CliProcess.CreateStartInfo(project.ManagedRoot, parser, true,
            "assess", project.FwDataPath, "--words", wordsPath, "--json"));

        try
        {
            await WaitForFile(started, TimeSpan.FromSeconds(30));
            child.Process.Kill(entireProcessTree: true);
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
            Assert.True(child.Process.HasExited);

            project.Behave(new { });
            var retry = await project.Client.AssessAsync(AssessRequest(project.FwDataPath),
                new Progress<AssessmentProgress>(), CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(30));

            Assert.True(retry.Succeeded, retry.Refusal?.Message);
            Assert.Equal(2, project.Invocations().Count(command => command == "batch"));
        }
        finally
        {
            File.WriteAllText(release, string.Empty);
            if (!child.Process.HasExited) child.Process.Kill(entireProcessTree: true);
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
        }
    }

    private static AssessRequest AssessRequest(string projectPath) => new(
        projectPath, new SelectionRequest(false, [], [SeededProject.AnalysedWordForm], false, null), 1000);

    private static string CreateTrialProposal(string projectPath, Guid senseId)
    {
        var version = MotifProductVersion.CurrentText;
        const string draftName = "parse lease admission";
        var created = ProposalCommands.New(new NewDraftRequest(projectPath, version, draftName, "Lease test"));
        Assert.True(created.Succeeded, created.Refusal?.Message);
        var added = ProposalCommands.AddSetGloss(new AddSetGlossRequest(projectPath, version, draftName,
            SIL.Motif.Contract.Ids.CanonicalId.FromGuid(senseId).Value, "en", "lease test"));
        Assert.True(added.Succeeded, added.Refusal?.Message);
        return created.Value!.ProposalId;
    }

    private static string EnqueueTrial(string projectPath, string proposalId)
    {
        var queued = JobCommands.EnqueueTrial(new EnqueueTrialRequest(projectPath, MotifProductVersion.CurrentText,
            proposalId, Words: [SeededProject.AnalysedWordForm]));
        Assert.True(queued.Succeeded, queued.Refusal?.Message);
        return queued.Value!.JobId;
    }

    private static JobRunnerLaunchOptions WorkerOptions(string root, string parser) =>
        new(root, parser)
        {
            WorkerExecutable = BuildOutput.Worker,
            OwnerNamespace = "motif-parse-lease-" + Guid.NewGuid().ToString("N"),
            IdleTimeout = TimeSpan.FromMinutes(1),
        };

    private static void SetHeldBatch(string parser, string started, string release) =>
        FakeParser.BehaveBesideExecutable(parser, new
        {
            streamProgress = true,
            startedPath = started,
            holdEachWordUntil = release,
            words = new[] { new { word = SeededProject.AnalysedWordForm, outcome = "complete", elapsedMs = 2500 } },
        });

    private static RunningWorker StartWorker(JobRunnerLaunchOptions options, string projectPath)
    {
        var arguments = ProcessRunnerLauncher.LaunchArguments(options)
            .Concat([RunnerOptions.WakeProjectArgument, projectPath]).ToArray();
        var start = CliProcess.Start(options, arguments);
        start.FileName = BuildOutput.Worker;
        start.RedirectStandardInput = true;
        var process = Process.Start(start)!;
        process.StandardInput.Close();
        return new RunningWorker(process);
    }

    private static async Task WaitForFile(string path, TimeSpan cap)
    {
        var deadline = Stopwatch.GetTimestamp() + (long)(cap.TotalSeconds * Stopwatch.Frequency);
        while (!File.Exists(path))
        {
            Assert.True(Stopwatch.GetTimestamp() < deadline, $"The held parser never created '{path}'.");
            await Task.Delay(25);
        }
    }

    private sealed class RunningWorker(Process process) : IAsyncDisposable
    {
        private readonly Task<string> _stdout = process.StandardOutput.ReadToEndAsync();
        private readonly Task<string> _stderr = process.StandardError.ReadToEndAsync();

        public async Task StopAsync()
        {
            if (!process.HasExited)
            {
                try { process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { }
                catch (System.ComponentModel.Win32Exception) { }
            }
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
            await Task.WhenAll(_stdout, _stderr).WaitAsync(TimeSpan.FromSeconds(15));
        }

        public async ValueTask DisposeAsync()
        {
            await StopAsync();
            process.Dispose();
        }
    }
}
