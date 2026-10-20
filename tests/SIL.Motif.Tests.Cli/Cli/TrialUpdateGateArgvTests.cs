using System.Diagnostics;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Installation;
using Xunit;

namespace SIL.Motif.Tests.Cli;

/// <summary>Tests that hold the machine's update gate; they run alone so no sibling test sees it held.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class UpdateGateCollection
{
    public const string Name = "Update gate";
}

[Collection(UpdateGateCollection.Name)]
public sealed class TrialUpdateGateArgvTests
{
    [Fact]
    public async Task ATrialRefusedDuringAnUpdateExitsThreeAndKeepsItsCode()
    {
        // The test process and the CLI it starts share the update gate's test namespace through the environment.
        using var update = MotifUpdateGate.TryAcquireForUpdate();
        Assert.NotNull(update);

        var start = new ProcessStartInfo(BuildOutput.Cli)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in new[] { "trial", "--project", "missing.fwdata", "proposal/one", "--json" })
            start.ArgumentList.Add(argument);
        start.Environment["MOTIF_DEVELOPER_COMMANDS"] = "1";
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await Task.WhenAll(output, error, process.WaitForExitAsync()).WaitAsync(TimeSpan.FromSeconds(60));

        Assert.Equal(3, process.ExitCode);
        Assert.Equal(RefusalCodes.TrialUpdateInProgress,
            ProjectionJson.Deserialize<FailureEnvelope>(await error)!.Code);
    }
}
