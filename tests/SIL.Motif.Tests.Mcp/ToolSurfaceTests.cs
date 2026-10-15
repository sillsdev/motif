using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Mcp;
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

    [Fact]
    public void TheMcpBatchOffersEveryComposerVerbTheCatalogOffersToAgentsExceptThreeNamedOnes()
    {
        // Retirements take one intent_json document, so a batch has no property schema for them.
        var retirements = new[] { "retire-allomorph", "retire-redundant-zero-affix" };
        // The default batch refuses a disposition on purpose; McpRoundTripTests pins that refusal.
        var disposition = "compose-record-parsimony-disposition";
        var notInBatch = retirements.Append(disposition);
        var agentEligible = CommandCatalog.All
            .Where(command => SIL.Motif.Commands.ProposalCommands.ComposerVerbs.Contains(command.Name))
            .Where(command => command.IsAgentTool)
            .Select(command => command.Name)
            .Except(notInBatch)
            .Order(StringComparer.Ordinal)
            .ToArray();

        var batch = AgentTools.All.Single(tool => tool.Name == "motif_compose_batch").InputSchema;
        var offered = batch["properties"]!["intents"]!["items"]!["oneOf"]!.AsArray()
            .Select(branch => branch!["properties"]!["verb"]!["enum"]![0]!.GetValue<string>())
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(agentEligible.Except(offered));
        Assert.Empty(offered.Except(agentEligible));
    }

    [Fact]
    public void EveryTypedComposerHasExactlyOneToolBuiltFromItsShapeAndIsOffered()
    {
        foreach (var composer in SIL.Motif.Commands.ProposalCommands.ComposerDescriptors.Where(c => c.IsAgentTool))
        {
            var tool = Assert.Single(AgentTools.All, candidate => candidate.Name == composer.ToolName);
            Assert.Equal(composer.OnByDefault, tool.OnByDefault);
            Assert.Equal(["draft", .. composer.FieldNames, "project"], tool.ArgumentNames);
            Assert.False(tool.InputSchema["additionalProperties"]!.GetValue<bool>());
            var batch = AgentTools.All.Single(candidate => candidate.Name == "motif_compose_batch").InputSchema.ToJsonString();
            Assert.Equal(composer.InBatch, batch.Contains($"\"{composer.Verb}\"", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void OnlyTheNumeralAndStemEditorsOptOutOfDefaultToolExposure()
    {
        var optedOut = CommandCatalog.All.Where(command => !command.ExposeAsAgentTool)
            .Select(command => command.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(new[]
        {
            "compose-edit-affix-inflection-classes",
            "compose-edit-part-of-speech-inflection-class-default",
            "compose-edit-stem-allomorph-stem-name",
            "compose-edit-stem-inflection-class",
        }, optedOut);

        var numeralEditors = CommandCatalog.All.Where(command =>
            command.Name is "compose-edit-part-of-speech-inflection-class-default" or
                "compose-edit-affix-inflection-classes" or "compose-edit-stem-allomorph-stem-name" or
                "compose-edit-stem-inflection-class").ToArray();
        Assert.Equal(4, numeralEditors.Length);
        Assert.All(numeralEditors, command =>
        {
            Assert.Equal(CommandSurface.AdvancedAi, command.Surface);
            Assert.Equal(AgentClass.Draft, command.Agent);
            Assert.False(command.IsAgentTool);
            Assert.DoesNotContain(AgentTools.All, tool => tool.Requests.Contains(command.RequestType));
        });

        Assert.All(CommandCatalog.All.Except(numeralEditors), command =>
            Assert.True(command.ExposeAsAgentTool));
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
        Assert.Equal(25, verbs.Length);
        Assert.Equal(new[]
        {
            "compose-author-lexeme-form",
            "compose-author-feature-structure",
            "compose-author-feature-value",
            "compose-author-phoneme",
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
            "compose-author-affix-slot",
            "compose-author-affix-template",
            "compose-edit-adhoc-prohibition",
            "compose-edit-affix-slot",
            "compose-edit-affix-template",
            "compose-edit-inflectional-affix",
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
