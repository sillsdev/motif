using System.Text.Json;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Cli;

public sealed class WorkerStopArgvTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-worker-stop-argv-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task WithNoRunnerInTheRootItSaysSoAndSucceedsInTextAndJson()
    {
        Directory.CreateDirectory(_root);

        var text = await CliProcess.RunAsync(_root, null, false, "worker", "stop");
        var json = await CliProcess.RunAsync(_root, null, false, "worker", "stop", "--json");

        Assert.True(text.ExitCode == 0, text.Error);
        Assert.Contains("No background runner is running for the worker root", text.Output, StringComparison.Ordinal);
        Assert.True(json.ExitCode == 0, json.Error);
        using var document = JsonDocument.Parse(json.Output);
        Assert.Equal(0, document.RootElement.GetProperty("workers").GetArrayLength());
    }

    [Fact]
    public async Task AnUnknownArgumentIsUsage()
    {
        Directory.CreateDirectory(_root);

        var extra = await CliProcess.RunAsync(_root, null, false, "worker", "stop", "now");
        var flag = await CliProcess.RunAsync(_root, null, false, "worker", "stop", "--force");
        var bare = await CliProcess.RunAsync(_root, null, false, "worker");

        foreach (var result in new[] { extra, flag, bare })
            Assert.True(result.ExitCode == FailureEnvelope.ExitCodeFor(FailureReason.InvalidArgument), result.Error);
        Assert.Contains("worker stop [--project <fwdata>]", extra.Error + extra.Output, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
