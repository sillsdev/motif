using SIL.Motif.Cli.Rendering;
using SIL.Motif.Commands;
using SIL.Motif.Contract.Commands;
using Xunit;

namespace SIL.Motif.Tests.Cli;

public sealed class StorePruneRenderingTests
{
    [Fact]
    public void PruneTextReportsDirectoryCountsAndIncompleteReclamation()
    {
        var result = CommandTextRenderer.Render(CommandOutcome<StorePruneResponse>.Success(
            new StorePruneResponse(2, 300, 200, false, 3, 4, 500, false, "/fixture/runs")), asJson: false);

        Assert.Contains("Removed 2 Assessments and 3 invocation directories", result.Output);
        Assert.Contains("kept 4 unowned directories (500 bytes) in /fixture/runs", result.Output);
        Assert.Contains("store: 300 to 200 bytes", result.Output);
        Assert.Contains("The unowned directory size is incomplete", result.Output);
        Assert.Contains("Reclamation is incomplete", result.Output);
    }
}
