namespace SIL.Motif.App.ViewModels;

/// <summary>
/// Which pages draw the view chosen on the Proposal bar. Every other page shows today's numbers whatever the bar
/// says, and the bar's tooltip says so. A page joins by being added to <see cref="Pages"/> and reading
/// <see cref="ViewOf"/>.
/// </summary>
public static class ProposalViewScope
{
    /// <summary>The pages that draw the bar's view.</summary>
    public static IReadOnlyList<WorkspacePage> Pages { get; } = [WorkspacePage.Overview, WorkspacePage.Texts, WorkspacePage.Warnings, WorkspacePage.Timing];

    /// <summary>The view a page should draw: the bar's choice while a Proposal is open, else today's.</summary>
    public static MarkupView ViewOf(WorkspaceContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.ProposalView;
    }
}
