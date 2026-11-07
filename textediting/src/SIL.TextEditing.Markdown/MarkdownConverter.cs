using System;
using System.Collections.Generic;
using System.Linq;
using SIL.TextEditing.Markdown.Reader;
using SIL.TextEditing.Markdown.Writer;

namespace SIL.TextEditing.Markdown
{
    /// <summary>The result of reading one inline-only text.</summary>
    public sealed class MarkdownInlineResult
    {
        readonly ReaderContext _context;

        internal MarkdownInlineResult(IReadOnlyList<Run> runs, IReadOnlyList<MarkdownDiagnostic> diagnostics, ReaderContext context)
        {
            Runs = runs;
            Diagnostics = diagnostics;
            _context = context;
        }

        public IReadOnlyList<Run> Runs { get; }
        public IReadOnlyList<MarkdownDiagnostic> Diagnostics { get; }
        public bool HasErrors => Diagnostics.Any(d => d.Severity == MarkdownSeverity.Error);

        /// <summary>The text the offsets, lines and columns refer to.</summary>
        public string Source => _context.Source;

        /// <summary>Every object the parser built from the notation, in source order, each with its place in the text.</summary>
        public IReadOnlyList<MarkdownObject> Objects => _context.Objects;

        /// <summary>Makes a finding for a range of <see cref="Source"/>, with its line, column and observed text filled in. It is not added to
        /// <see cref="Diagnostics"/>.</summary>
        public MarkdownDiagnostic CreateDiagnostic(string id, MarkdownSeverity severity, string message, int start, int end,
            string? replacement = null, IReadOnlyList<string>? choices = null, IReadOnlyList<string>? nearest = null)
            => _context.CreateDiagnostic(id, severity, message, start, end, replacement, choices, nearest);
    }

    /// <summary>Converts between the run model and FieldWorks Markdown, a strict profile of Pandoc Markdown (ADRs 0011 and 0028; the notation is Motif's spec 2026-11-03-gdr-markdown-design, revised on 2026-11-04, and the conformance fixtures in tests/conformance).
    /// Writing then reading gives back the same paragraphs, and reading then writing is stable: the writer is a function of the model
    /// alone, and it checks every paragraph it writes by reading it back.</summary>
    public static class MarkdownConverter
    {
        /// <summary>Writes the document as canonical FieldWorks Markdown: the front matter (unless <see cref="MarkdownOptions.WriteFrontMatter"/> is false),
        /// one line per paragraph, one blank line between blocks, <c>\n</c> line ends, the escapes of the table in spec 3.4 and no others, the attribute
        /// order of spec 3.3, and links in the form <see cref="MarkdownOptions.LinkForm"/> asks for. The text is as stored; nothing is normalised.</summary>
        /// <exception cref="MarkdownWriteException">A paragraph cannot be written so that it reads back unchanged.</exception>
        public static string ToMarkdown(Document document, MarkdownOptions? options = null)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            return ToMarkdown(document.Paragraphs, options);
        }

        public static string ToMarkdown(IEnumerable<Paragraph> paragraphs, MarkdownOptions? options = null)
        {
            if (paragraphs == null) throw new ArgumentNullException(nameof(paragraphs));
            return MarkdownWriter.Write(paragraphs, options ?? new MarkdownOptions());
        }

        /// <summary>Reads Markdown into a document and the findings the parser makes. Paragraph keys are null. The text of every run is
        /// normalised to NFD, the form the editor stores (ADR 0018), whatever form the Markdown was written in, unless <see cref="MarkdownOptions.NormalizeText"/> is false (attribute values are never normalised); the offsets, lines and columns of
        /// the findings and objects still refer to the text as written (<see cref="MarkdownParseResult.Source"/>).</summary>
        public static MarkdownParseResult Parse(string markdown, MarkdownOptions? options = null)
        {
            if (markdown == null) throw new ArgumentNullException(nameof(markdown));
            return MarkdownReader.Read(markdown, options ?? new MarkdownOptions());
        }

        /// <summary>Reads Markdown and writes it again: the canonical form of the text, when it has no errors.</summary>
        public static string Canonicalize(string markdown, MarkdownOptions? options = null)
        {
            options = options ?? new MarkdownOptions();
            return ToMarkdown(Parse(markdown, options).Document, options);
        }

        /// <summary>Writes the runs of one string with the inline grammar only (a multi-string field such as the abstract, spec 5.1).</summary>
        public static string ToInlineMarkdown(IEnumerable<Run> runs, MarkdownOptions? options = null)
        {
            if (runs == null) throw new ArgumentNullException(nameof(runs));
            return MarkdownWriter.WriteInline(runs, options ?? new MarkdownOptions());
        }

        /// <summary>Reads text that may use the inline grammar only; block syntax is reported as <c>fwmd.field.block-syntax</c>. Run text is
        /// NFD, as for <see cref="Parse"/>, and <see cref="MarkdownInlineResult.Source"/> is the text as written.</summary>
        public static MarkdownInlineResult ParseInline(string markdown, MarkdownOptions? options = null)
        {
            if (markdown == null) throw new ArgumentNullException(nameof(markdown));
            var runs = MarkdownReader.ReadInline(markdown, options ?? new MarkdownOptions(), out var context);
            return new MarkdownInlineResult(runs, context.Diagnostics, context);
        }

        /// <summary>True when two paragraphs say the same as far as Markdown carries: after empty runs are dropped and neighbours of one
        /// format are merged, the same style, other paragraph properties (direction included), runs (text, writing system, properties,
        /// object data) and, for an empty paragraph, typing writing system. Paragraph keys are not compared; a non-empty paragraph's
        /// <see cref="Paragraph.DefaultWs"/> is taken to be its first run's, as the LCModel adapter sets it.</summary>
        public static bool AreEquivalent(Paragraph a, Paragraph b)
        {
            if (a == null) throw new ArgumentNullException(nameof(a));
            if (b == null) throw new ArgumentNullException(nameof(b));
            return Equivalence.Equal(a, b);
        }

        /// <summary>True when the two lists are equivalent paragraph by paragraph (<see cref="AreEquivalent(Paragraph, Paragraph)"/>).</summary>
        public static bool AreEquivalent(IReadOnlyList<Paragraph> a, IReadOnlyList<Paragraph> b)
        {
            if (a == null) throw new ArgumentNullException(nameof(a));
            if (b == null) throw new ArgumentNullException(nameof(b));
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++) if (!Equivalence.Equal(a[i], b[i])) return false;
            return true;
        }
    }
}
