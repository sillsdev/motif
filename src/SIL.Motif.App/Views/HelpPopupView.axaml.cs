using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace SIL.Motif.App.Views;

/// <summary>The title, description, Markdown, and online link for the selected help entry.</summary>
public sealed partial class HelpPopupView : UserControl
{
    public HelpPopupView() => AvaloniaXamlLoader.Load(this);
}
