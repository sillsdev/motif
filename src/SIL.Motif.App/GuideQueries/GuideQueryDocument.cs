using SIL.Motif.Contract.Gdr;

namespace SIL.Motif.App.GuideQueries;

/// <summary>
/// A Guide section's Discussion read as the text its <c>motif-query</c> and <c>motif-show</c> blocks are written in: one
/// paragraph per line, joined by line feeds. The project's Markdown writer escapes backticks, so the blocks are read from
/// the paragraphs' own text, and the parser is the Contract's.
/// </summary>
public sealed class GuideQueryDocument
{
    private readonly (int Start, int End)[] _ranges;

    private GuideQueryDocument(string text, (int Start, int End)[] ranges)
    {
        Text = text;
        _ranges = ranges;
        Blocks = GuideQueryBlocks.Parse(text);
    }

    /// <summary>The paragraphs' texts joined with a line feed between them.</summary>
    public string Text { get; }

    /// <summary>The blocks found in <see cref="Text"/>.</summary>
    public GuideBlocks Blocks { get; }

    /// <summary>Whether the text holds any query or show block.</summary>
    public bool HasBlocks => Blocks.Queries.Count > 0 || Blocks.Shows.Count > 0;

    /// <summary>Reads <paramref name="paragraphs"/>, each the text of one paragraph.</summary>
    public static GuideQueryDocument From(IReadOnlyList<string> paragraphs)
    {
        ArgumentNullException.ThrowIfNull(paragraphs);
        var ranges = new (int Start, int End)[paragraphs.Count];
        var text = new System.Text.StringBuilder();
        for (var i = 0; i < paragraphs.Count; i++)
        {
            if (i > 0) text.Append('\n');
            ranges[i].Start = text.Length;
            text.Append(paragraphs[i]);
            ranges[i].End = text.Length;
        }
        return new GuideQueryDocument(text.ToString(), ranges);
    }

    /// <summary>
    /// The paragraphs from <paramref name="first"/> to <paramref name="last"/> that <paramref name="span"/> covers exactly;
    /// false when the block starts or ends inside a paragraph that also holds other text.
    /// </summary>
    public bool TryParagraphsOf(GuideBlockSpan span, out int first, out int last)
    {
        ArgumentNullException.ThrowIfNull(span);
        first = Array.FindIndex(_ranges, range => range.Start == span.Start);
        last = Array.FindIndex(_ranges, range => range.End == span.End);
        return first >= 0 && last >= first;
    }

    /// <summary>The block's text with its body replaced by <paramref name="body"/>, fences kept as written.</summary>
    public string WithBody(GuideBlockSpan span, string body)
    {
        ArgumentNullException.ThrowIfNull(span);
        ArgumentNullException.ThrowIfNull(body);
        var opening = Text[span.Start..LineEnd(span.Start, span.End)];
        var closingStart = Text.LastIndexOf('\n', Math.Max(span.End - 1, span.Start)) + 1;
        var closing = closingStart > span.Start ? Text[closingStart..span.End] : "";
        return closing.Length == 0 ? opening + "\n" + body : opening + "\n" + body + "\n" + closing;
    }

    /// <summary>The body text of <paramref name="span"/>: what lies between its fences.</summary>
    public string BodyOf(GuideBlockSpan span)
    {
        ArgumentNullException.ThrowIfNull(span);
        return span.BodyEnd > span.BodyStart ? Text[span.BodyStart..span.BodyEnd] : "";
    }

    /// <summary>The text with <paramref name="span"/>'s body replaced; every other character is as it was.</summary>
    public string Replaced(GuideBlockSpan span, string body) => GuideQueryBlocks.Replace(Text, span, WithBody(span, body));

    private int LineEnd(int start, int limit)
    {
        var end = Text.IndexOf('\n', start, limit - start);
        return end < 0 ? limit : end;
    }
}
