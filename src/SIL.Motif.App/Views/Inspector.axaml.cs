using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App.Views;

/// <summary>
/// The inspector panel beside the page: one object's breadcrumb, name and sections, from its
/// <see cref="ViewModels.InspectorViewModel"/>. The window places it, and steps it back on Esc.
/// </summary>
public sealed partial class Inspector : UserControl
{
    private InspectorViewModel? _viewModel;

    public Inspector()
    {
        AvaloniaXamlLoader.Load(this);
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_viewModel is not null) _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel = DataContext as InspectorViewModel;
        if (_viewModel is not null) _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        ScheduleRelatedFactIntoView();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is null or "" or nameof(InspectorViewModel.Facts))
            ScheduleRelatedFactIntoView();
    }

    private void ScheduleRelatedFactIntoView() =>
        Dispatcher.UIThread.Post(BringRelatedFactIntoView, DispatcherPriority.Loaded);

    private void BringRelatedFactIntoView()
    {
        if (_viewModel is not { IsOpen: true } viewModel) return;
        var index = -1;
        for (var i = 0; i < viewModel.Facts.Count; i++)
        {
            if (viewModel.Facts[i].InspectSubject is null) continue;
            index = i;
            break;
        }
        if (index < 0) return;

        var facts = this.FindControl<ItemsControl>("InspectorFactsItems");
        if (facts is null) return;
        facts.ScrollIntoView(index);
        facts.UpdateLayout();
        this.GetVisualDescendants().OfType<InspectLink>()
            .FirstOrDefault(link => ReferenceEquals(link.DataContext, viewModel.Facts[index]))?
            .BringIntoView();
    }
}
