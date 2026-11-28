using System;
using System.Linq;
using SIL.Motif.Cli;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.Cli;

/// <summary>The CLI gives a failure the MCP contract: a <c>next</c> sentence, and <c>--json</c> on every write verb.</summary>
public sealed class CliFailureContractTests
{
    [Theory]
    [InlineData(FailureReason.InvalidArgument)]
    [InlineData(FailureReason.NotFound)]
    [InlineData(FailureReason.Refused)]
    [InlineData(FailureReason.Cancelled)]
    [InlineData(FailureReason.StoppedByBudget)]
    [InlineData(FailureReason.Busy)]
    [InlineData(FailureReason.StoreInconsistent)]
    public void EveryReasonGetsASteeringSentence(FailureReason reason)
    {
        var envelope = CliNext.Envelope(reason, "It failed.");

        Assert.False(string.IsNullOrWhiteSpace(envelope.Next));
        Assert.DoesNotContain("motif_", envelope.Next, StringComparison.Ordinal);
    }

    [Fact]
    public void ACodeWithANextStepNamesAMotifLineNotAnMcpTool()
    {
        var help = SIL.Motif.Help.HelpCatalog.Load();
        var spec = RefusalCatalog.Specs.Values.First(candidate => candidate.Next is { } step &&
            step.Command is not (NextStep.RepeatCall or NextStep.AskPerson) && help.RefusalWhy(candidate.Code) is not null);
        var envelope = CliNext.Envelope(FailureReason.Refused, "It failed.", code: spec.Code);

        Assert.Contains("`motif " + spec.Next!.Command, envelope.Next, StringComparison.Ordinal);
        Assert.DoesNotContain("motif_", envelope.Next, StringComparison.Ordinal);
    }

    [Fact]
    public void ANextStepNamingAHumanOnlyVerbAsksThePerson()
    {
        var help = SIL.Motif.Help.HelpCatalog.Load();
        var humanOnly = RefusalCatalog.Specs.Values
            .Where(spec => spec.Next is { } step && help.RefusalWhy(spec.Code) is not null &&
                CommandCatalog.All.Any(command => command.Name == step.Command && command.Agent == AgentClass.HumanOnly))
            .ToList();

        foreach (var spec in humanOnly)
        {
            var next = CliNext.For(FailureReason.Refused, spec.Code);
            Assert.Contains("Ask the person to run `motif " + spec.Next!.Command, next, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void NextIsSerializedAndOmittedWhenAbsent()
    {
        var with = ProjectionJson.Serialize(new FailureEnvelope(FailureReason.Busy, "Locked.", next: "Wait."));
        var without = ProjectionJson.Serialize(new FailureEnvelope(FailureReason.Busy, "Locked."));

        Assert.Contains("\"next\": \"Wait.\"", with, StringComparison.Ordinal);
        Assert.DoesNotContain("\"next\"", without, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryCliVerbThatChangesStateAcceptsJson()
    {
        var missing = CliVerbCatalog.All
            .Where(verb => CommandCatalog.All.Single(command => command.Name == verb.CommandName).Agent != AgentClass.Read)
            .Where(verb => !verb.UsageLines.Any(line => line.Contains("--json", StringComparison.Ordinal)))
            .Select(verb => verb.CommandName)
            .ToList();

        Assert.Empty(missing);
    }

    [Fact]
    public void ExitCodeSixIsDocumentedAsTheTrialLoss()
    {
        var guide = SIL.Motif.Help.HelpCatalog.Load().Entries
            .Single(entry => entry.Kind == SIL.Motif.Help.HelpEntryKind.Guide &&
                entry.Code.EndsWith("output-and-exit-codes", StringComparison.Ordinal));

        Assert.Equal(6, FailureEnvelope.TrialLossExitCode);
        Assert.Contains("`6`", guide.HelpPage, StringComparison.Ordinal);
        Assert.Contains("\"next\"", guide.HelpPage, StringComparison.Ordinal);
    }
}
