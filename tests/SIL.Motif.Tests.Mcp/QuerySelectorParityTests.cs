using System.Text.Json;
using System.Text.Json.Nodes;
using SIL.Motif.Commands;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Query;
using SIL.Motif.Host.Parsimony;
using SIL.Motif.Host.Store;
using SIL.Motif.Mcp;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Mcp;

public sealed class QuerySelectorParityTests
{
    [Theory]
    [InlineData("form~^a=b$", "~", "^a=b$", "one")]
    [InlineData("form=word in phrase", "is", "word in phrase", "two")]
    public async Task CliAndMcpInterpretSelectorValuesIdentically(string selector, string op, string value, string expected)
    {
        var root = Path.Combine(Path.GetTempPath(), "motif-query-parity", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var project = Path.Combine(root, "project.fwdata");
        try
        {
            File.WriteAllText(project, "marker");
            var rows = new[]
            {
                new QueryForm("one", "a=b", "a=b", "lexeme", "e", "", "", [], []),
                new QueryForm("two", "word in phrase", "word in phrase", "lexeme", "e", "", "", [], [])
            };
            var saved = ProjectStoreCommand.Run(project, "1.0", (database, _) =>
            {
                using var connection = database.OpenConnection();
                using var transaction = connection.BeginTransaction();
                QuerySnapshotStore.Replace(connection, transaction, new("trial", "grammar", "bundle", "ws",
                    new QueryFactsReader.Snapshot(rows, [], 0)));
                transaction.Commit();
                return CommandOutcome<string>.Success("ok");
            });
            Assert.True(saved.Succeeded, saved.Refusal?.Message);
            var cli = await CliProcess.RunAsync(Path.Combine(root, "worker"), null, false,
                "query", "forms", "--project", project, "--where", selector, "--json");
            Assert.True(cli.ExitCode == 0, cli.FailureDetails);
            using var json = JsonDocument.Parse(cli.Output);
            var cliId = json.RootElement.GetProperty("result").GetProperty("rows")[0].GetProperty("id").GetString();
            var tool = AgentTools.All.Single(t => t.Name == "motif_query");
            var context = new ServerContext(project, "1.0", new NoRunnerLauncher(new JobRunnerLaunchOptions(root, null)),
                new ActivityLog(), ToolProfile.Builtin, TextWriter.Null, advancedAiModeEnabled: true);
            var args = new JsonObject { ["from"] = "forms", ["where"] = new JsonArray(
                new JsonObject { ["col"] = "form", ["op"] = op, ["value"] = value }), ["group"] = new JsonArray() };
            var result = await tool.Run(context, new ToolArgs(args, tool.ArgumentNames), CancellationToken.None);
            Assert.Null(result.Refusal);
            var mcpId = result.Value!["result"]!["rows"]![0]!["id"]!.GetValue<string>();
            Assert.Equal(expected, cliId);
            Assert.Equal(cliId, mcpId);
        }
        finally { Directory.Delete(root, true); }
    }
}
