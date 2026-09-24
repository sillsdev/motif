using System.Diagnostics;
using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(LcmCacheTestCollection.Name)]
public sealed class PendingChangesCliAppTests(PristineProjectFixture pristine)
{
    [Fact]
    public async Task CliDraftAppearsInAppAfterOpeningAndAfterRestart()
    {
        string path;
        using (var scratch = pristine.NewScratch())
        {
            path = scratch.ProjectId.Path;
            NonUndoableUnitOfWorkHelper.Do(scratch.ActionHandlerAccessor, () =>
                scratch.ServiceLocator.GetInstance<IWfiWordformFactory>()
                    .Create(TsStringUtils.MakeString("cli-app-word", scratch.DefaultVernWs)));
            new FwDataProjectLoader().Save(scratch);
        }
        Assert.True(BaselineCaptureCommand.Capture(new BaselineCaptureRequest(path),
            Path.Combine(Path.GetDirectoryName(path)!, "cli-app-managed")).Succeeded);

        var id = CanonicalId.Mint().Value;
        var start = new ProcessStartInfo(BuildOutput.Cli)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in new[] { "put-pending-change", "--project", path,
            "--expected-revision", "none", "--change-id", id, "--kind", "incorrect-spelling",
            "--word", "cli-app-word", "--json" }) start.ArgumentList.Add(argument);
        start.Environment["MOTIF_DEVELOPER_COMMANDS"] = "1";
        using (var process = Process.Start(start)!)
        {
            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
            Assert.True(process.WaitForExit(60000), error);
            Assert.True(process.ExitCode == 0, error);
            Assert.Equal(id, Assert.Single(JsonSerializer.Deserialize<PendingChangesSnapshot>(output,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!.Changes).ChangeId);
        }

        var first = new ChangesViewModel(new CommandClient());
        await first.SetProjectAsync(path);
        Assert.Equal(id, Assert.Single(first.Items).ChangeId);

        var reopened = new ChangesViewModel(new CommandClient());
        await reopened.SetProjectAsync(path);
        Assert.Equal(id, Assert.Single(reopened.Items).ChangeId);
    }
}
