using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace SIL.Motif.App.Views;

/// <summary>
/// The Proposal bar's view switch: a list of choices where exactly one is always chosen, so a Ctrl-click on the chosen
/// one, which a list would read as "clear the choice", changes nothing.
/// </summary>
public sealed class ProposalViewSwitch : ListBox
{
    /// <summary>Creates the switch.</summary>
    public ProposalViewSwitch() => AddHandler(PointerPressedEvent, KeepChoice, RoutingStrategies.Tunnel);

    /// <inheritdoc />
    protected override Type StyleKeyOverride => typeof(ListBox);

    private static void KeepChoice(object? sender, PointerPressedEventArgs e)
    {
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.Source is not Visual source) return;
        var item = source as ListBoxItem ?? source.FindAncestorOfType<ListBoxItem>();
        if (item is { IsSelected: true }) e.Handled = true;
    }
}
