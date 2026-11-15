using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App.Views;

/// <summary>The user-chosen file Handoff action and its files.</summary>
public sealed partial class HandoffSection : UserControl
{
    public HandoffSection(HandoffSessionViewModel session)
    {
        ArgumentNullException.ThrowIfNull(session);
        DataContext = session;
        AvaloniaXamlLoader.Load(this);
        this.FindControl<ContentControl>("HandoffHost")!.Content = new HandoffPanel(session.Handoff);
    }
}
