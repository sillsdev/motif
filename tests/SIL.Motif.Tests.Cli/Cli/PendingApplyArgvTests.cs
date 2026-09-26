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
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.Parser;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker;
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
            Assert.Equal("{\"ok\":true,\"applied\":false}", JsonSerializer.Serialize(response.RootElement));
            Assert.True(response.RootElement.GetProperty("ok").GetBoolean());
            Assert.False(response.RootElement.GetProperty("applied").GetBoolean());
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
        start.Environment[CommandSurfacePolicy.DeveloperCommandsEnvironmentVariable] = "1";
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
