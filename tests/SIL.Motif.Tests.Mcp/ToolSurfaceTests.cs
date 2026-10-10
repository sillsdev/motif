using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Mcp;
using Xunit;

namespace SIL.Motif.Tests.Mcp;

public sealed class ToolSurfaceTests
{
    private static readonly string[] AlwaysHumanOnly =
        ["apply", "apply --all-pending", "discard-draft", "store delete-refused", "reject", "defer", "supersede",
            "parsimony negative confirm", "parsimony negative retract"];

    [Fact]
    public void TheDefaultProfileListsExactlyTheToolsInTheGoldenSnapshot()
    {
        var listed = Listing(MotifMcpServer.Expose(ToolProfile.Load(ShippedProfile("default"))));
        var golden = GoldenPath();

        if (Environment.GetEnvironmentVariable("MOTIF_UPDATE_GOLDEN") == "1") File.WriteAllText(golden, listed);

        Assert.Equal(File.ReadAllText(golden).ReplaceLineEndings("\n"), listed.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void ThePhonologyProfilePinsTheFiveSemanticDraftTools()
    {
        var tools = MotifMcpServer.Expose(ToolProfile.Load(ShippedProfile("phonology")));
        foreach (var name in new[] { "motif_add_feature_value", "motif_add_phoneme", "motif_add_natural_class", "motif_add_environment", "motif_add_phonological_rule" })
        {
            var tool = Assert.Single(tools, t => t.Name == name);
            Assert.Equal(AgentClass.Draft, tool.Tool.Class);
            Assert.False(tool.Describe().Annotations!.ReadOnlyHint);
            Assert.False(tool.Describe().Annotations!.DestructiveHint);
            Assert.False(tool.Describe().Annotations!.IdempotentHint);
            Assert.Contains("Next:", tool.Description);
        }
        var listed = Listing(tools);
        var golden = Path.Combine(Path.GetDirectoryName(GoldenPath())!, "tools-list.phonology.json");
        if (Environment.GetEnvironmentVariable("MOTIF_UPDATE_GOLDEN") == "1") File.WriteAllText(golden, listed);
        Assert.Equal(File.ReadAllText(golden).ReplaceLineEndings("\n"), listed.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void TheParsimonyProfileExposesTheDispositionDraftToolForHumanReview()
    {
        var tools = MotifMcpServer.Expose(ToolProfile.Load(ShippedProfile("parsimony")));
        var disposition = Assert.Single(tools, tool => tool.Name == "motif_record_parsimony_disposition");
        var recordTypes = Assert.Single(tools, tool => tool.Name == "motif_parsimony_record_types");
        var findingDisposition = Assert.Single(tools, tool => tool.Name == "motif_dispose_parsimony_finding");

        Assert.Equal(AgentClass.Draft, disposition.Tool.Class);
        Assert.False(disposition.Describe().Annotations!.ReadOnlyHint);
        Assert.Contains("only a person can Apply", disposition.Description, StringComparison.Ordinal);
        Assert.Contains("pending keep does not suppress findings", disposition.Description, StringComparison.Ordinal);
        Assert.Equal(AgentClass.Read, recordTypes.Tool.Class);
        Assert.True(recordTypes.Describe().Annotations!.ReadOnlyHint);
        Assert.Contains("never by its English name", recordTypes.Description, StringComparison.Ordinal);
        Assert.Equal(AgentClass.Draft, findingDisposition.Tool.Class);
        Assert.False(findingDisposition.Describe().Annotations!.ReadOnlyHint);
        Assert.Contains("Motif derives the subject", findingDisposition.Description, StringComparison.Ordinal);
        Assert.Contains("only a person can Apply", findingDisposition.Description, StringComparison.Ordinal);
        var listed = Listing(tools);
        var golden = Path.Combine(Path.GetDirectoryName(GoldenPath())!, "tools-list.parsimony.json");
        if (Environment.GetEnvironmentVariable("MOTIF_UPDATE_GOLDEN") == "1") File.WriteAllText(golden, listed);
        Assert.Equal(File.ReadAllText(golden).ReplaceLineEndings("\n"), listed.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void TheExperimentalParsimonyProfilePinsItsNamedReadAndDraftTools()
    {
        var tools = MotifMcpServer.Expose(ToolProfile.Load(ShippedProfile("parsimony-experimental")));
        var listed = Listing(tools);
        var golden = Path.Combine(Path.GetDirectoryName(GoldenPath())!, "tools-list.parsimony-experimental.json");

        Assert.Equal(25, tools.Count);
        Assert.Contains(tools, tool => tool.Name == "motif_parsimony_view");
        var slot = Assert.Single(tools, tool => tool.Name == "motif_add_affix_slot");
        Assert.Equal(AgentClass.Draft, slot.Tool.Class);
        Assert.Contains("only a person can Apply", slot.Description, StringComparison.Ordinal);
        Assert.Equal(AgentClass.Draft, Assert.Single(tools, tool => tool.Name == "motif_edit_adhoc_prohibition").Tool.Class);
        Assert.All(new[] { "motif_edit_affix_slot", "motif_edit_affix_template", "motif_edit_inflectional_affix" },
            name => Assert.Equal(AgentClass.Draft, Assert.Single(tools, tool => tool.Name == name).Tool.Class));
        var retirement = Assert.Single(tools, tool => tool.Name == "motif_retire_allomorph");
        Assert.Equal(AgentClass.Draft, retirement.Tool.Class);
        Assert.Contains("exact destination", retirement.Description, StringComparison.Ordinal);
        Assert.Contains("owned dependents", retirement.Description, StringComparison.Ordinal);
        var rule = Assert.Single(tools, tool => tool.Name == "motif_add_phonological_rule");
        Assert.Equal(AgentClass.Draft, rule.Tool.Class);
        Assert.Contains("at most one", rule.Description, StringComparison.Ordinal);
        Assert.DoesNotContain(tools, tool => tool.Name.Contains("apply", StringComparison.Ordinal));
        Assert.DoesNotContain(tools, tool => tool.Name.Contains("negative", StringComparison.Ordinal));
        var template = Assert.Single(tools, tool => tool.Name == "motif_add_affix_template");
        Assert.Equal(AgentClass.Draft, template.Tool.Class);
        Assert.False(template.Describe().Annotations!.ReadOnlyHint);
        Assert.Contains("only a person can Apply", template.Description, StringComparison.Ordinal);
        if (Environment.GetEnvironmentVariable("MOTIF_UPDATE_GOLDEN") == "1") File.WriteAllText(golden, listed);
        Assert.Equal(File.ReadAllText(golden).ReplaceLineEndings("\n"), listed.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void ReviewedNegativeConfirmationAndRetractionAreNeverExposedAsAgentTools()
    {
        foreach (var name in new[] { "parsimony negative confirm", "parsimony negative retract" })
        {
            var command = Assert.Single(CommandCatalog.All, item => item.Name == name);
            Assert.Equal(AgentClass.HumanOnly, command.Agent);
            Assert.Null(command.AgentTool);
            Assert.DoesNotContain(MotifMcpServer.Expose(ToolProfile.Load(ShippedProfile("parsimony"))),
                tool => tool.Name.Contains("negative", StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void TheDefaultProfileExposesBetweenTenAndFifteenTools()
    {
        var tools = MotifMcpServer.Expose(ToolProfile.Builtin);

        Assert.InRange(tools.Count, 10, 15);
    }

    [Fact]
    public void NoHumanOnlyCommandIsServedByAnyTool()
    {
        foreach (var command in CommandCatalog.All.Where(command => command.Agent == AgentClass.HumanOnly))
            Assert.Null(command.AgentTool);
        foreach (var verb in AlwaysHumanOnly)
            Assert.Equal(AgentClass.HumanOnly, Assert.Single(CommandCatalog.All, command => command.Name == verb).Agent);
    }

    [Fact]
    public void EveryToolNamedByTheCatalogExistsAndEveryCommandItServesIsNotHumanOnly()
    {
        var registry = AgentTools.Names;
        var named = CommandCatalog.All.Where(command => command.AgentTool is not null).ToList();

        Assert.All(named, command =>
        {
            Assert.Contains(command.AgentTool!, registry);
            Assert.NotEqual(AgentClass.HumanOnly, command.Agent);
        });
    }

    [Fact]
    public void AProfileThatExposesEveryToolStillOffersNoHumanOnlyCommand()
    {
        var everything = new ToolProfile("all", null, null, "concise",
            AgentTools.Names.Order(StringComparer.Ordinal).Select(name => new ToolSelection(name, null, null, null)).ToList());

        var tools = MotifMcpServer.Expose(everything);

        Assert.Equal(AgentTools.Names.Count, tools.Count);
        Assert.All(tools, tool => Assert.NotEqual(AgentClass.HumanOnly, tool.Tool.Class));
        var servedCommands = CommandCatalog.All.Where(command => command.AgentTool is not null)
            .Select(command => command.Name).ToHashSet();
        Assert.DoesNotContain(servedCommands, name => AlwaysHumanOnly.Contains(name));
    }

    [Fact]
    public void AProfileThatRenamesAndHidesToolsChangesTheListing()
    {
        var profile = ToolProfile.Load(ShippedProfile("lean"));

        var tools = MotifMcpServer.Expose(profile);

        Assert.Equal(10, tools.Count);
        Assert.Contains(tools, tool => tool.Name == "motif_new_proposal");
        Assert.DoesNotContain(tools, tool => tool.Name == "motif_start_proposal");
        Assert.DoesNotContain(tools, tool => tool.Name == "motif_activity");
        Assert.Equal("Start a draft Proposal.", tools.Single(tool => tool.Name == "motif_new_proposal").Description);
        Assert.Null(profile.Instructions);
    }

    [Fact]
    public void AProfileNamingAToolThatDoesNotExistIsRefusedAtStartup()
    {
        var profile = new ToolProfile("bad", null, null, "concise",
            [new ToolSelection("motif_apply", null, null, null)]);

        var failure = Assert.Throws<ProfileException>(() => MotifMcpServer.Expose(profile));

        Assert.Contains("motif_apply", failure.Message);
    }

    [Fact]
    public void TwoToolsGivenTheSameNameAreRefused()
    {
        var profile = new ToolProfile("twin", null, null, "concise",
        [
            new ToolSelection("motif_overview", "same", null, null),
            new ToolSelection("motif_word", "same", null, null),
        ]);

        Assert.Throws<ProfileException>(() => MotifMcpServer.Expose(profile));
    }

    private static string ShippedProfile(string name) =>
        Path.Combine(AppContext.BaseDirectory, "profiles", name + ".json");

    private static string Listing(IEnumerable<ExposedTool> tools) =>
        new JsonArray(tools.Select(tool =>
        {
            var described = tool.Describe();
            return (JsonNode)new JsonObject
            {
                ["name"] = described.Name,
                ["description"] = described.Description,
                ["inputSchema"] = JsonNode.Parse(described.InputSchema.GetRawText()),
                ["readOnlyHint"] = described.Annotations!.ReadOnlyHint,
                ["destructiveHint"] = described.Annotations.DestructiveHint,
                ["idempotentHint"] = described.Annotations.IdempotentHint,
            };
        }).ToArray()).ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n";

    private static string GoldenPath([CallerFilePath] string source = "") =>
        Path.Combine(Path.GetDirectoryName(source)!, "Golden", "tools-list.default.json");
}
