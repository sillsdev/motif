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

    [Fact]
    public void PruneTextReportsTheBudgetAndWhatWasKeptByReason()
    {
        var result = CommandTextRenderer.Render(CommandOutcome<StorePruneResponse>.Success(
            new StorePruneResponse(0, 300, 200, true, 0, 0, 0, true, "/fixture/runs", OverBudget: true,
                ByteBudget: 100, BytesInUse: 150,
                AssessmentsKept: new Dictionary<string, int> { ["openProposal"] = 3 },
                RunDirectoriesKept: new Dictionary<string, int> { ["referenced"] = 2 },
                SourcesRemoved: 4, BaselinesRemoved: 1, UserDataBytesRemoved: 900, UserDataBytesKept: 50,
                UserDataKept: new Dictionary<string, int> { ["inUse"] = 1 })), asJson: false);

        Assert.Contains("Store budget 100 bytes, 150 bytes in use, over budget.", result.Output);
        Assert.Contains("Kept Assessments by reason: openProposal=3.", result.Output);
        Assert.Contains("Kept run directories by reason: referenced=2.", result.Output);
        Assert.Contains("removed 4 sources and 1 Baselines (900 bytes); 50 bytes kept.", result.Output);
        Assert.Contains("Kept per-user data entries by reason: inUse=1.", result.Output);
    }
}
