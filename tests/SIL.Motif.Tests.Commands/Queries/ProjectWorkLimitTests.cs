using System.Diagnostics;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Host.PanGloss;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary>Every trace and comparison resolves its parser work limit the same way, and the parser is told.</summary>
public sealed class ProjectWorkLimitTests
{
    private static readonly ParserWorkLimit Flag = new(1_000);
    private static readonly ParserWorkLimit Project = new(60_000_000);
    private static readonly ParserWorkLimit Saved = new(5_000_000);

    [Fact]
    public void AnExplicitLimitOutranksTheProjectSettingTheSavedLimitAndTheDefault() =>
        Assert.Equal(Flag, ProjectWorkLimit.Resolve(Flag, Project, Saved));

    [Fact]
    public void TheProjectSettingOutranksTheSavedLimit() =>
        Assert.Equal(Project, ProjectWorkLimit.Resolve(null, Project, Saved));

    [Fact]
    public void TheSavedLimitOutranksTheDefault() =>
        Assert.Equal(Saved, ProjectWorkLimit.Resolve(null, null, Saved));

    [Fact]
    public void WithNothingSetTheDefaultApplies() =>
        Assert.Equal(ParserWorkLimit.Default, ProjectWorkLimit.Resolve(null, null, null));

    [Fact]
    public void ARejectionTraceRequestPassesTheWorkLimitToTheParser()
    {
        var grammar = Path.GetTempFileName();
        try
        {
            var request = new PanGlossRequest.RejectionTrace(grammar, "word", 3) { WorkLimit = Project };
            var start = new ProcessStartInfo();

            request.AddArguments(start, "scratch");

            var arguments = start.ArgumentList.ToList();
            Assert.Equal("60000000", arguments[arguments.IndexOf("--work-cap") + 1]);
        }
        finally
        {
            File.Delete(grammar);
        }
    }

    [Fact]
    public void ARejectionTraceRequestWithNoLimitLeavesTheParsersOwnDefault()
    {
        var start = new ProcessStartInfo();

        new PanGlossRequest.RejectionTrace("g.fwdata", "word", 3).AddArguments(start, "scratch");

        Assert.DoesNotContain("--work-cap", start.ArgumentList);
    }

    [Fact]
    public void ACappedParseExitIsReadWhenALimitWasPassedAndRefusedWhenNoneWas()
    {
        Assert.True(new PanGlossRequest.RejectionTrace("g.fwdata", "word", 3) { WorkLimit = Project }.AcceptsNonzeroExit(""));
        Assert.False(new PanGlossRequest.RejectionTrace("g.fwdata", "word", 3).AcceptsNonzeroExit(""));
    }
}
