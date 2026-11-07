using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using SIL.TextEditing.Markdown.Notation;

namespace SIL.TextEditing.Markdown.Reader
{
    /// <summary>Reads FieldWorks Markdown into paragraphs: Markdig for the CommonMark blocks and inlines, our own scan for divs and the front matter
    /// (spec 3.1), the notation extension for spec 3.2 to 3.4.</summary>
    internal sealed class MarkdownReader
    {
        readonly MarkdownOptions _options;
        readonly ReaderContext _context;
        readonly string _source;
        readonly List<Paragraph> _paragraphs = new List<Paragraph>();
        // Where each paragraph came from, parallel to _paragraphs: the source range of the paragraph, and the number of the Markdown block it belongs to
        // (a table's rows share one, a list's items share one). The record reader places block-not-allowed findings with them.
        readonly List<ParagraphSource> _sources = new List<ParagraphSource>();
        int _spanStart, _spanEnd, _blockNumber;

        void StartBlock(int start, int end)
        {
            _blockNumber++;
            _spanStart = start;
            _spanEnd = end;
        }

        void SetSpan(Markdig.Syntax.SourceSpan span)
        {
            _spanStart = span.Start;
            _spanEnd = span.End + 1;
        }

        MarkdownReader(MarkdownOptions options, string source, bool bom)
        {
            _options = options;
            _context = new ReaderContext(options, source);
            _source = ReadFrontMatter(source, bom ? 1 : 0);
        }

        bool _unsupportedVersion;

        /// <summary>Finds the front matter (spec 2.1), takes the defaults and the version from it, and returns the text with it blanked (same length, so every
        /// offset is the source's). Front matter with no <c>fwmd</c> key is some other YAML block and only blanked.</summary>
        string ReadFrontMatter(string source, int start)
        {
            if (!FrontMatterFormat.TryFind(source, start, out int end, out bool closed) || !closed) return source;
            var block = source.Substring(0, end);
            int record = RecordKeyAt(block);
            if (record >= 0)
            {
                // A record file (owner decision R-9): its frame is not this reader's, so it says so once and reads nothing, instead of reporting the frame's
                // lines as malformed or reading only the body.
                int lineEnd = record;
                while (lineEnd < block.Length && block[lineEnd] != '\n' && block[lineEnd] != '\r') lineEnd++;
                _context.Report(MarkdownDiagnosticIds.RecordFile, MarkdownSeverity.Error,
                    "This is a record file (its front matter has record:); read it with RecordFileReader.", record, lineEnd);
                _unsupportedVersion = true;
                var whole = new StringBuilder(source);
                Blank(whole, 0, end);
                return whole.ToString();
            }
            var fm = FrontMatterFormat.Parse(block, (a, b, message) => { if (block.IndexOf("fwmd", StringComparison.Ordinal) >= 0) _context.Report(MarkdownDiagnosticIds.MalformedAttribute, MarkdownSeverity.Error, message, a, b); });
            fm.Range = new[] { 0, end };
            var blanked = new StringBuilder(source);
            Blank(blanked, 0, end);
            if (fm.Version == null)
            {
                return blanked.ToString();
            }
            _context.FrontMatter = fm;
            if (fm.Version.Value > MarkdownFrontMatter.CurrentVersion || fm.Version.Value < 1)
            {
                int at = source.IndexOf("fwmd", StringComparison.Ordinal);
                int lineEnd = at;
                while (lineEnd < end && source[lineEnd] != '\n' && source[lineEnd] != '\r') lineEnd++;
                _context.Report(MarkdownDiagnosticIds.VersionUnsupported, MarkdownSeverity.Error,
                    "fwmd: " + fm.Version.Value + " is not a version this reader knows (it reads version " + MarkdownFrontMatter.CurrentVersion + "). Update the tool.", at, lineEnd);
                _unsupportedVersion = true;
            }
            // The file names its defaults, and the project says whether it has them (spec 2.1): an id the project lacks is as unknown here as after ws=.
            if (fm.Analysis != null) { _context.DefaultAnalysis = fm.Analysis; _context.CheckWritingSystem(fm.Analysis, fm.AnalysisAt![0], fm.AnalysisAt[1]); }
            if (fm.Vernacular != null) { _context.DefaultVernacular = fm.Vernacular; _context.CheckWritingSystem(fm.Vernacular, fm.VernacularAt![0], fm.VernacularAt[1]); }
            return blanked.ToString();
        }

        /// <summary>The offset of a top-level <c>record</c> key in a front-matter block, or -1: <c>record:</c>, <c>"record":</c> or <c>'record':</c> at the
        /// start of a line, followed by a space, a tab or the line end; or, in a front matter written as one flow mapping (<c>{fwmd: 1, record: ...}</c>), a
        /// <c>record</c> key of that mapping.</summary>
        static int RecordKeyAt(string block)
        {
            // The first line is the opening fence.
            int pos = 0, depth = 0;
            while (pos < block.Length && block[pos] != '\n' && block[pos] != '\r') pos++;
            while (pos < block.Length && (block[pos] == '\n' || block[pos] == '\r')) pos++;
            bool flow = false, firstContent = true;
            while (pos < block.Length)
            {
                int lineEnd = pos;
                while (lineEnd < block.Length && block[lineEnd] != '\n' && block[lineEnd] != '\r') lineEnd++;
                {
                    int key = KeyAt(block, pos, lineEnd);
                    if (key >= 0 && !flow) return pos;
                    if (firstContent)
                    {
                        int i = pos;
                        while (i < lineEnd && (block[i] == ' ' || block[i] == '\t')) i++;
                        if (i < lineEnd) { firstContent = false; flow = block[i] == '{'; }
                    }
                    if (flow)
                    {
                        // A key of the outer mapping follows its { or a comma at depth one.
                        for (int i = pos; i < lineEnd; i++)
                        {
                            char c = block[i];
                            if (c == '"' || c == '\'')
                            {
                                int close = block.IndexOf(c, i + 1);
                                if (close < 0 || close > lineEnd) break;
                                if (depth == 1 && IsKeyStart(block, i)) { int k = KeyAt(block, i, lineEnd); if (k >= 0) return i; }
                                i = close;
                                continue;
                            }
                            if (c == '{' || c == '[') { depth++; if (depth == 1) continue; }
                            else if (c == '}' || c == ']') depth--;
                            else if (depth == 1 && IsKeyStart(block, i) && KeyAt(block, i, lineEnd) >= 0) return i;
                        }
                    }
                }
                pos = lineEnd;
                while (pos < block.Length && (block[pos] == '\n' || block[pos] == '\r')) pos++;
            }
            return -1;
        }

        /// <summary>True when the character at <paramref name="at"/> starts a key of a flow mapping: only white space back to its { or comma.</summary>
        static bool IsKeyStart(string s, int at)
        {
            int i = at - 1;
            while (i >= 0 && (s[i] == ' ' || s[i] == '\t' || s[i] == '\n' || s[i] == '\r')) i--;
            return i >= 0 && (s[i] == '{' || s[i] == ',');
        }

        /// <summary>0 when a <c>record</c> key (bare or quoted) and its colon start at <paramref name="at"/>, followed by white space or the end; -1 otherwise.</summary>
        static int KeyAt(string s, int at, int end)
        {
            int i = at;
            if (i < end && (s[i] == '"' || s[i] == '\''))
            {
                char q = s[i];
                if (string.CompareOrdinal(s, i + 1, "record", 0, 6) != 0 || i + 7 >= end || s[i + 7] != q) return -1;
                i += 8;
            }
            else
            {
                if (string.CompareOrdinal(s, i, "record", 0, 6) != 0) return -1;
                i += 6;
            }
            if (i >= end || s[i] != ':') return -1;
            i++;
            return i >= end || s[i] == ' ' || s[i] == '\t' ? 0 : -1;
        }

        MarkdownStyleNames Names => _options.StyleNames;

        public static MarkdownParseResult Read(string markdown, MarkdownOptions options)
        {
            var reader = new MarkdownReader(options, Prepare(markdown), markdown.Length > 0 && markdown[0] == Chars.ByteOrderMark);
            reader.ReadAll();
            var document = new Document();
            document.Paragraphs.AddRange(reader._paragraphs);
            return new MarkdownParseResult(document, reader._context.Diagnostics.Select((d, i) => (d, i)).OrderBy(x => x.d.Start).ThenBy(x => x.i).Select(x => x.d).ToList(), reader._context, reader._sources);
        }

        /// <summary>The paragraphs of a text, for the writer's check that what it wrote reads back. <paramref name="clean"/> is false when
        /// the parser found a syntax, table or heading error, which the canonical form never has. Findings about the project (an unknown
        /// writing system or link) are not the writer's concern.</summary>
        public static List<Paragraph> ReadParagraphs(string markdown, MarkdownOptions options, out bool clean)
        {
            var reader = new MarkdownReader(options, Prepare(markdown), markdown.Length > 0 && markdown[0] == Chars.ByteOrderMark);
            reader.ReadAll();
            clean = true;
            foreach (var d in reader._context.Diagnostics)
            {
                if (d.Severity == MarkdownSeverity.Error && IsSyntaxFinding(d.Id)) clean = false;
            }
            return reader._paragraphs;
        }

        /// <summary>True for the findings that mean the text is not well-formed notation (syntax, table and heading errors).</summary>
        public static bool IsSyntaxFinding(string id)
            => id.StartsWith("fwmd.syntax.", StringComparison.Ordinal) || id.StartsWith("fwmd.table.", StringComparison.Ordinal) || id.StartsWith("fwmd.heading.", StringComparison.Ordinal)
               || id == MarkdownDiagnosticIds.FieldBlockSyntax;

        /// <summary>One inline-only text (the abstract, spec 3.5): runs, with block syntax reported.</summary>
        public static List<Run> ReadInline(string text, MarkdownOptions options, out ReaderContext context)
        {
            var reader = new MarkdownReader(options, Prepare(text), text.Length > 0 && text[0] == Chars.ByteOrderMark);
            var runs = reader.ReadInlineText();
            context = reader._context;
            return runs;
        }

        static string Prepare(string markdown)
        {
            if (markdown.Length > 0 && markdown[0] == Chars.ByteOrderMark) return " " + markdown.Substring(1); // keep every offset
            return markdown;
        }

        // ---------------------------------------------------------------- the document

        sealed class Fence
        {
            public int Start;
            public AttrBlock? Attrs;
            public readonly List<int[]> Lines = new List<int[]>(); // {start, end} of each content line, terminator excluded
            public bool Closed;
        }

        void ReadAll()
        {
            if (_unsupportedVersion) return;
            var fences = new List<Fence>();
            string blanked = ScanFences(fences);
            var fenceStarts = new HashSet<int>();
            foreach (var fence in fences) fenceStarts.Add(fence.Start);
            _context.FenceStarts = fenceStarts;
            var tooBig = new List<int[]>();
            var document = TryParse(blanked, NotationExtension.Document, _context);
            if (document == null)
            {
                // Some block is beyond Markdig's limits. Find the blocks that are, keep their lines as text, and read the rest as usual.
                blanked = BlankChunks(blanked, fenceStarts, tooBig, probe: true);
                document = TryParse(blanked, NotationExtension.Document, _context);
                if (document == null)
                {
                    // Every block reads alone but the whole does not: nothing is left to read as structure.
                    tooBig.Clear();
                    blanked = BlankChunks(blanked, fenceStarts, tooBig, probe: false);
                    document = TryParse(blanked, NotationExtension.Document, _context) ?? new Markdig.Syntax.MarkdownDocument();
                }
            }
            _context.EndParse();
            var items = new List<KeyValuePair<int, object>>();
            foreach (var block in document) items.Add(new KeyValuePair<int, object>(block.Span.Start, block));
            foreach (var fence in fences) items.Add(new KeyValuePair<int, object>(fence.Start, fence));
            foreach (var chunk in tooBig)
            {
                ReportTooDeep(chunk[0], FirstLineEnd(chunk[0], chunk[1]));
                items.Add(new KeyValuePair<int, object>(chunk[0], chunk));
            }
            items.Sort((a, b) => a.Key.CompareTo(b.Key));
            foreach (var item in items)
            {
                if (item.Value is Fence fenceBlock) ReadFence(fenceBlock);
                else if (item.Value is int[] chunk) ReadLiteralLines(chunk[0], chunk[1]);
                else if (item.Value is ThematicBreakBlock && fenceStarts.Contains(item.Key)) continue; // a blanked fence, see Interrupt
                else ReadBlock((Block)item.Value, blanked);
            }
        }

        // ---------------------------------------------------------------- Markdig's limits

        /// <summary>True for what Markdig throws when text nests too deeply or is too large for it (its depth limit), or when the stack is
        /// nearly used up. The notation's own parsers and the host's callbacks do not throw these.</summary>
        static bool IsParseLimit(Exception e)
            => e is InsufficientExecutionStackException
               || (e is ArgumentException && e.Message.IndexOf("too deeply nested", StringComparison.Ordinal) >= 0);

        /// <summary>Reads the text with Markdig; null when it is beyond Markdig's limits. The findings a parse that did not finish made are dropped.</summary>
        static Markdig.Syntax.MarkdownDocument? TryParse(string text, Markdig.MarkdownPipeline pipeline, ReaderContext context)
        {
            int mark = context.Count;
            try
            {
                return Markdig.Markdown.Parse(text, pipeline, context.NewParserContext());
            }
            catch (Exception e) when (IsParseLimit(e))
            {
                context.Truncate(mark);
                return null;
            }
        }

        const string TooDeepMessage = "This text is nested too deeply or is too large to read. Split it into smaller paragraphs, lists or tables. Its lines are kept as plain text.";

        void ReportTooDeep(int start, int end) => _context.Report(MarkdownDiagnosticIds.TooDeep, MarkdownSeverity.Error, TooDeepMessage, start, end);

        int FirstLineEnd(int start, int end)
        {
            int i = start;
            while (i < end && _source[i] != '\n' && _source[i] != '\r') i++;
            return i;
        }

        Run LiteralRun(string text) => new Run { Text = text, Ws = _context.DefaultAnalysis };

        /// <summary>The lines of [start, end) as paragraphs of plain text, the way a block that cannot be read is kept.</summary>
        void ReadLiteralLines(int start, int end)
        {
            _context.Base = 0;
            int i = start;
            while (i < end)
            {
                int lineEnd = i;
                while (lineEnd < end && _source[lineEnd] != '\n' && _source[lineEnd] != '\r') lineEnd++;
                if (lineEnd > i) _paragraphs.Add(MakeParagraph(Names.Normal, new PropertyBag(), new List<Run> { LiteralRun(_source.Substring(i, lineEnd - i)) }, null));
                i = lineEnd < end && _source[lineEnd] == '\r' && lineEnd + 1 < end && _source[lineEnd + 1] == '\n' ? lineEnd + 2 : lineEnd + 1;
            }
        }

        /// <summary>Splits the text into blocks at its blank lines (and at the lines that stand for fences) and returns it with the blocks Markdig
        /// cannot read blanked out, same length, so every offset stays the source's. With <paramref name="probe"/> false every block is blanked.
        /// The blocks blanked are added to <paramref name="chunks"/> as {start, end} of their lines.</summary>
        string BlankChunks(string text, HashSet<int> fenceStarts, List<int[]> chunks, bool probe)
        {
            var blanked = new StringBuilder(text);
            int chunkStart = -1, chunkEnd = -1;
            int pos = 0;
            while (pos < text.Length)
            {
                int lineEnd = pos;
                while (lineEnd < text.Length && text[lineEnd] != '\n' && text[lineEnd] != '\r') lineEnd++;
                int next = lineEnd;
                if (next < text.Length) next += (text[next] == '\r' && next + 1 < text.Length && text[next + 1] == '\n') ? 2 : 1;
                int first = pos;
                while (first < lineEnd && (text[first] == ' ' || text[first] == '\t')) first++;
                bool boundary = first == lineEnd || fenceStarts.Contains(first);
                if (!boundary)
                {
                    if (chunkStart < 0) chunkStart = pos;
                    chunkEnd = lineEnd;
                }
                else FlushChunk(text, blanked, chunks, probe, ref chunkStart, chunkEnd);
                pos = next;
            }
            FlushChunk(text, blanked, chunks, probe, ref chunkStart, chunkEnd);
            return blanked.ToString();
        }

        void FlushChunk(string text, StringBuilder blanked, List<int[]> chunks, bool probe, ref int chunkStart, int chunkEnd)
        {
            if (chunkStart < 0) return;
            int start = chunkStart;
            chunkStart = -1;
            if (probe)
            {
                string chunk = text.Substring(start, chunkEnd - start);
                if (TryParse(chunk, NotationExtension.Document, new ReaderContext(_options, chunk)) != null) return;
            }
            chunks.Add(new[] { start, chunkEnd });
            Blank(blanked, start, chunkEnd);
        }

        List<Run> ReadInlineText()
        {
            string text = _source;
            _context.Base = 0;
            var document = TryParse(text, NotationExtension.Document, _context);
            if (document == null)
            {
                ReportTooDeep(0, FirstLineEnd(0, text.Length));
                return text.Length == 0 ? new List<Run>() : new List<Run> { LiteralRun(text) };
            }
            var runs = new List<Run>();
            bool first = true;
            foreach (var block in document)
            {
                if (block is ParagraphBlock paragraph)
                {
                    if (!first) {
                        _context.Report(MarkdownDiagnosticIds.FieldBlockSyntax, MarkdownSeverity.Error,
                            "A multi-string field such as the abstract is one paragraph. A blank line starts a second one; use a hard line break (a backslash at the end of the line) instead.",
                            paragraph.Span.Start, paragraph.Span.End + 1);
                        runs.Add(new Run { Text = Chars.LineSeparatorText, Ws = _context.DefaultAnalysis });
                    }
                    first = false;
                    if (_source.Substring(paragraph.Span.Start, Math.Min(3, _source.Length - paragraph.Span.Start)) == ":::")
                        _context.Report(MarkdownDiagnosticIds.FieldBlockSyntax, MarkdownSeverity.Error,
                            "A multi-string field such as the abstract can't hold a div. Escape the first colon, as in \\: .", paragraph.Span.Start, paragraph.Span.End + 1);
                    runs.AddRange(InlineReader.Read(paragraph.Inline, _context));
                    continue;
                }
                _context.Report(MarkdownDiagnosticIds.FieldBlockSyntax, MarkdownSeverity.Error,
                    "A multi-string field such as the abstract can't hold " + Describe(block) + ". Move it to the body.", block.Span.Start, block.Span.End + 1);
            }
            var p = new Paragraph { Runs = runs };
            p.Normalize();
            if (_options.NormalizeText) ToNfd(p.Runs);
            return p.Runs.ToList();
        }

        static string Describe(Block block)
        {
            if (block is HeadingBlock) return "a heading";
            if (block is ListBlock) return "a list";
            if (block is QuoteBlock) return "a block quote";
            if (block is Table) return "a table";
            return "block syntax";
        }

        // ---------------------------------------------------------------- fences

        /// <summary>Finds the <c>::: {...}</c> blocks and returns the text with them blanked out (same length, so Markdig's offsets
        /// stay the source's offsets).</summary>
        string ScanFences(List<Fence> fences)
        {
            string s = _source;
            var blanked = new StringBuilder(s);
            Fence? open = null;
            int openColons = 0;
            int openLineStart = 0;
            int pos = 0;
            int prevStart = -1, prevEnd = -1; // the line before, when it is text that a fence would not end (not a fence, a heading or a rule)
            while (pos < s.Length)
            {
                int lineStart = pos;
                int lineEnd = lineStart;
                while (lineEnd < s.Length && s[lineEnd] != '\n' && s[lineEnd] != '\r') lineEnd++;
                int next = lineEnd;
                if (next < s.Length) next += (s[next] == '\r' && next + 1 < s.Length && s[next + 1] == '\n') ? 2 : 1;
                if (open == null)
                {
                    if (TryOpener(lineStart, lineEnd, out int colons, out Fence? fence))
                    {
                        if (prevStart >= 0 && TextLineBefore(prevStart, prevEnd))
                            _context.Report(MarkdownDiagnosticIds.UnsupportedMarkup, MarkdownSeverity.Error,
                                "Pandoc keeps a ::: line that follows a line of text in that paragraph, as text. Put a blank line before it.", lineStart, lineEnd,
                                "\n" + _source.Substring(lineStart, lineEnd - lineStart));
                        open = fence;
                        openColons = colons;
                        openLineStart = lineStart;
                    }
                }
                else
                {
                    if (IsCloser(lineStart, lineEnd, openColons))
                    {
                        open.Closed = true;
                        Blank(blanked, openLineStart, next);
                        Interrupt(blanked, open);
                        fences.Add(open);
                        open = null;
                    }
                    else
                    {
                        if (IsFenceLike(lineStart, lineEnd))
                            _context.Report(MarkdownDiagnosticIds.UnsupportedMarkup, MarkdownSeverity.Error,
                                "A div inside a div has no counterpart in FieldWorks text. Escape the first colon, as in \\: , if this is text.", lineStart, lineEnd, "\\" + _source.Substring(lineStart, lineEnd - lineStart));
                        open.Lines.Add(new[] { lineStart, lineEnd });
                    }
                }
                prevStart = lineStart; prevEnd = lineEnd;
                if (open != null || lineEnd == lineStart) prevStart = -1;
                pos = next;
            }
            if (open != null)
            {
                _context.Report(MarkdownDiagnosticIds.UnclosedMark, MarkdownSeverity.Error, "This ::: block is never closed. Add a line with :::.", open.Start, Math.Min(_source.Length, open.Start + 3),
                    replacement: null);
                Blank(blanked, openLineStart, s.Length);
                Interrupt(blanked, open);
                fences.Add(open);
            }
            return blanked.ToString();
        }

        /// <summary>A blanked fence still ends the list, quote or paragraph before it: its first three characters become a thematic break,
        /// which the reader skips. Without it, two lists with a fence between them would read as one.</summary>
        static void Interrupt(StringBuilder text, Fence fence)
        {
            for (int i = 0; i < 3; i++) text[fence.Start + i] = '*';
        }

        /// <summary>True when the line is text that Pandoc's paragraph goes on through: not blank, not a fence, an ATX heading, a setext underline, a rule or a table row.</summary>
        bool TextLineBefore(int start, int end)
        {
            string line = _source.Substring(start, end - start).Trim();
            if (line.Length == 0 || line.StartsWith(":::", StringComparison.Ordinal) || line[0] == '#' || line[0] == '|') return false;
            bool underline = true;
            foreach (char c in line) if (c != '-' && c != '=' && c != '*' && c != '_' && c != ' ') underline = false;
            return !underline;
        }

        static void Blank(StringBuilder text, int start, int end)
        {
            for (int i = start; i < end; i++) if (text[i] != '\n' && text[i] != '\r') text[i] = ' ';
        }

        bool TryOpener(int lineStart, int lineEnd, out int colons, out Fence? fence)
        {
            colons = 0;
            fence = null;
            string s = _source;
            int i = lineStart;
            while (i < lineEnd && i - lineStart < 3 && s[i] == ' ') i++;
            int colonStart = i;
            while (i < lineEnd && s[i] == ':') i++;
            colons = i - colonStart;
            if (colons < 3) return false;
            while (i < lineEnd && s[i] == ' ') i++;
            if (i >= lineEnd || s[i] != '{') return false;
            fence = new Fence { Start = colonStart };
            var status = AttributeScanner.TryScan(s, i, lineEnd, out var block, out int errorPos, out string error);
            if (status == ScanStatus.Ok)
            {
                int after = block!.End;
                while (after < lineEnd && s[after] == ' ') after++;
                if (after < lineEnd)
                    _context.Report(MarkdownDiagnosticIds.MalformedAttribute, MarkdownSeverity.Error, "Nothing may follow the attribute block of a ::: line.", after, lineEnd);
                fence.Attrs = block;
            }
            else
            {
                _context.Base = 0;
                SpanParser.ReportBadBlock(_context, status, i, errorPos, error);
            }
            return true;
        }

        /// <summary>A line of three or more colons followed by anything: Pandoc would read it as the fence of a nested div.</summary>
        bool IsFenceLike(int lineStart, int lineEnd)
        {
            int i = lineStart;
            while (i < lineEnd && i - lineStart < 3 && _source[i] == ' ') i++;
            int colons = i;
            while (i < lineEnd && _source[i] == ':') i++;
            return i - colons >= 3;
        }

        bool IsCloser(int lineStart, int lineEnd, int openColons)
        {
            string s = _source;
            int i = lineStart;
            while (i < lineEnd && i - lineStart < 3 && s[i] == ' ') i++;
            int colonStart = i;
            while (i < lineEnd && s[i] == ':') i++;
            if (i - colonStart < openColons) return false;
            while (i < lineEnd && (s[i] == ' ' || s[i] == '\t')) i++;
            return i == lineEnd;
        }

        void ReadFence(Fence fence)
        {
            var (style, props) = fence.Attrs == null ? (Names.Normal, new PropertyBag()) : ReadParagraphAttributes(fence.Attrs, false, out _);
            var lines = fence.Lines;
            // Paragraphs in a div are separated by blank lines, as everywhere else (spec 3.1).
            int i = 0;
            while (i < lines.Count)
            {
                if (IsBlank(lines[i][0], lines[i][1])) { i++; continue; }
                int first = i;
                while (i < lines.Count && !IsBlank(lines[i][0], lines[i][1])) i++;
                ReadDivParagraph(lines[first][0], lines[i - 1][1], style, props);
            }
        }

        bool IsBlank(int start, int end)
        {
            for (int i = start; i < end; i++) if (_source[i] != ' ' && _source[i] != '\t') return false;
            return true;
        }

        /// <summary>One paragraph of a div. The text is read with the block parser, so a block marker the author forgot to escape (a heading, a list, a quote, a
        /// table) is found here as it would be by Pandoc: it is an error and the escape is the fix (spec 3.1); the text is then read as a paragraph.</summary>
        void ReadDivParagraph(int start, int end, string style, PropertyBag props)
        {
            string text = _source.Substring(start, end - start);
            _context.Base = 0;
            if (TryEmptyMarker(text, start, style, props)) return;
            CheckPandoc(start, end, true, false, false);
            _context.Base = start;
            StartBlock(start, end);
            int mark = _context.Count;
            var document = TryParse(text, NotationExtension.Document, _context);
            if (document == null)
            {
                ReportTooDeep(start, end);
                _context.Base = 0;
                _paragraphs.Add(MakeParagraph(style, props, new List<Run> { LiteralRun(text) }, null));
                return;
            }
            bool single = document.Count == 1 && document[0] is ParagraphBlock;
            if (!single)
            {
                _context.Truncate(mark);
                _context.Report(MarkdownDiagnosticIds.UnsupportedMarkup, MarkdownSeverity.Error,
                    "A paragraph in a styled block cannot start with block markup: this reads as " + DescribeBlock(document) + ". Escape the first marker, as in \\# or 1\\. .",
                    start, end, LineStart.Escaped(text));
                document = TryParse(text, NotationExtension.InlineOnly, _context);
            }
            var runs = new List<Run>();
            if (document != null)
                foreach (var block in document)
                    if (block is ParagraphBlock paragraph) runs.AddRange(InlineReader.Read(paragraph.Inline, _context));
            _context.Base = 0;
            _paragraphs.Add(MakeParagraph(style, props, runs, null));
        }

        static string DescribeBlock(Markdig.Syntax.MarkdownDocument document)
        {
            if (document.Count == 0) return "no paragraph";
            switch (document[0])
            {
                case HeadingBlock _: return "a heading";
                case ListBlock _: return "a list";
                case QuoteBlock _: return "a block quote";
                case Table _: return "a table";
                case ThematicBreakBlock _: return "a thematic break";
                case LinkReferenceDefinitionGroup _: return "a link reference definition";
                case CodeBlock _: return "a code block";
                case HtmlBlock _: return "raw HTML";
                default: return "block syntax";
            }
        }

        // ---------------------------------------------------------------- blocks

        void ReadBlock(Block block, string text)
        {
            _context.Base = 0;
            StartBlock(block.Span.Start, block.Span.End + 1);
            switch (block)
            {
                case ParagraphBlock paragraph:
                    ReadParagraph(paragraph, text);
                    break;
                case HeadingBlock heading:
                    ReadHeading(heading);
                    break;
                case ListBlock list:
                    ReadList(list);
                    break;
                case QuoteBlock quote:
                    ReadQuote(quote);
                    break;
                case Table table:
                    ReadTable(table);
                    break;
                case LinkReferenceDefinitionGroup group:
                    UnsupportedBlock(group.Count > 0 ? group[0] : (Block)group, "A link reference definition is not supported. Escape the [ as \\[.");
                    break;
                case ThematicBreakBlock rule:
                    UnsupportedBlock(rule, "A thematic break is not supported. Escape the first character.");
                    break;
                case CodeBlock code:
                    UnsupportedBlock(code, "A code block is not supported.");
                    break;
                case HtmlBlock html:
                    UnsupportedBlock(html, "Raw HTML is not supported. Escape the < as \\<.");
                    break;
                default:
                    UnsupportedBlock(block, "This block syntax is not supported.");
                    break;
            }
        }

        void UnsupportedBlock(Block block, string message)
        {
            int start = block.Span.Start, end = block.Span.End + 1;
            // Raw HTML is fixed by escaping its angle brackets.
            string? fix = block is HtmlBlock && start >= 0 && end <= _source.Length && end >= start ? _source.Substring(start, end - start).Replace("<", "\\<") : null;
            _context.Report(MarkdownDiagnosticIds.UnsupportedMarkup, MarkdownSeverity.Error, message, start, end, fix);
        }

        void ReadParagraph(ParagraphBlock block, string text)
        {
            string raw = text.Substring(block.Span.Start, block.Span.Length);
            if (TryLoneMarkers(raw)) return;
            if (TryEmptyMarker(raw, block.Span.Start))
            {
                // Markdig read the line as text before the marker was recognised, so a quoted value that looks like a link or a
                // span has drawn findings of its own. The marker is read from the raw line, and those findings do not apply.
                _context.RemoveParsed(block.Span.Start, block.Span.End + 1);
                return;
            }
            CheckPandoc(block.Span.Start, block.Span.End + 1, true, NearTable(block));
            _paragraphs.Add(MakeParagraph(Names.Normal, new PropertyBag(), InlineReader.Read(block.Inline, _context), null));
        }

        static readonly System.Text.RegularExpressions.Regex LoneMarker = new System.Text.RegularExpressions.Regex(@"^ {0,3}(?:[-*+]|[0-9]{1,9}[.)])[ \t]*$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

        /// <summary>Lines that hold only a list marker are empty list items, which Pandoc reads too: empty paragraphs in the list's style. Markdig reads a lone marker
        /// that follows a paragraph as text, so the reader takes it here.</summary>
        bool TryLoneMarkers(string raw)
        {
            var lines = raw.Split('\n');
            foreach (string line in lines) if (!LoneMarker.IsMatch(line.TrimEnd('\r'))) return false;
            foreach (string line in lines)
            {
                string t = line.Trim();
                string style = t[0] >= '0' && t[0] <= '9' ? Names.NumberedList : Names.BulletedList;
                _paragraphs.Add(MakeParagraph(style, new PropertyBag(), new List<Run>(), null));
            }
            return true;
        }

        /// <summary>A paragraph that is exactly <c>[]{.empty ...}</c> is an empty paragraph (spec 3.1). The attributes may name its writing system
        /// (<c>.v</c> or <c>ws=</c>) because an empty paragraph has no run to carry one; the reader also takes paragraph attributes here (the writer puts a styled
        /// empty paragraph in a div). Inside a div the paragraph takes the div's style and properties unless the marker gives its own.</summary>
        bool TryEmptyMarker(string raw, int rawStart, string? divStyle = null, PropertyBag? divProps = null)
        {
            int first = 0;
            while (first < raw.Length && (raw[first] == ' ' || raw[first] == '\t')) first++;
            int last = raw.Length;
            while (last > first && (raw[last - 1] == ' ' || raw[last - 1] == '\t')) last--;
            string line = raw.Substring(first, last - first);
            if (!line.StartsWith("[]{", StringComparison.Ordinal) || line.IndexOf('\n') >= 0 || line.IndexOf('\r') >= 0 || line[line.Length - 1] != '}') return false;
            int at = rawStart + first + 2;
            var status = AttributeScanner.TryScan(_source, at, rawStart + last, out var block, out int errorPos, out string error);
            if (status != ScanStatus.Ok || block!.End != rawStart + last)
            {
                // Not a marker unless it says .empty somewhere: a bad block is reported by the inline reader as for any other span.
                return false;
            }
            bool isEmpty = false;
            foreach (var item in block.Items) if (item.IsClass && item.Key == "empty") isEmpty = true;
            if (!isEmpty) return false;
            var (style, props) = ReadParagraphAttributes(block, true, out string? defaultWs, out bool styleGiven);
            if (divProps != null)
            {
                var merged = divProps.Clone();
                foreach (var kv in props.IntProps) merged.IntProps[kv.Key] = kv.Value;
                foreach (var kv in props.StrProps) merged.StrProps[kv.Key] = kv.Value;
                props = merged;
                if (!styleGiven && divStyle != null) style = divStyle;
            }
            _paragraphs.Add(MakeParagraph(style, props, new List<Run>(), defaultWs));
            return true;
        }

        /// <summary>True when a pipe table is the block before or after: Pandoc reads a <c>Table:</c> or <c>:</c> line next to a table as its caption.</summary>
        static bool NearTable(Block block)
        {
            if (!(block.Parent is ContainerBlock parent)) return false;
            int index = parent.IndexOf(block);
            return (index > 0 && parent[index - 1] is Table) || (index >= 0 && index + 1 < parent.Count && parent[index + 1] is Table);
        }

        void CheckPandoc(int start, int end, bool lines = true, bool nearTable = false, bool fences = true)
        {
            if (start < 0) return;
            if (end > _source.Length) end = _source.Length;
            PandocConstructs.Find(_source, start, end, lines, (s, e, message, fix) =>
                _context.Report(MarkdownDiagnosticIds.UnsupportedMarkup, MarkdownSeverity.Error, message, s, e, fix), nearTable, fences);
        }

        void CheckHeading(HeadingBlock heading)
        {
            int start = heading.Span.Start, end = Math.Min(heading.Span.End + 1, _source.Length);
            if (start < 0 || end <= start) return;
            string line = _source.Substring(start, end - start).TrimEnd(Newlines);
            int open = PandocConstructs.HeadingAttributes(line);
            if (open >= 0)
                _context.Report(MarkdownDiagnosticIds.UnsupportedMarkup, MarkdownSeverity.Error,
                    "Pandoc reads a group in braces at the end of a heading as its attributes. Escape the opening brace.", start + open, start + line.Length,
                    "\\" + line.Substring(open));
            CheckPandoc(start, end, false);
        }

        static readonly char[] Newlines = { (char)10, (char)13 };

        void ReadHeading(HeadingBlock heading)
        {
            string? style = Names.Heading(heading.Level);
            if (style == null)
            {
                _context.Report(MarkdownDiagnosticIds.HeadingTooDeep, MarkdownSeverity.Error, "A heading of level " + heading.Level + " has no style. Use ### or a div with custom-style.",
                    heading.Span.Start, heading.Span.End + 1);
                style = "Heading " + heading.Level;
            }
            CheckHeading(heading);
            _paragraphs.Add(MakeParagraph(style, new PropertyBag(), InlineReader.Read(heading.Inline, _context), null));
        }

        void ReadList(ListBlock list)
        {
            string style = list.IsOrdered ? Names.NumberedList : Names.BulletedList;
            foreach (var child in list)
            {
                if (!(child is ListItemBlock item)) continue;
                bool first = true;
                foreach (var part in item)
                {
                    if (part is ParagraphBlock paragraph)
                    {
                        if (!first)
                            _context.Report(MarkdownDiagnosticIds.UnsupportedMarkup, MarkdownSeverity.Error, "A list item is one paragraph. Start a new item.", paragraph.Span.Start, paragraph.Span.End + 1);
                        first = false;
                        SetSpan(item.Span);   // the item, so that its marker is in the range
                        CheckPandoc(paragraph.Span.Start, paragraph.Span.End + 1);
                        _paragraphs.Add(MakeParagraph(style, new PropertyBag(), InlineReader.Read(paragraph.Inline, _context), null));
                    }
                    else UnsupportedBlock(part, "A list item holds one paragraph; nested blocks are not supported.");
                }
                // A marker with nothing after it is an empty list item in Pandoc too: an empty paragraph in the list's style.
                if (item.Count == 0) { SetSpan(item.Span); _paragraphs.Add(MakeParagraph(style, new PropertyBag(), new List<Run>(), null)); }
            }
        }

        void ReadQuote(QuoteBlock quote)
        {
            foreach (var part in quote)
            {
                if (part is ParagraphBlock paragraph)
                    _paragraphs.Add(MakeParagraph(Names.BlockQuote, new PropertyBag(), InlineReader.Read(paragraph.Inline, _context), null));
                else UnsupportedBlock(part, "A block quote holds paragraphs only.");
            }
        }

        void ReadTable(Table table)
        {
            if (Names.TableHeader == null || Names.TableRow == null)
                _context.Report(MarkdownDiagnosticIds.UnsupportedMarkup, MarkdownSeverity.Error,
                    "A pipe table needs table styles, and the host names none. Its rows are read as paragraphs.", table.Span.Start, table.Span.End + 1);
            ReportAlignments(table);
            int headerCells = -1;
            int rowNumber = 0; // the header is row 1, the delimiter row is not counted
            foreach (var child in table)
            {
                if (!(child is TableRow row)) continue;
                rowNumber++;
                SetSpan(row.Span);
                var cellRuns = new List<List<Run>>();
                foreach (var cellBlock in row)
                {
                    if (!(cellBlock is TableCell cell)) continue;
                    if (IsPhantom(cell)) continue;
                    var runs = new List<Run>();
                    foreach (var part in cell)
                        if (part is ParagraphBlock paragraph) runs.AddRange(InlineReader.Read(paragraph.Inline, _context));
                    // A raw tab reads as a space, but a cell may not hold one: the tab is the cell separator when the row is stored.
                    bool tabInCell = cell.Span.Start >= 0 && cell.Span.End < _source.Length && _source.IndexOf('\t', cell.Span.Start, cell.Span.End - cell.Span.Start + 1) >= 0;
                    foreach (var run in runs)
                        if (run.Text.IndexOf('\t') >= 0 || run.Text.IndexOf(Chars.LineSeparator) >= 0) { tabInCell = true; break; }
                    if (tabInCell)
                        _context.Report(MarkdownDiagnosticIds.TableTabInCell, MarkdownSeverity.Error, "A tab or line break inside a cell can't be stored. Replace it with spaces.", cell.Span.Start, cell.Span.End + 1);
                    cellRuns.Add(runs);
                }
                bool isHeader = row.IsHeader;
                if (isHeader) { headerCells = cellRuns.Count; ReportDelimiterWidth(table, headerCells); }
                else if (headerCells >= 0 && cellRuns.Count != headerCells)
                    _context.Report(MarkdownDiagnosticIds.TableRagged, MarkdownSeverity.Error,
                        "Row " + rowNumber + " has " + cellRuns.Count + (cellRuns.Count == 1 ? " cell" : " cells") + ", the header has " + headerCells + ". Add an empty cell or remove one.",
                        row.Span.Start, row.Span.End + 1);
                var joined = new List<Run>();
                for (int c = 0; c < cellRuns.Count; c++)
                {
                    if (c > 0) joined.Add(new Run { Text = "\t", Ws = _context.DefaultAnalysis });
                    joined.AddRange(cellRuns[c]);
                }
                _paragraphs.Add(MakeParagraph((isHeader ? Names.TableHeader : Names.TableRow) ?? Names.Normal, new PropertyBag(), joined, null));
            }
        }

        /// <summary>The second line of the table, the delimiter row: its start, and its end before the line end.</summary>
        void DelimiterLine(Table table, out int lineStart, out int lineEnd)
        {
            string s = _source;
            int headerEnd = table.Span.Start;
            while (headerEnd < s.Length && s[headerEnd] != '\n' && s[headerEnd] != '\r') headerEnd++;
            lineStart = headerEnd < s.Length ? headerEnd + (s[headerEnd] == '\r' && headerEnd + 1 < s.Length && s[headerEnd + 1] == '\n' ? 2 : 1) : headerEnd;
            lineEnd = lineStart;
            while (lineEnd < s.Length && s[lineEnd] != '\n' && s[lineEnd] != '\r') lineEnd++;
        }

        /// <summary>A delimiter row with another number of cells than the header is a finding of the table's own (<c>fwmd.table.ragged</c>, the
        /// same fault as a row of another width); Markdig reads such a table by the header's width and says nothing.</summary>
        void ReportDelimiterWidth(Table table, int headerCells)
        {
            DelimiterLine(table, out int lineStart, out int lineEnd);
            int a = lineStart, b = lineEnd;
            while (a < b && (_source[a] == ' ' || _source[a] == '\t')) a++;
            while (b > a && (_source[b - 1] == ' ' || _source[b - 1] == '\t')) b--;
            if (a < b && _source[a] == '|') a++;
            if (b > a && _source[b - 1] == '|') b--;
            int cells = 1;
            for (int i = a; i < b; i++) if (_source[i] == '|') cells++;
            if (cells == headerCells) return;
            string replacement = "|" + string.Concat(Enumerable.Repeat("---|", headerCells));
            _context.Report(MarkdownDiagnosticIds.TableRagged, MarkdownSeverity.Error,
                "The delimiter row has " + cells + (cells == 1 ? " cell" : " cells") + ", the header has " + headerCells + ". Write one --- for each column of the header.",
                lineStart, lineEnd, replacement);
        }

        /// <summary>A column alignment (<c>:---</c>, <c>---:</c>, <c>:---:</c>) has nowhere to be stored (spec 3.1). Each aligned cell of the
        /// delimiter row, the second line of the table, is a finding of its own, and replacing it with <c>---</c> is the fix.</summary>
        void ReportAlignments(Table table)
        {
            bool aligned = false;
            foreach (var column in table.ColumnDefinitions) if (column.Alignment != null) aligned = true;
            if (!aligned) return;
            const string message = "A column alignment can't be stored. Use ---.";
            string s = _source;
            DelimiterLine(table, out int lineStart, out int lineEnd);
            bool any = false;
            int i = lineStart;
            while (i < lineEnd)
            {
                int cellEnd = i;
                while (cellEnd < lineEnd && s[cellEnd] != '|') cellEnd++;
                int a = i, b = cellEnd;
                while (a < b && (s[a] == ' ' || s[a] == '\t')) a++;
                while (b > a && (s[b - 1] == ' ' || s[b - 1] == '\t')) b--;
                if (a < b && (s[a] == ':' || s[b - 1] == ':'))
                {
                    any = true;
                    _context.Report(MarkdownDiagnosticIds.TableAlignment, MarkdownSeverity.Error, message, a, b, replacement: "---");
                }
                i = cellEnd + 1;
            }
            // Markdig saw an alignment this scan did not find: report the delimiter row as a whole.
            if (!any) _context.Report(MarkdownDiagnosticIds.TableAlignment, MarkdownSeverity.Error, message, lineStart, lineEnd);
        }

        /// <summary>Markdig pads short rows with empty cells that have no source.</summary>
        static bool IsPhantom(TableCell cell) => cell.Span.End < cell.Span.Start && cell.Span.Start == 0;

        // ---------------------------------------------------------------- paragraph attributes

        /// <summary>The attributes of a div or of <c>[]{.empty ...}</c>: the style (<c>custom-style</c>), paragraph properties and, for an empty paragraph, the
        /// writing system typing starts in (<c>.v</c> or <c>ws=</c>).</summary>
        (string style, PropertyBag props) ReadParagraphAttributes(AttrBlock attrs, bool isEmpty, out string? defaultWs, out bool styleGiven)
        {
            defaultWs = null;
            string? style = null;
            AttrItem? styleItem = null, wsItem = null, vItem = null;
            var rest = new List<AttrItem>();
            foreach (var item in attrs.Items)
            {
                if (item.IsClass && item.Key == "empty" && isEmpty) continue;
                if (!item.IsClass && item.Key == "custom-style" && !item.IsFlag)
                {
                    if (styleItem != null) _context.Report(MarkdownDiagnosticIds.DuplicateAttribute, MarkdownSeverity.Error, "'custom-style' appears twice. Keep one.", item.Start, item.End);
                    styleItem = styleItem ?? item;
                    continue;
                }
                if (isEmpty && item.IsClass && item.Key == "v") { vItem = item; continue; }
                if (isEmpty && !item.IsClass && item.Key == "ws" && !item.IsFlag) { wsItem = item; continue; }
                rest.Add(item);
            }
            var props = new PropertyBag();
            PropertyKeys.Apply(rest, props, _context, item => ReportUnknownParagraphItem(item, isEmpty));
            if (styleItem != null)
            {
                style = styleItem.Value!;
                var catalog = _options.Styles;
                if (catalog != null && style.Length > 0) _context.CheckStyle(style, true, styleItem.Start, styleItem.End);
            }
            if (props.StrProps.TryGetValue(TextPropertyNumbers.NamedStyle, out var s133))
            {
                if (style != null) _context.Report(MarkdownDiagnosticIds.DuplicateAttribute, MarkdownSeverity.Error, "s133 is the style; it cannot be combined with custom-style.", attrs.Start, attrs.End);
                style = s133;
            }
            props.StrProps.Remove(TextPropertyNumbers.NamedStyle);
            if (isEmpty)
            {
                if (wsItem != null && vItem != null) _context.Report(MarkdownDiagnosticIds.DuplicateAttribute, MarkdownSeverity.Error, ".v and ws both name the writing system. Keep one.", vItem.Start, vItem.End);
                if (wsItem != null)
                {
                    defaultWs = wsItem.Value;
                    _context.CheckWritingSystem(defaultWs!, wsItem.Start, wsItem.End);
                }
                else if (vItem != null) defaultWs = _context.DefaultVernacular;
            }
            styleGiven = style != null;
            return (style ?? Names.Normal, props);
        }

        (string style, PropertyBag props) ReadParagraphAttributes(AttrBlock attrs, bool isEmpty, out string? defaultWs)
            => ReadParagraphAttributes(attrs, isEmpty, out defaultWs, out _);

        void ReportUnknownParagraphItem(AttrItem item, bool isEmpty)
        {
            if (!item.IsClass && (item.Key == "guid" || item.Key == "url" || item.Key == "tool" || item.Key == "ws" || item.Key == "x") || (item.IsClass && (item.Key == "name" || item.Key == "example" || item.Key == "broken" || item.Key == "nostyle" || item.Key == "v" || item.Key == "nul" || item.Key == "surrogate")))
            {
                _context.Report(MarkdownDiagnosticIds.UnknownAttribute, MarkdownSeverity.Error, "'" + item.Spelling + "' is for runs, not for a block of paragraphs.", item.Start, item.End);
                return;
            }
            var known = new List<string> { "custom-style" };
            if (isEmpty) { known.Add(".v"); known.Add("ws"); known.Add(".empty"); }
            known.AddRange(PropertyKeys.AllKeys());
            _context.ReportUnknown(item, known);
        }

        /// <summary>The text of every run as NFD, the form the editor stores (ADR 0018). Done once the runs are merged, so a mark and its base
        /// that Markdig handed over in two pieces compose as one string. Findings and objects keep the source's offsets: only the runs change.</summary>
        static void ToNfd(IList<Run> runs)
        {
            foreach (var run in runs) run.Text = TextNormalizer.Normalize(run.Text, NormalizationForm.FormD);
        }

        Paragraph MakeParagraph(string style, PropertyBag props, List<Run> runs, string? defaultWs)
        {
            var paragraph = new Paragraph { Style = style, Props = props.Clone(), Runs = runs };
            if (style.Length > 0) paragraph.Props.StrProps[TextPropertyNumbers.NamedStyle] = style;
            paragraph.Rtl = paragraph.Props.TryGetInt(128, out int rtl) && rtl != 0;
            paragraph.Normalize();
            if (_options.NormalizeText) ToNfd(paragraph.Runs);
            paragraph.DefaultWs = paragraph.Runs.Count > 0 ? paragraph.Runs[0].Ws : defaultWs ?? _context.DefaultAnalysis;
            _sources.Add(new ParagraphSource(_spanStart, _spanEnd, _blockNumber));
            return paragraph;
        }
    }
}
