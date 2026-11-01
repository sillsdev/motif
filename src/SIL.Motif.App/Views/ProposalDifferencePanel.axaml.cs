using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia;
using Avalonia.Threading;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App.Views;

/// <summary>
/// The saved Difference of the selected Proposal, shown in the Proposal inspector. Bind a
/// <see cref="ViewModels.ProposalDifferenceViewModel"/> as its data context; while no Difference is saved it hides.
/// </summary>
public sealed partial class ProposalDifferencePanel : UserControl
{
    private ProposalDifferenceMaker? _maker;

    public ProposalDifferencePanel()
    {
        AvaloniaXamlLoader.Load(this);
        DataContextChanged += (_, _) => Watch((DataContext as ProposalDifferenceViewModel)?.Maker);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Watch((DataContext as ProposalDifferenceViewModel)?.Maker);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        Watch(null);
        base.OnDetachedFromVisualTree(e);
    }

    private void Watch(ProposalDifferenceMaker? maker)
    {
        if (_maker is not null) _maker.PropertyChanged -= OnMakerChanged;
        _maker = maker;
        if (_maker is not null) _maker.PropertyChanged += OnMakerChanged;
    }

    private void OnMakerChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ProposalDifferenceMaker.IsActive) || _maker is null) return;
        var starting = _maker.IsActive;
        // Making disables Make and ending hides Cancel, so focus would be lost; it follows the button that takes over.
        Dispatcher.UIThread.Post(() =>
        {
            var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
            var from = this.FindControl<Button>("DifferenceMake");
            var to = this.FindControl<Button>("DifferenceCancel");
            if (starting && (focused is null || ReferenceEquals(focused, from))) to?.Focus();
            else if (!starting && (focused is null || ReferenceEquals(focused, to))) from?.Focus();
        });
    }
}
