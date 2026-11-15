using SIL.Motif.Contract.Responses;
using SIL.Motif.Runner.Operations;

namespace SIL.Motif.App.Compare;

/// <summary>A Rationale comparison in its written Markdown, preserving even text that cannot be read as rich text.</summary>
public sealed class RationaleComparison
{
    public RationaleComparison(string starting, string mine, RationaleRevisionView other)
    {
        Other = other;
        Comparison = Compare(Version(starting), Version(mine), Version(other.Markdown));
        var author = other.AuthorLabel ?? (other.PersonName.Length > 0 ? other.PersonName : MotifAppStrings.RationaleRevisionAPerson);
        Labels = new CompareLabels(author, author)
        {
            Explanation = MotifAppStrings.RationaleCompareExplanation,
            Cancel = MotifAppStrings.Cancel,
            StartAgain = $"Start again from {author}'s text",
        };
    }

    public RationaleRevisionView Other { get; }
    public TextComparison Comparison { get; }
    public CompareLabels Labels { get; }

    /// <summary>The chosen Markdown, including paragraph breaks typed in the side-by-side view.</summary>
    public static string Text(IReadOnlyList<ParagraphVersion> paragraphs) =>
        string.Join('\n', paragraphs.Select(paragraph => paragraph.Text));

    internal static TextComparison Compare(ParagraphVersion starting, ParagraphVersion mine, ParagraphVersion other) =>
        new(TextSort.Sort(new TextVersions(
            [new StartingParagraph("rationale", starting.Text, "", starting)], [mine], [other]),
            MotifAppStrings.ProposalRationale, preserveSpelling: true));

    private static ParagraphVersion Version(string text) => new("rationale", "", TextProps.None, [new TextRun(text, "en")]);
}
