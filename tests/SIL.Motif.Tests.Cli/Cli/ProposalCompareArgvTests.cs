using System.Text.Json;
using SIL.Motif.Commands;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Host;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Cli;

public sealed class ProposalCompareArgvTests
{
    private static string[] Publish(ReviewAcceptanceFixture fixture, char secondBaseline = 'a')
    {
        string[] ids = [];
        var result = ProjectStoreCommand.Run<string>(fixture.Project, MotifProductVersion.CurrentText, (database, project) =>
        {
            var a = ProposalCompareExports.AddTrial(database, project, [new("f1", [1], [1, 2]), new("f2", [1], [])]);
            var b = ProposalCompareExports.AddTrial(database, project, [new("f1", [1], [1, 3]), new("f2", [1], [1])],
                baseline: secondBaseline);
            ids = [a, b];
            return CommandOutcome<string>.Success("");
        });
        Assert.True(result.Succeeded, result.Refusal?.Message);
        return ids;
    }

    [Fact]
    public async Task CompareSummarisesPagesAndStatesItsBasisInJsonAndText()
    {
        using var fixture = new ReviewAcceptanceFixture();
        var ids = Publish(fixture);
        var runnerRoot = Directory.CreateDirectory(Path.Combine(fixture.Root, "worker")).FullName;
        Task<CliProcessResult> Run(params string[] options) => CliProcess.RunAsync(runnerRoot, null, false,
            ["proposal", "compare", ids[0], ids[1], "--project", fixture.Project, .. options]);

        var summary = await Run("--json");
        Assert.True(summary.ExitCode == 0, summary.Error);
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(summary.Output) <= 2000);
        using var json = JsonDocument.Parse(summary.Output);
        Assert.Equal("trial-before-after", json.RootElement.GetProperty("basis").GetString());
        Assert.Equal(2, json.RootElement.GetProperty("columns").GetArrayLength());
        Assert.False(json.RootElement.TryGetProperty("page", out _));

        var rows = await Run("--json", "--page", "1", "--at", ProposalCompareExports.BaselineDigest('a'));
        Assert.True(rows.ExitCode == 0, rows.Error);
        using var page = JsonDocument.Parse(rows.Output);
        Assert.Equal(2, page.RootElement.GetProperty("page").GetProperty("rows").GetArrayLength());

        var text = await Run();
        Assert.True(text.ExitCode == 0, text.Error);
        Assert.Contains("not measured against approved analyses", text.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CompareRefusesMixedBaselinesWithItsRegisteredCode()
    {
        using var fixture = new ReviewAcceptanceFixture();
        var ids = Publish(fixture, secondBaseline: 'b');
        var runnerRoot = Directory.CreateDirectory(Path.Combine(fixture.Root, "worker")).FullName;
        var result = await CliProcess.RunAsync(runnerRoot, null, false,
            "proposal", "compare", ids[0], ids[1], "--project", fixture.Project, "--json");
        Assert.NotEqual(0, result.ExitCode);
        using var json = JsonDocument.Parse(string.IsNullOrWhiteSpace(result.Output) ? result.Error : result.Output);
        Assert.Equal("proposal.compare-baseline-mismatch", json.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task CompareNeedsAtLeastTwoProposals()
    {
        using var fixture = new ReviewAcceptanceFixture();
        var runnerRoot = Directory.CreateDirectory(Path.Combine(fixture.Root, "worker")).FullName;
        var result = await CliProcess.RunAsync(runnerRoot, null, false,
            "proposal", "compare", fixture.Proposal, "--project", fixture.Project, "--json");
        Assert.NotEqual(0, result.ExitCode);
    }
}
