using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Xunit;
using static SIL.TextEditing.Tests.TestDocs;

namespace SIL.TextEditing.Avalonia.Tests
{
    /// <summary>A host that keeps the clipboard itself (FieldWorks' WinForms host puts a serialised object under "TsString") plugs in an
    /// <see cref="IClipboardBackend"/>, and its forms carry strings, bytes or host objects through it untouched (ADR 0029).</summary>
    public class ClipboardBackendTests
    {
        sealed class FakeBackend : IClipboardBackend
        {
            public string Plain = string.Empty;
            public readonly Dictionary<string, object> Formats = new Dictionary<string, object>();

            public Task<IReadOnlyList<string>> SetAsync(string plainText, IReadOnlyList<KeyValuePair<string, object>> forms)
            {
                Plain = plainText;
                Formats.Clear();
                foreach (var f in forms) Formats[f.Key] = f.Value;
                return Task.FromResult<IReadOnlyList<string>>(new string[0]);
            }

            public Task<object?> GetAsync(string formatName, ClipboardPayloadKind kind) => Task.FromResult(Formats.TryGetValue(formatName, out var v) ? v : null);
            public Task<string?> GetTextAsync() => Task.FromResult<string?>(Plain);
        }

        /// <summary>A host object that is neither text nor bytes, like FieldWorks' serialisable wrapper of an ITsString.</summary>
        sealed class Wrapper
        {
            public Wrapper(string text) { Text = text; }
            public string Text { get; }
        }

        sealed class BytesForm : IClipboardForm
        {
            public static readonly byte[] Bytes = { 0xFF, 0xFE, 0xC0, 0x80, 0x00, 0x01, 0x80, 0xFF, 0x00 };   // not text in any encoding, ends in a zero
            public object? Received;
            public string Id => "bytes";
            public string DisplayName => "Bytes";
            public string FormatName => "test/bytes";
            public bool OffersCopyAs => false;
            public ClipboardPayloadKind PayloadKind => ClipboardPayloadKind.Bytes;
            public object? Write(IReadOnlyList<Paragraph> fragment) => Bytes;
            public IReadOnlyList<Paragraph>? Read(object payload)
            {
                Received = payload;
                return payload is byte[] ? new[] { Para("en", "en", "from bytes") } : null;
            }
        }

        /// <summary>A host's own TsString form: its payload is an object, not a string.</summary>
        sealed class WrapperForm : IClipboardForm
        {
            public object? Received;
            public string Id => ClipboardForms.TsStringId;
            public string DisplayName => "TsString";
            public string FormatName => "TsString";
            public bool OffersCopyAs => false;
            public ClipboardPayloadKind PayloadKind => ClipboardPayloadKind.Object;
            public object? Write(IReadOnlyList<Paragraph> fragment) => new Wrapper(string.Join("|", fragment.Select(p => p.Text)));
            public IReadOnlyList<Paragraph>? Read(object payload)
            {
                Received = payload;
                return payload is Wrapper w ? new[] { Para("en", "en", w.Text) } : null;
            }
        }

        static RichTextField Field(IClipboardBackend backend, string text, params IClipboardForm[] forms)
        {
            var field = new RichTextField(OneParagraph(text)) { ClipboardBackend = backend };
            foreach (var f in forms)
            {
                field.ClipboardForms.Add(f);
                if (!field.ClipboardForms.PasteOrder.Contains(f.Id)) field.ClipboardForms.PasteOrder.Insert(0, f.Id);
            }
            return field;
        }

        static void SelectAll(RichTextField field)
        {
            field.Editor.Anchor = new TextPosition(0, 0);
            field.Editor.SetCaret(new TextPosition(0, field.Editor.Document.Paragraphs[0].Length), true);
        }

        [Fact]
        public void A_byte_payload_that_is_not_text_reaches_the_backend_and_the_form_exactly() => Headless.Run(async () =>
        {
            var backend = new FakeBackend();
            var form = new BytesForm();
            var field = Field(backend, "hello", form);
            SelectAll(field);
            await field.CopyAsync(false);
            Assert.Same(BytesForm.Bytes, backend.Formats["test/bytes"]);
            Assert.Equal("hello", backend.Plain);
            field.Editor.SetCaret(new TextPosition(0, 5));
            await field.PasteAsync();
            Assert.Equal(BytesForm.Bytes, Assert.IsType<byte[]>(form.Received));
            Assert.Equal("hellofrom bytes", field.Editor.Document.Paragraphs[0].Text);
        });

        [Fact]
        public void A_host_object_payload_round_trips_untouched() => Headless.Run(async () =>
        {
            var backend = new FakeBackend();
            var form = new WrapperForm();
            var field = Field(backend, "hello", form);
            SelectAll(field);
            await field.CopyAsync(false);
            var written = Assert.IsType<Wrapper>(backend.Formats["TsString"]);
            Assert.Equal("hello", written.Text);
            field.Editor.SetCaret(new TextPosition(0, 5));
            await field.PasteAsync();
            Assert.Same(written, form.Received);
        });

        [Fact]
        public void A_host_TsString_form_with_an_object_payload_is_used_on_paste_before_Markdown_and_plain_text() => Headless.Run(async () =>
        {
            var backend = new FakeBackend();
            backend.Plain = "plain";
            backend.Formats["TsString"] = new Wrapper("from the wrapper");
            backend.Formats["FieldWorks Markdown"] = "from markdown";
            var field = Field(backend, string.Empty, new WrapperForm(), new FakeClipboardFormMarkdown());
            await field.PasteAsync();
            Assert.Equal("from the wrapper", field.Editor.Document.Paragraphs[0].Text);
        });

        sealed class FakeClipboardFormMarkdown : IClipboardForm
        {
            public string Id => ClipboardForms.FieldWorksMarkdownId;
            public string DisplayName => "Markdown";
            public string FormatName => "FieldWorks Markdown";
            public bool OffersCopyAs => true;
            public ClipboardPayloadKind PayloadKind => ClipboardPayloadKind.Text;
            public object? Write(IReadOnlyList<Paragraph> fragment) => null;
            public IReadOnlyList<Paragraph>? Read(object payload) => new[] { Para("en", "en", (string)payload) };
        }

        [Fact]
        public void A_payload_the_form_does_not_understand_falls_through_to_the_next_form_and_never_throws() => Headless.Run(async () =>
        {
            var backend = new FakeBackend();
            backend.Plain = "plain";
            backend.Formats["TsString"] = new byte[] { 1, 2, 3, 255 };    // FieldWorks' real data where a form expects an object it cannot read
            var field = Field(backend, string.Empty, new WrapperForm());
            await field.PasteAsync();
            Assert.Equal("plain", field.Editor.Document.Paragraphs[0].Text);
        });

        [Fact]
        public void Copy_as_offers_only_text_so_a_form_with_an_object_payload_is_not_copied_as() => Headless.Run(async () =>
        {
            var backend = new FakeBackend();
            var field = Field(backend, "hello", new WrapperForm());
            SelectAll(field);
            Assert.False(await field.CopyAsAsync(ClipboardForms.TsStringId));
        });

        [Fact]
        public void Avalonias_own_clipboard_round_trips_bytes_that_are_not_text() => Headless.Run(async () =>
        {
            var (window, field) = Fixtures.InWindow(OneParagraph("x"));
            try
            {
                var top = TopLevel.GetTopLevel(field);
                await ClipboardCompat.SetAsync(top, "plain", new[] { new KeyValuePair<string, object>("test/bytes", BytesForm.Bytes) });
                var back = await field.ClipboardBackend.GetAsync("test/bytes", ClipboardPayloadKind.Bytes);
                Assert.Equal(BytesForm.Bytes, Assert.IsType<byte[]>(back));
            }
            finally { window.Close(); }
        });

        [Fact]
        public void Text_and_bytes_are_read_back_as_the_kind_asked_for_and_never_one_as_the_other() => Headless.Run(async () =>
        {
            var (window, field) = Fixtures.InWindow(OneParagraph("x"));
            try
            {
                var top = TopLevel.GetTopLevel(field);
                string arabic = "\u0627\u0644\u0639\u0631\u0628\u064A\u0629";   // starts above U+00FF: no byte-guessing may mistake it
                await ClipboardCompat.SetAsync(top, "plain", new[]
                {
                    new KeyValuePair<string, object>("test/bytes", BytesForm.Bytes),
                    new KeyValuePair<string, object>("test/text", arabic),
                });
                Assert.Equal(arabic, await field.ClipboardBackend.GetAsync("test/text", ClipboardPayloadKind.Text));
                Assert.Equal(BytesForm.Bytes, Assert.IsType<byte[]>(await field.ClipboardBackend.GetAsync("test/bytes", ClipboardPayloadKind.Bytes)));
                Assert.Null(await field.ClipboardBackend.GetAsync("test/bytes", ClipboardPayloadKind.Object));
            }
            finally { window.Close(); }
        });

        [Fact]
        public void The_default_backend_tells_the_host_which_forms_it_could_not_store() => Headless.Run(async () =>
        {
            var (window, field) = Fixtures.InWindow(OneParagraph("hello"));
            try
            {
                field.ClipboardForms.Add(new WrapperForm());
                IReadOnlyList<string>? told = null;
                field.ClipboardFormsNotStored += (s, e) => told = e;
                SelectAll(field);
                await field.CopyAsync(false);
                Assert.Equal(new[] { "TsString" }, told!.ToArray());
            }
            finally { window.Close(); }
        });

        [Fact]
        public void The_backend_cannot_be_set_to_null() => Headless.Run(() =>
        {
            var field = new RichTextField(OneParagraph("x"));
            Assert.Throws<ArgumentNullException>(() => field.ClipboardBackend = null!);
        });
    }
}
