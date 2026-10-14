using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
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

    /// <summary>Exposes a named sign as one image; unnamed signs and their children remain decorative.</summary>
    protected override AutomationPeer OnCreateAutomationPeer() => new MarkGlyphAutomationPeer(this);

    private sealed class MarkGlyphAutomationPeer(MarkGlyph owner) : ControlAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Image;

        protected override bool IsContentElementCore() => owner.IsVisible && owner.Mark is { Glyph.Length: > 0 } &&
            !string.IsNullOrWhiteSpace(AutomationProperties.GetName(owner));

        protected override bool IsControlElementCore() => IsContentElementCore();

        // The child draws the symbol itself; announcing it would speak the symbol instead of the outcome name.
        protected override IReadOnlyList<AutomationPeer>? GetChildrenCore() => [];
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
        AutomationProperties.SetAutomationId(glyph, AutomationIds.MarkGlyph);
        glyph.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center;
        glyph.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
        Child = glyph;
    }
}
