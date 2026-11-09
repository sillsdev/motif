using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SIL.TextEditing.Markdown.Tests.Conformance;
using Xunit;

namespace SIL.TextEditing.Markdown.Tests
{
    public class ClipboardFormTests
    {
        [Fact]
        public void The_form_is_text_and_offers_Copy_as_under_the_standard_id()
        {
            var form = new FieldWorksMarkdownClipboardForm();
            Assert.Equal(ClipboardForms.FieldWorksMarkdownId, form.Id);
            Assert.Equal("FieldWorks Markdown", form.DisplayName);
            Assert.Equal(FieldWorksMarkdownClipboardForm.ClipboardFormatName, form.FormatName);
            Assert.Equal(ClipboardPayloadKind.Text, form.PayloadKind);
            Assert.True(form.OffersCopyAs);
            Assert.Null(form.Write(Array.Empty<Paragraph>()));
        }

        internal static Paragraph[] Fragment()
        {
            var first = new Paragraph { Style = "Heading 1", DefaultWs = "en", Key = "source" };
            first.Runs.Add(new Run { Ws = "en", Text = "café " });
            var styled = new Run { Ws = "swh", Text = "rafiki" };
            styled.Props.SetStr(TextPropertyNumbers.NamedStyle, "Emphasis");
            first.Runs.Add(styled);
            var second = new Paragraph { Style = "Normal", DefaultWs = "ar", Rtl = true };
            second.Runs.Add(new Run { Ws = "ar", Text = "مرحبا " });
            var strong = new Run { Ws = "en", Text = "cafe\u0301" };
            strong.Props.SetStr(TextPropertyNumbers.NamedStyle, "Strong");
            second.Runs.Add(strong);
            return new[] { first, second };
        }

        [Fact]
        public void A_round_trip_keeps_paragraphs_styles_properties_and_stored_normalization_without_keys()
        {
            var options = new MarkdownOptions { WriteFrontMatter = false, NormalizeText = true,
                WritingSystems = new FixedWritingSystems("en", "swh") };
            var source = Fragment();
            var form = new FieldWorksMarkdownClipboardForm(options);
            string text = Assert.IsType<string>(form.Write(source));
            Assert.StartsWith("---\nfwmd: 1\n", text);
            var target = new FieldWorksMarkdownClipboardForm(new MarkdownOptions
                { WritingSystems = new FixedWritingSystems("fr", "de") });
            var read = target.Read(text);
            Assert.NotNull(read);
            Assert.True(MarkdownConverter.AreEquivalent(source, read!));
            Assert.All(read!, p => Assert.Null(p.Key));
            Assert.NotSame(source[0], read![0]);
            Assert.NotSame(source[0].Runs[0], read[0].Runs[0]);
            Assert.Contains("café", read[0].Text);
            Assert.Contains("cafe\u0301", read[1].Text);
            Assert.False(options.WriteFrontMatter);
            Assert.True(options.NormalizeText);
            Assert.Equal("source", source[0].Key);
        }

        [Fact]
        public void Unknown_writing_systems_and_styles_return_findings_and_keep_their_names()
        {
            var p = new Paragraph { DefaultWs = "qqq", Style = "Missing paragraph" };
            var r = new Run { Ws = "qqq", Text = "foreign" };
            r.Props.SetStr(TextPropertyNumbers.NamedStyle, "Missing character");
            p.Runs.Add(r);
            string text = Assert.IsType<string>(new FieldWorksMarkdownClipboardForm().Write(new[] { p }));
            MarkdownParseResult? observed = null;
            var form = new FieldWorksMarkdownClipboardForm(new MarkdownOptions
                { WritingSystems = new KnownWritingSystems(), Styles = new KnownStyles() })
                { OnRead = result => observed = result };
            var read = form.Read(text);
            Assert.NotNull(read);
            Assert.Equal("qqq", read![0].Runs[0].Ws);
            Assert.Equal("Missing paragraph", read[0].Style);
            Assert.Equal("Missing character", read[0].Runs[0].Props.StrProps[TextPropertyNumbers.NamedStyle]);
            Assert.NotNull(observed);
            Assert.Contains(observed!.Diagnostics, d => d.Id == "fwmd.ws.unknown");
            Assert.Contains(observed.Diagnostics, d => d.Id == "fwmd.style.unknown");
        }

        [Theory]
        [InlineData("plain text")]
        [InlineData("---\nfwmd: 2\nanalysis: en\nvernacular: swh\n---\ntext")]
        [InlineData("---\nfwmd: 1\nanalysis: en\n---\ntext")]
        [InlineData("---\nfwmd: 1\nanalysis: en\nvernacular: swh\nrecord: entry\n---\ntext")]
        public void Non_fragment_text_is_declined_for_the_next_clipboard_form(string text)
            => Assert.Null(new FieldWorksMarkdownClipboardForm().Read(text));

        [Fact]
        public void Bytes_and_host_objects_are_not_guessed_as_text()
        {
            var form = new FieldWorksMarkdownClipboardForm();
            Assert.Null(form.Read(Encoding.UTF8.GetBytes("text")));
            Assert.Null(form.Read(new object()));
        }

        [Fact]
        public void A_rejected_version_still_reports_its_parser_finding()
        {
            MarkdownParseResult? observed = null;
            var form = new FieldWorksMarkdownClipboardForm { OnRead = result => observed = result };
            Assert.Null(form.Read("---\nfwmd: 999\nanalysis: en\nvernacular: en\n---\ntext"));
            Assert.NotNull(observed);
            Assert.Contains(observed!.Diagnostics, d => d.Id == MarkdownDiagnosticIds.VersionUnsupported);
        }

        public static IEnumerable<object[]> ConformanceCases() => ConformanceLoader.All()
            .Where(c => !c.Inline && !c.HasErrors && c.Paragraphs != null && c.Paragraphs.Count > 0
                && (!c.CanonicalSpecified || c.Canonical != null))
            .Select(c => new object[] { c.Id });

        [Theory]
        [MemberData(nameof(ConformanceCases))]
        public void Conforming_models_round_trip_through_the_clipboard(string id)
        {
            var c = ConformanceLoader.Find(id);
            var form = new FieldWorksMarkdownClipboardForm(ConformanceTests.OptionsFor(c, out _));
            string payload = Assert.IsType<string>(form.Write(c.Paragraphs!));
            var read = form.Read(payload);
            Assert.NotNull(read);
            Assert.True(MarkdownConverter.AreEquivalent(c.Paragraphs!, read!));
        }

        sealed class KnownWritingSystems : IWritingSystemMapper
        {
            public string DefaultAnalysis => "en";
            public string DefaultVernacular => "en";
            public bool IsKnown(string tag) => tag == "en";
        }

        sealed class KnownStyles : IStyleCatalog
        {
            public bool IsParagraphStyle(string name) => name == "Normal";
            public bool IsCharacterStyle(string name) => name == "Emphasis";
        }
    }
}
