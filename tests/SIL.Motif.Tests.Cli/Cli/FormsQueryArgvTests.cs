using SIL.Motif.Commands;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Query;
using SIL.Motif.Host.Parsimony;
using SIL.Motif.Host.Store;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Cli;

public sealed class FormsQueryArgvTests
{
    [Fact]
    public async Task MalformedSelectorReturnsUsageInsteadOfUnexpectedError()
    {
        var result = await CliProcess.RunAsync(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")),
            null, false, "query", "forms", "--project", "unused.fwdata", "--where", "malformed");
        Assert.True(result.ExitCode == 1, result.FailureDetails);
        Assert.DoesNotContain("cli.unexpected", result.Output + result.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RepeatedSelectorsAndGroupingReachTheReleasedExecutable(bool idsOnly)
    {
        var root = Path.Combine(Path.GetTempPath(), "motif-query-argv", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "project.fwdata");
        try
        {
            File.WriteAllText(path, "marker");
            var snapshot = new QueryFactsReader.Snapshot(
                [new QueryForm("id", "sarang", "sadang", "alternate", "entry", "stem", "", [], [], 5)], ["ng"], 0);
            var outcome = ProjectStoreCommand.Run(path, "1.0", (database, _) =>
            {
                using var connection = database.OpenConnection();
                using var transaction = connection.BeginTransaction();
                QuerySnapshotStore.Replace(connection, transaction,
                    new QuerySnapshotStore.Stored("trial", "grammar", "bundle", "ws", snapshot,
                        File.GetLastWriteTimeUtc(path).Ticks));
                transaction.Commit();
                return CommandOutcome<string>.Success("ok");
            });
            Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
            var result = await CliProcess.RunAsync(Path.Combine(root, "worker"), null, false,
                "query", "forms", "--project", path, "--where", "kind=alternate", "--where", "change=d>r",
                "--group", "letters.dropped");
            Assert.True(result.ExitCode == 0, result.FailureDetails);
            Assert.Contains("d\t1\t█", result.Output, StringComparison.Ordinal);
            if (!idsOnly)
            {
                Assert.Contains("trial", result.Output, StringComparison.Ordinal);
                Assert.Contains("grammar", result.Output, StringComparison.Ordinal);
                return;
            }
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(1));
            var ids = await CliProcess.RunAsync(Path.Combine(root, "worker"), null, false,
                "query", "forms", "--project", path, "--ids");
            Assert.True(ids.ExitCode == 0, ids.FailureDetails);
            Assert.Equal("id", ids.Output.Trim());
            Assert.Contains("This evidence is stale", ids.Error, StringComparison.Ordinal);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task ASwitchBeforeTheSourceEatsItAsItsValueAndGivesUsage()
    {
        var result = await CliProcess.RunAsync(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")),
            null, false, "query", "--ids", "forms", "--project", "unused.fwdata");
        Assert.True(result.ExitCode == 1, result.FailureDetails);
        Assert.Contains("query forms", result.Output + result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WordsAfterADoubleDashAreForwardedAndDoNotBreakTheQuery()
    {
        var root = Path.Combine(Path.GetTempPath(), "motif-query-argv", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "project.fwdata");
        try
        {
            File.WriteAllText(path, "marker");
            var snapshot = new QueryFactsReader.Snapshot(
                [new QueryForm("id", "sarang", "sadang", "alternate", "entry", "stem", "", [], [], 5)], ["ng"], 0);
            var outcome = ProjectStoreCommand.Run(path, "1.0", (database, _) =>
            {
                using var connection = database.OpenConnection();
                using var transaction = connection.BeginTransaction();
                QuerySnapshotStore.Replace(connection, transaction,
                    new QuerySnapshotStore.Stored("trial", "grammar", "bundle", "ws", snapshot,
                        File.GetLastWriteTimeUtc(path).Ticks));
                transaction.Commit();
                return CommandOutcome<string>.Success("ok");
            });
            Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
            var result = await CliProcess.RunAsync(Path.Combine(root, "worker"), null, false,
                "query", "forms", "--project", path, "--group", "letters.dropped", "--", "extra");
            Assert.True(result.ExitCode == 0, result.FailureDetails);
            Assert.Contains("trial", result.Output, StringComparison.Ordinal);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task HypothesisFlagsReachTheReleasedExecutableAndAWordSetIsRequired()
    {
        var result = await CliProcess.RunAsync(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")),
            null, false, "query", "stems", "--project", "unused.fwdata", "--change", "d>r", "--at", "final",
            "--affixes", "an,un", "--score", "reliability", "--test-result");
        Assert.NotEqual(0, result.ExitCode);
        Assert.DoesNotContain("cli.unexpected", result.Output + result.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("Unknown option", result.Output + result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EvidenceAndAnalysisFlagsReachTheReleasedExecutableAndNeedAGrouping()
    {
        var members = Path.GetTempFileName();
        try
        {
            File.WriteAllText(members, "a1\na2\n");
            foreach (var args in new[]
                {
                    new[] { "query", "evidence", "stems", "--project", "unused.fwdata", "--members", members },
                    new[] { "query", "evidence", "forms", "--project", "unused.fwdata" },
                    new[] { "query", "stems", "--project", "unused.fwdata", "--score", "size", "--context", "final:2=ud", "--exception", "features=KEEP" },
                })
            {
                var result = await CliProcess.RunAsync(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")), null, false, args);
                Assert.NotEqual(0, result.ExitCode);
                Assert.DoesNotContain("cli.unexpected", result.Output + result.Error, StringComparison.Ordinal);
                Assert.DoesNotContain("Unknown option", result.Output + result.Error, StringComparison.OrdinalIgnoreCase);
            }
        }
        finally { File.Delete(members); }
    }

    [Fact]
    public async Task EvidenceOverAStoredSnapshotPrintsOneScreenAndNamesTheLedgerNeighbours()
    {
        var root = Path.Combine(Path.GetTempPath(), "motif-query-evidence", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "project.fwdata");
        try
        {
            File.WriteAllText(path, "marker");
            var forms = Enumerable.Range(0, 6).Select(i => new QueryForm("v" + i, $"sar{i}ng", $"sad{i}ng", "alternate", "e" + i, "stem", "", [], ["Verb"], 10 - i))
                .Append(new QueryForm("x", "other", "other", "lexeme", "ex", "stem", "", [], ["Verb"], 1)).ToList();
            var snapshot = new QueryFactsReader.Snapshot(forms, ["ng"], 0);
            var outcome = ProjectStoreCommand.Run(path, "1.0", (database, _) =>
            {
                using var connection = database.OpenConnection();
                using var transaction = connection.BeginTransaction();
                QuerySnapshotStore.Replace(connection, transaction,
                    new QuerySnapshotStore.Stored("trial", "grammar", "bundle", "ws", snapshot, File.GetLastWriteTimeUtc(path).Ticks));
                transaction.Commit();
                return CommandOutcome<string>.Success("ok");
            });
            Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
            var result = await CliProcess.RunAsync(Path.Combine(root, "worker"), null, false,
                "query", "evidence", "forms", "--project", path, "--where", "kind=alternate");
            Assert.True(result.ExitCode == 0, result.FailureDetails);
            Assert.Contains("Same shape", result.Output, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("rule attempt", result.Output, StringComparison.OrdinalIgnoreCase);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
