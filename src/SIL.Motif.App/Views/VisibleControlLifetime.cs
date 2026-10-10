using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace SIL.Motif.App.Views;

// Hidden hosts stay attached; pinned by `HiddenMatrixHostReleasesRowsAndRestoresCheckedWords`.
internal sealed class VisibleControlLifetime
{
    private readonly Control _control;
    private readonly Action _show;
    private readonly Action _hide;
    private readonly List<Control> _ancestors = [];
    private bool _attached;
    private bool _active;
    private bool _initialized;

    public VisibleControlLifetime(Control control, Action show, Action hide)
    {
        _control = control;
        _show = show;
        _hide = hide;
        control.AttachedToVisualTree += (_, _) => Attach();
        control.DetachedFromVisualTree += (_, _) => Detach();
        control.PropertyChanged += OnPropertyChanged;
    }

    private void Attach()
    {
        _attached = true;
        foreach (var ancestor in _control.GetVisualAncestors().OfType<Control>())
        {
            _ancestors.Add(ancestor);
            ancestor.PropertyChanged += OnPropertyChanged;
        }
        Update();
    }

    private void Detach()
    {
        _attached = false;
        Update();
        foreach (var ancestor in _ancestors) ancestor.PropertyChanged -= OnPropertyChanged;
        _ancestors.Clear();
    }

    private void OnPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs change)
    {
        if (change.Property == Visual.IsVisibleProperty) Update();
    }

    private void Update()
    {
        var active = _attached && _control.IsEffectivelyVisible;
        if (_initialized && active == _active) return;
        _initialized = true;
        _active = active;
        if (active) _show();
        else _hide();
    }
}
