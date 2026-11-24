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

public sealed class QueryV2ParityTests : IDisposable
{
    private const string Document = "{\"version\":\"motif.query.v2\",\"from\":\"forms\",\"where\":{\"or\":[" +
        "{\"field\":\"kind\",\"op\":\"eq\",\"value\":\"variant\"},{\"field\":\"form\",\"op\":\"regex\",\"value\":\"sar.*\"}]},\"limit\":2}";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-query-v2", Guid.NewGuid().ToString("N"));
    private readonly string _project;

    public QueryV2ParityTests()
    {
        Directory.CreateDirectory(_root);
        _project = Path.Combine(_root, "project.fwdata");
        File.WriteAllText(_project, "marker");
        var rows = new[]
        {
            new QueryForm("a", "sarang", "sadang", "alternate", "e1", "", "", [], []),
            new QueryForm("b", "sarangan", "sadang", "alternate", "e2", "", "", [], []),
            new QueryForm("c", "sang", "sadang", "variant", "e3", "", "", [], []),
            new QueryForm("d", "kaon", "kaon", "lexeme", "e4", "", "", [], []),
        };
        var saved = ProjectStoreCommand.Run(_project, "1.0", (database, _) =>
        {
            using var connection = database.OpenConnection();
            using var transaction = connection.BeginTransaction();
            QuerySnapshotStore.Replace(connection, transaction, new("trial", "grammar", "bundle", "ws",
                new QueryFactsReader.Snapshot(rows, [], 0)));
            transaction.Commit();
            return CommandOutcome<string>.Success("ok");
        });
        Assert.True(saved.Succeeded, saved.Refusal?.Message);
    }

    public void Dispose() => Directory.Delete(_root, true);

    private ServerContext Context() => new(_project, "1.0", new NoRunnerLauncher(new JobRunnerLaunchOptions(_root, null)),
        new ActivityLog(null), ToolProfile.Builtin, TextWriter.Null, advancedAiModeEnabled: true);

    private async Task<ToolOutcome> Mcp(JsonObject args)
    {
        var tool = AgentTools.All.Single(t => t.Name == "motif_query");
        return await tool.Run(Context(), new ToolArgs(args, tool.ArgumentNames), CancellationToken.None);
    }

    [Fact]
    public async Task SchemaIsPrintedByTheCliAndMatchesTheGenerator()
    {
        var cli = await CliProcess.RunAsync(Path.Combine(_root, "worker"), null, false, "query", "--schema");
        Assert.True(cli.ExitCode == 0, cli.FailureDetails);
        Assert.Equal(QuerySchema.Generate(), cli.Output);
    }

    [Fact]
    public async Task TheCliTheMcpToolAndTheTextFormReturnTheSameRowsForOneQuery()
    {
        var file = Path.Combine(_root, "query.json");
        File.WriteAllText(file, Document);
        var cli = await CliProcess.RunAsync(Path.Combine(_root, "worker"), null, false,
            "query", "--project", _project, "--query-json", file, "--json");
        Assert.True(cli.ExitCode == 0, cli.FailureDetails);
        using var cliJson = JsonDocument.Parse(cli.Output);
        Assert.Equal("motif.query.v2", cliJson.RootElement.GetProperty("schemaVersion").GetString());
        var cliIds = cliJson.RootElement.GetProperty("result").GetProperty("rows").EnumerateArray()
            .Select(row => row.GetProperty("id").GetString()).ToArray();

        var result = await Mcp(new JsonObject { ["query"] = JsonNode.Parse(Document) });
        Assert.Null(result.Refusal);
        var mcpIds = result.Value!["result"]!["rows"]!.AsArray().Select(row => row!["id"]!.GetValue<string>()).ToArray();
        Assert.Equal("motif.query.v2", result.Value["schemaVersion"]!.GetValue<string>());

        var text = await CliProcess.RunAsync(Path.Combine(_root, "worker"), null, false,
            "query", "forms", "--project", _project, "--where", "kind is variant or form matches \"sar.*\"", "--limit", "2", "--json");
        Assert.True(text.ExitCode == 0, text.FailureDetails);
        using var textJson = JsonDocument.Parse(text.Output);
        var textIds = textJson.RootElement.GetProperty("result").GetProperty("rows").EnumerateArray()
            .Select(row => row.GetProperty("id").GetString()).ToArray();

        Assert.Equal(2, cliIds.Length);
        Assert.Equal(cliIds, mcpIds);
        Assert.Equal(cliIds, textIds);
    }

    [Fact]
    public async Task AnOpThatDoesNotFitItsColumnIsRefusedWithItsCodeByBothFrontEnds()
    {
        const string bad = "{\"version\":\"motif.query.v2\",\"from\":\"forms\",\"where\":{\"field\":\"form\",\"op\":\"lt\",\"value\":3}}";
        var result = await Mcp(new JsonObject { ["query"] = JsonNode.Parse(bad) });
        Assert.Equal(RefusalCodes.QueryOpTypeMismatch, result.Refusal?.Code);
        var file = Path.Combine(_root, "bad.json");
        File.WriteAllText(file, bad);
        var cli = await CliProcess.RunAsync(Path.Combine(_root, "worker"), null, false,
            "query", "--project", _project, "--query-json", file, "--json");
        Assert.NotEqual(0, cli.ExitCode);
        Assert.Contains(RefusalCodes.QueryOpTypeMismatch, cli.Output + cli.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AReservedKindIsRefusedAsNotYetAvailableAndAMixedCallIsInvalid()
    {
        const string reserved = "{\"version\":\"motif.query.v2\",\"from\":\"forms\",\"where\":{\"morph\":{\"and\":[]}}}";
        var result = await Mcp(new JsonObject { ["query"] = JsonNode.Parse(reserved) });
        Assert.Equal(RefusalCodes.QueryNotYetAvailable, result.Refusal?.Code);
        await Assert.ThrowsAsync<ToolArgumentException>(() =>
            Mcp(new JsonObject { ["query"] = JsonNode.Parse(Document), ["from"] = "forms" }));
    }
}
