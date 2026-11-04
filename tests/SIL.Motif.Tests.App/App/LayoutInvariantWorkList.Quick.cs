namespace SIL.Motif.Tests.App;

internal static partial class LayoutInvariantWorkList
{
    /// <summary>
    /// The quick tier's known layout findings. Each entry is a follow-up owned by the named area; an entry that stops recurring fails the run, and
    /// a finding not listed here fails it too.
    /// </summary>
    internal static IReadOnlyList<LayoutException> QuickEntries { get; } =
    [
        new(new("Texts/lists-ticked", "640x600", 150, "light"), "unreachable-control",
            new("WordRow", null, "motif-word-row-lists-hawajafika-row", "hawajafika · Approved · PanGloss: No parse · Lost", "", "border"),
            null, "Texts lists", "hawajafika · Approved · PanGloss: No parse · Lost has no hit target or scroll route at 640x600 150%.",
            "no-visible-hit-target-or-scroll-route", 1),
    ];

    /// <summary>The quick tier's findings when the words, texts and Review change are Arabic script.</summary>
    internal static IReadOnlyList<LayoutException> QuickRightToLeftEntries { get; } =
    [
    ];

    /// <summary>The quick tier's findings with one long Proposal name and one long Parsimony limitation.</summary>
    internal static IReadOnlyList<LayoutException> QuickLongEntries { get; } =
    [
    ];
}
