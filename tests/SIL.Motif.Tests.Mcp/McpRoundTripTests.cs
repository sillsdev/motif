using System.IO.Pipelines;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.DomainServices;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.Store;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Mcp;
using SIL.Motif.Commands.Preferences;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker;
using SIL.Motif.Worker.Projects;
using Xunit;

namespace SIL.Motif.Tests.Mcp;

[Collection(LcmCacheParallelCollections.Group3)]
public sealed class McpRoundTripTests(PristineProjectFixture pristine)
{
    [Fact]
    public async Task KnownProjectSelectorsKeepDraftsInTheirOwnProjectsInOneSession()
    {
        using var first = new WalkthroughProject(pristine);
        using var second = new WalkthroughProject(pristine);
        await using var session = await Session.StartAsync(first.FwDataPath, new McpLaunchOptions());
        using (var machine = MachineDatabase.Open(RunnerOptions.ResolveRoot()))
            new KnownProjectRegistry(machine).Record(ProjectWorkspaceKey.Compute(
                new ProjectLocator(second.FwDataPath, "second")), second.FwDataPath, DateTimeOffset.UtcNow);

        var firstDraft = await session.CallAsync("motif_start_proposal", ("draft", "shared-name"),
            ("label", "First project"), ("comment", "One linguistic claim."));
        var secondDraft = await session.CallAsync("motif_start_proposal", ("project", second.FwDataPath),
            ("draft", "shared-name"), ("label", "Second project"), ("comment", "The related claim in the first project."));
        var firstId = firstDraft.GetProperty("result").GetProperty("proposalId").GetString();
        var secondId = secondDraft.GetProperty("result").GetProperty("proposalId").GetString();
        Assert.NotEqual(firstId, secondId);
        var firstProposals = await session.CallAsync("motif_proposals");
        var secondProposals = await session.CallAsync("motif_proposals", ("project", second.FwDataPath));
        Assert.Equal(firstId, Assert.Single(firstProposals.GetProperty("result").GetProperty("proposals")
            .EnumerateArray()).GetProperty("proposalId").GetString());
        Assert.Equal(secondId, Assert.Single(secondProposals.GetProperty("result").GetProperty("proposals")
            .EnumerateArray()).GetProperty("proposalId").GetString());
    }

    [Fact]
    public async Task AnAgentMeasuresADraftBeforeFinalizingItsUnchangedContent()
    {
        using var project = new WalkthroughProject(pristine);
        var activityLog = Path.Combine(project.ManagedRoot, "activity.jsonl");
        Assert.True(BaselineCaptureCommand.Capture(new BaselineCaptureRequest(project.FwDataPath), project.ManagedRoot).Succeeded);
        await using var runner = new InProcessRunnerLauncher(
            new JobRunnerLaunchOptions(project.ManagedRoot, FakeParser.ExecutablePath));
        await using var session = await Session.StartAsync(project.FwDataPath,
            new McpLaunchOptions(ActivityLogPath: activityLog, Runner: runner));
        var client = session.Client;

        var listed = await client.ListToolsAsync();
        Assert.Contains(listed, tool => tool.Name == "motif_grammar");
        Assert.DoesNotContain(listed, tool => tool.Name.Contains("apply") || tool.Name.Contains("discard"));

        var summary = await session.CallAsync("motif_grammar");
        Assert.True(summary.GetProperty("ok").GetBoolean());
        Assert.True(summary.GetProperty("result").TryGetProperty("counts", out _));

        var morphTypes = await session.CallAsync("motif_grammar", ("kind", "morph_types"));
        Assert.True(morphTypes.GetProperty("result").GetProperty("total").GetInt32() > 0);

        var lexicon = await session.CallAsync("motif_lexicon", ("query", SeededProject.FirstForm));
        var entry = lexicon.GetProperty("result").GetProperty("entries")[0];
        var sense = entry.GetProperty("senses")[0];
        Assert.Equal(SeededProject.FirstGloss, sense.GetProperty("gloss").GetString());

        var started = await session.CallAsync("motif_start_proposal", ("draft", "gloss-fix"), ("label", "Reword the first gloss"),
            ("comment", "The seeded gloss reads awkwardly."));
        var proposalId = started.GetProperty("result").GetProperty("proposalId").GetString()!;

        var glossed = await session.CallAsync("motif_set_gloss", ("draft", "gloss-fix"),
            ("sense", sense.GetProperty("id").GetString()!), ("ws", sense.GetProperty("glossWs").GetString()!),
            ("text", "reworded gloss"));
        Assert.Equal(1, glossed.GetProperty("result").GetProperty("operationCount").GetInt32());

        var dryRun = await session.CallAsync("motif_dry_run", ("proposal", proposalId), ("wait_seconds", 120));
        Assert.True(dryRun.GetProperty("ok").GetBoolean(), dryRun.ToString());
        Assert.True(dryRun.GetProperty("result").GetProperty("evidenceCurrent").GetBoolean());

        var trial = await session.CallAsync("motif_trial", ("proposal", proposalId),
            ("words", new[] { SeededProject.AnalysedWordForm }), ("wait_seconds", 120));
        Assert.True(trial.GetProperty("ok").GetBoolean(), trial.ToString());
        var evidence = trial.GetProperty("result");
        Assert.True(evidence.GetProperty("evidenceCurrent").GetBoolean());
        Assert.Equal(TrialResults.Categories.Append("counts"),
            evidence.GetProperty("summary").EnumerateObject().Select(property => property.Name));
        var difference = evidence.GetProperty("difference").GetString()!;
        var page = await session.CallAsync("motif_difference", ("difference", difference),
            ("category", "other-changes"), ("offset", 0), ("limit", 1));
        Assert.Equal(difference, page.GetProperty("result").GetProperty("difference").GetString());
        Assert.InRange(page.GetProperty("result").GetProperty("returned").GetInt32(), 0, 1);

        var operationId = glossed.GetProperty("result").GetProperty("operationId").GetString()!;
        await session.CallAsync("motif_remove_operations", ("draft", "gloss-fix"),
            ("operation_ids", new[] { operationId }));
        await session.CallAsync("motif_set_gloss", ("draft", "gloss-fix"),
            ("sense", sense.GetProperty("id").GetString()!), ("ws", sense.GetProperty("glossWs").GetString()!),
            ("text", "revised gloss"));
        var oldDryRun = await session.CallAsync("motif_dry_run", ("proposal", proposalId),
            ("job", dryRun.GetProperty("result").GetProperty("job").GetString()));
        Assert.False(oldDryRun.GetProperty("result").GetProperty("evidenceCurrent").GetBoolean());
        var oldTrial = await session.CallAsync("motif_trial", ("proposal", proposalId),
            ("job", evidence.GetProperty("job").GetString()));
        Assert.False(oldTrial.GetProperty("result").GetProperty("evidenceCurrent").GetBoolean());
        dryRun = await session.CallAsync("motif_dry_run", ("proposal", proposalId), ("wait_seconds", 120));
        trial = await session.CallAsync("motif_trial", ("proposal", proposalId),
            ("words", new[] { SeededProject.AnalysedWordForm }), ("wait_seconds", 120));
        var revisedEvidence = trial.GetProperty("result");
        Assert.NotEqual(evidence.GetProperty("contentDigest").GetString(), revisedEvidence.GetProperty("contentDigest").GetString());
        Assert.True(revisedEvidence.GetProperty("evidenceCurrent").GetBoolean());
        evidence = revisedEvidence;

        var finished = await session.CallAsync("motif_finalize_proposal", ("draft", "gloss-fix"));
        Assert.Equal(proposalId, finished.GetProperty("result").GetProperty("proposalId").GetString());
        var retainedDryRun = await session.CallAsync("motif_dry_run", ("proposal", proposalId),
            ("job", dryRun.GetProperty("result").GetProperty("job").GetString()));
        Assert.True(retainedDryRun.GetProperty("result").GetProperty("evidenceCurrent").GetBoolean());
        var retainedTrial = await session.CallAsync("motif_trial", ("proposal", proposalId),
            ("job", evidence.GetProperty("job").GetString()));
        Assert.True(retainedTrial.GetProperty("result").GetProperty("evidenceCurrent").GetBoolean());
        var proposals = await session.CallAsync("motif_proposals");
        Assert.Single(proposals.GetProperty("result").GetProperty("proposals").EnumerateArray());

        var activity = await session.CallAsync("motif_activity");
        var result = activity.GetProperty("result");
        Assert.True(result.GetProperty("calls").GetProperty("total").GetInt32() >= 8);
        Assert.Contains(result.GetProperty("proposals").GetProperty("proposals").EnumerateArray(),
            item => item.GetProperty("proposalId").GetString() == proposalId);
        Assert.NotEmpty(result.GetProperty("jobs").EnumerateArray());
        Assert.True(File.ReadAllLines(activityLog).Length >= 8);
    }

    [Fact]
    public async Task ProfileDetailReachesGrammarAndLexiconAndExplicitArgumentsOverrideIt()
    {
        using var project = new WalkthroughProject(pristine);
        Assert.True(BaselineCaptureCommand.Capture(new BaselineCaptureRequest(project.FwDataPath), project.ManagedRoot).Succeeded);
        var profile = Path.Combine(project.ManagedRoot, "detailed.json");
        File.WriteAllText(profile, "{\"name\":\"detailed\",\"defaultDetail\":\"detailed\",\"tools\":[{\"tool\":\"motif_grammar\"},{\"tool\":\"motif_lexicon\"}]}");
        await using var session = await Session.StartAsync(project.FwDataPath, new McpLaunchOptions(Profile: profile));
        var grammar = await session.CallAsync("motif_grammar", ("kind", "categories"));
        Assert.True(grammar.GetProperty("result").GetProperty("items")[0].TryGetProperty("parent", out _));
        var conciseGrammar = await session.CallAsync("motif_grammar", ("kind", "categories"), ("detail", "concise"));
        Assert.False(conciseGrammar.GetProperty("result").GetProperty("items")[0].TryGetProperty("parent", out _));
        var lexicon = await session.CallAsync("motif_lexicon", ("query", SeededProject.FirstForm));
        Assert.True(lexicon.GetProperty("result").GetProperty("entries")[0].TryGetProperty("alternateForms", out _));
        var conciseLexicon = await session.CallAsync("motif_lexicon", ("query", SeededProject.FirstForm), ("detail", "concise"));
        Assert.False(conciseLexicon.GetProperty("result").GetProperty("entries")[0].TryGetProperty("alternateForms", out _));
    }

    [Fact]
    public async Task AReadBeforeAnyBaselineIsAToolErrorThatSaysHowToTakeOne()
    {
        using var project = new WalkthroughProject(pristine);
        await using var session = await Session.StartAsync(project.FwDataPath, new McpLaunchOptions());

        var result = await session.Client.CallToolAsync("motif_grammar", new Dictionary<string, object?> { ["project"] = project.FwDataPath });

        Assert.True(result.IsError);
        var text = ((TextContentBlock)result.Content[0]).Text;
        Assert.StartsWith("baseline.missing:", text);
        Assert.Contains("motif_capture_baseline", text);
    }

    [Fact]
    public async Task TheBuiltCliListsToolsAndServesAReadOverStdio()
    {
        using var project = new WalkthroughProject(pristine);
        Assert.True(BaselineCaptureCommand.Capture(
            new BaselineCaptureRequest(project.FwDataPath), project.ManagedRoot).Succeeded);
        var preferencePath = Path.Combine(project.ManagedRoot, "advanced-ai-mode.json");
        new FileAdvancedAiModePreferenceStore(preferencePath).SetEnabled(true);
        var serverErrors = new System.Collections.Concurrent.ConcurrentQueue<string>();
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            EnvironmentVariables = new Dictionary<string, string?>
            {
                [FileAdvancedAiModePreferenceStore.PathEnvironmentVariable] = preferencePath,
                [RunnerOptions.RootVariable] = project.ManagedRoot,
            },
            Name = "motif process regression",
            Command = BuildOutput.Cli,
            Arguments = ["mcp"],
            WorkingDirectory = project.ManagedRoot,
            StandardErrorLines = serverErrors.Enqueue,
        });
        await using var client = await McpClient.CreateAsync(transport);

        var tools = await client.ListToolsAsync();
        Assert.Contains(tools, tool => tool.Name == "motif_grammar");

        var result = await client.CallToolAsync("motif_grammar", new Dictionary<string, object?> { ["project"] = project.FwDataPath });
        Assert.True(result.IsError != true, string.Join(Environment.NewLine, serverErrors));
        Assert.True(result.StructuredContent!.Value.GetProperty("ok").GetBoolean());
        Assert.True(result.StructuredContent.Value.GetProperty("result").TryGetProperty("counts", out _));
    }

    [Fact]
    public async Task AProfileThatRenamesAndHidesToolsChangesWhatTheClientSees()
    {
        using var project = new WalkthroughProject(pristine);
        await using var session = await Session.StartAsync(project.FwDataPath, new McpLaunchOptions(Profile: "lean"));

        var names = (await session.Client.ListToolsAsync()).Select(tool => tool.Name).ToList();

        Assert.Equal(10, names.Count);
        Assert.Contains("motif_new_proposal", names);
        Assert.DoesNotContain("motif_start_proposal", names);
        Assert.Null(session.Client.ServerInstructions);
    }

    [Fact]
    public async Task ExperimentalParsimonyToolsAndResourceUseTheSharedCommandHandlers()
    {
        using var project = new WalkthroughProject(pristine);
        var prohibitionId = CreateAllomorphProhibition(project);
        var slotId = CreateAffixSlot(project);
        Assert.True(BaselineCaptureCommand.Capture(
            new BaselineCaptureRequest(project.FwDataPath), project.ManagedRoot).Succeeded);
        var parserPath = FakeParser.CopyRecordingInvocations(Path.Combine(project.ManagedRoot, "parsimony-parser"));
        FakeParser.WriteFactsFixture(parserPath, new { prohibitions = Array.Empty<object>() });
        await using var runner = new InProcessRunnerLauncher(
            new JobRunnerLaunchOptions(RunnerOptions.ResolveRoot(), parserPath));
        await using var session = await Session.StartAsync(project.FwDataPath, new McpLaunchOptions(
            Profile: "parsimony-experimental", Runner: runner));

        var resources = await session.Client.ListResourcesAsync();
        Assert.Contains(resources, resource => resource.Uri == "motif://parsimony/measures");
        var resource = await session.Client.ReadResourceAsync("motif://parsimony/measures");
        var measures = ParsimonyViewsCommand.Measures(new ListParsimonyMeasuresRequest(
            project.FwDataPath, MotifProductVersion.CurrentText));
        Assert.True(measures.Succeeded, measures.Refusal?.Message);
        Assert.Equal(ProjectionJson.Serialize(measures.Value!), Assert.IsType<TextResourceContents>(
            Assert.Single(resource.Contents)).Text);

        var catalog = await session.CallAsync("motif_parsimony_measures");
        Assert.True(catalog.GetProperty("result").GetProperty("measures").GetArrayLength() > 0);
        var run = await session.CallAsync("motif_run_parsimony_measure", ("measure_id", "P-adhoc-duplicate"),
            ("evidence_scope", "project-approved"), ("wait_seconds", 120));
        Assert.True(run.GetProperty("ok").GetBoolean(), run.ToString());
        var report = run.GetProperty("result");
        var cliReport = ParsimonyCommands.Show(new ShowParsimonyReportRequest(project.FwDataPath,
            MotifProductVersion.CurrentText, report.GetProperty("reportId").GetString()!));
        Assert.True(cliReport.Succeeded, cliReport.Refusal?.Message);
        Assert.Equal(JsonNode.Parse(ProjectionJson.Serialize(cliReport.Value!))!.ToJsonString(),
            JsonNode.Parse(report.GetRawText())!.ToJsonString());

        var bundleId = report.GetProperty("inputs").GetProperty("bundleId").GetString()!;
        var viewRequest = new ReadParsimonyViewRequest(project.FwDataPath, MotifProductVersion.CurrentText,
            new ParsimonyNamedViewRequest(bundleId, "parsimony-active-findings",
                new ParsimonyViewFilters(ReportId: cliReport.Value!.ReportId)));
        var cliView = ParsimonyViewsCommand.View(viewRequest);
        Assert.True(cliView.Succeeded, cliView.Refusal?.Message);
        var mcpView = await session.CallAsync("motif_parsimony_view", ("bundle_id", bundleId),
            ("view", "parsimony-active-findings"), ("report_id", cliReport.Value.ReportId));
        Assert.Equal(JsonNode.Parse(ProjectionJson.Serialize(cliView.Value!))!.ToJsonString(),
            JsonNode.Parse(mcpView.GetProperty("result").GetRawText())!.ToJsonString());
        Assert.Equal(cliView.Value!.ActiveCount,
            mcpView.GetProperty("result").GetProperty("activeCount").GetInt32());

        await session.CallAsync("motif_start_proposal", ("draft", "slot-edit"),
            ("label", "Make one affix slot optional"), ("comment", "This is a Draft-only check."));
        var slotEdit = await session.CallAsync("motif_edit_affix_slot", ("draft", "slot-edit"),
            ("target", slotId.Value), ("expected_optional", false), ("optional", true));
        var slotOperation = Assert.Single(slotEdit.GetProperty("result").GetProperty("operations").EnumerateArray());
        Assert.Equal("grammar/moInflAffixSlot/setOptional", slotOperation.GetProperty("kind").GetString());
        Assert.Equal(1, slotEdit.GetProperty("result").GetProperty("operationCount").GetInt32());

        await session.CallAsync("motif_start_proposal", ("draft", "slot-create"),
            ("label", "Add a category-owned slot"), ("comment", "The affix needs a new position class."));
        var createdSlot = await session.CallAsync("motif_add_affix_slot", ("draft", "slot-create"),
            ("category", CanonicalId.FromGuid(project.Seed.PartOfSpeechId).Value),
            ("name", "MCP authored slot"), ("ws", "en"), ("optional", false));
        var createdOperations = createdSlot.GetProperty("result").GetProperty("operations").EnumerateArray().ToArray();
        Assert.Equal(3, createdSlot.GetProperty("result").GetProperty("operationCount").GetInt32());
        Assert.Contains(createdOperations, operation =>
            operation.GetProperty("kind").GetString() == "grammar/partOfSpeech/createAffixSlots" &&
            operation.GetProperty("entityId").GetString() is not null);
        Assert.Contains(createdOperations, operation =>
            operation.GetProperty("kind").GetString() == "grammar/moInflAffixSlot/setName");
        Assert.Contains(createdOperations, operation =>
            operation.GetProperty("kind").GetString() == "grammar/moInflAffixSlot/setOptional");

        var createdSlotId = createdOperations.Single(operation =>
            operation.GetProperty("kind").GetString() == "grammar/partOfSpeech/createAffixSlots")
            .GetProperty("entityId").GetString()!;
        var template = await session.CallAsync("motif_add_affix_template", ("draft", "slot-create"),
            ("category", CanonicalId.FromGuid(project.Seed.PartOfSpeechId).Value),
            ("name", "MCP alternative template"), ("ws", "en"),
            ("prefix_slots", Array.Empty<string>()), ("suffix_slots", new[] { createdSlotId }), ("final", false));
        var templateOperations = template.GetProperty("result").GetProperty("operations").EnumerateArray().ToArray();
        Assert.Equal(7, template.GetProperty("result").GetProperty("operationCount").GetInt32());
        Assert.Contains(templateOperations, operation =>
            operation.GetProperty("kind").GetString() == "grammar/partOfSpeech/createAffixTemplates");
        Assert.Contains(templateOperations, operation =>
            operation.GetProperty("kind").GetString() == "grammar/moInflAffixTemplate/setName");
        Assert.Contains(templateOperations, operation =>
            operation.GetProperty("kind").GetString() == "grammar/moInflAffixTemplate/setFinal");
        Assert.Contains(templateOperations, operation =>
            operation.GetProperty("kind").GetString() == "grammar/moInflAffixTemplate/addRefSuffixSlots");

        await session.CallAsync("motif_start_proposal", ("draft", "disable-adhoc"),
            ("label", "Disable one duplicate rule"), ("comment", "This is a Draft-only check."));
        var staged = await session.CallAsync("motif_edit_adhoc_prohibition", ("draft", "disable-adhoc"),
            ("target", prohibitionId.Value), ("expected_disabled", false), ("disabled", true));
        var operation = Assert.Single(staged.GetProperty("result").GetProperty("operations").EnumerateArray());
        Assert.Equal("grammar/moAdhocProhib/setDisabled", operation.GetProperty("kind").GetString());
        using var saved = new FwDataProjectLoader().LoadCache(project.FwDataPath);
        Assert.False(((IMoAdhocProhib)saved.ServiceLocator.GetInstance<ICmObjectRepository>()
            .GetObject(prohibitionId.ToGuid())).Disabled);
        Assert.False(((IMoInflAffixSlot)saved.ServiceLocator.GetInstance<ICmObjectRepository>()
            .GetObject(slotId.ToGuid())).Optional);
    }

    [Fact]
    public async Task PhonologyToolsUseDraftReferencesAndReturnActionableRefusals()
    {
        using var project = new WalkthroughProject(pristine);
        await using var session = await Session.StartAsync(project.FwDataPath, new McpLaunchOptions(Profile: "phonology"));
        await session.CallAsync("motif_start_proposal", ("draft", "sounds"), ("label", "Add vowel context"),
            ("comment", "Synthetic phonological contrast."));
        var phoneme = await session.CallAsync("motif_add_phoneme", ("draft", "sounds"), ("name", "z"), ("representations", new[] { "z" }));
        var phonemeId = phoneme.GetProperty("result").GetProperty("operations").EnumerateArray()
            .Single(o => o.GetProperty("kind").GetString()!.EndsWith("/createPhonemes")).GetProperty("entityId").GetString()!;
        var naturalClass = await session.CallAsync("motif_add_natural_class", ("draft", "sounds"),
            ("name", "new consonants"), ("abbreviation", "NewC"), ("members", new[] { phonemeId }));
        var classId = naturalClass.GetProperty("result").GetProperty("operations")[0].GetProperty("entityId").GetString()!;
        await session.CallAsync("motif_add_environment", ("draft", "sounds"), ("name", "after new consonant"),
            ("left", new[] { new { naturalClass = classId } }), ("right", new[] { new { boundary = "word" } }));
        var rule = await session.CallAsync("motif_add_phonological_rule", ("draft", "sounds"),
            ("name", "delete z between z"), ("direction", "simultaneous"),
            ("input", new[] { new { phoneme = phonemeId } }), ("output", Array.Empty<object>()),
            ("left", new[] { new { phoneme = phonemeId } }),
            ("right", new[] { new { phoneme = phonemeId } }));
        var ruleOperations = rule.GetProperty("result").GetProperty("operations").EnumerateArray().ToArray();
        Assert.Contains(ruleOperations, operation =>
            operation.GetProperty("kind").GetString() == "grammar/phPhonData/createPhonRules");
        Assert.Contains(ruleOperations, operation =>
            operation.GetProperty("kind").GetString() == "grammar/phSegmentRule/setDisabled");
        var refused = await session.Client.CallToolAsync("motif_add_natural_class", new Dictionary<string, object?>
        {
            ["project"] = project.FwDataPath, ["draft"] = "sounds", ["name"] = "duplicate", ["abbreviation"] = "NewC", ["members"] = new[] { phonemeId },
        });
        Assert.True(refused.IsError);
        var text = ((TextContentBlock)refused.Content[0]).Text;
        Assert.Contains("already exists", text);
        Assert.Contains("Next:", text);
        await session.CallAsync("motif_finalize_proposal", ("draft", "sounds"));
    }

    private sealed class Session : IAsyncDisposable
    {
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _server;

        private Session(McpClient client, Task server, string project)
        {
            _project = project;
            Client = client;
            _server = server;
        }

        private readonly string _project;

        public McpClient Client { get; }

        public static async Task<Session> StartAsync(string project, McpLaunchOptions options)
        {
            Assert.True(ProposalCommands.Open(new OpenRequest(project)).Succeeded);
            using (var machine = MachineDatabase.Open(RunnerOptions.ResolveRoot()))
                new KnownProjectRegistry(machine).Record(ProjectWorkspaceKey.Compute(
                    new ProjectLocator(project, Path.GetFileNameWithoutExtension(project))), project, DateTimeOffset.UtcNow);
            var advancedAiMode = FileAdvancedAiModePreferenceStore.ForInstallation();
            advancedAiMode.SetEnabled(true);
            options = options with { AdvancedAiModeEnabled = advancedAiMode.IsEnabled, ParserPath = FakeParser.ExecutablePath };

            var toServer = new Pipe();
            var fromServer = new Pipe();
            var stop = new CancellationTokenSource();
            var server = Task.Run(() => MotifMcpServer.RunAsync(options, toServer.Reader.AsStream(),
                fromServer.Writer.AsStream(), TextWriter.Null, stop.Token));
            var connecting = McpClient.CreateAsync(
                new StreamClientTransport(toServer.Writer.AsStream(), fromServer.Reader.AsStream()));
            if (await Task.WhenAny(connecting, server) == server) await server;
            var client = await connecting;
            var session = new Session(client, server, project);
            session._stop.Token.Register(stop.Cancel);
            return session;
        }

        public async Task<JsonElement> CallAsync(string tool, params (string Name, object? Value)[] arguments)
        {
            var input = arguments.ToDictionary(pair => pair.Name, pair => pair.Value);
            if (tool is not ("motif_guide" or "motif_list_projects")) input.TryAdd("project", _project);
            var result = await Client.CallToolAsync(tool, input);
            Assert.True(result.IsError != true, ((TextContentBlock)result.Content[0]).Text);
            return result.StructuredContent!.Value;
        }

        public async ValueTask DisposeAsync()
        {
            await Client.DisposeAsync();
            _stop.Cancel();
            try { await _server.WaitAsync(TimeSpan.FromSeconds(10)); }
            catch (Exception exception) when (exception is OperationCanceledException or TimeoutException) { }
        }
    }

    private static CanonicalId CreateAllomorphProhibition(WalkthroughProject project)
    {
        using var cache = new FwDataProjectLoader().LoadCache(project.FwDataPath);
        var prohibition = cache.ServiceLocator.GetInstance<IMoAlloAdhocProhibFactory>().Create();
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            cache.LangProject.MorphologicalDataOA.AdhocCoProhibitionsOC.Add(prohibition);
            prohibition.Disabled = false;
        });
        var id = CanonicalId.FromGuid(prohibition.Guid);
        new FwDataProjectLoader().Save(cache);
        return id;
    }

    private static CanonicalId CreateAffixSlot(WalkthroughProject project)
    {
        using var cache = new FwDataProjectLoader().LoadCache(project.FwDataPath);
        var category = (IPartOfSpeech)cache.ServiceLocator.GetInstance<ICmObjectRepository>()
            .GetObject(project.Seed.PartOfSpeechId);
        IMoInflAffixSlot slot = null!;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            slot = cache.ServiceLocator.GetInstance<IMoInflAffixSlotFactory>().Create();
            category.AffixSlotsOC.Add(slot);
            slot.Name.set_String(cache.DefaultAnalWs, "MCP slot");
        });
        var id = CanonicalId.FromGuid(slot.Guid);
        new FwDataProjectLoader().Save(cache);
        return id;
    }
}
