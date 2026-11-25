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

    [Fact]
    public void AnExplicitLimitOutranksTheConfiguredOne() =>
        Assert.Equal(Flag, ProjectWorkLimit.Resolve(Flag, Project));

    [Fact]
    public void TheConfiguredLimitOutranksTheDefault() =>
        Assert.Equal(Project, ProjectWorkLimit.Resolve(null, Project));

    [Fact]
    public void WithNothingSetTheDefaultApplies() =>
        Assert.Equal(ParserWorkLimit.Default, ProjectWorkLimit.Resolve(null, null));

    [Fact]
    public void AnInvalidProjectSettingIsRefusedNotSwallowed()
    {
        var folder = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var fwdata = Path.Combine(folder, "p.fwdata");
            File.WriteAllText(fwdata, "<x/>");
            File.WriteAllText(Path.Combine(folder, "p.motif.toml"), "[parser]\nwork-limit = \"lots\"\n");

            Assert.Throws<SIL.Motif.Host.Config.ProjectConfigurationException>(() => ProjectWorkLimit.ForProject(fwdata));
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void WithoutAStoreTheProjectSettingStillApplies()
    {
        var folder = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var fwdata = Path.Combine(folder, "p.fwdata");
            File.WriteAllText(fwdata, "<x/>");
            File.WriteAllText(Path.Combine(folder, "p.motif.toml"), "[parser]\nwork-limit = 60000000\n");

            Assert.Equal(Project, ProjectWorkLimit.ForProject(fwdata).Limit);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Theory]
    [InlineData("work_limit", true)]
    [InlineData("timed_out", true)]
    [InlineData("invalid_shape", true)]
    [InlineData("unrelated", false)]
    public void ANonzeroExitIsReadOnlyWhenTheTraceRecordsAStoppedSearch(string reason, bool accepted)
    {
        var folder = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var trace = Path.Combine(folder, PanGlossRequest.RejectionTrace.ArtifactName);
            File.WriteAllText(trace,
                "{\"type\":\"header\"}\n{\"type\":\"word_end\",\"search\":{\"status\":\"stopped\",\"stopReasons\":[\"" + reason + "\"]}}\n");

            Assert.Equal(accepted, new PanGlossRequest.RejectionTrace("g.fwdata", "w", 3) { WorkLimit = Project }
                .AcceptsNonzeroExit("", folder));
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void ACompileFailureWithNoTraceKeepsItsTypedRefusal()
    {
        var folder = Directory.CreateTempSubdirectory().FullName;
        try
        {
            Assert.False(new PanGlossRequest.RejectionTrace("g.fwdata", "w", 3) { WorkLimit = Project }
                .AcceptsNonzeroExit("", folder));
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void ATraceWithNoRecordedStopKeepsItsRefusalEvenWithALimit()
    {
        var folder = Directory.CreateTempSubdirectory().FullName;
        try
        {
            File.WriteAllText(Path.Combine(folder, PanGlossRequest.RejectionTrace.ArtifactName),
                "{\"type\":\"word_end\",\"search\":{\"status\":\"complete\",\"stopReasons\":[]}}\n");

            Assert.False(new PanGlossRequest.RejectionTrace("g.fwdata", "w", 3) { WorkLimit = Project }
                .AcceptsNonzeroExit("", folder));
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

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
}
