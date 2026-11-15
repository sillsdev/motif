using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;
using Microsoft.Data.Sqlite;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Host.Store;
using SIL.Motif.Mcp;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Mcp;

/// <summary>Checks project preservation and the store contract, including explicit evidence-writing exceptions.</summary>
[Collection(LcmCacheParallelCollections.Group3)]
public sealed class ReadToolStorePreservationTests(PristineProjectFixture pristine)
{
    private const string UnknownId = "00000000-0000-4000-8000-000000000001";

    public static IEnumerable<object[]> ReadCalls()
    {
        foreach (var tool in AgentTools.All
            .Where(tool => tool.Class == AgentClass.Read || tool.Name is "motif_trial" or "motif_assess"))
        foreach (var state in new[] { "missing", "empty", "empty-without-lease", "older", "current", "current-without-lease" })
        foreach (var branch in Branches(tool.Name))
            yield return [tool.Name, state, branch];
    }

    [Theory]
    [MemberData(nameof(ReadCalls))]
    public async Task ReadToolsPreserveBytesUnlessTheirActionExplicitlyWritesEvidence(string toolName, string state, string branch)
    {
        var tool = AgentTools.All.Single(tool => tool.Name == toolName);
        var projectPath = pristine.CopyProjectFile();
        var projectRoot = Path.GetDirectoryName(projectPath)!;
        var project = new ProjectLocator(projectPath, Path.GetFileNameWithoutExtension(projectPath));
        PrepareStore(project, state);

        using var privateRoot = PrivateManagedRoot.UseTemporary("SIL.Motif.Tests.ReadTools");
        var managedRoot = RunnerOptions.ResolveRoot();
        using (var machine = MachineDatabase.Open(managedRoot))
            new KnownProjectRegistry(machine).Record(ProjectWorkspaceKey.Compute(project), projectPath,
                DateTimeOffset.UtcNow);

        var context = new ServerContext(projectPath, "1.0", new NoRunnerLauncher(
            new JobRunnerLaunchOptions(managedRoot, null)), new ActivityLog(null), ToolProfile.Builtin,
            TextWriter.Null, advancedAiModeEnabled: true);
        var arguments = Arguments(tool, projectPath, branch);
        var validation = JsonSchema.FromText(tool.InputSchema.ToJsonString())
            .Evaluate(JsonSerializer.SerializeToElement(arguments));
        Assert.True(validation.IsValid, $"{toolName}/{branch} arguments violate its schema: {arguments}");

        var before = Snapshot(projectRoot);
        ToolOutcome? outcome = null;
        var exception = await Record.ExceptionAsync(async () =>
            outcome = await tool.Run(context, new ToolArgs(arguments, tool.ArgumentNames), CancellationToken.None));
        var after = Snapshot(projectRoot);

        AssertUnchanged(before, after, $"{toolName}/{branch}", state);
        Assert.True(exception is null or ToolArgumentException,
            $"{toolName}/{branch} threw with a {state} store: {exception}");
        if (exception is ToolArgumentException argument)
            Assert.NotEqual("tool.missing-argument", argument.Code);
        if (toolName == "motif_source_passage")
        {
            Assert.Null(exception);
            Assert.Equal(state switch
            {
                "missing" or "current" => RefusalCodes.SourceIndexMissing,
                "older" => RefusalCodes.StoreUnsupported,
                "empty" => RefusalCodes.StoreInconsistent,
                _ => "project.operation-io",
            }, outcome!.Refusal!.Code);
        }
    }

    private static void AssertUnchanged(Dictionary<string, byte[]> before, Dictionary<string, byte[]> after,
        string label, string state)
    {
        Assert.Equal(before.Keys.Order(StringComparer.Ordinal), after.Keys.Order(StringComparer.Ordinal));
        foreach (var (path, bytes) in before)
            Assert.True(bytes.AsSpan().SequenceEqual(after[path]), $"{label} changed {path} with a {state} store.");
    }

    public static IEnumerable<object[]> ExceptionBranches()
    {
        foreach (var state in new[] { "missing", "empty", "empty-without-lease", "older" })
        foreach (var branch in new[] { "read-state-set", "read-state-clear", "read-state-expected", "wait-cancel" })
            yield return [state, branch];
    }

    /// <summary>The conditionally writing branches never create, initialise or upgrade a store that is not current.</summary>
    [Theory]
    [MemberData(nameof(ExceptionBranches))]
    public async Task ConditionalWritingBranchesPreserveStoresThatAreNotCurrent(string state, string branch)
    {
        var projectPath = pristine.CopyProjectFile();
        var projectRoot = Path.GetDirectoryName(projectPath)!;
        var project = new ProjectLocator(projectPath, Path.GetFileNameWithoutExtension(projectPath));
        PrepareStore(project, state);
        using var privateRoot = PrivateManagedRoot.UseTemporary("SIL.Motif.Tests.ReadTools");

        var before = Snapshot(projectRoot);
        switch (branch)
        {
            case "wait-cancel":
                var cancel = typeof(JobWait).GetMethod("CancelJobAsync", BindingFlags.Static | BindingFlags.NonPublic)!;
                var cancelled = await (Task<CommandOutcome<SIL.Motif.Contract.Jobs.JobStatusResponse>>)cancel.Invoke(
                    null, [projectPath, UnknownId, "1.0"])!;
                Assert.False(cancelled.Succeeded);
                break;
            default:
                var request = new WordReadStateRequest(projectPath, Guid.NewGuid(), null, branch != "read-state-clear")
                {
                    ExpectedContext = branch == "read-state-expected" ? new ExpectedContext(default!) : null,
                };
                ReadStateCommands.Execute(request);
                break;
        }
        AssertUnchanged(before, Snapshot(projectRoot), branch, state);
    }

    private static void PrepareStore(ProjectLocator project, string state)
    {
        var path = ProjectDatabaseCatalog.DatabasePathFor(project);
        if (state == "missing") return;
        if (state.StartsWith("empty", StringComparison.Ordinal))
        {
            File.WriteAllBytes(path, []);
            File.WriteAllBytes(path + ".owner.lock", []);
            if (state == "empty") File.WriteAllBytes(path + ".use.lock", []);
            return;
        }

        using (new ProjectDatabaseCatalog(MotifSchema.CurrentSchema, new Version(1, 0)).OpenOwned(project)) { }
        if (state == "older")
        {
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = path, Mode = SqliteOpenMode.ReadWrite, Pooling = false,
            }.ToString());
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = $"PRAGMA user_version = {MotifSchema.CurrentSchema - 1}; PRAGMA wal_checkpoint(TRUNCATE);";
            command.ExecuteNonQuery();
        }
        if (state == "current-without-lease") File.Delete(path + ".use.lock");
    }

    private static Dictionary<string, byte[]> Snapshot(string root) => Directory
        .EnumerateFiles(root, "*", SearchOption.AllDirectories)
        .Where(path => !path.EndsWith("-wal", StringComparison.Ordinal) &&
            !path.EndsWith("-shm", StringComparison.Ordinal))
        .ToDictionary(path => Path.GetRelativePath(root, path), File.ReadAllBytes, StringComparer.Ordinal);

    private static IEnumerable<string> Branches(string name) => name switch
    {
        "motif_trial" => ["stored", "report"],
        "motif_proposals" => ["default", "proposal"],
        "motif_sources" => ["default", "source", "chapter"],
        "motif_current" => ["default", "source"],
        "motif_decision_evidence" => ["default", "subject"],
        "motif_difference" => ["legacy", "report", "trace"],
        "motif_proposal_difference" => ["proposal", "difference"],
        "motif_report" => ["coverage", "correctness", "difference", "parsimony"],
        "motif_find_composer" => ["goal", "subject"],
        _ => ["default"],
    };

    private static JsonObject Arguments(AgentTool tool, string projectPath, string branch)
    {
        var arguments = RequiredArguments(tool.InputSchema, projectPath);
        void Set(string key, string value) => arguments[key] = value;
        switch (tool.Name)
        {
            case "motif_trial":
                Set("action", "read");
                if (branch == "report") Set("report", UnknownId);
                break;
            case "motif_assess": Set("assessment", UnknownId); break;
            case "motif_proposals" when branch == "proposal": Set("proposal", UnknownId); break;
            case "motif_sources" when branch == "source": Set("id", "unknown-source"); break;
            case "motif_sources" when branch == "chapter": Set("ref", "MAT 5"); break;
            case "motif_source_passage": Set("source", "unknown-source"); Set("ref", "MAT 5"); break;
            case "motif_current" when branch == "source": Set("source", "unknown-source"); break;
            case "motif_decision_evidence" when branch == "subject": Set("subject", UnknownId); break;
            case "motif_difference" when branch == "report":
                Set("report", UnknownId);
                Set("proposal", UnknownId);
                break;
            case "motif_difference" when branch == "trace":
                Set("report", UnknownId);
                Set("proposal", UnknownId);
                arguments["trace"] = true;
                break;
            case "motif_difference": Set("difference", UnknownId); Set("category", "positives-lost"); break;
            case "motif_proposal_difference": Set(branch, UnknownId); break;
            case "motif_report": Set("kind", branch); break;
            case "motif_find_composer":
                Set(branch, branch == "goal" ? "author a phoneme" : "phonemes");
                break;
        }
        return arguments;
    }

    private static JsonObject RequiredArguments(JsonObject schema, string projectPath)
    {
        var arguments = new JsonObject();
        if (schema["required"] is not JsonArray required) return arguments;
        foreach (var property in required)
        {
            var name = property!.GetValue<string>();
            arguments[name] = Sample(name, schema["properties"]![name]!.AsObject(), projectPath);
        }
        return arguments;
    }

    private static JsonNode Sample(string name, JsonObject schema, string projectPath)
    {
        if (schema["const"] is { } constant) return constant.DeepClone();
        if (schema["enum"] is JsonArray choices) return choices[0]!.DeepClone();
        if (schema["oneOf"] is JsonArray alternatives) return Sample(name, alternatives[0]!.AsObject(), projectPath);
        return schema["type"]?.GetValue<string>() switch
        {
            "object" => RequiredArguments(schema, projectPath),
            "array" => new JsonArray(Enumerable.Range(0, schema["minItems"]?.GetValue<int>() ?? 0)
                .Select(_ => Sample(name, schema["items"]!.AsObject(), projectPath)).ToArray()),
            "integer" => JsonValue.Create(schema["minimum"]?.GetValue<int>() ?? 0)!,
            "number" => JsonValue.Create(schema["minimum"]?.GetValue<double>() ?? 0)!,
            "boolean" => JsonValue.Create(false)!,
            "string" => JsonValue.Create(name switch
            {
                "project" or "path" => projectPath,
                "word" => "cat",
                "timeout" => "00:00:00",
                "planJson" => "{}",
                "diagnosticPath" => Path.Combine(Path.GetDirectoryName(projectPath)!, "absent-trace.json"),
                _ => UnknownId,
            })!,
            _ => throw new InvalidOperationException($"No schema-valid sample for '{name}': {schema}"),
        };
    }
}
