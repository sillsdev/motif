using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.PanGloss;

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
        var parts = Parts ?? [];
        ProgressivePanel.Populate(this, parts, (part, index) =>
        {
            var control = ControlFor(part);
            // Named objects that follow one another are a list, so each but the last carries its comma.
            return part.Role == GrammarWarningPartRole.Object && index + 1 < parts.Count &&
                parts[index + 1].Role == GrammarWarningPartRole.Object ? WithSeparator(control) : control;
        });
    }

    private static Control WithSeparator(Control control)
    {
        var row = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal };
        row.Children.Add(control);
        row.Children.Add(new TextBlock { Text = MotifAppStrings.WarningsListSeparator, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom });
        return row;
    }

    private static Control ControlFor(GrammarWarningPart part)
    {
        if (part.Status == GrammarSubjectStatus.Object && part.Role == GrammarWarningPartRole.Object &&
            Uri.TryCreate(part.FieldWorksLink, UriKind.Absolute, out var link) &&
            string.Equals(link.Scheme, "silfw", StringComparison.OrdinalIgnoreCase))
            return LinkFor(part, link);

        var text = part.Status == GrammarSubjectStatus.ProjectSettings ? $"Project setting: {part.Text}"
            : part.Role == GrammarWarningPartRole.Missing ? $"Missing: {part.Text}" : part.Text;
        if (part.Field is { Length: > 0 } field) text += $" > {field}";
        var block = new CopyableTextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
        };
        block.Classes.Add("warningPart");
        switch (part.Role)
        {
            case GrammarWarningPartRole.Object:
                block.Classes.Add("warningObject");
                break;
            case GrammarWarningPartRole.Missing:
                block.Classes.Add("warningMissing");
                break;
            case GrammarWarningPartRole.Value:
                block.Classes.Add("warningValue");
                break;
        }
        return block;
    }

    private static HyperlinkButton LinkFor(GrammarWarningPart part, Uri link)
    {
        // A TextBlock, not a string, so the "_" an environment is written with is not read as an access key.
        var button = new HyperlinkButton
        {
            Content = new TextBlock { Text = part.Text },
            NavigateUri = link,
        };
        button.Classes.Add("warningObjectLink");
        var tool = FieldWorksLinks.ToolName(part.FieldWorksTool ?? FieldWorksLinks.ToolOf(link.AbsoluteUri));
        ToolTip.SetTip(button, $"Open this item in {tool}");
        ClearTipPlacement.SetIsEnabled(button, true);
        return button;
    }
}
