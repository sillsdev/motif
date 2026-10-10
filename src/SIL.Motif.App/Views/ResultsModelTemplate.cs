using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Metadata;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Controls.WordPresentation;

namespace SIL.Motif.App.Views;

/// <summary>Recycles Word row, strip and line templates while keeping its item as a compact source position.</summary>
public sealed class ResultsModelTemplate : IRecyclingDataTemplate
{
    private readonly ConditionalWeakTable<Control, Source> _sources = new();

    [Content]
    public IDataTemplate? Inner { get; set; }

    public bool Match(object? data) => data is not null;

    public Control? Build(object? data) => Build(data, null);

    public Control? Build(object? data, Control? existing)
    {
        if (existing is not null && _sources.TryGetValue(existing, out var reused))
        {
            reused.Value = data;
            existing.DataContext = Resolve(data);
            return existing;
        }
        var model = Resolve(data);
        var control = Inner?.Build(model);
        if (control is null) return null;
        var source = new Source(data);
        _sources.Add(control, source);
        control.DataContext = model;
        control.AttachedToVisualTree += (_, _) => control.DataContext = Resolve(source.Value);
        control.DetachedFromVisualTree += (_, _) => ClearDisplayedModel(control);
        return control;
    }

    internal static bool RepresentsSameModel(object previous, object next) =>
        Resolve(previous) is { } model && ReferenceEquals(model, Resolve(next));

    internal void RestoreModel(Control? control, object position)
    {
        if (control is null || !_sources.TryGetValue(control, out var source)) return;
        source.Value = position;
        control.DataContext = Resolve(position);
    }

    internal void ReleaseModel(Control? control)
    {
        if (control is null || !_sources.TryGetValue(control, out var source)) return;
        source.Value = null;
        ClearDisplayedModel(control);
    }

    private static void ClearDisplayedModel(Control control)
    {
        control.DataContext = null;
        if (control is WordStripToken strip) strip.SetCurrentValue(WordStripToken.DataProperty, null);
        else if (control is WordRow row) row.SetCurrentValue(WordRow.DataProperty, null);
        else if (control is WordCard card) card.Document = null;
    }

    private static object? Resolve(object? source) => source switch
    {
        SelectionWordRowPosition position => position.Model,
        SelectionLinePosition position => position.Model,
        SelectionTokenPosition position => position.Model,
        ResultsLineViewModel or ResultsTokenViewModel => source,
        WeakReference<object> reference => reference.TryGetTarget(out var model) ? model : null,
        _ => null,
    };

    private sealed class Source
    {
        private object? _position;
        private WeakReference<object>? _model;

        public Source(object? value) => Value = value;

        public object? Value
        {
            get => _model is { } model ? model.TryGetTarget(out var found) ? found : null : _position;
            set
            {
                _model = value is ResultsLineViewModel or ResultsTokenViewModel ? new(value) : null;
                _position = _model is null ? value : null;
            }
        }
    }
}
