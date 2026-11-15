using SIL.Motif.Commands;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Query;
using SIL.Motif.Host.Parsimony;
using SIL.Motif.Host.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands.Queries;

public sealed class QuerySourcesCommandTests
{
    [Fact]
    public void StemsObjectsAndSavedQueriesRunThroughTheReadOnlyCommand()
    {
        var root = Path.Combine(Path.GetTempPath(), nameof(QuerySourcesCommandTests), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "project.fwdata");
        try
        {
            File.WriteAllText(path, "marker");
            QueryRow Stem(string id, string form, string category) =>
                new(id, [[form], ["e"], [category.Length == 0 ? "none" : category], [], ["g"], ["1"], ["0"]]);
            QueryRow Object(string id, string cls, string applied) =>
                new(id, [[cls], ["l"], ["0"], [applied], ["0"], ["0"], []]);
            var snapshot = new QueryFactsReader.Snapshot(
                [new QueryForm("f1", "Taka", "taka", "lexeme", "e", "", "", [], [])], [], 0,
                [Stem("s1", "tapa", ""), Stem("s2", "kato", "noun")], [],
                [Object("o1", "environment", "0"), Object("o2", "environment", "3")]);
            var stored = ProjectStoreCommand.Run(path, "1.0", (database, _) =>
            {
                using var connection = database.OpenConnection();
                using var transaction = connection.BeginTransaction();
                QuerySnapshotStore.Replace(connection, transaction, new("trial", "grammar", "bundle", "ws", snapshot));
                transaction.Commit();
                return CommandOutcome<string>.Success("ok");
            });
            Assert.True(stored.Succeeded, stored.Refusal?.Message);

            var stems = FormsQueryCommand.Run(new(path, "1.0", new QueryRecord("stems", [new("category", "is", "none")], [])));
            Assert.True(stems.Succeeded, stems.Refusal?.Message);
            Assert.Equal("s1", Assert.Single(stems.Value!.Result.Items!).Id);

            var objects = FormsQueryCommand.Run(new(path, "1.0", new QueryRecord("objects",
                [new("class", "is", "environment"), new("applied", "is", "0")], [])));
            Assert.True(objects.Succeeded, objects.Refusal?.Message);
            Assert.Equal("o1", Assert.Single(objects.Value!.Result.Items!).Id);

            var folded = FormsQueryCommand.Run(new(path, "1.0", new QueryRecord("forms", [], ["fold"]) { Fold = "project" }));
            Assert.True(folded.Succeeded, folded.Refusal?.Message);

            var unknown = FormsQueryCommand.Run(new(path, "1.0", new QueryRecord("stems", [new("nope", "is", "x")], [])));
            Assert.Equal("query.invalid", unknown.Refusal!.Code);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
