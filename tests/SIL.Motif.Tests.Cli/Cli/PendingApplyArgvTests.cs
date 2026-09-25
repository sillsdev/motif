using System.Diagnostics;
using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using SIL.Motif.App.Services;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.LcmUtils;
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
        using var scratch = pristine.NewScratch();
        var path = scratch.ProjectId.Path;
        Guid wordformId = Guid.Empty;
        NonUndoableUnitOfWorkHelper.Do(scratch.ActionHandlerAccessor, () =>
            wordformId = scratch.ServiceLocator.GetInstance<IWfiWordformFactory>()
                .Create(TsStringUtils.MakeString("review-word", scratch.DefaultVernWs)).Guid);
        new FwDataProjectLoader().Save(scratch);

        var root = Path.Combine(Path.GetDirectoryName(path)!, "pending-apply-worker");
        var environment = new Dictionary<string, string?>
        {
            [RunnerOptions.RootVariable] = Environment.GetEnvironmentVariable(RunnerOptions.RootVariable),
            ["MOTIF_WORKER_EXE"] = Environment.GetEnvironmentVariable("MOTIF_WORKER_EXE"),
            ["MOTIF_PANGLOSS_EXE"] = Environment.GetEnvironmentVariable("MOTIF_PANGLOSS_EXE"),
            [RunnerOptions.NamespaceVariable] = Environment.GetEnvironmentVariable(RunnerOptions.NamespaceVariable),
            [RunnerOptions.IdleVariable] = Environment.GetEnvironmentVariable(RunnerOptions.IdleVariable),
        };
        try
        {
            Environment.SetEnvironmentVariable(RunnerOptions.RootVariable, root);
            Environment.SetEnvironmentVariable("MOTIF_WORKER_EXE", BuildOutput.Worker);
            Environment.SetEnvironmentVariable("MOTIF_PANGLOSS_EXE", FakeParser.ExecutablePath);
            Environment.SetEnvironmentVariable(RunnerOptions.NamespaceVariable, Guid.NewGuid().ToString("N"));
            Environment.SetEnvironmentVariable(RunnerOptions.IdleVariable, "1");
            var client = new CommandClient(root);
            var captured = await client.CaptureBaselineAsync(new BaselineCaptureRequest(path), CancellationToken.None);
            Assert.True(captured.Succeeded, captured.Refusal?.Message);
            var initial = PendingChanges.Load(new PendingChangesRequest(path, "1.0"));
            var added = PendingChanges.Put(new PutPendingChangeRequest(path, "1.0", initial.Value!.Revision,
                new ChangeIntent(CanonicalId.Mint().Value, "incorrect-spelling",
                    CanonicalId.FromGuid(wordformId).Value, "review-word", OriginPage: "Texts")));
            Assert.True(added.Succeeded, added.Refusal?.Message);
            var measured = await PendingChangesWorkflow.Measure(new MeasurePendingRequest(
                path, added.Value!.DraftId!, added.Value.Revision, ["review-word"]),
                new Progress<MeasureProgress>(), CancellationToken.None);
            Assert.True(measured.Succeeded, measured.Refusal?.Message);
            Assert.True(measured.Value!.EvidenceComplete);

            var apply = new ProcessStartInfo(BuildOutput.Cli) { UseShellExecute = false };
            apply.ArgumentList.Add("apply");
            apply.ArgumentList.Add("--all-pending");
            apply.ArgumentList.Add("--project");
            apply.ArgumentList.Add(path);
            if (includeRevision)
            {
                apply.ArgumentList.Add("--revision");
                apply.ArgumentList.Add(added.Value.Revision);
            }
            apply.ArgumentList.Add("--json");
            apply.RedirectStandardOutput = true;
            apply.RedirectStandardError = true;
            using var process = Process.Start(apply)!;
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            var output = await outputTask;
            var error = await errorTask;
            Assert.True(process.ExitCode == 0, $"CLI failed with {process.ExitCode}: {error}{output}");
            using var response = JsonDocument.Parse(output);
            Assert.Equal(added.Value.DraftId, response.RootElement.GetProperty("proposalId").GetString());

            using var database = ProjectMotifDatabase.Open(path);
            using var connection = database.OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM Receipts WHERE ProposalId = $proposal;";
            command.Parameters.AddWithValue("$proposal", added.Value.DraftId);
            Assert.Equal(1L, (long)command.ExecuteScalar()!);
        }
        finally
        {
            foreach (var (key, value) in environment)
                Environment.SetEnvironmentVariable(key, value);
        }
    }

    [Fact]
    public async Task ApplyAllPendingWithNothingPendingSucceedsWithoutWritingAReceipt()
    {
        using var scratch = pristine.NewScratch();
        var path = scratch.ProjectId.Path;
        var root = Path.Combine(Path.GetDirectoryName(path)!, "empty-pending-apply-worker");
        var environment = new Dictionary<string, string?>
        {
            [RunnerOptions.RootVariable] = Environment.GetEnvironmentVariable(RunnerOptions.RootVariable),
            ["MOTIF_WORKER_EXE"] = Environment.GetEnvironmentVariable("MOTIF_WORKER_EXE"),
            ["MOTIF_PANGLOSS_EXE"] = Environment.GetEnvironmentVariable("MOTIF_PANGLOSS_EXE"),
            [RunnerOptions.NamespaceVariable] = Environment.GetEnvironmentVariable(RunnerOptions.NamespaceVariable),
            [RunnerOptions.IdleVariable] = Environment.GetEnvironmentVariable(RunnerOptions.IdleVariable),
        };
        try
        {
            Environment.SetEnvironmentVariable(RunnerOptions.RootVariable, root);
            Environment.SetEnvironmentVariable("MOTIF_WORKER_EXE", BuildOutput.Worker);
            Environment.SetEnvironmentVariable("MOTIF_PANGLOSS_EXE", FakeParser.ExecutablePath);
            Environment.SetEnvironmentVariable(RunnerOptions.NamespaceVariable, Guid.NewGuid().ToString("N"));
            Environment.SetEnvironmentVariable(RunnerOptions.IdleVariable, "1");
            var client = new CommandClient(root);
            var captured = await client.CaptureBaselineAsync(new BaselineCaptureRequest(path), CancellationToken.None);
            Assert.True(captured.Succeeded, captured.Refusal?.Message);

            var apply = new ProcessStartInfo(BuildOutput.Cli) { UseShellExecute = false };
            foreach (var argument in new[] { "apply", "--all-pending", "--project", path, "--json" })
                apply.ArgumentList.Add(argument);
            apply.Environment[RunnerKick.SuppressVariable] = "1";
            apply.RedirectStandardOutput = true;
            apply.RedirectStandardError = true;
            using var process = Process.Start(apply)!;
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            var output = await outputTask;
            var error = await errorTask;

            Assert.Equal(0, process.ExitCode);
            Assert.Empty(error);
            using var response = JsonDocument.Parse(output);
            Assert.False(response.RootElement.GetProperty("ok").GetBoolean());
            Assert.Equal("apply.nothing-pending", response.RootElement.GetProperty("code").GetString());
            Assert.Equal("NoChanges", response.RootElement.GetProperty("reason").GetString());
            using var database = ProjectMotifDatabase.Open(path);
            using var connection = database.OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM Receipts;";
            Assert.Equal(0L, (long)command.ExecuteScalar()!);
        }
        finally
        {
            foreach (var (key, value) in environment)
                Environment.SetEnvironmentVariable(key, value);
        }
    }
}
