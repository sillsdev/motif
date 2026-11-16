using System.Text;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Query;
using SIL.Motif.Host.Parsimony;
using SIL.Motif.Host.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands.Queries;

public sealed class QueryHypothesisCommandTests
{
    private static void Manifest(string root, string id, string role, params string[] words)
    {
        var lines = new StringBuilder("{\"schema\":\"motif.review-word-set\",\"schemaVersion\":1}\n");
        for (var i = 0; i < words.Length; i++)
            lines.Append($"{{\"caseId\":\"{id}-{i}\",\"word\":\"{words[i]}\",\"writingSystemId\":\"ceb\",\"source\":{{\"citation\":\"t\"}}," +
                $"\"expectationRefs\":[],\"occurrenceId\":\"{id}-o{i}\",\"textId\":\"t\",\"position\":{i},\"weight\":1}}\n");
        File.WriteAllText(Path.Combine(root, id + ".jsonl"), lines.ToString());
    }

    [Fact]
    public void AHypothesisQueryReadsNamedWordSetsAndRefusesWhatIsNotMeasured()
    {
        var root = Path.Combine(Path.GetTempPath(), nameof(QueryHypothesisCommandTests), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "project.fwdata");
        try
        {
            File.WriteAllText(path, "marker");
            Manifest(root, "union", "sampled-tokens", "kaaran", "kuudun", "tapad");
            Manifest(root, "held", "sampled-tokens", "kuurun");
            Manifest(root, "cells", "paradigm-cells");
            File.WriteAllText(Path.Combine(root, "project.motif.toml"),
                "[[review-word-set]]\nid = \"union\"\nname = \"Union\"\nrole = \"sampled-tokens\"\nmanifest = \"union.jsonl\"\n" +
                "[[review-word-set]]\nid = \"held\"\nname = \"Held\"\nrole = \"sampled-tokens\"\nmanifest = \"held.jsonl\"\n" +
                "[[review-word-set]]\nid = \"ref\"\nname = \"Ref\"\nrole = \"reference\"\nselection = \"x\"\n");
            QueryRow Stem(string id, string form) => new(id, [[form], ["e-" + id], ["V"], [], ["g"], ["1"], ["0"]]);
            var snapshot = new QueryFactsReader.Snapshot(
                [new QueryForm("f1", "kuurun", "kuurun", "lexeme", "other", "", "", [], [])], [], 0,
                [Stem("a", "kaad"), Stem("b", "kuud"), Stem("c", "tapad")], [], []);
            var stored = ProjectStoreCommand.Run(path, "1.0", (database, _) =>
            {
                using var connection = database.OpenConnection();
                using var transaction = connection.BeginTransaction();
                QuerySnapshotStore.Replace(connection, transaction, new("trial", "grammar", "bundle", "ws", snapshot));
                transaction.Commit();
                return CommandOutcome<string>.Success("ok");
            });
            Assert.True(stored.Succeeded, stored.Refusal?.Message);
            QueryRecord Record(QueryHypothesis change, params string[] score) =>
                new(QuerySources.Stems, [new("final:2", "~", "^[aiu]d$")], [], Change: change, Score: score);
            QueryHypothesis Change(params string[] sets) => new("d", "r", "final", ["an", "un"], sets);

            var run = FormsQueryCommand.Run(new(path, "1.0", Record(Change("union"), "reliability", "tolerance")));
            Assert.True(run.Succeeded, run.Refusal?.Message);
            var reliability = run.Value!.Result.Scores!.Single(s => s.Scorer == "reliability");
            Assert.Equal(1, reliability.Counts["hits"]);
            Assert.Equal(2, reliability.Counts["scope"]);
            Assert.Contains(run.Value.NotMeasured, note => note.Contains("no current parser evidence"));
            var rows = FormsQueryCommand.Run(new(path, "1.0", new QueryRecord(QuerySources.Stems, [], [], Change: Change("union"))));
            var collides = rows.Value!.Result.Items!.ToDictionary(i => i.Id, i => i.Values["collides"]);
            Assert.Equal(["entry"], collides["b"]);
            Assert.Equal(["not-measured"], collides["a"]);

            Assert.Equal("query.invalid", FormsQueryCommand.Run(new(path, "1.0", Record(Change("nope")))).Refusal!.Code);
            Assert.Equal("query.invalid", FormsQueryCommand.Run(new(path, "1.0", Record(Change("ref")))).Refusal!.Code);
            var sealedSet = FormsQueryCommand.Run(new(path, "1.0", Record(Change("union") with { HeldOut = ["union"] }, "control")));
            Assert.Equal("query.invalid", sealedSet.Refusal!.Code);
            Assert.Equal("query.not-measured", FormsQueryCommand.Run(new(path, "1.0", Record(Change("union"), "control"))).Refusal!.Code);
            Assert.Equal("query.not-measured", FormsQueryCommand.Run(new(path, "1.0", Record(Change(), "reliability"))).Refusal!.Code);
            Assert.Equal("query.mixed-units", FormsQueryCommand.Run(new(path, "1.0",
                Record(Change("union") with { Discovery = ["union"], HeldOut = ["held"] }, "control") with { Unit = "msa" })).Refusal!.Code);
            var control = FormsQueryCommand.Run(new(path, "1.0", Record(Change("union") with { Discovery = ["union"], HeldOut = ["held"] }, "control")));
            Assert.True(control.Succeeded, control.Refusal?.Message);
            Assert.Equal(1, control.Value!.Result.Scores![0].Counts["rule_hits"]);
            Assert.Equal("query.invalid", FormsQueryCommand.Run(new(path, "1.0",
                new QueryRecord(QuerySources.Forms, [], [], Change: Change("union")))).Refusal!.Code);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
