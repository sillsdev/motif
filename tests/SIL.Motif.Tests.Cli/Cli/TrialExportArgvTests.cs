using System.Text.Json;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Cli;

public sealed class TrialExportArgvTests
{
    [Fact]
    public async Task TrialExportReadChecksTheRevisionAndPagesTheFrozenWordOrder()
    {
        using var fixture = new ReviewAcceptanceFixture(extraFailures: 105);
        var receipt = fixture.CreateInterruptedTrialExport();
        var runnerRoot = Directory.CreateDirectory(Path.Combine(fixture.Root, "worker")).FullName;
        using var artifact = JsonDocument.Parse(File.ReadAllBytes(receipt.Path));
        var provenance = artifact.RootElement.GetProperty("provenance");
        var revision = provenance.GetProperty("proposalRevision").GetString()!;
        var baseline = provenance.GetProperty("baseline").GetProperty("bundleDigest").GetString()!;
        Task<CliProcessResult> Read(params string[] options) => CliProcess.RunAsync(runnerRoot, null, false,
            ["proposal", "trial", "export", receipt.JobId, "--project", fixture.Project, "--json", .. options]);

        var wrong = await Read("--at", "wrong-revision");
        Assert.NotEqual(0, wrong.ExitCode);
        using var refusal = JsonDocument.Parse(string.IsNullOrWhiteSpace(wrong.Output) ? wrong.Error : wrong.Output);
        Assert.Equal("trial.export-unavailable", refusal.RootElement.GetProperty("code").GetString());

        var first = await Read("--at", revision, "--page", "1");
        var second = await Read("--at", baseline, "--page", "2");
        Assert.True(first.ExitCode == 0, first.Error);
        Assert.True(second.ExitCode == 0, second.Error);
        using var firstPage = JsonDocument.Parse(first.Output);
        using var secondPage = JsonDocument.Parse(second.Output);
        var firstResult = firstPage.RootElement.GetProperty("page");
        var secondResult = secondPage.RootElement.GetProperty("page");
        Assert.Equal(2, firstResult.GetProperty("nextPage").GetInt32());
        Assert.Equal(JsonValueKind.Null, secondResult.GetProperty("nextPage").ValueKind);
        var firstWords = firstResult.GetProperty("words").EnumerateArray();
        var secondWords = secondResult.GetProperty("words").EnumerateArray();
        Assert.Equal(100, firstWords.Count());
        Assert.Equal(artifact.RootElement.GetProperty("words").EnumerateArray().Select(row => row.GetProperty("caseKey").GetString()),
            firstWords.Concat(secondWords).Select(row => row.GetProperty("caseKey").GetString()));
    }

    [Fact]
    public async Task TrialExportDefaultTextNamesBothSidesAndAnIncompleteComparison()
    {
        using var fixture = new ReviewAcceptanceFixture();
        var receipt = fixture.CreateInterruptedTrialExport();
        var runnerRoot = Directory.CreateDirectory(Path.Combine(fixture.Root, "worker")).FullName;
        var result = await CliProcess.RunAsync(runnerRoot, null, false, "proposal", "trial", "export",
            receipt.JobId, "--project", fixture.Project);
        Assert.True(result.ExitCode == 0, result.Error);
        Assert.Contains("Before", result.Output, StringComparison.Ordinal);
        Assert.Contains("After", result.Output, StringComparison.Ordinal);
        Assert.Contains("Comparison incomplete", result.Output, StringComparison.Ordinal);
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(result.Output) <= 2000);
    }

    [Fact]
    public async Task UnknownTrialExportReturnsAStructuredRefusal()
    {
        using var fixture = new ReviewAcceptanceFixture();
        var runnerRoot = Path.Combine(fixture.Root, "worker");
        Directory.CreateDirectory(runnerRoot);
        var result = await CliProcess.RunAsync(runnerRoot, null, false, "proposal", "trial", "export",
            "job/00000000-0000-0000-0000-000000000099", "--project", fixture.Project, "--json");
        Assert.NotEqual(0, result.ExitCode);
        using var json = JsonDocument.Parse(string.IsNullOrWhiteSpace(result.Output) ? result.Error : result.Output);
        Assert.Equal("trial.export-unavailable", json.RootElement.GetProperty("code").GetString());
    }
}
