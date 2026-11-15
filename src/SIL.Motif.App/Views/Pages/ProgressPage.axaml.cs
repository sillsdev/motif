using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App.Views;

/// <summary>The open project's Progress page.</summary>
public sealed partial class ProgressPage : UserControl
{
    /// <summary>Creates the page over the workspace's shared page model.</summary>
    public ProgressPage(WorkspaceContext context) : this(
        (context ?? throw new ArgumentNullException(nameof(context))).GetOrCreatePage(static owner => new ProgressPageModel(owner))) { }

    public ProgressPage(ProgressPageModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        DataContext = model;
        AvaloniaXamlLoader.Load(this);
    }
}
