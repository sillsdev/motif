using System;
using System.Collections.Generic;
using System.Linq;

namespace SIL.TextEditing
{
    /// <summary>The block kinds a field may hold beyond plain paragraphs (ADR 0033, owner decision Q79). A field has a set of them on
    /// <see cref="DocumentEditor.BlockFeatures"/> beside <see cref="DocumentEditor.SingleParagraph"/>; a record schema's rich field has the same set. The default
    /// is <see cref="All"/>, so a field that never sets it behaves as it always did. A paragraph is one kind at most, so <see cref="IBlockRecognizer.Recognize"/>
    /// answers with one flag, or <see cref="None"/> for a plain paragraph.</summary>
    [Flags]
    public enum BlockFeatures
    {
        /// <summary>Plain paragraphs only. As the answer of <see cref="IBlockRecognizer.Recognize"/>: the paragraph is plain.</summary>
        None = 0,
        /// <summary>Tables: one paragraph per row, in today's form a tab between cells. The default recogniser knows no table style (the host names them), so
        /// a host that turns this off must give <see cref="DocumentEditor.BlockRecognizer"/> its table styles, for instance
        /// <c>MarkdownStyleNames.ToBlockRecognizer()</c>; until it does, no paragraph is a table row and nothing is flattened or refused.</summary>
        Tables = 1,
        /// <summary>Bulleted and numbered lists: one paragraph per item.</summary>
        Lists = 2,
        /// <summary>Headings.</summary>
        Headings = 4,
        /// <summary>Every block kind. The default of a field.</summary>
        All = Tables | Lists | Headings,
    }

    /// <summary>How a paragraph is recognised as a table row, a list item or a heading, and how it is made plain again. This is the one place that knows;
    /// the editor, paste and the record reader all ask it, so that "what a table is" is not spread over the code. The default, <see cref="StyleBlockRecognizer"/>,
    /// goes by the host's paragraph style names. Table storage is expected to change (a styled separator, issue 152): a host or a later version plugs another
    /// recogniser in and nothing else moves.</summary>
    public interface IBlockRecognizer
    {
        /// <summary>The one feature the paragraph is an instance of, or <see cref="BlockFeatures.None"/> for a plain paragraph. The answer must be exactly one of
        /// <see cref="BlockFeatures.Tables"/>, <see cref="BlockFeatures.Lists"/> and <see cref="BlockFeatures.Headings"/>, or <see cref="BlockFeatures.None"/>: an
        /// answer that combines flags is taken to mean "not a block" (see <see cref="BlockFlattener.IsBlockKind"/>).</summary>
        BlockFeatures Recognize(Paragraph paragraph);

        /// <summary>The feature a paragraph style gives, or <see cref="BlockFeatures.None"/>: the answer for "may this style be applied here?" before any
        /// paragraph has it. A paragraph's own properties (a bullet scheme) cannot be seen from a style name.</summary>
        BlockFeatures RecognizeStyle(string styleName);

        /// <summary>Makes the paragraph, which <see cref="Recognize"/> said is <paramref name="kind"/>, plain, in place: the plain style, no list properties, and
        /// for a table row one space where each cell separator was. The runs keep their writing systems and character formatting.</summary>
        void Flatten(Paragraph paragraph, BlockFeatures kind);
    }

    /// <summary>The default recogniser: by paragraph style names, which the host gives (ADR 0004: the field gives no style a meaning of its own). A paragraph
    /// with a bullet or number scheme of its own (<see cref="TextPropertyNumbers.BulNumScheme"/>) is a list item too, whatever its style. Today's table form
    /// is a tab between cells in a paragraph of a table style.</summary>
    public sealed class StyleBlockRecognizer : IBlockRecognizer
    {
        readonly HashSet<string> _headings, _lists, _tables;

        /// <param name="headingStyles">The heading styles.</param>
        /// <param name="listStyles">The list item styles.</param>
        /// <param name="tableStyles">The table row styles (header and body); none when the host has no table styles.</param>
        /// <param name="plainStyle">The style a flattened paragraph gets; default <c>Normal</c>, FieldWorks' name for it.</param>
        public StyleBlockRecognizer(IEnumerable<string>? headingStyles, IEnumerable<string>? listStyles, IEnumerable<string>? tableStyles, string plainStyle = "Normal")
        {
            _headings = Set(headingStyles);
            _lists = Set(listStyles);
            _tables = Set(tableStyles);
            PlainStyle = plainStyle ?? throw new ArgumentNullException(nameof(plainStyle));
        }

        static HashSet<string> Set(IEnumerable<string>? names) => new HashSet<string>((names ?? Enumerable.Empty<string>()).Where(n => n != null), StringComparer.Ordinal);

        /// <summary>The names a new FieldWorks project has: Heading 1 to 3, Bulleted List and Numbered List, and <b>no table style</b> (the host names those). With
        /// this recogniser no paragraph is a table row, so turning <see cref="BlockFeatures.Tables"/> off does nothing and <see cref="DocumentEditor.IsParagraphStyleAllowed"/>
        /// says yes to every table style; a host with tables gives the field a recogniser that names its table styles.</summary>
        public static StyleBlockRecognizer Default { get; } = new StyleBlockRecognizer(
            new[] { "Heading 1", "Heading 2", "Heading 3" }, new[] { "Bulleted List", "Numbered List" }, null);

        /// <summary>The style a flattened paragraph gets.</summary>
        public string PlainStyle { get; }

        public BlockFeatures RecognizeStyle(string styleName)
        {
            if (styleName == null) return BlockFeatures.None;
            if (_tables.Contains(styleName)) return BlockFeatures.Tables;
            if (_lists.Contains(styleName)) return BlockFeatures.Lists;
            if (_headings.Contains(styleName)) return BlockFeatures.Headings;
            return BlockFeatures.None;
        }

        public BlockFeatures Recognize(Paragraph paragraph)
        {
            var kind = RecognizeStyle(paragraph.Style);
            if (kind != BlockFeatures.None) return kind;
            return paragraph.Props.TryGetInt(TextPropertyNumbers.BulNumScheme, out int scheme) && scheme != 0 ? BlockFeatures.Lists : BlockFeatures.None;
        }

        public void Flatten(Paragraph paragraph, BlockFeatures kind) => BlockFlattener.ToPlain(paragraph, kind, PlainStyle);
    }

    /// <summary>What a flattening changed: how many paragraphs of each kind became plain. Reported to the host (<see cref="DocumentEditor.BlocksFlattened"/>).</summary>
    public sealed class BlockFlattening
    {
        internal BlockFlattening(int tableRows, int listItems, int headings)
        {
            TableRows = tableRows;
            ListItems = listItems;
            Headings = headings;
        }

        /// <summary>Paragraphs that were table rows.</summary>
        public int TableRows { get; }
        /// <summary>Paragraphs that were list items.</summary>
        public int ListItems { get; }
        /// <summary>Paragraphs that were headings.</summary>
        public int Headings { get; }
        /// <summary>The paragraphs changed.</summary>
        public int Total => TableRows + ListItems + Headings;
        /// <summary>The kinds that were flattened.</summary>
        public BlockFeatures Kinds => (TableRows > 0 ? BlockFeatures.Tables : 0) | (ListItems > 0 ? BlockFeatures.Lists : 0) | (Headings > 0 ? BlockFeatures.Headings : 0);

        public override string ToString() => TableRows + " table rows, " + ListItems + " list items, " + Headings + " headings";
    }

    /// <summary>Flattening: the one way a block that a field does not allow becomes plain paragraphs. Paste (and so Markdown import), and the record reader's fix for
    /// <c>fwmd.record.block-not-allowed</c>, both call <see cref="Flatten"/>, so they agree by construction (ADR 0033).</summary>
    public static class BlockFlattener
    {
        /// <summary>True for exactly one of <see cref="BlockFeatures.Tables"/>, <see cref="BlockFeatures.Lists"/> and <see cref="BlockFeatures.Headings"/>: the answers of
        /// <see cref="IBlockRecognizer.Recognize"/> that name a block. <see cref="BlockFeatures.None"/>, and an answer that combines flags, do not.</summary>
        public static bool IsBlockKind(BlockFeatures kind) => kind == BlockFeatures.Tables || kind == BlockFeatures.Lists || kind == BlockFeatures.Headings;

        /// <summary>Makes every paragraph of a kind not in <paramref name="allowed"/> plain, in place, and says what it changed. A table becomes one plain
        /// paragraph per row, each tab (the cell separator) becoming a space, a bar and a space, so the cells stay apart and the text is what a visible separator
        /// would give later (a tab cannot join them: a field's paragraphs hold no tab, and a rich value in a record file cannot either). A list item and a heading
        /// become plain paragraphs. The paragraphs are the caller's own copies.</summary>
        public static BlockFlattening Flatten(IEnumerable<Paragraph> paragraphs, BlockFeatures allowed, IBlockRecognizer recognizer)
        {
            if (paragraphs == null) throw new ArgumentNullException(nameof(paragraphs));
            if (recognizer == null) throw new ArgumentNullException(nameof(recognizer));
            int tables = 0, lists = 0, headings = 0;
            if ((allowed & BlockFeatures.All) != BlockFeatures.All)
            {
                foreach (var p in paragraphs)
                {
                    var kind = recognizer.Recognize(p);
                    if (!IsBlockKind(kind) || (allowed & kind) != 0) continue;
                    recognizer.Flatten(p, kind);
                    if (kind == BlockFeatures.Tables) tables++;
                    else if (kind == BlockFeatures.Lists) lists++;
                    else headings++;
                }
            }
            return new BlockFlattening(tables, lists, headings);
        }

        /// <summary>The standard flattening, for a recogniser to call: the style (and the style name a paragraph keeps in its properties) becomes
        /// <paramref name="plainStyle"/>; whatever the kind, the paragraph loses its own bullet and number properties (a heading with a bullet goes fully plain); a
        /// table row's tabs become " | ".</summary>
        public static void ToPlain(Paragraph paragraph, BlockFeatures kind, string plainStyle)
        {
            if (paragraph == null) throw new ArgumentNullException(nameof(paragraph));
            paragraph.Style = plainStyle;
            // The reader and the LCModel adapter keep the style name in the bag too; a stale name there would undo the change.
            if (paragraph.Props.StrProps.ContainsKey(TextPropertyNumbers.NamedStyle))
            {
                if (plainStyle.Length == 0) paragraph.Props.StrProps.Remove(TextPropertyNumbers.NamedStyle);
                else paragraph.Props.StrProps[TextPropertyNumbers.NamedStyle] = plainStyle;
            }
            {
                paragraph.Props.IntProps.Remove(TextPropertyNumbers.BulNumScheme);
                paragraph.Props.IntProps.Remove(TextPropertyNumbers.BulNumStartAt);
                paragraph.Props.StrProps.Remove(TextPropertyNumbers.BulNumTxtBefore);
                paragraph.Props.StrProps.Remove(TextPropertyNumbers.BulNumTxtAfter);
                paragraph.Props.StrProps.Remove(TextPropertyNumbers.BulNumFontInfo);
            }
            if (kind == BlockFeatures.Tables)
            {
                bool any = false;
                foreach (var r in paragraph.Runs)
                    if (!r.IsObject && r.Text.IndexOf('\t') >= 0) { r.Text = r.Text.Replace("\t", " | "); any = true; }
                if (any) paragraph.Normalize();
            }
        }
    }
}
