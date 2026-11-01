using SIL.Motif.Commands.Queries;
using SIL.Motif.Commands.Handoff;
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
using Xunit.Abstractions;

namespace SIL.Motif.Tests.App.RealClient;

[Collection(LcmCacheTestCollection.Name)]
[Trait("MotifTestLevel", "Integration")]
public sealed class ParseLeaseAdmissionRealClientTests(PristineProjectFixture pristine, ITestOutputHelper output)
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
        var direct = await StartAssessmentHeldAt(project, clientStarted, TimeSpan.FromMinutes(2));
        var secondJob = EnqueueTrial(project.FwDataPath, proposalId);
        await using (var worker = StartWorker(options, project.FwDataPath))
        {
            Exception? failure = null;
            try
            {
                var refused = JobProgress.WaitUntil(project.FwDataPath, secondJob,
                    job => job.Status == SIL.Motif.Contract.Jobs.JobStatus.Failed,
                    "the Trial worker did not return while the direct Assessment held the lease");
                Assert.Equal(SIL.Motif.Contract.Jobs.JobStatus.Failed, refused.Status);
                Assert.Contains("already running", refused.ResultJson, StringComparison.OrdinalIgnoreCase);
                Assert.Equal(2, FakeParser.Invocations(parser).Count(command => command == "batch"));
            }
            catch (Exception exception)
            {
                failure = exception;
                throw;
            }
            finally
            {
                File.WriteAllText(clientRelease + ".0", string.Empty);
                var workerOutput = "the worker did not stop in time";
                try { workerOutput = await worker.StopAsync(); }
                catch (TimeoutException) when (failure is not null) { }
                if (failure is not null)
                    throw new InvalidOperationException($"{failure.GetType().Name}: {failure.Message}{Environment.NewLine}" +
                        $"Trial worker output (last {OutputLimit} characters):{Environment.NewLine}{Tail(workerOutput)}", failure);
                var completed = await direct.WaitAsync(TimeSpan.FromMinutes(2));
                Assert.True(completed.Succeeded, Why(completed.Refusal));
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
            var retry = await AssessOnceTheDeadOwnersStoreIsFree(project).WaitAsync(TimeSpan.FromSeconds(30));

            Assert.True(retry.Succeeded, Why(retry.Refusal));
            Assert.Equal(2, project.Invocations().Count(command => command == "batch"));
        }
        finally
        {
            File.WriteAllText(release, string.Empty);
            if (!child.Process.HasExited) child.Process.Kill(entireProcessTree: true);
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
        }
    }

    [Fact]
    public async Task AHandoffAutomaticTraceKeepsAdmissionAcrossProcesses()
    {
        using var project = await GrammarClientProject.OpenAsync(pristine);
        var heartbeat = Path.Combine(project.ManagedRoot, "handoff-trace-heartbeat");
        project.Behave(new
        {
            subcommands = new Dictionary<string, object>
            {
                ["parse"] = new { heartbeatPath = heartbeat },
            },
        });
        var words = Path.Combine(project.ManagedRoot, "handoff-words.txt");
        var destination = Path.Combine(project.ManagedRoot, "held-handoff");
        File.WriteAllText(words, SeededProject.AnalysedWordForm + "\n");
        using var cancellation = new CancellationTokenSource();
        var handoff = project.Client.HandoffAsync(new HandoffRequest(
                project.FwDataPath, destination,
                new SelectionRequest(false, [], [SeededProject.AnalysedWordForm], false, null), true),
            new Progress<AssessmentProgress>(), cancellation.Token);
        try
        {
            await WaitForFile(heartbeat, TimeSpan.FromSeconds(30));
            Assert.Single(project.Invocations(), command => command == "batch");
            Assert.Single(project.Invocations(), command => command == "parse");
            var cli = await CliProcess.RunAsync(project.ManagedRoot, project.ParserPath, true,
                "assess", project.FwDataPath, "--words", words, "--json");
            Assert.True(cli.ExitCode != 0, cli.FailureDetails);
            Assert.Contains("parse.already-running", cli.Error);
            var other = RealCommandClient.Create(project.ManagedRoot, project.ParserPath);
            var trace = await other.TraceWordAsync(new WordTraceRequest(project.FwDataPath, "motifa"),
                CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(FailureReason.Busy, trace.Refusal?.Reason);
            Assert.Single(project.Invocations(), command => command == "batch");
            Assert.Single(project.Invocations(), command => command == "parse");
        }
        finally
        {
            cancellation.Cancel();
            var cancelled = await handoff.WaitAsync(TimeSpan.FromSeconds(20));
            Assert.Equal("handoff.cancelled", cancelled.Refusal?.Code);
            Assert.False(Directory.Exists(destination));
        }
        project.Behave(new { });
        var retry = await project.Client.AssessAsync(AssessRequest(project.FwDataPath),
            new Progress<AssessmentProgress>(), CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(30));
        Assert.True(retry.Succeeded, Why(retry.Refusal));
    }

    private const int OutputLimit = 4000;

    private static string Tail(string text) => text.Length <= OutputLimit ? text : text[^OutputLimit..];

    // Only these refusals are retried: SQLite reports the store Busy for a moment after its owner was killed.
    private static bool IsStoreBusy(Refusal? refusal) =>
        refusal is { Code: "project.store-io" or "project.operation-io", Reason: FailureReason.Busy };

    private static readonly TimeSpan StoreBusyPatience = TimeSpan.FromSeconds(10);

    private async Task<CommandOutcome<AssessCommandResponse>> AssessOnceTheDeadOwnersStoreIsFree(GrammarClientProject project)
    {
        var elapsed = Stopwatch.StartNew();
        for (var attempt = 1; ; attempt++)
        {
            var outcome = await project.Client.AssessAsync(AssessRequest(project.FwDataPath),
                new Progress<AssessmentProgress>(), CancellationToken.None);
            if (!IsStoreBusy(outcome.Refusal) || elapsed.Elapsed > StoreBusyPatience)
            {
                output.WriteLine($"store free after {attempt} attempt(s), {elapsed.ElapsedMilliseconds} ms");
                return outcome;
            }
            await Task.Delay(50);
        }
    }

    // Starts a held Assessment, starting it again while the store is Busy and its parser has not begun.
    private async Task<Task<CommandOutcome<AssessCommandResponse>>> StartAssessmentHeldAt(
        GrammarClientProject project, string startedPath, TimeSpan cap)
    {
        var elapsed = Stopwatch.StartNew();
        var description = "the direct Assessment did not reach its held parser row";
        for (var attempt = 1; ; attempt++)
        {
            var direct = project.Client.AssessAsync(AssessRequest(project.FwDataPath),
                new Progress<AssessmentProgress>(), CancellationToken.None);
            while (!File.Exists(startedPath) && !direct.IsCompleted)
            {
                Assert.True(elapsed.Elapsed < cap, $"{description} at '{startedPath}' within {cap.TotalMinutes:0.#} minutes.");
                await Task.WhenAny(direct, Task.Delay(25));
            }

            if (File.Exists(startedPath))
            {
                output.WriteLine($"direct Assessment held its parser on attempt {attempt}, {elapsed.ElapsedMilliseconds} ms");
                return direct;
            }

            var busy = !direct.IsFaulted && !direct.IsCanceled && IsStoreBusy(direct.Result.Refusal);
            if (!busy || elapsed.Elapsed > StoreBusyPatience)
                Assert.Fail($"{description}, but attempt {attempt} completed before the held parser created '{startedPath}' " +
                    $"after {elapsed.ElapsedMilliseconds} ms: {Describe(direct)}");
            await Task.Delay(50);
        }
    }

    private static AssessRequest AssessRequest(string projectPath) => new(
        projectPath, new SelectionRequest(false, [], [SeededProject.AnalysedWordForm], false, null), 1000);

    private static string CreateTrialProposal(string projectPath, Guid senseId)
    {
        var version = MotifProductVersion.CurrentText;
        const string draftName = "parse lease admission";
        var created = ProposalCommands.New(new NewDraftRequest(projectPath, version, draftName, "Lease test"));
        Assert.True(created.Succeeded, Why(created.Refusal));
        var added = ProposalCommands.AddSetGloss(new AddSetGlossRequest(projectPath, version, draftName,
            SIL.Motif.Contract.Ids.CanonicalId.FromGuid(senseId).Value, "en", "lease test"));
        Assert.True(added.Succeeded, Why(added.Refusal));
        return created.Value!.ProposalId;
    }

    private static string EnqueueTrial(string projectPath, string proposalId)
    {
        var queued = JobCommands.EnqueueTrial(new EnqueueTrialRequest(projectPath, MotifProductVersion.CurrentText,
            proposalId, Words: [SeededProject.AnalysedWordForm]));
        Assert.True(queued.Succeeded, Why(queued.Refusal));
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
        FakeParser.BehaveInPhasesBesideExecutable(parser, "batch", new
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

    private static string Why(Refusal? refusal) => refusal is null ? "it succeeded" :
        $"{refusal.Code} ({refusal.Reason}): {refusal.Message} " +
        string.Join("; ", refusal.Facts.Select(fact => $"{fact.Key}={fact.Value}"));

    private static string Describe<T>(Task<CommandOutcome<T>> operation) where T : class =>
        operation.IsFaulted ? $"it faulted with {operation.Exception}" :
        operation.IsCanceled ? "it was cancelled" :
        $"its outcome: {Why(operation.Result.Refusal)}";

    private static async Task WaitForFile(string path, TimeSpan cap, string description = "the held parser never started")
    {
        var deadline = Stopwatch.GetTimestamp() + (long)(cap.TotalSeconds * Stopwatch.Frequency);
        while (!File.Exists(path))
        {
            Assert.True(Stopwatch.GetTimestamp() < deadline,
                $"{description} at '{path}' within {cap.TotalMinutes:0.#} minutes.");
            await Task.Delay(25);
        }
    }

    private sealed class RunningWorker(Process process) : IAsyncDisposable
    {
        private readonly Task<string> _stdout = process.StandardOutput.ReadToEndAsync();
        private readonly Task<string> _stderr = process.StandardError.ReadToEndAsync();

        public async Task<string> StopAsync()
        {
            if (!process.HasExited)
            {
                try { process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { }
                catch (System.ComponentModel.Win32Exception) { }
            }
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
            await Task.WhenAll(_stdout, _stderr).WaitAsync(TimeSpan.FromSeconds(15));
            return $"stdout: {_stdout.Result}{Environment.NewLine}stderr: {_stderr.Result}";
        }

        public async ValueTask DisposeAsync()
        {
            await StopAsync();
            process.Dispose();
        }
    }
}
