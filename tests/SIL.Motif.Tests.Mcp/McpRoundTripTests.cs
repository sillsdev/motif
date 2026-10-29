using System.IO.Pipelines;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.DomainServices;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Review;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.HumanJudgments;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Retirement;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Mcp;
using SIL.Motif.Commands.Preferences;
using SIL.Motif.Projection.HumanJudgments;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Runner.Retirement;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Mcp;

[Collection(LcmCacheParallelCollections.Group3)]
public sealed class McpRoundTripTests(PristineProjectFixture pristine)
{
    [Fact]
    public async Task EntryDoNotParseToolStagesWithoutApplying()
    {
        using var project = new WalkthroughProject(pristine);
        var projectHash = HashProject(project.FwDataPath);
        await using var session = await Session.StartAsync(project.FwDataPath, new McpLaunchOptions());
        await session.CallAsync("motif_start_proposal", ("draft", "entry-flag"),
            ("label", "Flag entry"), ("rationale", "Review the entry flag."));

        var result = await session.CallAsync("motif_set_entry_do_not_parse", ("draft", "entry-flag"),
            ("target", CanonicalId.FromGuid(project.Seed.FirstEntryId).Value),
            ("expectedDoNotParse", false), ("doNotParse", true));

        Assert.True(result.GetProperty("ok").GetBoolean(), result.ToString());
        var operation = Assert.Single(result.GetProperty("result").GetProperty("operations").EnumerateArray());
        Assert.Equal(LexEntryDoNotUseForParsingOperationKinds.SetDoNotUseForParsing,
            operation.GetProperty("kind").GetString());
        Assert.Equal(projectHash, HashProject(project.FwDataPath));
    }

    [Fact]
    public async Task LexiconRowsAndSourceStayBoundToTheCaptureUntilAReplacementBaseline()
    {
        using var project = new WalkthroughProject(pristine,
            sourceLastWriteUtc: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var capture = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(project.FwDataPath), project.ManagedRoot);
        Assert.True(capture.Succeeded, capture.Refusal?.Message);
        await using var session = await Session.StartAsync(project.FwDataPath, new McpLaunchOptions());
        var evidence = SIL.Motif.Commands.Queries.CurrentEvidenceQuery.ReadCurrentEvidence(project.FwDataPath);
        Assert.True(evidence.Succeeded, evidence.Refusal?.Message);
        var baseline = evidence.Value!.Baseline!;
        var lexicon = (await session.CallAsync("motif_lexicon")).GetProperty("result");
        var source = lexicon.GetProperty("source");
        Assert.Equal(baseline.Token, ProjectionJson.Deserialize<SIL.Motif.Contract.Baselines.BaselineToken>(
            source.GetProperty("token").GetRawText()));
        Assert.Equal(baseline.SourceLastWriteUtc, source.GetProperty("sourceLastWriteUtc").GetDateTimeOffset());
        Assert.Equal(baseline.PublishedUtc, source.GetProperty("publishedUtc").GetDateTimeOffset());
        Assert.Equal(evidence.Value.ProjectSummary!.LexemeCount, lexicon.GetProperty("matched").GetInt32());
        Assert.Equal(new BaselineReadSource(baseline.Token, baseline.SourceLastWriteUtc, baseline.PublishedUtc),
            ProjectionJson.Deserialize<BaselineReadSource>(source.GetRawText()));
        var overview = SIL.Motif.Commands.Catalog.OverviewCommand.Overview(new OverviewRequest(project.FwDataPath));
        Assert.True(overview.Succeeded, overview.Refusal?.Message);
        Assert.Equal(baseline.Token, overview.Value!.BaselineToken);
        Assert.Equal(overview.Value.LexemeCount, lexicon.GetProperty("matched").GetInt32());

        var loader = new FwDataProjectLoader();
        using (var cache = loader.LoadCache(project.FwDataPath))
        {
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
                cache.ServiceLocator.GetInstance<ILexEntryRepository>().GetObject(project.Seed.SecondEntryId).Delete());
            loader.Save(cache);
        }
        File.SetLastWriteTimeUtc(project.FwDataPath, baseline.SourceLastWriteUtc.UtcDateTime.AddMinutes(1));

        var stale = (await session.CallAsync("motif_lexicon")).GetProperty("result");
        Assert.True(JsonElement.DeepEquals(lexicon, stale));
        var filtered = (await session.CallAsync("motif_lexicon", ("query", SeededProject.FirstForm),
            ("detail", "detailed"), ("limit", 1))).GetProperty("result");
        Assert.True(JsonElement.DeepEquals(source, filtered.GetProperty("source")));
        Assert.Equal(1, filtered.GetProperty("matched").GetInt32());

        var replacement = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(project.FwDataPath), project.ManagedRoot);
        Assert.True(replacement.Succeeded, replacement.Refusal?.Message);
        Assert.NotEqual(capture.Value!.Token, replacement.Value!.Token);
        var refreshed = (await session.CallAsync("motif_lexicon")).GetProperty("result");
        Assert.Equal(replacement.Value.Token, ProjectionJson.Deserialize<SIL.Motif.Contract.Baselines.BaselineToken>(
            refreshed.GetProperty("source").GetProperty("token").GetRawText()));
        Assert.Equal(replacement.Value.SourceLastWriteUtc,
            refreshed.GetProperty("source").GetProperty("sourceLastWriteUtc").GetDateTimeOffset());
        Assert.Equal(lexicon.GetProperty("matched").GetInt32() - 1, refreshed.GetProperty("matched").GetInt32());
    }

    [Fact]
    public async Task GrammarCheckAndWarningsReturnMcpErrorsWithoutABaseline()
    {
        using var project = new WalkthroughProject(pristine);
        var profile = Path.Combine(project.ManagedRoot, "grammar-tools.json");
        File.WriteAllText(profile, """
            {"name":"grammar-tools","tools":[{"tool":"motif_check_grammar"},{"tool":"motif_findings"}]}
            """);
        await using var session = await Session.StartAsync(project.FwDataPath,
            new McpLaunchOptions(Profile: profile));
        var arguments = new Dictionary<string, object?> { ["project"] = project.FwDataPath };

        foreach (var tool in new[] { "motif_check_grammar", "motif_findings" })
        {
            var result = await session.Client.CallToolAsync(tool, arguments);

            Assert.True(result.IsError);
            Assert.Equal("baseline.missing", result.StructuredContent!.Value.GetProperty("code").GetString());
            // This profile hides the capture tool, so the step is the person's, not a call the agent cannot make.
            Assert.Contains("Ask the person to run `motif baseline capture",
                ((TextContentBlock)Assert.Single(result.Content)).Text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task AffixAuthoringToolsRouteEntryConditionsAndAnAlternateForm()
    {
        using var project = new WalkthroughProject(pristine);
        var projectHash = HashProject(project.FwDataPath);
        await using var session = await Session.StartAsync(project.FwDataPath,
            new McpLaunchOptions(Profile: "phonology"));
        await session.CallAsync("motif_start_proposal", ("draft", "affix-authoring"),
            ("label", "Author a conditioned prefix"), ("rationale", "Each form names its conditions."));

        var category = CanonicalId.FromGuid(project.Seed.PartOfSpeechId).Value;
        var inflectionClass = await session.CallAsync("motif_add_inflection_class", ("draft", "affix-authoring"),
            ("category", category), ("name", "1/2"), ("ws", "en"));
        Assert.True(inflectionClass.GetProperty("ok").GetBoolean(), inflectionClass.ToString());
        var classCreate = Assert.Single(inflectionClass.GetProperty("result").GetProperty("operations").EnumerateArray(),
            operation => operation.GetProperty("kind").GetString() == "grammar/partOfSpeech/createInflectionClasses");
        var classId = classCreate.GetProperty("entityId").GetString()!;

        var phoneme = await session.CallAsync("motif_add_phoneme", ("draft", "affix-authoring"),
            ("name", "z"), ("representations", new[] { "z" }));
        Assert.True(phoneme.GetProperty("ok").GetBoolean(), phoneme.ToString());
        Assert.StartsWith($"Draft now has {phoneme.GetProperty("result").GetProperty("operationCount").GetInt32()} operations.",
            phoneme.GetProperty("next").GetString(), StringComparison.Ordinal);
        var phonemeCreate = Assert.Single(phoneme.GetProperty("result").GetProperty("operations").EnumerateArray(),
            operation => operation.GetProperty("kind").GetString()!.EndsWith("/createPhonemes", StringComparison.Ordinal));
        var phonemeId = phonemeCreate.GetProperty("entityId").GetString()!;

        var environment = await session.CallAsync("motif_add_environment", ("draft", "affix-authoring"),
            ("name", "after a"), ("left", new[] { new { phoneme = phonemeId } }),
            ("right", Array.Empty<object>()));
        Assert.True(environment.GetProperty("ok").GetBoolean(), environment.ToString());
        Assert.StartsWith($"Draft now has {environment.GetProperty("result").GetProperty("operationCount").GetInt32()} operations.",
            environment.GetProperty("next").GetString(), StringComparison.Ordinal);
        var environmentCreate = Assert.Single(environment.GetProperty("result").GetProperty("operations").EnumerateArray(),
            operation => operation.GetProperty("kind").GetString()!.EndsWith("/createEnvironments", StringComparison.Ordinal));
        var environmentId = environmentCreate.GetProperty("entityId").GetString()!;
        var prefix = CanonicalId.FromGuid(MoMorphTypeTags.kguidMorphPrefix).Value;

        var entry = await session.CallAsync("motif_add_lexical_entry", ("draft", "affix-authoring"),
            ("form", "m"), ("formWs", "fr"), ("morphType", prefix), ("gloss", "class 1/2"),
            ("analysisWs", "en"), ("msa", new { kind = "inflectional", category }),
            ("inflectionClasses", new[] { classId }), ("environments", new[] { environmentId }));
        Assert.True(entry.GetProperty("ok").GetBoolean(), entry.ToString());
        var entryOperations = entry.GetProperty("result").GetProperty("operations").EnumerateArray().ToArray();
        Assert.Contains(entryOperations, operation =>
            operation.GetProperty("kind").GetString() == "grammar/moAffixForm/addRefInflectionClasses");
        Assert.Contains(entryOperations, operation =>
            operation.GetProperty("kind").GetString() == "grammar/moAffixAllomorph/addRefPhoneEnv");
        var entryCreate = Assert.Single(entryOperations, operation =>
            operation.GetProperty("kind").GetString() == "lexical/lexDb/createEntries");
        var entryId = entryCreate.GetProperty("entityId").GetString()!;

        var alternate = await session.CallAsync("motif_add_alternate_form", ("draft", "affix-authoring"),
            ("entry", "entry:" + entryId), ("form", "mw"), ("formWs", "fr"), ("morphType", prefix),
            ("inflectionClasses", new[] { classId }), ("environments", new[] { environmentId }));
        Assert.True(alternate.GetProperty("ok").GetBoolean(), alternate.ToString());
        var alternateOperations = alternate.GetProperty("result").GetProperty("operations").EnumerateArray().ToArray();
        Assert.Contains(alternateOperations, operation =>
            operation.GetProperty("kind").GetString() == "lexical/lexEntry/createAlternateForms");
        Assert.Contains(alternateOperations, operation =>
            operation.GetProperty("kind").GetString() == "grammar/moAffixForm/addRefInflectionClasses");
        Assert.Contains(alternateOperations, operation =>
            operation.GetProperty("kind").GetString() == "grammar/moAffixAllomorph/addRefPhoneEnv");
        Assert.Equal(projectHash, HashProject(project.FwDataPath));
    }

    [Fact]
    public async Task DerivationalRestrictionToolSetsTheClassOnAStagedDerivationalEntryAndAlternateFormClassesAreRefused()
    {
        using var project = new WalkthroughProject(pristine);
        await using var session = await Session.StartAsync(project.FwDataPath,
            new McpLaunchOptions(Profile: "phonology"));
        await session.CallAsync("motif_start_proposal", ("draft", "derivational"),
            ("label", "Restrict a derivational prefix"), ("rationale", "Only marked stems take it."));
        var category = CanonicalId.FromGuid(project.Seed.PartOfSpeechId).Value;
        var inflectionClass = await session.CallAsync("motif_add_inflection_class", ("draft", "derivational"),
            ("category", category), ("name", "1/2"), ("ws", "en"));
        var classId = Assert.Single(inflectionClass.GetProperty("result").GetProperty("operations").EnumerateArray(),
            operation => operation.GetProperty("kind").GetString() == "grammar/partOfSpeech/createInflectionClasses")
            .GetProperty("entityId").GetString()!;
        var derived = await session.CallAsync("motif_add_part_of_speech", ("draft", "derivational"),
            ("name", "Derived"), ("ws", "en"));
        var derivedId = derived.GetProperty("result").GetProperty("operations").EnumerateArray()
            .First(operation => operation.TryGetProperty("entityId", out _)).GetProperty("entityId").GetString()!;
        var prefix = CanonicalId.FromGuid(MoMorphTypeTags.kguidMorphPrefix).Value;
        var entry = await session.CallAsync("motif_add_lexical_entry", ("draft", "derivational"),
            ("form", "pag"), ("formWs", "fr"), ("morphType", prefix), ("gloss", "agentive"), ("analysisWs", "en"),
            ("msa", new { kind = "derivational", category, toCategory = derivedId }));
        Assert.True(entry.GetProperty("ok").GetBoolean(), entry.ToString());
        var entryOperations = entry.GetProperty("result").GetProperty("operations").EnumerateArray().ToArray();
        var msaId = Assert.Single(entryOperations, operation =>
            operation.GetProperty("kind").GetString() == "lexical/lexEntry/createMorphoSyntaxAnalyses")
            .GetProperty("entityId").GetString()!;
        var entryId = Assert.Single(entryOperations, operation =>
            operation.GetProperty("kind").GetString() == "lexical/lexDb/createEntries").GetProperty("entityId").GetString()!;

        var restricted = await session.CallAsync("motif_edit_derivational_affix_restriction", ("draft", "derivational"),
            ("msa", msaId), ("fromInflectionClass", classId));

        Assert.True(restricted.GetProperty("ok").GetBoolean(), restricted.ToString());
        Assert.Contains(restricted.GetProperty("result").GetProperty("operations").EnumerateArray(), operation =>
            operation.GetProperty("kind").GetString() == "grammar/moDerivAffMsa/setFromInflectionClass");
        var refused = await session.Client.CallToolAsync("motif_add_alternate_form", new Dictionary<string, object?>
        {
            ["project"] = project.FwDataPath, ["draft"] = "derivational", ["entry"] = "entry:" + entryId,
            ["form"] = "mag", ["formWs"] = "fr", ["morphType"] = prefix, ["inflectionClasses"] = new[] { classId },
        });
        Assert.True(refused.IsError);
        Assert.Contains("compose-edit-derivational-affix-restriction", ((TextContentBlock)refused.Content[0]).Text);
    }

    [Fact]
    public async Task KnownProjectSelectorsKeepDraftsInTheirOwnProjectsInOneSession()
    {
        using var first = new WalkthroughProject(pristine);
        using var second = new WalkthroughProject(pristine);
        await using var session = await Session.StartAsync(first.FwDataPath, new McpLaunchOptions());
        KnownProjectTestSetup.AssignUniqueTag(RunnerOptions.ResolveRoot(), second.FwDataPath, "second");

        var firstDraft = await session.CallAsync("motif_start_proposal", ("draft", "shared-name"),
            ("label", "First project"), ("rationale", "One linguistic claim."));
        var secondDraft = await session.CallAsync("motif_start_proposal", ("project", second.FwDataPath),
            ("draft", "shared-name"), ("label", "Second project"), ("rationale", "The related claim in the first project."));
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
    public async Task DefaultMcpToolsAuthorStructuredLexicalAndGrammarIntentsThroughFinalizeAndDryRun()
    {
        using var project = new WalkthroughProject(pristine);
        Assert.True(BaselineCaptureCommand.Capture(
            new BaselineCaptureRequest(project.FwDataPath), project.ManagedRoot).Succeeded);
        await using var runner = new InProcessRunnerLauncher(
            new JobRunnerLaunchOptions(project.ManagedRoot, FakeParser.ExecutablePath));
        await using var session = await Session.StartAsync(project.FwDataPath, new McpLaunchOptions(Runner: runner));

        var listed = await session.Client.ListToolsAsync();
        Assert.All(new[]
        {
            "motif_add_part_of_speech",
            "motif_add_lexical_entry",
            "motif_compose_batch",
        }, name => Assert.Contains(listed, tool => tool.Name == name));

        var started = await session.CallAsync("motif_start_proposal", ("draft", "author-grammar"),
            ("label", "Add a noun and a short sound inventory"),
            ("rationale", "The synthetic grammar needs one new noun entry."));
        var proposalId = started.GetProperty("result").GetProperty("proposalId").GetString()!;

        var morphTypes = await session.CallAsync("motif_grammar", ("kind", "morph_types"), ("detail", "detailed"));
        var stemTypeId = morphTypes.GetProperty("result").GetProperty("items").EnumerateArray()
            .Single(item => string.Equals(item.GetProperty("name").GetString(), "stem", StringComparison.OrdinalIgnoreCase))
            .GetProperty("id").GetString()!;

        var partOfSpeech = await session.CallAsync("motif_add_part_of_speech", ("draft", "author-grammar"),
            ("name", "McpNoun"), ("ws", "en"), ("abbreviation", "mcpn"));
        var categoryId = partOfSpeech.GetProperty("result").GetProperty("operations").EnumerateArray()
            .First(operation => operation.TryGetProperty("entityId", out _))
            .GetProperty("entityId").GetString()!;

        var affixSlot = await session.CallAsync("motif_add_affix_slot", ("draft", "author-grammar"),
            ("category", categoryId), ("name", "McpSuffixSlot"), ("ws", "en"), ("optional", false));
        var slotId = affixSlot.GetProperty("result").GetProperty("operations").EnumerateArray()
            .First(operation => operation.TryGetProperty("entityId", out _))
            .GetProperty("entityId").GetString()!;

        var entry = await session.CallAsync("motif_add_lexical_entry", ("draft", "author-grammar"),
            ("form", "mcpnoun"), ("formWs", "fr"), ("morphType", stemTypeId),
            ("gloss", "MCP noun"), ("analysisWs", "en"),
            ("msa", new { kind = "stem", category = categoryId }));
        Assert.True(entry.GetProperty("result").GetProperty("operationCount").GetInt32() > 0);
        var entryId = Assert.Single(entry.GetProperty("result").GetProperty("operations").EnumerateArray(),
            operation => operation.GetProperty("kind").GetString() == "lexical/lexDb/createEntries")
            .GetProperty("entityId").GetString()!;

        var batch = await session.CallAsync("motif_compose_batch", ("draft", "author-grammar"), ("intents", new object[]
        {
            new { verb = "compose-author-affix-template", intent = new
            {
                category = categoryId,
                name = "McpTemplate",
                ws = "en",
                prefixSlots = Array.Empty<string>(),
                suffixSlots = new[] { slotId },
                final = true,
            } },
            new { verb = "compose-author-phoneme", intent = new { name = "McpZ", representations = new[] { "z" } } },
            new { verb = "compose-author-phoneme", intent = new { name = "McpQ", representations = new[] { "q" } } },
            new { verb = "compose-author-alternate-form", intent = new
            {
                entry = "entry:" + entryId,
                form = "mcpnoun-alt",
                formWs = "fr",
                morphType = stemTypeId,
            } },
        }));
        var batchIntents = batch.GetProperty("result").GetProperty("intents");
        Assert.Equal(4, batchIntents.GetArrayLength());
        var alternateIntent = batchIntents.EnumerateArray()
            .Single(intent => intent.GetProperty("verb").GetString() == "compose-author-alternate-form");
        Assert.Contains(alternateIntent.GetProperty("operations").EnumerateArray(), operation =>
            operation.GetProperty("kind").GetString() == "lexical/lexEntry/createAlternateForms");
        Assert.True(batch.GetProperty("result").GetProperty("operationCount").GetInt32() > 0);

        var suffixTypeId = morphTypes.GetProperty("result").GetProperty("items").EnumerateArray()
            .Single(item => string.Equals(item.GetProperty("name").GetString(), "suffix", StringComparison.OrdinalIgnoreCase))
            .GetProperty("id").GetString()!;
        var affix = await session.CallAsync("motif_add_lexical_entry", ("draft", "author-grammar"),
            ("form", "mcp-sfx"), ("formWs", "fr"), ("morphType", suffixTypeId),
            ("gloss", "MCP suffix"), ("analysisWs", "en"),
            ("msa", new { kind = "inflectional", category = categoryId, slots = new[] { slotId } }));
        Assert.True(affix.GetProperty("result").GetProperty("operationCount").GetInt32() > 0);

        var dryRun = await session.CallAsync("motif_dry_run", ("proposal", proposalId), ("wait_seconds", 120));
        Assert.True(dryRun.GetProperty("ok").GetBoolean(), dryRun.ToString());
        Assert.True(dryRun.GetProperty("result").GetProperty("evidenceCurrent").GetBoolean());

        var finalized = await session.CallAsync("motif_finalize_proposal", ("draft", "author-grammar"));
        Assert.Equal(proposalId, finalized.GetProperty("result").GetProperty("proposalId").GetString());
    }

    [Fact]
    public async Task DefaultBatchRefusesValidDispositionIntentBeforeComposingEarlierAllowedIntent()
    {
        using var project = new WalkthroughProject(pristine);
        var savedDispositions = SeedSavedDispositions(project);
        var duplicateRules = CreateDuplicateAllomorphProhibitions(project);
        Assert.True(BaselineCaptureCommand.Capture(
            new BaselineCaptureRequest(project.FwDataPath), project.ManagedRoot).Succeeded);
        var projectHash = HashProject(project.FwDataPath);
        var parserPath = FakeParser.CopyRecordingInvocations(Path.Combine(project.ManagedRoot, "batch-disposition-parser"));
        var primary = project.Seed.FirstLexemeFormId.ToString("D").ToLowerInvariant();
        var other = project.Seed.SecondLexemeFormId.ToString("D").ToLowerInvariant();
        FakeParser.WriteFactsFixture(parserPath, new
        {
            prohibitions = new[]
            {
                Prohibition(duplicateRules[0].ToGuid().ToString("D").ToLowerInvariant(), primary, other),
                Prohibition(duplicateRules[1].ToGuid().ToString("D").ToLowerInvariant(), primary, other),
            },
        });
        await using var runner = new InProcessRunnerLauncher(
            new JobRunnerLaunchOptions(RunnerOptions.ResolveRoot(), parserPath));
        await using var experimental = await Session.StartAsync(project.FwDataPath,
            new McpLaunchOptions(Profile: "parsimony-experimental", Runner: runner));

        const string controlDraft = "batch-disposition-control";
        var controlStarted = await experimental.CallAsync("motif_start_proposal", ("draft", controlDraft),
            ("label", "Record one measured decision"), ("rationale", "Use the specialized disposition path."));
        var measure = await experimental.CallAsync("motif_run_parsimony_measure", ("measure_id", "P-adhoc-duplicate"),
            ("evidence_scope", "project-approved"), ("wait_seconds", 120));
        Assert.True(measure.GetProperty("ok").GetBoolean(), measure.ToString());
        var report = measure.GetProperty("result");
        var finding = Assert.Single(report.GetProperty("findings").EnumerateArray());
        var recordTypes = await experimental.CallAsync("motif_parsimony_record_types");
        var recordTypeId = Assert.Single(recordTypes.GetProperty("result").GetProperty("recordTypes")
            .EnumerateArray()).GetProperty("id").GetString()!;
        var disposed = await experimental.CallAsync("motif_dispose_parsimony_finding",
            ("report_id", report.GetProperty("reportId").GetString()),
            ("finding_id", finding.GetProperty("findingId").GetString()),
            ("disposition", "defer"), ("record_type_id", recordTypeId), ("draft", controlDraft),
            ("reason", "Confirm the observed prohibition identities first."));
        Assert.Equal("RecordParsimonyDisposition",
            disposed.GetProperty("result").GetProperty("composerName").GetString());
        var dispositionIntent = Assert.Single(AssertDraftComposers(project.FwDataPath, controlDraft,
            controlStarted.GetProperty("result").GetProperty("proposalId").GetString()!, ["RecordParsimonyDisposition"]));

        await using var session = await Session.StartAsync(project.FwDataPath, new McpLaunchOptions());
        const string safeDraft = "batch-disposition-safe-control";
        await session.CallAsync("motif_start_proposal", ("draft", safeDraft),
            ("label", "Stage an ordinary phoneme"), ("rationale", "Confirm the safe composer accepts this intent."));
        var safe = await session.CallAsync("motif_add_phoneme", ("draft", safeDraft),
            ("name", "T1SafeDisposition"), ("representations", new[] { "t1d" }));
        Assert.True(safe.GetProperty("result").GetProperty("operationCount").GetInt32() > 0);

        const string batchDraft = "batch-disposition-refusal";
        var started = await session.CallAsync("motif_start_proposal", ("draft", batchDraft),
            ("label", "Keep this Draft unchanged"), ("rationale", "A restricted intent must refuse atomically."));
        var before = ReadDraftState(project.FwDataPath, batchDraft);
        var result = await session.Client.CallToolAsync("motif_compose_batch", new Dictionary<string, object?>
        {
            ["project"] = project.FwDataPath,
            ["draft"] = batchDraft,
            ["intents"] = new object[]
            {
                new { verb = "compose-author-phoneme", intent = new { name = "T1BatchSafeDisposition", representations = new[] { "t1b" } } },
                new { verb = "compose-record-parsimony-disposition", intent = dispositionIntent },
            },
        });

        AssertTypedBatchRefusal(result);
        AssertDraftStateUnchanged(before, ReadDraftState(project.FwDataPath, batchDraft));
        Assert.NotNull(started.GetProperty("result").GetProperty("proposalId").GetString());
        AssertProjectUnchanged(project, projectHash, savedDispositions);
    }

    [Fact]
    public async Task DefaultBatchRefusesValidRetirementIntentBeforeComposingEarlierAllowedIntent()
    {
        using var project = new WalkthroughProject(pristine);
        var retirementIntent = AddDuplicateAffixRetirementIntent(project, pristine.Seed);
        await using var experimental = await Session.StartAsync(project.FwDataPath,
            new McpLaunchOptions(Profile: "parsimony-experimental"));

        const string controlDraft = "batch-retirement-control";
        await experimental.CallAsync("motif_start_proposal", ("draft", controlDraft),
            ("label", "Retire one duplicate alternate"), ("rationale", "Use the specialized retirement path."));
        var retired = await experimental.CallAsync("motif_retire_allomorph", ("draft", controlDraft),
            ("intent_json", retirementIntent));
        Assert.True(retired.GetProperty("ok").GetBoolean(), retired.ToString());
        Assert.True(retired.GetProperty("result").GetProperty("operationCount").GetInt32() > 0);

        await using var session = await Session.StartAsync(project.FwDataPath, new McpLaunchOptions());
        const string safeDraft = "batch-retirement-safe-control";
        await session.CallAsync("motif_start_proposal", ("draft", safeDraft),
            ("label", "Stage an ordinary phoneme"), ("rationale", "Confirm the safe composer accepts this intent."));
        var safe = await session.CallAsync("motif_add_phoneme", ("draft", safeDraft),
            ("name", "T1SafeRetirement"), ("representations", new[] { "t1r" }));
        Assert.True(safe.GetProperty("result").GetProperty("operationCount").GetInt32() > 0);

        const string batchDraft = "batch-retirement-refusal";
        await session.CallAsync("motif_start_proposal", ("draft", batchDraft),
            ("label", "Keep this Draft unchanged"), ("rationale", "A restricted intent must refuse atomically."));
        var before = ReadDraftState(project.FwDataPath, batchDraft);
        using var parsedRetirement = JsonDocument.Parse(retirementIntent);
        var result = await session.Client.CallToolAsync("motif_compose_batch", new Dictionary<string, object?>
        {
            ["project"] = project.FwDataPath,
            ["draft"] = batchDraft,
            ["intents"] = new object[]
            {
                new { verb = "compose-author-phoneme", intent = new { name = "T1BatchSafeRetirement", representations = new[] { "t1q" } } },
                new { verb = "retire-allomorph", intent = parsedRetirement.RootElement.Clone() },
            },
        });

        AssertTypedBatchRefusal(result);
        AssertDraftStateUnchanged(before, ReadDraftState(project.FwDataPath, batchDraft));
    }

    private static string ComposeProfile(string folder)
    {
        var path = Path.Combine(folder, "compose-profile.json");
        File.WriteAllText(path, """
            {"name":"compose-only","noInstructions":true,
             "tools":[{"tool":"motif_start_proposal"},{"tool":"motif_compose"}]}
            """);
        return path;
    }

    private static async Task<CallToolResult> ComposeSteps(Session session, string draft, params object[] steps) =>
        await session.Client.CallToolAsync("motif_compose", new Dictionary<string, object?>
        {
            ["project"] = session.ProjectPath,
            ["proposal"] = draft,
            ["steps"] = steps,
        });

    [Fact]
    public async Task ACompositionStepRunsARetirementThatTheDefaultBatchRefuses()
    {
        using var project = new WalkthroughProject(pristine);
        var retirementIntent = AddDuplicateAffixRetirementIntent(project, pristine.Seed);
        using var parsed = JsonDocument.Parse(retirementIntent);
        await using var session = await Session.StartAsync(project.FwDataPath,
            new McpLaunchOptions(Profile: ComposeProfile(project.ManagedRoot)));
        await session.CallAsync("motif_start_proposal", ("draft", "step-retirement"),
            ("label", "Retire one duplicate alternate"), ("rationale", "Compose it as a step."));

        var composed = await ComposeSteps(session, "step-retirement",
            new { verb = "retire-allomorph", intent = parsed.RootElement.Clone() });

        Assert.True(composed.IsError != true, ((TextContentBlock)composed.Content[0]).Text);
        var result = composed.StructuredContent!.Value.GetProperty("result");
        Assert.Equal("RetireAllomorph", result.GetProperty("intents")[0].GetProperty("composerName").GetString());
        Assert.True(result.GetProperty("operationCount").GetInt32() > 0);
    }

    [Fact]
    public async Task ACompositionStepRecordsAndReplacesADispositionAndRefusesTwoOfOneFindingInOneCall()
    {
        using var project = new WalkthroughProject(pristine);
        SeedSavedDispositions(project);
        var duplicateRules = CreateDuplicateAllomorphProhibitions(project);
        Assert.True(BaselineCaptureCommand.Capture(
            new BaselineCaptureRequest(project.FwDataPath), project.ManagedRoot).Succeeded);
        var parserPath = FakeParser.CopyRecordingInvocations(Path.Combine(project.ManagedRoot, "step-disposition-parser"));
        var primary = project.Seed.FirstLexemeFormId.ToString("D").ToLowerInvariant();
        var other = project.Seed.SecondLexemeFormId.ToString("D").ToLowerInvariant();
        FakeParser.WriteFactsFixture(parserPath, new
        {
            prohibitions = new[]
            {
                Prohibition(duplicateRules[0].ToGuid().ToString("D").ToLowerInvariant(), primary, other),
                Prohibition(duplicateRules[1].ToGuid().ToString("D").ToLowerInvariant(), primary, other),
            },
        });
        await using var runner = new InProcessRunnerLauncher(
            new JobRunnerLaunchOptions(RunnerOptions.ResolveRoot(), parserPath));
        JsonElement dispositionIntent;
        await using (var experimental = await Session.StartAsync(project.FwDataPath,
                         new McpLaunchOptions(Profile: "parsimony-experimental", Runner: runner)))
        {
            var started = await experimental.CallAsync("motif_start_proposal", ("draft", "step-disposition-source"),
                ("label", "Record one measured decision"), ("rationale", "Take its intent for a step."));
            var measure = await experimental.CallAsync("motif_run_parsimony_measure", ("measure_id", "P-adhoc-duplicate"),
                ("evidence_scope", "project-approved"), ("wait_seconds", 120));
            Assert.True(measure.GetProperty("ok").GetBoolean(), measure.ToString());
            var report = measure.GetProperty("result");
            var finding = Assert.Single(report.GetProperty("findings").EnumerateArray());
            var recordTypes = await experimental.CallAsync("motif_parsimony_record_types");
            var recordTypeId = Assert.Single(recordTypes.GetProperty("result").GetProperty("recordTypes")
                .EnumerateArray()).GetProperty("id").GetString()!;
            await experimental.CallAsync("motif_dispose_parsimony_finding",
                ("report_id", report.GetProperty("reportId").GetString()),
                ("finding_id", finding.GetProperty("findingId").GetString()),
                ("disposition", "defer"), ("record_type_id", recordTypeId), ("draft", "step-disposition-source"),
                ("reason", "Confirm the observed prohibition identities first."));
            dispositionIntent = Assert.Single(AssertDraftComposers(project.FwDataPath, "step-disposition-source",
                started.GetProperty("result").GetProperty("proposalId").GetString()!, ["RecordParsimonyDisposition"]));
        }

        await using var session = await Session.StartAsync(project.FwDataPath,
            new McpLaunchOptions(Profile: ComposeProfile(project.ManagedRoot)));
        await session.CallAsync("motif_start_proposal", ("draft", "step-disposition"),
            ("label", "Record a disposition as a step"), ("rationale", "A step may record one."));

        var recorded = await ComposeSteps(session, "step-disposition",
            new { verb = "compose-record-parsimony-disposition", intent = dispositionIntent });
        Assert.True(recorded.IsError != true, ((TextContentBlock)recorded.Content[0]).Text);
        Assert.Equal("RecordParsimonyDisposition", recorded.StructuredContent!.Value.GetProperty("result")
            .GetProperty("intents")[0].GetProperty("composerName").GetString());

        var revised = JsonNode.Parse(dispositionIntent.GetRawText())!.AsObject();
        revised["disposition"] = "fix";
        revised["reason"] = "The duplicate is a real error.";
        var before = ReadDraftState(project.FwDataPath, "step-disposition");
        var twice = await ComposeSteps(session, "step-disposition",
            new { verb = "compose-record-parsimony-disposition", intent = JsonNode.Parse(revised.ToJsonString()) },
            new { verb = "compose-record-parsimony-disposition", intent = JsonNode.Parse(revised.ToJsonString()) });
        Assert.True(twice.IsError);
        Assert.Contains("already records a disposition of the same finding", ((TextContentBlock)twice.Content[0]).Text);
        AssertDraftStateUnchanged(before, ReadDraftState(project.FwDataPath, "step-disposition"));

        var replaced = await ComposeSteps(session, "step-disposition",
            new { verb = "compose-record-parsimony-disposition", intent = JsonNode.Parse(revised.ToJsonString()) });
        Assert.True(replaced.IsError != true, ((TextContentBlock)replaced.Content[0]).Text);
        var only = Assert.Single(AssertDraftComposersOf(project.FwDataPath, "step-disposition"));
        Assert.Equal("fix", only.GetProperty("disposition").GetString());
    }

    [Fact]
    public async Task ACompositionStepReplacesADispositionTheFindingToolStagedInTheSameDraft()
    {
        using var project = new WalkthroughProject(pristine);
        SeedSavedDispositions(project);
        var duplicateRules = CreateDuplicateAllomorphProhibitions(project);
        Assert.True(BaselineCaptureCommand.Capture(
            new BaselineCaptureRequest(project.FwDataPath), project.ManagedRoot).Succeeded);
        var parserPath = FakeParser.CopyRecordingInvocations(Path.Combine(project.ManagedRoot, "mixed-disposition-parser"));
        var primary = project.Seed.FirstLexemeFormId.ToString("D").ToLowerInvariant();
        var other = project.Seed.SecondLexemeFormId.ToString("D").ToLowerInvariant();
        FakeParser.WriteFactsFixture(parserPath, new
        {
            prohibitions = new[]
            {
                Prohibition(duplicateRules[0].ToGuid().ToString("D").ToLowerInvariant(), primary, other),
                Prohibition(duplicateRules[1].ToGuid().ToString("D").ToLowerInvariant(), primary, other),
            },
        });
        await using var runner = new InProcessRunnerLauncher(
            new JobRunnerLaunchOptions(RunnerOptions.ResolveRoot(), parserPath));
        const string draft = "mixed-disposition";
        JsonElement staged;
        await using (var experimental = await Session.StartAsync(project.FwDataPath,
                         new McpLaunchOptions(Profile: "parsimony-experimental", Runner: runner)))
        {
            var started = await experimental.CallAsync("motif_start_proposal", ("draft", draft),
                ("label", "Record one finding twice"), ("rationale", "The step replaces the tool's record."));
            var measure = await experimental.CallAsync("motif_run_parsimony_measure", ("measure_id", "P-adhoc-duplicate"),
                ("evidence_scope", "project-approved"), ("wait_seconds", 120));
            Assert.True(measure.GetProperty("ok").GetBoolean(), measure.ToString());
            var report = measure.GetProperty("result");
            var finding = Assert.Single(report.GetProperty("findings").EnumerateArray());
            var recordTypes = await experimental.CallAsync("motif_parsimony_record_types");
            var recordTypeId = Assert.Single(recordTypes.GetProperty("result").GetProperty("recordTypes")
                .EnumerateArray()).GetProperty("id").GetString()!;
            await experimental.CallAsync("motif_dispose_parsimony_finding",
                ("report_id", report.GetProperty("reportId").GetString()),
                ("finding_id", finding.GetProperty("findingId").GetString()),
                ("disposition", "defer"), ("record_type_id", recordTypeId), ("draft", draft),
                ("reason", "Confirm the observed prohibition identities first."));
            staged = Assert.Single(AssertDraftComposers(project.FwDataPath, draft,
                started.GetProperty("result").GetProperty("proposalId").GetString()!, ["RecordParsimonyDisposition"]));
        }

        var revised = JsonNode.Parse(staged.GetRawText())!.AsObject();
        revised["disposition"] = "fix";
        revised["reason"] = "The duplicate is a real error.";
        await using var session = await Session.StartAsync(project.FwDataPath,
            new McpLaunchOptions(Profile: ComposeProfile(project.ManagedRoot)));

        var replaced = await ComposeSteps(session, draft,
            new { verb = "compose-record-parsimony-disposition", intent = JsonNode.Parse(revised.ToJsonString()) });

        Assert.True(replaced.IsError != true, ((TextContentBlock)replaced.Content[0]).Text);
        var only = Assert.Single(AssertDraftComposersOf(project.FwDataPath, draft));
        Assert.Equal("fix", only.GetProperty("disposition").GetString());
    }

    private static JsonElement[] AssertDraftComposersOf(string projectPath, string draftName)
    {
        var state = ReadDraftState(projectPath, draftName);
        using var json = JsonDocument.Parse(state.ProposalJson);
        var composers = json.RootElement.GetProperty("composerProvenance").EnumerateArray().ToArray();
        Assert.All(composers, item => Assert.Equal("RecordParsimonyDisposition", item.GetProperty("composer").GetString()));
        return composers.Select(item => item.GetProperty("input").Clone()).ToArray();
    }

    [Fact]
    public async Task AddFeatureValueToolComposesItsOperationsIntoTheDraftAndRefusesUnknownArguments()
    {
        using var project = new WalkthroughProject(pristine);
        var (featStruc, feature, value) = SeedFeatureStructureWithClosedFeature(project);
        await using var session = await Session.StartAsync(project.FwDataPath, new McpLaunchOptions());
        var started = await session.CallAsync("motif_start_proposal", ("draft", "feature-value"),
            ("label", "Add a feature value"), ("rationale", "The structure needs one specification."));
        var proposalId = started.GetProperty("result").GetProperty("proposalId").GetString()!;

        var added = await session.CallAsync("motif_add_feature_value", ("draft", "feature-value"),
            ("featStruc", featStruc), ("feature", feature), ("value", value));

        Assert.True(added.GetProperty("ok").GetBoolean(), added.ToString());
        Assert.Equal(3, added.GetProperty("result").GetProperty("operationCount").GetInt32());
        AssertDraftComposers(project.FwDataPath, "feature-value", proposalId, ["AuthorFeatureValue"]);
        var staged = JsonDocument.Parse(ReadDraftState(project.FwDataPath, "feature-value").ProposalJson).RootElement
            .GetProperty("operations").EnumerateArray().Select(operation => operation.GetProperty("kind").GetString());
        Assert.Equal(["grammar/fsFeatStruc/createFeatureSpecs", "grammar/fsFeatureSpecification/setFeature",
            "grammar/fsClosedValue/setValue"], staged);

        var before = ReadDraftState(project.FwDataPath, "feature-value");
        var refused = await session.Client.CallToolAsync("motif_add_feature_value", new Dictionary<string, object?>
        {
            ["project"] = project.FwDataPath,
            ["draft"] = "feature-value",
            ["featStruc"] = featStruc,
            ["feature"] = feature,
            ["notAField"] = "x",
        });
        Assert.True(refused.IsError);
        Assert.Equal("tool.unknown-argument", refused.StructuredContent!.Value.GetProperty("code").GetString());
        AssertDraftStateUnchanged(before, ReadDraftState(project.FwDataPath, "feature-value"));
    }

    private (string FeatStruc, string Feature, string Value) SeedFeatureStructureWithClosedFeature(
        WalkthroughProject project)
    {
        var loader = new FwDataProjectLoader();
        using var cache = loader.LoadCache(project.FwDataPath);
        var msa = (IMoStemMsa)cache.ServiceLocator.GetInstance<ILexSenseRepository>()
            .GetObject(pristine.Seed.FirstSenseId).MorphoSyntaxAnalysisRA;
        var featStrucGuid = Guid.NewGuid();
        Guid featureGuid = default, valueGuid = default;
        UndoableUnitOfWorkHelper.Do("test setup", "test setup", cache.ServiceLocator.GetInstance<IActionHandler>(), () =>
        {
            MoStemMsaMsFeaturesCreateLowering.Apply(cache, msa, featStrucGuid);
            var closed = cache.ServiceLocator.GetInstance<IFsClosedFeatureFactory>().Create();
            cache.LangProject.MsFeatureSystemOA.FeaturesOC.Add(closed);
            var symbolic = cache.ServiceLocator.GetInstance<IFsSymFeatValFactory>().Create();
            closed.ValuesOC.Add(symbolic);
            featureGuid = closed.Guid;
            valueGuid = symbolic.Guid;
        });
        loader.Save(cache);
        return (CanonicalId.FromGuid(featStrucGuid).Value, CanonicalId.FromGuid(featureGuid).Value,
            CanonicalId.FromGuid(valueGuid).Value);
    }

    [Fact]
    public async Task AnEmptyDraftCanBeWithdrawnThroughMcp()
    {
        using var project = new WalkthroughProject(pristine);
        await using var session = await Session.StartAsync(project.FwDataPath, new McpLaunchOptions());

        var started = await session.CallAsync("motif_start_proposal", ("draft", "empty-draft"),
            ("label", "An empty draft"), ("rationale", "No change was needed."));
        var proposalId = started.GetProperty("result").GetProperty("proposalId").GetString()!;

        var withdrawn = await session.CallAsync("motif_withdraw_proposal", ("proposal", proposalId));

        Assert.Equal("withdrawn", withdrawn.GetProperty("result").GetProperty("status").GetString());

        var reused = await session.CallAsync("motif_start_proposal", ("draft", "empty-draft"),
            ("label", "A replacement draft"), ("rationale", "The same short name is available again."));
        Assert.NotEqual(proposalId, reused.GetProperty("result").GetProperty("proposalId").GetString());
    }

    [Fact]
    public async Task ANoteTheLinguistLeftIsReportedOnEveryResultAboutTheProposalUntilTheNextRationale()
    {
        using var project = new WalkthroughProject(pristine);
        await using var session = await Session.StartAsync(project.FwDataPath, new McpLaunchOptions());
        var started = await session.CallAsync("motif_start_proposal", ("draft", "noted-draft"),
            ("label", "A noted draft"), ("rationale", "Why this draft exists."));
        var proposalId = started.GetProperty("result").GetProperty("proposalId").GetString()!;
        Assert.False(started.GetProperty("result").GetProperty("noteChanged").GetBoolean());

        var noted = SIL.Motif.Commands.ProposalTexts.SetNote(new SIL.Motif.Commands.Requests.SetNoteRequest(
            project.FwDataPath, SIL.Motif.Host.MotifProductVersion.CurrentText, proposalId, "Please explain the gloss."));
        Assert.True(noted.Succeeded, noted.Refusal?.Message);

        var shown = await session.CallAsync("motif_proposals", ("proposal", proposalId));
        var result = shown.GetProperty("result");
        Assert.True(result.GetProperty("noteChanged").GetBoolean());
        Assert.Equal("Please explain the gloss.", result.GetProperty("note").GetProperty("markdown").GetString());
        Assert.Contains("left a note", shown.GetProperty("next").GetString(), StringComparison.Ordinal);

        var batched = await session.CallAsync("motif_compose_batch", ("draft", "noted-draft"), ("intents", new object[]
        {
            new { verb = "compose-author-phoneme", intent = new { name = "NoteBatchPhoneme", representations = new[] { "nb" } } },
        }));
        Assert.True(batched.GetProperty("result").GetProperty("noteChanged").GetBoolean(), batched.ToString());

        var answered = await session.CallAsync("motif_set_rationale", ("proposal", proposalId),
            ("rationale", "The gloss is explained here."));
        Assert.False(answered.GetProperty("result").GetProperty("noteChanged").GetBoolean());
        Assert.False((await session.CallAsync("motif_proposals", ("proposal", proposalId)))
            .GetProperty("result").GetProperty("noteChanged").GetBoolean());
    }

    [Fact]
    public async Task TryWordParsesAWordWithADraftsChangesAndNamesTheDraftItTracedAgainst()
    {
        using var project = new WalkthroughProject(pristine);
        Assert.True(BaselineCaptureCommand.Capture(new BaselineCaptureRequest(project.FwDataPath), project.ManagedRoot).Succeeded);
        await using var runner = new InProcessRunnerLauncher(
            new JobRunnerLaunchOptions(project.ManagedRoot, FakeParser.ExecutablePath));
        await using var session = await Session.StartAsync(project.FwDataPath, new McpLaunchOptions(Runner: runner));
        var word = SeededProject.AnalysedWordForm;

        var started = await session.CallAsync("motif_start_proposal", ("draft", "try-draft"), ("label", "Try draft"),
            ("rationale", "Trace a word with the Draft's changes."));
        var proposalId = started.GetProperty("result").GetProperty("proposalId").GetString()!;
        var lexicon = await session.CallAsync("motif_lexicon", ("query", SeededProject.FirstForm));
        var sense = lexicon.GetProperty("result").GetProperty("entries")[0].GetProperty("senses")[0];
        await session.CallAsync("motif_set_gloss", ("draft", "try-draft"), ("sense", sense.GetProperty("id").GetString()!),
            ("ws", lexicon.GetProperty("result").GetProperty("glossWs").GetString()!), ("text", "traced gloss"));
        var trial = await session.CallAsync("motif_trial", ("proposal", proposalId), ("words", new[] { word }),
            ("wait_seconds", 120));
        Assert.True(trial.GetProperty("ok").GetBoolean(), trial.ToString());

        var baseline = (await session.CallAsync("motif_try_word", ("word", word))).GetProperty("result");
        Assert.False(baseline.GetProperty("parsed").GetBoolean(), baseline.ToString());
        Assert.Equal("baseline", baseline.GetProperty("tracedAgainst").GetString());

        // The Trial's copy is where the Draft's changes live; let the fake parser accept the word only there.
        var scratchRoot = SIL.Motif.Worker.Jobs.TrialScratchPath.ForJob(
            trial.GetProperty("result").GetProperty("job").GetString()!);
        using var scratchCleanup = new ScratchDirectoryCleanup(scratchRoot);
        var scratch = SIL.Motif.Worker.Jobs.TrialScratchPath.ProjectFile(scratchRoot);
        Assert.NotNull(scratch);
        FakeParser.Behave(Path.GetDirectoryName(scratch)!, FakeParser.TraceBehavior(word, "draft-sig", "DraftRule"));

        var traced = await session.CallAsync("motif_try_word", ("word", word), ("proposal", proposalId));
        Assert.True(traced.GetProperty("ok").GetBoolean(), traced.ToString());
        Assert.True(traced.GetProperty("result").GetProperty("parsed").GetBoolean(), traced.ToString());
        Assert.Contains(proposalId, traced.GetProperty("result").GetProperty("tracedAgainst").GetString());
        var still = (await session.CallAsync("motif_try_word", ("word", word))).GetProperty("result");
        Assert.False(still.GetProperty("parsed").GetBoolean());

        var concise = traced.GetProperty("result");
        var detailed = (await session.CallAsync("motif_try_word", ("word", word), ("proposal", proposalId),
            ("detail", "detailed"))).GetProperty("result");
        Assert.Contains(proposalId, detailed.GetProperty("tracedAgainst").GetString());
        Assert.True(detailed.GetProperty("parsed").GetBoolean());
        Assert.Equal(word, detailed.GetProperty("word").GetString());
        Assert.True(detailed.TryGetProperty("reading", out _), "detailed keeps the full response shape");
        Assert.False(concise.TryGetProperty("reading", out _));

        var unknown = await session.Client.CallToolAsync("motif_try_word", new Dictionary<string, object?>
        {
            ["project"] = project.FwDataPath, ["word"] = word, ["proposal"] = "AAAAAAAAAAAAAAAAAAAAAA",
        });
        Assert.True(unknown.IsError);
        var text = ((TextContentBlock)unknown.Content[0]).Text;
        Assert.Contains("trial.none", text);
        Assert.Contains("motif_trial", text);

        var malformed = await session.Client.CallToolAsync("motif_try_word", new Dictionary<string, object?>
        {
            ["project"] = project.FwDataPath, ["word"] = word, ["proposal"] = "proposal/does-not-exist",
        });
        Assert.True(malformed.IsError);
        var malformedText = ((TextContentBlock)malformed.Content[0]).Text;
        Assert.Contains("proposal.invalid-id", malformedText);
        Assert.Contains("is not a Proposal id", malformedText);
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
            ("rationale", "The seeded gloss reads awkwardly."));
        var proposalId = started.GetProperty("result").GetProperty("proposalId").GetString()!;

        var glossed = await session.CallAsync("motif_set_gloss", ("draft", "gloss-fix"),
            ("sense", sense.GetProperty("id").GetString()!), ("ws", lexicon.GetProperty("result").GetProperty("glossWs").GetString()!),
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
        var noted = SIL.Motif.Commands.ProposalTexts.SetNote(new SIL.Motif.Commands.Requests.SetNoteRequest(
            project.FwDataPath, SIL.Motif.Host.MotifProductVersion.CurrentText, proposalId, "Check the other gloss too."));
        Assert.True(noted.Succeeded, noted.Refusal?.Message);
        var dryRunAgain = await session.CallAsync("motif_dry_run", ("proposal", proposalId), ("wait_seconds", 120));
        Assert.True(dryRunAgain.GetProperty("result").GetProperty("noteChanged").GetBoolean(), dryRunAgain.ToString());
        Assert.Contains("left a note", dryRunAgain.GetProperty("next").GetString(), StringComparison.Ordinal);
        var trialAgain = await session.CallAsync("motif_trial", ("proposal", proposalId),
            ("words", new[] { SeededProject.AnalysedWordForm }), ("wait_seconds", 120));
        Assert.True(trialAgain.GetProperty("result").GetProperty("noteChanged").GetBoolean(), trialAgain.ToString());

        var difference = evidence.GetProperty("difference").GetString()!;
        var page = await session.CallAsync("motif_difference", ("difference", difference),
            ("category", "other-changes"), ("offset", 0), ("limit", 1));
        Assert.Equal(difference, page.GetProperty("result").GetProperty("difference").GetString());
        Assert.InRange(page.GetProperty("result").GetProperty("returned").GetInt32(), 0, 1);

        var operationId = glossed.GetProperty("result").GetProperty("operationId").GetString()!;
        await session.CallAsync("motif_remove_operations", ("draft", "gloss-fix"),
            ("operation_ids", new[] { operationId }));
        await session.CallAsync("motif_set_gloss", ("draft", "gloss-fix"),
            ("sense", sense.GetProperty("id").GetString()!), ("ws", lexicon.GetProperty("result").GetProperty("glossWs").GetString()!),
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
        var calls = result.GetProperty("calls");
        Assert.Equal(JsonValueKind.Object, calls.GetProperty("byTool").ValueKind);
        Assert.Equal(JsonValueKind.Object, calls.GetProperty("refusalsByCode").ValueKind);
        Assert.True(calls.GetProperty("followedNext").GetInt32() + calls.GetProperty("ignoredNext").GetInt32() >= 1);
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

    [Theory]
    [InlineData("motif_grammar")]
    [InlineData("motif_lexicon")]
    public async Task AReadBeforeAnyBaselineIsAToolErrorThatSaysHowToTakeOne(string tool)
    {
        using var project = new WalkthroughProject(pristine);
        await using var session = await Session.StartAsync(project.FwDataPath, new McpLaunchOptions());

        var result = await session.Client.CallToolAsync(tool, new Dictionary<string, object?> { ["project"] = project.FwDataPath });

        Assert.True(result.IsError);
        var text = ((TextContentBlock)result.Content[0]).Text;
        Assert.StartsWith("baseline.missing:", text);
        Assert.Contains("motif_capture_baseline", text);
    }

    [Fact]
    public async Task TheBuiltCliInitializesWithValidMcpFramingWhenModeIsOffAndExplainsTheEmptyToolList()
    {
        using var project = new WalkthroughProject(pristine);
        var preferencePath = Path.Combine(project.ManagedRoot, "advanced-ai-mode.json");
        new FileAdvancedAiModePreferenceStore(preferencePath).SetEnabled(false);
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            EnvironmentVariables = new Dictionary<string, string?>
            {
                [FileAdvancedAiModePreferenceStore.PathEnvironmentVariable] = preferencePath,
                [RunnerOptions.RootVariable] = project.ManagedRoot,
            },
            Name = "motif process regression with Advanced AI off",
            Command = BuildOutput.Cli,
            Arguments = ["mcp"],
            WorkingDirectory = project.ManagedRoot,
        });
        await using var client = await McpClient.CreateAsync(transport);

        Assert.Empty(await client.ListToolsAsync());
        var prompts = await client.ListPromptsAsync();
        var setupPrompt = Assert.Single(prompts);
        Assert.Equal("motif_advanced_ai_setup", setupPrompt.Name);
        Assert.Empty(setupPrompt.ProtocolPrompt.Arguments ?? []);
        var setup = await client.GetPromptAsync(setupPrompt.Name);
        Assert.Equal(setupPrompt.Description, setup.Description);
        var setupText = Assert.IsType<TextContentBlock>(Assert.Single(setup.Messages).Content).Text;
        Assert.Contains("motif settings advanced-ai on", setupText, StringComparison.Ordinal);
        Assert.Contains(FileAdvancedAiModePreferenceStore.PathEnvironmentVariable, setupText, StringComparison.Ordinal);
        Assert.Contains("restart the Motif MCP server and reconnect the assistant", setupText,
            StringComparison.Ordinal);
        Assert.Contains("does not read or change any project or setting", setupText, StringComparison.Ordinal);
        Assert.Equal(FileAdvancedAiModePreferenceStore.DisabledServerInstructions, client.ServerInstructions);
        var refused = await client.CallToolAsync("motif_grammar", new Dictionary<string, object?>());
        Assert.True(refused.IsError);
        Assert.Equal("advanced-ai.disabled", refused.StructuredContent!.Value.GetProperty("code").GetString());
        Assert.Contains(FileAdvancedAiModePreferenceStore.PathEnvironmentVariable,
            ((TextContentBlock)refused.Content[0]).Text, StringComparison.Ordinal);
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
        Assert.NotEmpty(tools);
        Assert.Contains(tools, tool => tool.Name == "motif_grammar");
        Assert.Empty(await client.ListPromptsAsync());
        Assert.DoesNotContain("Advanced AI mode is OFF", client.ServerInstructions ?? string.Empty,
            StringComparison.Ordinal);

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

        var all = await session.CallAsync("motif_run_parsimony_measure", ("measure_id", "all"),
            ("evidence_scope", "project-approved"), ("wait_seconds", 120));
        Assert.True(all.GetProperty("ok").GetBoolean(), all.ToString());
        var allReports = all.GetProperty("result").GetProperty("reports").EnumerateArray().ToArray();
        Assert.Equal(SIL.Motif.Host.Parsimony.ParsimonyAdmission.SupportedMeasureIds().Count, allReports.Length);
        Assert.Single(allReports.Select(item => item.GetProperty("inputs").GetProperty("bundleId").GetString())
            .Distinct());

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
            ("label", "Make one affix slot optional"), ("rationale", "This is a Draft-only check."));
        var slotEdit = await session.CallAsync("motif_edit_affix_slot", ("draft", "slot-edit"),
            ("target", slotId.Value), ("expected_optional", false), ("optional", true));
        var slotOperation = Assert.Single(slotEdit.GetProperty("result").GetProperty("operations").EnumerateArray());
        Assert.Equal("grammar/moInflAffixSlot/setOptional", slotOperation.GetProperty("kind").GetString());
        Assert.Equal(1, slotEdit.GetProperty("result").GetProperty("operationCount").GetInt32());

        await session.CallAsync("motif_start_proposal", ("draft", "slot-create"),
            ("label", "Add a category-owned slot"), ("rationale", "The affix needs a new position class."));
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
            ("label", "Disable one duplicate rule"), ("rationale", "This is a Draft-only check."));
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
    public async Task ExperimentalDispositionToolsStageMeasuredChangesWithoutApplyingThem()
    {
        using var project = new WalkthroughProject(pristine);
        var savedDispositions = SeedSavedDispositions(project);
        var duplicateRules = CreateDuplicateAllomorphProhibitions(project);
        Assert.True(BaselineCaptureCommand.Capture(
            new BaselineCaptureRequest(project.FwDataPath), project.ManagedRoot).Succeeded);
        var parserPath = FakeParser.CopyRecordingInvocations(Path.Combine(project.ManagedRoot, "disposition-parser"));
        var primary = project.Seed.FirstLexemeFormId.ToString("D").ToLowerInvariant();
        var other = project.Seed.SecondLexemeFormId.ToString("D").ToLowerInvariant();
        FakeParser.WriteFactsFixture(parserPath, new
        {
            prohibitions = new[]
            {
                Prohibition(duplicateRules[0].ToGuid().ToString("D").ToLowerInvariant(), primary, other),
                Prohibition(duplicateRules[1].ToGuid().ToString("D").ToLowerInvariant(), primary, other),
            },
        });
        await using var runner = new InProcessRunnerLauncher(
            new JobRunnerLaunchOptions(RunnerOptions.ResolveRoot(), parserPath));
        await using var session = await Session.StartAsync(project.FwDataPath, new McpLaunchOptions(
            Profile: "parsimony-experimental", Runner: runner));
        var projectHash = HashProject(project.FwDataPath);

        const string draftName = "mcp-disposition-round-trip";
        var started = await session.CallAsync("motif_start_proposal", ("draft", draftName),
            ("label", "Review measured dispositions"), ("rationale", "Keep these choices in a Draft."));
        var proposalId = started.GetProperty("result").GetProperty("proposalId").GetString()!;
        AssertDraftComposers(project.FwDataPath, draftName, proposalId, []);
        AssertProjectUnchanged(project, projectHash, savedDispositions);

        var measure = await session.CallAsync("motif_run_parsimony_measure", ("measure_id", "P-adhoc-duplicate"),
            ("evidence_scope", "project-approved"), ("wait_seconds", 120));
        Assert.True(measure.GetProperty("ok").GetBoolean(), measure.ToString());
        var report = measure.GetProperty("result");
        var finding = Assert.Single(report.GetProperty("findings").EnumerateArray());
        var findingId = finding.GetProperty("findingId").GetString()!;
        var reportId = report.GetProperty("reportId").GetString()!;
        AssertDraftComposers(project.FwDataPath, draftName, proposalId, []);
        AssertProjectUnchanged(project, projectHash, savedDispositions);

        var recordTypes = await session.CallAsync("motif_parsimony_record_types");
        var recordTypeId = Assert.Single(recordTypes.GetProperty("result").GetProperty("recordTypes")
            .EnumerateArray()).GetProperty("id").GetString()!;
        var disposed = await session.CallAsync("motif_dispose_parsimony_finding",
            ("report_id", reportId), ("finding_id", findingId), ("disposition", "defer"),
            ("record_type_id", recordTypeId), ("draft", draftName), ("reason", "Compare the exact rules first."));
        Assert.Equal("RecordParsimonyDisposition", disposed.GetProperty("result").GetProperty("composerName").GetString());
        var afterDispose = AssertDraftComposers(project.FwDataPath, draftName, proposalId,
            ["RecordParsimonyDisposition"]);
        var disposalInput = afterDispose[0];
        Assert.Equal("defer", disposalInput.GetProperty("disposition").GetString());
        var measuredObjectId = CanonicalId.FromGuid(Guid.Parse(findingId["P-adhoc-duplicate:".Length..])).Value;
        Assert.Equal(measuredObjectId,
            disposalInput.GetProperty("subject").GetProperty("object").GetProperty("id").GetString());
        AssertProjectUnchanged(project, projectHash, savedDispositions);

        var revised = await session.CallAsync("motif_revise_parsimony_disposition", ("draft", draftName),
            ("recordId", savedDispositions.ToRevise.RecordId),
            ("expectedHeads", new[] { new
            {
                revisionId = savedDispositions.ToRevise.Head.RevisionId,
                contentDigest = savedDispositions.ToRevise.Head.ContentDigest,
            } }),
            ("disposition", "defer"), ("reason", "The evidence needs another check."));
        Assert.Equal("ReviseParsimonyDisposition", revised.GetProperty("result").GetProperty("composerName").GetString());
        var afterRevision = AssertDraftComposers(project.FwDataPath, draftName, proposalId,
            ["RecordParsimonyDisposition", "ReviseParsimonyDisposition"]);
        var revisionInput = afterRevision[1];
        Assert.Equal(savedDispositions.ToRevise.RecordId, revisionInput.GetProperty("recordId").GetString());
        Assert.Equal("defer", revisionInput.GetProperty("disposition").GetString());
        Assert.Equal("The evidence needs another check.", revisionInput.GetProperty("reason").GetString());
        AssertProjectUnchanged(project, projectHash, savedDispositions);

        var retracted = await session.CallAsync("motif_retract_parsimony_disposition", ("draft", draftName),
            ("recordId", savedDispositions.ToRetract.RecordId),
            ("expectedHeads", new[] { new
            {
                revisionId = savedDispositions.ToRetract.Head.RevisionId,
                contentDigest = savedDispositions.ToRetract.Head.ContentDigest,
            } }));
        Assert.Equal("RetractParsimonyDisposition", retracted.GetProperty("result").GetProperty("composerName").GetString());
        var afterRetraction = AssertDraftComposers(project.FwDataPath, draftName, proposalId,
            ["RecordParsimonyDisposition", "ReviseParsimonyDisposition", "RetractParsimonyDisposition"]);
        Assert.Equal(savedDispositions.ToRetract.RecordId, afterRetraction[2].GetProperty("recordId").GetString());
        Assert.Equal(savedDispositions.ToRetract.Head.RevisionId,
            afterRetraction[2].GetProperty("expectedHeads")[0].GetProperty("revisionId").GetString());
        AssertProjectUnchanged(project, projectHash, savedDispositions);
    }

    [Fact]
    public async Task PhonologyToolsUseDraftReferencesAndReturnActionableRefusals()
    {
        using var project = new WalkthroughProject(pristine);
        await using var session = await Session.StartAsync(project.FwDataPath, new McpLaunchOptions(Profile: "phonology"));
        await session.CallAsync("motif_start_proposal", ("draft", "sounds"), ("label", "Add vowel context"),
            ("rationale", "Synthetic phonological contrast."));
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

    [Fact]
    public async Task AffixProcessToolResolvesEarlierStagedReferencesAndPersistsTypedOperations()
    {
        using var project = new WalkthroughProject(pristine);
        await using var session = await Session.StartAsync(project.FwDataPath,
            new McpLaunchOptions(Profile: "phonology"));
        await session.CallAsync("motif_start_proposal", ("draft", "process"),
            ("label", "Add a copying affix"), ("rationale", "The process repeats the matched stem."));
        var phoneme = await session.CallAsync("motif_add_phoneme", ("draft", "process"),
            ("name", "process-qh"), ("representations", new[] { "qh" }));
        var phonemeId = phoneme.GetProperty("result").GetProperty("operations").EnumerateArray()
            .Single(operation => operation.GetProperty("kind").GetString()!.EndsWith("/createPhonemes", StringComparison.Ordinal))
            .GetProperty("entityId").GetString()!;
        var naturalClass = await session.CallAsync("motif_add_natural_class", ("draft", "process"),
            ("name", "process vowels"), ("abbreviation", "PV"), ("members", new[] { phonemeId }));
        var naturalClassId = naturalClass.GetProperty("result").GetProperty("operations").EnumerateArray()
            .Single(operation => operation.GetProperty("kind").GetString() == "grammar/phPhonData/createNaturalClasses")
            .GetProperty("entityId").GetString()!;

        var authored = await session.CallAsync("motif_add_affix_process", ("draft", "process"),
            ("morphType", CanonicalId.FromGuid(MoMorphTypeTags.kguidMorphPrefix).Value),
            ("gloss", "repeat"), ("analysisWs", "en"),
            ("msa", new { kind = "unclassified", category = CanonicalId.FromGuid(project.Seed.PartOfSpeechId).Value }),
            ("input", new object[]
            {
                new { name = "vowel", naturalClass = naturalClassId },
                new { name = "stem", remaining = true },
            }),
            ("output", new object[]
            {
                new { copy = "vowel" }, new { copy = "stem" },
                new { insert = new[] { phonemeId } }, new { copy = "stem" },
            }));

        Assert.True(authored.GetProperty("ok").GetBoolean(), authored.ToString());
        var reportedOperations = authored.GetProperty("result").GetProperty("operations").EnumerateArray().ToArray();
        Assert.Contains(reportedOperations, operation =>
            operation.GetProperty("kind").GetString() == "lexical/lexEntry/createLexemeForm");
        Assert.Equal(4, reportedOperations.Count(operation =>
            operation.GetProperty("kind").GetString() == "grammar/moAffixProcess/createOutput"));

        var entryId = reportedOperations.Single(operation =>
            operation.GetProperty("kind").GetString() == "lexical/lexDb/createEntries").GetProperty("entityId").GetString()!;
        var allomorph = await session.CallAsync("motif_add_affix_process_allomorph", ("draft", "process"),
            ("entry", entryId), ("input", new object[] { new { name = "stem", remaining = true } }),
            ("output", new object[] { new { insert = new[] { phonemeId } }, new { copy = "stem" } }));
        Assert.True(allomorph.GetProperty("ok").GetBoolean(), allomorph.ToString());

        using var database = ProjectMotifDatabase.Open(project.FwDataPath);
        var draft = ProposalCommands.DeserializeDraft(new ProposalRepository(database).GetDraft("process").ProposalJson!);
        Assert.Contains(draft.ComposerProvenance, item =>
            item.GetProperty("composer").GetString() == "AuthorAffixProcess");
        Assert.Contains(draft.ComposerProvenance, item =>
            item.GetProperty("composer").GetString() == "AuthorAffixProcessAllomorph");
        Assert.Contains(draft.Operations, operation => operation.Kind == "lexical/lexEntry/createAlternateForms" &&
            operation.After["class"].GetString() == "MoAffixProcess");
        Assert.Contains(draft.Operations, operation => operation.Kind == "lexical/lexEntry/createLexemeForm" &&
            operation.After["class"].GetString() == "MoAffixProcess");
        Assert.Contains(draft.Operations, operation => operation.Kind == "grammar/moInsertPhones/addRefContent" &&
            operation.After.Values.Any(value => value.ValueKind == JsonValueKind.String && value.GetString() == phonemeId));
        Assert.Contains(draft.Operations, operation => operation.Kind == "grammar/phSimpleContextNC/setFeatureStructure" &&
            operation.After.Values.Any(value => value.ValueKind == JsonValueKind.String && value.GetString() == naturalClassId));
    }

    [Fact]
    public async Task EditPhonemeToolStagesADigraphOnAnEarlierPhonemeAndNeverApplies()
    {
        using var project = new WalkthroughProject(pristine);
        await using var session = await Session.StartAsync(project.FwDataPath,
            new McpLaunchOptions(Profile: "phonology"));
        await session.CallAsync("motif_start_proposal", ("draft", "digraphs"),
            ("label", "Add ng"), ("rationale", "The language writes the velar nasal as ng."));
        var phoneme = await session.CallAsync("motif_add_phoneme", ("draft", "digraphs"),
            ("name", "q"), ("representations", new[] { "q" }));
        Assert.True(phoneme.GetProperty("ok").GetBoolean(), phoneme.ToString());
        var phonemeId = phoneme.GetProperty("result").GetProperty("operations").EnumerateArray()
            .Single(operation => operation.GetProperty("kind").GetString()!.EndsWith("/createPhonemes", StringComparison.Ordinal))
            .GetProperty("entityId").GetString()!;

        var edited = await session.CallAsync("motif_edit_phoneme", ("draft", "digraphs"),
            ("target", phonemeId), ("add", new object[] { new { representation = "qh" } }));

        Assert.True(edited.GetProperty("ok").GetBoolean(), edited.ToString());
        using var database = ProjectMotifDatabase.Open(project.FwDataPath);
        var draft = ProposalCommands.DeserializeDraft(new ProposalRepository(database).GetDraft("digraphs").ProposalJson!);
        Assert.Contains(draft.ComposerProvenance, item => item.GetProperty("composer").GetString() == "EditPhoneme");
        Assert.Contains(draft.Operations, operation => operation.Kind == "grammar/phCode/setRepresentation" &&
            operation.After["text"].GetString() == "qh");
    }

    [Fact]
    public async Task AllomorphConditionToolAcceptsItsCamelCaseIntentFields()
    {
        using var project = new WalkthroughProject(pristine);
        await using var session = await Session.StartAsync(project.FwDataPath,
            new McpLaunchOptions(Profile: "parsimony-experimental"));
        await session.CallAsync("motif_start_proposal", ("draft", "condition"),
            ("label", "Restrict one allomorph"), ("rationale", "The environment identifies its use."));

        var result = await session.CallAsync("motif_edit_allomorph_condition", ("draft", "condition"),
            ("target", CanonicalId.FromGuid(pristine.Seed.FirstLexemeFormId).Value), ("field", "phoneEnv"),
            ("expectedEnvironments", Array.Empty<string>()), ("environments", Array.Empty<string>()));

        Assert.True(result.GetProperty("ok").GetBoolean(), result.ToString());
        Assert.Equal(0, result.GetProperty("result").GetProperty("operationCount").GetInt32());
    }

    [Fact]
    public async Task AllomorphOrderToolAcceptsItsCamelCaseIntentFields()
    {
        using var project = new WalkthroughProject(pristine);
        var expectedAlternates = AddAlternateForms(project, pristine.Seed, count: 2);
        await using var session = await Session.StartAsync(project.FwDataPath,
            new McpLaunchOptions(Profile: "parsimony-experimental"));
        await session.CallAsync("motif_start_proposal", ("draft", "order"),
            ("label", "Order alternate forms"), ("rationale", "The first form is preferred."));

        var result = await session.CallAsync("motif_order_allomorphs", ("draft", "order"),
            ("target", CanonicalId.FromGuid(pristine.Seed.FirstEntryId).Value),
            ("expectedAlternates", expectedAlternates), ("alternates", expectedAlternates.Reverse().ToArray()));

        Assert.True(result.GetProperty("ok").GetBoolean(), result.ToString());
        Assert.True(result.GetProperty("result").GetProperty("operationCount").GetInt32() > 0);
    }

    private sealed class ScratchDirectoryCleanup(string path) : IDisposable
    {
        public void Dispose()
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
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

        public string ProjectPath => _project;

        public static async Task<Session> StartAsync(string project, McpLaunchOptions options)
        {
            Assert.True(ProposalCommands.Open(new OpenRequest(project)).Succeeded);
            KnownProjectTestSetup.AssignUniqueTag(RunnerOptions.ResolveRoot(), project,
                Path.GetFileNameWithoutExtension(project));
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

    private static string[] AddAlternateForms(WalkthroughProject project, SeededProject seed, int count)
    {
        var loader = new FwDataProjectLoader();
        using var cache = loader.LoadCache(project.FwDataPath);
        var entry = cache.ServiceLocator.GetInstance<ILexEntryRepository>().GetObject(seed.FirstEntryId);
        var formFactory = cache.ServiceLocator.GetInstance<IMoStemAllomorphFactory>();
        var ids = new List<string>(count);
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            for (var index = 0; index < count; index++)
            {
                var form = formFactory.Create();
                entry.AlternateFormsOS.Add(form);
                form.MorphTypeRA = entry.LexemeFormOA!.MorphTypeRA;
                form.Form.set_String(cache.DefaultVernWs, $"motifa{index + 1}");
                ids.Add(CanonicalId.FromGuid(form.Guid).Value);
            }
        });
        loader.Save(cache);
        return ids.ToArray();
    }

    private static SavedDispositionPair SeedSavedDispositions(WalkthroughProject project)
    {
        using var cache = new FwDataProjectLoader().LoadCache(project.FwDataPath);
        var fieldId = NotebookJudgmentFixture.InitializeReservedField(cache);
        var notebook = cache.LangProject.ResearchNotebookOA;
        var recordTypes = notebook.RecTypesOA;
        ICmPossibility recordType = null!;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            recordTypes ??= cache.ServiceLocator.GetInstance<ICmPossibilityListFactory>().Create();
            notebook.RecTypesOA = recordTypes;
            recordType = cache.ServiceLocator.GetInstance<ICmPossibilityFactory>().Create();
            recordTypes.PossibilitiesOS.Add(recordType);
            recordType.Name.set_String(cache.DefaultAnalWs, TsStringUtils.MakeString("Motif disposition", cache.DefaultAnalWs));
        });
        var projectId = CanonicalId.FromGuid(cache.LangProject.Guid).Value;
        var values = new[]
        {
            CreateSavedDisposition(projectId, ParsimonyDispositionKind.Keep, "Existing choice to revise."),
            CreateSavedDisposition(projectId, ParsimonyDispositionKind.Fix, "Existing choice to retract."),
        };
        foreach (var judgment in values)
        {
            var record = NotebookJudgmentFixture.AddRecord(cache,
                CanonicalId.Parse(judgment.RevisionId).ToGuid(), fieldId, HumanJudgmentCodec.Format(judgment));
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () => record.TypeRA = recordType);
        }
        new FwDataProjectLoader().Save(cache);
        return new SavedDispositionPair(ToSavedHead(values[0]), ToSavedHead(values[1]));
    }

    private static HumanJudgment CreateSavedDisposition(string projectId, ParsimonyDispositionKind disposition,
        string reason)
    {
        var revisionId = CanonicalId.FromGuid(Guid.NewGuid()).Value;
        return new HumanJudgment(projectId, revisionId, revisionId, [],
            new DispositionJudgment(new ProjectJudgmentSubject(projectId), "P-adhoc-duplicate", disposition,
                "sha256:" + new string('a', 64), "p-adhoc-duplicate/v1", "the FieldWorks project", "Duplicate rules"),
            Reason: reason, Actor: new JudgmentActor(JudgmentActorKind.Agent));
    }

    private static SavedDisposition ToSavedHead(HumanJudgment judgment) => new(judgment.RevisionId,
        new JudgmentPredecessor(judgment.RevisionId, HumanJudgmentCodec.LogicalDigest(judgment)));

    private static CanonicalId[] CreateDuplicateAllomorphProhibitions(WalkthroughProject project)
    {
        using var cache = new FwDataProjectLoader().LoadCache(project.FwDataPath);
        var prohibitions = new IMoAlloAdhocProhib[2];
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            for (var index = 0; index < prohibitions.Length; index++)
            {
                prohibitions[index] = cache.ServiceLocator.GetInstance<IMoAlloAdhocProhibFactory>().Create();
                cache.LangProject.MorphologicalDataOA.AdhocCoProhibitionsOC.Add(prohibitions[index]);
            }
        });
        new FwDataProjectLoader().Save(cache);
        return prohibitions.Select(item => CanonicalId.FromGuid(item.Guid)).ToArray();
    }

    private static object Prohibition(string id, string primary, string other) => new
    {
        prohibitionGuid = id,
        kind = "allomorph",
        disabled = false,
        adjacency = "somewhereToLeft",
        primaryGuid = primary,
        targetKind = "allomorph",
        others = new[] { new { targetGuid = other, targetKind = "allomorph" } },
        loaded = true,
    };

    private static JsonElement[] AssertDraftComposers(string projectPath, string draftName, string proposalId,
        string[] expectedComposers)
    {
        var review = ProposalReview.Read(new ProposalReviewRequest(projectPath, proposalId));
        Assert.True(review.Succeeded, review.Refusal?.Message);
        Assert.True(review.Value!.IsDraft);
        Assert.Equal(draftName, review.Value.DraftName);
        Assert.Equal(expectedComposers.Length, review.Value.Entries.Count);

        var draft = ProjectStoreCommand.Run(projectPath, MotifProductVersion.CurrentText, (database, _) =>
        {
            var record = new ProposalRepository(database).GetDraft(draftName);
            return CommandOutcome<string>.Success(record.ProposalJson!);
        });
        Assert.True(draft.Succeeded, draft.Refusal?.Message);
        using var json = JsonDocument.Parse(draft.Value!);
        var composers = json.RootElement.GetProperty("composerProvenance").EnumerateArray().ToArray();
        Assert.Equal(expectedComposers, composers.Select(item => item.GetProperty("composer").GetString()));
        return composers.Select(item => item.GetProperty("input").Clone()).ToArray();
    }

    private static void AssertProjectUnchanged(WalkthroughProject project, string expectedHash,
        SavedDispositionPair savedDispositions)
    {
        Assert.Equal(expectedHash, HashProject(project.FwDataPath));
        using var cache = new FwDataProjectLoader().LoadScratchCache(project.FwDataPath);
        var snapshot = HumanJudgmentReader.Read(cache);
        Assert.Equal(2, snapshot.Judgments.Count);
        Assert.Empty(snapshot.Unavailable);
        Assert.Contains(snapshot.Judgments, item => item.RecordId == savedDispositions.ToRevise.RecordId &&
            item.Judgment.Body is DispositionJudgment { Disposition: ParsimonyDispositionKind.Keep });
        Assert.Contains(snapshot.Judgments, item => item.RecordId == savedDispositions.ToRetract.RecordId &&
            item.Judgment.Body is DispositionJudgment { Disposition: ParsimonyDispositionKind.Fix });
    }

    private static string HashProject(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private static DraftState ReadDraftState(string projectPath, string draftName)
    {
        var result = ProjectStoreCommand.Run(projectPath, MotifProductVersion.CurrentText, (database, _) =>
        {
            var json = new ProposalRepository(database).GetDraft(draftName).ProposalJson!;
            return CommandOutcome<DraftState>.Success(new(json, Encoding.UTF8.GetBytes(json), DraftRevision.Compute(json)));
        });
        Assert.True(result.Succeeded, result.Refusal?.Message);
        return result.Value!;
    }

    private static void AssertDraftStateUnchanged(DraftState before, DraftState after)
    {
        Assert.Equal(before.ProposalJson, after.ProposalJson);
        Assert.True(before.ProposalJsonBytes.AsSpan().SequenceEqual(after.ProposalJsonBytes));
        Assert.Equal(before.Revision, after.Revision);
    }

    private static void AssertTypedBatchRefusal(CallToolResult result)
    {
        Assert.True(result.IsError);
        Assert.Equal("tool.invalid-argument", result.StructuredContent!.Value.GetProperty("code").GetString());
    }

    private static string AddDuplicateAffixRetirementIntent(WalkthroughProject project, SeededProject seed)
    {
        using var cache = new FwDataProjectLoader().LoadCache(project.FwDataPath);
        var entry = cache.ServiceLocator.GetInstance<ILexEntryRepository>().GetObject(seed.FirstEntryId);
        var prefix = cache.ServiceLocator.GetInstance<IMoMorphTypeRepository>()
            .GetObject(MoMorphTypeTags.kguidMorphPrefix);
        IMoAffixAllomorph[] forms = new IMoAffixAllomorph[2];
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            for (var index = 0; index < forms.Length; index++)
            {
                forms[index] = cache.ServiceLocator.GetInstance<IMoAffixAllomorphFactory>().Create();
                entry.AlternateFormsOS.Add(forms[index]);
                forms[index].MorphTypeRA = prefix;
                forms[index].Form.set_String(cache.DefaultVernWs, "t1-retirement-duplicate");
            }
        });
        new FwDataProjectLoader().Save(cache);

        var entryId = CanonicalId.FromGuid(entry.Guid);
        var identities = forms.Select(form => new AllomorphIdentity(
            CanonicalId.FromGuid(form.Guid).Value,
            entryId.Value,
            form.ClassName,
            "alternate",
            "prefix",
            AllomorphRetirementSemanticDigest.Compute(cache, form),
            null)).ToArray();
        return RetireAllomorphCodec.ToJson(new RetireAllomorphIntentDocument(
            new RetireAllomorphIntent(entryId.Value, AllomorphRetirementScope.Affix,
                [identities[0]], [], [], []),
            [identities[1]]));
    }

    private sealed record SavedDisposition(string RecordId, JudgmentPredecessor Head);

    private sealed record DraftState(string ProposalJson, byte[] ProposalJsonBytes, string Revision);

    private sealed record SavedDispositionPair(SavedDisposition ToRevise, SavedDisposition ToRetract);

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
