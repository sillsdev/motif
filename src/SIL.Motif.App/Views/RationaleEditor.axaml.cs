using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Interactivity;
using SIL.Motif.App.Compare;
using SIL.Motif.App.ViewModels;
using SIL.TextEditing.Avalonia;

namespace SIL.Motif.App.Views;

/// <summary>
/// Shows a <see cref="RationaleEditorModel"/>: its text in the shared rich field, read-only until Edit, with Save,
/// Cancel, the findings of the last save and the earlier versions to restore. A link opens from its context menu.
/// </summary>
public sealed partial class RationaleEditor : UserControl
{
    private readonly ContentControl _fieldHost;
    private RationaleEditorModel? _model;

    public RationaleEditor()
    {
        AvaloniaXamlLoader.Load(this);
        _fieldHost = this.FindControl<ContentControl>("FieldHost")!;
        DataContextChanged += (_, _) => Attach(DataContext as RationaleEditorModel);
    }

    /// <summary>The field showing the model's text, or null when there is no model.</summary>
    public RichTextField? Field => _fieldHost.Content as RichTextField;

    private void Attach(RationaleEditorModel? model)
    {
        if (_model is not null) _model.PropertyChanged -= OnModelChanged;
        _model = model;
        if (_model is not null) _model.PropertyChanged += OnModelChanged;
        IsVisible = _model is not null;
        Rebuild();
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RationaleEditorModel.Document)) Rebuild();
        else if (e.PropertyName is nameof(RationaleEditorModel.IsEditing) or nameof(RationaleEditorModel.IsSaving) or
                 nameof(RationaleEditorModel.CanEdit) && Field is { } field)
            field.IsReadOnly = !_model!.CanEdit || !_model.IsEditing || _model.IsSaving;
    }

    private async void CompareClicked(object? sender, RoutedEventArgs e)
    {
        if (_model is not { } model || TopLevel.GetTopLevel(this) is not Window owner) return;
        await model.CompareAsync(comparison => new CompareWindow(MotifAppStrings.ProposalRationale,
            comparison.Comparison, savedAt: comparison.Other.CreatedUtc, labels: comparison.Labels)
            .ShowDialog<CompareOutcome?>(owner));
    }

    // The field edits the model's document in place, so Save reads what the person typed.
    private void Rebuild()
    {
        if (_model is null)
        {
            _fieldHost.Content = null;
            return;
        }
        var model = _model;
        var field = new RichTextField(model.Document)
        {
            StyleResolver = model.Styles,
            IsReadOnly = !model.CanEdit || !model.IsEditing || model.IsSaving,
        };
        field.SetValue(Avalonia.Automation.AutomationProperties.NameProperty, model.IsComment ? MotifAppStrings.CommentsHeading : MotifAppStrings.ProposalRationale);
        field.ContextMenuRequested += (_, e) =>
        {
            var objData = e.Info.Run?.ObjData;
            if (RationaleEditorModel.LinkOf(objData) is null) return;
            var open = new MenuItem { Header = MotifAppStrings.RationaleOpenLink };
            open.Click += (_, _) => _ = model.OpenLinkAsync(objData);
            new ContextMenu { ItemsSource = new[] { open } }.Open(field);
        };
        _fieldHost.Content = field;
    }
}
