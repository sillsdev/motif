using Avalonia;

namespace SIL.Motif.Tests.App;

internal sealed record LayoutFindingContext(string Scene, string Size, int Zoom, string Theme);

/// <summary>
/// What a layout finding says about one control. <paramref name="NamedAncestor"/> is the nearest visual ancestor
/// that has a Name or AutomationId and is not a template part, which names a control that cannot carry an id itself, such as the
/// AccessText a Button creates for its label.
/// </summary>
internal sealed record ControlDescription(
    string Type, string? Name, string? AutomationId, string? AutomationName, string Classes,
    string? NamedAncestor = null);

internal sealed record LayoutBounds(double X, double Y, double Width, double Height)
{
    internal static LayoutBounds From(Rect rect) => new(rect.X, rect.Y, rect.Width, rect.Height);
}

internal sealed record LayoutFinding(
    string Scene,
    string Size,
    int Zoom,
    string Theme,
    string Check,
    ControlDescription Control,
    LayoutBounds Bounds,
    string Identity,
    string Detail,
    ControlDescription? RelatedControl = null,
    int Count = 1)
{
    // A lone control keeps its accessible name apart; a repeated pair, such as row texts, collapses across rows.
    internal LayoutFindingKey Key => new(Scene, Size, Zoom, Theme, Check,
        StableControlKey(Control, withName: RelatedControl is null), Identity,
        RelatedControl is null ? null : StableControlKey(RelatedControl, withName: false));

    private static string StableControlKey(ControlDescription control, bool withName) =>
        $"{control.Type}#{control.Name}#{control.AutomationId}.{control.Classes}" +
        (withName ? $"#{control.AutomationName}" : string.Empty);
}

internal sealed record LayoutFindingKey(
    string Scene,
    string Size,
    int Zoom,
    string Theme,
    string Check,
    string Control,
    string Identity,
    string? RelatedControl);

internal static class LayoutCheckNames
{
    internal const string RequiredActionMissing = "required-action-missing";
    internal const string MissingAutomationName = "missing-automation-name";
    internal const string DuplicateAutomationId = "duplicate-automation-id";
    internal const string UnreachableControl = "unreachable-control";
    internal const string OverlappingText = "overlapping-text";
    internal const string MissingGlyph = "missing-glyph";
    internal const string TitleBarOverflow = "title-bar-overflow";
    internal const string TitleBarOverlap = "title-bar-overlap";
    internal const string TextBoxOverflow = "text-own-box-overflow";
    internal const string PopupOutsideWindow = "popup-outside-window";
    internal const string NavItemHidden = "nav-item-hidden";
    internal const string PageHorizontalScroll = "page-horizontal-scroll";
    internal const string MidWordWrap = "mid-word-wrap";
    internal const string EdgeCrowding = "edge-crowding";

    /// <summary>The checks that <c>InspectReachabilityAndTitleBar</c> can report.</summary>
    internal static IReadOnlySet<string> ReachabilityAndTitleBar { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        UnreachableControl, NavItemHidden, PageHorizontalScroll, TitleBarOverflow, TitleBarOverlap,
        PopupOutsideWindow, MissingAutomationName, DuplicateAutomationId,
    };
}
