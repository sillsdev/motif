using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.Views;

/// <summary>
/// Shows one side of a grammar finding as coloured, wrapping text: prose plain, echoed values in a fixed
/// width, each named object as a link that opens FieldWorks on it, and an identifier the project does not
/// contain in red, since that identifier is usually the finding itself.
/// </summary>
public sealed class GrammarWarningPartsBlock : TextBlock
{
    public static readonly StyledProperty<IReadOnlyList<GrammarWarningPart>?> PartsProperty =
        AvaloniaProperty.Register<GrammarWarningPartsBlock, IReadOnlyList<GrammarWarningPart>?>(nameof(Parts));

    internal static readonly IBrush ObjectBrush = new SolidColorBrush(Color.Parse("#2F7FD8"));
    internal static readonly IBrush MissingBrush = new SolidColorBrush(Color.Parse("#D64545"));
    internal static readonly IBrush ValueBrush = new SolidColorBrush(Color.Parse("#2E9E6B"));

    static GrammarWarningPartsBlock()
    {
        PartsProperty.Changed.AddClassHandler<GrammarWarningPartsBlock>((block, _) => block.Rebuild());
    }

    public GrammarWarningPartsBlock()
    {
        TextWrapping = TextWrapping.Wrap;
    }

    protected override Type StyleKeyOverride => typeof(TextBlock);

    public IReadOnlyList<GrammarWarningPart>? Parts
    {
        get => GetValue(PartsProperty);
        set => SetValue(PartsProperty, value);
    }

    private void Rebuild()
    {
        var inlines = new InlineCollection();
        var first = true;
        foreach (var part in Parts ?? [])
        {
            if (!first) inlines.Add(new Run(" "));
            first = false;
            inlines.Add(InlineFor(part));
        }
        Inlines = inlines;
    }

    private static Inline InlineFor(GrammarWarningPart part) => part.Role switch
    {
        "object" when part.FieldWorksLink is { } link => new InlineUIContainer(LinkFor(part, link))
        {
            BaselineAlignment = BaselineAlignment.TextBottom,
        },
        "object" => new Run(part.Text) { Foreground = ObjectBrush, FontWeight = FontWeight.SemiBold },
        "missing" => new Run($"missing object {part.Text}")
        {
            Foreground = MissingBrush, FontWeight = FontWeight.SemiBold,
        },
        "value" => new Run(part.Text)
        {
            Foreground = ValueBrush, FontFamily = new FontFamily("Cascadia Mono, Consolas, monospace"),
        },
        _ => new Run(part.Text),
    };

    private static HyperlinkButton LinkFor(GrammarWarningPart part, string link)
    {
        var button = new HyperlinkButton
        {
            Content = part.Text,
            NavigateUri = new Uri(link),
            Padding = new Thickness(0),
            Foreground = ObjectBrush,
            FontWeight = FontWeight.SemiBold,
        };
        ToolTip.SetTip(button, $"Open this {part.Kind?.ToLowerInvariant() ?? "object"} in FieldWorks");
        return button;
    }
}
