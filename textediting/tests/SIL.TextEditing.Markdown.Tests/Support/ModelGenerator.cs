using System;
using System.Collections.Generic;
using System.Text;

namespace SIL.TextEditing.Markdown.Tests
{
    /// <summary>Random documents that stress the notation: every property number the converter names and some it does not, several
    /// writing systems, objects of each kind, linguistic punctuation, invisible characters, surrogates, fences, tables and empty paragraphs.
    /// Text is NFD, as the editor stores it and the parser returns it (ADR 0018). The same seed always gives the same document, so a failure is reproduced by its seed.</summary>
    internal sealed class ModelGenerator
    {
        static readonly string[] Words = { "mang", "kaon", "palit", "ng", "a", "x", "Hello", "world", "naga", "k\u0301a", "cafe\u0301", "\u0645\u0631\u062D\u0628\u0627", "\u0E2A\u0E27\u0E31\u0E2A\u0E14\u0E35" };

        // The strings the spec's stress list names, plus every character the notation treats specially.
        static readonly string[] Specials =
        {
            "[ng]", "[+nasal]", "{a,b}", "/a/", "C -> [+nasal] / _V", "]{", "[[", "]]", "[[w:palit]]", "[[w:palit]]{guid=1}", "[x]{v}", "[x]{bold=on}", "](", "[x](y)", "![x](y)",
            "\\", "\\\\", "\\*", "\\[", "\\&#x20;", "#", "## x", "- ", "+ ", "* ", "> ", "1. ", "2) ", "---", "===", ":::", "::: {x}", "{empty}", "{empty style=\"x\"}", "|", " | ", "|---|",
            "\t", "  ", " ", "\u00A0", "\u2003", "\u2028", "\u2029", "\u0085", "\uFFFC", "\u0000", "\u0001", "\u007f", "\r", "\n", "\ud800", "\udfff", "\uD83D\uDE00", "\uFEFF", "\u200D",
            "*", "**", "_", "__", "***", "~", "`", "``", "<", "<b>", "</b>", "<http://x.y>", "&", "&amp;", "&#x20;", "&#0;", "&#xD800;", "&#x1F600;", "&nbsp;", "!", "!!",
            "\"", "'", "}", "{", "=", "a=b", "bold", "v", "ws=en", "{ws=en}", "[", "]", "(", ")", ".", ":", ";", ",", "-", "_V", "[+", "+]",
        };

        static readonly string[] Writing = { "en", "ceb", "ar", "ceb-x-old", "x y", "q\"z" };
        static readonly string[] CharStyles = { "Strong", "Emphasized Text", "Hyperlink", "none", "", "a b", "q\"uote", "x|y", "caf\u00E9", "{b}" };
        static readonly string[] ParagraphStyles =
        {
            "Normal", "Normal", "Normal", "Heading 1", "Heading 2", "Heading 3", "Heading 4", "Bulleted List", "Bulleted List", "Numbered List", "Block Quote",
            "GDR Table Header", "GDR Table Row", "Example", "", "none", "a b", "x\"y", "Normal ",
        };

        static readonly int[] ToggleNumbers = { 2, 3 };
        static readonly int[] EnumNumbers = { 4, 5, 17, 26, 155 };
        static readonly int[] MeasureNumbers = { 6, 7, 18, 19, 20, 21, 22, 23, 24, 50, 134, 135, 136, 137, 138, 139, 140, 141 };
        static readonly int[] ColorNumbers = { 8, 9, 10, 25, 142 };
        static readonly int[] PlainNumbers = { 128, 129, 143, 144, 148, 149, 162 };
        static readonly int[] NamedStrings = { 1, 4, 5, 130, 145, 146 };
        static readonly int[] Variations = { 0, 1, 2, 3, 15, 5 };

        readonly Random _random;
        readonly TestObjects _objects;
        readonly List<Guid> _entries = new List<Guid>();
        readonly List<Guid> _pictures = new List<Guid>();

        public ModelGenerator(int seed, TestObjects objects)
        {
            _random = new Random(seed);
            _objects = objects;
        }

        // Taken from the seeded generator, so a seed gives the same GUIDs and so the same object data.
        Guid NextGuid()
        {
            var bytes = new byte[16];
            _random.NextBytes(bytes);
            return new Guid(bytes);
        }

        bool Chance(int percent) => _random.Next(100) < percent;
        T Pick<T>(IReadOnlyList<T> items) => items[_random.Next(items.Count)];

        public Document NextDocument()
        {
            var document = new Document();
            int count = _random.Next(0, 7);
            for (int i = 0; i < count; i++)
            {
                if (Chance(15)) { AddTable(document); continue; }
                document.Paragraphs.Add(NextParagraph());
            }
            return document;
        }

        void AddTable(Document document)
        {
            int columns = _random.Next(1, 4);
            document.Paragraphs.Add(TableParagraph("GDR Table Header", columns));
            int rows = _random.Next(0, 4);
            for (int r = 0; r < rows; r++)
                document.Paragraphs.Add(TableParagraph("GDR Table Row", Chance(15) ? _random.Next(1, 4) : columns));
        }

        Paragraph TableParagraph(string style, int columns)
        {
            var paragraph = NewParagraph(style);
            for (int c = 0; c < columns; c++)
            {
                if (c > 0) paragraph.Runs.Add(new Run { Text = "\t", Ws = "en" });
                int runs = _random.Next(0, 3);
                for (int k = 0; k < runs; k++) paragraph.Runs.Add(NextRun(allowTab: false, allowObject: Chance(30)));
            }
            Finish(paragraph);
            return paragraph;
        }

        Paragraph NewParagraph(string style)
        {
            var paragraph = new Paragraph { Style = style };
            if (style.Length > 0) paragraph.Props.StrProps[TextPropertyNumbers.NamedStyle] = style;
            return paragraph;
        }

        void Finish(Paragraph paragraph)
        {
            paragraph.DefaultWs = paragraph.Runs.Count > 0 ? paragraph.Runs[0].Ws : Pick(new[] { "en", "ceb", "ar" });
            // Runs the way the adapter builds them: an empty run is never kept.
            for (int i = paragraph.Runs.Count - 1; i >= 0; i--) if (paragraph.Runs[i].Text.Length == 0) paragraph.Runs.RemoveAt(i);
            paragraph.DefaultWs = paragraph.Runs.Count > 0 ? paragraph.Runs[0].Ws : paragraph.DefaultWs;
        }

        public Paragraph NextParagraph()
        {
            var paragraph = NewParagraph(Pick(ParagraphStyles));
            if (Chance(18)) RandomProps(paragraph.Props, paragraph: true);
            if (Chance(8)) paragraph.Rtl = true;
            if (paragraph.Rtl && !paragraph.Props.IntProps.ContainsKey(128)) paragraph.Props.SetInt(128, 1);
            if (!paragraph.Rtl && paragraph.Props.IntProps.TryGetValue(128, out var rtl) && rtl.Value != 0) paragraph.Rtl = true;
            if (!Chance(12))
            {
                int runs = _random.Next(1, 6);
                for (int i = 0; i < runs; i++) paragraph.Runs.Add(NextRun(allowTab: true, allowObject: true));
            }
            Finish(paragraph);
            return paragraph;
        }

        Run NextRun(bool allowTab, bool allowObject)
        {
            string ws = Chance(60) ? "en" : Pick(Writing);
            var run = new Run { Ws = ws };
            if (allowObject && Chance(14)) { MakeObject(run); }
            else run.Text = Text(allowTab);
            if (run.Text.Length == 0) run.Text = "x";
            if (Chance(35)) RandomProps(run.Props, paragraph: false);
            if (Chance(15)) run.Props.SetStr(TextPropertyNumbers.NamedStyle, Pick(CharStyles));
            if (run.ObjData != null && Chance(60) && !run.Props.StrProps.ContainsKey(TextPropertyNumbers.NamedStyle) && IsLink(run.ObjData)) run.Props.SetStr(TextPropertyNumbers.NamedStyle, "Hyperlink");
            return run;
        }

        static bool IsLink(string objData) => objData.Length > 0 && objData[0] == TestObjects.ExternalPath;

        void MakeObject(Run run)
        {
            switch (_random.Next(5))
            {
                case 0: // a picture the host knows
                {
                    var guid = NextGuid();
                    _objects.Pictures.Add(guid);
                    _pictures.Add(guid);
                    run.Text = "\uFFFC";
                    run.ObjData = TestObjects.PictureData(guid);
                    break;
                }
                case 1: // a link to a project object, in name form or text form
                {
                    var guid = NextGuid();
                    string kind = Pick(new[] { "w", "rule", "exc", "tmpl", "cat" });
                    string name = Chance(50) ? Pick(Words) : Text(false);
                    if (name.Length == 0) name = "n";
                    _objects.Names[guid] = name;
                    _entries.Add(guid);
                    run.Text = Chance(60) ? name : Text(false);
                    run.ObjData = TestObjects.LinkData(kind, guid);
                    break;
                }
                case 2: // an external link
                    run.Text = Text(false);
                    run.ObjData = TestObjects.ExternalPath + Pick(new[] { "https://example.org/a?b=1&c=2", "https://x.org", "a b)", "x|y", "", "&amp;", "<a>", "\u00E9", "a\u0001b", "[x](y)", "ok_(1)" });
                    break;
                case 3: // some other object, picture the host does not know
                {
                    run.Text = "\uFFFC";
                    run.ObjData = Chance(50) ? TestObjects.PictureData(NextGuid()) : TestObjects.OwnName + Text(false);
                    break;
                }
                default: // anything at all
                    run.Text = Chance(70) ? "\uFFFC" : Text(false);
                    run.ObjData = Text(true);
                    break;
            }
        }

        string Text(bool allowTab)
        {
            var sb = new StringBuilder();
            int pieces = _random.Next(0, 7);
            for (int i = 0; i < pieces; i++)
            {
                string piece = Chance(55) ? Pick(Words) : Chance(80) ? Pick(Specials) : RandomChars();
                if (!allowTab) piece = piece.Replace("\t", " ");
                sb.Append(piece);
                if (Chance(55)) sb.Append(' ');
            }
            return sb.ToString();
        }

        string RandomChars()
        {
            var sb = new StringBuilder();
            int n = _random.Next(1, 4);
            for (int i = 0; i < n; i++) sb.Append((char)_random.Next(0x20, 0x7F));
            return sb.ToString();
        }

        int Value()
        {
            switch (_random.Next(8))
            {
                case 0: return 0;
                case 1: return 1;
                case 2: return -1;
                case 3: return int.MinValue;
                case 4: return int.MaxValue;
                case 5: return -1073741824;
                case 6: return _random.Next(0, 0x1000000);
                default: return _random.Next(-20000, 20000);
            }
        }

        void RandomProps(PropertyBag bag, bool paragraph)
        {
            int count = _random.Next(1, 5);
            for (int i = 0; i < count; i++)
            {
                switch (_random.Next(8))
                {
                    case 0: bag.SetInt(Pick(ToggleNumbers), Chance(70) ? _random.Next(0, 3) : Value(), Chance(75) ? 3 : Pick(Variations)); break;
                    case 1: bag.SetInt(Pick(EnumNumbers), Chance(70) ? _random.Next(0, 7) : Value(), Chance(75) ? 3 : Pick(Variations)); break;
                    case 2: bag.SetInt(Pick(MeasureNumbers), Value(), Chance(75) ? Pick(new[] { 1, 2 }) : Pick(Variations)); break;
                    case 3: bag.SetInt(Pick(ColorNumbers), Value(), Chance(75) ? 0 : Pick(Variations)); break;
                    case 4: bag.SetInt(Pick(PlainNumbers), Value(), Chance(75) ? 0 : Pick(Variations)); break;
                    case 5: bag.SetStr(Pick(NamedStrings), Chance(60) ? Pick(Words) : Text(false)); break;
                    case 6:
                    {
                        int number = _random.Next(2, 400);
                        if (number == 133) number = 134;
                        bag.SetInt(number, Value(), Pick(Variations));
                        break;
                    }
                    default:
                    {
                        int number = Pick(new[] { 2, 3, 7, 147, 156, 999, 12345 });
                        bag.SetStr(number, Chance(60) ? Pick(Words) : Text(false));
                        break;
                    }
                }
            }
        }
    }
}
