using SIL.Motif.Commands.Catalog;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Mcp;
using SIL.Motif.Mcp.Exposure;
using Xunit;

namespace SIL.Motif.Tests.Mcp;

[Trait("MotifTestLevel", "Unit")]
public sealed class ExposurePolicyTests
{
    private static ExposurePolicy PolicyFor(ToolProfile profile, bool advancedAi = true) =>
        ExposurePolicy.Create(new ExposureSettings(new CommandSurfacePolicy(false, advancedAi), profile), AgentTools.All);

    public static TheoryData<string> ShippedProfiles()
    {
        var data = new TheoryData<string> { "builtin" };
        foreach (var path in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "profiles"), "*.json").Order())
            data.Add(Path.GetFileNameWithoutExtension(path));
        return data;
    }

    private static ToolProfile Profile(string name) => name == "builtin" ? ToolProfile.Builtin : ToolProfile.Load(name);

    private static string GoldenPath(string profile, [System.Runtime.CompilerServices.CallerFilePath] string source = "") =>
        Path.Combine(Path.GetDirectoryName(source)!, "Golden", "tools-list." + (profile == "builtin" ? "default" : profile) + ".json");

    // The expectation is frozen: the tool-list golden where one exists, otherwise the profile's own explicit list.
    private static List<(string Name, string? Description)> FrozenExpectation(string name, ToolProfile profile)
    {
        var path = GoldenPath(name);
        if (File.Exists(path))
            return [.. System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!.AsArray()
                .Select(node => (node!["name"]!.GetValue<string>(), (string?)node["description"]!.GetValue<string>()))];
        return [.. profile.Tools.Select(selection => (selection.As ?? selection.Tool, selection.Description))];
    }

    [Theory]
    [MemberData(nameof(ShippedProfiles))]
    public void ThePolicyExposesExactlyTheToolsTheProfileListsInOrder(string name)
    {
        var profile = Profile(name);
        var expected = FrozenExpectation(name, profile);
        var actual = PolicyFor(profile).Tools;

        Assert.Equal(expected.Select(tool => tool.Name), actual.Select(tool => tool.Name));
        for (var index = 0; index < expected.Count; index++)
            if (expected[index].Description is { } description) Assert.Equal(description, actual[index].Description);
        Assert.Equal(actual.Select(tool => tool.Name), MotifMcpServer.Expose(profile).Select(tool => tool.Name));
    }

    [Fact]
    public void TheDefaultProfileNamesOnlyToolsThatExistAndEachOnceInTheServersOrder()
    {
        var known = AgentTools.All.Select(tool => tool.Name).ToList();
        var listed = ToolProfile.Builtin.Tools.Select(selection => selection.Tool).ToList();

        Assert.NotEmpty(listed);
        Assert.Empty(listed.Except(known));
        Assert.Equal(listed.Count, listed.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(known.Where(listed.Contains), listed);
        Assert.False(ToolProfile.Builtin.AllTools);
        Assert.All(ToolProfile.Builtin.Tools, selection => Assert.Null(selection.AlwaysLoad));
    }

    [Fact]
    public void AProfileMayExposeEveryToolAndAProfileWithNoToolListIsRefused()
    {
        var all = new ToolProfile("all", null, null, "concise", [], AllTools: true);

        Assert.Equal(AgentTools.All.Select(tool => tool.Name), PolicyFor(all).Tools.Select(tool => tool.Name));

        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var path = Path.Combine(directory.FullName, "bare.json");
            File.WriteAllText(path, "{ \"name\": \"bare\" }");
            Assert.Throws<ProfileException>(() => ToolProfile.Load(path));
            File.WriteAllText(path, "{ \"name\": \"full\", \"allTools\": true }");
            Assert.True(ToolProfile.Load(path).AllTools);
        }
        finally { directory.Delete(true); }
    }

    private static string ThisFile([System.Runtime.CompilerServices.CallerFilePath] string source = "") => source;

    [Fact]
    public void NoSourceFileMentionsAnOnByDefaultSwitch()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(ThisFile())!, "..", "..", "src"));
        var sep = Path.DirectorySeparatorChar;
        var hits = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{sep}obj{sep}") && !path.Contains($"{sep}bin{sep}"))
            .Where(path => File.ReadAllText(path).Contains("OnByDefault", StringComparison.Ordinal))
            .ToList();

        Assert.Empty(hits);
    }

    [Theory]
    [MemberData(nameof(ShippedProfiles))]
    public void AdvancedAiOffExposesNoTools(string name)
    {
        var policy = PolicyFor(Profile(name), advancedAi: false);

        Assert.Empty(policy.Tools);
        Assert.Empty(policy.RouteTable);
        Assert.False(policy.Exposes("overview"));
        Assert.Equal(SIL.Motif.Commands.Preferences.FileAdvancedAiModePreferenceStore.DisabledServerInstructions,
            policy.Instructions);
    }

    [Fact]
    public void EveryRouteNamesACataloguedCommand()
    {
        var names = CommandCatalog.All.Select(command => command.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var tool in AgentTools.All)
            foreach (var route in tool.Routes)
            {
                Assert.Contains(route.Command, names);
                Assert.Equal(CommandCatalog.All.Single(command => command.Name == route.Command).Agent, route.Class);
            }
        Assert.Contains(AgentTools.All, tool => tool.Routes.Any(route => route.Selector is not null));
    }

    [Theory]
    [MemberData(nameof(ShippedProfiles))]
    public void NoProfileReachesAHumanOnlyOrDeveloperCommand(string name)
    {
        var policy = PolicyFor(Profile(name));
        foreach (var command in policy.RouteTable.Keys)
        {
            var descriptor = CommandCatalog.All.Single(candidate => candidate.Name == command);
            Assert.True(ExposurePolicy.IsAgentEligible(descriptor.Surface, descriptor.Agent), command);
            Assert.NotEqual(CommandSurface.Developer, descriptor.Surface);
            Assert.NotEqual(AgentClass.HumanOnly, descriptor.Agent);
        }
    }

    [Fact]
    public void ADedicatedToolBeatsASelectorRouteForTheSameVerb()
    {
        var policy = PolicyFor(ToolProfile.Builtin);

        var phoneme = Assert.IsType<CommandExposure.Tool>(policy.Resolve("compose-author-phoneme"));
        Assert.Equal("motif_add_phoneme", phoneme.ToolName);
        Assert.Null(phoneme.Selector);
        Assert.Contains("draft", phoneme.ArgNames);
    }

    [Fact]
    public void TheComposeToolBeatsTheBatchForAVerbBothServeWhateverTheToolOrder()
    {
        var compose = new ToolSelection("motif_compose", null, null, null);
        var batch = new ToolSelection("motif_compose_batch", null, null, null);
        foreach (var tools in new[] { new[] { compose, batch }, new[] { batch, compose } })
        {
            var policy = PolicyFor(new ToolProfile("both", null, null, "concise", tools));
            var step = Assert.IsType<CommandExposure.Tool>(policy.Resolve("compose-author-phoneme"));
            Assert.Equal("motif_compose", step.ToolName);
            Assert.Equal(new ToolSelector("steps[].verb", "compose-author-phoneme"), step.Selector);
        }
    }

    [Fact]
    public void EachSelectorPathNamesTheVerbDiscriminatorOfItsToolsRealSchema()
    {
        foreach (var tool in AgentTools.All.Where(tool => tool.VerbSelector is not null))
        {
            var path = tool.VerbSelector!.Split("[].");
            var items = tool.InputSchema["properties"]![path[0]]!["items"]!["oneOf"]!.AsArray();
            var verbs = items.Select(branch => branch!["properties"]![path[1]]!["enum"]![0]!.GetValue<string>()).ToHashSet();
            var routed = tool.Routes.Select(route => route.Command).ToHashSet();

            Assert.NotEmpty(routed);
            Assert.All(routed, verb => Assert.Contains(verb, verbs));
            Assert.All(tool.Routes, route => Assert.Equal(tool.VerbSelector, route.Selector!.Argument));
        }
    }

    [Fact]
    public void AVerbWithNoDedicatedToolIsReachedByTheBatchSelector()
    {
        var profile = new ToolProfile("batch-only", null, null, "concise", [new ToolSelection("motif_compose_batch", null, null, null)]);
        var policy = PolicyFor(profile);

        var step = Assert.IsType<CommandExposure.Tool>(policy.Resolve("compose-author-phoneme"));
        Assert.Equal("motif_compose_batch", step.ToolName);
        Assert.Equal(new ToolSelector("intents[].verb", "compose-author-phoneme"), step.Selector);
    }

    [Fact]
    public void ACommandNoToolReachesIsAStepForThePerson()
    {
        var policy = PolicyFor(ToolProfile.Builtin);

        Assert.False(policy.Exposes("apply"));
        Assert.Equal(new CommandExposure.PersonStep("apply"), policy.Resolve("apply"));
    }

    [Fact]
    public void ANextStepNamingATypedComposerRendersAsTheToolThatServesIt()
    {
        var refusal = new Refusal("test.next", FailureReason.Refused, "Not yet.");
        var step = new NextStep("compose-author-feature-value", new Dictionary<string, NextArg>());
        var policy = PolicyFor(ToolProfile.Builtin);

        var text = RefusalMapper.RenderStep(step, refusal, policy, null);

        Assert.Equal(" Call `motif_add_feature_value`.", text);
    }
}
