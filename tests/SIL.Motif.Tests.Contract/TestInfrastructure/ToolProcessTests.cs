using System.Diagnostics;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.TestInfrastructure;

public sealed class ToolProcessTests
{
    // git's name for the empty blob: what `hash-object --stdin` prints for a standard input with nothing in it.
    private const string EmptyBlob = "e69de29bb2d1d6434b8b29ae775ad8c2e48c5391";

    [Fact]
    public void AToolReadsAnEmptyStandardInputOfItsOwnRatherThanTheTestProcesss()
    {
        var start = new ProcessStartInfo("git");
        start.ArgumentList.Add("hash-object");
        start.ArgumentList.Add("--stdin");

        var result = ToolProcess.Run(start);

        Assert.True(result.ExitCode == 0, $"git hash-object exited {result.ExitCode}: {result.Error}");
        Assert.Equal(EmptyBlob, result.Output.Trim());
    }
}
