using Avalonia.Controls;
using SIL.Motif.App.ViewModels;
using static SIL.Motif.App.Views.PageIcons;

namespace SIL.Motif.App.Views;

/// <summary>One page of the window: which it is, its sidebar label and icon, and how its view is built.</summary>
/// <param name="Page">The page this entry registers.</param>
/// <param name="Title">The sidebar label.</param>
/// <param name="Icon">The sidebar icon as path data on a 24-unit grid, stroked in the text colour.</param>
/// <param name="Create">Builds the page's view from the workspace.</param>
public sealed record PageEntry(
    WorkspacePage Page, string Title, string Icon, Func<HandoffWorkspaceViewModel, Control> Create);

/// <summary>
/// The one place pages are registered. Each entry carries everything a page needs to appear: its place in the
/// sidebar (the order of <see cref="Entries"/>), its label, its icon, and its view. Filling a page changes only
/// that page's own view and view model; adding one is one entry here, beside its <see cref="WorkspacePage"/>
/// name.
/// </summary>
public static class PageRegistry
{
    /// <summary>Every page, in sidebar order.</summary>
    public static IReadOnlyList<PageEntry> Entries { get; } =
    [
        new(WorkspacePage.Overview, "Overview",
            Of(Rect(3.5, 3.5, 7, 9), Rect(13.5, 3.5, 7, 5), Rect(13.5, 11.5, 7, 9), Rect(3.5, 15.5, 7, 5)),
            workspace => new OverviewPage(workspace)),
        new(WorkspacePage.Texts, "Texts",
            "M3 5h6a3 3 0 0 1 3 3v12a2 2 0 0 0-2-2H3z M21 5h-6a3 3 0 0 0-3 3v12a2 2 0 0 1 2-2h7z",
            workspace => new TextsPage(workspace)),
        new(WorkspacePage.TryAWord, "Try a Word",
            Of(Circle(10.5, 10.5, 6.5), "M15.5 15.5L21 21 M7.5 10.5h6M10.5 8v5"),
            workspace => new TryWordPanel(workspace.Assess.Trace)),
        new(WorkspacePage.Timing, "Timing",
            Of(Circle(12, 13, 7), "M12 13V9M10 3h4M12 3v3M18 6l1.5-1.5"),
            workspace => new TimingPage(workspace)),
        new(WorkspacePage.Warnings, "Warnings",
            "M12 4l9 16H3z M12 10v4M12 17v.5",
            workspace => new WarningsPage(workspace)),
        new(WorkspacePage.Review, "Review changes",
            "M7 4v16M17 4v16 M4 8l3-3 3 3M14 16l3 3 3-3",
            workspace => new ReviewPanel(workspace)),
        new(WorkspacePage.AiHandoff, "AI Handoff",
            "M12 3l1.8 4.8L18 9.5l-4.2 1.7L12 16l-1.8-4.8L6 9.5l4.2-1.7z M18 15l.8 2 2 .8-2 .8-.8 2-.8-2-2-.8 2-.8z",
            workspace => new AiHandoffPage(workspace)),
    ];

    /// <summary>The entry for <paramref name="page"/>.</summary>
    public static PageEntry For(WorkspacePage page) => Entries.Single(entry => entry.Page == page);
}
