using System;
using System.Globalization;
using System.Text;
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Helpers;
using Markdig.Parsers;
using Markdig.Parsers.Inlines;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using SIL.TextEditing.Markdown.Notation;

namespace SIL.TextEditing.Markdown.Reader
{
    /// <summary>A <c>[text]{...}</c> span: the inlines between the brackets, and the attribute block.</summary>
    internal sealed class SpanInline : ContainerInline
    {
        public AttrBlock Attrs = null!;
        /// <summary>Where the opening bracket is, as Markdig numbers it (the source text; a fence line is numbered from its own start).</summary>
        public int OpenAt;
    }

    /// <summary>The attribute block that follows a <c>[text](url)</c> or <c>![caption](url)</c>; it is the link's next sibling.</summary>
    internal sealed class AttrsInline : LeafInline
    {
        public AttrBlock Attrs = null!;
    }

    /// <summary>Pandoc's <c>^x^</c> (superscript) or <c>~x~</c> (subscript): only the reader takes them; the writer uses the attribute form.</summary>
    internal sealed class SupSubInline : LeafInline
    {
        public string Text = string.Empty;
        public bool IsSuper;
    }

    /// <summary>The Markdown extension of the notation (spec 3.2 to 3.4): <c>[text]{...}</c>, <c>{...}</c> after a link, and Pandoc's <c>^x^</c> and <c>~x~</c>.
    /// Numeric and named character references are Markdig's own (NUL and a lone surrogate become U+FFFD, as in CommonMark and Pandoc).</summary>
    internal sealed class NotationExtension : IMarkdownExtension
    {
        public void Setup(MarkdownPipelineBuilder pipeline)
        {
            pipeline.InlineParsers.InsertBefore<LinkInlineParser>(new SpanParser());
            pipeline.InlineParsers.Insert(0, new SupSubParser());
            pipeline.InlineParsers.Insert(0, new AfterLinkAttrsParser());
        }

        public void Setup(MarkdownPipeline pipeline, Markdig.Renderers.IMarkdownRenderer renderer) { }

        /// <summary>The two pipelines: the document (blocks and pipe tables) and the inline-only one used for the text of a block known to be a paragraph.</summary>
        public static readonly MarkdownPipeline Document = BuildDocument();

        static MarkdownPipeline BuildDocument()
        {
            var builder = new MarkdownPipelineBuilder()
                .UsePreciseSourceLocation()
                .UsePipeTables(new PipeTableOptions { RequireHeaderSeparator = true })
                .Use(new NotationExtension());
            PandocInterrupts.Install(builder);
            return builder.Build();
        }

        public static readonly MarkdownPipeline InlineOnly = BuildInlineOnly();

        static MarkdownPipeline BuildInlineOnly()
        {
            var builder = new MarkdownPipelineBuilder().UsePreciseSourceLocation().Use(new NotationExtension());
            builder.BlockParsers.Clear();
            builder.BlockParsers.Add(new ParagraphBlockParser());
            return builder.Build();
        }
    }

    /// <summary><c>^x^</c> and <c>~x~</c>, as Pandoc reads them: no space inside (unless escaped), something inside, and the same mark closes it. A doubled
    /// <c>~~</c> is Pandoc's strikeout, which the notation does not have, so it stays text.</summary>
    sealed class SupSubParser : InlineParser
    {
        public SupSubParser() { OpeningCharacters = new[] { '^', '~' }; }

        public override bool Match(InlineProcessor processor, ref StringSlice slice)
        {
            char mark = slice.CurrentChar;
            string text = slice.Text;
            int start = slice.Start;
            int limit = slice.End + 1;
            if (start + 1 >= limit || text[start + 1] == mark || (mark == '~' && text[start + 1] == '~')) return false;
            if (start > 0 && text[start - 1] == '\\') return false;
            var content = new StringBuilder();
            int i = start + 1;
            while (i < limit)
            {
                char c = text[i];
                if (c == mark) break;
                if (c == ' ' || c == '\t' || c == '\n' || c == '\r') return false;
                if (c == '\\' && i + 1 < limit)
                {
                    char n = text[i + 1];
                    if (n == ' ') { content.Append(' '); i += 2; continue; }
                    if (SpanParser.IsAsciiPunctuation(n)) { content.Append(n); i += 2; continue; }
                }
                if (c == '[' || c == ']' || c == '*' || c == '`' || c == '<' || c == '&' || c == '{' || c == '!') return false;
                content.Append(c);
                i++;
            }
            if (i >= limit || content.Length == 0) return false;
            if (mark == '~' && i + 1 < limit && text[i + 1] == '~') return false;
            int end = i + 1;
            processor.Inline = new SupSubInline
            {
                Text = content.ToString(),
                IsSuper = mark == '^',
                Span = new SourceSpan(processor.GetSourcePosition(start, out int line, out int column), processor.GetSourcePosition(end - 1)),
                Line = line,
                Column = column,
            };
            slice.Start = end;
            return true;
        }
    }

    /// <summary><c>]{...}</c> closes the nearest open <c>[</c> as a span with attributes. Everything else about <c>]</c> is Markdig's.</summary>
    sealed class SpanParser : InlineParser
    {
        internal static bool IsAsciiPunctuation(char c) => (c >= '!' && c <= '/') || (c >= ':' && c <= '@') || (c >= '[' && c <= '`') || (c >= '{' && c <= '~');

        public SpanParser() { OpeningCharacters = new[] { ']' }; }

        public override bool Match(InlineProcessor processor, ref StringSlice slice)
        {
            if (slice.PeekChar() != '{') return false;
            var context = ReaderContext.From(processor.Context);
            if (context == null) return false;
            var open = processor.Inline?.FirstParentOfType<LinkDelimiterInline>();
            if (open == null || open.IsImage) return false;
            int braceAt = slice.Start + 1;
            var status = AttributeScanner.TryScan(slice.Text, braceAt, slice.End + 1, out var block, out int errorPos, out string error);
            int delta = processor.GetSourcePosition(braceAt) - braceAt;
            if (status != ScanStatus.Ok)
            {
                ReportBadBlock(context, status, braceAt, errorPos, error, delta);
                return false;
            }
            var span = new SpanInline { Attrs = block!.Shifted(delta), OpenAt = open.Span.Start };
            int openStart = open.Span.Start;
            int line = open.Line, column = open.Column;
            open.ReplaceBy(span);
            processor.Inline = span;
            processor.PostProcessInlines(0, span, null, false);
            span.IsClosed = true;
            span.Span = new SourceSpan(openStart, processor.GetSourcePosition(block!.End - 1));
            span.Line = line;
            span.Column = column;
            slice.Start = block.End;
            return true;
        }

        /// <summary>The block with the brace it lacks, when that is an exact fix: the result reads as a block, and every bare word in it is a
        /// flag the grammar knows. Stray words (<c>{ws=ceb and more</c>) would become unknown flags, so they get no fix.</summary>
        static string? ClosingBraceFix(string observed)
        {
            string fixedBlock = observed + "}";
            if (AttributeScanner.TryScan(fixedBlock, 0, fixedBlock.Length, out var block, out _, out _) != ScanStatus.Ok) return null;
            foreach (var item in block!.Items)
                if (item.IsFlag) return null;
            return fixedBlock;
        }

        /// <param name="delta">What to add to an offset in Markdig's text to get the offset in the source (not zero inside a table).</param>
        internal static void ReportBadBlock(ReaderContext context, ScanStatus status, int blockStart, int errorPos, string error, int delta = 0)
        {
            string id = status == ScanStatus.Unclosed ? MarkdownDiagnosticIds.UnclosedMark : MarkdownDiagnosticIds.MalformedAttribute;
            int start = context.Base + delta + blockStart;
            // An unclosed block ran out at errorPos (the line end or the end of the text), so it ends there and the line end is not part
            // of it; any other error is at a character of the block, which is the last one it covers.
            int end = Math.Min(context.Source.Length, context.Base + delta + errorPos + (status == ScanStatus.Unclosed ? 0 : 1));
            string? fix = status == ScanStatus.Unclosed && error == "no closing }" && end > start ? ClosingBraceFix(context.Source.Substring(start, end - start)) : null;
            context.Report(id, MarkdownSeverity.Error, "The attribute block here is not valid: " + error + ". Escape the brace as \\{ if it is text.", start, end, fix);
        }
    }

    /// <summary><c>{...}</c> right after <c>[text](url)</c> or <c>![caption](url)</c>.</summary>
    sealed class AfterLinkAttrsParser : InlineParser
    {
        public AfterLinkAttrsParser() { OpeningCharacters = new[] { '{' }; }

        public override bool Match(InlineProcessor processor, ref StringSlice slice)
        {
            // The link must end right before the brace. Its Span is unreliable inside a table cell, so look at the text.
            if (!(processor.Inline is LinkInline link) || !link.IsClosed || slice.Start == 0 || slice.Text[slice.Start - 1] != ')') return false;
            var context = ReaderContext.From(processor.Context);
            if (context == null) return false;
            var status = AttributeScanner.TryScan(slice.Text, slice.Start, slice.End + 1, out var block, out int errorPos, out string error);
            int start = slice.Start;
            int delta = processor.GetSourcePosition(start) - start;
            if (status != ScanStatus.Ok)
            {
                SpanParser.ReportBadBlock(context, status, start, errorPos, error, delta);
                return false;
            }
            processor.Inline = new AttrsInline
            {
                Attrs = block!.Shifted(delta),
                Span = new SourceSpan(processor.GetSourcePosition(start, out int line, out int column), processor.GetSourcePosition(block!.End - 1)),
                Line = line,
                Column = column,
            };
            slice.Start = block.End;
            return true;
        }
    }
}

namespace SIL.TextEditing.Markdown.Reader
{
    /// <summary>Pandoc keeps a line that starts like a heading, a list item, a quote or a thematic break in the paragraph above it unless a blank line comes between
    /// (<c>blank_before_header</c> and its kin, and a list needs a blank line before it); CommonMark lets them interrupt. At the top of the document (and of a div)
    /// the notation follows Pandoc, so hard-wrapped text is not split. Inside a list item or a quote the CommonMark reading stays, and the reader reports what a
    /// list item or quote holds besides one paragraph.</summary>
    static class PandocInterrupts
    {
        public static bool Allowed(BlockProcessor processor, Block block)
            => !(block is ParagraphBlock) || !(processor.CurrentContainer is Markdig.Syntax.MarkdownDocument);

        public static void Install(MarkdownPipelineBuilder builder)
        {
            builder.BlockParsers.Replace<HeadingBlockParser>(new PandocHeadingParser());
            builder.BlockParsers.Replace<ListBlockParser>(new PandocListParser());
            builder.BlockParsers.Replace<QuoteBlockParser>(new PandocQuoteParser());
            builder.BlockParsers.Replace<ThematicBreakParser>(new PandocBreakParser());
        }
    }

    sealed class PandocHeadingParser : HeadingBlockParser
    {
        public override bool CanInterrupt(BlockProcessor processor, Block blockParent) => PandocInterrupts.Allowed(processor, blockParent) && base.CanInterrupt(processor, blockParent);
    }

    sealed class PandocListParser : ListBlockParser
    {
        public override bool CanInterrupt(BlockProcessor processor, Block blockParent) => PandocInterrupts.Allowed(processor, blockParent) && base.CanInterrupt(processor, blockParent);
    }

    sealed class PandocQuoteParser : QuoteBlockParser
    {
        public override bool CanInterrupt(BlockProcessor processor, Block blockParent) => PandocInterrupts.Allowed(processor, blockParent) && base.CanInterrupt(processor, blockParent);
    }

    sealed class PandocBreakParser : ThematicBreakParser
    {
        public override bool CanInterrupt(BlockProcessor processor, Block blockParent)
        {
            // A div fence is blanked to a thematic break in the text Markdig reads, and it must still end the paragraph before it.
            var context = ReaderContext.From(processor.Context);
            if (context != null && context.FenceStarts.Contains(processor.Line.Start)) return base.CanInterrupt(processor, blockParent);
            return PandocInterrupts.Allowed(processor, blockParent) && base.CanInterrupt(processor, blockParent);
        }
    }
}
