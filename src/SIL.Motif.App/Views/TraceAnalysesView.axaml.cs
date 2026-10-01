using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace SIL.Motif.App.Views;

/// <summary>
/// A trace's analyses, each once with the number of ways the search found it, under a heading that gives the
/// answer. Try a Word places it above the rules on the word's path; a standalone diagnostic places it first.
/// </summary>
public sealed partial class TraceAnalysesView : UserControl
{
    public TraceAnalysesView() => AvaloniaXamlLoader.Load(this);
}
