using Xunit;

namespace SIL.Motif.Tests.App;

internal sealed record ReportedAppGap(string Test, string Reason);

internal static class ReportedAppGaps
{
    private static IReadOnlyList<ReportedAppGap> Gaps { get; } =
    [
        new(nameof(KeyboardInteractionContractTests.PointerAndKeyboardSelectionRespectTheSameCellGuards),
            "ComparePanel keyboard activation selects an empty impossible cell."),
        new(nameof(KeyboardInteractionContractTests.WordCardArrowsRespectOccurrenceBoundariesAndChildInputs),
            "A focused reading picker lets the word card's Right arrow move to another occurrence."),
    ];

    public static void AssertStillReproduces(string test, bool reproduced)
    {
        var gap = Assert.Single(Gaps, item => item.Test == test);
        Assert.True(reproduced,
            $"{gap.Test}: the reported gap no longer reproduces, so remove it and restore a direct assertion. {gap.Reason}");
    }
}
