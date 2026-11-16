using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App.Views.Guide;

/// <summary>
/// "In the Grammar": the names of the grammar objects a Grammar Guide section's text links to, as the project resolved the
/// links, each with Open in FieldWorks. A link whose target was deleted is listed as missing. It shows names and links
/// only: the grammar is read and edited in FieldWorks.
/// </summary>
public sealed partial class GuideGrammarNames : UserControl
{
    private readonly GrammarGuidePageModel _model;

    public GuideGrammarNames(GrammarGuidePageModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        _model = model;
        DataContext = model;
        AvaloniaXamlLoader.Load(this);
        AddHandler(Button.ClickEvent, OnOpenClick);
    }

    private void OnOpenClick(object? sender, RoutedEventArgs e)
    {
        var button = (e.Source as Visual)?.GetSelfAndVisualAncestors().OfType<Button>().FirstOrDefault(candidate => candidate.Classes.Contains("guideOpen"));
        if (button?.Tag is not Uri uri) return;
        if (_model.OpenInFieldWorksCommand.CanExecute(uri)) _model.OpenInFieldWorksCommand.Execute(uri);
        e.Handled = true;
    }
}
