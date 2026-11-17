using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App.Views;

/// <summary>
/// Lists each finding of a kind once, as its sentence with the FieldWorks objects it names as links inside the
/// sentence. The sentence is one wrapping block, so punctuation between links is ordinary text and stays on
/// whichever line it falls.
/// </summary>
public sealed class FindingSentenceList : StackPanel
{
    public static readonly StyledProperty<IReadOnlyList<IReadOnlyList<FindingSegment>>?> SentencesProperty =
        AvaloniaProperty.Register<FindingSentenceList, IReadOnlyList<IReadOnlyList<FindingSegment>>?>(nameof(Sentences));

    static FindingSentenceList()
    {
        SentencesProperty.Changed.AddClassHandler<FindingSentenceList>((list, _) => list.Rebuild());
    }

    public IReadOnlyList<IReadOnlyList<FindingSegment>>? Sentences
    {
        get => GetValue(SentencesProperty);
        set => SetValue(SentencesProperty, value);
    }

    private void Rebuild() =>
        ProgressivePanel.Populate(this, Sentences ?? [], (segments, _) => BlockFor(segments));

    private static Control BlockFor(IReadOnlyList<FindingSegment> segments)
    {
        var block = new CopyableTextBlock { TextWrapping = TextWrapping.Wrap };
        if (segments.All(segment => segment.Link is null))
        {
            block.Text = string.Concat(segments.Select(segment => segment.Text));
            return block;
        }
        foreach (var segment in segments)
        {
            if (segment.Link is { FieldWorksLink: { } address } part && Uri.TryCreate(address, UriKind.Absolute, out var link))
                block.Inlines!.Add(new InlineUIContainer(GrammarWarningPartsBlock.LinkFor(part, link))
                    { BaselineAlignment = BaselineAlignment.Center });
            else block.Inlines!.Add(new Run(segment.Text));
        }
        return block;
    }
}
