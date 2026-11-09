using System;
using System.Collections.Generic;

namespace SIL.TextEditing.Markdown
{
    /// <summary>A host-registered FieldWorks Markdown clipboard form (ADR 0029). Rich payloads include front matter and
    /// preserve stored text; the target editor owns writing-system mapping, normalization and block flattening.</summary>
    public sealed class FieldWorksMarkdownClipboardForm : IClipboardForm
    {
        /// <summary>The custom clipboard format holding a complete FieldWorks Markdown string.</summary>
        public const string ClipboardFormatName = "SIL.TextEditing.FieldWorksMarkdown";

        public FieldWorksMarkdownClipboardForm(MarkdownOptions? options = null)
        {
            Options = options ?? new MarkdownOptions();
        }

        /// <summary>The host's writing systems, styles and object resolvers. Each operation copies these options and forces
        /// front matter on and text normalization off, without modifying the host's options.</summary>
        public MarkdownOptions Options { get; }

        /// <summary>Receives each parsed text payload, including findings for unknown writing systems and styles. These findings
        /// do not reject a recovered fragment; unsupported or incomplete front matter still makes <see cref="Read"/> return null.</summary>
        public Action<MarkdownParseResult>? OnRead { get; set; }

        public string Id => ClipboardForms.FieldWorksMarkdownId;
        public string DisplayName => "FieldWorks Markdown";
        public string FormatName => ClipboardFormatName;
        public ClipboardPayloadKind PayloadKind => ClipboardPayloadKind.Text;
        public bool OffersCopyAs => true;

        public object? Write(IReadOnlyList<Paragraph> fragment)
        {
            if (fragment.Count == 0) return null;
            return MarkdownConverter.ToMarkdown(fragment, ClipboardOptions());
        }

        public IReadOnlyList<Paragraph>? Read(object payload)
        {
            string? text = ClipboardPayload.AsText(payload);
            if (text == null) return null;
            var result = MarkdownConverter.Parse(text, ClipboardOptions());
            OnRead?.Invoke(result);
            var frontMatter = result.FrontMatter;
            if (frontMatter == null || frontMatter.Version != MarkdownFrontMatter.CurrentVersion
                || string.IsNullOrEmpty(frontMatter.Analysis) || string.IsNullOrEmpty(frontMatter.Vernacular)) return null;
            return result.Document.Paragraphs;
        }

        MarkdownOptions ClipboardOptions()
        {
            var options = Options.Copy();
            options.WriteFrontMatter = true;
            options.NormalizeText = false;
            return options;
        }
    }
}
