using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace SIL.Motif.App.Views;

/// <summary>A shared prompt for the words that need parsing after Refresh.</summary>
public sealed partial class ParsePrompt : UserControl
{
    public ParsePrompt() => AvaloniaXamlLoader.Load(this);
}
