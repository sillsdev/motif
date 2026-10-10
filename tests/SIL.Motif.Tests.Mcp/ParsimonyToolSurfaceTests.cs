using System.IO.Pipelines;
using System.Text.Json.Nodes;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Mcp;
using Xunit;

namespace SIL.Motif.Tests.Mcp;

public sealed class ParsimonyToolSurfaceTests
{
    private static readonly string[] ExperimentalTools =
    [
        "motif_start_proposal",
        "motif_finish_proposal",
        "motif_dry_run",
        "motif_parsimony_measures",
        "motif_run_parsimony_measure",
        "motif_parsimony_report",
        "motif_retirement_review",
        "motif_parsimony_view",
        "motif_parsimony_record_types",
        "motif_dispose_parsimony_finding",
        "motif_revise_parsimony_disposition",
        "motif_retract_parsimony_disposition",
        "motif_retire_allomorph",
        "motif_edit_adhoc_prohibition",
        "motif_add_phonological_rule",
        "motif_add_natural_class",
        "motif_edit_natural_class",
        "motif_relink_natural_class",
        "motif_edit_affix_slot",
        "motif_edit_affix_template",
        "motif_edit_inflectional_affix",
        "motif_edit_allomorph_condition",
        "motif_order_allomorphs",
        "motif_add_affix_slot",
        "motif_add_affix_template",
    ];

    [Fact]
    public void ExperimentalProfileExposesOnlyTheNamedParsimonyWorkflowTools()
    {
        var profile = ToolProfile.Load(ShippedProfile("parsimony-experimental"));

        Assert.Equal(ExperimentalTools.Order(StringComparer.Ordinal),
            MotifMcpServer.Expose(profile).Select(tool => tool.Name).Order(StringComparer.Ordinal));
        Assert.Contains("experimental", profile.Description!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("only a person can Apply", profile.Instructions!, StringComparison.Ordinal);
    }

    [Fact]
    public void ParsimonyToolsStayOutOfDefaultAndPhonologyProfiles()
    {
        foreach (var profileName in new[] { "default", "phonology" })
        {
            var tools = MotifMcpServer.Expose(ToolProfile.Load(ShippedProfile(profileName)));

            Assert.DoesNotContain(tools, tool => tool.Name.StartsWith("motif_parsimony", StringComparison.Ordinal));
            Assert.DoesNotContain(tools, tool => tool.Name == "motif_run_parsimony_measure");
            Assert.DoesNotContain(tools, tool => tool.Name == "motif_add_affix_slot");
            Assert.DoesNotContain(tools, tool => tool.Name == "motif_add_affix_template");
            Assert.DoesNotContain(tools, tool => tool.Name is "motif_edit_natural_class" or "motif_relink_natural_class");
        }
    }

    [Fact]
    public void ParsimonyDraftToolsStateThatOnlyAPersonCanApply()
    {
        var tools = MotifMcpServer.Expose(ToolProfile.Load(ShippedProfile("parsimony-experimental")));

        var dispose = Assert.Single(tools, tool => tool.Name == "motif_dispose_parsimony_finding");
        Assert.Contains("only a person can Apply", dispose.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void ExperimentalProfileStagesDispositionRevisionsAndRetractionsWithoutApply()
    {
        var tools = MotifMcpServer.Expose(ToolProfile.Load(ShippedProfile("parsimony-experimental")));

        foreach (var name in new[]
                 { "motif_revise_parsimony_disposition", "motif_retract_parsimony_disposition" })
        {
            var tool = Assert.Single(tools, candidate => candidate.Name == name);
            Assert.Equal(AgentClass.Draft, tool.Tool.Class);
            Assert.False(tool.Describe().Annotations!.ReadOnlyHint);
            Assert.Contains("only a person can Apply", tool.Description, StringComparison.Ordinal);
        }

        Assert.DoesNotContain(tools, tool => tool.Name.Contains("apply", StringComparison.Ordinal));
        Assert.DoesNotContain(tools, tool => tool.Name.Contains("negative", StringComparison.Ordinal));
    }

    [Fact]
    public void ExperimentalProfileStagesAdhocEditsWithAnExplicitCurrentValue()
    {
        var tools = MotifMcpServer.Expose(ToolProfile.Load(ShippedProfile("parsimony-experimental")));
        var edit = Assert.Single(tools, tool => tool.Name == "motif_edit_adhoc_prohibition");

        Assert.Equal(AgentClass.Draft, edit.Tool.Class);
        Assert.False(edit.Describe().Annotations!.ReadOnlyHint);
        Assert.Contains("only a person can Apply", edit.Description, StringComparison.Ordinal);
        Assert.Contains("expected_disabled", edit.Tool.ArgumentNames);
        Assert.Contains("disabled", edit.Tool.ArgumentNames);
        Assert.DoesNotContain(tools, tool => tool.Name.Contains("apply", StringComparison.Ordinal));
    }

    [Fact]
    public void ExperimentalProfileStagesTypedMorphotacticsEditsForHumanReview()
    {
        var tools = MotifMcpServer.Expose(ToolProfile.Load(ShippedProfile("parsimony-experimental")));
        var expectedArguments = new Dictionary<string, string[]>
        {
            ["motif_add_affix_slot"] = ["draft", "category", "name", "ws", "optional", "assignments"],
            ["motif_add_affix_template"] =
                ["draft", "category", "name", "ws", "prefix_slots", "suffix_slots", "final"],
            ["motif_edit_affix_slot"] = ["draft", "target", "expected_optional", "optional"],
            ["motif_edit_affix_template"] =
                ["draft", "target", "expected_prefix_slots", "prefix_slots", "expected_suffix_slots", "suffix_slots"],
            ["motif_edit_inflectional_affix"] = ["draft", "target", "expected_slots", "slots"],
            ["motif_edit_allomorph_condition"] =
                ["draft", "target", "field", "expected_environments", "environments"],
            ["motif_order_allomorphs"] = ["draft", "target", "expected_alternates", "alternates"],
            ["motif_edit_natural_class"] = ["draft", "target", "expectedMembers", "members"],
            ["motif_relink_natural_class"] =
                ["draft", "source", "replacement", "replacementCreationOperation", "environments", "ruleContexts"],
        };

        foreach (var (name, arguments) in expectedArguments)
        {
            var tool = Assert.Single(tools, candidate => candidate.Name == name);
            Assert.Equal(AgentClass.Draft, tool.Tool.Class);
            Assert.False(tool.Describe().Annotations!.ReadOnlyHint);
            Assert.Contains("only a person can Apply", tool.Description, StringComparison.Ordinal);
            Assert.Equal(arguments, tool.Tool.ArgumentNames);
        }

        Assert.DoesNotContain(tools, tool => tool.Name.Contains("apply", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("sql")]
    [InlineData("query")]
    public void NamedViewRejectsAdHocQueryArguments(string argument)
    {
        var tool = MotifMcpServer.Expose(ToolProfile.Load(ShippedProfile("parsimony-experimental")))
            .Single(candidate => candidate.Name == "motif_parsimony_view").Tool;
        var args = new JsonObject
        {
            ["bundle_id"] = "bundle/one",
            ["view"] = "parsimony-suppressed",
            [argument] = "select * from evidence",
        };

        var refusal = Assert.Throws<ToolArgumentException>(() => new ToolArgs(args, tool.ArgumentNames));

        Assert.Equal("tool.unknown-argument", refusal.Code);
    }

    [Fact]
    public async Task AdvancedAiModeIsRequiredBeforeAnyProfileCanServeTools()
    {
        var toServer = new Pipe();
        var fromServer = new Pipe();
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => MotifMcpServer.RunAsync(
            new McpLaunchOptions("missing.fwdata", Profile: "parsimony-experimental"),
            toServer.Reader.AsStream(), fromServer.Writer.AsStream(), TextWriter.Null, CancellationToken.None));

        Assert.Contains("Advanced AI mode is required", exception.Message, StringComparison.Ordinal);
    }

    private static string ShippedProfile(string name) =>
        Path.Combine(AppContext.BaseDirectory, "profiles", name + ".json");
}
