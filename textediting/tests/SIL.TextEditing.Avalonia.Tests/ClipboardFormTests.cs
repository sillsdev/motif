using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using SIL.TextEditing.Tests;
using Xunit;
using static SIL.TextEditing.Tests.TestDocs;

namespace SIL.TextEditing.Avalonia.Tests
{
    /// <summary>Clipboard forms the host registers (ADR 0029): copy writes all of them with NFC plain text, paste prefers them in a
    /// configurable order, and writing systems the target lacks map to the writing system of the selection.</summary>
    public class ClipboardFormTests
    {
        const string Rich = "rich", Md = "md";

        static (Window Window, RichTextField Field, TopLevel Top) Shown(Document doc, params IClipboardForm[] forms)
        {
            var (window, field) = Fixtures.InWindow(doc);
            foreach (var f in forms)
            {
                field.ClipboardForms.Add(f);
                if (!field.ClipboardForms.PasteOrder.Contains(f.Id)) field.ClipboardForms.PasteOrder.Insert(0, f.Id);   // a form paste reads is one the host listed
            }
            return (window, field, TopLevel.GetTopLevel(field)!);
        }

        static void Select(RichTextField field, int from, int to)
        {
            field.Editor.Anchor = new TextPosition(0, from);
            field.Editor.SetCaret(new TextPosition(0, to), true);
        }

        static async Task Put(TopLevel top, string plain, params (IClipboardForm Form, string Payload)[] forms) =>
            await ClipboardCompat.SetAsync(top, plain, forms.Select(f => new KeyValuePair<string, object>(f.Form.FormatName, f.Payload)).ToList());

        static void Empty(RichTextField field)
        {
            field.Editor.Document.Paragraphs[0].Runs.Clear();
            field.Editor.SetCaret(new TextPosition(0, 0));
        }

        [Fact]
        public void Copy_writes_every_registered_form_and_the_plain_text_in_nfc() => Headless.Run(async () =>
        {
            var rich = new FakeClipboardForm(Rich);
            var md = new FakeClipboardForm(Md);
            var (window, field, top) = Shown(OneParagraph("café au lait"), rich, md);
            try
            {
                Select(field, 0, 5);
                await field.CopyAsync(false);
                Assert.Equal("[rich]café", await ClipboardCompat.GetStringAsync(top, rich.FormatName));
                Assert.Equal("[md]café", await ClipboardCompat.GetStringAsync(top, md.FormatName));
                Assert.Equal("café", await ClipboardCompat.GetTextAsync(top));
            }
            finally { window.Close(); }
        });

        [Fact]
        public void A_form_that_throws_or_writes_nothing_is_left_out_and_the_others_still_go() => Headless.Run(async () =>
        {
            var bad = new FakeClipboardForm("bad") { Throws = true };
            var empty = new FakeClipboardForm("empty") { WritesNothing = true };
            var good = new FakeClipboardForm("good");
            var (window, field, top) = Shown(OneParagraph("hello"), bad, empty, good);
            try
            {
                Select(field, 0, 5);
                await field.CopyAsync(false);
                Assert.Null(await ClipboardCompat.GetStringAsync(top, bad.FormatName));
                Assert.Null(await ClipboardCompat.GetStringAsync(top, empty.FormatName));
                Assert.Equal("[good]hello", await ClipboardCompat.GetStringAsync(top, good.FormatName));
                Assert.Equal("hello", await ClipboardCompat.GetTextAsync(top));
            }
            finally { window.Close(); }
        });

        [Fact]
        public void Paste_prefers_TsString_then_Markdown_then_plain_text() => Headless.Run(async () =>
        {
            var ts = new FakeClipboardForm(ClipboardForms.TsStringId);
            var md = new FakeClipboardForm(ClipboardForms.FieldWorksMarkdownId);
            var (window, field, top) = Shown(OneParagraph(""), ts, md);
            try
            {
                await Put(top, "plain", (ts, "[TsString]from tsstring"), (md, "[FieldWorks Markdown]from markdown"));
                await field.PasteAsync();
                Assert.Equal("from tsstring", field.Editor.Document.Paragraphs[0].Text);

                Empty(field);
                await Put(top, "plain", (md, "[FieldWorks Markdown]from markdown"));
                await field.PasteAsync();
                Assert.Equal("from markdown", field.Editor.Document.Paragraphs[0].Text);

                Empty(field);
                await Put(top, "plain only");
                await field.PasteAsync();
                Assert.Equal("plain only", field.Editor.Document.Paragraphs[0].Text);
            }
            finally { window.Close(); }
        });

        [Fact]
        public void The_paste_order_is_the_hosts_to_change() => Headless.Run(async () =>
        {
            var ts = new FakeClipboardForm(ClipboardForms.TsStringId);
            var md = new FakeClipboardForm(ClipboardForms.FieldWorksMarkdownId);
            var (window, field, top) = Shown(OneParagraph(""), ts, md);
            try
            {
                field.ClipboardForms.PasteOrder = new List<string> { ClipboardForms.FieldWorksMarkdownId, ClipboardForms.PlainTextId };
                await Put(top, "plain", (ts, "[TsString]from tsstring"), (md, "[FieldWorks Markdown]from markdown"));
                await field.PasteAsync();
                Assert.Equal("from markdown", field.Editor.Document.Paragraphs[0].Text);
                Assert.Equal(0, ts.Reads);   // a form that is not in the list is never read

                Empty(field);
                field.ClipboardForms.PasteOrder = new List<string> { ClipboardForms.PlainTextId, ClipboardForms.TsStringId };
                await field.PasteAsync();
                Assert.Equal("plain", field.Editor.Document.Paragraphs[0].Text);
            }
            finally { window.Close(); }
        });

        [Fact]
        public void A_form_that_cannot_read_its_payload_hands_over_to_the_next_one() => Headless.Run(async () =>
        {
            var ts = new FakeClipboardForm(ClipboardForms.TsStringId);
            var md = new FakeClipboardForm(ClipboardForms.FieldWorksMarkdownId);
            var (window, field, top) = Shown(OneParagraph(""), ts, md);
            try
            {
                // The TsString slot holds something that is not TsString; Markdown reads.
                await Put(top, "plain", (ts, "garbage"), (md, "[FieldWorks Markdown]from markdown"));
                await field.PasteAsync();
                Assert.Equal("from markdown", field.Editor.Document.Paragraphs[0].Text);

                Empty(field);
                ts.Throws = true;
                await Put(top, "plain", (ts, "[TsString]x"), (md, "[FieldWorks Markdown]from markdown"));
                await field.PasteAsync();
                Assert.Equal("from markdown", field.Editor.Document.Paragraphs[0].Text);
            }
            finally { window.Close(); }
        });

        [Fact]
        public void A_writing_system_the_target_lacks_becomes_the_selections_and_the_host_is_told() => Headless.Run(async () =>
        {
            var rich = new FakeClipboardForm(Rich) { ReadWs = "zzz" };
            var (window, field, top) = Shown(Doc(Para("fr", "fr", "bonjour ")), rich);
            try
            {
                field.WritingSystems = WritingSystemRegistry.CreateDefault();
                var told = new List<WritingSystemMapping>();
                field.WritingSystemsMapped += (s, e) => told.Add(e);
                await Put(top, "plain", (rich, "[rich]monde"));
                field.Editor.SetCaret(new TextPosition(0, 8));
                await field.PasteAsync();
                var p = field.Editor.Document.Paragraphs[0];
                Assert.Equal("bonjour monde", p.Text);
                Assert.All(p.Runs, r => Assert.Equal("fr", r.Ws));
                var note = Assert.Single(told);
                Assert.Equal(new[] { "zzz" }, note.Missing.ToArray());
                Assert.Equal("fr", note.Target);
            }
            finally { window.Close(); }
        });

        [Fact]
        public void A_known_writing_system_is_kept_and_no_notice_is_raised() => Headless.Run(async () =>
        {
            var rich = new FakeClipboardForm(Rich) { ReadWs = "ar" };
            var (window, field, top) = Shown(Doc(Para("fr", "fr", "bonjour ")), rich);
            try
            {
                field.WritingSystems = WritingSystemRegistry.CreateDefault();
                int told = 0;
                field.WritingSystemsMapped += (s, e) => told++;
                await Put(top, "plain", (rich, "[rich]x"));
                field.Editor.SetCaret(new TextPosition(0, 8));
                await field.PasteAsync();
                Assert.Equal("ar", field.Editor.Document.Paragraphs[0].Runs.Last().Ws);
                Assert.Equal(0, told);
            }
            finally { window.Close(); }
        });

        [Fact]
        public void The_host_decides_which_writing_systems_the_target_has() => Headless.Run(async () =>
        {
            var rich = new FakeClipboardForm(Rich) { ReadWs = "ar" };
            var (window, field, top) = Shown(Doc(Para("fr", "fr", "bonjour ")), rich);
            try
            {
                field.KnowsWritingSystem = ws => ws == "fr";
                WritingSystemMapping? told = null;
                field.WritingSystemsMapped += (s, e) => told = e;
                await Put(top, "plain", (rich, "[rich]x"));
                field.Editor.SetCaret(new TextPosition(0, 8));
                await field.PasteAsync();
                Assert.Equal("fr", field.Editor.Document.Paragraphs[0].Runs.Last().Ws);
                Assert.Equal(new[] { "ar" }, told!.Missing.ToArray());
            }
            finally { window.Close(); }
        });

        [Fact]
        public void Copy_as_puts_one_form_on_the_clipboard_as_text() => Headless.Run(async () =>
        {
            var rich = new FakeClipboardForm(Rich);
            var md = new FakeClipboardForm(Md);
            var (window, field, top) = Shown(OneParagraph("café au lait"), rich, md);
            try
            {
                Select(field, 0, 5);
                await field.CopyAsync(false);
                Assert.True(await field.CopyAsAsync(Md));
                Assert.Equal("[md]café", await ClipboardCompat.GetTextAsync(top));
                Assert.Null(await ClipboardCompat.GetStringAsync(top, rich.FormatName));   // the earlier copy is replaced
                Assert.True(await field.CopyAsAsync(ClipboardForms.PlainTextId));
                Assert.Equal("café", await ClipboardCompat.GetTextAsync(top));
                Assert.False(await field.CopyAsAsync("nonesuch"));
            }
            finally { window.Close(); }
        });

        [Fact]
        public void Copy_then_paste_through_a_rich_form_keeps_paragraphs_and_formatting() => Headless.Run(async () =>
        {
            var p = Para("en", "en", "one");
            p.Runs.Add(new Run { Ws = "fr", Text = " deux" });
            p.Runs[1].Props.SetInt(4242, 7);
            var (window, field, top) = Shown(Doc(p, Para("en", "en", "three")), new InterchangeClipboardForm());
            try
            {
                field.Editor.Anchor = new TextPosition(0, 2);
                field.Editor.SetCaret(new TextPosition(1, 2), true);
                await field.CopyAsync(false);
                field.Editor.SetCaret(new TextPosition(1, 5));
                await field.PasteAsync();
                var paras = field.Editor.Document.Paragraphs;
                Assert.Equal(3, paras.Count);
                Assert.Equal("threee deux", paras[1].Text);
                Assert.Contains(paras[1].Runs, r => r.Ws == "fr" && r.Props.IntProps.ContainsKey(4242));
            }
            finally { window.Close(); }
        });

        [Fact]
        public void A_serialised_object_under_the_TsString_format_name_is_skipped_and_paste_falls_through() => Headless.Run(async () =>
        {
            // FieldWorks stores a .NET-serialised wrapper object there, bytes that are not text. A form that reads text must see "not mine", never throw.
            var ts = new FakeClipboardForm(ClipboardForms.TsStringId, formatName: "TsString");
            var (window, field, top) = Shown(OneParagraph(""), ts);
            try
            {
                var item = new global::Avalonia.Input.DataTransferItem();
                item.SetText("plain fallback");
                item.Set(global::Avalonia.Input.DataFormat.CreateBytesPlatformFormat("TsString"), new byte[] { 0, 1, 0, 0, 0, 255, 255, 255, 255, 1, 0, 0, 0, 0, 0, 0, 0, 12, 2, 0, 0, 0 });
                var transfer = new global::Avalonia.Input.DataTransfer();
                transfer.Add(item);
                await top.Clipboard!.SetDataAsync(transfer);
                await field.PasteAsync();
                Assert.Equal("plain fallback", field.Editor.Document.Paragraphs[0].Text);
            }
            finally { window.Close(); }
        });

        [Fact]
        public void A_paste_a_read_only_field_refuses_changes_nothing_and_raises_no_notice() => Headless.Run(async () =>
        {
            var rich = new FakeClipboardForm(Rich) { ReadWs = "zzz" };
            var (window, field, top) = Shown(Doc(Para("fr", "fr", "bonjour ")), rich);
            try
            {
                field.WritingSystems = WritingSystemRegistry.CreateDefault();
                field.IsReadOnly = true;
                int told = 0;
                field.WritingSystemsMapped += (s, e) => told++;
                await Put(top, "plain", (rich, "[rich]monde"));
                field.Editor.SetCaret(new TextPosition(0, 8));
                await field.PasteAsync();
                Assert.Equal("bonjour ", field.Editor.Document.Paragraphs[0].Text);
                Assert.Equal(0, told);
                Assert.Equal(0, rich.Reads);   // the clipboard was not even read
            }
            finally { window.Close(); }
        });

        [Fact]
        public void A_paste_into_a_run_that_may_not_be_edited_changes_nothing_and_raises_no_notice() => Headless.Run(async () =>
        {
            var rich = new FakeClipboardForm(Rich) { ReadWs = "zzz" };
            var p = Para("fr", "fr", "locked");
            p.Runs[0].Props.SetInt(TextPropertyNumbers.Editable, TextPropertyNumbers.EditableNo);
            var (window, field, top) = Shown(Doc(p), rich);
            try
            {
                field.WritingSystems = WritingSystemRegistry.CreateDefault();
                int told = 0;
                field.WritingSystemsMapped += (s, e) => told++;
                await Put(top, "plain", (rich, "[rich]monde"));
                field.Editor.SetCaret(new TextPosition(0, 3));
                await field.PasteAsync();
                Assert.Equal("locked", field.Editor.Document.Paragraphs[0].Text);
                Assert.Equal(0, told);
            }
            finally { window.Close(); }
        });

        [Fact]
        public void The_missing_writing_system_becomes_the_writing_system_of_the_selection_not_the_one_chosen_for_typing() => Headless.Run(async () =>
        {
            var rich = new FakeClipboardForm(Rich) { ReadWs = "zzz" };
            var (window, field, top) = Shown(Doc(Para("fr", "fr", "bonjour ")), rich);
            try
            {
                field.WritingSystems = WritingSystemRegistry.CreateDefault();
                field.Editor.SetCaret(new TextPosition(0, 8));
                field.Editor.SetActiveWs("ar");                 // a keyboard switch: typing would be Arabic
                WritingSystemMapping? told = null;
                field.WritingSystemsMapped += (s, e) => told = e;
                await Put(top, "plain", (rich, "[rich]monde"));
                await field.PasteAsync();
                Assert.Equal("fr", told!.Target);
                Assert.All(field.Editor.Document.Paragraphs[0].Runs, r => Assert.Equal("fr", r.Ws));
            }
            finally { window.Close(); }
        });

        [Fact]
        public void A_payload_that_names_no_writing_system_takes_the_selections_without_a_notice() => Headless.Run(async () =>
        {
            var rich = new FakeClipboardForm(Rich) { ReadWs = "" };
            var (window, field, top) = Shown(Doc(Para("fr", "fr", "bonjour ")), rich);
            try
            {
                int told = 0;
                field.WritingSystemsMapped += (s, e) => told++;
                await Put(top, "plain", (rich, "[rich]monde"));
                field.Editor.SetCaret(new TextPosition(0, 8));
                await field.PasteAsync();
                Assert.All(field.Editor.Document.Paragraphs[0].Runs, r => Assert.Equal("fr", r.Ws));
                Assert.Equal(0, told);
            }
            finally { window.Close(); }
        });

        [Fact]
        public void The_clipboard_forms_cannot_be_set_to_null() => Headless.Run(() =>
        {
            var field = new RichTextField(OneParagraph("x"));
            Assert.Throws<System.ArgumentNullException>(() => field.ClipboardForms = null!);
        });
    }
}
