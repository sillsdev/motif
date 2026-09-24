using System.ComponentModel;
using System.Globalization;

namespace SIL.Motif.App.ViewModels;

/// <summary>
/// The Review changes page's model: the pending changes every page adds to, before any is written to FieldWorks,
/// counted beside the page's sidebar label.
/// </summary>
public sealed class ReviewPageModel : PageModel
{
    public ReviewPageModel(WorkspaceContext context) : base(context)
    {
        Changes.PropertyChanged += OnChangesChanged;
        context.PropertyChanged += OnContextPropertyChanged;
    }

    public ChangesViewModel Changes => Context.Changes;

    /// <summary>The open project's file name, which the apply card names.</summary>
    public string ProjectName => Context.ProjectName;

    private void OnChangesChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ChangesViewModel.Count))
            Badge = Changes.Count > 0 ? Changes.Count.ToString(CultureInfo.CurrentCulture) : string.Empty;
    }

    private void OnContextPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WorkspaceContext.ProjectName)) OnPropertyChanged(nameof(ProjectName));
    }
}
