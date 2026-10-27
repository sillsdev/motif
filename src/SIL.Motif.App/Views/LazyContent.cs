using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.VisualTree;

namespace SIL.Motif.App.Views;

/// <summary>
/// Holds content that is built the first time layout reaches it, which a hidden ancestor prevents, so a panel the
/// user may never open, or a list behind a collapsed expander, costs nothing until it is shown. A template builds
/// with this control's data context; a factory builds in code.
/// </summary>
public sealed class LazyContent : ContentControl
{
    public static readonly StyledProperty<IDataTemplate?> BuildProperty =
        AvaloniaProperty.Register<LazyContent, IDataTemplate?>(nameof(Build));

    protected override Type StyleKeyOverride => typeof(ContentControl);

    /// <summary>Builds in code what this control holds; preferred over <see cref="Build"/> when both are set.</summary>
    public Func<Control>? Factory { get; set; }

    /// <summary>The XAML template to build from, used when no factory was given.</summary>
    public IDataTemplate? Build
    {
        get => GetValue(BuildProperty);
        set => SetValue(BuildProperty, value);
    }

    /// <summary>Whether the content has been built.</summary>
    public bool IsRealized => Content is not null;

    /// <summary>The built content, or null while it has not been needed.</summary>
    public Control? Realized => Content as Control;

    /// <summary>Builds the content now, for a caller that must reach it before it is shown.</summary>
    public Control? Realize()
    {
        if (Content is null)
            Content = Factory?.Invoke() ?? Build?.Build(DataContext);
        return Realized;
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        // A control shown by its own flag is built at once, so the state is complete before the next layout pass.
        if (change.Property == IsVisibleProperty && change.GetNewValue<bool>() && this.IsAttachedToVisualTree()) Realize();
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        // Layout skips a control with a hidden ancestor, so reaching here means the content is about to be seen.
        Realize();
        return base.MeasureOverride(availableSize);
    }
}
