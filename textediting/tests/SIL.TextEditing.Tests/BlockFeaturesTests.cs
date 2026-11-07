using System.Collections.Generic;
using System.Linq;
using Xunit;
using static SIL.TextEditing.Tests.TestDocs;

namespace SIL.TextEditing.Tests
{
    /// <summary>ADR 0033 (owner decision Q79): which of tables, lists and headings a field may hold.</summary>
    public class BlockFeaturesTests
    {
        const string Row = "Table Row", Head = "Table Header";

        static readonly IBlockRecognizer Recognizer = new StyleBlockRecognizer(
            new[] { "Heading 1", "Heading 2" }, new[] { "Bulleted List", "Numbered List" }, new[] { Head, Row });

        static Paragraph Styled(string style, params string[] wsAndText)
        {
            var p = Para("en", wsAndText);
            p.Style = style;
            p.Props.StrProps[TextPropertyNumbers.NamedStyle] = style;
            return p;
        }

        static Paragraph TableRow(params string[] cells)
        {
            var args = new List<string>();
            for (int i = 0; i < cells.Length; i++)
            {
                if (i > 0) { args.Add("en"); args.Add("\t"); }
                args.Add("en"); args.Add(cells[i]);
            }
            return Styled(Row, args.ToArray());
        }

        static DocumentEditor Editor(BlockFeatures features, string text = "")
        {
            var e = new DocumentEditor(OneParagraph(text)) { BlockFeatures = features, BlockRecognizer = Recognizer };
            e.SetCaret(new TextPosition(0, text.Length));
            return e;
        }

        static string[] Texts(DocumentEditor e) => e.Document.Paragraphs.Select(p => p.Text).ToArray();

        // The first pasted paragraph joins the paragraph under the caret and takes its style (as it always did), so a paste of blocks starts with a plain lead paragraph.
        static List<Paragraph> Frag(params Paragraph[] blocks) => new[] { Para("en", "en", "lead") }.Concat(blocks).ToList();

        static string[] AfterLead(DocumentEditor e) => Texts(e).Skip(1).ToArray();

        // ---- the setting and the recogniser

        [Fact]
        public void The_default_is_All_so_nothing_changes()
        {
            var e = new DocumentEditor(OneParagraph(""));
            Assert.Equal(BlockFeatures.All, e.BlockFeatures);
            Assert.Equal(BlockFeatures.Tables | BlockFeatures.Lists | BlockFeatures.Headings, BlockFeatures.All);
            Assert.Equal(0, (int)BlockFeatures.None);
            Assert.Same(StyleBlockRecognizer.Default, e.BlockRecognizer);
        }

        [Fact]
        public void With_everything_on_a_paste_keeps_its_blocks_and_raises_nothing()
        {
            var e = Editor(BlockFeatures.All);
            BlockFlattening? seen = null;
            e.BlocksFlattened += f => seen = f;
            e.Paste(Frag(Styled("Heading 1", "en", "Title"), TableRow("a", "b"), Styled("Bulleted List", "en", "item")));
            Assert.Equal(4, e.Document.Paragraphs.Count);
            Assert.Equal("Heading 1", e.Document.Paragraphs[1].Style);
            Assert.Equal(Row, e.Document.Paragraphs[2].Style);
            Assert.Equal("Bulleted List", e.Document.Paragraphs[3].Style);
            Assert.Null(seen);
        }

        [Fact]
        public void The_recogniser_goes_by_the_hosts_style_names_and_never_by_GDR()
        {
            Assert.Equal(BlockFeatures.Headings, Recognizer.Recognize(Styled("Heading 2", "en", "x")));
            Assert.Equal(BlockFeatures.Lists, Recognizer.Recognize(Styled("Numbered List", "en", "x")));
            Assert.Equal(BlockFeatures.Tables, Recognizer.Recognize(Styled(Head, "en", "x")));
            Assert.Equal(BlockFeatures.None, Recognizer.Recognize(Styled("Normal", "en", "x")));
            // The default recogniser has no table style: the host names them.
            Assert.Equal(BlockFeatures.None, StyleBlockRecognizer.Default.Recognize(Styled("GDR Table Row", "en", "x")));
            var gdr = new StyleBlockRecognizer(null, null, new[] { "GDR Table Header", "GDR Table Row" });
            Assert.Equal(BlockFeatures.Tables, gdr.Recognize(Styled("GDR Table Row", "en", "x")));
        }

        [Fact]
        public void A_bullet_scheme_of_the_paragraphs_own_makes_it_a_list_item()
        {
            var p = Para("en", "en", "x");
            p.Props.SetInt(TextPropertyNumbers.BulNumScheme, 100);
            Assert.Equal(BlockFeatures.Lists, Recognizer.Recognize(p));
            p.Props.SetInt(TextPropertyNumbers.BulNumScheme, 0);
            Assert.Equal(BlockFeatures.None, Recognizer.Recognize(p));
        }

        // ---- paste

        [Fact]
        public void A_pasted_table_becomes_one_plain_paragraph_per_row_with_the_cells_joined_by_a_space()
        {
            var e = Editor(BlockFeatures.Lists | BlockFeatures.Headings);
            BlockFlattening? seen = null;
            e.BlocksFlattened += f => seen = f;
            e.Paste(Frag(TableRow("Name", "Gloss"), TableRow("palit", "buy")));
            Assert.Equal(new[] { "Name | Gloss", "palit | buy" }, AfterLead(e));
            Assert.All(e.Document.Paragraphs.Skip(1), p => Assert.Equal("Normal", p.Style));
            Assert.All(e.Document.Paragraphs.Skip(1), p => Assert.Equal("Normal", p.Props.StrProps[TextPropertyNumbers.NamedStyle]));
            Assert.NotNull(seen);
            Assert.Equal(2, seen!.TableRows);
            Assert.Equal(0, seen.ListItems + seen.Headings);
            Assert.Equal(BlockFeatures.Tables, seen.Kinds);
        }

        [Fact]
        public void A_pasted_table_keeps_the_cells_apart_even_with_empty_cells_and_other_writing_systems()
        {
            var e = Editor(BlockFeatures.None);
            var row = Styled(Row, "en", "a", "en", "\t", "fr", "b", "en", "\t", "en", "\t", "fr", "d");
            e.Paste(new List<Paragraph> { row });
            Assert.Equal("a | b |  | d", e.Document.Paragraphs[0].Text);
            Assert.Contains(e.Document.Paragraphs[0].Runs, r => r.Ws == "fr" && r.Text.Contains("b"));
            Assert.DoesNotContain('\t', e.Document.Paragraphs[0].Text);
        }

        [Fact]
        public void Pasted_lists_and_headings_become_plain_paragraphs_and_lose_their_list_properties()
        {
            var e = Editor(BlockFeatures.Tables);
            var bullet = Styled("Bulleted List", "en", "one");
            bullet.Props.SetInt(TextPropertyNumbers.BulNumScheme, 100);
            bullet.Props.SetStr(TextPropertyNumbers.BulNumTxtAfter, ")");
            var withScheme = Para("en", "en", "two");
            withScheme.Props.SetInt(TextPropertyNumbers.BulNumScheme, 10);
            BlockFlattening? seen = null;
            e.BlocksFlattened += f => seen = f;
            e.Paste(Frag(Styled("Heading 1", "en", "Title"), bullet, withScheme));
            Assert.Equal(new[] { "Title", "one", "two" }, AfterLead(e));
            Assert.All(e.Document.Paragraphs.Skip(1), p => Assert.Equal(BlockFeatures.None, Recognizer.Recognize(p)));
            Assert.All(e.Document.Paragraphs.Skip(1), p => Assert.False(p.Props.IntProps.ContainsKey(TextPropertyNumbers.BulNumScheme)));
            Assert.All(e.Document.Paragraphs.Skip(1), p => Assert.False(p.Props.StrProps.ContainsKey(TextPropertyNumbers.BulNumTxtAfter)));
            Assert.Equal(1, seen!.Headings);
            Assert.Equal(2, seen.ListItems);
        }

        [Fact]
        public void Only_the_kinds_that_are_off_are_flattened()
        {
            var e = Editor(BlockFeatures.Lists);
            e.Paste(Frag(Styled("Heading 1", "en", "Title"), Styled("Bulleted List", "en", "one"), TableRow("a", "b")));
            Assert.Equal(new[] { "Title", "one", "a | b" }, AfterLead(e));
            Assert.Equal("Bulleted List", e.Document.Paragraphs[2].Style);
            Assert.Equal("Normal", e.Document.Paragraphs[1].Style);
        }

        [Fact]
        public void A_flattened_table_is_one_undo_step_and_undo_restores_nothing_of_it()
        {
            var e = Editor(BlockFeatures.None, "x");
            var undo = new SimpleUndoStack();
            undo.Attach(e);
            e.Paste(new List<Paragraph> { TableRow("a", "b"), TableRow("c", "d") });
            Assert.Equal(1, undo.DoneSteps);
            undo.Undo();
            Assert.Equal(new[] { "x" }, Texts(e));
            undo.Redo();
            Assert.Equal(new[] { "xa | b", "c | d" }, Texts(e));
        }

        [Fact]
        public void Flattening_runs_before_single_paragraph_joins_so_the_cells_stay_apart()
        {
            var e = Editor(BlockFeatures.None);
            e.SingleParagraph = true;
            e.Paste(new List<Paragraph> { TableRow("a", "b"), TableRow("c", "d") });
            Assert.Equal(new[] { "a | b c | d" }, Texts(e));
        }

        [Fact]
        public void Plain_text_paste_is_not_a_block_and_raises_nothing()
        {
            var e = Editor(BlockFeatures.None);
            BlockFlattening? seen = null;
            e.BlocksFlattened += f => seen = f;
            e.PasteText("a\tb\nc");
            Assert.Null(seen);
        }

        // ---- commands, Enter, existing blocks

        [Fact]
        public void A_command_that_would_make_a_block_is_refused_by_the_style_query()
        {
            var e = Editor(BlockFeatures.Lists);
            Assert.True(e.IsParagraphStyleAllowed("Bulleted List"));
            Assert.False(e.IsParagraphStyleAllowed("Heading 1"));
            Assert.False(e.IsParagraphStyleAllowed(Row));
            Assert.True(e.IsParagraphStyleAllowed("Normal"));
            Assert.True(e.IsParagraphStyleAllowed("Anything the host invents"));
            Assert.True(e.IsAllowed(BlockFeatures.None));
            Assert.False(e.IsAllowed(BlockFeatures.Tables));
            Assert.False(e.IsAllowed(BlockFeatures.Tables | BlockFeatures.Lists));
            e.BlockFeatures = BlockFeatures.All;
            Assert.True(e.IsParagraphStyleAllowed("Heading 1"));
        }

        [Fact]
        public void The_blocks_a_document_already_holds_are_kept()
        {
            var doc = Doc(Styled("Heading 1", "en", "Title"), TableRow("a", "b"));
            var e = new DocumentEditor(doc) { BlockFeatures = BlockFeatures.None, BlockRecognizer = Recognizer };
            Assert.Equal("Heading 1", e.Document.Paragraphs[0].Style);
            Assert.Equal("a\tb", e.Document.Paragraphs[1].Text);
        }

        [Fact]
        public void Enter_in_a_disallowed_block_starts_a_plain_paragraph_and_in_an_allowed_one_continues_it()
        {
            var off = new DocumentEditor(Doc(Styled("Bulleted List", "en", "item"))) { BlockFeatures = BlockFeatures.Tables, BlockRecognizer = Recognizer };
            off.SetCaret(new TextPosition(0, 4));
            off.Enter();
            Assert.Equal("Bulleted List", off.Document.Paragraphs[0].Style);
            Assert.Equal("Normal", off.Document.Paragraphs[1].Style);

            var start = new DocumentEditor(Doc(Styled("Bulleted List", "en", "item"))) { BlockFeatures = BlockFeatures.Tables, BlockRecognizer = Recognizer };
            start.SetCaret(new TextPosition(0, 0));
            start.Enter();
            Assert.Equal("Normal", start.Document.Paragraphs[0].Style);
            Assert.Equal("Bulleted List", start.Document.Paragraphs[1].Style);
            Assert.Equal("item", start.Document.Paragraphs[1].Text);

            var on = new DocumentEditor(Doc(Styled("Bulleted List", "en", "item"))) { BlockRecognizer = Recognizer };
            on.SetCaret(new TextPosition(0, 4));
            on.Enter();
            Assert.Equal("Bulleted List", on.Document.Paragraphs[1].Style);
        }

        [Fact]
        public void Undo_and_redo_restore_a_block_exactly_even_when_the_feature_is_off()
        {
            var doc = Doc(Styled("Heading 1", "en", "Title"));
            var e = new DocumentEditor(doc) { BlockFeatures = BlockFeatures.None, BlockRecognizer = Recognizer };
            var before = doc.Paragraphs[0].Clone();
            var delta = new EditDelta { Start = 0, Before = new List<Paragraph> { before.Clone() }, After = new List<Paragraph> { Styled("Normal", "en", "Title") }, CaretBefore = e.Caret, CaretAfter = e.Caret, Label = "style" };
            e.Apply(delta);
            Assert.Equal("Normal", e.Document.Paragraphs[0].Style);
            e.Apply(new EditDelta { Start = 0, Before = delta.After, After = delta.Before, CaretBefore = e.Caret, CaretAfter = e.Caret, Label = "undo" });
            Assert.Equal("Heading 1", e.Document.Paragraphs[0].Style);
        }

        [Fact]
        public void The_recogniser_cannot_be_null()
        {
            var e = Editor(BlockFeatures.All);
            Assert.Throws<System.ArgumentNullException>(() => e.BlockRecognizer = null!);
        }

        // ---- the flattener, shared with the record reader

        [Fact]
        public void Flatten_reports_what_it_changed_and_leaves_allowed_and_plain_paragraphs_alone()
        {
            var paras = new List<Paragraph> { Para("en", "en", "plain"), Styled("Heading 1", "en", "t"), TableRow("a", "b"), Styled("Numbered List", "en", "n") };
            var report = BlockFlattener.Flatten(paras, BlockFeatures.Lists, Recognizer);
            Assert.Equal(1, report.Headings);
            Assert.Equal(1, report.TableRows);
            Assert.Equal(0, report.ListItems);
            Assert.Equal(2, report.Total);
            Assert.Equal("Numbered List", paras[3].Style);
            Assert.Equal("a | b", paras[2].Text);
            Assert.Equal(0, BlockFlattener.Flatten(paras, BlockFeatures.All, Recognizer).Total);
        }
        // ---- review of PR 156

        [Fact]
        public void Replace_all_inside_a_kept_heading_or_table_row_raises_nothing_and_keeps_the_block()
        {
            var doc = Doc(Styled("Heading 1", "en", "foo bar foo"), TableRow("foo", "bar"));
            var e = new DocumentEditor(doc) { BlockFeatures = BlockFeatures.None, BlockRecognizer = Recognizer };
            var seen = new List<BlockFlattening>();
            e.BlocksFlattened += f => seen.Add(f);
            Assert.Equal(3, e.ReplaceAll(new FindOptions { Pattern = "foo" }, "baz"));
            Assert.Empty(seen);
            Assert.Equal("baz bar baz", e.Document.Paragraphs[0].Text);
            Assert.Equal("Heading 1", e.Document.Paragraphs[0].Style);
            Assert.Equal(Row, e.Document.Paragraphs[1].Style);
            Assert.Equal("baz\tbar", e.Document.Paragraphs[1].Text);
        }

        sealed class CombinedRecognizer : IBlockRecognizer
        {
            public int Flattens;
            public BlockFeatures Recognize(Paragraph paragraph) => BlockFeatures.All;
            public BlockFeatures RecognizeStyle(string styleName) => BlockFeatures.All;
            public void Flatten(Paragraph paragraph, BlockFeatures kind) => Flattens++;
        }

        [Fact]
        public void A_recogniser_answer_that_is_not_one_flag_means_not_a_block()
        {
            var r = new CombinedRecognizer();
            var e = Editor(BlockFeatures.None);
            e.BlockRecognizer = r;
            BlockFlattening? seen = null;
            e.BlocksFlattened += f => seen = f;
            e.Paste(Frag(Styled("Heading 1", "en", "Title")));
            Assert.Equal(0, r.Flattens);
            Assert.Null(seen);
            Assert.Equal(0, BlockFlattener.Flatten(new List<Paragraph> { Para("en", "en", "x") }, BlockFeatures.None, r).Total);
        }

        [Fact]
        public void A_heading_with_bullets_of_its_own_goes_fully_plain_when_headings_are_off()
        {
            var p = Styled("Heading 1", "en", "Title");
            p.Props.SetInt(TextPropertyNumbers.BulNumScheme, 100);
            p.Props.SetStr(TextPropertyNumbers.BulNumTxtAfter, ")");
            var e = Editor(BlockFeatures.Lists | BlockFeatures.Tables);
            e.Paste(Frag(p));
            var plain = e.Document.Paragraphs[1];
            Assert.Equal("Normal", plain.Style);
            Assert.False(plain.Props.IntProps.ContainsKey(TextPropertyNumbers.BulNumScheme));
            Assert.False(plain.Props.StrProps.ContainsKey(TextPropertyNumbers.BulNumTxtAfter));
            Assert.Equal(BlockFeatures.None, Recognizer.Recognize(plain));
        }

        [Fact]
        public void The_default_recogniser_has_no_table_style_so_tables_need_a_recogniser_that_names_them()
        {
            var e = new DocumentEditor(OneParagraph("")) { BlockFeatures = BlockFeatures.Lists | BlockFeatures.Headings };
            Assert.True(e.IsParagraphStyleAllowed("Table Row"));
            e.BlockRecognizer = new StyleBlockRecognizer(null, null, new[] { "Table Row" });
            Assert.False(e.IsParagraphStyleAllowed("Table Row"));
        }
    }
}
