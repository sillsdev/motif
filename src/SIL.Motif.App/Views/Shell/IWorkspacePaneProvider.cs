using Avalonia.Controls;

namespace SIL.Motif.App.Views.Shell;

/// <summary>
/// Optional page-owned views placed beside the working page. A host attaches them while the page is
/// shown; each control must have one visual parent and retains the page's data context.
/// Construct panes without a parent; getters must return those controls without changing layout or ownership.
/// </summary>
public interface IWorkspacePaneProvider
{
    /// <summary>The title of the page's optional side panel.</summary>
    string SidePanelTitle => string.Empty;

    /// <summary>Page controls shown between the navigation rail and working page, or null for no panel.</summary>
    Control? SidePanel => null;

    /// <summary>Optional page details shown unless an object inspection is open; null means no page details.</summary>
    Control? InspectorContent => null;

    /// <summary>Actions pinned below the Inspector, or below the page when it collapses; null means no pinned actions.</summary>
    Control? InspectorFooter => null;

    /// <summary>
    /// Actions that stay in view below <see cref="InspectorFooter"/> and are never scrolled away; the footer's
    /// own height is cut to what they leave. Null means no pinned actions.
    /// </summary>
    Control? InspectorActions => null;
}
