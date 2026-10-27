using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace SIL.Motif.App.Views;

/// <summary>
/// The resting face of a word's opinion or add button: a letter and a drawn caret in a bordered box. It is one element
/// where the real button is thirteen, and it measures the same, so the word does not move when the button replaces it.
/// It takes the same properties as a <see cref="Border"/> and styles as one, so the disposition face rules apply to it.
/// </summary>
public sealed class DispositionFace : Control
{
    /// <summary>The letter or count shown before the caret.</summary>
    public static readonly StyledProperty<string?> LetterProperty =
        AvaloniaProperty.Register<DispositionFace, string?>(nameof(Letter));

    /// <summary>The fill behind the letter.</summary>
    public static readonly StyledProperty<IBrush?> BackgroundProperty = Border.BackgroundProperty.AddOwner<DispositionFace>();

    /// <summary>The outline colour.</summary>
    public static readonly StyledProperty<IBrush?> BorderBrushProperty = Border.BorderBrushProperty.AddOwner<DispositionFace>();

    /// <summary>The outline thickness.</summary>
    public static readonly StyledProperty<Thickness> BorderThicknessProperty =
        Border.BorderThicknessProperty.AddOwner<DispositionFace>();

    /// <summary>The corner radius.</summary>
    public static readonly StyledProperty<CornerRadius> CornerRadiusProperty = Border.CornerRadiusProperty.AddOwner<DispositionFace>();

    /// <summary>The space between the outline and the letter.</summary>
    public static readonly StyledProperty<Thickness> PaddingProperty = Border.PaddingProperty.AddOwner<DispositionFace>();

    private const double CaretSize = 8;
    private const double CaretGap = 2;
    private FormattedText? _text;

    static DispositionFace()
    {
        AffectsMeasure<DispositionFace>(LetterProperty, BorderThicknessProperty, PaddingProperty);
        AffectsRender<DispositionFace>(LetterProperty, BackgroundProperty, BorderBrushProperty, BorderThicknessProperty,
            CornerRadiusProperty);
        LetterProperty.Changed.AddClassHandler<DispositionFace>((face, _) => face._text = null);
        TextElement.ForegroundProperty.Changed.AddClassHandler<DispositionFace>((face, _) => face.InvalidateVisual());
        TextElement.FontSizeProperty.Changed.AddClassHandler<DispositionFace>((face, _) =>
        {
            face._text = null;
            face.InvalidateMeasure();
        });
        TextElement.FontWeightProperty.Changed.AddClassHandler<DispositionFace>((face, _) =>
        {
            face._text = null;
            face.InvalidateMeasure();
        });
    }

    /// <summary>The letter or count shown before the caret.</summary>
    public string? Letter
    {
        get => GetValue(LetterProperty);
        set => SetValue(LetterProperty, value);
    }

    public IBrush? Background { get => GetValue(BackgroundProperty); set => SetValue(BackgroundProperty, value); }

    public IBrush? BorderBrush { get => GetValue(BorderBrushProperty); set => SetValue(BorderBrushProperty, value); }

    public Thickness BorderThickness { get => GetValue(BorderThicknessProperty); set => SetValue(BorderThicknessProperty, value); }

    public CornerRadius CornerRadius { get => GetValue(CornerRadiusProperty); set => SetValue(CornerRadiusProperty, value); }

    public Thickness Padding { get => GetValue(PaddingProperty); set => SetValue(PaddingProperty, value); }

    protected override Type StyleKeyOverride => typeof(Border);

    private FormattedText Text => _text ??= new FormattedText(Letter ?? string.Empty, CultureInfo.CurrentUICulture,
        FlowDirection.LeftToRight,
        new Typeface(TextElement.GetFontFamily(this), TextElement.GetFontStyle(this), TextElement.GetFontWeight(this)),
        TextElement.GetFontSize(this), TextElement.GetForeground(this));

    protected override Size MeasureOverride(Size availableSize)
    {
        var inset = Padding + BorderThickness;
        var width = Text.WidthIncludingTrailingWhitespace + CaretGap + CaretSize + inset.Left + inset.Right;
        var height = Math.Max(Text.Height, CaretSize) + inset.Top + inset.Bottom;
        return new Size(width, height);
    }

    public override void Render(DrawingContext context)
    {
        var thickness = BorderThickness.Left;
        var outline = BorderBrush is { } stroke && thickness > 0 ? new Pen(stroke, thickness) : null;
        var edge = new Rect(Bounds.Size).Deflate(thickness / 2);
        var radius = CornerRadius.TopLeft;
        context.DrawRectangle(Background, outline, edge, radius, radius);
        var inset = Padding + BorderThickness;
        var ink = TextElement.GetForeground(this);
        var content = new Rect(inset.Left, inset.Top, Math.Max(0, Bounds.Width - inset.Left - inset.Right),
            Math.Max(0, Bounds.Height - inset.Top - inset.Bottom));
        var text = Text;
        if (ink is not null) text.SetForegroundBrush(ink);
        context.DrawText(text, new Point(content.X, content.Y + (content.Height - text.Height) / 2));
        var x = content.X + text.WidthIncludingTrailingWhitespace + CaretGap;
        var y = content.Y + (content.Height - CaretSize) / 2;
        var caret = new StreamGeometry();
        using (var geometry = caret.Open())
        {
            geometry.BeginFigure(new Point(x, y + CaretSize * 0.1), true);
            geometry.LineTo(new Point(x + CaretSize * 0.5, y + CaretSize * 0.6));
            geometry.LineTo(new Point(x + CaretSize, y + CaretSize * 0.1));
            geometry.EndFigure(true);
        }
        context.DrawGeometry(ink, null, caret);
    }
}
