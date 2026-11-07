using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using SIL.TextEditing.Tests;
using Xunit;
using static SIL.TextEditing.Tests.TestDocs;

namespace SIL.TextEditing.Avalonia.Tests
{
    /// <summary>ADR 0033 on the field: BlockFeatures sits beside SingleParagraph, a paste flattens what is off and tells the host, and the host asks the field which styles it may offer.</summary>
    public class BlockFeaturesFieldTests
    {
        const string RowStyle = "Table Row";

        static readonly IBlockRecognizer Recognizer = new StyleBlockRecognizer(new[] { "Heading 1" }, new[] { "Bulleted List" }, new[] { RowStyle });

        /// <summary>A rich form whose payload is any text and whose paragraphs are a plain lead, a heading, a two-row table and a list item.</summary>
        sealed class BlocksForm : IClipboardForm
        {
            public string Id => "blocks";
            public string DisplayName => "blocks";
            public string FormatName => "test/blocks";
            public bool OffersCopyAs => false;
            public ClipboardPayloadKind PayloadKind => ClipboardPayloadKind.Text;
            public object? Write(IReadOnlyList<Paragraph> fragment) => null;

            public IReadOnlyList<Paragraph>? Read(object payload)
            {
                Paragraph P(string style, string text)
                {
                    var p = new Paragraph { Style = style, DefaultWs = "en" };
                    p.Runs.Add(new Run { Ws = "en", Text = text });
                    return p;
                }
                return new[] { P("", "lead"), P("Heading 1", "Title"), P(RowStyle, "a\tb"), P(RowStyle, "c\td"), P("Bulleted List", "item") };
            }
        }

        static (Window Window, RichTextField Field) Shown()
        {
            var (window, field) = Fixtures.InWindow(OneParagraph(string.Empty));
            var form = new BlocksForm();
            field.ClipboardForms.Add(form);
            field.ClipboardForms.PasteOrder.Insert(0, form.Id);
            field.BlockRecognizer = Recognizer;
            return (window, field);
        }

        static async System.Threading.Tasks.Task Paste(RichTextField field)
        {
            await ClipboardCompat.SetAsync(TopLevel.GetTopLevel(field), "x", new[] { new KeyValuePair<string, object>("test/blocks", "x") });
            await field.PasteAsync();
        }

        [Fact]
        public void The_setting_forwards_to_the_editor_and_defaults_to_all() => Headless.Run(() =>
        {
            var field = Fixtures.Field(OneParagraph("x"));
            Assert.Equal(BlockFeatures.All, field.BlockFeatures);
            field.BlockFeatures = BlockFeatures.Lists;
            Assert.Equal(BlockFeatures.Lists, field.Editor.BlockFeatures);
            field.BlockRecognizer = Recognizer;
            Assert.Same(Recognizer, field.Editor.BlockRecognizer);
        });

        [Fact]
        public void The_host_asks_the_field_which_paragraph_styles_it_may_offer() => Headless.Run(() =>
        {
            var field = Fixtures.Field(OneParagraph("x"), 520, f => { f.BlockRecognizer = Recognizer; f.BlockFeatures = BlockFeatures.Lists; });
            Assert.True(field.IsParagraphStyleAllowed("Bulleted List"));
            Assert.False(field.IsParagraphStyleAllowed("Heading 1"));
            Assert.False(field.IsParagraphStyleAllowed(RowStyle));
            Assert.True(field.IsParagraphStyleAllowed("Normal"));
        });

        [Fact]
        public void A_paste_with_everything_on_keeps_its_blocks_and_raises_nothing() => Headless.Run(async () =>
        {
            var (window, field) = Shown();
            try
            {
                int raised = 0;
                field.BlocksFlattened += (o, f) => raised++;
                await Paste(field);
                Assert.Equal(new[] { RowStyle, RowStyle, "Bulleted List" }, field.Editor.Document.Paragraphs.Skip(2).Select(p => p.Style).ToArray());
                Assert.Equal("Heading 1", field.Editor.Document.Paragraphs[1].Style);
                Assert.Equal(0, raised);
            }
            finally { window.Close(); }
        });

        [Fact]
        public void A_paste_with_features_off_flattens_the_blocks_and_tells_the_host_once() => Headless.Run(async () =>
        {
            var (window, field) = Shown();
            try
            {
                field.BlockFeatures = BlockFeatures.None;
                var seen = new List<BlockFlattening>();
                field.BlocksFlattened += (o, f) => { Assert.Same(field, o); seen.Add(f); };
                await Paste(field);
                var paragraphs = field.Editor.Document.Paragraphs;
                Assert.Equal(new[] { "lead", "Title", "a | b", "c | d", "item" }, paragraphs.Select(p => p.Text).ToArray());
                Assert.All(paragraphs.Skip(1), p => Assert.Equal(BlockFeatures.None, Recognizer.Recognize(p)));
                var one = Assert.Single(seen);
                Assert.Equal(2, one.TableRows);
                Assert.Equal(1, one.ListItems);
                Assert.Equal(1, one.Headings);
            }
            finally { window.Close(); }
        });

        [Fact]
        public void Only_the_tables_are_flattened_when_only_tables_are_off() => Headless.Run(async () =>
        {
            var (window, field) = Shown();
            try
            {
                field.BlockFeatures = BlockFeatures.Lists | BlockFeatures.Headings;
                await Paste(field);
                var paragraphs = field.Editor.Document.Paragraphs;
                Assert.Equal("Heading 1", paragraphs[1].Style);
                Assert.Equal("Bulleted List", paragraphs[4].Style);
                Assert.Equal("a | b", paragraphs[2].Text);
                Assert.NotEqual(RowStyle, paragraphs[2].Style);
            }
            finally { window.Close(); }
        });

        [Fact]
        public void A_single_paragraph_field_with_tables_off_gets_one_line_with_the_cells_apart() => Headless.Run(async () =>
        {
            var (window, field) = Shown();
            try
            {
                field.SingleParagraph = true;
                field.BlockFeatures = BlockFeatures.None;
                await Paste(field);
                Assert.Single(field.Editor.Document.Paragraphs);
                Assert.Equal("lead Title a | b c | d item", field.Editor.Document.Paragraphs[0].Text);
            }
            finally { window.Close(); }
        });
    }
}
