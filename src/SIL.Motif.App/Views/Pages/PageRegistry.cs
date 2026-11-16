using Avalonia.Controls;
using SIL.Motif.App.ViewModels;
using static SIL.Motif.App.Views.PageIcons;

namespace SIL.Motif.App.Views;

/// <summary>
/// One page of the window: which it is, its navigation title and icon, how its model is built from the
/// <see cref="WorkspaceContext"/>, and how its view is built from that model.
/// </summary>
/// <param name="Page">The page this entry registers.</param>
/// <param name="Title">The page title, also used in primary navigation when the entry is shown there.</param>
/// <param name="Icon">The sidebar icon as path data on a 24-unit grid, stroked in the text colour.</param>
/// <param name="CreateModel">Builds the page's model from the context alone.</param>
/// <param name="CreateView">Builds the page's view from the model <paramref name="CreateModel"/> built.</param>
public sealed record PageEntry(
    WorkspacePage Page, string Title, string Icon, Func<WorkspaceContext, PageModel> CreateModel,
    Func<PageModel, Control> CreateView)
{
    /// <summary>Whether this page appears in the primary navigation rail.</summary>
    public bool ShowInRail { get; init; } = true;

    /// <summary>Whether this page requires Diagnostics to be enabled.</summary>
    public bool IsDiagnostic { get; init; }

    /// <summary>An entry whose view is built from a model of the one type its model factory builds.</summary>
    public static PageEntry Of<TModel>(
        WorkspacePage page, string title, string icon, Func<WorkspaceContext, TModel> model, Func<TModel, Control> view)
        where TModel : PageModel =>
        new(page, title, icon, context => model(context), built => view((TModel)built));
}

/// <summary>
/// The one place pages are registered, including pages opened outside the primary rail. Each entry carries
/// its title, icon, model and view; primary navigation lists entries with <see cref="PageEntry.ShowInRail"/>
/// in the order of <see cref="Entries"/>.
/// </summary>
public static class PageRegistry
{
    /// <summary>Every registered page; primary navigation follows this order for entries shown in the rail.</summary>
    public static IReadOnlyList<PageEntry> Entries { get; } =
    [
        PageEntry.Of(WorkspacePage.Progress, MotifAppStrings.BoardProgress,
            Of(Rect(3.5, 3.5, 7, 9), Rect(13.5, 3.5, 7, 5), Rect(13.5, 11.5, 7, 9), Rect(3.5, 15.5, 7, 5)),
            context => new ProgressPageModel(context), model => new ProgressPage(model)),
        PageEntry.Of(WorkspacePage.Texts, MotifAppStrings.BoardTexts,
            "M3 5h6a3 3 0 0 1 3 3v12a2 2 0 0 0-2-2H3z M21 5h-6a3 3 0 0 0-3 3v12a2 2 0 0 1 2-2h7z",
            context => new TextsPageModel(context), model => new TextsPage(model)),
        PageEntry.Of(WorkspacePage.Words, MotifAppStrings.BoardWords,
            Of(Circle(10.5, 10.5, 6.5), "M15.5 15.5L21 21 M7.5 10.5h6M10.5 8v5"),
            context => new WordsPageModel(context), model => new WordsPage(model)),
        PageEntry.Of(WorkspacePage.GrammarGuide, MotifAppStrings.BoardGrammarGuide,
            "M4 4h12a3 3 0 0 1 3 3v13H7a3 3 0 0 1-3-3z M7 4v13M10 8h6M10 12h6",
            context => new GrammarGuidePageModel(context), model => new GrammarGuidePage(model)),
        PageEntry.Of(WorkspacePage.Proposals, MotifAppStrings.BoardProposals,
            Of(Rect(3.5, 9.5, 13, 11), Rect(7.5, 3.5, 13, 11)),
            context => ProposalCompareHost.Attach(new ProposalsPageModel(context)), model => new ProposalsPage(model)),
        PageEntry.Of(WorkspacePage.Diagnostics, MotifAppStrings.BoardDiagnostics,
            "M12 4l9 16H3z M12 10v4M12 17v.5",
            context => new DiagnosticsPageModel(context), model => new DiagnosticsPage(model)) with { IsDiagnostic = true },
        PageEntry.Of(WorkspacePage.Settings, MotifAppStrings.BoardSettings,
            "M19.73 9.93 L21.98 9.88 L21.98 14.12 L19.73 14.07 L18.93 16.0 L20.55 17.56 L17.56 20.55 L16.0 18.93 L14.07 19.73 L14.12 21.98 L9.88 21.98 L9.93 19.73 L8.0 18.93 L6.44 20.55 L3.45 17.56 L5.07 16.0 L4.27 14.07 L2.02 14.12 L2.02 9.88 L4.27 9.93 L5.07 8.0 L3.45 6.44 L6.44 3.45 L8.0 5.07 L9.93 4.27 L9.88 2.02 L14.12 2.02 L14.07 4.27 L16.0 5.07 L17.56 3.45 L20.55 6.44 L18.93 8.0 Z M12 8.5 A3.5 3.5 0 1 0 12 15.5 A3.5 3.5 0 1 0 12 8.5 Z",
            context => new SettingsPageModel(context), model => new SettingsPage(model)) with { ShowInRail = false },
    ];

    /// <summary>The entry for <paramref name="page"/>.</summary>
    public static PageEntry For(WorkspacePage page) => Entries.Single(entry => entry.Page == page);
}
