using SIL.Motif.Cli.Rendering;
using SIL.Motif.Commands.Assess;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.Cli;

public sealed class AssessmentCostPresentationTests
{
    private static BaselineToken Token() => new("project", "sha256:" + new string('a', 64), "1",
        "2026-11-07T00:00:00Z", "sha256:" + new string('b', 64));

    private static AssessmentWordResult Word() => new("word", "analysed", false, "Search completed", 123, null)
    {
        ParserWork = 9876,
        Attempts = 88,
        Passes = 77,
        Morphology = new ParseWordEvidence("fieldworks-parse-analysis/v1", 0, "word", 123, false, false, false, [], [])
            { Attempts = 88 },
    };

    [Fact]
    public void AssessmentDetailAndFullJsonDoNotExposeRawCosts()
    {
        var response = new AssessCommandResponse(new BaselineCaptureResponse(Token(), "project", DateTimeOffset.UtcNow,
            false, true), new SelectionProjection(["word"], []), ["assessment"], "complete") { Words = [Word()] };
        var full = CommandTextRenderer.Render(CommandOutcome<AssessCommandResponse>.Success(response), true).Output;
        var detail = AssessSummary.Page(response, 0, 10, detailed: true).ToJsonString();
        foreach (var output in new[] { full, detail })
        {
            Assert.DoesNotContain("parserWork", output, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("elapsedMs", output, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("attempts", output, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("costComparison", output, StringComparison.Ordinal);
            // A lone word is the whole run's work, shown as a share and never as a count (#482).
            Assert.DoesNotContain("workUnits", output, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("9876", output, StringComparison.Ordinal);
            Assert.Contains(100d, new[] { (double)System.Text.Json.Nodes.JsonNode.Parse(output)!["words"]![0]!["workSharePercent"]! });
            Assert.DoesNotContain("baselineWorkSharePercent", output, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TrialJsonAndTextShowPercentWithoutRawCountersOrTime()
    {
        var trial = new TrialRecord("job", "proposal", "digest", Token(), null, new Dictionary<string, string>(),
            ["word"], false, null, JobStatus.Completed, null, TrialFreshness.Current)
        {
            WordResults = [Word()],
            Work = new TrialWork(1, 100, 105, 5, [new TrialWorkIncrease("word", 100, 105, 5)], 0, 0)
                { BaselineTimeMs = 123, TrialTimeMs = 456 },
        };
        var json = CommandTextRenderer.Render(CommandOutcome<TrialRecord>.Success(trial), true).Output;
        var text = CommandTextRenderer.Render(CommandOutcome<TrialRecord>.Success(trial), false).Output;
        Assert.DoesNotContain("\"baselineWork\"", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("elapsedMs", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("parserWork", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("5% more", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Warning", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(" ms", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TrialWordsCarryBothSidesPercentOfTotalWorkAndThePointsDeltaButNoCounts()
    {
        var trial = new TrialRecord("job", "proposal", "digest", Token(), null, new Dictionary<string, string>(),
            ["word", "other"], false, null, JobStatus.Completed, null, TrialFreshness.Current)
        {
            WordResults =
            [
                Word() with { ParserWork = 750, BaselineWorkUnits = 500 },
                Word() with { ParserWork = 250, BaselineWorkUnits = 500 },
            ],
        };
        var output = CommandTextRenderer.Render(CommandOutcome<TrialRecord>.Success(trial), true).Output;
        var words = System.Text.Json.Nodes.JsonNode.Parse(output)!["wordResults"]!.AsArray();
        Assert.Equal(75d, (double)words[0]!["workSharePercent"]!);
        Assert.Equal(50d, (double)words[0]!["baselineWorkSharePercent"]!);
        Assert.Equal(25d, (double)words[0]!["workSharePointsDelta"]!);
        Assert.Equal(25d, (double)words[1]!["workSharePercent"]!);
        Assert.Equal(-25d, (double)words[1]!["workSharePointsDelta"]!);
        Assert.DoesNotContain("workUnits", output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("750", output, StringComparison.Ordinal);
    }

    private static AssessCommandResponse Run(params long?[] work) =>
        new(new BaselineCaptureResponse(Token(), "project", DateTimeOffset.UtcNow, false, true),
            new SelectionProjection(["word"], []), ["assessment"], "complete")
        { Words = work.Select((count, index) => Word() with { ParserWork = count }).ToArray() };

    [Fact]
    public void ASecondPageUsesTheWholeRunsTotalNotThePages()
    {
        var page = AssessSummary.Page(Run(100, 100, 200, 600), 2, 2, detailed: false);
        var words = page["words"]!.AsArray();
        Assert.Equal(20d, (double)words[0]!["workSharePercent"]!);
        Assert.Equal(60d, (double)words[1]!["workSharePercent"]!);
    }

    [Fact]
    public void SharesAreOmittedForTheWholeRunWhenAnyWordHasNoCount()
    {
        var response = Run(100, null, 300);
        var detail = AssessSummary.Page(response, 0, 10, detailed: true).ToJsonString();
        var concise = AssessSummary.Page(response, 0, 10, detailed: false).ToJsonString();
        var full = AssessSummary.Full(response).ToJsonString();
        foreach (var output in new[] { detail, concise, full })
            Assert.DoesNotContain("workSharePercent", output, StringComparison.Ordinal);
    }

    [Fact]
    public void ANonComparableTrialRendersOnlyTheProposalSidePercent()
    {
        var trial = new TrialRecord("job", "proposal", "digest", Token(), null, new Dictionary<string, string>(),
            ["word", "other"], false, null, JobStatus.Completed, null, TrialFreshness.Current)
        {
            WordResults = [Word() with { ParserWork = 300 }, Word() with { ParserWork = 100 }],
        };
        var output = CommandTextRenderer.Render(CommandOutcome<TrialRecord>.Success(trial), true).Output;
        var words = System.Text.Json.Nodes.JsonNode.Parse(output)!["wordResults"]!.AsArray();
        Assert.Equal(75d, (double)words[0]!["workSharePercent"]!);
        Assert.DoesNotContain("baselineWorkSharePercent", output, StringComparison.Ordinal);
        Assert.DoesNotContain("workSharePointsDelta", output, StringComparison.Ordinal);
    }

    [Fact]
    public void ATrialWithAMissingBaselineCountOmitsTheBaselineSideAndDelta()
    {
        var trial = new TrialRecord("job", "proposal", "digest", Token(), null, new Dictionary<string, string>(),
            ["word", "other"], false, null, JobStatus.Completed, null, TrialFreshness.Current)
        {
            WordResults = [Word() with { ParserWork = 300, BaselineWorkUnits = 100 }, Word() with { ParserWork = 100 }],
        };
        var output = CommandTextRenderer.Render(CommandOutcome<TrialRecord>.Success(trial), true).Output;
        Assert.Contains("workSharePercent", output, StringComparison.Ordinal);
        Assert.DoesNotContain("baselineWorkSharePercent", output, StringComparison.Ordinal);
        Assert.DoesNotContain("workSharePointsDelta", output, StringComparison.Ordinal);
    }
}
