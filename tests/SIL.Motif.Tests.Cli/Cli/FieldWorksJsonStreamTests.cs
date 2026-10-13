using System.Text.Json;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using SIL.LCModel;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Jobs;
using Xunit;

namespace SIL.Motif.Tests.Cli;

/// <summary>
/// Pins, with the built <c>motif</c> executable as a child process the way FieldWorks runs it, which stream carries
/// the JSON of <c>apply --all-pending --json</c>: success on standard output, a refusal as a FailureEnvelope on
/// standard error with standard output empty.
/// </summary>
[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group3)]
[Trait("MotifTestLevel", "System")]
public sealed class FieldWorksJsonStreamTests(PristineProjectFixture pristine)
{
    [Fact]
    public async Task SuccessJsonIsOnStandardOutputAndStandardErrorIsEmpty()
    {
        var (path, root) = ProjectWithBaseline("stream-success");

        var (exitCode, output, error) = await ApplyAllPendingJson(path, root);

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        using var response = JsonDocument.Parse(output);
        Assert.True(response.RootElement.GetProperty("ok").GetBoolean());
        Assert.False(response.RootElement.GetProperty("applied").GetBoolean());
    }

    [Fact]
    public async Task RefusalIsAFailureEnvelopeOnStandardErrorAndStandardOutputIsEmpty()
    {
        var (path, root) = ProjectWithBaseline("stream-refusal");
        ProjectReconciliationMarker.Mark(path);

        var (exitCode, output, error) = await ApplyAllPendingJson(path, root);

        Assert.Equal(4, exitCode);
        Assert.Empty(output);
        using var failure = JsonDocument.Parse(error);
        Assert.False(failure.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("apply.reconciliation-needed", failure.RootElement.GetProperty("code").GetString());
        Assert.Equal("StoreInconsistent", failure.RootElement.GetProperty("reason").GetString());
        Assert.False(string.IsNullOrWhiteSpace(failure.RootElement.GetProperty("message").GetString()));
    }

    private static Task<(int ExitCode, string Output, string Error)> ApplyAllPendingJson(string path, string root)
    {
        var apply = CliProcess.Start(IsolatedRunner.Options(root), "apply", "--all-pending", "--project", path, "--json");
        apply.Environment[ProcessRunnerLauncher.SuppressVariable] = "1";
        return CliProcess.RunAsync(apply);
    }

    private (string Path, string Root) ProjectWithBaseline(string name)
    {
        using var cache = pristine.NewScratch();
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            cache.ServiceLocator.GetInstance<IWfiWordformFactory>()
                .Create(TsStringUtils.MakeString(name, cache.DefaultVernWs)));
        new FwDataProjectLoader().Save(cache);
        var path = cache.ProjectId.Path;
        var root = Path.Combine(Path.GetDirectoryName(path)!, name + "-worker");
        var captured = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(path), root);
        Assert.True(captured.Succeeded, captured.Refusal?.Message);
        return (path, root);
    }
}
