namespace SIL.Motif.Tests.App;

internal static partial class LayoutInvariantWorkList
{
    /// <summary>
    /// The quick tier's known layout findings. Each entry is a follow-up owned by the named area; an entry that stops recurring fails the run, and
    /// a finding not listed here fails it too.
    /// </summary>
    internal static IReadOnlyList<LayoutException> QuickEntries { get; } =
    [
        new(new("Timing/populated", "640x600", 150, "light"), "unreachable-control",
            new("WordRow", null, "motif-word-row-timing-rule-words-hawajafika-row", "hawajafika · Approved · PanGloss: No parse · Lost", "", "TimingPageCostliestRuleWordRowsItems"),
            null, "Frame", "hawajafika · Approved · PanGloss: No parse · Lost has no hit target or scroll route at 640x600 150%.",
            "no-visible-hit-target-or-scroll-route", 1),
    ];
}
