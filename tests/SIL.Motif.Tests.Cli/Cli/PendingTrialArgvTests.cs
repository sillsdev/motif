using System.Diagnostics;
using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Cli;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;
using SIL.Motif.Host.Installation;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.Store;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Cli;

[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group1)]
public sealed class PendingTrialArgvTests(PristineProjectFixture pristine)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AWaitedTrialWithAKilledParserReportsFailedAndKeepsJsonOnStdout(bool beforeOnly)
    {
        using var scratch = pristine.NewScratch();
        var path = scratch.ProjectId.Path;
        Guid wordformId = Guid.Empty;
        NonUndoableUnitOfWorkHelper.Do(scratch.ActionHandlerAccessor, () =>
            wordformId = scratch.ServiceLocator.GetInstance<IWfiWordformFactory>()
                .Create(TsStringUtils.MakeString("signal-word", scratch.DefaultVernWs)).Guid);
        new FwDataProjectLoader().Save(scratch);
        var root = Path.Combine(Path.GetDirectoryName(path)!, "signal-worker");
        Assert.True(BaselineCaptureCommand.Capture(new BaselineCaptureRequest(path), root).Succeeded);
        var initial = PendingChanges.Load(new PendingChangesRequest(path, MotifProductVersion.CurrentText));
        var pending = PendingChanges.Put(new PutPendingChangeRequest(path, MotifProductVersion.CurrentText,
            initial.Value!.Revision, new ChangeIntent(CanonicalId.Mint().Value, "incorrect-spelling",
                CanonicalId.FromGuid(wordformId).Value, "signal-word", OriginPage: "Texts")));
        Assert.True(pending.Succeeded, pending.Refusal?.Message);
        var parser = FakeParser.Copy(Path.Combine(root, "parser"));
        var killed = new { mode = "killed" };
        if (beforeOnly)
            FakeParser.BehaveInPhasesBesideExecutable(parser, "batch", killed, new { mode = "success" });
        else
            FakeParser.BehaveBesideExecutable(parser, killed);
        var words = Path.Combine(root, "signal-words.txt");
        File.WriteAllText(words, "signal-word\n");

        var result = await RunWithParserAsync(root, parser, "trial", "--project", path, pending.Value!.DraftId!,
            "--words", words, "--wait", "--json");

        Assert.True(result.ExitCode == 2, result.Error + result.Output);
        using var json = JsonDocument.Parse(result.Output);
        Assert.Equal("failed", json.RootElement.GetProperty("status").GetString());
        Assert.Equal("parser-killed", json.RootElement.GetProperty("failureCategory").GetString());
        Assert.Equal("assess.parser-killed", json.RootElement.GetProperty("failureCode").GetString());
        Assert.Contains("error:", result.Error);
    }

    [Theory]
    [InlineData("describe", false)]
    [InlineData("version", false)]
    [InlineData("describe", true)]
    [InlineData("version", true)]
    public async Task AWaitedTrialFailsWhenOnlyAnInitialParserProbeIsTerminated(string probe, bool memory)
    {
        using var scratch = pristine.NewScratch();
        var path = scratch.ProjectId.Path;
        Guid wordformId = Guid.Empty;
        NonUndoableUnitOfWorkHelper.Do(scratch.ActionHandlerAccessor, () =>
            wordformId = scratch.ServiceLocator.GetInstance<IWfiWordformFactory>()
                .Create(TsStringUtils.MakeString("probe-word", scratch.DefaultVernWs)).Guid);
        new FwDataProjectLoader().Save(scratch);
        var root = Path.Combine(Path.GetDirectoryName(path)!, "probe-worker");
        Assert.True(BaselineCaptureCommand.Capture(new BaselineCaptureRequest(path), root).Succeeded);
        var initial = PendingChanges.Load(new PendingChangesRequest(path, MotifProductVersion.CurrentText));
        var pending = PendingChanges.Put(new PutPendingChangeRequest(path, MotifProductVersion.CurrentText,
            initial.Value!.Revision, new ChangeIntent(CanonicalId.Mint().Value, "incorrect-spelling",
                CanonicalId.FromGuid(wordformId).Value, "probe-word", OriginPage: "Texts")));
        Assert.True(pending.Succeeded, pending.Refusal?.Message);
        var parser = FakeParser.Copy(Path.Combine(root, "parser"));
        var failure = memory
            ? (object)new { mode = "fail", exitCode = 1, standardError = "Out of memory.\n" }
            : new { mode = "killed" };
        FakeParser.BehaveInPhasesBesideExecutable(parser, probe, probe == "version"
            ? [new { mode = "success" }, failure, new { mode = "success" }]
            : [failure, new { mode = "success" }]);
        var words = Path.Combine(root, "probe-words.txt");
        File.WriteAllText(words, "probe-word\n");

        var result = await RunWithParserAsync(root, parser, "trial", "--project", path, pending.Value!.DraftId!,
            "--words", words, "--wait", "--json");

        Assert.True(result.ExitCode == 2, result.Error + result.Output);
        using var json = JsonDocument.Parse(result.Output);
        Assert.True(json.RootElement.TryGetProperty("status", out var status), result.Error + result.Output);
        Assert.Equal("failed", status.GetString());
        Assert.Equal(memory ? "out-of-memory" : "parser-killed",
            json.RootElement.GetProperty("failureCategory").GetString());
        Assert.Equal(memory ? "assess.out-of-memory" : "assess.parser-killed",
            json.RootElement.GetProperty("failureCode").GetString());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task AWaitedTrialFailsWhenOnlyItsGrammarCheckIsTerminated(bool baseline, bool memory)
    {
        using var scratch = pristine.NewScratch();
        var path = scratch.ProjectId.Path;
        Guid wordformId = Guid.Empty;
        NonUndoableUnitOfWorkHelper.Do(scratch.ActionHandlerAccessor, () =>
            wordformId = scratch.ServiceLocator.GetInstance<IWfiWordformFactory>()
                .Create(TsStringUtils.MakeString("grammar-word", scratch.DefaultVernWs)).Guid);
        new FwDataProjectLoader().Save(scratch);
        var root = Path.Combine(Path.GetDirectoryName(path)!, "grammar-worker");
        Assert.True(BaselineCaptureCommand.Capture(new BaselineCaptureRequest(path), root).Succeeded);
        var initial = PendingChanges.Load(new PendingChangesRequest(path, MotifProductVersion.CurrentText));
        var pending = PendingChanges.Put(new PutPendingChangeRequest(path, MotifProductVersion.CurrentText,
            initial.Value!.Revision, new ChangeIntent(CanonicalId.Mint().Value, "incorrect-spelling",
                CanonicalId.FromGuid(wordformId).Value, "grammar-word", OriginPage: "Texts")));
        Assert.True(pending.Succeeded, pending.Refusal?.Message);
        var parser = FakeParser.Copy(Path.Combine(root, "parser"));
        var failure = memory
            ? (object)new { mode = "fail", exitCode = 1, standardError = "memory allocation of 4096 bytes failed" }
            : new { mode = "killed" };
        FakeParser.BehaveInPhasesBesideExecutable(parser, "grammar-health",
            baseline ? new { mode = "success" } : failure, failure);
        var words = Path.Combine(root, "grammar-words.txt");
        File.WriteAllText(words, "grammar-word\n");

        var result = await RunWithParserAsync(root, parser, "trial", "--project", path, pending.Value!.DraftId!,
            "--words", words, "--wait", "--json");

        Assert.True(result.ExitCode == 2, result.Error + result.Output);
        using var json = JsonDocument.Parse(result.Output);
        Assert.Equal("failed", json.RootElement.GetProperty("status").GetString());
        Assert.Equal(memory ? "out-of-memory" : "parser-killed",
            json.RootElement.GetProperty("failureCategory").GetString());
        Assert.Equal(memory ? "assess.out-of-memory" : "assess.parser-killed",
            json.RootElement.GetProperty("failureCode").GetString());
        var jobId = json.RootElement.GetProperty("jobId").GetString()!;
        var stored = ProjectStoreCommand.Run(path, MotifProductVersion.CurrentText,
            (database, _) => SIL.Motif.Contract.Commands.CommandOutcome<SIL.Motif.Contract.Jobs.JobRecord>
                .Success(new JobRepository(database).Get(jobId)));
        Assert.True(stored.Value!.DryRunPublished);
        Assert.NotNull(stored.Value.DryRunJson);
    }

    [Fact]
    public async Task TrialOverBudgetKeepsItsBeforeAndPrunesOldUnrootedEvidence()
    {
        using var scratch = pristine.NewScratch();
        var path = scratch.ProjectId.Path;
        Guid wordformId = Guid.Empty;
        NonUndoableUnitOfWorkHelper.Do(scratch.ActionHandlerAccessor, () =>
            wordformId = scratch.ServiceLocator.GetInstance<IWfiWordformFactory>()
                .Create(TsStringUtils.MakeString("retention-word", scratch.DefaultVernWs)).Guid);
        new FwDataProjectLoader().Save(scratch);
        var root = Path.Combine(Path.GetDirectoryName(path)!, "retention-worker");
        Assert.True(BaselineCaptureCommand.Capture(new BaselineCaptureRequest(path), root).Succeeded);
        var wordsPath = Path.Combine(Path.GetDirectoryName(path)!, "retention-words.txt");
        var oldWordsPath = Path.Combine(Path.GetDirectoryName(path)!, "old-retention-words.txt");
        File.WriteAllText(oldWordsPath, "obsolete-evidence\n");
        File.WriteAllText(wordsPath, "retention-word\n");
        var old = await RunAsync(root, "assess", path, "--words", oldWordsPath, "--json");
        Assert.True(old.ExitCode == 0, old.Error);
        var oldIds = ProjectionJson.Deserialize<AssessCommandResponse>(old.Output)!.AssessmentIds;
        Assert.NotEmpty(oldIds);
        var reusable = await RunAsync(root, "assess", path, "--words", wordsPath, "--json");
        Assert.True(reusable.ExitCode == 0, reusable.Error);
        var reusableIds = ProjectionJson.Deserialize<AssessCommandResponse>(reusable.Output)!.AssessmentIds;
        var initial = PendingChanges.Load(new PendingChangesRequest(path, MotifProductVersion.CurrentText));
        var pending = PendingChanges.Put(new PutPendingChangeRequest(path, MotifProductVersion.CurrentText,
            initial.Value!.Revision, new ChangeIntent(CanonicalId.Mint().Value, "incorrect-spelling",
                CanonicalId.FromGuid(wordformId).Value, "retention-word", OriginPage: "Texts")));
        Assert.True(pending.Succeeded, pending.Refusal?.Message);
        var project = new ProjectLocator(Path.GetFullPath(path), Path.GetFileNameWithoutExtension(path));
        using var database = new ProjectDatabaseCatalog(MotifSchema.CurrentSchema, MotifProductVersion.Current)
            .OpenOwned(project);
        using (var connection = database.OpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "INSERT INTO ProposalPictures (ProposalId, MediaType, Width, Height, Bytes, CreatedUtc) " +
                "SELECT ProposalId, 'image/png', 1, 1, zeroblob(105 * 1024 * 1024), '2026-01-01T00:00:00Z' " +
                "FROM Proposals WHERE Role = 'pending-changes';";
            Assert.Equal(1, command.ExecuteNonQuery());
        }

        var result = await RunAsync(root, "trial", "--project", path, pending.Value!.DraftId!, "--words", wordsPath,
            "--wait", "--wait-timeout-ms", ((int)(JobProgress.Cap + TimeSpan.FromMinutes(1)).TotalMilliseconds)
                .ToString(System.Globalization.CultureInfo.InvariantCulture), "--json");

        Assert.True(result.ExitCode == 0, PendingTrialFailure(path, result));
        // A waited Trial prints its review summary, which does not name the job.
        var job = new JobRepository(database).Get(JobProgress.LatestJobId(path, JobCommands.TrialKind)!)!;
        var before = TrialJobInput.Parse(job.InputJson).BaselineAssessmentIds!;
        Assert.NotEmpty(before);
        Assert.Contains(before, reusableIds.Contains);
        var assessments = new AssessmentRepository(database);
        Assert.All(before, id => Assert.Equal(id, assessments.Get(id).AssessmentId));
        Assert.All(oldIds, id => Assert.Throws<KeyNotFoundException>(() => assessments.Get(id)));
    }

    [Fact]
    public async Task PendingTrialWaitsForTheCurrentDraftWithoutIdentityFlags()
    {
        using var scratch = pristine.NewScratch();
        var path = scratch.ProjectId.Path;
        Guid wordformId = Guid.Empty;
        NonUndoableUnitOfWorkHelper.Do(scratch.ActionHandlerAccessor, () =>
            wordformId = scratch.ServiceLocator.GetInstance<IWfiWordformFactory>()
                .Create(TsStringUtils.MakeString("pending-trial-word", scratch.DefaultVernWs)).Guid);
        new FwDataProjectLoader().Save(scratch);
        var root = Path.Combine(Path.GetDirectoryName(path)!, "pending-trial-worker");
        Assert.True(BaselineCaptureCommand.Capture(new BaselineCaptureRequest(path), root).Succeeded);
        var initial = PendingChanges.Load(new PendingChangesRequest(path, MotifProductVersion.CurrentText));
        var pending = PendingChanges.Put(new PutPendingChangeRequest(path, MotifProductVersion.CurrentText,
            initial.Value!.Revision, new ChangeIntent(CanonicalId.Mint().Value, "incorrect-spelling",
                CanonicalId.FromGuid(wordformId).Value, "pending-trial-word", OriginPage: "Texts")));
        Assert.True(pending.Succeeded, pending.Refusal?.Message);

        // The CLI's own wait outlasts the progress bound, so a slow Trial fails only if it stops progressing.
        var result = await RunAsync(root, "trial", "--pending", "--project", path, "--words", "pending-trial-word",
            "--wait", "--wait-timeout-ms", ((int)(JobProgress.Cap + TimeSpan.FromMinutes(1)).TotalMilliseconds)
                .ToString(System.Globalization.CultureInfo.InvariantCulture), "--json");

        if (result.ExitCode != 0)
            Assert.Fail(PendingTrialFailure(path, result));
        Assert.Equal(0, result.ExitCode);
        using var response = JsonDocument.Parse(result.Output);
        Assert.True(response.RootElement.GetProperty("evidenceComplete").GetBoolean());
        Assert.StartsWith("job/", response.RootElement.GetProperty("jobId").GetString());
    }

    [Fact]
    public async Task PendingTrialRequiresItsWaitFlag()
    {
        var result = await RunAsync(null, "trial", "--pending", "--project", "missing.fwdata",
            "--words", "word", "--json");

        Assert.Equal(1, result.ExitCode);
        Assert.Equal(FailureReason.InvalidArgument, Envelope(result.Error).Reason);
        Assert.Contains("Usage: motif trial --pending", Envelope(result.Error).Message, StringComparison.Ordinal);
        Assert.DoesNotContain("always waits", Envelope(result.Error).Message, StringComparison.OrdinalIgnoreCase);
    }

    private static Task<CliRun> RunAsync(string? root, params string[] arguments) =>
        RunWithParserAsync(root, FakeParser.ExecutablePath, arguments);

    private static async Task<CliRun> RunWithParserAsync(string? root, string parser, params string[] arguments)
    {
        var start = new ProcessStartInfo(BuildOutput.Cli) { UseShellExecute = false };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        start.Environment["MOTIF_DEVELOPER_COMMANDS"] = "1";
        if (root is not null)
        {
            CliProcess.EnableAdvancedAiMode(start, root);
            start.Environment[MotifUpdateGate.TestNamespaceVariable] = Guid.NewGuid().ToString("N");
            start.Environment[RunnerOptions.RootVariable] = root;
            start.Environment["MOTIF_WORKER_EXE"] = BuildOutput.Worker;
            start.Environment["MOTIF_PANGLOSS_EXE"] = parser;
            start.Environment[RunnerOptions.NamespaceVariable] = Guid.NewGuid().ToString("N");
            start.Environment[RunnerOptions.IdleVariable] = "1";
            start.Environment.Remove(ProcessRunnerLauncher.SuppressVariable);
        }
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        using var process = Process.Start(start)!;
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        var finished = Task.WhenAll(outputTask, errorTask, process.WaitForExitAsync());
        var project = ProjectArgument(arguments);
        if (root is null || project is null)
        {
            await finished.WaitAsync(Wait.Scaled(TimeSpan.FromMinutes(30)));
        }
        else
        {
            var stalled = await WatchTrialAsync(project, finished);
            if (stalled is not null)
            {
                try { process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { }
                Assert.Fail("'motif " + string.Join(' ', arguments) + "': " + stalled);
            }
        }
        return new CliRun(process.ExitCode, await outputTask, await errorTask);
    }

    // Why the Trial the CLI queued stopped progressing before the CLI finished, or null once it finishes.
    private static async Task<string?> WatchTrialAsync(string project, Task finished)
    {
        var progress = new JobProgress();
        var enqueueDeadline = DateTime.UtcNow + JobProgress.ClaimBound;
        while (!finished.IsCompleted)
        {
            var jobId = JobProgress.LatestJobId(project, JobCommands.TrialKind);
            if (jobId is null && DateTime.UtcNow > enqueueDeadline)
                return "no Trial was queued within " + JobProgress.ClaimBound;
            if (jobId is not null && JobProgress.Read(project, jobId) is var job &&
                progress.Stalled(job) is { } stalled)
                return stalled + " (" + JobProgress.Describe(job) + ")";
            await Task.WhenAny(finished, Task.Delay(TimeSpan.FromMilliseconds(100)));
        }
        await finished;
        return null;
    }

    private static string? ProjectArgument(string[] arguments)
    {
        var index = Array.IndexOf(arguments, "--project");
        return index >= 0 && index + 1 < arguments.Length && File.Exists(arguments[index + 1])
            ? arguments[index + 1]
            : null;
    }

    private static FailureEnvelope Envelope(string text) =>
        ProjectionJson.Deserialize<FailureEnvelope>(text)!;

    private static string PendingTrialFailure(string path, CliRun result)
    {
        var stderr = string.IsNullOrWhiteSpace(result.Error) ? "(empty)" : result.Error.Trim();
        try
        {
            var project = new ProjectLocator(Path.GetFullPath(path), Path.GetFileNameWithoutExtension(path));
            using var database = new ProjectDatabaseCatalog(MotifSchema.CurrentSchema, MotifProductVersion.Current)
                .OpenOwned(project);
            var job = new JobRepository(database)
                .ListByProjectAndKind(ProjectWorkspaceKey.Compute(project), JobCommands.TrialKind)
                .OrderByDescending(record => record.CreatedUtc, StringComparer.Ordinal)
                .FirstOrDefault();
            if (job is null)
                return $"Pending Trial CLI exited {result.ExitCode}; no Trial job was found; CLI stderr: {stderr}";

            var runnerClaimed = job.OwnerId is not null || job.ClaimToken is not null;
            return $"Pending Trial CLI exited {result.ExitCode}; job {job.JobId} state {job.Status}, " +
                $"attempt {job.Attempt}, runner claimed {runnerClaimed}, owner {job.OwnerId ?? "<none>"}, " +
                $"claim token present {job.ClaimToken is not null}, claim/last heartbeat UTC " +
                $"{job.HeartbeatUtc ?? "<none>"}; CLI stderr: {stderr}";
        }
        catch (Exception exception)
        {
            return $"Pending Trial CLI exited {result.ExitCode}; job state, attempt, and runner claim " +
                $"were unavailable ({exception.GetType().Name}: {exception.Message}); CLI stderr: {stderr}";
        }
    }

    private sealed record CliRun(int ExitCode, string Output, string Error);
}
