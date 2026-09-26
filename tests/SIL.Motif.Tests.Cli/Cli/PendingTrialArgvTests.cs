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
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker;
using Xunit;

namespace SIL.Motif.Tests.Cli;

[Collection(LcmCacheTestCollection.Name)]
public sealed class PendingTrialArgvTests(PristineProjectFixture pristine)
{
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

        var result = await RunAsync(root, "trial", "--pending", "--project", path,
            "--words", "pending-trial-word", "--wait", "--wait-timeout-ms", "30000", "--json");

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

    private static async Task<CliRun> RunAsync(string? root, params string[] arguments)
    {
        var start = new ProcessStartInfo(BuildOutput.Cli) { UseShellExecute = false };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        start.Environment["MOTIF_DEVELOPER_COMMANDS"] = "1";
        if (root is not null)
        {
            start.Environment[RunnerOptions.RootVariable] = root;
            start.Environment["MOTIF_WORKER_EXE"] = BuildOutput.Worker;
            start.Environment["MOTIF_PANGLOSS_EXE"] = FakeParser.ExecutablePath;
            start.Environment[RunnerOptions.NamespaceVariable] = Guid.NewGuid().ToString("N");
            start.Environment[RunnerOptions.IdleVariable] = "1";
            start.Environment.Remove(ProcessRunnerLauncher.SuppressVariable);
        }
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        using var process = Process.Start(start)!;
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(60));
        return new CliRun(process.ExitCode, await outputTask, await errorTask);
    }

    private static FailureEnvelope Envelope(string text) =>
        ProjectionJson.Deserialize<FailureEnvelope>(text)!;

    private sealed record CliRun(int ExitCode, string Output, string Error);
}
