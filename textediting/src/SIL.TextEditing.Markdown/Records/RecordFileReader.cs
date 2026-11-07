using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using SIL.TextEditing.Markdown.Reader;
using YamlDotNet.Core;

namespace SIL.TextEditing.Markdown.Records
{
    /// <summary>Reads a record file (ADR 0032): the YAML front matter with YamlDotNet's parser, every value typed by the record schema by its path (never by
    /// YAML's own resolution, so <c>en: no</c> in a text field is "no"), rich values and the body with the FieldWorks Markdown reader, and notation values
    /// handed to the host's checker. The reader is lenient (decision 9): what it can read it reads, with information findings for what is not canonical,
    /// and errors with an exact fix where intent is clear.</summary>
    public static class RecordFileReader
    {
        public static RecordReadResult Read(string text, RecordSchema schema, RecordReadOptions? options = null)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            if (schema == null) throw new ArgumentNullException(nameof(schema));
            return new RecordReading(text, schema, options ?? new RecordReadOptions()).Run();
        }
    }

    internal sealed class RecordReading
    {
        static readonly string[] FrontMatterKeys = { "fwmd", "record", "schema", "root", "base", "analysis", "vernacular", "writingSystems", "record-data" };

        readonly string _source;      // as given
        readonly string _text;        // a byte order mark is a space, so every offset is the source's
        readonly RecordSchema _schema;
        readonly RecordReadOptions _options;
        readonly ReaderContext _context;
        readonly List<int> _lineStarts;
        readonly Dictionary<Guid, int> _guids = new Dictionary<Guid, int>();
        string _analysis, _vernacular;
        List<YamlFrameNode> _blocks = new List<YamlFrameNode>();

        public RecordReading(string source, RecordSchema schema, RecordReadOptions options)
        {
            _source = source;
            _text = source.Length > 0 && source[0] == Chars.ByteOrderMark ? " " + source.Substring(1) : source;
            _schema = schema;
            _options = options;
            _context = new ReaderContext(options.Markdown, _text);
            _lineStarts = RecordProfile.LineStarts(_text);
            _analysis = options.Markdown.WritingSystems.DefaultAnalysis;
            _vernacular = options.Markdown.WritingSystems.DefaultVernacular;
        }

        RecordReadResult Result(RecordDocument? document)
        {
            var diagnostics = _context.Diagnostics.Select((d, i) => (d, i)).OrderBy(x => x.d.Start).ThenBy(x => x.i).Select(x => x.d).ToList();
            return new RecordReadResult(_text, document, diagnostics);
        }

        MarkdownDiagnostic Report(string id, MarkdownSeverity severity, string message, int start, int end, string? replacement = null,
            IReadOnlyList<string>? choices = null, IReadOnlyList<string>? nearest = null)
            => _context.Report(id, severity, message, start, end, replacement, choices, nearest);

        void Error(string id, string message, int start, int end, string? replacement = null) => Report(id, MarkdownSeverity.Error, message, start, end, replacement);
        void Info(string id, string message, int start, int end, string? replacement = null) => Report(id, MarkdownSeverity.Suggestion, message, start, end, replacement);

        // ---------------------------------------------------------------- the file

        public RecordReadResult Run()
        {
            int start = _text.Length > 0 && _source[0] == Chars.ByteOrderMark ? 1 : 0;
            bool found = FrontMatterFormat.TryFind(_text, start, out int end, out bool closed);
            if (!found || !closed)
            {
                Error(RecordDiagnosticIds.NotARecord, found ? "The front matter has no closing --- line." : "A record file starts with YAML front matter between two --- lines.", start, LineEnd(start));
                return Result(null);
            }
            int firstLineEnd = NextLine(start);
            int closeStart = LineStartOf(end - 1);
            // YamlDotNet reads the file cut at the closing fence, with the opening fence blanked, so its offsets are the file's.
            var yaml = new StringBuilder(_text.Substring(0, closeStart));
            for (int i = 0; i < firstLineEnd && i < yaml.Length; i++) if (yaml[i] != '\n' && yaml[i] != '\r') yaml[i] = ' ';
            string yamlText = yaml.ToString();

            var separators = new List<int>();
            for (int i = firstLineEnd; i < closeStart; i++) if (yamlText[i] == '\u2028' || yamlText[i] == '\u2029' || yamlText[i] == '\u0085') separators.Add(i);
            var tabLines = TabLines(yamlText, firstLineEnd, closeStart);

            string original = yamlText;
            var parse = YamlTree.Parse(yamlText);
            var reported = new List<int>();
            if (parse.Error != null && tabLines.Count > 0)
            {
                // A tab in the indentation stops the parser. Read on as if each were a space, so the rest of the file is still checked.
                var spaced = new StringBuilder(yamlText);
                foreach (int line in tabLines) for (int i = line; i < spaced.Length && (spaced[i] == ' ' || spaced[i] == '\t'); i++) spaced[i] = ' ';
                parse = YamlTree.Parse(spaced.ToString());
                yamlText = spaced.ToString();
                CollectBlocks(parse.Root);
                reported.AddRange(tabLines);
            }
            else
            {
                CollectBlocks(parse.Root);
                foreach (int line in tabLines) if (!InBlockContent(line)) reported.Add(line);
            }
            // The fix gives two spaces for each tab, but only where that reads as the reader read the file: where it would move a key to another parent, or
            // where the reader could not read the file at all, it gives none.
            bool fixTabs = reported.Count > 0 && parse.Error == null && TabFixKeepsStructure(original, reported, parse.Root);
            foreach (int line in reported) ReportTab(line, fixTabs);
            foreach (int at in separators)
            {
                var holder = BlockAt(at);
                bool inRich = holder != null && holder.Start < at && at < holder.End;
                Error(RecordDiagnosticIds.LineSeparator, "U+" + ((int)_text[at]).ToString("X4", CultureInfo.InvariantCulture) + " is a line break to some YAML readers and not to others; write it as a character reference.",
                    at, at + 1, inRich ? "&#x" + ((int)_text[at]).ToString("X4", CultureInfo.InvariantCulture) + ";" : null);
            }
            if (parse.Error == null) foreach (var comment in YamlTree.Comments(yamlText)) ReportComment(comment[0], comment[1]);
            foreach (int at in parse.LaterDocuments)
                // The reader reads the first document only, so the fix that makes the file say what was read removes the rest of the front matter.
                Error(RecordDiagnosticIds.SecondDocument, "A second YAML document starts here; a record file's front matter is one document, and the rest is not read.", LineStartOf(at), closeStart, string.Empty);
            if (parse.TooDeepAt != null) Error(RecordDiagnosticIds.TooDeep, "The YAML nests deeper than " + YamlTree.MaxDepth + " levels; what is deeper is not read.", parse.TooDeepAt.Value, LineEnd(parse.TooDeepAt.Value));
            if (parse.Error != null)
            {
                int at = Math.Max(firstLineEnd, Math.Min(parse.ErrorAt, closeStart));
                Error(RecordDiagnosticIds.YamlSyntax, "The front matter is not YAML: " + parse.Error, at, Math.Max(at, LineEnd(at)));
                return Result(null);
            }
            if (parse.Root == null || parse.Root.Kind != YamlFrameKind.Mapping)
            {
                Error(RecordDiagnosticIds.NotARecord, "A record file's front matter is a mapping of keys to values.", start, LineEnd(start));
                return Result(null);
            }
            CheckYaml(parse.Root, false);
            var document = ReadFrontMatter(parse.Root, start, out var root, out var data, out var kind);
            if (document == null || root == null || kind == null)
            {
                CheckBody(end, null, null, null);
                return Result(null);
            }
            if (data != null) document.Data = ReadObject(data, root.Type, string.Empty, root.Field, root.IsRecord ? kind.BodyField : null);
            else document.Data = new RecordObject();
            CheckBody(end, root.Body, document, root);
            return Result(document);
        }

        // ---------------------------------------------------------------- lines and places

        int LineIndex(int offset)
        {
            int lo = 0, hi = _lineStarts.Count - 1;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (_lineStarts[mid] <= offset) lo = mid; else hi = mid - 1;
            }
            return lo;
        }

        int LineStartOf(int offset) => _lineStarts[LineIndex(Math.Max(0, offset))];
        int Column(int offset) => offset - LineStartOf(offset);

        int LineEnd(int offset)
        {
            int i = Math.Max(0, Math.Min(offset, _text.Length));
            while (i < _text.Length && _text[i] != '\n' && _text[i] != '\r') i++;
            return i;
        }

        int NextLine(int offset)
        {
            int i = LineEnd(offset);
            if (i < _text.Length) i += _text[i] == '\r' && i + 1 < _text.Length && _text[i + 1] == '\n' ? 2 : 1;
            return i;
        }

        /// <summary>A fix that removes the line [start, end) is on, when nothing else is on it: the range of the whole line with its line end.</summary>
        bool WholeLine(int start, int end, out int lineStart, out int next)
        {
            lineStart = LineStartOf(start);
            next = NextLine(end);
            for (int i = lineStart; i < start; i++) if (_text[i] != ' ') return false;
            for (int i = end; i < LineEnd(end); i++) if (_text[i] != ' ') return false;
            return true;
        }

        // ---------------------------------------------------------------- YAML the profile does not have

        /// <summary>The starts of the lines in [from, to) whose indentation holds a tab.</summary>
        List<int> TabLines(string text, int from, int to)
        {
            var lines = new List<int>();
            int i = from;
            while (i < to)
            {
                int lineStart = i;
                bool tab = false;
                while (i < to && (text[i] == ' ' || text[i] == '\t')) { if (text[i] == '\t') tab = true; i++; }
                if (tab) lines.Add(lineStart);
                while (i < to && text[i] != '\n' && text[i] != '\r') i++;
                if (i < to) i += text[i] == '\r' && i + 1 < to && text[i + 1] == '\n' ? 2 : 1;
            }
            return lines;
        }

        void ReportTab(int lineStart, bool withFix)
        {
            int i = lineStart;
            var fix = new StringBuilder();
            while (i < _text.Length && (_text[i] == ' ' || _text[i] == '\t')) { fix.Append(_text[i] == '\t' ? "  " : " "); i++; }
            Error(RecordDiagnosticIds.Tab, "YAML is indented with spaces, not tabs.", lineStart, i, withFix ? fix.ToString() : null);
        }

        /// <summary>True when the text with two spaces for each tab of the reported lines parses to the same tree the reader read.</summary>
        static bool TabFixKeepsStructure(string text, List<int> lines, YamlFrameNode? read)
        {
            var sb = new StringBuilder(text.Length + 16);
            int from = 0;
            foreach (int line in lines)
            {
                sb.Append(text, from, line - from);
                int i = line;
                while (i < text.Length && (text[i] == ' ' || text[i] == '\t')) { sb.Append(text[i] == '\t' ? "  " : " "); i++; }
                from = i;
            }
            sb.Append(text, from, text.Length - from);
            var fixedParse = YamlTree.Parse(sb.ToString());
            return fixedParse.Error == null && Shape(fixedParse.Root) == Shape(read);
        }

        /// <summary>A tree's structure and values as text, without places.</summary>
        static string Shape(YamlFrameNode? node)
        {
            var sb = new StringBuilder();
            void Walk(YamlFrameNode? n)
            {
                if (n == null) { sb.Append('~'); return; }
                switch (n.Kind)
                {
                    case YamlFrameKind.Mapping:
                        sb.Append('{');
                        foreach (var p in n.Pairs) { Walk(p.Key); sb.Append(':'); Walk(p.Value); sb.Append(','); }
                        sb.Append('}');
                        break;
                    case YamlFrameKind.Sequence:
                        sb.Append('[');
                        foreach (var item in n.Items) { Walk(item); sb.Append(','); }
                        sb.Append(']');
                        break;
                    default:
                        sb.Append(n.Kind == YamlFrameKind.Alias ? "*" : "=").Append(n.Value.Length).Append(':').Append(n.Value);
                        break;
                }
            }
            Walk(node);
            return sb.ToString();
        }

        void CollectBlocks(YamlFrameNode? node)
        {
            _blocks = new List<YamlFrameNode>();
            void Walk(YamlFrameNode? n)
            {
                if (n == null) return;
                if (n.Kind == YamlFrameKind.Scalar && (n.Style == ScalarStyle.Literal || n.Style == ScalarStyle.Folded)) _blocks.Add(n);
                foreach (var p in n.Pairs) { Walk(p.Key); Walk(p.Value); }
                foreach (var item in n.Items) Walk(item);
            }
            Walk(node);
        }

        /// <summary>True when a line with a tab in its leading space is a block's content line and the tab comes after the block's indentation.</summary>
        bool InBlockContent(int lineStart)
        {
            var block = BlockAt(lineStart);
            if (block == null || lineStart < NextLine(block.Start) || lineStart >= block.End) return false;
            int indent = BlockIndent(block);
            int i = lineStart;
            while (i < _text.Length && _text[i] == ' ') i++;
            return i - lineStart >= indent;
        }

        /// <summary>The last block that starts before <paramref name="offset"/>, or null: a binary search, since the blocks are in document order and do not
        /// overlap (review of PR 147, P2-2).</summary>
        YamlFrameNode? BlockAt(int offset)
        {
            int lo = 0, hi = _blocks.Count - 1, found = -1;
            while (lo <= hi)
            {
                RecordWork.Steps++;
                int mid = (lo + hi) / 2;
                if (_blocks[mid].Start < offset) { found = mid; lo = mid + 1; }
                else hi = mid - 1;
            }
            return found < 0 ? null : _blocks[found];
        }

        /// <summary>The indentation of a block scalar's content: its first line that is not blank.</summary>
        int BlockIndent(YamlFrameNode block)
        {
            int i = NextLine(block.Start);
            while (i < block.End)
            {
                int j = i;
                while (j < _text.Length && _text[j] == ' ') j++;
                if (j < _text.Length && _text[j] != '\n' && _text[j] != '\r') return j - i;
                i = NextLine(i);
            }
            return 0;
        }

        /// <summary>A comment is information; the fix removes it, with its line when nothing else is on the line.</summary>
        void ReportComment(int start, int end)
        {
            const string message = "A comment is dropped: the project has nowhere to keep it.";
            int from = start;
            while (from > 0 && _text[from - 1] == ' ') from--;
            int lineStart = LineStartOf(start);
            if (from == lineStart) Info(RecordDiagnosticIds.CommentDropped, message, lineStart, NextLine(start), string.Empty);
            else Info(RecordDiagnosticIds.CommentDropped, message, from, end, string.Empty);
        }

        /// <summary>The generic checks: anchors, tags, aliases, merge keys, flow collections, duplicate keys and keys that are not scalars.</summary>
        void CheckYaml(YamlFrameNode node, bool inFlow)
        {
            if (node.Anchor != null)
            {
                Token(node.Start, '&', out int s, out int e);
                Error(RecordDiagnosticIds.Anchor, "Anchors are not part of a record file; write the value out.", s, e, s < e ? string.Empty : null);
            }
            if (node.Tag != null)
            {
                Token(node.Start, '!', out int s, out int e);
                Error(RecordDiagnosticIds.Tag, "Tags are not part of a record file; the schema says what each value is.", s, e, s < e ? string.Empty : null);
            }
            if (node.Kind == YamlFrameKind.Alias)
            {
                string text = "*" + node.Value;
                Error(RecordDiagnosticIds.Alias, "'" + text + "' is a YAML alias. As text it needs quotes.", node.Start, node.End, RecordProfile.CanQuote(text) ? RecordProfile.Quote(text) : null);
            }
            if ((node.Kind == YamlFrameKind.Mapping || node.Kind == YamlFrameKind.Sequence) && node.Flow && !inFlow)
                Info(RecordDiagnosticIds.Flow, "A flow collection is read; the canonical form is block style, one value per line.", node.Start, Math.Max(node.End, node.Start + 1));
            if (node.Kind == YamlFrameKind.Mapping)
            {
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var pair in node.Pairs)
                {
                    var key = pair.Key;
                    if (key.Kind != YamlFrameKind.Scalar)
                    {
                        if (key.Kind != YamlFrameKind.Alias) Error(RecordDiagnosticIds.WrongShape, "A key is a single value.", key.Start, Math.Max(key.End, key.Start + 1));
                    }
                    else if (key.Value == "<<" && key.Style == ScalarStyle.Plain)
                        ReportPairRemoval(RecordDiagnosticIds.MergeKey, "Merge keys are not part of a record file, and this one is not read; write the merged keys out.", pair);
                    else if (!seen.Add(key.Value))
                        ReportPairRemoval(RecordDiagnosticIds.DuplicateKey, "'" + key.Value + "' is already a key of this mapping; the first is read, and this one is not.", pair);
                    CheckYaml(key, inFlow || node.Flow);
                    CheckYaml(pair.Value, inFlow || node.Flow);
                }
            }
            foreach (var item in node.Items) CheckYaml(item, inFlow || node.Flow);
        }

        /// <summary>An error on a key whose pair the reader does not read. The fix removes the pair's lines, so the file says what was read; when the pair
        /// shares a line with something else (a flow mapping, the first key of a list item) the finding is on the key and has no fix.</summary>
        void ReportPairRemoval(string id, string message, KeyValuePair<YamlFrameNode, YamlFrameNode> pair)
        {
            int end = Math.Max(pair.Key.End, TrimLineEnds(pair.Value.End));
            int lineStart = LineStartOf(pair.Key.Start);
            bool alone = true;
            for (int i = lineStart; i < pair.Key.Start; i++) if (_text[i] != ' ') alone = false;
            for (int i = end; i < LineEnd(end); i++) if (_text[i] != ' ') alone = false;
            if (alone) Error(id, message, lineStart, NextLine(end), string.Empty);
            else Error(id, message, pair.Key.Start, pair.Key.End);
        }

        /// <summary>The range of the anchor (<c>&amp;</c>) or tag (<c>!</c>) token among a node's properties, with the spaces after it, or an empty range
        /// when it is not where the node starts.</summary>
        void Token(int at, char kind, out int start, out int end)
        {
            int i = at;
            for (int round = 0; round < 2 && i < _text.Length; round++)
            {
                if (_text[i] != '&' && _text[i] != '!') break;
                int s = i;
                while (i < _text.Length && _text[i] != ' ' && _text[i] != '\n' && _text[i] != '\r') i++;
                int tokenEnd = i;
                while (i < _text.Length && _text[i] == ' ') i++;
                if (_text[s] == kind)
                {
                    start = s;
                    end = i < _text.Length && (_text[i] == '\n' || _text[i] == '\r') ? tokenEnd : i;
                    if (end == tokenEnd) while (start > 0 && _text[start - 1] == ' ') start--;
                    return;
                }
            }
            start = end = at;
        }

        // ---------------------------------------------------------------- the front matter

        RecordDocument? ReadFrontMatter(YamlFrameNode map, int fileStart, out RecordRoot? root, out YamlFrameNode? data, out RecordKind? kind)
        {
            root = null;
            data = null;
            kind = null;
            var keys = new Dictionary<string, KeyValuePair<YamlFrameNode, YamlFrameNode>>(StringComparer.Ordinal);
            foreach (var pair in map.Pairs) if (pair.Key.IsScalar && !keys.ContainsKey(pair.Key.Value)) keys[pair.Key.Value] = pair;

            if (!keys.TryGetValue("fwmd", out var fwmd) || !keys.TryGetValue("record", out var record))
            {
                Error(RecordDiagnosticIds.NotARecord, "A record file's front matter has the keys fwmd and record.", fileStart, LineEnd(fileStart));
                return null;
            }
            var version = TopInteger(fwmd);
            if (version == null) return null;
            if (version.Value != MarkdownFrontMatter.CurrentVersion)
            {
                Report(MarkdownDiagnosticIds.VersionUnsupported, MarkdownSeverity.Error,
                    "fwmd: " + version.Value + " is not a version this reader knows (it reads version " + MarkdownFrontMatter.CurrentVersion + "). Update the tool.", fwmd.Key.Start, fwmd.Value.End);
                return null;
            }
            string? kindName = TopString(record);
            if (kindName == null) return null;
            kind = _schema.Kind(kindName);
            if (kind == null)
            {
                Report(RecordDiagnosticIds.UnknownKind, MarkdownSeverity.Error, "The schema has no record kind '" + kindName + "'.", record.Value.Start, record.Value.End,
                    null, _schema.Kinds.Select(k => k.Name).ToList(), ReaderContext.NearestNames(kindName, _schema.Kinds.Select(k => k.Name)));
                return null;
            }
            var document = new RecordDocument(kindName);

            int order = -1;
            bool reportedOrder = false;
            foreach (var pair in map.Pairs)
            {
                if (!pair.Key.IsScalar) continue;
                if (!ReferenceEquals(keys[pair.Key.Value].Key, pair.Key)) continue; // a duplicate, already reported
                string key = pair.Key.Value;
                int index = Array.IndexOf(FrontMatterKeys, key);
                if (index < 0)
                {
                    Report(RecordDiagnosticIds.UnknownKey, MarkdownSeverity.Error, "'" + key + "' is not a key of a record file's front matter.", pair.Key.Start, pair.Key.End,
                        null, null, ReaderContext.NearestNames(key, FrontMatterKeys));
                    continue;
                }
                if (index < order && !reportedOrder)
                {
                    Info(RecordDiagnosticIds.KeyOrder, "The front matter's keys are written in the order " + string.Join(", ", FrontMatterKeys) + ".", pair.Key.Start, pair.Key.End);
                    reportedOrder = true;
                }
                order = Math.Max(order, index);
                CheckKeySpelling(pair.Key, key);
                switch (key)
                {
                    case "schema":
                        document.Schema = TopString(pair);
                        if (document.Schema != null && document.Schema != _schema.Id)
                            Error(RecordDiagnosticIds.SchemaMismatch, "The file names the schema '" + document.Schema + "'; this reader has '" + _schema.Id + "'.", pair.Value.Start, pair.Value.End, RecordProfile.Quote(_schema.Id));
                        break;
                    case "root":
                        document.Root = TopString(pair);
                        break;
                    case "base":
                        document.Base = TopString(pair);
                        break;
                    case "analysis":
                        document.Analysis = TopString(pair);
                        if (document.Analysis != null) { _analysis = document.Analysis; _context.CheckWritingSystem(document.Analysis, pair.Key.Start, LineEnd(pair.Key.Start)); }
                        break;
                    case "vernacular":
                        document.Vernacular = TopString(pair);
                        if (document.Vernacular != null) { _vernacular = document.Vernacular; _context.CheckWritingSystem(document.Vernacular, pair.Key.Start, LineEnd(pair.Key.Start)); }
                        break;
                    case "writingSystems":
                        ReadWritingSystems(pair.Value, document);
                        break;
                    case "record-data":
                        if (IsEmptyNode(pair.Value)) ReportEmpty(pair);
                        else if (pair.Value.Kind != YamlFrameKind.Mapping) Shape(pair.Value, "a mapping of the record's fields");
                        else data = pair.Value;
                        break;
                }
            }
            if (document.Root != null || keys.ContainsKey("root"))
            {
                root = RecordRoot.Resolve(kind, document.Root, out string? error);
                if (root == null)
                {
                    var r = keys["root"];
                    Error(RecordDiagnosticIds.BadRoot, error!, r.Value.Start, r.Value.End);
                    return null;
                }
            }
            else root = RecordRoot.Resolve(kind, null, out _);
            return document;
        }

        long? TopInteger(KeyValuePair<YamlFrameNode, YamlFrameNode> pair)
        {
            var node = pair.Value;
            if (node.Kind == YamlFrameKind.Scalar && long.TryParse(node.Value, NumberStyles.None, CultureInfo.InvariantCulture, out long v))
            {
                if (node.Style != ScalarStyle.Plain || node.Value != v.ToString(CultureInfo.InvariantCulture))
                    Info(RecordDiagnosticIds.ScalarStyle, "A whole number is written plain.", node.Start, node.End, v.ToString(CultureInfo.InvariantCulture));
                return v;
            }
            Error(RecordDiagnosticIds.NotAnInteger, pair.Key.Value + " takes a whole number, the format version.", node.Start, Math.Max(node.End, node.Start + 1));
            return null;
        }

        /// <summary>A front-matter key's string value, with the same rules as a text field.</summary>
        string? TopString(KeyValuePair<YamlFrameNode, YamlFrameNode> pair)
        {
            var node = pair.Value;
            if (node.Kind != YamlFrameKind.Scalar && node.Kind != YamlFrameKind.Alias) { Shape(node, "a single value"); return null; }
            if (node.Kind == YamlFrameKind.Scalar && !RecordProfile.CanQuote(node.Value))
            {
                // A front-matter value is always a quoted string; one with a control character or a line separator cannot be written (review of PR 147).
                Error(RecordDiagnosticIds.Unwritable, "The value of " + pair.Key.Value + " cannot be written in a record file: it holds a control character or a line separator.",
                    ValueStart(node), Math.Max(node.End, node.Start + 1));
                return node.Value;
            }
            var value = ReadLeaf(node, TopText, pair.Key.Value, Column(pair.Key.Start) + 2, null);
            return value?.Text;
        }

        static readonly RecordField TopText = new RecordField("front matter", RecordFieldKind.Text);

        void ReadWritingSystems(YamlFrameNode node, RecordDocument document)
        {
            if (node.Kind != YamlFrameKind.Mapping) { if (!IsEmptyNode(node)) Shape(node, "a mapping from writing-system id to lang, dir and font"); return; }
            var listed = new HashSet<string>(document.WritingSystems.Select(w => w.Key), StringComparer.Ordinal);
            foreach (var pair in node.Pairs)
            {
                RecordWork.Steps++;
                if (!pair.Key.IsScalar) continue;
                string id = pair.Key.Value;
                if (!CheckKeyWritable(pair.Key, "writing-system id")) continue;
                CheckKeySpelling(pair.Key, id);
                var props = new Dictionary<string, string>(StringComparer.Ordinal);
                if (pair.Value.Kind == YamlFrameKind.Mapping)
                {
                    foreach (var p in pair.Value.Pairs)
                    {
                        if (!p.Key.IsScalar) continue;
                        if (p.Key.Value != "lang" && p.Key.Value != "dir" && p.Key.Value != "font")
                        {
                            Report(RecordDiagnosticIds.UnknownKey, MarkdownSeverity.Error, "A writing system has lang, dir and font, not '" + p.Key.Value + "'.", p.Key.Start, p.Key.End,
                                null, null, ReaderContext.NearestNames(p.Key.Value, new[] { "lang", "dir", "font" }));
                            continue;
                        }
                        string? v = TopString(p);
                        if (v != null && !props.ContainsKey(p.Key.Value)) props[p.Key.Value] = v;
                    }
                }
                else if (!IsEmptyNode(pair.Value)) Shape(pair.Value, "a mapping with lang, dir and font");
                var fallback = WritingSystemInfo.Default(id);
                props.TryGetValue("lang", out var lang);
                props.TryGetValue("dir", out var dir);
                props.TryGetValue("font", out var font);
                if (listed.Add(id))
                    document.WritingSystems.Add(new KeyValuePair<string, WritingSystemInfo>(id, new WritingSystemInfo(lang ?? fallback.Language, dir ?? fallback.Direction, font ?? fallback.Font)));
            }
        }

        /// <summary>False, with an error, for a key the writer cannot quote (an escaped control character or line separator); such a key is not read.</summary>
        bool CheckKeyWritable(YamlFrameNode key, string what)
        {
            if (RecordProfile.CanQuote(key.Value)) return true;
            Error(RecordDiagnosticIds.Unwritable, "This " + what + " cannot be written in a record file: it holds a control character or a line separator.", key.Start, Math.Max(key.End, key.Start + 1));
            return false;
        }

        void CheckKeySpelling(YamlFrameNode key, string name)
        {
            string canonical = RecordProfile.Key(name);
            if (Spelling(key) != canonical) Info(RecordDiagnosticIds.ScalarStyle, "This key is written " + canonical + ".", key.Start, key.End, canonical);
        }

        string Spelling(YamlFrameNode node) => _text.Substring(node.Start, Math.Max(0, Math.Min(node.End, _text.Length) - node.Start));

        void Shape(YamlFrameNode node, string expected)
            => Error(RecordDiagnosticIds.WrongShape, "The schema expects " + expected + " here.", node.Start, Math.Max(node.End, node.Start + 1));

        static bool IsEmptyNode(YamlFrameNode node)
            => (node.Kind == YamlFrameKind.Scalar && (node.Value.Length == 0 || (node.Style == ScalarStyle.Plain && node.Value == "~")))
               || ((node.Kind == YamlFrameKind.Mapping || node.Kind == YamlFrameKind.Sequence) && node.Pairs.Count == 0 && node.Items.Count == 0);

        /// <summary>An empty value is information; the fix removes its line when the key has a line of its own.</summary>
        void ReportEmpty(KeyValuePair<YamlFrameNode, YamlFrameNode> pair)
        {
            const string message = "An empty value means the field is empty, which a record file says by leaving the key out.";
            int end = Math.Max(pair.Value.End, pair.Key.End);
            if (pair.Value.Kind != YamlFrameKind.Scalar) end = Math.Max(end, pair.Value.Start + 2);
            if (WholeLine(pair.Key.Start, end, out int lineStart, out int next)) Info(RecordDiagnosticIds.Empty, message, lineStart, next, string.Empty);
            else Info(RecordDiagnosticIds.Empty, message, pair.Key.Start, end);
        }

        // ---------------------------------------------------------------- record data

        RecordObject ReadObject(YamlFrameNode node, RecordObjectType type, string path, RecordField? only, RecordField? body)
        {
            var result = new RecordObject { Path = path, Range = new[] { node.Start, node.End } };
            int order = -2;
            bool reportedOrder = false;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var pair in node.Pairs)
            {
                if (!pair.Key.IsScalar) continue;
                string key = pair.Key.Value;
                if (!seen.Add(key)) continue; // a duplicate, already reported
                if (key == "<<" && pair.Key.Style == ScalarStyle.Plain) continue; // a merge key, already reported
                if (!node.Flow) CheckKeySpelling(pair.Key, key);
                int index;
                if (key == "guid" && type.HasGuid && only == null)
                {
                    index = -1;
                    if (!IsEmptyNode(pair.Value))
                    {
                        var g = ReadLeaf(pair.Value, GuidField, Join(path, "guid"), Column(pair.Key.Start) + 2, null);
                        var guid = g?.AsGuid();
                        if (guid != null)
                        {
                            result.Guid = guid;
                            if (_guids.ContainsKey(guid.Value))
                                Error(RecordDiagnosticIds.DuplicateGuid, "Another object of this file has the GUID " + guid.Value.ToString("D") + ".", pair.Value.Start, pair.Value.End);
                            else _guids[guid.Value] = pair.Value.Start;
                        }
                    }
                    else ReportEmpty(pair);
                }
                else
                {
                    var field = type.Field(key);
                    if (field == null)
                    {
                        var names = type.Fields.Select(f => f.Name).ToList();
                        if (type.HasGuid) names.Insert(0, "guid");
                        Report(RecordDiagnosticIds.UnknownField, MarkdownSeverity.Error, "'" + key + "' is not a field of " + type.Name + ".", pair.Key.Start, pair.Key.End,
                            null, null, ReaderContext.NearestNames(key, names));
                        continue;
                    }
                    if (body != null && field == body)
                    {
                        Error(RecordDiagnosticIds.BodyInFrame, "The value of " + key + " is this file's body, below the front matter, not a key of record-data.", pair.Key.Start, pair.Key.End);
                        continue;
                    }
                    if (only != null && field != only)
                    {
                        Error(RecordDiagnosticIds.OutsideRoot, "This file's root names the field " + only.Name + "; " + key + " is outside it.", pair.Key.Start, pair.Key.End);
                        continue;
                    }
                    index = IndexOf(type, field);
                    if (IsEmptyNode(pair.Value)) ReportEmpty(pair);
                    else
                    {
                        var value = ReadField(pair.Value, field, Join(path, key), Column(pair.Key.Start) + 2);
                        if (value != null) result[key] = value;
                    }
                }
                if (index < order && !reportedOrder)
                {
                    Info(RecordDiagnosticIds.KeyOrder, "The keys of " + type.Name + " are written " + (type.HasGuid ? "guid first, then " : string.Empty) + "in the schema's order: "
                        + string.Join(", ", type.Fields.Select(f => f.Name)) + ".", pair.Key.Start, pair.Key.End);
                    reportedOrder = true;
                }
                order = Math.Max(order, index);
            }
            return result;
        }

        static int IndexOf(RecordObjectType type, RecordField field)
        {
            for (int i = 0; i < type.Fields.Count; i++) if (type.Fields[i] == field) return i;
            return -1;
        }

        static readonly RecordField GuidField = new RecordField("guid", RecordFieldKind.Guid);

        static string Join(string path, string name) => path.Length == 0 ? name : path + "/" + name;

        RecordValue? ReadField(YamlFrameNode node, RecordField field, string path, int blockIndent)
        {
            if (field.MultiWritingSystem)
            {
                if (node.Kind != YamlFrameKind.Mapping) { Shape(node, "a mapping from writing-system id to value"); return null; }
                var alts = new RecordAlternatives { Path = path, Range = new[] { node.Start, node.End } };
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var pair in node.Pairs)
                {
                    if (!pair.Key.IsScalar || !seen.Add(pair.Key.Value)) continue;
                    string ws = pair.Key.Value;
                    if (!CheckKeyWritable(pair.Key, "writing-system id")) continue;
                    if (!node.Flow) CheckKeySpelling(pair.Key, ws);
                    _context.CheckWritingSystem(ws, pair.Key.Start, pair.Key.End);
                    if (IsEmptyNode(pair.Value)) { ReportEmpty(pair); continue; }
                    var value = ReadLeaf(pair.Value, field, path + "." + ws, Column(pair.Key.Start) + 2, ws);
                    if (value != null) alts.Items.Add(new KeyValuePair<string, RecordScalar>(ws, value));
                }
                return alts;
            }
            switch (field.Kind)
            {
                case RecordFieldKind.Sequence:
                {
                    if (node.Kind != YamlFrameKind.Sequence) { Shape(node, "a list"); return null; }
                    var list = new RecordList { Path = path, Range = new[] { node.Start, node.End } };
                    foreach (var item in node.Items)
                    {
                        string itemPath = path + "/" + (list.Items.Count + 1).ToString(CultureInfo.InvariantCulture);
                        if (IsEmptyNode(item))
                        {
                            Info(RecordDiagnosticIds.Empty, "An empty item is left out.", item.Start, Math.Max(item.End, item.Start + 1));
                            continue;
                        }
                        RecordValue? value;
                        if (field.Item!.Kind == RecordFieldKind.Object)
                        {
                            if (item.Kind != YamlFrameKind.Mapping) { Shape(item, "an object"); continue; }
                            value = ReadObject(item, field.Item.ObjectType!, itemPath, null, null);
                        }
                        // A block item's lines go where its value starts: two columns after the dash.
                        else value = ReadLeaf(item, field.Item, itemPath, Column(item.Start), null);
                        if (value != null) list.Items.Add(value);
                    }
                    return list;
                }
                case RecordFieldKind.Object:
                    if (node.Kind != YamlFrameKind.Mapping) { Shape(node, "an object"); return null; }
                    return ReadObject(node, field.ObjectType!, path, null, null);
                default:
                    return ReadLeaf(node, field, path, blockIndent, null);
            }
        }

        // ---------------------------------------------------------------- values

        /// <summary>Reads one value of <paramref name="field"/>. <paramref name="blockIndent"/> is where a block's lines go (the key's or dash's column plus
        /// two), for the fix; <paramref name="ws"/> is the alternative's writing system, or null.</summary>
        RecordScalar? ReadLeaf(YamlFrameNode node, RecordField field, string path, int blockIndent, string? ws)
        {
            if (node.Kind != YamlFrameKind.Scalar && node.Kind != YamlFrameKind.Alias) { Shape(node, "a single value"); return null; }
            string raw = node.Kind == YamlFrameKind.Alias ? "*" + node.Value : node.Value;
            int valueStart = ValueStart(node);
            int[] range = { valueStart, node.End };
            bool block = node.Kind == YamlFrameKind.Scalar && (node.Style == ScalarStyle.Literal || node.Style == ScalarStyle.Folded);
            int spellEnd = block ? TrimLineEnds(node.End) : node.End;
            // A file checked out with CR LF line ends spells a block the same as with LF.
            string written = _text.Substring(valueStart, Math.Max(0, spellEnd - valueStart)).Replace("\r\n", "\n");
            string? value;
            bool decoded = false, checkSpelling = true;
            switch (field.Kind)
            {
                case RecordFieldKind.Integer:
                    if (!(node.IsScalar && long.TryParse(raw, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long n)))
                    {
                        Error(RecordDiagnosticIds.NotAnInteger, "'" + raw + "' is not a whole number.", node.Start, Math.Max(node.End, node.Start + 1));
                        return null;
                    }
                    value = n.ToString(CultureInfo.InvariantCulture);
                    break;
                case RecordFieldKind.Boolean:
                {
                    string lower = raw.ToLowerInvariant();
                    string? meant = lower == "true" || lower == "yes" || lower == "y" || lower == "on" ? "true" : lower == "false" || lower == "no" || lower == "n" || lower == "off" ? "false" : null;
                    if (raw != "true" && raw != "false")
                    {
                        Error(RecordDiagnosticIds.NotABoolean, "A Boolean field is true or false" + (meant == null ? "." : ", so '" + raw + "' is written " + meant + "."),
                            node.Start, Math.Max(node.End, node.Start + 1), meant);
                        return null;
                    }
                    value = raw;
                    break;
                }
                case RecordFieldKind.Guid:
                {
                    if (!System.Guid.TryParse(raw, out var g))
                    {
                        Error(RecordDiagnosticIds.BadGuid, "'" + raw + "' is not a GUID.", node.Start, Math.Max(node.End, node.Start + 1));
                        return null;
                    }
                    value = g.ToString("D");
                    break;
                }
                case RecordFieldKind.Rich:
                    // A folded rich value is reported below, once its paragraphs are read, so the fix can be spelled from them when the text cannot be.
                    value = block ? raw.TrimEnd('\n') : raw;
                    break;
                default:
                    if (node.Kind == YamlFrameKind.Scalar && node.Style == ScalarStyle.Folded)
                    {
                        value = raw.TrimEnd('\n');
                        Error(RecordDiagnosticIds.Folded, "A folded block rewrites line ends; text is a double-quoted string.", valueStart, spellEnd, Canonical(field, value, blockIndent, path));
                    }
                    else if (block && (field.Kind == RecordFieldKind.Text || field.Kind == RecordFieldKind.Notation))
                    {
                        // Text in a block is inline FieldWorks Markdown, which can spell control characters (ADR 0032).
                        value = DecodePlainBlock(raw.TrimEnd('\n'), valueStart, spellEnd, out bool markup);
                        decoded = true;
                        if (markup) checkSpelling = false;
                    }
                    else value = raw;
                    break;
            }
            if (value == null) return null;

            var scalar = new RecordScalar(value) { Path = path, Range = range };
            int[] map = decoded ? Fallback(value, valueStart, spellEnd) : ValueMap(node, valueStart, value, spellEnd);
            if (field.Kind == RecordFieldKind.Rich) scalar.Paragraphs = ReadRich(value, field, ws, map, node.Kind == YamlFrameKind.Scalar && node.Style == ScalarStyle.Literal);

            // The canonical spelling, and a finding when the file spells it otherwise. A value the writer cannot spell is an error, so a file read without one
            // can always be written (review of PR 147, P2-1); its fix spells what was read, where that reads back the same.
            if (checkSpelling && node.Kind == YamlFrameKind.Scalar)
            {
                string? canonical = Canonical(field, value, blockIndent, path, out string? problem);
                bool folded = node.Style == ScalarStyle.Folded && field.Kind == RecordFieldKind.Rich;
                if (canonical == null)
                {
                    string? fix = field.Kind == RecordFieldKind.Rich ? Respelled(field, ws, scalar.Paragraphs!, blockIndent, path) : null;
                    if (folded) Error(RecordDiagnosticIds.Folded, "A folded block rewrites line ends; rich text is a literal block.", valueStart, spellEnd, fix);
                    Error(RecordDiagnosticIds.Unwritable, "This value cannot be written in a record file: " + problem + "." + (fix == null ? string.Empty : " The fix writes what was read."),
                        valueStart, spellEnd, fix);
                }
                else if (folded) Error(RecordDiagnosticIds.Folded, "A folded block rewrites line ends; rich text is a literal block.", valueStart, spellEnd, canonical);
                else if (node.Style != ScalarStyle.Folded && canonical != written)
                {
                    bool unquoted = node.Style == ScalarStyle.Plain && (field.IsQuotedText || field.Kind == RecordFieldKind.Rich);
                    if (unquoted) Info(RecordDiagnosticIds.Unquoted, "A plain value is read as the text it spells; " + (field.Kind == RecordFieldKind.Rich ? "rich text is a literal block." : "text is double-quoted."), valueStart, spellEnd, canonical);
                    else Info(RecordDiagnosticIds.ScalarStyle, "This value is written " + (canonical.IndexOf('\n') >= 0 ? "as a literal block" : canonical) + ".", valueStart, spellEnd, canonical);
                }
            }

            switch (field.Kind)
            {
                case RecordFieldKind.Reference:
                    scalar.Target = ReadReference(value, node, map);
                    break;
                case RecordFieldKind.Notation:
                    CheckNotation(field.Notation!, value, path, map);
                    break;
            }
            return scalar;
        }

        int TrimLineEnds(int end)
        {
            end = Math.Min(end, _text.Length);
            while (end > 0 && (_text[end - 1] == '\n' || _text[end - 1] == '\r')) end--;
            return end;
        }

        string? Canonical(RecordField field, string value, int blockIndent, string path) => Canonical(field, value, blockIndent, path, out _);

        string? Canonical(RecordField field, string value, int blockIndent, string path, out string? problem)
        {
            problem = null;
            try
            {
                string spelled = RecordFileWriter.Spell(field, value, blockIndent, path);
                return spelled.EndsWith("\n", StringComparison.Ordinal) ? spelled.Substring(0, spelled.Length - 1) : spelled;
            }
            catch (RecordWriteException e)
            {
                problem = e.Message.TrimEnd('.');
                return null;
            }
        }

        /// <summary>The paragraphs that were read, written again as FieldWorks Markdown, when that reads back to the same paragraphs; otherwise null.</summary>
        string? RespelledText(RecordField field, string? ws, IReadOnlyList<Paragraph> paragraphs)
        {
            try
            {
                var options = LeafOptions(DefaultWs(field, ws));
                string text = field.Paragraphs ? RecordScalar.Rich(paragraphs, options).Text
                    : RecordScalar.RichInline(paragraphs.Count == 1 ? (IEnumerable<Run>)paragraphs[0].Runs : Array.Empty<Run>(), options).Text;
                if (RecordProfile.LiteralProblem(text) != null) return null;
                IReadOnlyList<Paragraph> again;
                if (field.Paragraphs) again = MarkdownConverter.Parse(text, options).Document.Paragraphs;
                else
                {
                    var p = new Paragraph { Style = options.StyleNames.Normal };
                    p.Props.StrProps[TextPropertyNumbers.NamedStyle] = options.StyleNames.Normal;
                    foreach (var run in MarkdownConverter.ParseInline(text, options).Runs) p.Runs.Add(run);
                    if (p.Runs.Count > 0) p.DefaultWs = p.Runs[0].Ws;
                    again = new[] { p };
                }
                return MarkdownConverter.AreEquivalent(paragraphs, again) ? text : null;
            }
            catch (MarkdownWriteException)
            {
                return null;
            }
        }

        string? Respelled(RecordField field, string? ws, IReadOnlyList<Paragraph> paragraphs, int blockIndent, string path)
        {
            string? text = RespelledText(field, ws, paragraphs);
            return text == null ? null : Canonical(field, text, blockIndent, path);
        }

        string DecodePlainBlock(string markdown, int start, int end, out bool markup)
        {
            var options = new MarkdownOptions { WritingSystems = new FixedWritingSystems("x", "x"), NormalizeText = false };
            var result = MarkdownConverter.ParseInline(markdown, options);
            bool plain = result.Runs.All(r => r.Ws == "x" && r.Props.Count == 0 && !r.IsObject);
            foreach (var d in result.Diagnostics) Report(d.Id, d.Severity, d.Message, start, end, null);
            markup = !plain;
            if (!plain) Error(RecordDiagnosticIds.PlainFieldMarkup, "This field holds plain text; a block holds it as FieldWorks Markdown only to spell characters, not formatting or writing systems.", start, end);
            return string.Concat(result.Runs.Select(r => r.Text));
        }

        // ---------------------------------------------------------------- inner languages

        MarkdownOptions LeafOptions(string analysis)
        {
            var host = _options.Markdown;
            var options = new MarkdownOptions
            {
                WritingSystems = new DefaultsOverride(host.WritingSystems, analysis, _vernacular),
                StyleNames = host.StyleNames,
                Styles = host.Styles,
                NormalizeText = host.NormalizeText,
                Objects = host.Objects,
                Paths = host.Paths,
                WriteFrontMatter = false,
                LinkForm = host.LinkForm,
                LinkBase = host.LinkBase,
                ProjectName = host.ProjectName,
                SilfwDatabase = host.SilfwDatabase,
                UnknownGuids = host.UnknownGuids,
            };
            return options;
        }

        string DefaultWs(RecordField field, string? ws) => ws ?? (field.WritingSystem == RecordWritingSystem.Vernacular ? _vernacular : _analysis);

        IReadOnlyList<Paragraph> ReadRich(string value, RecordField field, string? ws, int[] map, bool literal = false)
        {
            var options = LeafOptions(DefaultWs(field, ws));
            if (field.Paragraphs)
            {
                var result = MarkdownConverter.Parse(value, options);
                Forward(result.Diagnostics, map);
                CheckBlocks(result, field, options, map, literal);
                return result.Document.Paragraphs.ToList();
            }
            var inline = MarkdownConverter.ParseInline(value, options);
            Forward(inline.Diagnostics, map);
            // One paragraph of inline text has the normal style, as a multi-string field's text is read into one.
            var paragraph = new Paragraph { Style = options.StyleNames.Normal };
            paragraph.Props.StrProps[TextPropertyNumbers.NamedStyle] = options.StyleNames.Normal;
            foreach (var run in inline.Runs) paragraph.Runs.Add(run);
            if (paragraph.Runs.Count > 0) paragraph.DefaultWs = paragraph.Runs[0].Ws;
            else paragraph.DefaultWs = DefaultWs(field, ws);
            return new[] { paragraph };
        }

        /// <summary>A table, list or heading in a rich field that does not allow it is <c>fwmd.record.block-not-allowed</c> (ADR 0033, owner decision Q79): one finding for each
        /// block, on the block's lines. Its fix is the block as plain paragraphs, made by <see cref="BlockFlattener"/>, the same code that flattens a paste; it is offered
        /// for a literal block (the body too), where the lines can be replaced, and only when the plain paragraphs read back as what they were made from.</summary>
        void CheckBlocks(MarkdownParseResult result, RecordField field, MarkdownOptions options, int[] map, bool literal)
        {
            var allowed = field.BlockFeatures;
            if (allowed == BlockFeatures.All) return;
            var paragraphs = result.Document.Paragraphs;
            var sources = result.ParagraphSources;
            if (sources.Count != paragraphs.Count) return;
            var recognizer = options.StyleNames.ToBlockRecognizer();
            int i = 0;
            while (i < paragraphs.Count)
            {
                var kind = recognizer.Recognize(paragraphs[i]);
                if (!BlockFlattener.IsBlockKind(kind) || (allowed & kind) != 0) { i++; continue; }
                int j = i + 1;
                while (j < paragraphs.Count && sources[j].Block == sources[i].Block && recognizer.Recognize(paragraphs[j]) == kind) j++;
                // The block's lines, whole: Markdig's span of a table row leaves out its outer pipes, and the fix replaces the lines.
                string text = result.Source;
                int start = Math.Max(0, Math.Min(sources[i].Start, text.Length)), end = Math.Max(start, Math.Min(sources[j - 1].End, text.Length));
                while (start > 0 && text[start - 1] != '\n' && text[start - 1] != '\r') start--;
                while (end > start && (text[end - 1] == '\n' || text[end - 1] == '\r')) end--;
                while (end < text.Length && text[end] != '\n' && text[end] != '\r') end++;
                var flat = paragraphs.Skip(i).Take(j - i).Select(p => p.Clone(false)).ToList();
                BlockFlattener.Flatten(flat, allowed, recognizer);
                string name = kind == BlockFeatures.Tables ? "a table" : kind == BlockFeatures.Lists ? "a list" : "a heading";
                string? fix = literal ? FlatBlockText(flat, options, Math.Max(0, ColumnOf(Map(map, 0)))) : null;
                Report(RecordDiagnosticIds.BlockNotAllowed, MarkdownSeverity.Error,
                    "This field does not allow " + name + (kind == BlockFeatures.Tables ? ". Its rows are plain paragraphs here." : kind == BlockFeatures.Lists ? ". Its items are plain paragraphs here." : ". It is a plain paragraph here."),
                    Map(map, start), MapEnd(map, start, end), fix);
                i = j;
            }
        }

        /// <summary>The paragraphs as FieldWorks Markdown for the lines of a literal block at <paramref name="indent"/> spaces, or null when they would not read back as they are.</summary>
        string? FlatBlockText(IReadOnlyList<Paragraph> flat, MarkdownOptions options, int indent)
        {
            try
            {
                string text = RecordScalar.Rich(flat, options).Text;
                if (RecordProfile.LiteralProblem(text) != null) return null;
                if (!MarkdownConverter.AreEquivalent(flat, MarkdownConverter.Parse(text, options).Document.Paragraphs)) return null;
                int firstBreak = _text.IndexOf('\n');   // the file's line ends: CR LF in the file gives CR LF in the replacement, not a mix
                bool crlf = firstBreak > 0 && _text[firstBreak - 1] == '\r';
                var lines = text.Split('\n');
                var sb = new System.Text.StringBuilder(text.Length + lines.Length * indent);
                for (int k = 0; k < lines.Length; k++)
                {
                    if (k > 0) sb.Append(crlf ? "\r\n" : "\n");
                    if (k > 0 && lines[k].Length > 0) sb.Append(' ', indent);
                    sb.Append(lines[k]);
                }
                return sb.ToString();
            }
            catch (MarkdownWriteException)
            {
                return null;
            }
        }

        /// <summary>The column of a file offset: its distance from the start of its line.</summary>
        int ColumnOf(int offset)
        {
            offset = Math.Max(0, Math.Min(offset, _text.Length));
            int i = offset;
            while (i > 0 && _text[i - 1] != '\n' && _text[i - 1] != '\r') i--;
            return offset - i;
        }

        Guid? ReadReference(string value, YamlFrameNode node, int[] map)
        {
            var options = LeafOptions(_analysis);
            var result = MarkdownConverter.ParseInline(value, options);
            Forward(result.Diagnostics, map);
            bool one = result.Objects.Count == 1 && result.Runs.Count == 1 && result.Runs[0].IsObject;
            if (!one)
            {
                Error(RecordDiagnosticIds.NotAReference, "A reference is one FieldWorks Markdown link to the object, with guid=.", node.Start, node.End);
                return null;
            }
            return result.Objects[0].Guid;
        }

        void CheckNotation(string notation, string value, string path, int[] map)
        {
            var checker = _options.Notations;
            if (checker == null) return;
            foreach (var d in checker.Check(notation, value, path))
            {
                int s = LanguageText.StartOffset(value, d.Range), e = LanguageText.EndOffset(value, d.Range);
                string? replacement = null;
                if (d.Fix != null && d.Fix.Edits.Count == 1 && d.Fix.Edits[0].Range.Equals(d.Range)) replacement = d.Fix.Edits[0].NewText;
                var severity = d.Severity == DiagnosticSeverity.Error ? MarkdownSeverity.Error : d.Severity == DiagnosticSeverity.Warning ? MarkdownSeverity.Warning : MarkdownSeverity.Suggestion;
                Report(d.Code ?? RecordDiagnosticIds.Notation, severity, d.Message, Map(map, s), MapEnd(map, s, e), replacement);
            }
        }

        void Forward(IReadOnlyList<MarkdownDiagnostic> diagnostics, int[] map)
        {
            foreach (var d in diagnostics)
                Report(d.Id, d.Severity, d.Message, Map(map, d.Start), MapEnd(map, d.Start, d.End), d.Replacement, d.Choices.Count > 0 ? d.Choices : null, d.Nearest.Count > 0 ? d.Nearest : null);
        }

        static int Map(int[] map, int offset) => map[Math.Max(0, Math.Min(offset, map.Length - 1))];
        static int MapEnd(int[] map, int start, int end) => end <= start ? Map(map, start) : Map(map, end - 1) + 1;

        // ---------------------------------------------------------------- where each character of a value is in the file

        /// <summary>For each offset of <paramref name="value"/> (and its end), the offset in the file of the character it was read from.</summary>
        int[] ValueMap(YamlFrameNode node, int start, string value, int end)
        {
            if (node.Kind != YamlFrameKind.Scalar) return Fallback(value, start, end);
            switch (node.Style)
            {
                case ScalarStyle.Literal: return LiteralMap(node, value, end) ?? Fallback(value, start, end);
                case ScalarStyle.DoubleQuoted: return QuotedMap(start + 1, value, '"') ?? Fallback(value, start, end);
                case ScalarStyle.SingleQuoted: return QuotedMap(start + 1, value, '\'') ?? Fallback(value, start, end);
                case ScalarStyle.Plain:
                    return string.CompareOrdinal(_text, start, value, 0, value.Length) == 0 ? Enumerable.Range(start, value.Length + 1).ToArray() : Fallback(value, start, end);
                default: return Fallback(value, start, end);
            }
        }

        /// <summary>Where a node's value starts: after its anchor and tag, if it has them.</summary>
        int ValueStart(YamlFrameNode node)
        {
            int i = node.Start;
            if (node.Anchor == null && node.Tag == null) return i;
            for (int round = 0; round < 2 && i < _text.Length && (_text[i] == '&' || _text[i] == '!'); round++)
            {
                while (i < _text.Length && _text[i] != ' ' && _text[i] != '\n' && _text[i] != '\r') i++;
                while (i < _text.Length && _text[i] == ' ') i++;
            }
            return i;
        }

        static int[] Fallback(string value, int start, int end)
        {
            var map = new int[value.Length + 1];
            for (int i = 0; i < value.Length; i++) map[i] = start;
            map[value.Length] = Math.Max(start, end - 1);
            return map;
        }

        int[]? LiteralMap(YamlFrameNode node, string value, int end)
        {
            int indent = BlockIndent(node);
            var map = new int[value.Length + 1];
            int line = NextLine(node.Start);
            int j = 0;
            foreach (string part in value.Split('\n'))
            {
                if (line > _text.Length) return null;
                int contentStart = Math.Min(line + indent, LineEnd(line));
                for (int k = 0; k < part.Length; k++)
                {
                    int at = contentStart + k;
                    if (at >= _text.Length || _text[at] != part[k]) return null;
                    map[j++] = at;
                }
                if (j < value.Length) map[j++] = LineEnd(line);
                line = NextLine(line);
            }
            map[value.Length] = value.Length > 0 ? map[value.Length - 1] + 1 : Math.Min(end, _text.Length);
            return map;
        }

        int[]? QuotedMap(int at, string value, char quote)
        {
            var map = new int[value.Length + 1];
            int i = at;
            for (int j = 0; j < value.Length; j++)
            {
                if (i >= _text.Length) return null;
                char c = _text[i];
                map[j] = i;
                if (quote == '\'' && c == '\'' && i + 1 < _text.Length && _text[i + 1] == '\'') { i += 2; continue; }
                if (quote == '"' && c == '\\' && i + 1 < _text.Length)
                {
                    char e = _text[i + 1];
                    int length = e == 'x' ? 4 : e == 'u' ? 6 : e == 'U' ? 10 : 2;
                    if (e == 'U' && j + 1 < value.Length && char.IsHighSurrogate(value[j]) && char.IsLowSurrogate(value[j + 1])) map[++j] = i;
                    i += length;
                    continue;
                }
                if (c != value[j]) return null;
                i++;
            }
            map[value.Length] = i;
            return map;
        }

        // ---------------------------------------------------------------- the body

        /// <summary>Reads the body: the body field's value when there is one, an error when there is text and no body field, and in any case a YAML block
        /// in it (<see cref="CheckBodyYaml"/>).</summary>
        void CheckBody(int end, RecordField? bodyField, RecordDocument? document, RecordRoot? root)
        {
            // The body, with CR LF and CR read as LF, from its first line that is not blank to its last character that is not white space.
            var value = new StringBuilder();
            var offsets = new List<int>();
            for (int i = end; i < _text.Length; i++)
            {
                char c = _text[i];
                if (c == '\r') { offsets.Add(i); value.Append('\n'); if (i + 1 < _text.Length && _text[i + 1] == '\n') i++; continue; }
                offsets.Add(i);
                value.Append(c);
            }
            // Only spaces, tabs and line ends are trimmed: a no-break or ideographic space at the end is text (review of PR 147).
            string all = value.ToString();
            int last = all.Length - 1;
            while (last >= 0 && IsBlank(all[last])) last--;
            int first = 0;
            for (int i = 0; i <= last; i++)
            {
                if (all[i] == '\n') first = i + 1;
                else if (!IsBlank(all[i])) break;
            }
            CheckBodyYaml(end);
            if (last < 0) return;
            string text = all.Substring(first, last + 1 - first);
            var map = new int[text.Length + 1];
            for (int i = 0; i < text.Length; i++) map[i] = offsets[first + i];
            map[text.Length] = offsets[last] + 1;
            if (document == null || root == null) return;
            if (bodyField == null)
            {
                Error(RecordDiagnosticIds.UnexpectedBody, "This record has no body field, so nothing follows the front matter.", map[0], map[text.Length]);
                return;
            }
            var scalar = new RecordScalar(text) { Path = bodyField.Name + (root.WritingSystem != null ? "." + root.WritingSystem : string.Empty), Range = new[] { map[0], map[text.Length] } };
            scalar.Paragraphs = ReadRich(text, bodyField, root.WritingSystem, map, true);
            string? problem = RecordProfile.LiteralProblem(text);
            if (problem != null)
            {
                string? fix = RespelledText(bodyField, root.WritingSystem, scalar.Paragraphs);
                Error(RecordDiagnosticIds.Unwritable, "The body cannot be written in a record file: " + problem + "." + (fix == null ? string.Empty : " The fix writes what was read."),
                    map[0], map[text.Length], fix);
            }
            if (root.WritingSystem != null)
            {
                var alts = document.Data[bodyField.Name] as RecordAlternatives ?? new RecordAlternatives { Path = bodyField.Name };
                alts[root.WritingSystem] = scalar;
                document.Data[bodyField.Name] = alts;
            }
            else document.Data[bodyField.Name] = scalar;
        }

        static bool IsBlank(char c) => c == ' ' || c == '\t' || c == '\n';

        /// <summary>A YAML block in the body (a <c>---</c> line after a blank line, a key on the next line, and a closing line), which Pandoc reads into the
        /// metadata: a second document.</summary>
        void CheckBodyYaml(int end)
        {
            int line = end;
            bool previousBlank = true;
            while (line < _text.Length)
            {
                int lineEnd = LineEnd(line);
                string text = _text.Substring(line, lineEnd - line);
                int next = NextLine(line);
                if (previousBlank && text.TrimEnd() == "---" && next < _text.Length)
                {
                    string following = _text.Substring(next, LineEnd(next) - next);
                    int colon = following.IndexOf(':');
                    if (following.Length > 0 && !char.IsWhiteSpace(following[0]) && colon > 0 && (colon + 1 == following.Length || following[colon + 1] == ' '))
                    {
                        int close = NextLine(next);
                        while (close < _text.Length)
                        {
                            string t = _text.Substring(close, LineEnd(close) - close).TrimEnd();
                            if (t == "---" || t == "...") break;
                            close = NextLine(close);
                        }
                        if (close < _text.Length)
                        {
                            Error(RecordDiagnosticIds.SecondDocument, "Pandoc reads this block as YAML metadata; a record file has one front matter.", line, NextLine(close), string.Empty);
                            line = NextLine(close);
                            previousBlank = false;
                            continue;
                        }
                    }
                }
                previousBlank = text.Trim().Length == 0;
                line = next;
            }
        }
    }

    /// <summary>The host's writing systems with other defaults: the file's (or an alternative's) analysis and the file's vernacular. Every optional interface
    /// of the host's mapper is passed through, so the FieldWorks Markdown reader checks tags as it would with the host's own.</summary>
    internal sealed class DefaultsOverride : IWritingSystemMapper, IWritingSystemChecker, IKnownWritingSystems, IWritingSystemDetails
    {
        readonly IWritingSystemMapper _inner;

        public DefaultsOverride(IWritingSystemMapper inner, string analysis, string vernacular)
        {
            _inner = inner;
            DefaultAnalysis = analysis;
            DefaultVernacular = vernacular;
        }

        public string DefaultAnalysis { get; }
        public string DefaultVernacular { get; }
        public bool IsKnown(string tag) => _inner.IsKnown(tag);

        public IReadOnlyList<string> KnownTags => (_inner as IKnownWritingSystems)?.KnownTags ?? Array.Empty<string>();

        public MarkdownProblem? CheckWritingSystem(string tag)
        {
            if (_inner is IWritingSystemChecker checker) return checker.CheckWritingSystem(tag);
            if (_inner.IsKnown(tag)) return null;
            var known = (_inner as IKnownWritingSystems)?.KnownTags;
            var nearest = known == null ? null : ReaderContext.NearestNames(tag, known);
            return new MarkdownProblem("'" + tag + "' is not a writing system of this project." + (nearest == null ? string.Empty : " Nearest: " + string.Join(", ", nearest) + "."));
        }

        public WritingSystemInfo? DescribeWritingSystem(string id) => (_inner as IWritingSystemDetails)?.DescribeWritingSystem(id);
    }
}
