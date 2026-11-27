using SIL.Motif.Tests.TestFixtures;
using System.Diagnostics;
using System.Text.Json;
using SIL.Motif.Cli;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Worker;
using SIL.Motif.Worker.Jobs;
using Xunit;

namespace SIL.Motif.Tests.Cli;

/// <summary>
/// Covers the verbs that let anything outside this process put work in the queue and read it back.
/// </summary>
/// <remarks>
/// Queue verbs are checked through the executable. Proposal and queue setup use the same dispatch in-process,
/// so the process boundary is exercised by each behavior under test rather than by every prerequisite command.
/// </remarks>
public sealed class JobVerbArgvTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "motif-job-verbs-" + Guid.NewGuid().ToString("N"));

    public JobVerbArgvTests()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Project, string.Empty);
    }

    private string Project => Path.Combine(_root, "project.fwdata");

    [Fact]
    public void EnqueueingADryRunLoadsAndValidatesTheProposalFirstThenPrintsAJobIdAndSucceeds()
    {
        var proposalId = FinalizeOneOperationProposal();

        var result = Run($"dry-run --project \"{Project}\" {proposalId}");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(string.Empty, result.Error);
        Assert.False(string.IsNullOrWhiteSpace(result.Output));
    }

    [Fact]
    public void DryRunOfAnAbsentProposalRefusesBeforeQueueingAnything()
    {
        var result = Run($"dry-run --project \"{Project}\" agent_AAECAwQFBgcICQoLDA0ODw --json");

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(FailureReason.NotFound, Envelope(result.Error).Reason);
        Assert.Equal("proposal.not-found", Envelope(result.Error).Code);
    }

    [Fact]
    public void DryRunWaitTimesOutWithADistinctFailureWhenNothingClaimsTheJob()
    {
        var proposalId = FinalizeOneOperationProposal();

        var result = Run($"dry-run --project \"{Project}\" {proposalId} --wait --wait-timeout-ms 300");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Timed out after", result.Error);
        Assert.Contains("jobs show", result.Error);
        Assert.DoesNotContain("cancellation", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("dry-run")]
    [InlineData("trial")]
    public void AWaitThatTimesOutSaysTheJobKeepsRunningAndNamesTheCommandThatResumesTheWait(string verb)
    {
        var proposalId = FinalizeOneOperationProposal();

        var result = Run($"{verb} --project \"{Project}\" {proposalId} --wait --wait-timeout-ms 0 --json");

        Assert.Equal(3, result.ExitCode);
        var refusal = Envelope(result.Error[result.Error.IndexOf('{')..]);
        Assert.Equal("job.wait-timeout", refusal.Code);
        Assert.Contains("not a failed", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("jobs wait " + refusal.Detail!["jobId"] + " --project", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DryRunWaitWithZeroTimeoutReturnsATypedRefusal()
    {
        var proposalId = FinalizeOneOperationProposal();

        var result = Run($"dry-run --project \"{Project}\" {proposalId} --wait --wait-timeout-ms 0 --json");

        Assert.Equal(3, result.ExitCode);
        Assert.Equal("job.wait-timeout", Envelope(result.Error).Code);
        Assert.Equal(FailureReason.Busy, Envelope(result.Error).Reason);
    }

    [Theory]
    [InlineData("dry-run")]
    [InlineData("trial")]
    public void TerminalWaitCancelsTheExactQueuedJobBeforeReturning(string verb)
    {
        var unrelated = Enqueue();
        var proposal = FinalizeOneOperationProposal();
        var result = Run($"{verb} --project \"{Project}\" {proposal} --wait --wait-terminal --wait-timeout-ms 0 --json");

        Assert.Equal(3, result.ExitCode);
        var refusal = Envelope(result.Error[result.Error.IndexOf('{')..]);
        Assert.Equal("job.wait-timeout", refusal.Code);
        using var database = ProjectMotifDatabase.Open(Project);
        var jobs = new JobRepository(database);
        Assert.Equal(JobStatus.Cancelled, jobs.Get(refusal.Detail!["jobId"])!.Status);
        Assert.Equal("cancelled", refusal.Detail["status"]);
        Assert.Equal(JobStatus.Queued, jobs.Get(unrelated)!.Status);
    }

    [Fact]
    public void TerminalWaitRefusesAnUntrackedJobWithoutCancellingIt()
    {
        var id = Enqueue();

        var result = Run($"jobs wait {id} --project \"{Project}\" --wait-terminal --wait-timeout-ms 0 --json");

        Assert.Equal("job.wait-kind-unsupported", Envelope(result.Error).Code);
        using var database = ProjectMotifDatabase.Open(Project);
        Assert.Equal(JobStatus.Queued, new JobRepository(database).Get(id)!.Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TerminalWaitReturnsAfterTheClaimedJobReleasesItsResources(bool parked)
    {
        var original = Enqueue();
        using var database = ProjectMotifDatabase.Open(Project);
        var jobs = new JobRepository(database);
        var key = jobs.Get(original)!.ProjectKey;
        jobs.CancelActive(original);
        const string id = "job/active-trial";
        jobs.Create(id, key, "trial", "{}", JobTimestamp.FormatUtc(DateTimeOffset.UtcNow));

        var heartbeat = Path.Combine(_root, "heartbeat");
        var processIdPath = Path.Combine(_root, "parser-pid");
        FakeParser.Behave(_root, new { heartbeatPath = heartbeat, processIdPath });
        using var queue = new MachinePanGlossQueue(["Local\\MotifTrialWaitTest-" + Guid.NewGuid().ToString("N")]);
        using var invoker = new PanGlossInvoker(FakeParser.ExecutablePath, queue);
        using var deadline = new CancellationTokenSource(Wait.Scaled(TimeSpan.FromMinutes(2)));
        var exitedParser = new TaskCompletionSource<PanGlossOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var loop = new JobRunnerLoop(new JobClaims(database), key, "test-runner",
            TimeSpan.FromSeconds(20), TimeSpan.Zero, new Dictionary<string, JobRunnerLoop.Handler>
            {
                ["trial"] = async (job, cancellation) =>
                {
                    if (parked) job.Transition(JobStatus.WaitingForBaseline);
                    var outcome = await invoker.RunAsync(new PanGlossRequest.Import(Project, Path.Combine(_root, "parser-output")),
                        "terminal-wait-test", cancellation);
                    // A terminal row can become visible before the handler releases its remaining resources.
                    job.Transition(JobStatus.Cancelled, JobFailureCategory.Cancellation);
                    exitedParser.SetResult(outcome);
                    await release.Task.WaitAsync(deadline.Token);
                    return null;
                },
            });
        var worker = loop.RunUntilIdleAsync(deadline.Token);
        Task<CliRun>? waiter = null;
        try
        {
            Assert.True(await Wait.ForAsync(() => File.Exists(heartbeat), TimeSpan.FromSeconds(30)));
            using var process = Process.GetProcessById(int.Parse(File.ReadAllText(processIdPath)));
            Assert.False(process.HasExited);
            waiter = Task.Run(() => Run($"jobs wait {id} --project \"{Project}\" --wait-terminal --wait-timeout-ms 0 --json",
                Wait.Scaled(TimeSpan.FromMinutes(2))));
            var first = await Task.WhenAny(waiter, exitedParser.Task).WaitAsync(deadline.Token);
            if (ReferenceEquals(waiter, first))
                Assert.Fail("The wait returned before the parser exited: " + (await waiter).Error);
            Assert.IsType<PanGlossOutcome.Cancelled>(await exitedParser.Task);
            Assert.Equal(JobStatus.Cancelled, jobs.Get(id)!.Status);
            Assert.True(process.HasExited);
            Assert.False(waiter.IsCompleted);
        }
        finally
        {
            release.TrySetResult();
            await deadline.CancelAsync();
            await worker;
        }
        var refusal = Envelope((await waiter!.WaitAsync(Wait.Scaled(TimeSpan.FromSeconds(30)))).Error);
        Assert.Equal("job.wait-timeout", refusal.Code);
        Assert.Equal(id, refusal.Detail!["jobId"]);
        Assert.Equal("cancelled", refusal.Detail["status"]);
    }

    [Theory]
    [InlineData("dry-run")]
    [InlineData("trial")]
    [InlineData("trial --pending")]
    public void MalformedWaitTimeoutReturnsUsageBeforeStartingWork(string verb)
    {
        var arguments = verb switch
        {
            "dry-run" => $"dry-run --project \"{Project}\" agent_AAECAwQFBgcICQoLDA0ODw " +
                "--wait --wait-timeout-ms invalid --json",
            "trial" => $"trial --project \"{Project}\" agent_AAECAwQFBgcICQoLDA0ODw " +
                "--wait --wait-timeout-ms invalid --json",
            _ => $"trial --pending --project \"{Project}\" --words word --wait " +
                "--wait-timeout-ms invalid --json",
        };

        var result = Run(arguments);

        Assert.Equal(1, result.ExitCode);
        Assert.Equal(FailureReason.InvalidArgument, Envelope(result.Error).Reason);
        Assert.Contains("Usage: motif", Envelope(result.Error).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EnqueueingATrialOfAFinalizedProposalPrintsAJobIdAndSucceeds()
    {
        var proposalId = FinalizeOneOperationProposal();

        var result = Run($"trial --project \"{Project}\" {proposalId}");

        Assert.Equal(0, result.ExitCode);
        Assert.StartsWith("warning: the Trial has no Baseline Assessment", result.Error, StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(result.Output));

        var jobId = result.Output.Trim();
        var shown = Run($"jobs show {jobId} --project \"{Project}\" --json");
        Assert.Equal(0, shown.ExitCode);
        using var document = JsonDocument.Parse(shown.Output);
        Assert.Equal("trial", document.RootElement.GetProperty("kind").GetString());
    }

    [Fact]
    public void TrialAllWordsFlagRequestsTheWholeCorpus()
    {
        var proposalId = FinalizeOneOperationProposal();
        var defaultRun = Run($"trial --project \"{Project}\" {proposalId}");
        var allRun = Run($"trial --project \"{Project}\" {proposalId} --all-words");

        Assert.True(defaultRun.ExitCode == 0, $"Expected exit code 0; got {defaultRun.ExitCode}.{Environment.NewLine}{defaultRun.Error}");
        Assert.True(allRun.ExitCode == 0, $"Expected exit code 0; got {allRun.ExitCode}.{Environment.NewLine}{allRun.Error}");
        using var database = ProjectMotifDatabase.Open(Project);
        var jobs = new JobRepository(database);
        using var defaultInput = JsonDocument.Parse(jobs.Get(defaultRun.Output.Trim())!.InputJson);
        using var allInput = JsonDocument.Parse(jobs.Get(allRun.Output.Trim())!.InputJson);
        Assert.Equal(JsonValueKind.Null, defaultInput.RootElement.GetProperty("words").ValueKind);
        Assert.False(defaultInput.RootElement.GetProperty("allWords").GetBoolean());
        Assert.True(allInput.RootElement.GetProperty("allWords").GetBoolean());
        Assert.Equal(JsonValueKind.Null, allInput.RootElement.GetProperty("words").ValueKind);
    }

    [Fact]
    public void TrialWordsFileRetainsTheExactBoundedSelection()
    {
        var proposalId = FinalizeOneOperationProposal();
        var wordsPath = Path.Combine(_root, "verification-words.txt");
        File.WriteAllLines(wordsPath, ["motifa", "sipu"]);

        var result = Run($"trial --project \"{Project}\" {proposalId} --words \"{wordsPath}\"");

        Assert.Equal(0, result.ExitCode);
        using var database = ProjectMotifDatabase.Open(Project);
        var inputJson = new JobRepository(database).Get(result.Output.Trim())!.InputJson;
        using var input = JsonDocument.Parse(inputJson);
        Assert.Equal(new[] { "motifa", "sipu" },
            input.RootElement.GetProperty("words").EnumerateArray().Select(item => item.GetString()));
        Assert.False(input.RootElement.GetProperty("allWords").GetBoolean());
    }

    [Fact]
    public void EnqueueingATrialOfAnUncommittedDraftPrintsAJobIdAndSucceeds()
    {
        var created = Run($"new --project \"{Project}\" --draft d2");
        Assert.Equal(0, created.ExitCode);

        const string marker = "proposalId: ";
        var start = created.Output.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, "Could not find '" + marker + "' in new's output: " + created.Output);
        var proposalId = created.Output.Substring(start + marker.Length).TrimEnd('\r', '\n');

        var result = Run($"trial --project \"{Project}\" {proposalId}");

        Assert.Equal(0, result.ExitCode);
        Assert.False(string.IsNullOrWhiteSpace(result.Output));
    }

    [Fact]
    public void TrialOfAnAbsentProposalRefusesBeforeQueueingAnything()
    {
        var result = Run($"trial --project \"{Project}\" agent_AAECAwQFBgcICQoLDA0ODw --json");

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(FailureReason.NotFound, Envelope(result.Error).Reason);
        Assert.Contains("not found", result.Error);
    }

    private string FinalizeOneOperationProposal()
    {
        Assert.Equal(0, RunInProcess($"new --project \"{Project}\" --draft d").ExitCode);
        Assert.Equal(0, RunInProcess(
            $"add-set-gloss --project \"{Project}\" --draft d --target agent_AAECAwQFBgcICQoLDA0ODw " +
                "--ws en --text hello").ExitCode);
        Assert.Equal(0, RunInProcess($"label --project \"{Project}\" --draft d \"a label\"").ExitCode);
        Assert.True(DraftRationale.Write(Project, SIL.Motif.Host.MotifProductVersion.CurrentText, "d", "a rationale")
            .Succeeded);
        var finalized = RunInProcess($"finalize --project \"{Project}\" --draft d");
        Assert.Equal(0, finalized.ExitCode);

        const string marker = "-> Proposal ";
        var start = finalized.Output.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, "Could not find '" + marker + "' in finalize output: " + finalized.Output);
        start += marker.Length;
        var end = finalized.Output.IndexOf(' ', start);
        return finalized.Output.Substring(start, end - start);
    }

    [Fact]
    public void AQueuedJobIsReadableByIdAsJson()
    {
        var jobId = Enqueue();

        var shown = Run($"jobs show {jobId} --project \"{Project}\" --json");

        Assert.Equal(0, shown.ExitCode);
        using var document = JsonDocument.Parse(shown.Output);
        Assert.Equal(jobId, document.RootElement.GetProperty("jobId").GetString());
        // Queued rather than running: nothing has claimed it, because no runner is going.
        Assert.Equal("queued", document.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public void WaitingForABudgetStoppedJobExitsFiveWithCounts()
    {
        Enqueue();
        using var database = ProjectMotifDatabase.Open(Project);
        var jobs = new JobRepository(database);
        var project = SIL.Motif.Worker.Projects.ProjectWorkspaceKey.Compute(
            new SIL.Motif.Contract.Projects.ProjectLocator(Path.GetFullPath(Project), Path.GetFileNameWithoutExtension(Project)));
        jobs.RecordBudgetStop("budget-job", project, "trial", "{}",
            "{\"code\":\"assessment.stopped-by-budget\",\"message\":\"Parsing stopped: 1 word done, 2 not done.\",\"wordsDone\":1,\"wordsNotDone\":2}",
            DateTimeOffset.UtcNow.ToString("O"));
        var result = Run($"jobs wait budget-job --project \"{Project}\" --json");
        Assert.Equal(5, result.ExitCode);
        Assert.Equal("assessment.stopped-by-budget", Envelope(result.Error).Code);
        Assert.Equal("1", Envelope(result.Error).Detail!["wordsDone"]);
        Assert.Equal("2", Envelope(result.Error).Detail!["wordsNotDone"]);
    }

    [Fact]
    public void AJobIdNobodyQueuedIsNotFoundRatherThanACrash()
    {
        Enqueue();

        var shown = Run($"jobs show job/absent --project \"{Project}\" --json");

        Assert.Equal(2, shown.ExitCode);
        Assert.Equal(FailureReason.NotFound, Envelope(shown.Error).Reason);
        Assert.DoesNotContain("   at ", shown.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void AMalformedJobIdIsAnInvocationErrorRatherThanARefusal()
    {
        Enqueue();

        var shown = Run($"jobs show \" \" --project \"{Project}\" --json");

        // Exit 1, not 2: the caller cannot fix this by retrying, and must not be told it can.
        Assert.Equal(1, shown.ExitCode);
        Assert.Equal(FailureReason.InvalidArgument, Envelope(shown.Error).Reason);
    }

    [Fact]
    public void OmittingTheProjectIsAUsageFailureThatNamesTheFlag()
    {
        var result = Run("jobs show job/anything");

        Assert.Equal(1, result.ExitCode);
        // Naming the verb is what separates this from the unknown-verb banner, which also lists --project.
        Assert.Contains("Usage: motif jobs show <jobId> --project", result.Error, StringComparison.Ordinal);
    }

    private string Enqueue()
    {
        var queuedId = JobProgress.QueueJob(Project);
        return queuedId;
    }

    private CliProcessResult RunInProcess(string arguments) =>
        CliInProcess.RunCommandLine(_root, null, developerCommands: true, arguments);

    private static FailureEnvelope Envelope(string stderr) =>
        ProjectionJson.Deserialize<FailureEnvelope>(stderr)!;

    private CliRun Run(string arguments, TimeSpan? cleanupBudget = null)
    {
        var executable = BuildOutput.Cli;
        var start = new ProcessStartInfo(executable)
        {
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        // This suite asserts on the queue with nothing claiming it; a real kicked runner would race it.
        start.Environment[RunnerOptions.RootVariable] = _root;
        CliProcess.EnableAdvancedAiMode(start, _root);
        start.Environment[ProcessRunnerLauncher.SuppressVariable] = "1";
        start.Environment["MOTIF_DEVELOPER_COMMANDS"] = "1";
        if (cleanupBudget is { } budget)
            start.Environment[JobWait.CleanupBudgetVariable] = ((int)budget.TotalMilliseconds).ToString();
        using var process = Process.Start(start)!;
        // Both pipes drain concurrently: a sequential read deadlocks past the pipe buffer.
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        var output = outputTask.GetAwaiter().GetResult();
        var error = errorTask.GetAwaiter().GetResult();
        Assert.True(process.WaitForExit((int)Wait.Scaled(TimeSpan.FromMinutes(30)).TotalMilliseconds),
            "The CLI process did not exit within 30 minutes (scaled by MOTIF_TEST_DEADLINE_SCALE).");
        return new CliRun(process.ExitCode, output, error);
    }

    private sealed record CliRun(int ExitCode, string Output, string Error);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); }
        catch (DirectoryNotFoundException) { }
        catch (IOException) { }
    }
}
