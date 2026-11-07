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
        Assert.DoesNotContain("baselineWork", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("elapsedMs", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("parserWork", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("5% more", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Warning", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(" ms", text, StringComparison.Ordinal);
    }
}
