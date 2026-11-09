using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using SIL.TextEditing.Avalonia;
using SIL.TextEditing.Markdown;
using Xunit;

namespace SIL.TextEditing.Playground.Tests
{
    /// <summary>ADR 0029 in the playground: a Copy as submenu, and a notice when pasted text names a writing system the project lacks.</summary>
    public class ClipboardPlaygroundTests
    {
        static Paragraph[] Fragment()
        {
            var first = new Paragraph { DefaultWs = "en" };
            first.Runs.Add(new Run { Ws = "en", Text = "cafe\u0301 " });
            var styled = new Run { Ws = "swh", Text = "rafiki" };
            styled.Props.SetStr(TextPropertyNumbers.NamedStyle, "Emphasis");
            first.Runs.Add(styled);
            var second = new Paragraph { Style = "Heading 1", DefaultWs = "ar" };
            var strong = new Run { Ws = "ar", Text = "مرحبا" };
            strong.Props.SetStr(TextPropertyNumbers.NamedStyle, "Strong");
            second.Runs.Add(strong);
            return new[] { first, second };
        }

        static RichTextField Field(IEnumerable<Paragraph> paragraphs, MemoryClipboard clipboard)
        {
            var doc = new Document();
            doc.Paragraphs.AddRange(paragraphs);
            var field = new RichTextField(doc) { ClipboardBackend = clipboard };
            field.ClipboardForms.Add(new FieldWorksMarkdownClipboardForm(new MarkdownOptions
                { StyleNames = new MarkdownStyleNames { Normal = string.Empty } }));
            return field;
        }

        static void SelectAll(RichTextField field)
        {
            var paragraphs = field.Editor.Document.Paragraphs;
            field.Editor.Anchor = new TextPosition(0, 0);
            field.Editor.SetCaret(new TextPosition(paragraphs.Count - 1, paragraphs[paragraphs.Count - 1].Length), true);
        }

        [Fact]
        public void Copy_between_fields_keeps_paragraphs_writing_systems_and_character_styles() => Headless.Run(async () =>
        {
            var clipboard = new MemoryClipboard();
            var fragment = Fragment();
            var source = Field(fragment, clipboard);
            SelectAll(source);
            await source.CopyAsync(false);
            Assert.Contains(FieldWorksMarkdownClipboardForm.ClipboardFormatName, clipboard.Forms.Keys);
            Assert.Equal("café rafiki\nمرحبا", clipboard.PlainText);
            var target = Field(new[] { new Paragraph() }, clipboard);
            await target.PasteAsync();
            Assert.True(MarkdownConverter.AreEquivalent(fragment, target.Editor.Document.Paragraphs));
        });

        [Fact]
        public void A_single_paragraph_target_joins_the_Markdown_paragraphs_with_a_space() => Headless.Run(async () =>
        {
            var clipboard = new MemoryClipboard();
            var source = Field(Fragment(), clipboard);
            SelectAll(source);
            await source.CopyAsync(false);
            var target = Field(new[] { new Paragraph() }, clipboard);
            target.SingleParagraph = true;
            await target.PasteAsync();
            var p = Assert.Single(target.Editor.Document.Paragraphs);
            Assert.Equal("cafe\u0301 rafiki مرحبا", p.Text);
            Assert.Contains(p.Runs, r => r.Ws == "swh" && r.Props.StrProps[TextPropertyNumbers.NamedStyle] == "Emphasis");
        });

        [Fact]
        public void A_target_with_headings_off_flattens_Markdown_headings_and_reports_it() => Headless.Run(async () =>
        {
            var clipboard = new MemoryClipboard();
            var source = Field(Fragment(), clipboard);
            SelectAll(source);
            await source.CopyAsync(false);
            var target = Field(new[] { new Paragraph() }, clipboard);
            target.BlockFeatures = BlockFeatures.None;
            target.BlockRecognizer = new MarkdownStyleNames { Normal = string.Empty }.ToBlockRecognizer();
            int notices = 0;
            target.BlocksFlattened += (sender, flattened) => notices++;
            await target.PasteAsync();
            Assert.Equal(2, target.Editor.Document.Paragraphs.Count);
            Assert.All(target.Editor.Document.Paragraphs, p => Assert.Equal(string.Empty, p.Style));
            Assert.Equal("مرحبا", target.Editor.Document.Paragraphs[1].Text);
            Assert.Equal(1, notices);
        });

        [Fact]
        public void Copy_as_Markdown_puts_the_complete_payload_in_plain_text_only() => Headless.Run(async () =>
        {
            var clipboard = new MemoryClipboard();
            var source = Field(Fragment(), clipboard);
            SelectAll(source);
            Assert.True(await source.CopyAsAsync(ClipboardForms.FieldWorksMarkdownId));
            Assert.StartsWith("---\nfwmd: 1\n", clipboard.PlainText);
            Assert.Empty(clipboard.Forms);
        });

        [Fact]
        public void An_unsupported_Markdown_payload_falls_back_to_plain_text() => Headless.Run(async () =>
        {
            var clipboard = new MemoryClipboard { PlainText = "fallback" };
            clipboard.Forms[FieldWorksMarkdownClipboardForm.ClipboardFormatName] =
                "---\nfwmd: 999\nanalysis: en\nvernacular: en\n---\nwrong";
            var target = Field(new[] { new Paragraph() }, clipboard);
            await target.PasteAsync();
            Assert.Equal("fallback", Assert.Single(target.Editor.Document.Paragraphs).Text);
        });

        sealed class MemoryClipboard : IClipboardBackend
        {
            public string PlainText = string.Empty;
            public readonly Dictionary<string, object> Forms = new Dictionary<string, object>();
            public Task<IReadOnlyList<string>> SetAsync(string plainText, IReadOnlyList<KeyValuePair<string, object>> forms)
            {
                PlainText = plainText;
                Forms.Clear();
                foreach (var pair in forms) Forms[pair.Key] = pair.Value;
                return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
            }
            public Task<object?> GetAsync(string formatName, ClipboardPayloadKind kind)
                => Task.FromResult(Forms.TryGetValue(formatName, out var value) ? value : null);
            public Task<string?> GetTextAsync() => Task.FromResult<string?>(PlainText);
        }

        [Fact]
        public void The_context_menu_has_a_Copy_as_submenu_with_plain_text_and_the_registered_forms() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.FindReplace))
            {
                s.Select(2, 0, 5);
                s.View.CloseContextMenu();
                s.RightClick(s.PointAt(2, 2));
                Assert.Contains(PlaygroundView.CopyAsCaption, s.View.LastContextMenuItems);
                Assert.Equal("Plain text", s.View.LastCopyAsItems[0]);
                Assert.Contains("FieldWorks Markdown", s.View.LastCopyAsItems);
                Assert.DoesNotContain("Runs (debug dump)", s.View.LastCopyAsItems);
            }
        });

        [Fact]
        public void Copy_as_plain_text_puts_only_the_text_on_the_clipboard() => Headless.Run(async () =>
        {
            using (var s = new Session(Scenarios.FindReplace))
            {
                s.Select(2, 0, 5);
                await s.View.CopyAsAsync(ClipboardForms.PlainTextId);
                Assert.Contains("clipboard: copied as " + ClipboardForms.PlainTextId, s.Log);
                var top = TopLevel.GetTopLevel(s.Field);
                Assert.Equal(s.ParagraphText(2).Substring(0, 5), await global::SIL.TextEditing.Avalonia.ClipboardCompat.GetTextAsync(top));
                Assert.Null(await global::SIL.TextEditing.Avalonia.ClipboardCompat.GetStringAsync(top, FieldWorksMarkdownClipboardForm.ClipboardFormatName));
            }
        });

        [Fact]
        public void Pasting_text_in_a_writing_system_the_project_lacks_logs_the_notice() => Headless.Run(async () =>
        {
            using (var s = new Session(Scenarios.FindReplace))
            {
                var p = new Paragraph { DefaultWs = "qqq" };
                p.Runs.Add(new Run { Ws = "qqq", Text = "stranger" });
                var top = TopLevel.GetTopLevel(s.Field);
                await global::SIL.TextEditing.Avalonia.ClipboardCompat.SetAsync(top, "stranger",
                    new[] { new System.Collections.Generic.KeyValuePair<string, object>(FieldWorksMarkdownClipboardForm.ClipboardFormatName, s.Field.ClipboardForms.Find(ClipboardForms.FieldWorksMarkdownId)!.Write(new[] { p })!) });
                s.Caret(0, 0);
                s.Click(PlaygroundView.Names.Paste);
                await s.WaitClipboard();
                Assert.Contains("fwmd.ws.unknown", s.Log);
                Assert.Contains("clipboard: notice, writing system(s) qqq", s.Log);
                Assert.All(s.Editor.Document.Paragraphs[0].Runs.Take(1), r => Assert.NotEqual("qqq", r.Ws));
            }
        });

        [Fact]
        public void Pasting_an_unknown_character_style_keeps_the_name_and_logs_the_finding() => Headless.Run(async () =>
        {
            using (var s = new Session(Scenarios.FindReplace))
            {
                var p = new Paragraph { DefaultWs = "en" };
                var r = new Run { Ws = "en", Text = "styled" };
                r.Props.SetStr(TextPropertyNumbers.NamedStyle, "Missing character");
                p.Runs.Add(r);
                var form = s.Field.ClipboardForms.Find(ClipboardForms.FieldWorksMarkdownId)!;
                await global::SIL.TextEditing.Avalonia.ClipboardCompat.SetAsync(TopLevel.GetTopLevel(s.Field), "styled",
                    new[] { new KeyValuePair<string, object>(form.FormatName, form.Write(new[] { p })!) });
                s.Caret(0, 0);
                s.Click(PlaygroundView.Names.Paste);
                await s.WaitClipboard();
                Assert.Contains("fwmd.style.unknown", s.Log);
                Assert.Equal("Missing character", s.Editor.Document.Paragraphs[0].Runs[0].Props.StrProps[TextPropertyNumbers.NamedStyle]);
            }
        });
    }
}
