using System;
using System.Collections.Generic;
using System.Linq;

namespace SIL.TextEditing.Markdown
{
    /// <summary>FieldWorks Markdown as a field's source form (ADR 0035): a field in source mode shows and edits its text as the body of a FieldWorks Markdown
    /// file (no front matter: the host's options name the writing systems), and leaving reads it with the same reader and checker as the Markdown clipboard
    /// form and the record reader. A field of one paragraph uses the inline grammar, as a one-paragraph field of a record file does, so block syntax there is
    /// <c>fwmd.field.block-syntax</c>. A link's or picture's attributes are reported as <see cref="SourceTokenKinds.Muted"/> tokens when they carry a GUID.</summary>
    public sealed class FieldWorksMarkdownSourceForm : ISourceForm
    {
        public FieldWorksMarkdownSourceForm(MarkdownOptions? options = null)
        {
            Options = options ?? new MarkdownOptions();
        }

        /// <summary>The host's writing systems, styles and object resolvers. Each operation copies them with front matter off; the host's options are not changed.</summary>
        public MarkdownOptions Options { get; }

        public string Name => "Markdown";

        public string? Write(IReadOnlyList<Paragraph> paragraphs, bool singleParagraph)
        {
            if (paragraphs == null) throw new ArgumentNullException(nameof(paragraphs));
            try
            {
                var text = singleParagraph && paragraphs.Count == 1
                    ? MarkdownConverter.ToInlineMarkdown(paragraphs[0].Runs, SourceOptions())
                    : MarkdownConverter.ToMarkdown(paragraphs, SourceOptions());
                return text.TrimEnd('\n');
            }
            catch (MarkdownWriteException)
            {
                return null;
            }
        }

        public SourceReading Read(string source, bool singleParagraph)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            IReadOnlyList<Paragraph> paragraphs;
            IReadOnlyList<MarkdownDiagnostic> findings;
            IReadOnlyList<MarkdownObject> objects;
            string text;
            if (singleParagraph)
            {
                var inline = MarkdownConverter.ParseInline(source, SourceOptions());
                var paragraph = new Paragraph { DefaultWs = inline.Runs.FirstOrDefault()?.Ws ?? Options.WritingSystems.DefaultAnalysis };
                foreach (var run in inline.Runs) paragraph.Runs.Add(run);
                paragraphs = new[] { paragraph };
                findings = inline.Diagnostics;
                objects = inline.Objects;
                text = inline.Source;
            }
            else
            {
                var result = MarkdownConverter.Parse(source, SourceOptions());
                paragraphs = result.Document.Paragraphs;
                findings = result.Diagnostics;
                objects = result.Objects;
                text = result.Source;
            }
            var diagnostics = findings.Select(f => ToLanguage(text, f)).ToList();
            var tokens = new List<LanguageToken>();
            foreach (var o in objects)
            {
                if (o.Guid == null || o.Source == null) continue;
                var (start, end) = AttributesOf(text, o.Source.Start, o.Source.End);
                if (end > start) tokens.Add(new LanguageToken(Range(text, start, end), SourceTokenKinds.Muted));
            }
            return new SourceReading(paragraphs, diagnostics, tokens);
        }

        public bool AreEquivalent(Paragraph original, Paragraph read) => MarkdownConverter.AreEquivalent(original, read);

        MarkdownOptions SourceOptions()
        {
            var options = Options.Copy();
            options.WriteFrontMatter = false;
            return options;
        }

        static LanguageRange Range(string text, int start, int end) =>
            new LanguageRange(LanguageText.PositionOf(text, start), LanguageText.PositionOf(text, end));

        static LanguageDiagnostic ToLanguage(string text, MarkdownDiagnostic finding)
        {
            var range = Range(text, finding.Start, finding.End);
            var fix = finding.Replacement == null ? null
                : new LanguageFix("Replace with " + finding.Replacement, new[] { new LanguageTextEdit(range, finding.Replacement) });
            return new LanguageDiagnostic(range, Severity(finding.Severity), finding.Message, finding.Id, "fwmd", fix);
        }

        static DiagnosticSeverity Severity(MarkdownSeverity severity)
        {
            switch (severity)
            {
                case MarkdownSeverity.Error: return DiagnosticSeverity.Error;
                case MarkdownSeverity.Warning: return DiagnosticSeverity.Warning;
                default: return DiagnosticSeverity.Information;
            }
        }

        /// <summary>The attribute block (<c>{…}</c>) that ends an object's notation in [start, end), or an empty range when there is none.</summary>
        static (int Start, int End) AttributesOf(string text, int start, int end)
        {
            end = Math.Min(end, text.Length);
            if (end <= start || text[end - 1] != '}') return (0, 0);
            bool quoted = false;
            for (int i = end - 2; i >= start; i--)
            {
                char c = text[i];
                if (c == '"' && (i == start || text[i - 1] != '\\')) quoted = !quoted;
                else if (c == '{' && !quoted) return (i, end);
            }
            return (0, 0);
        }
    }
}
