using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App.Views;

/// <summary>One Proposal's optional diagnostics, also usable independently on either side of Compare.</summary>
public sealed partial class ProposalDiagnosticsRow : UserControl
{
    private ProposalDiagnosticsViewModel? _model;

    public ProposalDiagnosticsRow()
    {
        AvaloniaXamlLoader.Load(this);
        DataContextChanged += (_, _) => BindModel();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        BindModel();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_model is not null) _model.PropertyChanged -= OnModelChanged;
        base.OnDetachedFromVisualTree(e);
    }

    private void BindModel()
    {
        if (ReferenceEquals(_model, DataContext))
        {
            if (_model is not null)
            {
                _model.PropertyChanged -= OnModelChanged;
                _model.PropertyChanged += OnModelChanged;
            }
            ShowContent();
            return;
        }
        if (_model is not null) _model.PropertyChanged -= OnModelChanged;
        _model = DataContext as ProposalDiagnosticsViewModel;
        if (_model is not null) _model.PropertyChanged += OnModelChanged;
        this.FindControl<ContentControl>("DiagnosticSurface")!.Content = null;
        ShowContent();
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(ProposalDiagnosticsViewModel.IsVisible)) ShowContent();
    }

    private void ShowContent()
    {
        var surface = this.FindControl<ContentControl>("DiagnosticSurface")!;
        IsVisible = _model?.IsVisible == true;
        if (!IsVisible) surface.Content = null;
        else if (surface.Content is null)
            surface.Content = ((IDataTemplate)Resources["DiagnosticContent"]!).Build(_model);
    }
}
