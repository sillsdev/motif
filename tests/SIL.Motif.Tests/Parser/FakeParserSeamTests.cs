using SIL.Motif.Host.Parser;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Parser;

/// <summary>
/// Parked until a pending grill decision names who produces the assessment report the real binary has
/// no subcommand for; the fake no longer answers <c>assess</c>, so these are skipped rather than deleted.
/// </summary>
public sealed class FakeParserSeamTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "motif-fake-parser-" + Guid.NewGuid().ToString("N"));

    public FakeParserSeamTests() => Directory.CreateDirectory(_root);

    [Fact(Skip = "The shipped pangloss has no assess subcommand (grill K46); the fake no longer pretends otherwise.")]
    public async Task AnAssessmentRoundTripsThroughTheRealProcessBoundary()
    {
        var candidate = Candidate("ok");

        var report = await Run(candidate);

        Assert.Equal("foma-confirm", report.Pipeline);
        var word = Assert.Single(report.Words);
        Assert.Equal("motifa", word.Word);
        // Interned keys resolved through the report's own table, which is the whole point of reading one.
        Assert.Equal("11111111-1111-1111-1111-111111111111", Assert.Single(word.Analyses).MorphemeGuids[0]);
    }

    [Fact(Skip = "The shipped pangloss has no assess subcommand (grill K46); the fake no longer pretends otherwise.")]
    public async Task TheParserIsHandedTheExportedGrammarSourceRatherThanTheDirectory()
    {
        var candidate = Candidate("names-source");

        var report = await Run(candidate);

        // The fake echoes what it was given; a directory here would mean the dispatch never found the file.
        Assert.NotEmpty(report.OutcomeDigest);
    }

    [Fact(Skip = "The shipped pangloss has no assess subcommand (grill K46); the fake no longer pretends otherwise.")]
    public async Task WhateverPipelineTheParserReportsIsWhatMotifStores()
    {
        var candidate = Candidate("pipeline");
        FakeParser.Behave(candidate, new { pipeline = "fst-only" });

        var report = await Run(candidate);

        // Motif sends no mode and records the one it was given; it must not substitute a preferred value.
        Assert.Equal("fst-only", report.Pipeline);
    }

    [Fact(Skip = "The shipped pangloss has no assess subcommand (grill K46); the fake no longer pretends otherwise.")]
    public async Task AParserThatExitsWithoutWritingIsRefusedRatherThanReadAsEmpty()
    {
        var candidate = Candidate("no-report");
        FakeParser.Behave(candidate, new { mode = "noReport" });

        var failure = await Record.ExceptionAsync(() => Run(candidate));

        // A clean exit code with no report is the shape most easily mistaken for "the grammar parsed nothing".
        Assert.IsType<ParserUnavailableException>(failure);
    }

    [Fact(Skip = "The shipped pangloss has no assess subcommand (grill K46); the fake no longer pretends otherwise.")]
    public async Task AParserThatFailsIsDistinguishedFromOneThatParsedNothing()
    {
        var candidate = Candidate("fail");
        FakeParser.Behave(candidate, new { mode = "fail", exitCode = 3, standardError = "grammar exploded" });

        var failure = await Record.ExceptionAsync(() => Run(candidate));

        Assert.NotNull(failure);
        Assert.Contains("grammar exploded", failure!.Message, StringComparison.Ordinal);
    }

    [Fact(Skip = "The shipped pangloss has no assess subcommand (grill K46); the fake no longer pretends otherwise.")]
    public async Task AnUnreadableReportIsRefusedRatherThanPartlyBelieved()
    {
        var candidate = Candidate("malformed");
        FakeParser.Behave(candidate, new { mode = "malformedReport" });

        var failure = await Record.ExceptionAsync(() => Run(candidate));

        Assert.NotNull(failure);
    }

    [Fact(Skip = "The shipped pangloss has no assess subcommand (grill K46); the fake no longer pretends otherwise.")]
    public async Task DiagnosticsTravelWithTheReportRatherThanBeingDropped()
    {
        var candidate = Candidate("diagnostics");
        FakeParser.Behave(candidate, new { diagnosticCount = 137 });

        var report = await Run(candidate);

        // A coverage figure computed while these were ignored is not a figure about the grammar.
        Assert.Equal(137, report.DiagnosticCount);
    }

    [Fact(Skip = "The shipped pangloss has no assess subcommand (grill K46); the fake no longer pretends otherwise.")]
    public async Task CancellingTheAssessmentStopsTheParserProcess()
    {
        var candidate = Candidate("slow");
        var heartbeat = Path.Combine(_root, "heartbeat.txt");
        FakeParser.Behave(candidate, new { heartbeatPath = heartbeat });
        using var cancellation = new CancellationTokenSource();

        var run = Run(candidate, cancellation.Token);
        for (var i = 0; i < 200 && !File.Exists(heartbeat); i++) await Task.Delay(25);
        Assert.True(File.Exists(heartbeat), "The fake parser never started ticking.");
        await cancellation.CancelAsync();
        await Record.ExceptionAsync(() => run);

        var stoppedAt = File.ReadAllText(heartbeat);
        await Task.Delay(300);
        // Still ticking here would mean cancellation abandoned the process rather than killing it.
        Assert.Equal(stoppedAt, File.ReadAllText(heartbeat));
    }

    private static Task<AssessReport> Run(string candidate, CancellationToken cancellationToken = default) =>
        new PanGlossAssessmentProcess(FakeParser.ExecutablePath).RunAsync(candidate, cancellationToken);

    private string Candidate(string name)
    {
        var directory = Path.Combine(_root, name);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "candidate.fwdata"), "the fake parser never reads this.");
        return directory;
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); }
        catch (DirectoryNotFoundException) { }
        catch (IOException) { }
    }
}
