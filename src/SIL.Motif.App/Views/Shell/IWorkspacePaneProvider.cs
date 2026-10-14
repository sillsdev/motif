using Avalonia.Controls;

namespace SIL.Motif.App.Views.Shell;

/// <summary>
/// Optional page-owned views placed beside the working page. The shell attaches them only while that
/// page is current; each control must have a single visual parent and retains the page's data context.
/// </summary>
public interface IWorkspacePaneProvider
{
    /// <summary>The title of the page's optional side panel.</summary>
    string SidePanelTitle => string.Empty;

    /// <summary>Page controls shown between the navigation rail and working page, or null for no panel.</summary>
    Control? SidePanel => null;

    /// <summary>Selection details hosted in the Inspector, or null to use the shared object Inspector.</summary>
    Control? InspectorContent => null;

    /// <summary>Actions pinned below the Inspector, or below the page when it collapses; null means no pinned actions.</summary>
    Control? InspectorFooter => null;
}
