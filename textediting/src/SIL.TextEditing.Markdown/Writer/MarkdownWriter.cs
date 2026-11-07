using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using SIL.TextEditing.Markdown.Notation;
using SIL.TextEditing.Markdown.Reader;

namespace SIL.TextEditing.Markdown.Writer
{
    /// <summary>Writes paragraphs as canonical FieldWorks Markdown (spec 3, 6.1). The spelling comes from the rules of the spec; every paragraph is then
    /// read back by the parser and compared with the model, and when the canonical spelling does not read back (which the rules are meant to make
    /// impossible) the writer falls back to the safe spelling, and last to numeric references, so what it writes always reads back to the same paragraph.</summary>
    /// <summary>For the tests: how often the writer had to leave the canonical spelling (the rules of spec 3.4 and 3.2) because it did not read back. The canonical form must not depend on the
    /// parser, so this stays 0 for every fixture and every generated document; a count above 0 is a gap in a rule. Per thread, so a test counts only its own work.</summary>
    internal static class WriterFallbacks
    {
        [ThreadStatic] public static int Generic;
        [ThreadStatic] public static int Safe;
        [ThreadStatic] public static int Numeric;
        [ThreadStatic] public static int SafeTable;
    }

    /// <summary>For the tests: how much work the writer did, counted and not timed (issue 141). <see cref="Steps"/> counts the units the inline generator
    /// looked at, including each step of a scan past a unit; <see cref="Characters"/> counts the text the writer gave the reader to check what it wrote. Per
    /// thread, so a test counts only its own work. Both grow with the input when the writer is linear, and grow as its square when it is not.</summary>
    internal static class WriterWork
    {
        [ThreadStatic] public static long Steps;
        [ThreadStatic] public static long Characters;
    }

    internal sealed class MarkdownWriter
    {
        readonly MarkdownOptions _options;

        MarkdownWriter(MarkdownOptions options) { _options = options; }

        MarkdownStyleNames Names => _options.StyleNames;

        sealed class Entry
        {
            public Paragraph Paragraph = null!;
            public PropertyBag Props = null!;
            public bool IsEmpty;
            public bool HasLineSeparator;
        }

        public static string Write(IEnumerable<Paragraph> paragraphs, MarkdownOptions options)
            => new MarkdownWriter(options).WriteAll(paragraphs);

        /// <summary>Writes the runs of one inline-only text (a multi-string field such as the abstract, spec 5.1). There is no front matter: the field's
        /// writing system is its host's.</summary>
        public static string WriteInline(IEnumerable<Run> runs, MarkdownOptions options)
        {
            var writer = new MarkdownWriter(options);
            var paragraph = new Paragraph { Runs = runs.Select(r => r.Clone()).ToList() };
            paragraph.Normalize();
            var cells = new List<List<Run>> { paragraph.Runs.ToList() };
            var generator = new InlineGenerator(options, BlockContext.Plain());
            bool Reads(string candidate)
            {
                WriterWork.Characters += candidate.Length;
                var back = MarkdownReader.ReadInline(candidate, options, out var context);
                var findings = context.Diagnostics;
                return !findings.Any(f => f.Severity == MarkdownSeverity.Error && MarkdownReader.IsSyntaxFinding(f.Id)) && Equivalence.Equal(paragraph.Runs, back);
            }
            // The text is read with and without a final line end: a file has one, a string in a database may not.
            string? text = writer.Search(generator, cells, candidate => Reads(candidate) && Reads(candidate + "\n"));
            if (text == null) throw new MarkdownWriteException("The runs cannot be written so that they read back unchanged.");
            return text.StartsWith("%", StringComparison.Ordinal) ? "\\" + text : text;
        }

        string WriteAll(IEnumerable<Paragraph> paragraphs)
        {
            var entries = paragraphs.Select(Prepare).ToList();
            var blocks = new List<string>();
            int i = 0;
            while (i < entries.Count)
            {
                var entry = entries[i];
                if (entry.Paragraph.Style == Names.TableHeader && entry.Props.Count == 0 && !entry.IsEmpty)
                {
                    string? table = TryTable(entries, i, out int used);
                    if (table != null) { blocks.Add(table); i += used; continue; }
                }
                var kind = KindOf(entry);
                if (kind == Kind.Empty) { blocks.Add(EmptyLine(entry)); i++; continue; }
                if (kind == Kind.Bullet || kind == Kind.Number)
                {
                    var lines = new List<string>();
                    int j = i;
                    while (j < entries.Count && KindOf(entries[j]) == kind)
                    {
                        var context = kind == Kind.Bullet ? BlockContext.Bullet() : BlockContext.Number(j - i + 1);
                        lines.Add(context.FirstPrefix + PlainInline(entries[j], context));
                        j++;
                    }
                    blocks.Add(string.Join("\n", lines));
                    i = j;
                    continue;
                }
                if (kind != Kind.None)
                {
                    var context = kind == Kind.Quote ? BlockContext.Quote() : kind == Kind.Heading ? BlockContext.Heading(HeadingLevel(entry)) : BlockContext.Plain();
                    blocks.Add(context.FirstPrefix + PlainInline(entry, context));
                    i++;
                    continue;
                }
                int end = i + 1;
                while (end < entries.Count && SameDiv(entry, entries[end])) end++;
                blocks.Add(Div(entries, i, end));
                i = end;
            }
            string body = blocks.Count == 0 ? string.Empty : string.Join("\n\n", blocks) + "\n";
            // Pandoc reads a first line that starts with % as a title block.
            if (!_options.WriteFrontMatter) return body.StartsWith("%", StringComparison.Ordinal) ? "\\" + body : body;
            string front = FrontMatter(entries);
            return body.Length == 0 ? front : front + "\n" + body;
        }

        /// <summary>The front matter of spec 2.1: the version, the two defaults, and every writing system the text uses (the defaults first, then in the order of first use).</summary>
        string FrontMatter(List<Entry> entries)
        {
            var used = new List<string>();
            foreach (var entry in entries)
            {
                if (entry.IsEmpty) { if (entry.Paragraph.DefaultWs != null && !used.Contains(entry.Paragraph.DefaultWs)) used.Add(entry.Paragraph.DefaultWs); }
                else InlineGenerator.CollectWritingSystems(entry.Paragraph.Runs, used);
            }
            var mapper = _options.WritingSystems;
            var details = mapper as IWritingSystemDetails;
            return FrontMatterFormat.Write(mapper.DefaultAnalysis, mapper.DefaultVernacular, used, id => details?.DescribeWritingSystem(id) ?? WritingSystemInfo.Default(id));
        }

        Entry Prepare(Paragraph paragraph)
        {
            var copy = Equivalence.Normalized(paragraph);
            if (copy.Style == null) copy.Style = string.Empty;
            var props = Equivalence.EffectiveProps(copy);
            return new Entry
            {
                Paragraph = copy,
                Props = props,
                IsEmpty = copy.Runs.Count == 0,
                HasLineSeparator = copy.Text.IndexOf(Chars.LineSeparator) >= 0,
            };
        }

        // ---------------------------------------------------------------- what a paragraph is written as

        enum Kind { None, Plain, Empty, Heading, Bullet, Number, Quote }

        int HeadingLevel(Entry e) => e.Paragraph.Style == Names.Heading1 ? 1 : e.Paragraph.Style == Names.Heading2 ? 2 : 3;

        Kind KindOf(Entry e)
        {
            if (e.Props.Count > 0) return Kind.None;
            string style = e.Paragraph.Style;
            if (e.IsEmpty) return style == Names.Normal ? Kind.Empty : Kind.None;
            if (style == Names.Normal) return Kind.Plain;
            if (style == Names.Heading1 || style == Names.Heading2 || style == Names.Heading3) return e.HasLineSeparator ? Kind.None : Kind.Heading;
            if (style == Names.BulletedList) return Kind.Bullet;
            if (style == Names.NumberedList) return Kind.Number;
            if (style == Names.BlockQuote) return Kind.Quote;
            return Kind.None;
        }

        /// <summary>One div holds a maximal run of adjacent paragraphs with the same style and properties, empty ones included (spec 3.1).</summary>
        bool SameDiv(Entry first, Entry other)
            => other.Paragraph.Style == first.Paragraph.Style && other.Props.ContentEquals(first.Props) && KindOf(other) == Kind.None
               && !(other.Paragraph.Style == Names.TableHeader && other.Props.Count == 0 && !other.IsEmpty);

        static List<List<Run>> OneCell(Entry e) => new List<List<Run>> { e.Paragraph.Runs.ToList() };

        string PlainInline(Entry e, BlockContext context)
        {
            var generator = new InlineGenerator(_options, context);
            string? text = Search(generator, OneCell(e), candidate => ReadsBack(context.Wrap(context.FirstPrefix + candidate), e.Paragraph));
            return text ?? throw new MarkdownWriteException("A paragraph cannot be written so that it reads back unchanged: " + Describe(e));
        }

        static string Describe(Entry e) => "style '" + e.Paragraph.Style + "', " + e.Paragraph.Runs.Count + " runs, " + e.Paragraph.Length + " characters";

        bool ReadsBack(string blockText, Paragraph expected, int paragraphs = 1)
        {
            var read = ReadBack(blockText + "\n", _options, out bool clean); // a block is always followed by a line end
            return clean && read.Count == paragraphs && Equivalence.Equal(expected, read[0]);
        }

        /// <summary>Every check of what the writer wrote is read here, so <see cref="WriterWork.Characters"/> is all the text the reader was given.</summary>
        List<Paragraph> ReadBack(string markdown, MarkdownOptions options, out bool clean)
        {
            WriterWork.Characters += markdown.Length;
            return MarkdownReader.ReadParagraphs(markdown, options, out clean);
        }

        // ---------------------------------------------------------------- the ladder

        /// <summary>Finds the first spelling of the inline text that <paramref name="accepts"/>: the canonical one, then the safe one (no stars, every
        /// punctuation character escaped), then numeric references, which always read back.</summary>
        string? Search(InlineGenerator generator, IReadOnlyList<List<Run>> cells, Func<string, bool> accepts)
        {
            string canonical = generator.Generate(cells, new GenMode());
            if (accepts(canonical)) return canonical;
            string safe = generator.Generate(cells, new GenMode { Safe = true });
            // Object data the object spellings cannot carry: those runs are written as the generic span, which holds any data.
            WriterFallbacks.Generic++;
            string generic = generator.Generate(cells, new GenMode { GenericObjects = true });
            if (generic != canonical && accepts(generic)) return generic;
            WriterFallbacks.Safe++;
            if (safe != canonical && accepts(safe)) return safe;
            string safeGeneric = generator.Generate(cells, new GenMode { Safe = true, GenericObjects = true });
            if (accepts(safeGeneric)) return safeGeneric;
            WriterFallbacks.Numeric++;
            string numeric = generator.Generate(cells, new GenMode { Numeric = true, GenericObjects = true });
            return accepts(numeric) ? numeric : null;
        }

        // ---------------------------------------------------------------- empty paragraphs and divs

        /// <summary>The marker of an empty paragraph: <c>[]{.empty}</c>, with its writing system when it is not the analysis default: <c>[]{.v .empty}</c> or
        /// <c>[]{.empty ws=ceb-x-old}</c> (an empty paragraph has no run to carry one).</summary>
        string EmptyMarker(Entry e)
        {
            var items = new List<PropertyKeys.Item>();
            var ws = _options.WritingSystems;
            string? paragraphWs = e.Paragraph.DefaultWs;
            bool vernacular = paragraphWs != null && paragraphWs != ws.DefaultAnalysis && paragraphWs == ws.DefaultVernacular;
            if (vernacular) items.Add(new PropertyKeys.Item(".v", null));
            items.Add(new PropertyKeys.Item(".empty", null));
            if (paragraphWs != null && paragraphWs != ws.DefaultAnalysis && !vernacular) items.Add(new PropertyKeys.Item("ws", paragraphWs));
            return "[]" + Braces(items);
        }

        string EmptyLine(Entry e)
        {
            string line = EmptyMarker(e);
            if (!ReadsBack(line, e.Paragraph)) throw new MarkdownWriteException("An empty paragraph cannot be written so that it reads back unchanged: " + Describe(e));
            return line;
        }

        static string Braces(List<PropertyKeys.Item> items, bool inTable = false)
        {
            var sb = new StringBuilder("{");
            for (int i = 0; i < items.Count; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(items[i].Key);
                if (items[i].Value != null) sb.Append('=').Append(AttributeText.Value(items[i].Value!, inTable));
            }
            return sb.Append('}').ToString();
        }

        string Div(List<Entry> entries, int start, int end)
        {
            var first = entries[start];
            var items = new List<PropertyKeys.Item>();
            if (first.Paragraph.Style != Names.Normal || first.Props.Count == 0) items.Add(new PropertyKeys.Item("custom-style", first.Paragraph.Style));
            items.AddRange(PropertyKeys.ToItems(first.Props));
            string header = Braces(items);
            var context = BlockContext.Div(header);
            var sb = new StringBuilder("::: ").Append(header);
            for (int i = start; i < end; i++)
            {
                var entry = entries[i];
                string text;
                if (entry.IsEmpty)
                {
                    text = EmptyMarker(entry);
                    if (!ReadsBack(context.Wrap(text), entry.Paragraph, 2)) throw new MarkdownWriteException("An empty paragraph in a styled block cannot be written so that it reads back unchanged: " + Describe(entry));
                }
                else
                {
                    var generator = new InlineGenerator(_options, context);
                    string? found = Search(generator, OneCell(entry), candidate => ReadsBack(context.Wrap(candidate), entry.Paragraph, 2));
                    if (found == null) throw new MarkdownWriteException("A paragraph in a styled block cannot be written so that it reads back unchanged: " + Describe(entry));
                    text = found;
                }
                sb.Append("\n\n").Append(text);
            }
            // The first paragraph follows the opening line directly (spec 3.1: paragraphs are separated by blank lines, the fences are not paragraphs).
            sb.Remove(("::: " + header).Length, 1);
            return sb.Append("\n:::").ToString();
        }

        // ---------------------------------------------------------------- tables

        static string Delimiter(int width) => "|" + string.Concat(Enumerable.Repeat("---|", width));

        static string PlaceholderHeader(int width) => "|" + string.Concat(Enumerable.Repeat(" x |", width));

        static string SyntheticRow(int width) => "|" + string.Concat(Enumerable.Repeat(" [x]{.v} |", width));

        /// <summary>A paragraph can be a table row when its tabs can be cell separators: the separator is plain text in the default analysis writing system, so a tab in an object run, in another
        /// writing system or with any property would come back as something else.</summary>
        bool CellEligible(Entry e)
        {
            if (e.HasLineSeparator) return false;
            string analysis = _options.WritingSystems.DefaultAnalysis;
            foreach (var run in e.Paragraph.Runs)
                if (run.Text.IndexOf(Chars.Tab) >= 0 && (run.ObjData != null || run.Ws != analysis || run.Props.Count > 0)) return false;
            return true;
        }

        static List<List<Run>> SplitCells(Paragraph paragraph)
        {
            var cells = new List<List<Run>> { new List<Run>() };
            foreach (var run in paragraph.Runs)
            {
                if (run.ObjData != null) { cells[cells.Count - 1].Add(run); continue; }
                string[] pieces = run.Text.Split(Chars.Tab);
                for (int i = 0; i < pieces.Length; i++)
                {
                    if (i > 0) cells.Add(new List<Run>());
                    if (pieces[i].Length > 0) cells[cells.Count - 1].Add(run.CloneWithText(pieces[i]));
                }
            }
            return cells;
        }

        /// <summary>Writes a header and the rows after it that fit. A bracket one cell leaves open can pair with a closing bracket in another row, which no single row
        /// shows, so every row is checked with a row of closing brackets after it. The whole table is read once more at the end; if rows still disturb one another
        /// there, every cell is written in the safe spelling, and only when that does not read back either is the table left to the divs.</summary>
        string? TryTable(List<Entry> entries, int start, out int used)
        {
            used = 0;
            var header = entries[start];
            if (!CellEligible(header)) return null;
            var headerCells = SplitCells(header.Paragraph);
            // Pandoc reads a header row whose cells are all empty as no header at all, so such a header cannot be a pipe table.
            if (headerCells.All(cell => cell.Count == 0)) return null;
            int width = headerCells.Count;
            var expected = new List<Paragraph> { header.Paragraph };
            var cellsOf = new List<List<List<Run>>> { headerCells };
            var acceptsOf = new List<Func<string, bool>>();
            var lines = new List<string>();

            Func<string, bool> headerAccepts = candidate =>
            {
                var read = ReadBack(candidate + "\n" + Delimiter(width) + "\n" + SyntheticRow(width) + "\n", _options, out bool clean);
                return clean && read.Count == 2 && Equivalence.Equal(header.Paragraph, read[0]);
            };
            string? headerLine = Search(new InlineGenerator(_options, BlockContext.TableRow()), headerCells, headerAccepts);
            if (headerLine == null) return null;
            acceptsOf.Add(headerAccepts);
            lines.Add(headerLine);
            int j = start + 1;
            while (j < entries.Count)
            {
                var row = entries[j];
                // A row with no runs is one empty cell, a row of a one-column table; in a wider table its cell count ends the table.
                if (row.Props.Count > 0 || row.Paragraph.Style != Names.TableRow || !CellEligible(row)) break;
                // An empty row has no run to carry a writing system, and a table row has no marker: a row that types in another writing system than the default is left to a div.
                if (row.IsEmpty && row.Paragraph.DefaultWs != _options.WritingSystems.DefaultAnalysis) break;
                var cells = SplitCells(row.Paragraph);
                if (cells.Count != width) break;
                Func<string, bool> rowAccepts = candidate =>
                {
                    var read = ReadBack(PlaceholderHeader(width) + "\n" + Delimiter(width) + "\n" + candidate + "\n" + SyntheticRow(width) + "\n", _options, out bool clean);
                    return clean && read.Count == 3 && Equivalence.Equal(row.Paragraph, read[1]);
                };
                string? line = Search(new InlineGenerator(_options, BlockContext.TableRow()), cells, rowAccepts);
                if (line == null) break;
                expected.Add(row.Paragraph);
                cellsOf.Add(cells);
                acceptsOf.Add(rowAccepts);
                lines.Add(line);
                j++;
            }
            if (!TableReadsBack(lines, width, expected))
            {
                WriterFallbacks.SafeTable++;
                for (int i = 0; i < lines.Count; i++)
                    lines[i] = new InlineGenerator(_options, BlockContext.TableRow()).Generate(cellsOf[i], new GenMode { Safe = true });
                if (!TableReadsBack(lines, width, expected)) return null;
            }
            used = j - start;
            return TableText(lines, width);
        }

        static string TableText(List<string> lines, int width)
            => lines[0] + "\n" + Delimiter(width) + (lines.Count > 1 ? "\n" + string.Join("\n", lines.Skip(1)) : string.Empty);

        /// <summary>True when the table made of these lines (header first, then the rows) reads back as exactly the paragraphs expected.</summary>
        bool TableReadsBack(List<string> lines, int width, List<Paragraph> expected)
        {
            var all = ReadBack(TableText(lines, width) + "\n", _options, out bool clean);
            if (!clean || all.Count != expected.Count) return false;
            for (int i = 0; i < expected.Count; i++) if (!Equivalence.Equal(expected[i], all[i])) return false;
            return true;
        }
    }
}
