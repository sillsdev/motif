using System.Diagnostics;
using System.ComponentModel;
using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Analysis;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.Parser;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Worker;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Cli;

[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group3)]
public sealed class PendingApplyArgvTests(PristineProjectFixture pristine)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ApplyAllPendingWritesOneReceiptWithOptionalRevision(bool includeRevision)
    {
        var (path, wordformId) = ReleasedProjectWithWord("review-word");
        var root = Path.Combine(Path.GetDirectoryName(path)!, "pending-apply-worker");
        var parser = FakeParser.Copy(root);
        var runner = IsolatedRunner.Process(root, parser);
        var captured = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(path), root);
        Assert.True(captured.Succeeded, captured.Refusal?.Message);
        var initial = PendingChanges.Load(new PendingChangesRequest(path, "1.0"));
        var added = PendingChanges.Put(new PutPendingChangeRequest(path, "1.0", initial.Value!.Revision,
            new ChangeIntent(CanonicalId.Mint().Value, "incorrect-spelling",
                CanonicalId.FromGuid(wordformId).Value, "review-word", OriginPage: "Texts")));
        Assert.True(added.Succeeded, added.Refusal?.Message);
        var measured = await PendingChangesWorkflow.Measure(new MeasurePendingRequest(
            path, added.Value!.DraftId!, added.Value.Revision, ["review-word"]),
            new Progress<MeasureProgress>(), CancellationToken.None, runnerLauncher: runner);
        Assert.True(measured.Succeeded, measured.Refusal?.Message);
        Assert.True(measured.Value!.EvidenceComplete);

        var apply = CliProcess.Start(runner.Options, "apply", "--all-pending", "--project", path);
        if (includeRevision)
        {
            apply.ArgumentList.Add("--revision");
            apply.ArgumentList.Add(added.Value.Revision);
        }
        apply.ArgumentList.Add("--json");
        var (exitCode, output, error) = await CliProcess.RunAsync(apply);

        Assert.True(exitCode == 0, $"CLI failed with {exitCode}: {error}{output}");
        ApplyAllPendingContract.Match("applied", exitCode, output, error);
        using var response = JsonDocument.Parse(output);
        Assert.True(response.RootElement.GetProperty("ok").GetBoolean());
        Assert.True(response.RootElement.GetProperty("applied").GetBoolean());
        Assert.Equal(added.Value.DraftId,
            response.RootElement.GetProperty("receipt").GetProperty("proposalId").GetString());
        Assert.Equal("Marked 1 word as incorrectly spelled.",
            response.RootElement.GetProperty("summary").GetString());

        using var database = ProjectMotifDatabase.Open(path);
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Receipts WHERE ProposalId = $proposal;";
        command.Parameters.AddWithValue("$proposal", added.Value.DraftId);
        Assert.Equal(1L, (long)command.ExecuteScalar()!);

        using (var cache = new FwDataProjectLoader().LoadScratchCache(path))
            Assert.Contains("review-word", ApprovedMorphologyReader.ReadIncorrectSpellings(cache));
    }

    [Fact]
    public async Task AfterMotifsOwnApplyTheCliDryRunAndTrialRecaptureByThemselvesButAnOutsideSaveIsRefused()
    {
        var (path, wordformId) = ReleasedProjectWithWord("review-word", out var senseId);
        var root = Path.Combine(Path.GetDirectoryName(path)!, "recapture-worker");
        var runner = IsolatedRunner.Process(root, FakeParser.Copy(root));
        Assert.True(BaselineCaptureCommand.Capture(new BaselineCaptureRequest(path), root).Succeeded);
        var added = PendingChanges.Put(new PutPendingChangeRequest(path, "1.0",
            PendingChanges.Load(new PendingChangesRequest(path, "1.0")).Value!.Revision,
            new ChangeIntent(CanonicalId.Mint().Value, "incorrect-spelling",
                CanonicalId.FromGuid(wordformId).Value, "review-word", OriginPage: "Texts")));
        Assert.True(added.Succeeded, added.Refusal?.Message);
        var measured = await PendingChangesWorkflow.Measure(new MeasurePendingRequest(
            path, added.Value!.DraftId!, added.Value.Revision, ["review-word"]),
            new Progress<MeasureProgress>(), CancellationToken.None, runnerLauncher: runner);
        Assert.True(measured.Succeeded, measured.Refusal?.Message);
        var later = ProposalCommands.New(new NewDraftRequest(path, "1.0", "later", "A later gloss"));
        Assert.True(later.Succeeded, later.Refusal?.Message);
        Assert.True(ProposalCommands.AddSetGloss(new AddSetGlossRequest(path, "1.0", "later",
            CanonicalId.FromGuid(senseId).Value, "en", "a later gloss")).Succeeded);
        var laterId = later.Value!.ProposalId;
        var wordsPath = Path.Combine(root, "words.txt");
        File.WriteAllText(wordsPath, "review-word" + Environment.NewLine);

        async Task<(int ExitCode, string Output, string Error)> Cli(params string[] arguments)
        {
            var start = CliProcess.Start(runner.Options, arguments);
            CliProcess.EnableAdvancedAiMode(start, root);
            return await CliProcess.RunAsync(start);
        }

        var apply = await Cli("apply", "--all-pending", "--project", path, "--json");
        Assert.True(apply.ExitCode == 0, apply.Error + apply.Output);

        var dryRun = await Cli("dry-run", "--project", path, laterId, "--wait", "--json");
        Assert.True(dryRun.ExitCode == 0, dryRun.Error + dryRun.Output);
        var trial = await Cli("trial", "--project", path, laterId, "--words", wordsPath, "--wait", "--json");
        Assert.True(trial.ExitCode == 0, trial.Error + trial.Output);
        Assert.Equal(SIL.Motif.Contract.Jobs.JobStatus.Completed,
            ProjectionJson.Deserialize<SIL.Motif.Contract.Jobs.JobStatusResponse>(trial.Output)!.Status);
        // Two jobs from the first Trial, then the Dry Run and Trial queued after Motif's own Apply.
        Assert.Equal(4, CompletedMeasureJobs(path).Length);
        Assert.All(CompletedMeasureJobs(path), status => Assert.StartsWith("completed", status));

        // A save made outside Motif: `dry-run` is refused until a capture.
        new FieldWorksSimulator(path).AddWordform("outside-word");
        var refusedDryRun = await Cli("dry-run", "--project", path, laterId, "--json");
        Assert.NotEqual(0, refusedDryRun.ExitCode);
        Assert.Contains("change.refresh-required", refusedDryRun.Error);
        var capture = await Cli("baseline", "capture", "--project", path, "--json");
        Assert.True(capture.ExitCode == 0, capture.Error + capture.Output);
        var accepted = await Cli("dry-run", "--project", path, laterId, "--wait", "--json");
        Assert.True(accepted.ExitCode == 0, accepted.Error + accepted.Output);

        // `trial` measures the Baseline before it queues, so it recaptures a saved project by itself.
        new FieldWorksSimulator(path).AddWordform("another-outside-word");
        var recaptured = await Cli("trial", "--project", path, laterId, "--words", wordsPath, "--wait", "--json");
        Assert.True(recaptured.ExitCode == 0, recaptured.Error + recaptured.Output);
    }

    [Fact]
    public async Task ApplyAllPendingRefusesWhileTheProjectCarriesAReconciliationWarning()
    {
        var (path, _) = ReleasedProjectWithWord("marked-word");
        var root = Path.Combine(Path.GetDirectoryName(path)!, "marked-pending-apply-worker");
        var captured = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(path), root);
        Assert.True(captured.Succeeded, captured.Refusal?.Message);
        ProjectReconciliationMarker.Mark(path);
        var projectBytes = File.ReadAllBytes(path);

        var apply = CliProcess.Start(IsolatedRunner.Options(root), "apply", "--all-pending", "--project", path, "--json");
        apply.Environment[ProcessRunnerLauncher.SuppressVariable] = "1";
        var (exitCode, output, error) = await CliProcess.RunAsync(apply);

        Assert.Equal(4, exitCode);
        Assert.Empty(output);
        using var failure = JsonDocument.Parse(error);
        Assert.Equal("apply.reconciliation-needed", failure.RootElement.GetProperty("code").GetString());
        Assert.Equal("StoreInconsistent", failure.RootElement.GetProperty("reason").GetString());
        Assert.Contains("Open Motif", failure.RootElement.GetProperty("message").GetString());
        Assert.Equal(projectBytes, File.ReadAllBytes(path));
        Assert.True(ProjectReconciliationMarker.Exists(path));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ApplyAllPendingWithNothingPendingSucceedsWithoutWritingAReceipt(bool asJson)
    {
        var (path, _) = ReleasedProjectWithWord("unchanged-word");
        var root = Path.Combine(Path.GetDirectoryName(path)!, "empty-pending-apply-worker");
        var captured = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(path), root);
        Assert.True(captured.Succeeded, captured.Refusal?.Message);
        var projectBytes = File.ReadAllBytes(path);

        var apply = CliProcess.Start(IsolatedRunner.Options(root), "apply", "--all-pending", "--project", path);
        if (asJson) apply.ArgumentList.Add("--json");
        apply.Environment[ProcessRunnerLauncher.SuppressVariable] = "1";
        var (exitCode, output, error) = await CliProcess.RunAsync(apply);

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        Assert.Equal(projectBytes, File.ReadAllBytes(path));
        if (asJson)
        {
            ApplyAllPendingContract.Match("empty", exitCode, output, error);
            using var response = JsonDocument.Parse(output);
            Assert.True(response.RootElement.GetProperty("ok").GetBoolean());
            Assert.False(response.RootElement.GetProperty("applied").GetBoolean());
            Assert.Equal("Nothing to apply.", response.RootElement.GetProperty("summary").GetString());
            Assert.DoesNotContain("code", response.RootElement.EnumerateObject().Select(property => property.Name));
        }
        else
        {
            Assert.Equal("Nothing to apply.", output.Trim());
        }
        using var database = ProjectMotifDatabase.Open(path);
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Receipts;";
        Assert.Equal(0L, (long)command.ExecuteScalar()!);
        command.CommandText = "SELECT COUNT(*) FROM Proposals;";
        Assert.Equal(0L, (long)command.ExecuteScalar()!);
    }

    [Fact]
    public async Task ApplyAllPendingReturnsBusyExitWhenFieldWorksHoldsTheProject()
    {
        var (path, wordformId) = ReleasedProjectWithWord("busy-apply-word");
        var root = Path.Combine(Path.GetDirectoryName(path)!, "busy-pending-apply-worker");
        var parser = FakeParser.Copy(root);
        var runner = IsolatedRunner.Process(root, parser);
        var captured = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(path), root);
        Assert.True(captured.Succeeded, captured.Refusal?.Message);
        var initial = PendingChanges.Load(new PendingChangesRequest(path, "1.0"));
        var added = PendingChanges.Put(new PutPendingChangeRequest(path, "1.0", initial.Value!.Revision,
            new ChangeIntent(CanonicalId.Mint().Value, "incorrect-spelling",
                CanonicalId.FromGuid(wordformId).Value, "busy-apply-word", OriginPage: "Texts")));
        Assert.True(added.Succeeded, added.Refusal?.Message);
        var measured = await PendingChangesWorkflow.Measure(new MeasurePendingRequest(
            path, added.Value!.DraftId!, added.Value.Revision, ["busy-apply-word"]),
            new Progress<MeasureProgress>(), CancellationToken.None, runnerLauncher: runner);
        Assert.True(measured.Succeeded, measured.Refusal?.Message);

        using var held = new FieldWorksSimulator(path).Hold();
        var apply = CliProcess.Start(runner.Options, "apply", "--all-pending", "--project", path, "--json");
        apply.Environment[ProcessRunnerLauncher.SuppressVariable] = "1";
        var (exitCode, output, error) = await CliProcess.RunAsync(apply);

        Assert.Equal(3, exitCode);
        ApplyAllPendingContract.Match("project-in-use", exitCode, output, error);
        Assert.Empty(output);
        using var failure = JsonDocument.Parse(error);
        Assert.Equal("project.in-use", failure.RootElement.GetProperty("code").GetString());
        Assert.Equal("Busy", failure.RootElement.GetProperty("reason").GetString());
        var unchanged = PendingChanges.Load(new PendingChangesRequest(path, "1.0"));
        Assert.True(unchanged.Succeeded, unchanged.Refusal?.Message);
        Assert.Single(unchanged.Value!.Changes);
        // The lock is refused first: the Draft was never finalized and no Dry Run was queued.
        Assert.False(HasDryRunJob(path));
        Assert.Equal(0L, CountRows(path, "ProposalRevisions"));
    }

    private static long CountRows(string projectPath, string table)
    {
        using var database = ProjectMotifDatabase.Open(projectPath);
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table};";
        return (long)command.ExecuteScalar()!;
    }

    [Fact]
    public async Task ApplyAllPendingReturnsRefusedExitAndCodeForAnUncertainOccurrence()
    {
        string path;
        SeededText text;
        using (var cache = pristine.NewScratch())
        {
            text = SeededProject.SeedText(cache, pristine.Seed);
            var paragraph = cache.ServiceLocator.GetInstance<IStTxtParaRepository>()
                .GetObject(text.FirstParagraphId);
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            {
                var contextWordform = cache.ServiceLocator.GetInstance<IWfiWordformFactory>()
                    .Create(TsStringUtils.MakeString("context-word", cache.DefaultVernWs));
                paragraph.SegmentsOS[0].AnalysesRS.Insert(1, contextWordform);
                paragraph.Contents = TsStringUtils.MakeString(
                    $"{SeededProject.AnalysedWordForm} context-word{SeededProject.PunctuationForm}",
                    cache.DefaultVernWs);
                paragraph.ParseIsCurrent = true;
            });
            new FwDataProjectLoader().Save(cache);
            path = cache.ProjectId.Path;
        }
        var root = Path.Combine(Path.GetDirectoryName(path)!, "uncertain-pending-apply-worker");
        Assert.True(BaselineCaptureCommand.Capture(new BaselineCaptureRequest(path), root).Succeeded);
        var initial = PendingChanges.Load(new PendingChangesRequest(path, "1.0"));
        Assert.True(initial.Succeeded, initial.Refusal?.Message);
        var added = PendingChanges.Put(new PutPendingChangeRequest(path, "1.0", initial.Value!.Revision,
            new ChangeIntent("uncertain-apply-change", AnalysisChangeKinds.Reject,
                CanonicalId.FromGuid(text.AnalysedWordformId).Value, SeededProject.AnalysedWordForm,
                StoredAnalysisId: CanonicalId.FromGuid(text.ApprovedAnalysisId).Value,
                Occurrence: new OccurrenceAnchor(text.TextId, text.FirstParagraphId, text.FirstSegmentId, 0))));
        Assert.True(added.Succeeded, added.Refusal?.Message);

        using (var editCache = new FwDataProjectLoader().LoadScratchCache(path))
        {
            var editParagraph = editCache.ServiceLocator.GetInstance<IStTxtParaRepository>()
                .GetObject(text.FirstParagraphId);
            var segment = editCache.ServiceLocator.GetInstance<ISegmentRepository>().GetObject(text.FirstSegmentId);
            NonUndoableUnitOfWorkHelper.Do(editCache.ActionHandlerAccessor, () =>
            {
                var replacement = editCache.ServiceLocator.GetInstance<IWfiWordformFactory>()
                    .Create(TsStringUtils.MakeString("changed-context-word", editCache.DefaultVernWs));
                segment.AnalysesRS.RemoveAt(1);
                segment.AnalysesRS.Insert(1, replacement);
                editParagraph.Contents = TsStringUtils.MakeString(
                    $"{SeededProject.AnalysedWordForm} changed-context-word{SeededProject.PunctuationForm}",
                    editCache.DefaultVernWs);
                editParagraph.ParseIsCurrent = true;
            });
            new FwDataProjectLoader().Save(editCache);
        }
        File.SetLastWriteTimeUtc(path, File.GetLastWriteTimeUtc(path).AddMinutes(1));
        Assert.True(BaselineCaptureCommand.Capture(new BaselineCaptureRequest(path), root).Succeeded);
        var rechecked = PendingChanges.Recheck(new RecheckPendingChangesRequest(path, "1.0", added.Value!.Revision));
        Assert.True(rechecked.Succeeded, rechecked.Refusal?.Message);
        Assert.Equal("uncertain", Assert.Single(rechecked.Value!.FitSummary).Status);

        var start = CliProcess.Start(IsolatedRunner.Options(root), "apply", "--all-pending", "--project", path, "--json");
        start.Environment[ProcessRunnerLauncher.SuppressVariable] = "1";
        var (exitCode, output, error) = await CliProcess.RunAsync(start);

        Assert.Equal(2, exitCode);
        Assert.Empty(output);
        using var failure = JsonDocument.Parse(error);
        Assert.Equal("apply.change-uncertain", failure.RootElement.GetProperty("code").GetString());
        Assert.Equal("Refused", failure.RootElement.GetProperty("reason").GetString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReleasedPendingApplyReportsReceiptBoundaryOutcome(bool failAfterReceipt)
    {
        var (path, wordformId) = ReleasedProjectWithWord("reconciliation-word");
        var root = Path.Combine(Path.GetDirectoryName(path)!, "reconciliation-pending-worker");
        var parser = FakeParser.Copy(root);
        var runner = IsolatedRunner.Process(root, parser);
        var captured = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(path), root);
        Assert.True(captured.Succeeded, captured.Refusal?.Message);
        var initial = PendingChanges.Load(new PendingChangesRequest(path, "1.0"));
        var added = PendingChanges.Put(new PutPendingChangeRequest(path, "1.0", initial.Value!.Revision,
            new ChangeIntent(CanonicalId.Mint().Value, "incorrect-spelling",
                CanonicalId.FromGuid(wordformId).Value, "reconciliation-word", OriginPage: "Texts")));
        Assert.True(added.Succeeded, added.Refusal?.Message);
        var measured = await PendingChangesWorkflow.Measure(new MeasurePendingRequest(
            path, added.Value!.DraftId!, added.Value.Revision, ["reconciliation-word"]),
            new Progress<MeasureProgress>(), CancellationToken.None, runnerLauncher: runner);
        Assert.True(measured.Succeeded, measured.Refusal?.Message);

        string databasePath;
        using (var database = ProjectMotifDatabase.Open(path))
            databasePath = database.FullPath;
        var apply = CliProcess.Start(runner.Options, "apply", "--all-pending", "--project", path, "--json");
        apply.Environment[ProcessRunnerLauncher.SuppressVariable] = "1";
        apply.Environment[failAfterReceipt ? "MOTIF_TEST_FAIL_AFTER_RECEIPT_FOR" : "MOTIF_TEST_FAIL_RECEIPT_WRITE_FOR"] = path;
        using var process = Process.Start(apply)!;
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        Process? worker = null;
        try
        {
            // Started as the CLI's own kick would be, so a runner still retiring from Measure hands the job over.
            await WaitForDryRunJobAsync(path, process, errorTask);
            worker = StartWorker(runner.Options with { IdleTimeout = TimeSpan.FromMinutes(5) }, path);
            await WaitForPendingProposalAnchorAsync(path, added.Value.DraftId!, process, errorTask);
            try
            {
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(60));
            }
            catch (TimeoutException)
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
                throw;
            }
            var output = await outputTask;
            var error = await errorTask;

            if (failAfterReceipt)
            {
                Assert.True(process.ExitCode == 0,
                    $"Expected successful Apply exit 0, got {process.ExitCode}. stderr: {error} stdout: {output}");
                ApplyAllPendingContract.Match("applied", process.ExitCode, output, error);
                using var success = JsonDocument.Parse(output);
                Assert.True(success.RootElement.GetProperty("ok").GetBoolean());
                Assert.True(success.RootElement.GetProperty("applied").GetBoolean());
                Assert.Empty(error);
            }
            else
            {
                Assert.True(process.ExitCode == 4,
                    $"Expected reconciliation exit 4, got {process.ExitCode}. stderr: {error} stdout: {output}");
                ApplyAllPendingContract.Match("reconciliation-needed", process.ExitCode, output, error);
                Assert.Empty(output);
                using var failure = JsonDocument.Parse(error);
                Assert.Equal("apply.reconciliation-needed", failure.RootElement.GetProperty("code").GetString());
                Assert.Equal("StoreInconsistent", failure.RootElement.GetProperty("reason").GetString());
                Assert.Contains("applied and saved to the project", failure.RootElement.GetProperty("message").GetString());
                Assert.Contains("recording that in the proposal store failed",
                    failure.RootElement.GetProperty("message").GetString());
            }

            using (var cache = new FwDataProjectLoader().LoadScratchCache(path))
                Assert.Contains("reconciliation-word", ApprovedMorphologyReader.ReadIncorrectSpellings(cache));
            using var store = ProjectMotifDatabase.Open(path);
            using var connection = store.OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM Receipts WHERE ProposalId = $proposal;";
            command.Parameters.AddWithValue("$proposal", added.Value.DraftId);
            Assert.Equal(failAfterReceipt ? 1L : 0L, (long)command.ExecuteScalar()!);
            Assert.Equal(!failAfterReceipt, ProjectReconciliationMarker.Exists(path));
            Assert.Empty(PendingChanges.Load(new PendingChangesRequest(path, "1.0")).Value!.Changes);
            if (failAfterReceipt)
                Assert.Equal("applied",
                    new ProposalRepository(store).Get(CanonicalId.Parse(added.Value.DraftId!)).Status);
            if (!failAfterReceipt) Assert.True(ProjectReconciliationMarker.Clear(path));
        }
        finally
        {
            if (worker is not null)
            {
                try
                {
                    if (!worker.HasExited) worker.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException) { }
                catch (Win32Exception) { }
                await worker.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
                worker.Dispose();
            }
        }
    }

    private static Process StartWorker(JobRunnerLaunchOptions options, string wakeProjectPath)
    {
        var start = new ProcessStartInfo(options.WorkerExecutable!) { UseShellExecute = false };
        foreach (var argument in ProcessRunnerLauncher.LaunchArguments(options))
            start.ArgumentList.Add(argument);
        start.ArgumentList.Add(RunnerOptions.WakeProjectArgument);
        start.ArgumentList.Add(wakeProjectPath);
        return Process.Start(start)!;
    }

    // Any state counts: a runner still alive from an earlier job may already have taken it.
    private static async Task WaitForDryRunJobAsync(string projectPath, Process process, Task<string> errorTask)
    {
        while (!HasDryRunJob(projectPath))
        {
            if (process.HasExited)
            {
                if (HasDryRunJob(projectPath)) return;
                throw new InvalidOperationException(
                    $"The CLI exited with code {process.ExitCode} before queueing its Dry Run. stderr: {await errorTask}");
            }
            await Task.Delay(100);
        }
    }

    private static string[] CompletedMeasureJobs(string projectPath)
    {
        using var database = ProjectMotifDatabase.Open(projectPath);
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Status FROM Jobs WHERE Kind IN ('dry-run', 'trial') ORDER BY CreatedUtc;";
        using var reader = command.ExecuteReader();
        var statuses = new List<string>();
        while (reader.Read()) statuses.Add(reader.GetString(0));
        return [.. statuses];
    }

    private static bool HasDryRunJob(string projectPath)
    {
        using var database = ProjectMotifDatabase.Open(projectPath);
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Jobs WHERE Kind = 'dry-run';";
        return (long)command.ExecuteScalar()! > 0;
    }

    private static async Task WaitForPendingProposalAnchorAsync(
        string projectPath, string proposalId, Process process, Task<string> errorTask)
    {
        while (!PendingProposalHasAnchor(projectPath, proposalId))
        {
            if (process.HasExited)
            {
                if (PendingProposalHasAnchor(projectPath, proposalId)) return;
                throw new InvalidOperationException(
                    $"The CLI exited with code {process.ExitCode} before writing the pending Proposal's Dry Run " +
                    $"anchor. stderr: {await errorTask}");
            }
            await Task.Delay(100);
        }
    }

    private static bool PendingProposalHasAnchor(string projectPath, string proposalId)
    {
        using var database = ProjectMotifDatabase.Open(projectPath);
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT AnchorJson FROM Proposals WHERE ProposalId = $proposalId;";
        command.Parameters.AddWithValue("$proposalId", proposalId);
        return command.ExecuteScalar() is string { Length: > 0 };
    }

    // The save-boundary contract has FieldWorks release the project before it calls the verb.
    private (string Path, Guid WordformId) ReleasedProjectWithWord(string word) =>
        ReleasedProjectWithWord(word, out _);

    private (string Path, Guid WordformId) ReleasedProjectWithWord(string word, out Guid senseId)
    {
        using var cache = pristine.NewScratch();
        Guid wordformId = Guid.Empty, sense = Guid.Empty;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            wordformId = cache.ServiceLocator.GetInstance<IWfiWordformFactory>()
                .Create(TsStringUtils.MakeString(word, cache.DefaultVernWs)).Guid;
            var entry = cache.ServiceLocator.GetInstance<ILexEntryFactory>().Create();
            var lexSense = cache.ServiceLocator.GetInstance<ILexSenseFactory>().Create();
            entry.SensesOS.Add(lexSense);
            sense = lexSense.Guid;
        });
        senseId = sense;
        new FwDataProjectLoader().Save(cache);
        return (cache.ProjectId.Path, wordformId);
    }
}
