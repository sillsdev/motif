using System.Text.Json;
using System.Text.Json.Nodes;
using SIL.Motif.Cli.Rendering;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
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
            new OverviewTextCoverage(43, 20, 71, 1, 555, 300),
            new OverviewAccuracy(33, 115, 18, 63, 2, 9, 4, 14),
            new OverviewTiming(8, 400, [new SlowWordTiming("Akjulule", 1007)], 71),
            Warnings: null);

        var rendered = CommandTextRenderer.Render(CommandOutcome<OverviewResponse>.Success(response), asJson: false);

        Assert.Contains("Selection default: 1 text + 4 words = 135 words, 555 occurrences", rendered.Output);
        Assert.Contains("43/135 parse (32%)", rendered.Output);
        Assert.Contains("33/115 approved kept  18 violations  63 unknown", rendered.Output);
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
            Warnings: null);
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
}
