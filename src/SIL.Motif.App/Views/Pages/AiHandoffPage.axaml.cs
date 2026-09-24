using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App.Views;

/// <summary>The AI Handoff page: the action that writes the files, above what they hold and how to use them.</summary>
public sealed partial class AiHandoffPage : UserControl
{
    public AiHandoffPage(AiHandoffPageModel page)
    {
        ArgumentNullException.ThrowIfNull(page);
        DataContext = page;
        AvaloniaXamlLoader.Load(this);
        this.FindControl<ContentControl>("HandoffHost")!.Content = new HandoffPanel(page.Handoff);
    }
}
