using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App.Views;

/// <summary>Draws a mark's declared vector icon or its font glyph.</summary>
public sealed class MarkGlyph : Decorator
{
    /// <summary>The mark whose sign this control draws.</summary>
    public static readonly StyledProperty<Mark?> MarkProperty =
        AvaloniaProperty.Register<MarkGlyph, Mark?>(nameof(Mark));

    static MarkGlyph() => MarkProperty.Changed.AddClassHandler<MarkGlyph>((control, _) => control.Rebuild());

    public MarkGlyph()
    {
        Classes.Add("markGlyphHost");
        IsHitTestVisible = false;
        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
        Rebuild();
    }

    /// <summary>The mark whose sign this control draws, or <see langword="null"/> for no sign.</summary>
    public Mark? Mark
    {
        get => GetValue(MarkProperty);
        set => SetValue(MarkProperty, value);
    }

    private void Rebuild()
    {
        Child = null;
        MarkClasses.SetMark(this, Mark);
        if (Mark is not { Glyph.Length: > 0 } mark)
        {
            IsVisible = false;
            return;
        }

        IsVisible = true;
        var classes = new[] { mark.KindClass + "Mark", mark.Value };
        Control glyph;
        if (MarkGlyphs.IconDataFor(mark) is { } data)
        {
            var icon = new PathIcon { Data = StreamGeometry.Parse(data), Classes = { "markGlyphIcon" } };
            icon.Bind(PathIcon.ForegroundProperty, this.GetObservable(TextElement.ForegroundProperty));
            glyph = icon;
        }
        else glyph = new TextBlock { Text = mark.Glyph, Classes = { "markGlyph" } };
        glyph.Classes.AddRange(classes);
        glyph.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center;
        glyph.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
        Child = glyph;
    }
}
