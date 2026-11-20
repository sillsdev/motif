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
            RecordBaseline(database, project, fixture.Project, 'a');
            return CommandOutcome<string>.Success("");
        });
        Assert.True(result.Succeeded, result.Refusal?.Message);
        return ids;
    }

    // The Trials' Baseline, backed by the fixture's real project so the approved analyses can be read from it.
    private static void RecordBaseline(SIL.Motif.Host.Store.MotifDatabase database, SIL.Motif.Contract.Projects.ProjectLocator project,
        string fwdata, char fill)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Baselines (ProjectKey, ProjectIdentity, SemanticSnapshotDigest, ProjectionVersion, CapturedUtc,
                BundleDigest, RootDirectory, FwDataPath, PublishedUtc, SourceLastWriteUtc)
            VALUES ($project, 'project', $digest, 'test', '2026-01-01T00:00:00Z', $digest, $root, $fwdata,
                '2026-01-01T00:00:00.0000000+00:00', '2026-01-01T00:00:00.0000000+00:00');
            """;
        command.Parameters.AddWithValue("$project", SIL.Motif.Worker.Projects.ProjectWorkspaceKey.Compute(project));
        command.Parameters.AddWithValue("$digest", ProposalCompareExports.BaselineDigest(fill));
        command.Parameters.AddWithValue("$root", Path.GetDirectoryName(fwdata)!);
        command.Parameters.AddWithValue("$fwdata", fwdata);
        command.ExecuteNonQuery();
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
        Assert.Equal(0, json.RootElement.GetProperty("approvedSkippedOtherAgents").GetInt32());
        Assert.Equal(0, json.RootElement.GetProperty("columns")[0].GetProperty("approvedKept").GetInt32());
        Assert.Equal(0, json.RootElement.GetProperty("columns")[0].GetProperty("approvedLost").GetInt32());

        var rows = await Run("--json", "--page", "1", "--snapshot", json.RootElement.GetProperty("snapshot").GetString()!, "--at", ProposalCompareExports.BaselineDigest('a'));
        Assert.True(rows.ExitCode == 0, rows.Error);
        using var page = JsonDocument.Parse(rows.Output);
        Assert.Equal(2, page.RootElement.GetProperty("page").GetProperty("rows").GetArrayLength());

        var stale = await Run("--json", "--page", "1", "--snapshot", "000000000000");
        Assert.NotEqual(0, stale.ExitCode);
        var past = await Run("--json", "--page", "9");
        Assert.NotEqual(0, past.ExitCode);

        var text = await Run();
        Assert.True(text.ExitCode == 0, text.Error);
        Assert.Contains("approved analyses each Proposal keeps and loses", text.Output, StringComparison.Ordinal);
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
