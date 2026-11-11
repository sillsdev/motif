using SIL.TextEditing;
using SIL.TextEditing.Markdown;

namespace SIL.Motif.App.Preview;

/// <summary>
/// FieldWorks Markdown for a field of the previews (SIL.TextEditing ADR 0035), written and read with Motif's options for
/// the shown text. The Markdown says what the project holds: the underline, colour and broken-link marker the preview
/// shows links with are left out of it, and given back to what it reads, so a link reads the same either way and an
/// untouched paragraph stays as it was.
/// </summary>
public sealed class DiscussionSourceForm : ISourceForm
{
    private readonly GdrDiscussion _discussion;
    private readonly FieldWorksMarkdownSourceForm _markdown;
    private readonly bool _richString;
    private Paragraph? _writtenInline;

    /// <summary>
    /// The form for a field showing <paramref name="discussion"/>, whose links name the project
    /// <paramref name="projectName"/>. A <paramref name="richString"/> field, such as a Definition, has no paragraph of
    /// its own in the project, so while it holds one paragraph its Markdown is that paragraph's text alone, and what is
    /// read keeps the shown paragraph's direction.
    /// </summary>
    public DiscussionSourceForm(GdrDiscussion discussion, string? projectName, bool richString = false)
    {
        _discussion = discussion ?? throw new ArgumentNullException(nameof(discussion));
        _markdown = new FieldWorksMarkdownSourceForm(discussion.MarkdownOptions(projectName));
        _richString = richString;
    }

    /// <inheritdoc/>
    public string Name => _markdown.Name;

    /// <inheritdoc/>
    public string? Write(IReadOnlyList<Paragraph> paragraphs, bool singleParagraph)
    {
        ArgumentNullException.ThrowIfNull(paragraphs);
        _writtenInline = _richString && paragraphs.Count == 1 ? paragraphs[0] : null;
        return _markdown.Write(paragraphs.Select(_discussion.AsProject).ToArray(), singleParagraph || _writtenInline != null);
    }

    /// <inheritdoc/>
    public SourceReading Read(string source, bool singleParagraph)
    {
        var inline = _writtenInline;
        var read = _markdown.Read(source, singleParagraph || inline != null);
        var paragraphs = read.Paragraphs.Select(_discussion.AsShown);
        if (inline != null) paragraphs = paragraphs.Select(paragraph => Shaped(inline, paragraph));
        return new SourceReading(paragraphs.ToArray(), read.Diagnostics, read.Tokens);
    }

    // Inline Markdown carries runs only, so the paragraph it came from gives the direction, style and key back.
    private static Paragraph Shaped(Paragraph shape, Paragraph read)
    {
        var shaped = shape.Clone();
        for (var i = shaped.Runs.Count - 1; i >= 0; i--) shaped.Runs.RemoveAt(i);
        foreach (var run in read.Runs) shaped.Runs.Add(run);
        return shaped;
    }

    /// <inheritdoc/>
    public bool AreEquivalent(Paragraph original, Paragraph read) =>
        _markdown.AreEquivalent(_discussion.AsProject(original), _discussion.AsProject(read));
}
