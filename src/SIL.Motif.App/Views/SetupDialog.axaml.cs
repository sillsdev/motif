using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace SIL.Motif.App.Views;

/// <summary>The modal project setup steps, placed over the shell from the main window.</summary>
public sealed partial class SetupDialog : UserControl
{
    public SetupDialog() => AvaloniaXamlLoader.Load(this);
}
