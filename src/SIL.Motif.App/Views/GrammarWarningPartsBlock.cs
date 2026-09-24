using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.Views;

/// <summary>
/// Shows one side of a grammar finding as coloured, wrapping text: prose plain, echoed values in a fixed
/// width, each named object as a link that opens FieldWorks on it, and an identifier the project does not
/// contain in red, since that identifier is usually the finding itself.
/// </summary>
public sealed class GrammarWarningPartsBlock : WrapPanel
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
        Orientation = Avalonia.Layout.Orientation.Horizontal;
    }

    public IReadOnlyList<GrammarWarningPart>? Parts
    {
        get => GetValue(PartsProperty);
        set => SetValue(PartsProperty, value);
    }

    private void Rebuild()
    {
        Children.Clear();
        foreach (var part in Parts ?? [])
            Children.Add(ControlFor(part));
    }

    private static Control ControlFor(GrammarWarningPart part)
    {
        if (part.Role == GrammarWarningPartRole.Object && part.FieldWorksLink is { } link)
            return LinkFor(part, link);

        var text = part.Role == GrammarWarningPartRole.Missing ? $"missing object {part.Text}" : part.Text;
        if (part.LinkStatus == FieldWorksLinkStatus.Unavailable)
            text += $" (FieldWorks link unavailable: {UnavailableReason(part.LinkReason)})";
        var block = new CopyableTextBlock
        {
            Text = text,
            Margin = new Thickness(0, 0, 4, 0),
            TextWrapping = TextWrapping.Wrap,
        };
        switch (part.Role)
        {
            case GrammarWarningPartRole.Object:
                block.Foreground = ObjectBrush;
                block.FontWeight = FontWeight.SemiBold;
                break;
            case GrammarWarningPartRole.Missing:
                block.Foreground = MissingBrush;
                block.FontWeight = FontWeight.SemiBold;
                break;
            case GrammarWarningPartRole.Value:
                block.Foreground = ValueBrush;
                block.FontFamily = new FontFamily("Cascadia Mono, Consolas, monospace");
                break;
        }
        return block;
    }

    private static string UnavailableReason(FieldWorksLinkReason? reason) => reason switch
    {
        FieldWorksLinkReason.MissingProject => "no FieldWorks project name was supplied",
        FieldWorksLinkReason.GuidNotRecorded => "PanGloss did not record a GUID",
        FieldWorksLinkReason.InvalidGuid => "the recorded GUID is invalid",
        FieldWorksLinkReason.UnsupportedKind => "FieldWorks cannot open this kind",
        _ => "the reason was not provided",
    };

    private static HyperlinkButton LinkFor(GrammarWarningPart part, string link)
    {
        var button = new HyperlinkButton
        {
            Content = part.Text,
            NavigateUri = new Uri(link),
            Padding = new Thickness(0),
            Margin = new Thickness(0, 0, 4, 0),
            Foreground = ObjectBrush,
            FontWeight = FontWeight.SemiBold,
        };
        ToolTip.SetTip(button, $"Open this {part.FieldWorksKind?.ToLowerInvariant() ?? "object"} in FieldWorks");
        return button;
    }
}
