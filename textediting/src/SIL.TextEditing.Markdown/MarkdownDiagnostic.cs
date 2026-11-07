using System;
using System.Collections.Generic;

namespace SIL.TextEditing.Markdown
{
    public enum MarkdownSeverity { Suggestion, Warning, Error }

    /// <summary>A finding the parser makes while reading Markdown: a stable id (the spec's <c>fwmd.*</c> ids), a place in the
    /// source and, where one exists, the exact fix. The project-aware checker in Motif adds its own findings to these.</summary>
    public sealed class MarkdownDiagnostic
    {
        public MarkdownDiagnostic(string id, MarkdownSeverity severity, string message, int start, int end)
        {
            Id = id;
            Severity = severity;
            Message = message;
            Start = start;
            End = Math.Max(start, end);
        }

        public string Id { get; }
        public MarkdownSeverity Severity { get; }
        public string Message { get; }

        /// <summary>UTF-16 offset of the first character of the finding in the source text.</summary>
        public int Start { get; }
        /// <summary>UTF-16 offset after the last character.</summary>
        public int End { get; }

        /// <summary>1-based line of <see cref="Start"/>.</summary>
        public int Line { get; internal set; }
        /// <summary>1-based column of <see cref="Start"/> in UTF-16 code units.</summary>
        public int Column { get; internal set; }
        public int EndLine { get; internal set; }
        public int EndColumn { get; internal set; }

        /// <summary>The text the finding is about.</summary>
        public string? Observed { get; set; }

        /// <summary>One exact replacement of <see cref="Observed"/> that fixes the finding, or null when intent is ambiguous.</summary>
        public string? Replacement { get; set; }

        /// <summary>The choices, when there is no single safe fix.</summary>
        public IReadOnlyList<string> Choices { get; set; } = Array.Empty<string>();

        /// <summary>The names the finding suggests instead (<c>fwmd.link.not-found</c>, <c>fwmd.style.unknown</c>, <c>fwmd.ws.unknown</c>).</summary>
        public IReadOnlyList<string> Nearest { get; set; } = Array.Empty<string>();

        public override string ToString() => Id + " " + Line + ":" + Column + " " + Message;
    }

    /// <summary>The result of reading Markdown: the document and every finding.</summary>
    public sealed class MarkdownParseResult
    {
        readonly Reader.ReaderContext _context;

        internal MarkdownParseResult(Document document, IReadOnlyList<MarkdownDiagnostic> diagnostics, Reader.ReaderContext context, IReadOnlyList<ParagraphSource> sources)
        {
            Document = document;
            Diagnostics = diagnostics;
            _context = context;
            ParagraphSources = sources;
        }

        /// <summary>For each paragraph of <see cref="Document"/>, in order, where it was read from (the record reader places findings about a block with it).</summary>
        internal IReadOnlyList<ParagraphSource> ParagraphSources { get; }

        /// <summary>The text the offsets, lines and columns of the findings and objects refer to (the text that was read; a byte order mark
        /// is a space here so that every offset stays the same).</summary>
        public string Source => _context.Source;

        /// <summary>Every object the parser built from the notation (links and pictures), in the order it read them (source order), each with its place
        /// in the text and, when the host resolved it, its data. The host places its own findings with them.</summary>
        public IReadOnlyList<MarkdownObject> Objects => _context.Objects;

        /// <summary>Makes a finding for a range of <see cref="Source"/>, with its line, column and observed text filled in, for a host that checks
        /// more than the parser does (an unmarked vernacular run, a rule of the project). It is not added to <see cref="Diagnostics"/>.</summary>
        public MarkdownDiagnostic CreateDiagnostic(string id, MarkdownSeverity severity, string message, int start, int end,
            string? replacement = null, IReadOnlyList<string>? choices = null, IReadOnlyList<string>? nearest = null)
            => _context.CreateDiagnostic(id, severity, message, start, end, replacement, choices, nearest);

        /// <summary>The front matter the text started with (spec 2.1), or null when it had none (or not one that names <c>fwmd</c>). Its <c>analysis</c> and <c>vernacular</c> were used for unmarked text and <c>.v</c>.</summary>
        public MarkdownFrontMatter? FrontMatter => _context.FrontMatter;

        /// <summary>The writing system unmarked text was read as: the front matter's analysis, or the host's default.</summary>
        public string DefaultAnalysis => _context.DefaultAnalysis;

        /// <summary>The writing system <c>.v</c> was read as.</summary>
        public string DefaultVernacular => _context.DefaultVernacular;

        /// <summary>The paragraphs read. Paragraph keys are null. When there are errors the document is a best effort.</summary>
        public Document Document { get; }

        public IReadOnlyList<MarkdownDiagnostic> Diagnostics { get; }

        public bool HasErrors
        {
            get
            {
                foreach (var d in Diagnostics) if (d.Severity == MarkdownSeverity.Error) return true;
                return false;
            }
        }
    }

    /// <summary>The ids of the findings this library reports. They are the spec's checker ids (section 6); the ones marked "added"
    /// are not in the spec's table.</summary>
    public static class MarkdownDiagnosticIds
    {
        public const string UnclosedMark = "fwmd.syntax.unclosed-mark";
        public const string VersionUnsupported = "fwmd.version.unsupported";
        /// <summary>Reported by hosts: unmarked text that looks like it is vernacular (a suggestion).</summary>
        public const string WsUnmarkedVernacular = "fwmd.ws.unmarked-vernacular";
        /// <summary>The URL of a back-link names one GUID and <c>guid=</c> another.</summary>
        public const string LinkGuidMismatch = "fwmd.link.guid-mismatch";
        /// <summary>A file-relative link with no base to resolve it against (<see cref="MarkdownOptions.LinkBase"/>).</summary>
        public const string LinkRelativeWithoutBase = "fwmd.link.relative-without-base";
        /// <summary>The GUID resolves but the object is now at another path (information); the next export writes the current path.</summary>
        public const string LinkStalePath = "fwmd.link.stale-path";
        /// <summary>The GUID is unknown here and the link was bound to the one object at its path (information).</summary>
        public const string LinkRebound = "fwmd.link.rebound";
        /// <summary>A link with a stored <c>url=</c> was retargeted, so the URL was rebuilt (information).</summary>
        public const string LinkUrlRebuilt = "fwmd.link.url-rebuilt";
        public const string UnknownAttribute = "fwmd.syntax.unknown-attribute";
        public const string DuplicateAttribute = "fwmd.syntax.duplicate-attribute";
        public const string UnsupportedMarkup = "fwmd.syntax.unsupported-markup";
        /// <summary>Added: an attribute block that breaks the grammar of spec 3.3 (an empty block, a bad character or escape), a value that does not fit its key, or text after the block of a ::: line. A key written as a flag is <see cref="UnknownAttribute"/>, and v with ws is <see cref="DuplicateAttribute"/>; the spec has no id for the rest.</summary>
        public const string MalformedAttribute = "fwmd.syntax.malformed-attribute";
        /// <summary>Added: a block, line or table too deeply nested or too large for the parser (Markdig's limit). Its lines are kept as plain text.</summary>
        public const string TooDeep = "fwmd.syntax.too-deep";
        public const string WsUnknown = "fwmd.ws.unknown";
        public const string LinkNotFound = "fwmd.link.not-found";
        /// <summary>Reported by hosts (through <see cref="MarkdownProblem"/>): a link path matches more than one target.</summary>
        public const string LinkAmbiguous = "fwmd.link.ambiguous";
        /// <summary>Reported by hosts (through <see cref="MarkdownProblem"/>): the link's target was deleted; a warning.</summary>
        public const string LinkBroken = "fwmd.link.broken";
        public const string StyleUnknown = "fwmd.style.unknown";
        public const string HeadingTooDeep = "fwmd.heading.too-deep";
        public const string TableRagged = "fwmd.table.ragged";
        public const string TableTabInCell = "fwmd.table.tab-in-cell";
        public const string TableAlignment = "fwmd.table.alignment";
        public const string PictureUnknown = "fwmd.picture.unknown";
        public const string FieldBlockSyntax = "fwmd.field.block-syntax";
        public const string PropUnknownNumber = "fwmd.prop.unknown-number";
        /// <summary>Added (owner decision R-9, ADR 0032): the front matter has a top-level <c>record:</c> key, so the text is a record file, which
        /// <c>RecordFileReader</c> reads. One error on that line; nothing else is read, so a host that writes the result back cannot drop the frame.</summary>
        public const string RecordFile = "fwmd.record.record-file";
    }

    /// <summary>Raised by the writer when a paragraph cannot be written so that it reads back unchanged. It should not happen for
    /// text; the usual cause is a host whose <see cref="IObjectMapper"/> does not invert itself.</summary>
    public sealed class MarkdownWriteException : InvalidOperationException
    {
        public MarkdownWriteException(string message) : base(message) { }
    }

    /// <summary>Where a paragraph was read from: the range of its source (end exclusive) and the number of the Markdown block it belongs to, which the paragraphs of one
    /// table or one list share.</summary>
    internal readonly struct ParagraphSource
    {
        public ParagraphSource(int start, int end, int block)
        {
            Start = start;
            End = end;
            Block = block;
        }

        public int Start { get; }
        public int End { get; }
        public int Block { get; }
    }
}
