using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Mcp;
using Xunit;

namespace SIL.Motif.Tests.Mcp;

public sealed class ToolSurfaceTests
{
    [Fact]
    public void EveryEligibleCommandHasAToolAndNoOtherCommandDoes()
    {
        foreach (var command in CommandCatalog.All)
            Assert.Equal(command.Surface is CommandSurface.AdvancedAi or CommandSurface.Released &&
                command.Agent != AgentClass.HumanOnly,
                AgentTools.All.Any(tool => tool.Requests.Contains(command.RequestType)));
        Assert.All(AgentTools.All, tool => Assert.NotEqual(AgentClass.HumanOnly, tool.Class));
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
            new SIL.Motif.Commands.JobRunnerLaunchOptions("root", null)), new ActivityLog(null), ToolProfile.Builtin, TextWriter.Null);
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
