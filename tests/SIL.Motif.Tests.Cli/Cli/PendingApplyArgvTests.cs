using System.Diagnostics;
using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Commands.Requests;
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

[Collection(LcmCacheTestCollection.Name)]
public sealed class PendingApplyArgvTests(PristineProjectFixture pristine)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ApplyAllPendingWritesOneReceiptWithOptionalRevision(bool includeRevision)
    {
        var (path, wordformId) = ReleasedProjectWithWord("review-word");
        var root = Path.Combine(Path.GetDirectoryName(path)!, "pending-apply-worker");
        var runner = IsolatedRunner.Process(root);
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

        var apply = CliStart(runner.Options, "apply", "--all-pending", "--project", path);
        if (includeRevision)
        {
            apply.ArgumentList.Add("--revision");
            apply.ArgumentList.Add(added.Value.Revision);
        }
        apply.ArgumentList.Add("--json");
        var (exitCode, output, error) = await RunAsync(apply);

        Assert.True(exitCode == 0, $"CLI failed with {exitCode}: {error}{output}");
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

        var apply = CliStart(IsolatedRunner.Options(root), "apply", "--all-pending", "--project", path);
        if (asJson) apply.ArgumentList.Add("--json");
        apply.Environment[ProcessRunnerLauncher.SuppressVariable] = "1";
        var (exitCode, output, error) = await RunAsync(apply);

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        if (asJson)
        {
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

        var start = CliStart(IsolatedRunner.Options(root), "apply", "--all-pending", "--project", path, "--json");
        start.Environment[ProcessRunnerLauncher.SuppressVariable] = "1";
        var (exitCode, output, error) = await RunAsync(start);

        Assert.Equal(2, exitCode);
        Assert.Empty(output);
        using var failure = JsonDocument.Parse(error);
        Assert.Equal("apply.change-uncertain", failure.RootElement.GetProperty("code").GetString());
        Assert.Equal("Refused", failure.RootElement.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task ReleasedPendingApplyReportsReconciliationExitFourWithDeveloperCommandsUnset()
    {
        var (path, wordformId) = ReleasedProjectWithWord("reconciliation-word");
        var root = Path.Combine(Path.GetDirectoryName(path)!, "reconciliation-pending-worker");
        var runner = IsolatedRunner.Process(root);
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
        var apply = CliStart(runner.Options, "apply", "--all-pending", "--project", path, "--json");
        apply.Environment[ProcessRunnerLauncher.SuppressVariable] = "1";
        apply.Environment["MOTIF_TEST_FAIL_RECEIPT_WRITE_FOR"] = path;
        using var process = Process.Start(apply)!;
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        using var worker = await StartQueuedWorkerAsync(path, runner.Options, process);
        try
        {
            await WaitForPendingProposalAnchorAsync(path);
            await process.WaitForExitAsync();
            var output = await outputTask;
            var error = await errorTask;

            Assert.True(process.ExitCode == 4,
                $"Expected reconciliation exit 4, got {process.ExitCode}. stderr: {error} stdout: {output}");
            Assert.Empty(output);
            using var failure = JsonDocument.Parse(error);
            Assert.Equal("apply.reconciliation-needed", failure.RootElement.GetProperty("code").GetString());
            Assert.Equal("StoreInconsistent", failure.RootElement.GetProperty("reason").GetString());

            using (var cache = new FwDataProjectLoader().LoadScratchCache(path))
                Assert.Contains("reconciliation-word", ApprovedMorphologyReader.ReadIncorrectSpellings(cache));
            using var store = ProjectMotifDatabase.Open(path);
            using var connection = store.OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM Receipts WHERE ProposalId = $proposal;";
            command.Parameters.AddWithValue("$proposal", added.Value.DraftId);
            Assert.Equal(0L, (long)command.ExecuteScalar()!);
        }
        finally
        {
            if (worker is not null) await worker.WaitForExitAsync();
        }
    }

    // Waits as long as the CLI runs, never a fixed time; null when it finished without queueing a Dry Run.
    private static async Task<Process?> StartQueuedWorkerAsync(string projectPath, JobRunnerLaunchOptions options,
        Process cli)
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
        return null;
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

    // The environment is the CLI's own configuration surface, so the child gets the runner settings there.
    private static ProcessStartInfo CliStart(JobRunnerLaunchOptions runner, params string[] arguments)
    {
        var start = new ProcessStartInfo(BuildOutput.Cli)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        start.Environment[RunnerOptions.RootVariable] = runner.Root;
        start.Environment[ProcessRunnerLauncher.ExecutableVariable] = runner.WorkerExecutable;
        start.Environment[PanGlossExecutable.PathVariable] = runner.ParserPath;
        start.Environment[RunnerOptions.NamespaceVariable] = runner.OwnerNamespace;
        start.Environment[RunnerOptions.IdleVariable] = "1";
        start.Environment.Remove(CommandSurfacePolicy.DeveloperCommandsEnvironmentVariable);
        return start;
    }

    private static async Task<(int ExitCode, string Output, string Error)> RunAsync(ProcessStartInfo start)
    {
        using var process = Process.Start(start)!;
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, await outputTask, await errorTask);
    }

    // The save-boundary contract has FieldWorks release the project before it calls the verb.
    private (string Path, Guid WordformId) ReleasedProjectWithWord(string word)
    {
        using var cache = pristine.NewScratch();
        Guid wordformId = Guid.Empty;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            wordformId = cache.ServiceLocator.GetInstance<IWfiWordformFactory>()
                .Create(TsStringUtils.MakeString(word, cache.DefaultVernWs)).Guid);
        new FwDataProjectLoader().Save(cache);
        return (cache.ProjectId.Path, wordformId);
    }
}
