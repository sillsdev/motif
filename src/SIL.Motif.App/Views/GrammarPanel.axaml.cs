using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App.Views;

/// <summary>The Grammar stage: the parser's findings about the grammar as a whole, bound to <see cref="Grammar"/>.</summary>
public sealed partial class GrammarPanel : UserControl
{
    public GrammarPanel(GrammarViewModel grammar)
    {
        ArgumentNullException.ThrowIfNull(grammar);
        Grammar = grammar;
        DataContext = this;
        AvaloniaXamlLoader.Load(this);
    }

    public GrammarViewModel Grammar { get; }
}
