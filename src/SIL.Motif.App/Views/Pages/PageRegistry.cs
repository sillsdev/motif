using Avalonia.Controls;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App.Views;

/// <summary>
/// The one place a page's view is chosen: each <see cref="WorkspacePage"/> maps to the control that shows it,
/// built from the workspace. The window adds every page's control once and shows the current one, so filling a
/// page means changing that page's own view, and adding one means one line here beside its
/// <see cref="WorkspacePage"/> value and sidebar label.
/// </summary>
public static class PageRegistry
{
    /// <summary>Builds the control that shows <paramref name="page"/> for <paramref name="workspace"/>.</summary>
    public static Control Create(WorkspacePage page, HandoffWorkspaceViewModel workspace) => page switch
    {
        WorkspacePage.Overview => new OverviewPage(workspace),
        WorkspacePage.Texts => new TextsPage(workspace),
        WorkspacePage.TryAWord => new TryWordPanel(workspace.Assess.Trace),
        WorkspacePage.Timing => new TimingPage(workspace),
        WorkspacePage.Warnings => new GrammarPanel(workspace.Grammar),
        WorkspacePage.Review => new ReviewPanel(workspace),
        WorkspacePage.AiHandoff => new AiHandoffPage(workspace),
        _ => throw new ArgumentOutOfRangeException(nameof(page), page, null),
    };
}
