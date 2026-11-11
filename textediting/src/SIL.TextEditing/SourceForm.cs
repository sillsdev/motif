using System;
using System.Collections.Generic;
using System.Linq;

namespace SIL.TextEditing
{
    /// <summary>A text form a rich field can be shown and edited in instead of rich text: source mode (ADR 0035). FieldWorks Markdown's is
    /// <c>FieldWorksMarkdownSourceForm</c> in SIL.TextEditing.Markdown; the host gives it the same options (writing systems, styles, objects) as its
    /// Markdown clipboard form, so leaving source mode reads the text with the reader and checker paste uses.</summary>
    public interface ISourceForm
    {
        /// <summary>The form's name as people see it: "Markdown" gives "Editing as Markdown" and the undo step "Edit as Markdown".</summary>
        string Name { get; }

        /// <summary>The source of a field's paragraphs, one line per line of the form, or null when they cannot be written so that they read back as they
        /// are (the field then stays in rich mode). <paramref name="singleParagraph"/> is true for a field of one paragraph, whose source has no paragraph
        /// syntax (the inline grammar).</summary>
        string? Write(IReadOnlyList<Paragraph> paragraphs, bool singleParagraph);

        /// <summary>Reads source back: the paragraphs it says, the findings about it (error, warning, information or hint, positioned as the language
        /// service's are) and the tokens a source view draws (<see cref="SourceTokenKinds"/>). The paragraphs are used only when no finding is an error.</summary>
        SourceReading Read(string source, bool singleParagraph);

        /// <summary>True when a paragraph read back says the same as the field's paragraph as far as the form carries, so leaving source mode leaves it
        /// alone (its identity, and anything the form does not carry, stay).</summary>
        bool AreEquivalent(Paragraph original, Paragraph read);
    }

    /// <summary>A source form that can tell cheaply that two paragraphs are not equivalent, so a return from source mode compares a long text's paragraphs
    /// in a few steps each rather than pair by pair (ADR 0035). Optional: a form without it is compared pair by pair, up to
    /// <see cref="DocumentEditor.PairwiseComparisonBudget"/> comparisons, and beyond that gives keys by position.</summary>
    public interface ISourceFormFingerprint
    {
        /// <summary>A string that is the same for any two paragraphs <see cref="ISourceForm.AreEquivalent"/> accepts; paragraphs that are not equivalent
        /// may share it. A fingerprint that differs for two equivalent paragraphs would give a paragraph's key to other text, so when unsure, say less.</summary>
        string Fingerprint(Paragraph paragraph);
    }

    /// <summary>What <see cref="ISourceForm.Read"/> found.</summary>
    public sealed class SourceReading
    {
        public SourceReading(IReadOnlyList<Paragraph> paragraphs, IReadOnlyList<LanguageDiagnostic> diagnostics, IReadOnlyList<LanguageToken>? tokens = null)
        {
            Paragraphs = paragraphs ?? throw new ArgumentNullException(nameof(paragraphs));
            Diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
            Tokens = tokens ?? new LanguageToken[0];
        }

        public IReadOnlyList<Paragraph> Paragraphs { get; }
        public IReadOnlyList<LanguageDiagnostic> Diagnostics { get; }
        public IReadOnlyList<LanguageToken> Tokens { get; }

        /// <summary>True when the source cannot become the field's text: the field stays in source mode (ADR 0035). Warnings never block.</summary>
        public bool HasErrors => Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);
    }

    /// <summary>The token kinds a source view draws.</summary>
    public static class SourceTokenKinds
    {
        /// <summary>Text that stays in the source so nothing is hidden but is drawn muted: a link's <c>{guid=…}</c> attributes (ADR 0035, decision 3).</summary>
        public const string Muted = "muted";
    }

    /// <summary>The language service of a source view: every analysis is one <see cref="ISourceForm.Read"/>, so the findings shown are exactly the ones that
    /// decide whether the field can leave source mode.</summary>
    public sealed class SourceFormLanguageService : TextLanguageService
    {
        readonly ISourceForm _form;
        readonly bool _singleParagraph;

        public SourceFormLanguageService(ISourceForm form, bool singleParagraph)
        {
            _form = form ?? throw new ArgumentNullException(nameof(form));
            _singleParagraph = singleParagraph;
        }

        protected override LanguageAnalysis Analyze(string text)
        {
            var reading = _form.Read(text, _singleParagraph);
            return new LanguageAnalysis(reading.Tokens, reading.Diagnostics);
        }
    }
}
