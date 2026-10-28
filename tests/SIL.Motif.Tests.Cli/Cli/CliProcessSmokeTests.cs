using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Cli;

/// <summary>
/// One real <c>motif</c> process per verb family whose argv suites run in-process through <see cref="CliInProcess"/>:
/// the apphost starts, parses its arguments, reaches the verb and exits with the code and streams the suites assert.
/// </summary>
public sealed class CliProcessSmokeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-cli-smoke-" + Guid.NewGuid().ToString("N"));

    public CliProcessSmokeTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task HelpListsCommandsFromARealProcess()
    {
        var result = await CliProcess.RunAsync(_root, null, false, "help");

        Assert.True(result.ExitCode == 0, result.FailureDetails);
        Assert.Contains("Open a project", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProjectTagRefusesAMissingProjectFromARealProcess()
    {
        var result = await CliProcess.RunAsync(_root, null, true,
            "project", "tag", "--project", Path.Combine(_root, "missing.fwdata"), "--json");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.NotEmpty(result.Error);
    }

    [Fact]
    public async Task TraceRefusesAMissingSavedTraceFromARealProcess()
    {
        var result = await CliProcess.RunAsync(_root, null, false,
            "trace", "--load", Path.Combine(_root, "missing.trace.json"));

        Assert.NotEqual(0, result.ExitCode);
        Assert.NotEmpty(result.Error);
    }

    [Fact]
    public async Task ArtifactInventoryRefusesMissingArgumentsFromARealProcess()
    {
        var result = await CliProcess.RunAsync(_root, null, true, "store", "artifacts", "--json");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("Usage: motif store artifacts", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task JobsListAllAnswersFromARealProcess()
    {
        var result = await CliProcess.RunAsync(_root, null, true, "jobs", "list", "--all", "--json");

        Assert.True(result.ExitCode == 0, result.FailureDetails);
        Assert.Contains("jobs", result.Output, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }
}
