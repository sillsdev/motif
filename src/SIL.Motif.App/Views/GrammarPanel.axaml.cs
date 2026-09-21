using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App.Views;

/// <summary>The Grammar stage: the parser's findings about the grammar as a whole, bound to <see cref="Assess"/>.</summary>
public sealed partial class GrammarPanel : UserControl
{
    public GrammarPanel(AssessViewModel assess)
    {
        ArgumentNullException.ThrowIfNull(assess);
        Assess = assess;
        DataContext = this;
        AvaloniaXamlLoader.Load(this);
    }

    public AssessViewModel Assess { get; }
}
