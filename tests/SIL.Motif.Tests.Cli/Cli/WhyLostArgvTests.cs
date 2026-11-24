using SIL.Motif.Cli.Rendering;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Cli;

/// <summary><c>proposal why-lost</c> through the released executable and its text rendering.</summary>
public sealed class WhyLostArgvTests : IDisposable
{
    private readonly string _workerRoot = Path.Combine(Path.GetTempPath(), "motif-why-lost-worker-" + Guid.NewGuid().ToString("N"));

    public WhyLostArgvTests() => Directory.CreateDirectory(_workerRoot);

    public void Dispose()
    {
        try { Directory.Delete(_workerRoot, recursive: true); } catch (IOException) { }
    }

    private Task<CliProcessResult> Run(params string[] arguments) =>
        CliProcess.RunAsync(_workerRoot, null, false, ["proposal", "why-lost", .. arguments]);

    [Theory]
    [InlineData("--project", "p.fwdata", "--proposal", "x")]
    [InlineData("w", "--proposal", "x")]
    [InlineData("w", "--project", "p.fwdata")]
    [InlineData("w", "--project", "p.fwdata", "--proposal", "x", "--bogus", "y")]
    public async Task AMissingWordProjectOrProposalAndAnUnknownFlagPrintTheUsage(params string[] arguments)
    {
        var result = await Run(arguments);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("proposal why-lost <word> --project <fwdata> --proposal <id>", result.Error + result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AProposalIdMotifCannotReadIsRefusedWithItsCodeBeforeAnyProjectOpens()
    {
        var result = await Run("w", "--project", "never-opened.fwdata", "--proposal", "baseline", "--json");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("why-lost.request-invalid", result.Error + result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void TheTextNamesTheCountsTheLostReadingAndEachCause()
    {
        var search = new RejectionTraceSearch("complete", "complete", 1, 1, []);
        var producer = new RejectionTraceProducer("0.32.0", "b", "pangloss.diagnostic.v1", "lineage", "h", "s");
        var morph = new RejectionTraceMorph("authored", Object("entry", "E1"), Object("msa", "M1"), [Object("form", "F1")], null,
            "unique_association", []);
        var lost = new WhyLostReading("a+b", [morph])
        {
            Causes = [new WhyLostCause("recorded", "PanGloss recorded the form F1 as blocked by the earlier form F2.")],
        };
        var response = new WhyLostResponse("w", "P1", "job-1", new WhyLostSide("baseline", producer, search, 1, true),
            new WhyLostSide("proposal P1", producer, search, 0, true), [lost], [], [])
        { Notes = ["a note"] };

        var text = WhyLostRenderer.Render(response);

        Assert.Contains("Why lost, w: Baseline against proposal P1 (Trial job job-1).", text, StringComparison.Ordinal);
        Assert.Contains("Baseline 1 Reading, Trial copy 0 Readings: lost 1, gained 0, kept 0.", text, StringComparison.Ordinal);
        Assert.Contains("Lost Reading 1: [entry E1 msa M1 form F1]", text, StringComparison.Ordinal);
        Assert.Contains("  PanGloss recorded the form F1 as blocked by the earlier form F2.", text, StringComparison.Ordinal);
        Assert.Contains("Note: a note", text, StringComparison.Ordinal);
    }

    private static RejectionTraceObject Object(string kind, string id) =>
        new(kind, id, "authored", "exact", true, [new RejectionTraceId(id, kind, "authored", 0, true)], null);
}
