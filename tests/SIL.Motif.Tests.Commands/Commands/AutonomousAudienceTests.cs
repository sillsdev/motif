using SIL.Motif.Commands;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Commands;
using Xunit;

namespace SIL.Motif.Tests.Commands;

[Trait("MotifTestLevel", "Unit")]
public sealed class AutonomousAudienceTests
{
    private static CommandDescriptor Command(string name) => CommandCatalog.All.Single(command => command.Name == name);

    [Theory]
    [InlineData(null, false, false)]
    [InlineData("", false, false)]
    [InlineData("0", false, false)]
    [InlineData("1", false, true)]
    [InlineData("true", false, true)]
    [InlineData(null, true, true)]
    public void TheAudienceOpensOnlyForTheEnvironmentValueOrTheFlag(string? value, bool flag, bool enabled)
    {
        var policy = CommandSurfacePolicy.FromEnvironment(null, autonomousValue: value, autonomousFlag: flag);

        Assert.Equal(enabled, policy.AutonomousEnabled);
        Assert.Equal(enabled, policy.IsAvailable(Command("discard-draft")));
        Assert.Equal(enabled, policy.IsAvailable(Command("discard-empty-drafts")));
    }

    [Fact]
    public void TheDeveloperSwitchDoesNotOpenAutonomousAndAutonomousDoesNotOpenDeveloper()
    {
        var developerOnly = CommandSurfacePolicy.FromEnvironment("1");
        var autonomousOnly = CommandSurfacePolicy.FromEnvironment(null, autonomousValue: "1");

        Assert.False(developerOnly.IsAvailable(Command("discard-draft")));
        Assert.True(developerOnly.IsAvailable(Command("word read-state")));
        Assert.True(autonomousOnly.IsAvailable(Command("discard-draft")));
        Assert.False(autonomousOnly.IsAvailable(Command("word read-state")));
    }

    [Fact]
    public void AutonomousCommandsAreNeverAgentToolsWhateverTheirClass()
    {
        var autonomous = CommandCatalog.All.Where(command => command.Surface == CommandSurface.Autonomous).ToArray();

        Assert.NotEmpty(autonomous);
        Assert.All(autonomous, command => Assert.False(command.IsAgentTool, command.Name));
        Assert.All(autonomous, command => Assert.False(CommandSurfacePolicy.IsAgentEligible(command.Surface, AgentClass.Read), command.Name));
    }

    [Fact]
    public void TheRefusalForAnAutonomousCommandNamesTheSwitchAndTheFlag()
    {
        var facts = CommandAvailabilityFacts.For(Command("discard-draft"));
        var text = CommandAvailabilityText.Render(facts);

        Assert.Equal("command.autonomous-required", facts.RefusalCode);
        Assert.Equal("MOTIF_AUTONOMOUS", facts.Setting);
        Assert.Contains("MOTIF_AUTONOMOUS=1", text, StringComparison.Ordinal);
        Assert.Contains("--autonomous", text, StringComparison.Ordinal);
        Assert.DoesNotContain("MOTIF_DEVELOPER_COMMANDS", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("hypothesis add")]
    [InlineData("hypothesis test")]
    [InlineData("hypothesis decide")]
    [InlineData("hypothesis link")]
    [InlineData("hypothesis grade")]
    [InlineData("hypothesis answer")]
    [InlineData("hypothesis neighbours")]
    [InlineData("hypothesis report")]
    [InlineData("list hypotheses")]
    public void TheHypothesisLedgerVerbsAreReleasedAndNeverAgentTools(string name)
    {
        var command = Command(name);
        Assert.Equal(CommandSurface.Released, command.Surface);
        Assert.False(command.IsAgentTool);
    }

    [Theory]
    [InlineData("discard-draft")]
    [InlineData("discard-empty-drafts")]
    [InlineData("reopen")]
    [InlineData("duplicate")]
    [InlineData("defer")]
    [InlineData("undefer")]
    [InlineData("supersede")]
    [InlineData("split")]
    [InlineData("promote-gloss")]
    [InlineData("preflight")]
    public void TheAgentWorkflowCommandsAreOnTheAutonomousSurface(string name) =>
        Assert.Equal(CommandSurface.Autonomous, Command(name).Surface);

    [Theory]
    [InlineData("word read-state")]
    [InlineData("inspect")]
    [InlineData("uses")]
    [InlineData("store artifacts")]
    [InlineData("pending-changes")]
    public void TheDeveloperDiagnosticsStayOnTheDeveloperSurface(string name) =>
        Assert.Equal(CommandSurface.Developer, Command(name).Surface);

    [Fact]
    public void TheCliDoesNotDiscardThePendingChangesDraftByName()
    {
        var outcome = ProposalCommands.DiscardNamedDraft(
            new DiscardDraftRequest("unused.fwdata", "0.1.0", PendingChanges.DraftName));

        Assert.False(outcome.Succeeded);
        Assert.Equal(RefusalCodes.DraftDiscardPendingChanges, outcome.Refusal!.Code);
        Assert.Contains("window", outcome.Refusal.Message, StringComparison.Ordinal);
    }
}
