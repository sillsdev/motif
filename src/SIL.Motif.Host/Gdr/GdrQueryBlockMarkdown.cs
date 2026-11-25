using SIL.Motif.Contract.Gdr;
using SIL.TextEditing;

namespace SIL.Motif.Host.Gdr;

/// <summary>
/// Carries <c>motif-query</c> and <c>motif-show</c> blocks across the FieldWorks Markdown reader and writer, which take no code
/// block. A Guide stores a block as plain paragraphs, one per line with the fence lines as text; the Markdown view shows it as a
/// real fence, and reading puts the lines back as paragraphs. The parser is the Contract's, the one the window uses.
/// </summary>
public static class GdrQueryBlockMarkdown
{
    private const string TokenPrefix = "MOTIFBLOCKPLACEHOLDER";

    /// <summary>One block lifted out of text: the stand-in paragraph's text and the block's lines.</summary>
    public sealed record Lifted(string Token, IReadOnlyList<string> Lines);

    private static string Token(int index) => TokenPrefix + index.ToString(System.Globalization.CultureInfo.InvariantCulture) + "X";

    /// <summary>
    /// Replaces each block in <paramref name="paragraphs"/> that sits whole in plain paragraphs with one stand-in paragraph.
    /// The block's lines come back through <see cref="Restore"/> after the writer ran.
    /// </summary>
    public static (IReadOnlyList<Paragraph> Paragraphs, IReadOnlyList<Lifted> Blocks) LiftParagraphs(IReadOnlyList<Paragraph> paragraphs)
    {
        var texts = paragraphs.Select(p => string.Concat(p.Runs.Select(run => run.Text))).ToArray();
        var starts = new int[texts.Length];
        var offset = 0;
        for (var i = 0; i < texts.Length; i++) { starts[i] = offset; offset += texts[i].Length + 1; }
        var spans = GuideQueryBlocks.Spans(string.Join("\n", texts));
        var result = new List<Paragraph>();
        var blocks = new List<Lifted>();
        var next = 0;
        foreach (var span in spans)
        {
            var first = Array.FindIndex(starts, start => start == span.Start);
            var last = first < 0 ? -1 : Array.FindIndex(starts, first, start => start + texts[Array.IndexOf(starts, start)].Length == span.End);
            if (first < next || last < first || !Enumerable.Range(first, last - first + 1).All(i => IsPlain(paragraphs[i]))) continue;
            result.AddRange(paragraphs.Skip(next).Take(first - next));
            var token = Token(blocks.Count);
            blocks.Add(new Lifted(token, texts[first..(last + 1)]));
            var stand = new Paragraph { DefaultWs = paragraphs[first].DefaultWs };
            stand.Runs.Add(new Run { Ws = paragraphs[first].Runs.FirstOrDefault()?.Ws ?? paragraphs[first].DefaultWs, Text = token });
            result.Add(stand);
            next = last + 1;
        }
        result.AddRange(paragraphs.Skip(next));
        return (result, blocks);
    }

    private static bool IsPlain(Paragraph paragraph) =>
        (paragraph.Style.Length == 0 || paragraph.Style == "Normal") && paragraph.Runs.Count <= 1 && !paragraph.Rtl &&
        paragraph.Runs.All(run => run.ObjData is null && !run.Text.Contains('\n', StringComparison.Ordinal));

    /// <summary>The written Markdown with each stand-in replaced by its block as a real fence.</summary>
    public static string RestoreFences(string markdown, IReadOnlyList<Lifted> blocks)
    {
        foreach (var block in blocks) markdown = markdown.Replace(block.Token, string.Join("\n", block.Lines), StringComparison.Ordinal);
        return markdown;
    }

    /// <summary>
    /// Replaces each fenced block in <paramref name="markdown"/> with a stand-in paragraph for the reader. The warnings name the
    /// blocks the parser found a problem in; such a block is kept as plain text all the same.
    /// </summary>
    public static (string Markdown, IReadOnlyList<Lifted> Blocks, IReadOnlyList<string> Warnings) LiftMarkdown(string markdown)
    {
        var parsed = GuideQueryBlocks.Parse(markdown);
        var spans = GuideQueryBlocks.Spans(markdown).OrderByDescending(s => s.Start).ToArray();
        var blocks = new List<Lifted>();
        var text = markdown;
        foreach (var span in spans)
        {
            var token = Token(spans.Length - 1 - blocks.Count);
            blocks.Add(new Lifted(token, markdown[span.Start..span.End].Split('\n')));
            text = GuideQueryBlocks.Replace(text, span, "\n\n" + token + "\n\n");
        }
        blocks.Reverse();
        var warnings = parsed.Diagnostics.Select(d => $"line {d.Line}: {d.Message} The block is kept as plain text.").ToArray();
        return (text, blocks, warnings);
    }

    /// <summary>
    /// The paragraphs the reader returned with each stand-in replaced by one paragraph per line of its block, in the stand-in's
    /// writing system and style, which is how a Guide stores a block.
    /// </summary>
    public static IReadOnlyList<Paragraph> RestoreParagraphs(IReadOnlyList<Paragraph> read, IReadOnlyList<Lifted> blocks)
    {
        if (blocks.Count == 0) return read;
        var result = new List<Paragraph>();
        foreach (var paragraph in read)
        {
            var text = string.Concat(paragraph.Runs.Select(run => run.Text));
            var block = blocks.FirstOrDefault(item => item.Token == text);
            if (block is null) { result.Add(paragraph); continue; }
            foreach (var line in block.Lines)
            {
                var copy = paragraph.Clone(keepKey: false);
                copy.Runs.Clear();
                if (line.Length > 0) copy.Runs.Add(paragraph.Runs[0].CloneWithText(line));
                result.Add(copy);
            }
        }
        return result;
    }
}
