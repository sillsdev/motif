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

    // The selection loop as it stood before the policy owned it, kept as the reference the policy must equal.
    private static List<ExposedTool> ReferenceExpose(ToolProfile profile)
    {
        var exposed = new List<ExposedTool>();
        foreach (var tool in AgentTools.All)
            if (profile.SelectionFor(tool.Name, tool.OnByDefault) is { } selection)
                exposed.Add(new ExposedTool(tool, selection.As ?? tool.Name, selection.Description ?? tool.Description,
                    selection.Detail ?? profile.DefaultDetail));
        return exposed;
    }

    [Theory]
    [MemberData(nameof(ShippedProfiles))]
    public void ThePolicyExposesExactlyWhatTheProfileSelectionAlwaysDid(string name)
    {
        var profile = Profile(name);
        var expected = ReferenceExpose(profile);
        var actual = PolicyFor(profile).Tools;

        Assert.Equal(expected.Count, actual.Count);
        for (var index = 0; index < expected.Count; index++)
        {
            Assert.Same(expected[index].Tool, actual[index].Tool);
            Assert.Equal(expected[index].Name, actual[index].Name);
            Assert.Equal(expected[index].Description, actual[index].Description);
            Assert.Equal(expected[index].DefaultDetail, actual[index].DefaultDetail);
            Assert.Equal(System.Text.Json.JsonSerializer.Serialize(expected[index].Describe()),
                System.Text.Json.JsonSerializer.Serialize(actual[index].Describe()));
        }
        Assert.Equal(expected.Select(tool => tool.Name), MotifMcpServer.Expose(profile).Select(tool => tool.Name));
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
