using System.Diagnostics;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Cli;

public sealed class TrialWordResultsArgvTests
{
    [Fact]
    public async Task WordResultsIsASwitchSoTheProposalIdAfterItIsStillRead()
    {
        var start = new ProcessStartInfo(BuildOutput.Cli)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in new[]
                     { "proposal", "trial", "--project", "missing.fwdata", "--word-results", "proposal/one", "--json" })
            start.ArgumentList.Add(argument);
        start.Environment["MOTIF_DEVELOPER_COMMANDS"] = "1";
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await Task.WhenAll(output, error, process.WaitForExitAsync()).WaitAsync(TimeSpan.FromSeconds(60));

        Assert.DoesNotContain("Usage: motif proposal trial", await error + await output, StringComparison.Ordinal);
    }
}
