namespace SIL.Motif.Tests.App;

internal static partial class LayoutInvariantWorkList
{
    /// <summary>
    /// The quick tier's known layout findings. Each entry is a follow-up owned by the named area; an entry that stops recurring fails the run, and
    /// a finding not listed here fails it too.
    /// </summary>
    internal static IReadOnlyList<LayoutException> QuickEntries { get; } =
    [
        new(new("Texts/populated", "640x600", 150, "light"), "unreachable-control",
            new("WordStripToken", "WordStrip", "motif-word-11111111000000000000000000000002-0-6b6974616275-strip", "Unread · kitabu · Approved · PanGloss: Built something else", "wordStrip.hoverReveal", "ResultsInTextPanelTokensItems"),
            null, "Analyze texts", "Unread · kitabu · Approved · PanGloss: Built something else has no hit target or scroll route at 640x600 150%.",
            "no-visible-hit-target-or-scroll-route", 1),
        new(new("TryAWord/populated", "640x600", 150, "light"), "unreachable-control",
            new("ListBoxItem", null, null, null, "", "border"),
            null, "Try a Word", "ListBoxItem has no hit target or scroll route at 640x600 150%.",
            "no-visible-hit-target-or-scroll-route", 1),
        new(new("TryAWord/populated", "640x600", 150, "light"), "unreachable-control",
            new("StackPanel", null, null, "Inspect ha-", "morphemePanelPart.morphemePanelPartLast.hoverReveal.morphemePanelInspectable", "TryWordPanelPiecesItems"),
            null, "Try a Word", "Inspect ha- has no hit target or scroll route at 640x600 150%.",
            "no-visible-hit-target-or-scroll-route", 1),
        new(new("TryAWord/populated", "640x600", 150, "light"), "unreachable-control",
            new("StackPanel", null, null, "Inspect wa-", "morphemePanelPart.morphemePanelPartLast.hoverReveal.morphemePanelInspectable", "TryWordPanelPiecesItems"),
            null, "Try a Word", "Inspect wa- has no hit target or scroll route at 640x600 150%.",
            "no-visible-hit-target-or-scroll-route", 1),
        new(new("TryAWord/populated", "640x600", 150, "light"), "unreachable-control",
            new("StackPanel", null, null, "Inspect ja-", "morphemePanelPart.morphemePanelPartLast.hoverReveal.morphemePanelInspectable", "TryWordPanelPiecesItems"),
            null, "Try a Word", "Inspect ja- has no hit target or scroll route at 640x600 150%.",
            "no-visible-hit-target-or-scroll-route", 1),
        new(new("TryAWord/populated", "640x600", 150, "light"), "unreachable-control",
            new("StackPanel", null, null, "Inspect fik", "morphemePanelPart.morphemePanelPartLast.hoverReveal.morphemePanelInspectable", "TryWordPanelPiecesItems"),
            null, "Try a Word", "Inspect fik has no hit target or scroll route at 640x600 150%.",
            "no-visible-hit-target-or-scroll-route", 1),
        new(new("TryAWord/populated", "640x600", 150, "light"), "unreachable-control",
            new("StackPanel", null, null, "Inspect -a", "morphemePanelPart.morphemePanelPartLast.hoverReveal.morphemePanelInspectable", "TryWordPanelPiecesItems"),
            null, "Try a Word", "Inspect -a has no hit target or scroll route at 640x600 150%.",
            "no-visible-hit-target-or-scroll-route", 1),
        new(new("Timing/populated", "640x600", 150, "light"), "unreachable-control",
            new("Button", null, null, null, "", "TimingSidePanel"),
            null, "Frame", "Button has no hit target or scroll route at 640x600 150%.",
            "no-visible-hit-target-or-scroll-route", 1),
        new(new("Timing/populated", "640x600", 150, "light"), "unreachable-control",
            new("WordRow", null, "motif-word-row-timing-rule-words-hawajafika-row", "hawajafika · Approved · PanGloss: No parse · Lost", "", "TimingPageCostliestRuleWordRowsItems"),
            null, "Frame", "hawajafika · Approved · PanGloss: No parse · Lost has no hit target or scroll route at 640x600 150%.",
            "no-visible-hit-target-or-scroll-route", 1),
        new(new("AiHandoff/populated", "640x600", 150, "light"), "unreachable-control",
            new("Button", null, null, "Copy path for grammar.json", "", "HandoffPanelFilesItems"),
            null, "AI pages", "Copy path for grammar.json has no hit target or scroll route at 640x600 150%.",
            "no-visible-hit-target-or-scroll-route", 1),
        new(new("AiHandoff/populated", "640x600", 150, "light"), "unreachable-control",
            new("ToggleButton", "ExpanderHeader", null, null, ":unchecked", "ExpanderHeaderLayoutContainer"),
            null, "AI pages", "ExpanderHeader has no hit target or scroll route at 640x600 150%.",
            "no-visible-hit-target-or-scroll-route", 1),
        new(new("Settings/populated", "640x600", 150, "light"), "unreachable-control",
            new("RepeatButton", "PART_PageUpButton", null, "Page up", ":empty", "SettingsListScroll"),
            null, "Frame / Settings", "Page up has no hit target or scroll route at 640x600 150%.",
            "no-visible-hit-target-or-scroll-route", 1),
    ];
}
