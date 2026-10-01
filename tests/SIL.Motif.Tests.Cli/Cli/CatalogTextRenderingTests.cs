using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using SIL.Motif.Cli.Rendering;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Cli;

public sealed class CatalogTextRenderingTests
{
    [Fact]
    public void OverviewTextShowsSelectionSourcesCoverageAccuracyAndTiming()
    {
        var response = new OverviewResponse(
            "Aweti", DateTimeOffset.Parse("2026-09-24T12:00:00Z"), DateTimeOffset.Parse("2026-09-24T11:00:00Z"),
            135, 1, 4, 555, 300, 12, 100, "assessment/1", DateTimeOffset.Parse("2026-09-24T11:30:00Z"),
            120, "sha256:grammar", "sha256:selection",
            new OverviewTextCoverage(43, 20, 71, 1, 555, 300)
            {
                OccurrenceCoveragePercent = 12.4,
            },
            new OverviewAccuracy(33, 115, 18, 63, 2, 9, 4, 14) { RejectedWordsInMatchCell = 1 },
            new OverviewTiming(8, 400, [new SlowWordTiming("Akjulule", 1007)], 71),
            Warnings: null)
        {
            SelectionResolved = true,
            WordCoveragePercent = 71.6,
        };

        var rendered = CommandTextRenderer.Render(CommandOutcome<OverviewResponse>.Success(response), asJson: false);

        Assert.Contains("Selection default: 1 text + 4 words = 135 words, 555 occurrences", rendered.Output);
        Assert.Contains("43/135 parse (72%)", rendered.Output);
        Assert.Contains("300/555 occurrences covered (12%)", rendered.Output);
        Assert.DoesNotContain("43/135 parse (32%)", rendered.Output);
        Assert.Contains("33/115 approved kept  18 violations  63 unknown", rendered.Output);
        Assert.Contains("rejected analyses rebuilt 2  rejected words matched 1/9", rendered.Output);
        Assert.Contains("median 8 ms  p95 400 ms  slowest Akjulule 1,007 ms", rendered.Output);
    }

    [Fact]
    public void OverviewHeaderShowsProjectFreshnessAndCallsIncompleteWordsLimits()
    {
        var baseline = new DateTimeOffset(2026, 9, 24, 11, 2, 0, TimeSpan.Zero);
        var saved = new DateTimeOffset(2026, 9, 24, 10, 58, 0, TimeSpan.Zero);
        var response = new OverviewResponse(
            "Aweti", baseline, saved, 135, 1, 4, 555, 300, 12, 100, "assessment/1", baseline,
            120, "sha256:grammar", "sha256:selection",
            new OverviewTextCoverage(43, 20, 71, 1, 555, 300),
            new OverviewAccuracy(33, 115, 18, 63, 2, 9, 4, 14),
            new OverviewTiming(8, 400, [new SlowWordTiming("Akjulule", 1007)], 71),
            Warnings: null)
        {
            SelectionResolved = true,
        };
        var payload = JsonSerializer.SerializeToNode(response)!.AsObject();
        payload["ProjectFileName"] = "Aweti.fwdata";
        payload["BaselineCapturedUtc"] = baseline;
        payload["BaselineSourceLastWriteUtc"] = saved;
        payload["IsStale"] = false;
        var populated = payload.Deserialize<OverviewResponse>()!;

        var rendered = CommandTextRenderer.Render(
            CommandOutcome<OverviewResponse>.Success(populated), asJson: false);

        Assert.Contains($"Aweti  Aweti.fwdata  Current (Baseline {baseline.ToLocalTime():HH:mm}, " +
            $"FieldWorks saved {saved.ToLocalTime():HH:mm})", rendered.Output);
        Assert.Contains("71 limit", rendered.Output);
        Assert.DoesNotContain("opened", rendered.Output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Warnings   not stored", rendered.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void OverviewTextShowsUnresolvedSelectionAndDoesNotInventNullPercentages()
    {
        var response = new OverviewResponse(
            "Aweti", DateTimeOffset.Parse("2026-09-24T12:00:00Z"), DateTimeOffset.Parse("2026-09-24T11:00:00Z"),
            0, 0, 0, 0, 300, 12, 100, null, null,
            null, "sha256:grammar", null,
            new OverviewTextCoverage(0, 0, 0, 0, 0, 0),
            new OverviewAccuracy(0, 0, 0, 0, 0, 0, 0, 0),
            new OverviewTiming(null, null, [], 0),
            Warnings: null)
        {
            SelectionResolved = false,
            WordCoveragePercent = null,
        };

        var rendered = CommandTextRenderer.Render(CommandOutcome<OverviewResponse>.Success(response), asJson: false);

        Assert.Contains("Selection default: not resolved against this Baseline", rendered.Output);
        Assert.DoesNotContain("0 text + 0 words", rendered.Output);
        Assert.Contains("0/0 parse (not recorded)", rendered.Output);
        Assert.Contains("0/0 occurrences covered (not recorded)", rendered.Output);
        Assert.DoesNotContain("0%", rendered.Output);
    }

    [Fact]
    public void TimingTextWarnsWhenTheProjectIsStale()
    {
        var response = new TimingResponse("assessment/1", "all", "rule", 1, 8, 400, [], [], [])
        {
            IsStale = true,
        };

        var rendered = CommandTextRenderer.Render(CommandOutcome<TimingResponse>.Success(response), asJson: false);

        Assert.Contains("FieldWorks has changed since the current Baseline", rendered.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void OverviewTextSplitsTotalWordTimeByKindWithNotAttributedBeside()
    {
        using var culture = new CultureScope(CultureInfo.GetCultureInfo("en-US"));
        var response = new OverviewResponse(
            "Aweti", DateTimeOffset.Parse("2026-09-24T12:00:00Z"), null, 2, 1, 0, 2, 2, 0, 0, "assessment/1",
            DateTimeOffset.Parse("2026-09-24T11:30:00Z"), 0.8, null, null,
            new OverviewTextCoverage(2, 0, 0, 0, 2, 2), new OverviewAccuracy(0, 0, 0, 0, 0, 0, 0, 0),
            new OverviewTiming(400, 500, [], 0)
            {
                MeasuredWordCount = 2,
                Kinds = [new TimingAggregateRow("morph_rule", "morph_rule", 600, 0.75, 2) { Kind = "morph_rule" }],
                Attribution = new WordTimeAttribution(2, 800, 600, 200, 0.25, 0, false),
            },
            null);

        var output = CommandTextRenderer.Render(CommandOutcome<OverviewResponse>.Success(response), asJson: false).Output;

        Assert.Contains("2 words, 0.8 s total word time: morph_rule 75.0%, not attributed 25.0%", output,
            StringComparison.Ordinal);
    }

    [Fact]
    public void TimingTextSharesEveryRowOfTotalWordTimeAndCountsCallsPerKind()
    {
        using var culture = new CultureScope(CultureInfo.GetCultureInfo("en-US"));
        var response = new TimingResponse("assessment/1", "all", "rule", 2, 8, 400, [],
            [
                new TimingAggregateRow("guid-1", "Plural", 300, 0.375, 2) { Kind = "morph_rule", Calls = 40 },
                new TimingAggregateRow("guid-2", "Plural", 200, 0.25, 1) { Kind = "lex_entry" },
            ], [])
        {
            Attribution = new WordTimeAttribution(2, 800, 500, 302, 0.3775, 2, true),
        };

        var output = CommandTextRenderer.Render(CommandOutcome<TimingResponse>.Success(response), asJson: false).Output;

        Assert.Contains("Total word time: 800.00 ms for 2 measured word(s)", output, StringComparison.Ordinal);
        Assert.Contains("Plural [guid-1]: 300.00 ms (37.5%), 40 morph_rule calls, 2 words", output, StringComparison.Ordinal);
        Assert.Contains("Plural [guid-2]: 200.00 ms (25.0%), calls not counted, 1 words", output, StringComparison.Ordinal);
        Assert.Contains("Not attributed: 302.00 ms (37.8%)", output, StringComparison.Ordinal);
        Assert.Contains("recorded 2.00 ms more than their words' own time", output, StringComparison.Ordinal);
        Assert.DoesNotContain("attempts", output, StringComparison.Ordinal);
    }

    [Fact]
    public void GrammarCheckTextAndJsonIdentifyErrorsSeparatelyFromWarnings()
    {
        var response = new GrammarCheckResponse(
            [new GrammarWarning(GrammarDiagnosticLevel.Error, "Feature system", [],
                [new GrammarWarningPart("A feature system could not be loaded.", GrammarWarningPartRole.Text)],
                "error: hc-invalid-feature-system: A feature system could not be loaded.")],
            HasBaseline: true);

        var text = CommandTextRenderer.Render(CommandOutcome<GrammarCheckResponse>.Success(response), asJson: false);
        var json = CommandTextRenderer.Render(CommandOutcome<GrammarCheckResponse>.Success(response), asJson: true);

        Assert.Contains("(1 error, 0 warnings, 0 information)", text.Output);
        Assert.Contains("error: hc-invalid-feature-system", text.Output);
        Assert.Contains("\"severity\": \"error\"", json.Output);
    }

    [Fact]
    public void WarningsTextAndJsonIdentifyErrorCountsSeparately()
    {
        var finding = new GrammarWarning(GrammarDiagnosticLevel.Error, "Feature system", [], [],
            "error: hc-invalid-feature-system: A feature system could not be loaded.");
        var response = new WarningsResponse(true, true, [finding], [], 0, 0) { ErrorCount = 1 };

        var text = CommandTextRenderer.Render(CommandOutcome<WarningsResponse>.Success(response), asJson: false);
        var json = CommandTextRenderer.Render(CommandOutcome<WarningsResponse>.Success(response), asJson: true);

        Assert.Contains("Grammar findings: 1 (1 error, 0 warnings, 0 information)", text.Output);
        Assert.Contains("\"errorCount\": 1", json.Output);
        Assert.Contains("\"severity\": \"error\"", json.Output);
    }
}
