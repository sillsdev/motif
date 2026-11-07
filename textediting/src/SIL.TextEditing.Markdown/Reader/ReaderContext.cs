using System;
using System.Collections.Generic;
using Markdig;
using SIL.TextEditing.Markdown.Notation;

namespace SIL.TextEditing.Markdown.Reader
{
    /// <summary>The state of one read: the options, the source text (for line and column numbers) and the findings. Markdig's
    /// parsers reach it through <see cref="MarkdownParserContext"/>.</summary>
    internal sealed class ReaderContext : IAttrIssues
    {
        internal const string ContextKey = "SIL.TextEditing.Markdown.Reader";

        readonly List<MarkdownDiagnostic> _diagnostics = new List<MarkdownDiagnostic>();
        List<int>? _lineStarts;

        public ReaderContext(MarkdownOptions options, string source)
        {
            Options = options;
            Source = source;
            DefaultAnalysis = options.WritingSystems.DefaultAnalysis;
            DefaultVernacular = options.WritingSystems.DefaultVernacular;
        }

        public MarkdownOptions Options { get; }

        /// <summary>The writing system of unmarked text: the front matter's <c>analysis</c>, or else the host's default.</summary>
        public string DefaultAnalysis { get; set; }

        /// <summary>The writing system <c>.v</c> stands for: the front matter's <c>vernacular</c>, or else the host's default.</summary>
        public string DefaultVernacular { get; set; }

        /// <summary>The front matter the text started with, or null.</summary>
        public MarkdownFrontMatter? FrontMatter { get; set; }
        public string Source { get; }
        public IReadOnlyList<MarkdownDiagnostic> Diagnostics
        {
            get
            {
                if (_removed != null)
                {
                    _diagnostics.RemoveAll(_removed.Contains);
                    _removed = null;
                }
                return _diagnostics;
            }
        }

        /// <summary>How many findings there are now; <see cref="Truncate"/> goes back to such a count.</summary>
        public int Count => _diagnostics.Count;

        /// <summary>Forgets the findings made after the first <paramref name="count"/>: a parse that did not finish may have made some, and
        /// the parse is done again.</summary>
        public void Truncate(int count)
        {
            if (count < _diagnostics.Count) _diagnostics.RemoveRange(count, _diagnostics.Count - count);
        }

        // The findings Markdig's parsers made while it read the whole text, in order of where they start, and the ones dropped since.
        MarkdownDiagnostic[]? _parsed;
        HashSet<MarkdownDiagnostic>? _removed;

        /// <summary>Called once Markdig has read the text: the findings so far are the ones <see cref="RemoveParsed"/> may drop.</summary>
        public void EndParse()
        {
            var parsed = _diagnostics.ToArray();
            var starts = new int[parsed.Length];
            for (int i = 0; i < parsed.Length; i++) starts[i] = parsed[i].Start;
            Array.Sort(starts, parsed);
            _parsed = parsed;
        }

        /// <summary>Drops the findings Markdig's parsers made that start in [start, end): the line was read as text before it was known to be
        /// a marker. Binary search and a mark, so a document with many markers and many findings is read in one pass.</summary>
        public void RemoveParsed(int start, int end)
        {
            var parsed = _parsed;
            if (parsed == null) return;
            int lo = 0, hi = parsed.Length;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (parsed[mid].Start < start) lo = mid + 1; else hi = mid;
            }
            for (int i = lo; i < parsed.Length && parsed[i].Start < end; i++)
            {
                if (_removed == null) _removed = new HashSet<MarkdownDiagnostic>();
                _removed.Add(parsed[i]);
            }
        }

        /// <summary>The offset in <see cref="Source"/> of the text Markdig is parsing now (a fence line is parsed on its own).</summary>
        public int Base { get; set; }

        /// <summary>The offsets where a div fence stands in the blanked text as a thematic break, which does end a paragraph (see MarkdownReader.Interrupt).</summary>
        public HashSet<int> FenceStarts { get; set; } = new HashSet<int>();

        public MarkdownParserContext NewParserContext()
        {
            var context = new MarkdownParserContext();
            context.Properties[ContextKey] = this;
            return context;
        }

        public static ReaderContext? From(MarkdownParserContext? context)
            => context != null && context.Properties.TryGetValue(ContextKey, out var value) ? value as ReaderContext : null;

        public MarkdownDiagnostic Report(string id, MarkdownSeverity severity, string message, int start, int end, string? replacement = null,
            IReadOnlyList<string>? choices = null, IReadOnlyList<string>? nearest = null)
        {
            var d = CreateDiagnostic(id, severity, message, start, end, replacement, choices, nearest);
            _diagnostics.Add(d);
            return d;
        }

        /// <summary>Makes a finding for a range of the source, with its line, column and observed text; the caller decides whether it is kept.</summary>
        public MarkdownDiagnostic CreateDiagnostic(string id, MarkdownSeverity severity, string message, int start, int end, string? replacement = null,
            IReadOnlyList<string>? choices = null, IReadOnlyList<string>? nearest = null)
        {
            start = Math.Max(0, Math.Min(start, Source.Length));
            end = Math.Max(start, Math.Min(end, Source.Length));
            var d = new MarkdownDiagnostic(id, severity, message, start, end) { Observed = Source.Substring(start, end - start), Replacement = replacement };
            if (choices != null) d.Choices = choices;
            if (nearest != null) d.Nearest = nearest;
            Locate(start, out int line, out int column);
            d.Line = line;
            d.Column = column;
            Locate(end, out int endLine, out int endColumn);
            d.EndLine = endLine;
            d.EndColumn = endColumn;
            return d;
        }

        /// <summary>The place of a range of the source, as a host sees it on a <see cref="MarkdownObject"/>.</summary>
        public MarkdownSourceRange RangeOf(int start, int end)
        {
            start = Math.Max(0, Math.Min(start, Source.Length));
            end = Math.Max(start, Math.Min(end, Source.Length));
            Locate(start, out int line, out int column);
            Locate(end, out int endLine, out int endColumn);
            return new MarkdownSourceRange(start, end, line, column, endLine, endColumn);
        }

        readonly List<MarkdownObject> _objects = new List<MarkdownObject>();

        /// <summary>The objects built so far, in the order the parser read them (source order within a paragraph; fences and blocks are read in order).</summary>
        public IReadOnlyList<MarkdownObject> Objects => _objects;

        public void AddObject(MarkdownObject target) => _objects.Add(target);

        void IAttrIssues.Report(string id, MarkdownSeverity severity, string message, AttrItem item, string? replacement)
            => Report(id, severity, message, Base + item.Start, Base + item.End, replacement);

        /// <summary>Reports an item of an attribute block whose key nothing recognises, with the nearest key as a hint.</summary>
        public void ReportUnknown(AttrItem item, IEnumerable<string> knownKeys)
        {
            string? nearest = Nearest(item.Key, knownKeys);
            string message = "'" + item.Spelling + "' is not a known " + (item.IsClass ? "class" : item.IsFlag ? "word" : "attribute") + "." + (nearest == null ? string.Empty : " Did you mean " + nearest + "?");
            var choices = nearest == null ? null : new[] { nearest };
            Report(MarkdownDiagnosticIds.UnknownAttribute, MarkdownSeverity.Error, message, Base + item.Start, Base + item.End, null, choices);
        }

        /// <summary>Asks the host about a writing system tag and reports what it says: <c>fwmd.ws.unknown</c> when it does not know the tag (the
        /// host's list, when it has one, as the choices and the closest tags as the nearest), or the host's own problem when it can give one.</summary>
        public void CheckWritingSystem(string tag, int start, int end)
        {
            var mapper = Options.WritingSystems;
            var known = (mapper as IKnownWritingSystems)?.KnownTags;
            var nearest = known == null ? null : NearestNames(tag, known);
            IReadOnlyList<string>? choices = known != null && known.Count > 0 ? known : null;
            MarkdownProblem? problem;
            if (mapper is IWritingSystemChecker checker)
            {
                problem = checker.CheckWritingSystem(tag);
                if (problem == null) return;
            }
            else
            {
                if (mapper.IsKnown(tag)) return;
                problem = new MarkdownProblem("'" + tag + "' is not a writing system of this project." + (nearest == null ? string.Empty : " Nearest: " + string.Join(", ", nearest) + "."));
            }
            ReportHostProblem(problem, MarkdownDiagnosticIds.WsUnknown, start, end, choices, nearest);
        }

        /// <summary>Asks the host about a style name (a paragraph style or a character style) and reports what it says: <c>fwmd.style.unknown</c>
        /// with the closest names as the nearest when it does not know the style, or the host's own problem when it can give one.</summary>
        public void CheckStyle(string name, bool paragraph, int start, int end)
        {
            var catalog = Options.Styles;
            if (catalog == null) return;
            var listed = catalog as IKnownStyles;
            var known = listed == null ? null : paragraph ? listed.ParagraphStyleNames : listed.CharacterStyleNames;
            var nearest = known == null ? null : NearestNames(name, known);
            MarkdownProblem? problem;
            if (catalog is IStyleChecker checker)
            {
                problem = checker.CheckStyle(name, paragraph);
                if (problem == null) return;
            }
            else
            {
                if (paragraph ? catalog.IsParagraphStyle(name) : catalog.IsCharacterStyle(name)) return;
                problem = new MarkdownProblem("'" + name + "' is not a " + (paragraph ? "paragraph" : "character") + " style of this project."
                    + (nearest == null ? string.Empty : " Nearest: " + string.Join(", ", nearest) + "."));
            }
            ReportHostProblem(problem, MarkdownDiagnosticIds.StyleUnknown, start, end, null, nearest);
        }

        /// <summary>Reports a host's problem on a range. What the host left out (choices, nearest names) is filled from its lists when it has them.</summary>
        void ReportHostProblem(MarkdownProblem problem, string defaultId, int start, int end, IReadOnlyList<string>? choices, IReadOnlyList<string>? nearest)
            => Report(problem.Id ?? defaultId, problem.Severity, problem.Message, start, end, problem.Replacement,
                problem.Choices.Count > 0 ? problem.Choices : choices, problem.Nearest.Count > 0 ? problem.Nearest : nearest);

        /// <summary>The names of <paramref name="known"/> closest to <paramref name="name"/>, nearest first (at most three), or null when none is
        /// close: within a third of the length, at least one edit, a swap of two neighbours counting as one.</summary>
        internal static IReadOnlyList<string>? NearestNames(string name, IEnumerable<string> known)
        {
            int limit = Math.Max(1, name.Length / 3);
            var found = new List<KeyValuePair<int, string>>();
            foreach (var candidate in known)
            {
                int d = EditDistance(name.ToLowerInvariant(), candidate.ToLowerInvariant());
                if (d <= limit) found.Add(new KeyValuePair<int, string>(d, candidate));
            }
            if (found.Count == 0) return null;
            // Stable: equally near names keep the host's order.
            var ordered = new List<string>();
            for (int d = 0; d <= limit && ordered.Count < 3; d++)
                foreach (var f in found) if (f.Key == d && ordered.Count < 3) ordered.Add(f.Value);
            return ordered;
        }

        /// <summary>Edits to turn one string into the other: insert, delete, substitute, or swap two neighbours.</summary>
        static int EditDistance(string a, string b)
        {
            var d = new int[a.Length + 1, b.Length + 1];
            for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
            for (int j = 0; j <= b.Length; j++) d[0, j] = j;
            for (int i = 1; i <= a.Length; i++)
            {
                for (int j = 1; j <= b.Length; j++)
                {
                    int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
                    if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1]) d[i, j] = Math.Min(d[i, j], d[i - 2, j - 2] + 1);
                }
            }
            return d[a.Length, b.Length];
        }

        static string? Nearest(string key, IEnumerable<string> known)
        {
            string? best = null;
            int bestDistance = 3;
            foreach (var k in known)
            {
                int d = Distance(key.ToLowerInvariant(), k.ToLowerInvariant());
                if (d < bestDistance) { bestDistance = d; best = k; }
            }
            return best;
        }

        static int Distance(string a, string b)
        {
            var row = new int[b.Length + 1];
            for (int j = 0; j <= b.Length; j++) row[j] = j;
            for (int i = 1; i <= a.Length; i++)
            {
                int diagonal = row[0];
                row[0] = i;
                for (int j = 1; j <= b.Length; j++)
                {
                    int above = row[j];
                    row[j] = Math.Min(Math.Min(row[j] + 1, row[j - 1] + 1), diagonal + (a[i - 1] == b[j - 1] ? 0 : 1));
                    diagonal = above;
                }
            }
            return row[b.Length];
        }

        void Locate(int offset, out int line, out int column)
        {
            if (_lineStarts == null)
            {
                _lineStarts = new List<int> { 0 };
                for (int i = 0; i < Source.Length; i++)
                {
                    if (Source[i] == '\n') _lineStarts.Add(i + 1);
                    else if (Source[i] == '\r' && !(i + 1 < Source.Length && Source[i + 1] == '\n')) _lineStarts.Add(i + 1);
                }
            }
            int lo = 0, hi = _lineStarts.Count - 1;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (_lineStarts[mid] <= offset) lo = mid; else hi = mid - 1;
            }
            line = lo + 1;
            column = offset - _lineStarts[lo] + 1;
        }
    }
}
