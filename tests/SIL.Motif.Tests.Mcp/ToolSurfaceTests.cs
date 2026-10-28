using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Mcp;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker;
using Xunit;

namespace SIL.Motif.Tests.Mcp;

[Trait("MotifTestLevel", "Unit")]
public sealed class ToolSurfaceTests
{
    [Fact]
    public void EveryEligibleCommandHasAToolAndNoOtherCommandDoes()
    {
        foreach (var command in CommandCatalog.All.Where(command =>
                     !SIL.Motif.Commands.ProposalCommands.ComposerDescriptors.Any(composer => composer.Verb == command.Name)))
            Assert.Equal(command.IsAgentTool,
                AgentTools.All.Any(tool => tool.Requests.Contains(command.RequestType)));
        Assert.All(AgentTools.All, tool => Assert.NotEqual(AgentClass.HumanOnly, tool.Class));
    }

    private static readonly string[] BatchlessComposers =
        ["retire-allomorph", "retire-redundant-zero-affix", "compose-record-parsimony-disposition"];

    private static string[] AgentEligibleComposerVerbs() => CommandCatalog.All
        .Where(command => SIL.Motif.Commands.ProposalCommands.ComposerVerbs.Contains(command.Name))
        .Where(command => command.IsAgentTool)
        .Select(command => command.Name)
        .Order(StringComparer.Ordinal)
        .ToArray();

    private static string[] OfferedVerbs(string tool, string property) =>
        AgentTools.All.Single(candidate => candidate.Name == tool).InputSchema["properties"]![property]!["items"]!["oneOf"]!
            .AsArray().Select(branch => branch!["properties"]!["verb"]!["enum"]![0]!.GetValue<string>())
            .Order(StringComparer.Ordinal).ToArray();

    [Fact]
    public void TheMcpBatchOffersEveryComposerVerbTheCatalogOffersToAgentsExceptThreeNamedOnes()
    {
        // The retiring batch keeps its published set; motif_compose offers the three it leaves out.
        foreach (var verb in BatchlessComposers)
        {
            Assert.Contains(verb, SIL.Motif.Commands.ProposalCommands.ComposerVerbs);
            Assert.True(CommandCatalog.All.Single(command => command.Name == verb).IsAgentTool,
                $"'{verb}' is excluded from the batch but is not an agent tool, so the exclusion is vacuous.");
        }
        var expected = AgentEligibleComposerVerbs().Except(BatchlessComposers).ToArray();

        Assert.Equal(expected, OfferedVerbs("motif_compose_batch", "intents"));
    }

    [Fact]
    public void TheMcpComposeToolOffersEveryComposerVerbTheCatalogOffersToAgents()
    {
        var expected = AgentEligibleComposerVerbs();

        Assert.Equal(expected, OfferedVerbs("motif_compose", "steps"));
        Assert.All(BatchlessComposers, verb => Assert.Contains(verb, expected));
    }

    [Theory]
    [InlineData("retire-allomorph", "format")]
    [InlineData("retire-redundant-zero-affix", "retirement")]
    [InlineData("compose-record-parsimony-disposition", "subject")]
    public void AStepOfAHandWrittenToolComposerHasASchemaClosedDownToItsLeaves(string verb, string nested)
    {
        var compose = AgentTools.All.Single(tool => tool.Name == "motif_compose").InputSchema;
        var branch = compose["properties"]!["steps"]!["items"]!["oneOf"]!.AsArray().Single(candidate =>
            candidate!["properties"]!["verb"]!["enum"]![0]!.GetValue<string>() == verb)!;
        var intent = branch["properties"]!["intent"]!;
        Assert.False(intent["additionalProperties"]!.GetValue<bool>());
        Assert.NotNull(intent["properties"]![nested]);
        Assert.DoesNotContain("\"intent_json\"", intent.ToJsonString());
    }

    [Theory]
    [InlineData("motif_retire_allomorph", new[] { "draft", "intent_json", "project" })]
    [InlineData("motif_retire_redundant_zero_affix", new[] { "draft", "intent_json", "project" })]
    [InlineData("motif_record_parsimony_disposition", new[] { "draft", "recordTypeId", "measureId", "subject",
        "disposition", "evidenceDigest", "evidenceContract", "subjectCaption", "measureCaption", "reason", "question",
        "reportId", "project" })]
    public void TheHandWrittenToolsKeepTheirArguments(string name, string[] arguments)
    {
        var tool = AgentTools.All.Single(candidate => candidate.Name == name);
        Assert.Equal(arguments.Order(StringComparer.Ordinal), tool.ArgumentNames.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void CliFirstCommandsAndTheActivityReaderOptOutOfDefaultToolExposure()
    {
        var optedOut = CommandCatalog.All.Where(command => !command.ExposeAsAgentTool)
            .Select(command => command.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        // activity has the hand-written motif_activity tool, which also reads Proposals and jobs.
        Assert.Equal(new[]
        {
            "activity",
            "compose-delete-empty-sense",
            "compose-edit-affix-inflection-classes",
            "compose-edit-part-of-speech-inflection-class-default",
            "compose-edit-stem-allomorph-stem-name",
            "compose-edit-stem-inflection-class",
            "compose-set-stem-part-of-speech",
            "project tag",
            "store artifacts",
        }, optedOut);

        var artifactInventory = Assert.Single(CommandCatalog.All,
            command => command.Name == "store artifacts");
        Assert.Equal(CommandSurface.Developer, artifactInventory.Surface);
        Assert.Equal(AgentClass.HumanOnly, artifactInventory.Agent);
        Assert.False(artifactInventory.IsAgentTool);
        Assert.DoesNotContain(AgentTools.All,
            tool => tool.Requests.Contains(artifactInventory.RequestType));

        var numeralEditors = CommandCatalog.All.Where(command =>
            command.Name is "compose-edit-part-of-speech-inflection-class-default" or
                "compose-edit-affix-inflection-classes" or "compose-edit-stem-allomorph-stem-name" or
                "compose-edit-stem-inflection-class" or "compose-delete-empty-sense" or
                "compose-set-stem-part-of-speech").ToArray();
        Assert.Equal(6, numeralEditors.Length);
        Assert.All(numeralEditors, command =>
        {
            Assert.Equal(CommandSurface.AdvancedAi, command.Surface);
            Assert.Equal(AgentClass.Draft, command.Agent);
            Assert.False(command.IsAgentTool);
            Assert.DoesNotContain(AgentTools.All, tool => tool.Requests.Contains(command.RequestType));
        });

        Assert.All(CommandCatalog.All.Except(numeralEditors).Where(command =>
            command.Name != "activity" && command.Name != "project tag" &&
            command.Name != "store artifacts"), command =>
            Assert.True(command.ExposeAsAgentTool));
        Assert.DoesNotContain(AgentTools.All, tool => tool.Name == "motif_activity" && tool.Requests.Count > 0);
    }

    [Theory]
    [InlineData("SIL.Motif.Contract")]
    [InlineData("SIL.Motif.Commands")]
    public void RequestFieldDocsTravelInsideTheServerSoAPublishedBuildCannotDropThem(string assembly)
    {
        Assert.Contains("motif-field-docs/" + assembly + ".xml",
            typeof(MotifMcpServer).Assembly.GetManifestResourceNames());
    }

    [Theory]
    [InlineData("default")]
    [InlineData("phonology")]
    [InlineData("parsimony")]
    [InlineData("parsimony-experimental")]
    public void SharedDescriptionsAndSchemasMatchTheProfileSnapshot(string profile)
    {
        var tools = MotifMcpServer.Expose(ToolProfile.Load(profile));
        var listed = new JsonArray(tools.Select(tool =>
        {
            var described = tool.Describe();
            return (JsonNode)new JsonObject
            {
                ["name"] = described.Name, ["description"] = described.Description,
                ["inputSchema"] = JsonNode.Parse(described.InputSchema.GetRawText()),
                ["readOnlyHint"] = described.Annotations!.ReadOnlyHint,
                ["destructiveHint"] = described.Annotations.DestructiveHint,
                ["idempotentHint"] = described.Annotations.IdempotentHint,
            };
        }).ToArray()).ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n";
        var path = GoldenPath(profile);
        if (Environment.GetEnvironmentVariable("MOTIF_UPDATE_GOLDEN") == "1") File.WriteAllText(path, listed);
        Assert.Equal(File.ReadAllText(path).ReplaceLineEndings("\n"), listed.ReplaceLineEndings("\n"));
    }

    [Theory]
    [InlineData("default")]
    [InlineData("phonology")]
    [InlineData("parsimony")]
    [InlineData("parsimony-experimental")]
    public void NoToolWritesThePersonsNoteAndTheRationaleToolIsOffered(string profile)
    {
        var tools = MotifMcpServer.Expose(ToolProfile.Load(profile));

        Assert.DoesNotContain(tools, tool => tool.Name.Contains("note", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(tools, tool => tool.Tool.Requests.Contains(typeof(SIL.Motif.Commands.Requests.SetNoteRequest)));
        if (profile == "default")
            Assert.Contains(tools, tool => tool.Name == "motif_set_rationale");
    }

    [Fact]
    public void FinalizeAndOperationRemovalAreAvailableByDefault()
    {
        var tools = MotifMcpServer.Expose(ToolProfile.Builtin);
        Assert.Contains(tools, tool => tool.Name == "motif_finalize_proposal");
        Assert.Contains(tools, tool => tool.Name == "motif_remove_operations");
        Assert.Contains(tools, tool => tool.Name == "motif_assess");
        Assert.Contains(tools, tool => tool.Name == "motif_list_projects");
        Assert.DoesNotContain(tools, tool => tool.Name.Contains("finish", StringComparison.Ordinal));
    }

    [Fact]
    public void ProjectTagsAreVisibleThroughProjectMetadataWithoutAnAgentSetter()
    {
        var tools = MotifMcpServer.Expose(ToolProfile.Builtin);

        Assert.Contains(tools, tool => tool.Name == "motif_list_projects");
        Assert.DoesNotContain(tools, tool => tool.Name.Contains("project_tag", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DefaultProfileOffersTypedToolsForBuildingALanguageGrammar()
    {
        var tools = MotifMcpServer.Expose(ToolProfile.Builtin);
        var names = tools.Select(tool => tool.Name).ToHashSet(StringComparer.Ordinal);

        Assert.All(new[]
        {
            "motif_add_part_of_speech",
            "motif_add_lexical_entry",
            "motif_add_feature_structure",
            "motif_add_feature_value",
            "motif_add_phoneme",
            "motif_add_natural_class",
            "motif_add_environment",
            "motif_add_phonological_rule",
            "motif_add_inflection_feature",
            "motif_add_inflection_class",
            "motif_add_stem_name",
            "motif_add_affix_slot",
            "motif_add_alternate_form",
            "motif_add_affix_template",
            "motif_compose_batch",
        }, name => Assert.Contains(name, names));

        var entry = tools.Single(tool => tool.Name == "motif_add_lexical_entry");
        Assert.Equal("object", entry.Tool.InputSchema["properties"]!["msa"]!["type"]!.GetValue<string>());
        var entryProperties = entry.Tool.InputSchema["properties"]!.AsObject();
        Assert.Contains("environments", entryProperties.Select(property => property.Key));
        Assert.Contains("inflectionClasses", entryProperties.Select(property => property.Key));
        Assert.Equal("array", entryProperties["environments"]!["type"]!.GetValue<string>());
        Assert.Equal("array", entryProperties["inflectionClasses"]!["type"]!.GetValue<string>());
        Assert.Equal("string", entryProperties["environments"]!["items"]!["type"]!.GetValue<string>());
        Assert.Equal("string", entryProperties["inflectionClasses"]!["items"]!["type"]!.GetValue<string>());
        Assert.DoesNotContain(entry.Tool.InputSchema["required"]!.AsArray(), value =>
            value!.GetValue<string>() is "environments" or "inflectionClasses");

        var alternate = tools.Single(tool => tool.Name == "motif_add_alternate_form").Tool.InputSchema;
        var alternateProperties = alternate["properties"]!.AsObject();
        Assert.Equal(new[]
        {
            "draft", "entry", "form", "formWs", "morphType", "environments", "inflectionClasses", "placement", "project",
        }, alternateProperties.Select(property => property.Key));
        Assert.Equal(new[] { "draft", "entry", "form", "formWs", "morphType", "project" },
            alternate["required"]!.AsArray().Select(value => value!.GetValue<string>()));
        Assert.False(alternate["additionalProperties"]!.GetValue<bool>());
        foreach (var field in new[] { "environments", "inflectionClasses", "placement" })
            Assert.DoesNotContain(alternate["required"]!.AsArray(), value => value!.GetValue<string>() == field);
        foreach (var field in new[] { "environments", "inflectionClasses" })
        {
            Assert.Equal("array", alternateProperties[field]!["type"]!.GetValue<string>());
            Assert.Equal("string", alternateProperties[field]!["items"]!["type"]!.GetValue<string>());
        }

        var batch = tools.Single(tool => tool.Name == "motif_compose_batch");
        var intents = batch.Tool.InputSchema["properties"]!["intents"]!;
        Assert.Equal("array", intents["type"]!.GetValue<string>());
        Assert.DoesNotContain("intentsJson", batch.Tool.ArgumentNames);
        var branches = intents["items"]!["oneOf"]!.AsArray();
        var verbs = branches.Select(branch => branch!["properties"]!["verb"]!["enum"]![0]!.GetValue<string>()).ToArray();
        Assert.Equal(32, verbs.Length);
        Assert.Equal(new[]
        {
            "compose-author-lexeme-form",
            "compose-author-feature-structure",
            "compose-author-feature-value",
            "compose-author-phoneme",
            "compose-edit-phoneme",
            "compose-edit-compound-rule",
            "compose-set-no-default-compounding",
            "compose-rewrite-lexeme-form",
            "compose-author-natural-class",
            "compose-edit-natural-class",
            "compose-relink-natural-class",
            "compose-author-environment",
            "compose-author-phonological-rule",
            "compose-author-part-of-speech",
            "compose-author-inflection-feature",
            "compose-author-inflection-class",
            "compose-author-stem-name",
            "compose-author-lexical-entry",
            "compose-author-alternate-form",
            "compose-author-affix-process",
            "compose-author-affix-process-allomorph",
            "compose-author-affix-slot",
            "compose-author-affix-template",
            "compose-edit-adhoc-prohibition",
            "compose-edit-affix-slot",
            "compose-edit-affix-template",
            "compose-edit-inflectional-affix",
            "compose-edit-derivational-affix-restriction",
            "compose-edit-allomorph-condition",
            "order-allomorphs",
            "compose-author-text",
            "compose-edit-analysis-decision",
        }.Order(StringComparer.Ordinal), verbs.Order(StringComparer.Ordinal));
        Assert.DoesNotContain("compose-record-parsimony-disposition", verbs);
        Assert.DoesNotContain("retire-allomorph", verbs);
        Assert.DoesNotContain("retire-redundant-zero-affix", verbs);
        Assert.DoesNotContain("compose-retire-allomorph", verbs);
        Assert.DoesNotContain("compose-retire-redundant-zero-affix", verbs);
        Assert.DoesNotContain("unknown-nonallowlisted-verb", verbs);
        Assert.DoesNotContain("parsimony dispose", verbs);
        Assert.DoesNotContain("parsimony revise", verbs);
        Assert.DoesNotContain("parsimony retract", verbs);

        var alternateBranch = branches.Single(branch =>
            branch!["properties"]!["verb"]!["enum"]![0]!.GetValue<string>() == "compose-author-alternate-form")!;
        var alternateBranchProperties = alternateBranch["properties"]!.AsObject();
        Assert.Equal(new[] { "verb", "intent", "citation" }, alternateBranchProperties.Select(property => property.Key));
        Assert.False(alternateBranch["additionalProperties"]!.GetValue<bool>());
        var alternateIntent = alternateBranchProperties["intent"]!;
        var alternateIntentProperties = alternateIntent["properties"]!.AsObject();
        Assert.Equal(new[] { "entry", "form", "formWs", "morphType", "environments", "inflectionClasses", "placement" },
            alternateIntentProperties.Select(property => property.Key));
        Assert.Equal(new[] { "entry", "form", "formWs", "morphType" },
            alternateIntent["required"]!.AsArray().Select(value => value!.GetValue<string>()));
        Assert.False(alternateIntent["additionalProperties"]!.GetValue<bool>());
        Assert.Equal(new[] { "compose-author-alternate-form" },
            alternateBranchProperties["verb"]!["enum"]!.AsArray().Select(value => value!.GetValue<string>()));
        foreach (var field in new[] { "environments", "inflectionClasses" })
        {
            Assert.Equal("array", alternateIntentProperties[field]!["type"]!.GetValue<string>());
            Assert.Equal("string", alternateIntentProperties[field]!["items"]!["type"]!.GetValue<string>());
        }
        var placement = alternateIntentProperties["placement"]!;
        Assert.Equal("object", placement["type"]!.GetValue<string>());
        Assert.Equal(new[] { "after", "before" }, placement["properties"]!.AsObject().Select(property => property.Key));
        Assert.False(placement["additionalProperties"]!.GetValue<bool>());
        Assert.False(placement.AsObject().ContainsKey("required"));

        var batchOnly = MotifMcpServer.Expose(new ToolProfile("batch-only", null, null, "concise",
            [new ToolSelection("motif_compose_batch", null, null, null)]));
        var batchOnlySchema = Assert.Single(batchOnly).Tool.InputSchema;
        var batchOnlyVerbs = batchOnlySchema["properties"]!["intents"]!["items"]!["oneOf"]!.AsArray()
            .Select(branch => branch!["properties"]!["verb"]!["enum"]![0]!.GetValue<string>())
            .Order(StringComparer.Ordinal);
        Assert.Equal(verbs.Order(StringComparer.Ordinal), batchOnlyVerbs);
        var template = branches.Single(branch => branch!["properties"]!["verb"]!["enum"]![0]!.GetValue<string>() ==
            "compose-author-affix-template");
        var templateIntent = template!["properties"]!["intent"]!["properties"]!;
        Assert.Contains("prefixSlots", templateIntent.AsObject().Select(pair => pair.Key));
        Assert.Contains("suffixSlots", templateIntent.AsObject().Select(pair => pair.Key));
        Assert.DoesNotContain("prefix_slots", templateIntent.AsObject().Select(pair => pair.Key));

        var lexicalForm = branches.Single(branch => branch!["properties"]!["verb"]!["enum"]![0]!.GetValue<string>() ==
            "compose-author-lexeme-form");
        var lexicalFormIntent = lexicalForm!["properties"]!["intent"]!["properties"]!;
        Assert.Contains("morphType", lexicalFormIntent.AsObject().Select(pair => pair.Key));
        Assert.DoesNotContain("morph_type", lexicalFormIntent.AsObject().Select(pair => pair.Key));
    }

    [Fact]
    public void ProjectToolsRequireAKnownProjectSelector()
    {
        foreach (var tool in AgentTools.All.Where(tool => tool.NeedsProject && tool.Name != "motif_guide"))
        {
            Assert.Contains("project", tool.ArgumentNames);
            Assert.Contains(tool.InputSchema["required"]!.AsArray(), value => value!.GetValue<string>() == "project");
            Assert.DoesNotContain("fwDataPath", tool.ArgumentNames);
            Assert.DoesNotContain("projectPath", tool.ArgumentNames);
        }
    }

    [Fact]
    public void DescriptionsAreGeneratedFromSharedHelpAndRequestDocumentation()
    {
        foreach (var tool in AgentTools.All.Where(tool => tool.Requests.Count > 0))
        {
            var commands = CommandCatalog.All.Where(command => tool.Requests.Contains(command.RequestType));
            Assert.Equal(CatalogTools.Describe(commands), tool.Description);
            foreach (var command in commands)
                foreach (var field in command.RequestType.GetProperties().Where(field =>
                             field.Name is not ("ProjectPath" or "FwDataPath" or "ProductVersion")))
                    Assert.Contains(field.Name + ": ", tool.Description);
        }
    }

    [Fact]
    public void EveryBuiltInDescriptionUsesTheBindingGlossary()
    {
        foreach (var tool in AgentTools.All)
            Assert.DoesNotMatch(new Regex(@"\b(finish|submit|commit|publish|pr|change set|change group|patch|branch|preview|plan|simulation|attempt|experiment|evaluation|test)\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), tool.Description);
    }

    [Theory]
    [InlineData("default")]
    [InlineData("lean")]
    [InlineData("phonology")]
    [InlineData("parsimony")]
    [InlineData("parsimony-experimental")]
    public void ToolDescriptionsUseTheBindingGlossary(string profile)
    {
        foreach (var tool in MotifMcpServer.Expose(ToolProfile.Load(profile)))
            Assert.DoesNotMatch(new Regex(@"\b(finish|submit|commit|publish|pr|change set|change group|patch|branch|preview|plan|simulation|attempt|experiment|evaluation|test)\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), tool.Description);
    }

    [Fact]
    public async Task GeneratedStatisticsToolAcceptsTheDeclaredEnumChoice()
    {
        var tool = AgentTools.All.Single(tool => tool.Name == "motif_stats");
        var context = new ServerContext("unused.fwdata", "1.0", new SIL.Motif.Commands.NoRunnerLauncher(
            new SIL.Motif.Commands.JobRunnerLaunchOptions("root", null)), new ActivityLog(null), ToolProfile.Builtin, TextWriter.Null, advancedAiModeEnabled: true);
        Assert.DoesNotContain(tool.InputSchema["required"]!.AsArray(), value => value!.GetValue<string>() == "assessmentId");
        var arguments = new JsonObject { ["output"] = "JsonRows",
            ["forwardedArguments"] = new JsonArray("--format", "jsonl") };

        var result = await tool.Run(context, new ToolArgs(arguments, tool.ArgumentNames), CancellationToken.None);

        Assert.Equal("stats.format-conflict", result.Refusal?.Code);
    }

    [Fact]
    public void AProfileCannotExposeHumanOnlyCommands()
    {
        var profile = new ToolProfile("bad", null, null, "concise", [new ToolSelection("motif_apply", null, null, null)]);
        Assert.Throws<ProfileException>(() => MotifMcpServer.Expose(profile));
    }

    [Fact]
    public void AProfileRejectsAmbiguousToolNames()
    {
        var profile = new ToolProfile("twin", null, null, "concise",
            [new ToolSelection("motif_overview", "same", null, null), new ToolSelection("motif_word", "same", null, null)]);
        Assert.Throws<ProfileException>(() => MotifMcpServer.Expose(profile));
    }

    private static string GoldenPath(string profile, [CallerFilePath] string source = "") =>
        Path.Combine(Path.GetDirectoryName(source)!, "Golden", "tools-list." + profile + ".json");
}

[Collection(LcmCacheParallelCollections.Group3)]
[Trait("MotifTestLevel", "Integration")]
public sealed class DetailedNaturalClassMemberToolTests(PristineProjectFixture pristine)
{
    [Fact]
    public async Task DetailedMemberIdsAreAcceptedAsExpectedMembersByNaturalClassEdit()
    {
        Guid phonemeGuid = default;
        Guid otherPhonemeGuid = default;
        Guid naturalClassGuid = default;
        Guid duplicateClassGuid = default;
        Guid environmentGuid = default;
        Guid duplicateEnvironmentGuid = default;
        var suggestionGuids = new List<Guid>();
        var baselineEnvironmentCount = 0;
        using var project = new WalkthroughProject(pristine, configure: (cache, _, _) =>
        {
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            {
                var phonology = cache.LangProject.PhonologicalDataOA;
                var services = cache.ServiceLocator;
                if (phonology.PhonemeSetsOS.Count == 0)
                    phonology.PhonemeSetsOS.Add(services.GetInstance<IPhPhonemeSetFactory>().Create());
                var set = phonology.PhonemeSetsOS[0];
                var phoneme = services.GetInstance<IPhPhonemeFactory>().Create();
                set.PhonemesOC.Add(phoneme);
                phoneme.Name.set_String(cache.DefaultVernWs, "eng");
                phoneme.CodesOS.Clear();
                var code = services.GetInstance<IPhCodeFactory>().Create();
                phoneme.CodesOS.Add(code);
                code.Representation.set_String(cache.DefaultVernWs, "ŋ");

                var otherPhoneme = services.GetInstance<IPhPhonemeFactory>().Create();
                set.PhonemesOC.Add(otherPhoneme);
                otherPhoneme.Name.set_String(cache.DefaultVernWs, "en");
                otherPhoneme.CodesOS.Clear();
                var otherCode = services.GetInstance<IPhCodeFactory>().Create();
                otherPhoneme.CodesOS.Add(otherCode);
                otherCode.Representation.set_String(cache.DefaultVernWs, "n");

                var naturalClass = services.GetInstance<IPhNCSegmentsFactory>().Create();
                phonology.NaturalClassesOS.Add(naturalClass);
                naturalClass.Name.set_String(cache.DefaultAnalWs, "nasals");
                naturalClass.Abbreviation.set_String(cache.DefaultAnalWs, "N");
                naturalClass.SegmentsRC.Add(phoneme);
                naturalClass.SegmentsRC.Add(otherPhoneme);
                var duplicateClass = services.GetInstance<IPhNCSegmentsFactory>().Create();
                phonology.NaturalClassesOS.Add(duplicateClass);
                duplicateClass.Name.set_String(cache.DefaultAnalWs, "nasals");
                duplicateClass.Abbreviation.set_String(cache.DefaultAnalWs, "N2");
                duplicateClass.SegmentsRC.Add(phoneme);
                var environment = services.GetInstance<IPhEnvironmentFactory>().Create();
                phonology.EnvironmentsOS.Add(environment);
                environment.Name.set_String(cache.DefaultAnalWs, "after nasal");
                environment.StringRepresentation = TsStringUtils.MakeString("/ [N] _", cache.DefaultVernWs);
                var duplicateEnvironment = services.GetInstance<IPhEnvironmentFactory>().Create();
                phonology.EnvironmentsOS.Add(duplicateEnvironment);
                duplicateEnvironment.Name.set_String(cache.DefaultAnalWs, "after nasal");
                duplicateEnvironment.StringRepresentation = TsStringUtils.MakeString("/ [N] _", cache.DefaultVernWs);
                var namedEnvironment = services.GetInstance<IPhEnvironmentFactory>().Create();
                phonology.EnvironmentsOS.Add(namedEnvironment);
                namedEnvironment.Name.set_String(cache.DefaultAnalWs, "after a nasal");
                namedEnvironment.StringRepresentation = TsStringUtils.MakeString("/ [N] _", cache.DefaultVernWs);
                for (var index = 0; index < 13; index++)
                {
                    var suggestion = services.GetInstance<IPhEnvironmentFactory>().Create();
                    phonology.EnvironmentsOS.Add(suggestion);
                    suggestion.Name.set_String(cache.DefaultAnalWs, "zzz candidate");
                    suggestion.StringRepresentation = TsStringUtils.MakeString("/ [N] _", cache.DefaultVernWs);
                    suggestionGuids.Add(suggestion.Guid);
                }
                baselineEnvironmentCount = phonology.EnvironmentsOS.Count;
                phonemeGuid = phoneme.Guid;
                otherPhonemeGuid = otherPhoneme.Guid;
                naturalClassGuid = naturalClass.Guid;
                duplicateClassGuid = duplicateClass.Guid;
                environmentGuid = environment.Guid;
                duplicateEnvironmentGuid = duplicateEnvironment.Guid;
            });
        });
        var managedRoot = RunnerOptions.ResolveRoot();
        KnownProjectTestSetup.AssignUniqueTag(managedRoot, project.FwDataPath,
            Path.GetFileNameWithoutExtension(project.FwDataPath));
        var baseline = BaselineCaptureCommand.Capture(
            new BaselineCaptureRequest(project.FwDataPath), managedRoot);
        Assert.True(baseline.Succeeded, baseline.Refusal?.Message);

        var defaultProfile = ToolProfile.Load("default");
        var context = new ServerContext(project.FwDataPath, "1.0",
            new NoRunnerLauncher(new JobRunnerLaunchOptions(managedRoot, null)), new ActivityLog(null),
            defaultProfile, TextWriter.Null, advancedAiModeEnabled: true);
        var grammarTools = MotifMcpServer.Expose(defaultProfile);
        var grammar = await MotifMcpServer.CallAsync(context, grammarTools, "motif_grammar",
            Arguments(("project", project.FwDataPath), ("kind", "natural_classes"), ("detail", "detailed")), CancellationToken.None);

        Assert.False(grammar.IsError ?? false, grammar.StructuredContent?.GetRawText());
        var naturalClass = grammar.StructuredContent!.Value.GetProperty("result").GetProperty("items")
            .EnumerateArray().Single(item => item.GetProperty("id").GetString() == CanonicalId.FromGuid(naturalClassGuid).Value);
        var members = naturalClass.GetProperty("members").EnumerateArray().ToArray();
        Assert.Equal(2, naturalClass.GetProperty("memberCount").GetInt32());
        Assert.Equal(2, members.Length);
        var member = members.Single(item => item.GetProperty("id").GetString() == CanonicalId.FromGuid(phonemeGuid).Value);
        var memberId = member.GetProperty("id").GetString()!;
        Assert.Equal(CanonicalId.FromGuid(phonemeGuid).Value, memberId);
        Assert.Equal("ŋ", member.GetProperty("symbol").GetString());
        var otherMember = members.Single(item => item.GetProperty("id").GetString() == CanonicalId.FromGuid(otherPhonemeGuid).Value);
        Assert.Equal("n", otherMember.GetProperty("symbol").GetString());
        var memberIds = members.Select(item => item.GetProperty("id").GetString()!).ToArray();

        var defaultGrammar = await MotifMcpServer.CallAsync(context, grammarTools,
            "motif_grammar", Arguments(("project", project.FwDataPath), ("kind", "natural_classes")), CancellationToken.None);
        Assert.False(defaultGrammar.IsError ?? false, defaultGrammar.StructuredContent?.GetRawText());
        var defaultMember = defaultGrammar.StructuredContent!.Value.GetProperty("result").GetProperty("items")
            .EnumerateArray().Single(item => item.GetProperty("id").GetString() == CanonicalId.FromGuid(naturalClassGuid).Value)
            .GetProperty("members").EnumerateArray().Single(item => item.GetProperty("id").GetString() == memberId);
        Assert.Equal(memberId, defaultMember.GetProperty("id").GetString());

        var conciseGrammar = await MotifMcpServer.CallAsync(context, grammarTools, "motif_grammar",
            Arguments(("project", project.FwDataPath), ("kind", "natural_classes"), ("detail", "concise")), CancellationToken.None);
        Assert.False(conciseGrammar.IsError ?? false, conciseGrammar.StructuredContent?.GetRawText());
        var conciseClass = conciseGrammar.StructuredContent!.Value.GetProperty("result").GetProperty("items")
            .EnumerateArray().Single(item => item.GetProperty("id").GetString() == naturalClass.GetProperty("id").GetString());
        Assert.Equal(2, conciseClass.GetProperty("memberCount").GetInt32());
        Assert.False(conciseClass.TryGetProperty("members", out _));

        var experimental = ToolProfile.Load("parsimony-experimental");
        var editContext = new ServerContext(project.FwDataPath, "1.0",
            new NoRunnerLauncher(new JobRunnerLaunchOptions(managedRoot, null)), new ActivityLog(null),
            experimental, TextWriter.Null, advancedAiModeEnabled: true);
        var experimentalTools = MotifMcpServer.Expose(experimental);
        var started = await MotifMcpServer.CallAsync(editContext, experimentalTools, "motif_start_proposal",
            Arguments(("project", project.FwDataPath), ("draft", "member-id-edit"), ("label", "Check a natural class member id"),
                ("rationale", "The listed phoneme id is the exact current member identity.")), CancellationToken.None);
        Assert.False(started.IsError ?? false, started.StructuredContent?.GetRawText());
        var edited = await MotifMcpServer.CallAsync(editContext, experimentalTools, "motif_edit_natural_class",
            Arguments(("project", project.FwDataPath), ("draft", "member-id-edit"),
                ("target", naturalClass.GetProperty("id").GetString()!),
                ("expectedMembers", memberIds), ("members", new[] { memberId })), CancellationToken.None);

        Assert.False(edited.IsError ?? false, edited.StructuredContent?.GetRawText());
        Assert.True(edited.StructuredContent!.Value.GetProperty("ok").GetBoolean(),
            edited.StructuredContent.Value.GetRawText());
        Assert.Equal(1, edited.StructuredContent.Value.GetProperty("result").GetProperty("operationCount").GetInt32());

        var missing = await MotifMcpServer.CallAsync(editContext, experimentalTools, "motif_edit_natural_class",
            Arguments(("project", project.FwDataPath), ("draft", "member-id-edit"), ("target", "nasa"),
                ("expectedMembers", memberIds), ("members", new[] { memberId })), CancellationToken.None);
        Assert.True(missing.IsError);
        var failure = missing.StructuredContent!.Value;
        Assert.False(failure.GetProperty("ok").GetBoolean());
        Assert.Equal("draft.invalid", failure.GetProperty("code").GetString());
        Assert.Contains(CanonicalId.FromGuid(naturalClassGuid).Value, failure.GetProperty("message").GetString());

        var namedEdit = await MotifMcpServer.CallAsync(editContext, experimentalTools, "motif_edit_natural_class",
            Arguments(("project", project.FwDataPath), ("draft", "member-id-edit"), ("target", "n"),
                ("expectedMembers", memberIds), ("members", new[] { memberId })), CancellationToken.None);
        Assert.False(namedEdit.IsError ?? false, namedEdit.StructuredContent?.GetRawText());
        Assert.Equal(1, namedEdit.StructuredContent!.Value.GetProperty("result").GetProperty("operationCount").GetInt32());

        var start = await MotifMcpServer.CallAsync(editContext, experimentalTools, "motif_start_proposal",
            Arguments(("project", project.FwDataPath), ("draft", "named-references"), ("label", "Use named references"),
                ("rationale", "Check explicit sound references.")), CancellationToken.None);
        Assert.False(start.IsError ?? false, start.StructuredContent?.GetRawText());
        async Task<ModelContextProtocol.Protocol.CallToolResult> AddEnvironment(string reference, string name) =>
            await MotifMcpServer.CallAsync(context, grammarTools, "motif_add_environment",
                Arguments(("project", project.FwDataPath), ("draft", "named-references"), ("name", name),
                    ("left", new[] { new { naturalClass = reference } }), ("right", Array.Empty<object>())), CancellationToken.None);
        var missingClass = await AddEnvironment("absent", "missing class");
        Assert.True(missingClass.IsError);
        Assert.Equal("draft.invalid", missingClass.StructuredContent!.Value.GetProperty("code").GetString());
        Assert.Contains(CanonicalId.FromGuid(naturalClassGuid).Value, missingClass.StructuredContent.Value.GetProperty("message").GetString());
        var ambiguousClass = await AddEnvironment("nasals", "ambiguous class");
        Assert.True(ambiguousClass.IsError);
        var classMessage = ambiguousClass.StructuredContent!.Value.GetProperty("message").GetString()!;
        Assert.Contains("ambiguous", classMessage);
        Assert.Contains(CanonicalId.FromGuid(naturalClassGuid).Value, classMessage);
        Assert.Contains(CanonicalId.FromGuid(duplicateClassGuid).Value, classMessage);
        Assert.False((await AddEnvironment("N", "named class")).IsError ?? false);
        Assert.False((await AddEnvironment(CanonicalId.FromGuid(naturalClassGuid).Value, "identified class")).IsError ?? false);

        var replacement = await MotifMcpServer.CallAsync(context, grammarTools, "motif_add_natural_class",
            Arguments(("project", project.FwDataPath), ("draft", "named-references"), ("name", "replacement nasals"),
                ("abbreviation", "RN"), ("members", new[] { memberId })), CancellationToken.None);
        Assert.False(replacement.IsError ?? false, replacement.StructuredContent?.GetRawText());
        var creation = replacement.StructuredContent!.Value.GetProperty("result").GetProperty("operations")
            .EnumerateArray().Single(operation => operation.GetProperty("kind").GetString()!.EndsWith("/createNaturalClasses"));
        var namedClass = await AddEnvironment("replacement nasals", "after replacement");
        Assert.False(namedClass.IsError ?? false, namedClass.StructuredContent?.GetRawText());
        async Task<ModelContextProtocol.Protocol.CallToolResult> Relink(string target) =>
            await MotifMcpServer.CallAsync(editContext, experimentalTools, "motif_relink_natural_class",
                Arguments(("project", project.FwDataPath), ("draft", "named-references"), ("source", "N"),
                    ("replacement", "replacement nasals"), ("replacementCreationOperation", creation.GetProperty("operationId").GetString()),
                    ("environments", new[] { new { target, expectedLeft = new[] { new { naturalClass = "N" } }, expectedRight = Array.Empty<object>() } }),
                    ("ruleContexts", Array.Empty<string>())), CancellationToken.None);
        var missingEnvironment = await Relink("absent");
        Assert.True(missingEnvironment.IsError);
        Assert.Equal("draft.invalid", missingEnvironment.StructuredContent!.Value.GetProperty("code").GetString());
        var missingMessage = missingEnvironment.StructuredContent.Value.GetProperty("message").GetString()!;
        Assert.Contains(CanonicalId.FromGuid(environmentGuid).Value, missingMessage);
        Assert.Equal(12, Regex.Matches(missingMessage, @"\[[A-Za-z0-9_-]{22}\]").Count);
        Assert.Contains($"Total: {baselineEnvironmentCount + 3}", missingMessage);
        Assert.Contains("motif_grammar(kind=environments)", missingMessage);
        var suggestions = await Relink("zzz");
        Assert.True(suggestions.IsError);
        var suggestionMessage = suggestions.StructuredContent!.Value.GetProperty("message").GetString()!;
        Assert.All(suggestionGuids, guid => Assert.Contains(CanonicalId.FromGuid(guid).Value, suggestionMessage));
        Assert.DoesNotContain("Total:", suggestionMessage);
        var manyAmbiguous = await Relink("zzz candidate");
        Assert.True(manyAmbiguous.IsError);
        var manyMessage = manyAmbiguous.StructuredContent!.Value.GetProperty("message").GetString()!;
        Assert.Contains("ambiguous", manyMessage);
        Assert.All(suggestionGuids, guid => Assert.Contains(CanonicalId.FromGuid(guid).Value, manyMessage));
        var ambiguousEnvironment = await Relink("after nasal");
        Assert.True(ambiguousEnvironment.IsError);
        var environmentMessage = ambiguousEnvironment.StructuredContent!.Value.GetProperty("message").GetString()!;
        Assert.Contains("ambiguous", environmentMessage);
        Assert.Contains(CanonicalId.FromGuid(environmentGuid).Value, environmentMessage);
        Assert.Contains(CanonicalId.FromGuid(duplicateEnvironmentGuid).Value, environmentMessage);
        var namedRelink = await Relink("AFTER A NASAL");
        Assert.False(namedRelink.IsError ?? false, namedRelink.StructuredContent?.GetRawText());
        var identifiedRelink = await Relink(CanonicalId.FromGuid(environmentGuid).Value);
        Assert.False(identifiedRelink.IsError ?? false, identifiedRelink.StructuredContent?.GetRawText());
    }

    private static Dictionary<string, JsonElement> Arguments(params (string Name, object? Value)[] values) =>
        values.ToDictionary(pair => pair.Name, pair => JsonSerializer.SerializeToElement(pair.Value));
}
