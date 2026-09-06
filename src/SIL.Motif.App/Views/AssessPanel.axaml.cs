using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App.Views;

/// <summary>The run strip, bound to its own <see cref="Assess"/> view model.</summary>
public sealed partial class AssessPanel : UserControl
{
    public AssessPanel(AssessViewModel assess)
    {
        ArgumentNullException.ThrowIfNull(assess);
        Assess = assess;
        DataContext = this;
        AvaloniaXamlLoader.Load(this);
    }

    public AssessViewModel Assess { get; }
}
