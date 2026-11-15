using System.Diagnostics;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Query;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Parsimony;
using SIL.Motif.Host.Store;
using Xunit;
using Xunit.Abstractions;
namespace SIL.Motif.Tests.Commands.Queries;
public sealed class QueryCebPerformanceTests(ITestOutputHelper output)
{
    [CopiedFactsFact]
    public void MeasureCommand()
    {
        var root = Environment.GetEnvironmentVariable("MOTIF_QUERY_CEB_COPY")!;
        var path = Path.Combine(root, "sample.fwdata");
        File.WriteAllText(path, "sample");
        var facts = Path.Combine(root, "facts.sqlite");
        var materialization = Stopwatch.StartNew();
        var snapshot = QueryFactsReader.Read(facts, QueryFactsReader.DefaultWritingSystem(facts));
        var factsMs = materialization.ElapsedMilliseconds;
        long writeMs = 0;
        var published = ProjectStoreCommand.Run(path, "1.0", (database, _) =>
        {
            using var connection = database.OpenConnection();
            using var transaction = connection.BeginTransaction();
            var write = Stopwatch.StartNew();
            QuerySnapshotStore.Replace(connection, transaction, new("trial", "grammar", "bundle", "ceb", snapshot));
            transaction.Commit();
            write.Stop();
            writeMs = write.ElapsedMilliseconds;
            return CommandOutcome<string>.Success("ok");
        });
        Assert.True(published.Succeeded, published.Refusal?.Message);
        materialization.Stop();
        output.WriteLine($"materialization_ms={factsMs + writeMs} facts_ms={factsMs} write_ms={writeMs} " +
            $"with_store_open_ms={materialization.ElapsedMilliseconds} forms={snapshot.Forms.Count} stems={snapshot.Stems?.Count}");
        foreach (var group in new[] { "change.right", "letters.dropped", "letters.gained" })
        {
            var conditions = group == "change.right"
                ? new[] { new QueryCondition("kind", "is", "alternate"), new QueryCondition("change", "is", "d>r") }
                : new[] { new QueryCondition("kind", "is", "alternate") };
            var watch = Stopwatch.StartNew();
            var outcome = FormsQueryCommand.Run(new(path, "1.0", new QueryRecord("forms", conditions, [group])));
            watch.Stop();
            Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
            output.WriteLine($"command_{group}_ms={watch.ElapsedMilliseconds} groups={outcome.Value!.Result.Groups.Count}");
            Assert.True(watch.ElapsedMilliseconds < 1000, $"Command took {watch.ElapsedMilliseconds} ms");
        }
    }
    private sealed class CopiedFactsFactAttribute : FactAttribute
    {
        public CopiedFactsFactAttribute()
        {
            var root = Environment.GetEnvironmentVariable("MOTIF_QUERY_CEB_COPY");
            if (string.IsNullOrWhiteSpace(root) || !File.Exists(Path.Combine(root, "facts.sqlite")))
                Skip = "Set MOTIF_QUERY_CEB_COPY to a private copy of the CEB facts artifact.";
        }
    }
}
