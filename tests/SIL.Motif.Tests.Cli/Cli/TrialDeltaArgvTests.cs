using System.Text.Json;
using SIL.Motif.Commands;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.HandAnalysis;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Host;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Cli;

public sealed class TrialDeltaArgvTests
{
    private static string Id(int value) => CanonicalId.FromGuid(new Guid(value, 0, 0, [1, 2, 3, 4, 5, 6, 7, 8])).Value;

    private static ReadingSignature Sig(int number) => new([new ReadingMorpheme(Id(number), Id(900001), [],
        ReadingSenseStatus.Resolved, Id(number + 100), Id(number + 200), null, true, null, [])]);

    [Fact]
    public async Task DeltaPrintsCountsAndWritesTheManifestsFromTheRetainedTrial()
    {
        using var fixture = new ReviewAcceptanceFixture();
        var proposal = "";
        var published = ProjectStoreCommand.Run<string>(fixture.Project, MotifProductVersion.CurrentText, (database, project) =>
        {
            proposal = ProposalCompareExports.AddTrial(database, project,
            [
                new("lost", [], []) { BeforeSignatures = [Sig(1)], AfterSignatures = [] },
                new("swap", [], []) { BeforeSignatures = [Sig(2)], AfterSignatures = [Sig(3)] },
            ]);
            return CommandOutcome<string>.Success("");
        });
        Assert.True(published.Succeeded, published.Refusal?.Message);
        var runnerRoot = Directory.CreateDirectory(Path.Combine(fixture.Root, "worker")).FullName;
        var directory = Path.Combine(fixture.Root, "manifests");

        var result = await CliProcess.RunAsync(runnerRoot, null, false, "proposal", "trial", "delta", "--project", fixture.Project,
            "--proposal", proposal, "--write-manifests", directory, "--json");

        Assert.True(result.ExitCode == 0, result.Error);
        using var json = JsonDocument.Parse(result.Output);
        Assert.Equal(1, json.RootElement.GetProperty("counts").GetProperty("lost").GetInt32());
        Assert.Equal(1, json.RootElement.GetProperty("counts").GetProperty("changed").GetInt32());
        Assert.Equal(2, File.ReadAllLines(Path.Combine(directory, "expect-retired.jsonl")).Length);
        Assert.Single(File.ReadAllLines(Path.Combine(directory, "expect-added.jsonl")));
    }

    [Fact]
    public async Task GroupByEntryPrintsOneGroupPerEntryAndRuleIsRefusedWithItsCode()
    {
        using var fixture = new ReviewAcceptanceFixture();
        var proposal = "";
        var published = ProjectStoreCommand.Run<string>(fixture.Project, MotifProductVersion.CurrentText, (database, project) =>
        {
            proposal = ProposalCompareExports.AddTrial(database, project,
            [
                new("a", [], []) { BeforeSignatures = [Sig(1)], AfterSignatures = [] },
                new("b", [], []) { BeforeSignatures = [Sig(1)], AfterSignatures = [] },
                new("swap", [], []) { BeforeSignatures = [Sig(2)], AfterSignatures = [Sig(3)] },
            ]);
            return CommandOutcome<string>.Success("");
        });
        Assert.True(published.Succeeded, published.Refusal?.Message);
        var runnerRoot = Directory.CreateDirectory(Path.Combine(fixture.Root, "worker")).FullName;

        var grouped = await CliProcess.RunAsync(runnerRoot, null, false, "proposal", "trial", "delta", "--project", fixture.Project,
            "--proposal", proposal, "--group-by", "entry", "--classes", "lost", "--json");
        var text = await CliProcess.RunAsync(runnerRoot, null, false, "proposal", "trial", "delta", "--project", fixture.Project,
            "--proposal", proposal, "--group-by", "entry");
        var rule = await CliProcess.RunAsync(runnerRoot, null, false, "proposal", "trial", "delta", "--project", fixture.Project,
            "--proposal", proposal, "--group-by", "rule", "--json");

        Assert.True(grouped.ExitCode == 0, grouped.Error);
        using var json = JsonDocument.Parse(grouped.Output);
        var rows = json.RootElement.GetProperty("groups").GetProperty("rows");
        Assert.Equal(1, rows.GetArrayLength());
        Assert.Equal(Id(1), rows[0].GetProperty("key").GetString());
        Assert.Equal(2, rows[0].GetProperty("retired").GetInt32());
        Assert.Equal(2, rows[0].GetProperty("words").GetInt32());
        Assert.Contains("By entry: 3 groups", text.Output);
        Assert.NotEqual(0, rule.ExitCode);
        Assert.Contains("trial.delta-group-unsupported", rule.Output + rule.Error);
    }

    [Fact]
    public async Task ExpectationsExitZeroWhenGreenAndSixWhenAManifestOrACapFails()
    {
        using var fixture = new ReviewAcceptanceFixture();
        var proposal = "";
        var published = ProjectStoreCommand.Run<string>(fixture.Project, MotifProductVersion.CurrentText, (database, project) =>
        {
            proposal = ProposalCompareExports.AddTrial(database, project,
            [
                new("lost", [], []) { BeforeSignatures = [Sig(1)], AfterSignatures = [] },
                new("doubled", [], []) { BeforeSignatures = [Sig(2)], AfterSignatures = [Sig(2), Sig(3)] },
            ]);
            ProposalCompareExports.RecordCurrentBaseline(database, project);
            return CommandOutcome<string>.Success("");
        });
        Assert.True(published.Succeeded, published.Refusal?.Message);
        var runnerRoot = Directory.CreateDirectory(Path.Combine(fixture.Root, "worker")).FullName;
        var directory = Path.Combine(fixture.Root, "manifests");
        var written = await CliProcess.RunAsync(runnerRoot, null, false, "proposal", "trial", "delta", "--project", fixture.Project,
            "--proposal", proposal, "--write-manifests", directory, "--json");
        Assert.True(written.ExitCode == 0, written.Error);
        var retired = Path.Combine(directory, "expect-retired.jsonl");
        var added = Path.Combine(directory, "expect-added.jsonl");

        var green = await CliProcess.RunAsync(runnerRoot, null, false, "proposal", "trial", "delta", "--project", fixture.Project,
            "--proposal", proposal, "--expect-retired", retired, "--expect-added", added, "--max-doubled", "1", "--json");
        var capped = await CliProcess.RunAsync(runnerRoot, null, false, "proposal", "trial", "delta", "--project", fixture.Project,
            "--proposal", proposal, "--expect-retired", retired, "--expect-added", added, "--max-doubled", "0", "--json");
        File.WriteAllText(retired, "");
        var mismatch = await CliProcess.RunAsync(runnerRoot, null, false, "proposal", "trial", "delta", "--project", fixture.Project,
            "--proposal", proposal, "--expect-retired", retired, "--json");

        Assert.True(green.ExitCode == 0, green.Error);
        using (var json = JsonDocument.Parse(green.Output))
            Assert.Equal("green", json.RootElement.GetProperty("expectations").GetProperty("state").GetString());
        Assert.Equal(6, capped.ExitCode);
        using (var json = JsonDocument.Parse(capped.Output))
            Assert.Equal("doubled-over-cap", json.RootElement.GetProperty("expectations").GetProperty("state").GetString());
        Assert.Equal(6, mismatch.ExitCode);
        using (var json = JsonDocument.Parse(mismatch.Output))
            Assert.Equal("mismatch", json.RootElement.GetProperty("expectations").GetProperty("state").GetString());
    }

    [Fact]
    public async Task ATrialOnAMovedBaselineExitsSixUnmeasuredAndPickStillProjectsTheResult()
    {
        using var fixture = new ReviewAcceptanceFixture();
        var proposal = "";
        var published = ProjectStoreCommand.Run<string>(fixture.Project, MotifProductVersion.CurrentText, (database, project) =>
        {
            proposal = ProposalCompareExports.AddTrial(database, project,
                [new("same", [], []) { BeforeSignatures = [Sig(1)], AfterSignatures = [Sig(1)] }]);
            ProposalCompareExports.RecordCurrentBaseline(database, project, 'b');
            return CommandOutcome<string>.Success("");
        });
        Assert.True(published.Succeeded, published.Refusal?.Message);
        var runnerRoot = Directory.CreateDirectory(Path.Combine(fixture.Root, "worker")).FullName;
        var empty = Path.Combine(fixture.Root, "empty.jsonl");
        File.WriteAllText(empty, "");

        var result = await CliProcess.RunAsync(runnerRoot, null, false, "proposal", "trial", "delta", "--project", fixture.Project,
            "--proposal", proposal, "--expect-retired", empty, "--pick", "expectations.state,expectations.reason", "--json");

        Assert.Equal(6, result.ExitCode);
        using var json = JsonDocument.Parse(result.Output);
        Assert.Equal("unmeasured", json.RootElement.GetProperty("expectations.state").GetString());
        Assert.Contains("Baseline", json.RootElement.GetProperty("expectations.reason").GetString());
        Assert.False(json.RootElement.TryGetProperty("counts", out _));
    }

    [Fact]
    public async Task ExpectationsCannotBeCombinedWithWriteManifests()
    {
        using var fixture = new ReviewAcceptanceFixture();
        var runnerRoot = Directory.CreateDirectory(Path.Combine(fixture.Root, "worker")).FullName;

        var result = await CliProcess.RunAsync(runnerRoot, null, false, "proposal", "trial", "delta", "--project", fixture.Project,
            "--job", "job/x", "--max-doubled", "1", "--write-manifests", Path.Combine(fixture.Root, "out"));

        Assert.NotEqual(0, result.ExitCode);
        Assert.False(Directory.Exists(Path.Combine(fixture.Root, "out")));
    }

    [Fact]
    public async Task AnExpectationManifestThatDoesNotExistIsRefused()
    {
        using var fixture = new ReviewAcceptanceFixture();
        var runnerRoot = Directory.CreateDirectory(Path.Combine(fixture.Root, "worker")).FullName;

        var result = await CliProcess.RunAsync(runnerRoot, null, false, "proposal", "trial", "delta", "--project", fixture.Project,
            "--job", "job/x", "--expect-added", Path.Combine(fixture.Root, "missing.jsonl"));

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("--expect-added", result.Output + result.Error);
    }

    [Fact]
    public async Task DeltaWithoutAProjectIsAUsageError()
    {
        using var fixture = new ReviewAcceptanceFixture();
        var runnerRoot = Directory.CreateDirectory(Path.Combine(fixture.Root, "worker")).FullName;

        var result = await CliProcess.RunAsync(runnerRoot, null, false, "proposal", "trial", "delta", "--job", "job/x");

        Assert.NotEqual(0, result.ExitCode);
    }

    [Fact]
    public async Task WriteManifestsWithoutADirectoryIsAUsageErrorNotASilentRead()
    {
        using var fixture = new ReviewAcceptanceFixture();
        var runnerRoot = Directory.CreateDirectory(Path.Combine(fixture.Root, "worker")).FullName;

        var result = await CliProcess.RunAsync(runnerRoot, null, false, "proposal", "trial", "delta", "--project", fixture.Project,
            "--job", "job/x", "--write-manifests");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("--write-manifests", result.Output + result.Error);
    }

    [Fact]
    public async Task WriteReceiptWritesTheFileOnlyForAGreenTrialAndAllPendingTakesNoReceipt()
    {
        using var fixture = new ReviewAcceptanceFixture();
        var proposal = "";
        var published = ProjectStoreCommand.Run<string>(fixture.Project, MotifProductVersion.CurrentText, (database, project) =>
        {
            proposal = ProposalCompareExports.AddTrial(database, project,
                [new("lost", [], []) { BeforeSignatures = [Sig(1)], AfterSignatures = [] }]);
            ProposalCompareExports.RecordCurrentBaseline(database, project);
            return CommandOutcome<string>.Success("");
        });
        Assert.True(published.Succeeded, published.Refusal?.Message);
        var runnerRoot = Directory.CreateDirectory(Path.Combine(fixture.Root, "worker")).FullName;
        var directory = Path.Combine(fixture.Root, "manifests");
        var receipt = Path.Combine(fixture.Root, "receipt.json");
        var written = await CliProcess.RunAsync(runnerRoot, null, false, "proposal", "trial", "delta", "--project", fixture.Project,
            "--proposal", proposal, "--write-manifests", directory, "--json");
        Assert.True(written.ExitCode == 0, written.Error);
        var retired = Path.Combine(directory, "expect-retired.jsonl");

        var noExpectations = await CliProcess.RunAsync(runnerRoot, null, false, "proposal", "trial", "delta", "--project", fixture.Project,
            "--proposal", proposal, "--write-receipt", receipt);
        Assert.NotEqual(0, noExpectations.ExitCode);
        Assert.False(File.Exists(receipt));

        var green = await CliProcess.RunAsync(runnerRoot, null, false, "proposal", "trial", "delta", "--project", fixture.Project,
            "--proposal", proposal, "--expect-retired", retired, "--write-receipt", receipt, "--json");
        Assert.True(green.ExitCode == 0, green.Error);
        Assert.True(File.Exists(receipt));
        using (var json = JsonDocument.Parse(green.Output))
            Assert.Equal(Path.GetFullPath(receipt), json.RootElement.GetProperty("receipt").GetProperty("path").GetString());

        File.WriteAllText(retired, "");
        var mismatch = await CliProcess.RunAsync(runnerRoot, null, false, "proposal", "trial", "delta", "--project", fixture.Project,
            "--proposal", proposal, "--expect-retired", retired, "--write-receipt", receipt, "--json");
        Assert.Equal(6, mismatch.ExitCode);
        Assert.False(File.Exists(receipt));

        var pending = await CliProcess.RunAsync(runnerRoot, null, false, "apply", "--all-pending", "--project", fixture.Project,
            "--receipt", receipt, "--json");
        Assert.NotEqual(0, pending.ExitCode);
        Assert.Contains("--receipt", pending.Output + pending.Error);
    }
}
