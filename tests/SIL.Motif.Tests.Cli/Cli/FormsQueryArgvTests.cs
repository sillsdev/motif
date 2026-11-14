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
}
