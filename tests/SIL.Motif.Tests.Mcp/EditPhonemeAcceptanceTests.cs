using System.Diagnostics;
using System.IO.Pipelines;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Preferences;
using SIL.Motif.Commands.Store;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Mcp;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Mcp;

[Collection(LcmCacheParallelCollections.Group3)]
public sealed class EditPhonemeAcceptanceTests(PristineProjectFixture pristine)
{
    [Fact]
    public async Task CliAndMcpStageEquivalentEditsAndRefuseAtomically()
    {
        using var project = new WalkthroughProject(pristine);
        var path = project.FwDataPath;
        Guid phonemeId;
        Guid nCodeId;
        Guid qCodeId;
        Guid outsideId;
        int phonemeCount;
        var loader = new FwDataProjectLoader();
        using (var cache = loader.LoadCache(path))
        {
            var set = cache.LangProject.PhonologicalDataOA.PhonemeSetsOS[0];
            var phoneme = set.PhonemesOC.Single(p => p.CodesOS.Any(code =>
                code.Representation.get_String(cache.DefaultVernWs)?.Text == "n"));
            phonemeId = phoneme.Guid;
            nCodeId = phoneme.CodesOS.Single().Guid;
            phonemeCount = set.PhonemesOC.Count;
            IPhCode added = null!;
            IPhPhoneme outside = null!;
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            {
                added = cache.ServiceLocator.GetInstance<IPhCodeFactory>().Create();
                phoneme.CodesOS.Add(added);
                added.Representation.set_String(cache.DefaultVernWs, "q");
                var environment = cache.ServiceLocator.GetInstance<IPhEnvironmentFactory>().Create();
                cache.LangProject.PhonologicalDataOA.EnvironmentsOS.Add(environment);
                environment.StringRepresentation = TsStringUtils.MakeString("/ n _ a", cache.DefaultVernWs);
                var otherSet = cache.ServiceLocator.GetInstance<IPhPhonemeSetFactory>().Create();
                cache.LangProject.PhonologicalDataOA.PhonemeSetsOS.Add(otherSet);
                outside = cache.ServiceLocator.GetInstance<IPhPhonemeFactory>().Create();
                otherSet.PhonemesOC.Add(outside);
            });
            qCodeId = added.Guid;
            outsideId = outside.Guid;
            loader.Save(cache);
        }
        var originalProjectHash = SHA256.HashData(File.ReadAllBytes(path));
        Assert.True(ProposalCommands.New(new(path, "1.0", "cli-edit", "Edit sounds")).Succeeded);
        Assert.True(ProposalCommands.New(new(path, "1.0", "mcp-edit", "Edit sounds")).Succeeded);
        await using var session = await Session.Start(path);
        var target = CanonicalId.FromGuid(phonemeId).Value;
        var valid = Intent(target, new[] { "ng" }, new[] { ("q", "dy") });

        var cli = RunCli(path, "cli-edit", valid, project.ManagedRoot);
        Assert.True(cli.ExitCode == 0, cli.Error + cli.Output);
        var mcp = await session.Edit(path, "mcp-edit", valid);
        Assert.False(mcp.IsError == true, mcp.StructuredContent?.ToString());
        var cliDraft = ReadDraft(path, "cli-edit");
        var mcpDraft = ReadDraft(path, "mcp-edit");
        var cliOperations = ProposalCommands.DeserializeDraft(cliDraft).Operations;
        var mcpOperations = ProposalCommands.DeserializeDraft(mcpDraft).Operations;
        Assert.Equal(3, cliOperations.Count);
        Assert.Equal(3, mcpOperations.Count);
        Assert.True(JsonNode.DeepEquals(Normalized(cliOperations), Normalized(mcpOperations)));
        Assert.Contains(cliOperations, op => op.Kind == "grammar/phCode/setRepresentation" &&
            op.Target == CanonicalId.FromGuid(qCodeId).Value && op.After["text"].GetString() == "dy");
        Assert.Contains(cliOperations, op => op.Kind == "grammar/phCode/setRepresentation" &&
            op.After["text"].GetString() == "ng");
        Assert.DoesNotContain(cliOperations, op => op.Kind.EndsWith("/createPhonemes", StringComparison.Ordinal));

        var missing = CanonicalId.Mint().Value;
        var refusals = new (string? Name, Dictionary<string, object?> Intent)[]
        {
            ("phoneme-edit-empty", Intent(target)),
            ("phoneme-representation-invalid", Intent(target, ["n g"])),
            ("phoneme-representation-duplicate", Intent(target, ["n"])),
            ("phoneme-representation-not-found", Intent(target, respell: [("x", "ts")])),
            ("phoneme-representation-referenced", Intent(target, respell: [("n", "ts")])),
            ("phoneme-not-in-inventory", Intent(CanonicalId.FromGuid(outsideId).Value, ["ts"])),
            (null, Intent(missing, ["ts"])),
        };
        foreach (var (name, intent) in refusals)
        {
            var beforeCli = ReadDraft(path, "cli-edit");
            var beforeMcp = ReadDraft(path, "mcp-edit");
            var cliRefusal = RunCli(path, "cli-edit", intent, project.ManagedRoot);
            Assert.NotEqual(0, cliRefusal.ExitCode);
            using var cliError = JsonDocument.Parse(cliRefusal.Error);
            var mcpRefusal = await session.Edit(path, "mcp-edit", intent);
            Assert.True(mcpRefusal.IsError == true);
            var mcpError = mcpRefusal.StructuredContent!.Value;
            Assert.Equal("draft.invalid", cliError.RootElement.GetProperty("code").GetString());
            Assert.Equal(cliError.RootElement.GetProperty("code").GetString(), mcpError.GetProperty("code").GetString());
            var cliMessage = cliError.RootElement.GetProperty("message").GetString()!;
            var mcpMessage = mcpError.GetProperty("message").GetString()!;
            Assert.Equal(cliMessage, mcpMessage);
            if (name is not null) Assert.Contains(name, cliMessage, StringComparison.Ordinal);
            Assert.Equal(beforeCli, ReadDraft(path, "cli-edit"));
            Assert.Equal(beforeMcp, ReadDraft(path, "mcp-edit"));
        }

        Assert.Equal(originalProjectHash, SHA256.HashData(File.ReadAllBytes(path)));
        using var reloaded = loader.LoadCache(path);
        var inventory = reloaded.LangProject.PhonologicalDataOA.PhonemeSetsOS[0];
        Assert.Equal(phonemeCount, inventory.PhonemesOC.Count);
        var original = Assert.Single(inventory.PhonemesOC, p => p.Guid == phonemeId);
        Assert.Contains(original.CodesOS, c => c.Guid == nCodeId &&
            c.Representation.get_String(reloaded.DefaultVernWs)?.Text == "n");
        Assert.Contains(original.CodesOS, c => c.Guid == qCodeId &&
            c.Representation.get_String(reloaded.DefaultVernWs)?.Text == "q");
        Assert.DoesNotContain(inventory.PhonemesOC.SelectMany(p => p.CodesOS), c =>
            c.Representation.get_String(reloaded.DefaultVernWs)?.Text is "ng" or "dy");
    }

    private static Dictionary<string, object?> Intent(string target, string[]? add = null,
        (string From, string To)[]? respell = null)
    {
        var intent = new Dictionary<string, object?> { ["target"] = target };
        if (add is not null) intent["add"] = add.Select(value => new { representation = value }).ToArray();
        if (respell is not null) intent["respell"] = respell.Select(value => new { from = value.From, to = value.To }).ToArray();
        return intent;
    }

    private static (int ExitCode, string Output, string Error) RunCli(string path, string draft, Dictionary<string, object?> intent, string root)
    {
        var preference = Path.Combine(root, "advanced-ai.json");
        new FileAdvancedAiModePreferenceStore(preference).SetEnabled(true);
        var start = new ProcessStartInfo(BuildOutput.Cli);
        start.Environment[FileAdvancedAiModePreferenceStore.PathEnvironmentVariable] = preference;
        return CliInProcess.Run(start, "compose-edit-phoneme", "--project", path, "--draft", draft,
            "--intent", JsonSerializer.Serialize(intent), "--json");
    }

    private static string ReadDraft(string path, string name)
    {
        using var database = ProjectMotifDatabase.Open(path);
        return new ProposalRepository(database).GetDraft(name).ProposalJson!;
    }

    private static JsonNode Normalized(IReadOnlyList<DraftOperation> operations)
    {
        var json = JsonSerializer.Serialize(operations);
        for (var index = 0; index < operations.Count; index++)
        {
            json = json.Replace(JsonSerializer.Serialize(operations[index].OperationId),
                JsonSerializer.Serialize("operation-" + index), StringComparison.Ordinal);
            if (operations[index].EntityId is { } entity)
                json = json.Replace(JsonSerializer.Serialize(entity), JsonSerializer.Serialize("entity-" + index),
                    StringComparison.Ordinal);
        }
        return JsonNode.Parse(json)!;
    }

    private sealed class Session : IAsyncDisposable
    {
        private readonly CancellationTokenSource _stop;
        private readonly Task _server;
        private readonly McpClient _client;

        private Session(CancellationTokenSource stop, Task server, McpClient client) =>
            (_stop, _server, _client) = (stop, server, client);

        public static async Task<Session> Start(string path)
        {
            Assert.True(ProposalCommands.Open(new(path)).Succeeded);
            KnownProjectTestSetup.AssignUniqueTag(RunnerOptions.ResolveRoot(), path, Path.GetFileNameWithoutExtension(path));
            var toServer = new Pipe();
            var fromServer = new Pipe();
            var stop = new CancellationTokenSource();
            using var startup = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var serverInput = toServer.Reader.AsStream();
            var serverOutput = fromServer.Writer.AsStream();
            var clientOutput = toServer.Writer.AsStream();
            var clientInput = fromServer.Reader.AsStream();
            var server = Task.Run(() => MotifMcpServer.RunAsync(
                new McpLaunchOptions(Profile: "phonology", AdvancedAiModeEnabled: true, ParserPath: FakeParser.ExecutablePath),
                serverInput, serverOutput, TextWriter.Null, stop.Token));
            Task<McpClient>? connecting = null;
            try
            {
                connecting = McpClient.CreateAsync(new StreamClientTransport(clientOutput, clientInput),
                    cancellationToken: startup.Token);
                if (await Task.WhenAny(connecting, server).WaitAsync(startup.Token) == server)
                {
                    await server;
                    throw new InvalidOperationException("MCP server stopped before the client connected.");
                }
                return new Session(stop, server, await connecting.WaitAsync(startup.Token));
            }
            catch (Exception failure)
            {
                var startupExpired = startup.IsCancellationRequested;
                startup.Cancel();
                stop.Cancel();
                toServer.Reader.CancelPendingRead();
                toServer.Writer.CancelPendingFlush();
                fromServer.Reader.CancelPendingRead();
                fromServer.Writer.CancelPendingFlush();
                foreach (var stream in new[] { serverInput, serverOutput, clientOutput, clientInput })
                {
                    try { stream.Dispose(); }
                    catch (Exception) { }
                }
                var cleanup = Task.WhenAll(server, DisposeConnection(connecting));
                try { await cleanup.WaitAsync(TimeSpan.FromSeconds(10)); }
                catch (TimeoutException exception)
                {
                    _ = cleanup.ContinueWith(task => _ = task.Exception, CancellationToken.None,
                        TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                        TaskScheduler.Default);
                    throw new TimeoutException($"MCP startup cleanup did not finish; server status: {server.Status}.",
                        new AggregateException(failure, exception));
                }
                catch (Exception) { }
                finally { stop.Dispose(); }
                if (failure is OperationCanceledException && startupExpired)
                    throw new TimeoutException("MCP client did not connect before the startup deadline.", failure);
                throw;
            }
        }

        private static async Task DisposeConnection(Task<McpClient>? connecting)
        {
            if (connecting is null) return;
            try { await (await connecting).DisposeAsync(); }
            catch (Exception) { }
        }

        public async Task<CallToolResult> Edit(string path, string draft, Dictionary<string, object?> intent)
        {
            var arguments = new Dictionary<string, object?>(intent) { ["project"] = path, ["draft"] = draft };
            return await _client.CallToolAsync("motif_edit_phoneme", arguments);
        }

        public async ValueTask DisposeAsync()
        {
            await _client.DisposeAsync();
            _stop.Cancel();
            try { await _server.WaitAsync(TimeSpan.FromSeconds(10)); }
            catch (Exception exception) when (exception is OperationCanceledException or TimeoutException) { }
            _stop.Dispose();
        }
    }
}
