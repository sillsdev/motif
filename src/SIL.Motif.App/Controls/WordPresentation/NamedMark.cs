using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using SIL.Motif.App.Controls;
using SIL.Motif.App.Views;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App.Controls.WordPresentation;

/// <summary>A selectable label paired with its mark glyph and accessible mark name.</summary>
public sealed class NamedMark : StackPanel
{
    public static readonly StyledProperty<Mark?> MarkProperty =
        AvaloniaProperty.Register<NamedMark, Mark?>(nameof(Mark));

    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<NamedMark, string?>(nameof(Text));

    static NamedMark()
    {
        MarkProperty.Changed.AddClassHandler<NamedMark>((control, _) => control.Rebuild());
        TextProperty.Changed.AddClassHandler<NamedMark>((control, _) => control.Rebuild());
    }

    public NamedMark()
    {
        Orientation = Avalonia.Layout.Orientation.Horizontal;
        Classes.Add("namedMark");
        Rebuild();
    }

    /// <summary>The semantic mark paired with the selectable words.</summary>
    public Mark? Mark
    {
        get => GetValue(MarkProperty);
        set => SetValue(MarkProperty, value);
    }

    /// <summary>The independently selectable words that describe this mark.</summary>
    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    private void Rebuild()
    {
        Children.Clear();
        if (Mark is { } mark && mark.Glyph.Length > 0)
            Children.Add(new MarkGlyph { Mark = mark, Classes = { "inline" } });
        if (Text is { Length: > 0 } text)
            Children.Add(new CopyableTextBlock { Text = text, Classes = { "namedMarkText", "muted" } });
        AutomationProperties.SetName(this,
            Mark is { } current && Text is { Length: > 0 } label ? $"{current.Word}: {label}" : Mark?.Word ?? Text ?? string.Empty);
    }
}
