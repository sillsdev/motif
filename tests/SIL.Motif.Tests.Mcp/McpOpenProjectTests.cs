using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using SIL.Motif.Commands.Preferences;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.Store;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker;
using Xunit;

namespace SIL.Motif.Tests.Mcp;

[Collection(LcmCacheParallelCollections.Group3)]
public sealed class McpOpenProjectTests
{
    [Fact]
    public async Task ExplicitOpenRegistersAnUnknownScratchProjectForProposalAuthoring()
    {
        var root = Path.Combine(Path.GetTempPath(), "motif-mcp-open-" + Guid.NewGuid().ToString("N"));
        var workerRoot = Path.Combine(root, "worker");
        var projectRoot = Path.Combine(root, "scratch-project");
        Directory.CreateDirectory(workerRoot);
        Directory.CreateDirectory(projectRoot);

        try
        {
            var fwDataPath = CreateBlankProject(projectRoot);
            var sourceHash = HashProject(fwDataPath);
            var preferencePath = Path.Combine(workerRoot, "advanced-ai-mode.json");
            new FileAdvancedAiModePreferenceStore(preferencePath).SetEnabled(true);
            await using var client = await StartClient(workerRoot, preferencePath);

            var tools = await client.ListToolsAsync();
            Assert.Contains(tools, tool => tool.Name == "motif_open");

            var initiallyKnown = await client.CallToolAsync("motif_list_projects", new Dictionary<string, object?>());
            Assert.True(initiallyKnown.IsError != true, ErrorText(initiallyKnown));
            Assert.Equal(0, initiallyKnown.StructuredContent!.Value.GetProperty("result").GetArrayLength());

            var ordinaryCall = await client.CallToolAsync("motif_proposals", new Dictionary<string, object?>
            {
                ["project"] = fwDataPath,
            });
            Assert.True(ordinaryCall.IsError);
            Assert.Equal("project.unknown", ordinaryCall.StructuredContent!.Value.GetProperty("code").GetString());

            var missingProjectPath = Path.Combine(root, "missing.fwdata");
            var missing = await client.CallToolAsync("motif_open", new Dictionary<string, object?>
            {
                ["path"] = missingProjectPath,
            });
            Assert.True(missing.IsError);
            Assert.Equal("project.not-found", missing.StructuredContent!.Value.GetProperty("code").GetString());

            var stillEmpty = await client.CallToolAsync("motif_list_projects", new Dictionary<string, object?>());
            Assert.True(stillEmpty.IsError != true, ErrorText(stillEmpty));
            Assert.Equal(0, stillEmpty.StructuredContent!.Value.GetProperty("result").GetArrayLength());

            var withProjectSelector = await client.CallToolAsync("motif_open", new Dictionary<string, object?>
            {
                ["path"] = fwDataPath,
                ["project"] = fwDataPath,
            });
            Assert.True(withProjectSelector.IsError);
            Assert.Equal("tool.unknown-argument",
                withProjectSelector.StructuredContent!.Value.GetProperty("code").GetString());

            var opened = await client.CallToolAsync("motif_open", new Dictionary<string, object?>
            {
                ["path"] = fwDataPath,
            });
            Assert.True(opened.IsError != true, ErrorText(opened));
            Assert.Equal(0, opened.StructuredContent!.Value.GetProperty("result")
                .GetProperty("lexicalEntryCount").GetInt32());

            var registered = await client.CallToolAsync("motif_list_projects", new Dictionary<string, object?>());
            Assert.True(registered.IsError != true, ErrorText(registered));
            Assert.Equal(1, registered.StructuredContent!.Value.GetProperty("result").GetArrayLength());
            Assert.Equal(Path.GetFullPath(fwDataPath), registered.StructuredContent!.Value.GetProperty("result")[0]
                .GetProperty("fullFwDataPath").GetString());
            Assert.Equal("fra", registered.StructuredContent.Value.GetProperty("result")[0]
                .GetProperty("projectTag").GetString());

            var reopened = await client.CallToolAsync("motif_open", new Dictionary<string, object?>
            {
                ["path"] = fwDataPath,
            });
            Assert.True(reopened.IsError != true, ErrorText(reopened));
            var stillOneKnownProject = await client.CallToolAsync("motif_list_projects", new Dictionary<string, object?>());
            Assert.True(stillOneKnownProject.IsError != true, ErrorText(stillOneKnownProject));
            Assert.Equal(1, stillOneKnownProject.StructuredContent!.Value.GetProperty("result").GetArrayLength());

            var started = await client.CallToolAsync("motif_start_proposal", new Dictionary<string, object?>
            {
                ["project"] = fwDataPath,
                ["draft"] = "opened-scratch-project",
                ["label"] = "Start a Proposal in the opened project",
                ["rationale"] = "The project was explicitly opened in this MCP session.",
            });
            Assert.True(started.IsError != true, ErrorText(started));
            Assert.Equal("opened-scratch-project", started.StructuredContent!.Value.GetProperty("result")
                .GetProperty("draftName").GetString());
            Assert.Equal(sourceHash, HashProject(fwDataPath));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task FailedMachineStoreRegistrationIsReturnedAsTypedMcpRefusal()
    {
        var root = Path.Combine(Path.GetTempPath(), "motif-mcp-open-registration-" + Guid.NewGuid().ToString("N"));
        var projectRoot = Path.Combine(root, "scratch-project");
        Directory.CreateDirectory(projectRoot);
        Directory.CreateDirectory(root);

        try
        {
            var fwDataPath = CreateBlankProject(projectRoot);
            var sourceHash = HashProject(fwDataPath);
            var workerRootParent = Path.Combine(root, "worker-root-parent");
            File.WriteAllText(workerRootParent, "A file prevents creation of the worker root.");
            var workerRoot = Path.Combine(workerRootParent, "worker");
            var preferencePath = Path.Combine(root, "advanced-ai-mode.json");
            new FileAdvancedAiModePreferenceStore(preferencePath).SetEnabled(true);

            await using var client = await StartClient(workerRoot, preferencePath);
            var result = await client.CallToolAsync("motif_open", new Dictionary<string, object?>
            {
                ["path"] = fwDataPath,
            });

            Assert.True(result.IsError, ErrorText(result));
            var refusal = result.StructuredContent!.Value;
            Assert.False(refusal.GetProperty("ok").GetBoolean());
            Assert.Equal("project.registration-failed", refusal.GetProperty("code").GetString());
            Assert.Equal("Refused", refusal.GetProperty("reason").GetString());
            var facts = refusal.GetProperty("facts");
            Assert.Equal(Path.GetFullPath(workerRoot), facts.GetProperty("workerRoot").GetString());
            Assert.Equal(Path.Combine(Path.GetFullPath(workerRoot), "motif.db"),
                facts.GetProperty("machineStorePath").GetString());
            Assert.False(string.IsNullOrWhiteSpace(facts.GetProperty("cause").GetString()));
            var next = refusal.GetProperty("next").GetString()!;
            Assert.Contains(Path.Combine(Path.GetFullPath(workerRoot), "motif.db"), next, StringComparison.Ordinal);
            Assert.Contains("motif_open", next, StringComparison.Ordinal);
            Assert.Equal(sourceHash, HashProject(fwDataPath));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task SqliteInsertFailureDuringOpenReturnsTypedMcpRegistrationRefusal()
    {
        var root = Path.Combine(Path.GetTempPath(), "motif-mcp-open-sqlite-" + Guid.NewGuid().ToString("N"));
        var workerRoot = Path.Combine(root, "worker");
        var projectRoot = Path.Combine(root, "scratch-project");
        Directory.CreateDirectory(workerRoot);
        Directory.CreateDirectory(projectRoot);

        try
        {
            var fwDataPath = CreateBlankProject(projectRoot);
            var sourceHash = HashProject(fwDataPath);
            var preferencePath = Path.Combine(workerRoot, "advanced-ai-mode.json");
            new FileAdvancedAiModePreferenceStore(preferencePath).SetEnabled(true);
            await using var client = await StartClient(workerRoot, preferencePath, Path.Combine(workerRoot, "motif.db"));

            var initiallyKnown = await client.CallToolAsync("motif_list_projects", new Dictionary<string, object?>());
            Assert.True(initiallyKnown.IsError != true, ErrorText(initiallyKnown));
            Assert.Empty(initiallyKnown.StructuredContent!.Value.GetProperty("result").EnumerateArray());

            using var machine = MachineDatabase.Open(workerRoot);
            using var writer = machine.OpenConnection();
            using var writeLock = writer.BeginTransaction(deferred: false);
            try
            {
                using (var readableMachine = MachineDatabase.Open(workerRoot))
                {
                    Assert.Empty(new KnownProjectRegistry(readableMachine).List());
                }

                var result = await client.CallToolAsync("motif_open", new Dictionary<string, object?>
                {
                    ["path"] = fwDataPath,
                });

                Assert.True(result.IsError, ErrorText(result));
                var refusal = result.StructuredContent!.Value;
                Assert.False(refusal.GetProperty("ok").GetBoolean());
                Assert.Equal("project.registration-failed", refusal.GetProperty("code").GetString());
                Assert.Equal("Refused", refusal.GetProperty("reason").GetString());
                var facts = refusal.GetProperty("facts");
                Assert.Equal(Path.GetFullPath(workerRoot), facts.GetProperty("workerRoot").GetString());
                var machineStorePath = Path.Combine(Path.GetFullPath(workerRoot), "motif.db");
                Assert.Equal(machineStorePath, facts.GetProperty("machineStorePath").GetString());
                var cause = facts.GetProperty("cause").GetString()!;
                Assert.True(cause.Contains("locked", StringComparison.OrdinalIgnoreCase) ||
                    cause.Contains("busy", StringComparison.OrdinalIgnoreCase), cause);
                var next = refusal.GetProperty("next").GetString()!;
                Assert.Contains(Path.GetFullPath(workerRoot), next, StringComparison.Ordinal);
                Assert.Contains(machineStorePath, next, StringComparison.Ordinal);
                Assert.Contains("motif_open", next, StringComparison.Ordinal);
                Assert.Contains("writer", next, StringComparison.OrdinalIgnoreCase);
                Assert.Equal(sourceHash, HashProject(fwDataPath));
            }
            finally
            {
                writeLock.Rollback();
            }

            var afterRelease = await client.CallToolAsync("motif_list_projects", new Dictionary<string, object?>());
            Assert.True(afterRelease.IsError != true, ErrorText(afterRelease));
            Assert.Empty(afterRelease.StructuredContent!.Value.GetProperty("result").EnumerateArray());
            Assert.Equal(sourceHash, HashProject(fwDataPath));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateBlankProject(string projectRoot)
    {
        using var cache = NewLangProjFixture.CreateCache(projectRoot);
        new FwDataProjectLoader().Save(cache);
        return cache.ProjectId.Path;
    }

    private static async Task<McpClient> StartClient(string workerRoot, string preferencePath, string? shortTimeoutPath = null)
    {
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            EnvironmentVariables = new Dictionary<string, string?>
            {
                [FileAdvancedAiModePreferenceStore.PathEnvironmentVariable] = preferencePath,
                [RunnerOptions.RootVariable] = workerRoot,
                ["MOTIF_TEST_SQLITE_SHORT_TIMEOUT_FOR"] = shortTimeoutPath,
            },
            Name = "motif MCP explicit-open regression",
            Command = BuildOutput.Cli,
            Arguments = ["mcp"],
            WorkingDirectory = Path.GetDirectoryName(preferencePath)!,
        });
        return await McpClient.CreateAsync(transport);
    }

    private static string ErrorText(CallToolResult result) => string.Join(Environment.NewLine,
        result.Content.OfType<TextContentBlock>().Select(block => block.Text));

    private static string HashProject(string path) => Convert.ToHexString(
        System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));
}
