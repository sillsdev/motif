using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Markup.Xaml;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views.Shell;

namespace SIL.Motif.App.Views;

/// <summary>The current Selection's text reader and source pane.</summary>
public sealed partial class TextsPage : UserControl, IWorkspacePaneProvider
{
    private readonly Control _sourcePane;

    /// <summary>Creates the page over the workspace's shared page model.</summary>
    public TextsPage(WorkspaceContext context) : this(
        (context ?? throw new ArgumentNullException(nameof(context))).GetOrCreatePage(static owner => new TextsPageModel(owner))) { }

    public TextsPage(TextsPageModel page) : this(page, null) { }

    internal TextsPage(TextsPageModel page, SettingsViewModel? settings)
    {
        ArgumentNullException.ThrowIfNull(page);
        DataContext = page;
        AvaloniaXamlLoader.Load(this);
        _sourcePane = new SelectionPanel(page.Selection, page.Words, settings);
        _sourcePane.Bind(IsEnabledProperty, new Binding(nameof(WorkspaceContext.ProjectAndSelectionEnabled))
        {
            Source = page.Context,
        });
        this.FindControl<ContentControl>("ReaderHost")!.Content = new ResultsInTextPanel(page.ResultsInText);
    }

    /// <summary>The source pane's title.</summary>
    public string SidePanelTitle => MotifAppStrings.Texts;

    /// <summary>The Selection control hosted beside the reader.</summary>
    public Control SidePanel => _sourcePane;
}
