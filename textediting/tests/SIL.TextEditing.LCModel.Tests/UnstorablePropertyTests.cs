using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.Core.Text;
using SIL.TextEditing.Markdown;
using Xunit;
using static SIL.TextEditing.LCModel.Tests.Fixtures;

namespace SIL.TextEditing.LCModel.Tests
{
    /// <summary>The adapter never hands liblcm a property, or a value of one, that its own serializers cannot take back (ADR 0016,
    /// amended for issues 136 and 139). Such a property stays in the run model's bag, is left out of the ITsString or StyleRules the
    /// adapter builds, and is reported through the callback. What is refused, and why, is pinned in <see cref="LiblcmRefusalSweepTests"/>;
    /// these tests drive each hand-off point.</summary>
    public class UnstorablePropertyTests
    {
        static readonly IWritingSystemHandles H = Handles.Instance;

        // Properties liblcm refuses by number: it throws on every value of each, in a paragraph's StyleRules. Found by asking liblcm, not
        // listed by hand; LiblcmRefusalSweepTests.Properties_refused_by_number_are_exactly_those_liblcm_throws_on_for_every_value
        // checks the whole domain.
        public static TheoryData<int> Refused()
        {
            var data = new TheoryData<int>();
            foreach (int tpt in LiblcmOracle.IntNumbers.Where(n => new[] { (1, 0), (3, 1), (0, 1), (2, 2), (1, 500) }
                .All(vv => LiblcmOracle.Int(true, n, vv.Item1, vv.Item2) == LiblcmOracle.Outcome.Throws)))
                if (tpt != 155) data.Add(tpt);   // 155 (editable) is refused only in StyleRules; it has its own test
            return data;
        }

        static Run RunWith(int tpt, int variation = 0, int value = 1)
        {
            var run = new Run { Text = "cell", Ws = "en" };
            run.Props.IntProps[Bold] = new IntPropValue(VarEnum, ForceOn);
            run.Props.IntProps[tpt] = new IntPropValue(variation, value);
            return run;
        }

        static Paragraph ParagraphWith(int tpt, int variation, int value)
        {
            var p = new Paragraph { Style = "Table Cell", DefaultWs = "en", Runs = new RunList(new[] { new Run { Text = "x", Ws = "en" } }) };
            p.Props.IntProps[tpt] = new IntPropValue(variation, value);
            if (tpt == Rtl) p.Rtl = value != 0;
            return p;
        }

        static int[] Ints(ITsTextProps p)
        {
            var list = new List<int>();
            for (int i = 0; i < p.IntPropCount; i++) { p.GetIntProp(i, out int t, out _); list.Add(t); }
            return list.OrderBy(x => x).ToArray();
        }

        static ITsTextProps LoadBack(ITsString tss)
        {
            Assert.True(LiblcmOracle.LoadsString(tss), "liblcm cannot load the string the adapter built");
            return TsStringClipboard.FromXml(TsStringClipboard.ToXml(tss, H), H).get_Properties(0);
        }

        // ---- a property liblcm refuses by number ----------------------------------------------------------------------------------------

        [Theory]
        [MemberData(nameof(Refused))]
        public void A_run_property_liblcm_refuses_is_left_out_and_reported(int tpt)
        {
            var run = RunWith(tpt);
            var dropped = new List<DroppedProperty>();
            var tss = TsStringConversion.ToTsString(new List<Run> { run }, H, "en", dropped.Add);

            var d = Assert.Single(dropped);
            Assert.Equal(tpt, d.Number);
            Assert.False(d.IsString);
            Assert.Equal(0, d.RunIndex);
            Assert.Null(d.ParagraphIndex);
            Assert.Equal(DroppedProperty.DiagnosticId, d.Id);
            Assert.Equal(DroppedReason.PropertyNotLoadable, d.Reason);
            Assert.Null(d.Detail);
            // The string loads back through liblcm's own XML reader, and keeps what liblcm can store.
            Assert.Equal(new[] { Ws, Bold }, Ints(LoadBack(tss)));
            // The model is not touched.
            Assert.True(run.Props.IntProps.ContainsKey(tpt));
        }

        [Fact]
        public void No_callback_still_drops_without_throwing()
        {
            var tss = TsStringConversion.ToTsString(new List<Run> { RunWith(152) }, H, "en");
            LoadBack(tss);
            var rules = TsStringConversion.ToStyleRules(ParagraphWith(Bold, VarEnum, 7));
            Assert.Equal("Table Cell", rules!.GetStrPropValue(NamedStyle));
        }

        [Theory]
        [InlineData(161, false)] [InlineData(9000, false)] [InlineData(9001, true)] [InlineData(4, true)] [InlineData(23, false)]
        public void A_property_liblcm_drops_without_error_still_passes_through_the_bag(int tpt, bool isString)
        {
            var run = new Run { Text = "x", Ws = "en" };
            if (isString) run.Props.StrProps[tpt] = "v"; else run.Props.IntProps[tpt] = new IntPropValue(0, 1);
            var dropped = new List<DroppedProperty>();
            var tss = TsStringConversion.ToTsString(new List<Run> { run }, H, "en", dropped.Add);
            Assert.Empty(dropped);
            var p = tss.get_Properties(0);
            if (isString) Assert.Equal("v", p.GetStrPropValue(tpt)); else Assert.Equal(1, p.GetIntPropValues(tpt, out _));
        }

        [Fact]
        public void Editable_stays_on_a_run_and_is_refused_in_StyleRules()
        {
            var run = new Run { Text = "x", Ws = "en" };
            run.Props.IntProps[155] = new IntPropValue(3, 0);
            var dropped = new List<DroppedProperty>();
            var tss = TsStringConversion.ToTsString(new List<Run> { run }, H, "en", dropped.Add);
            Assert.Empty(dropped);
            Assert.Equal(0, tss.get_Properties(0).GetIntPropValues(155, out _));

            var paragraph = new Paragraph { Style = "Normal" };
            paragraph.Props.IntProps[155] = new IntPropValue(3, 0);
            var rules = TsStringConversion.ToStyleRules(paragraph, dropped.Add)!;
            Assert.Equal(155, Assert.Single(dropped).Number);
            Assert.Equal(0, rules.IntPropCount);
        }

        [Theory]
        [MemberData(nameof(Refused))]
        public void StyleRules_liblcm_refuses_are_left_out_and_load_back(int tpt)
        {
            var paragraph = new Paragraph { Style = "Table Cell" };
            paragraph.Props.IntProps[tpt] = new IntPropValue(0, 1);
            var dropped = new List<DroppedProperty>();
            var rules = TsStringConversion.ToStyleRules(paragraph, dropped.Add)!;
            var d = Assert.Single(dropped);
            Assert.Equal(tpt, d.Number);
            Assert.Null(d.RunIndex);
            Assert.True(LiblcmOracle.Loads(true, rules));
            var back = TsStringUtils.GetTextProps(XElement.Parse(TsStringUtils.GetXmlRep(rules, null!)), null!);
            Assert.Equal("Table Cell", back.GetStrPropValue(NamedStyle));
        }

        [Fact]
        public void StyleRules_holding_only_refused_properties_becomes_null()
        {
            var paragraph = new Paragraph();
            paragraph.Props.IntProps[152] = new IntPropValue(0, 1);
            Assert.Null(TsStringConversion.ToStyleRules(paragraph));
        }

        // ---- a value liblcm refuses (issue 139) -----------------------------------------------------------------------------------------

        public static TheoryData<int, int, int, string> IntValuesLiblcmRefuses() => new TheoryData<int, int, int, string>
        {
            { 3, VarEnum, 7, "bold takes 0, 1 or 2" },
            { 2, VarEnum, -1, "italic takes 0, 1 or 2" },
            { 4, VarEnum, 3, "superscript takes 0, 1 or 2" },
            { 5, VarEnum, 7, "underline takes 0 to 6" },
            { 17, VarEnum, 6, "alignment takes 0 to 5" },
            { 26, VarEnum, 3, "spell check takes 0, 1 or 2" },
            { 6, VarMilli, -12000, "negative font size" },
            { 6, VarEnum, 12000, "font size in the enum variation" },
            { 7, 0, 100, "offset with no unit" },
            { 24, VarEnum, 1, "line height in the enum variation" },
            { 128, VarEnum, -1, "rightToLeft (rtl) negative" },
            { 134, VarMilli, -5, "padLeading negative" },
            { 136, VarMilli, -5, "padTop negative" },
            { 138, VarMilli, -5, "borderTop negative" },
            { 143, VarEnum, -1, "bulNumScheme negative" },
            { 149, VarEnum, -2, "keepTogether negative" },
            { 162, VarEnum, -1, "widowOrphan negative" },
            { 19, VarMilli, -100, "leading margin negative" },
            { 20, VarMilli, -100, "trailing margin negative" },
        };

        [Theory]
        [MemberData(nameof(IntValuesLiblcmRefuses))]
        public void A_run_value_liblcm_refuses_is_left_out_and_reported_with_its_reason(int tpt, int variation, int value, string what)
        {
            var run = RunWith(tpt, variation, value);
            var dropped = new List<DroppedProperty>();
            var tss = TsStringConversion.ToTsString(new List<Run> { run }, H, "en", dropped.Add);

            var d = Assert.Single(dropped);
            Assert.Equal((tpt, DroppedReason.ValueNotLoadable, 0), (d.Number, d.Reason, d.RunIndex));
            Assert.False(string.IsNullOrEmpty(d.Detail), what);
            Assert.Contains("cannot load this value back", d.Message);
            Assert.Equal(tpt == Bold ? new[] { Ws } : new[] { Ws, Bold }, Ints(LoadBack(tss)));
            Assert.Equal(new IntPropValue(variation, value), run.Props.IntProps[tpt]);   // the bag is untouched
        }

        [Fact]
        public void A_StyleRules_value_liblcm_refuses_is_left_out_and_reported()
        {
            var paragraph = ParagraphWith(136, VarMilli, -5);
            var dropped = new List<DroppedProperty>();
            var rules = TsStringConversion.ToStyleRules(paragraph, dropped.Add)!;
            var d = Assert.Single(dropped);
            Assert.Equal((136, DroppedReason.ValueNotLoadable, (int?)null), (d.Number, d.Reason, d.RunIndex));
            Assert.True(LiblcmOracle.Loads(true, rules));
            Assert.Equal("Table Cell", rules.GetStrPropValue(NamedStyle));
            Assert.Equal(-5, paragraph.Props.IntProps[136].Value);
        }

        [Fact]
        public void A_negative_offset_is_refused_on_a_run_and_kept_in_StyleRules()
        {
            // liblcm's run reader refuses a negative offset (a subscript offset); its props reader allows it.
            var run = RunWith(7, VarMilli, -2000);
            var dropped = new List<DroppedProperty>();
            TsStringConversion.ToTsString(new List<Run> { run }, H, "en", dropped.Add);
            Assert.Equal(7, Assert.Single(dropped).Number);

            dropped.Clear();
            var rules = TsStringConversion.ToStyleRules(ParagraphWith(7, VarMilli, -2000), dropped.Add)!;
            Assert.Empty(dropped);
            Assert.Equal(-2000, rules.GetIntPropValues(7, out _));
        }

        [Theory]
        [InlineData(8, 0, -1)]            // a colour out of range is changed by liblcm, not refused
        [InlineData(25, 0, int.MaxValue)]
        [InlineData(24, 2, -50)]          // a negative relative line height is read as its absolute value
        [InlineData(144, 0, int.MinValue)]
        [InlineData(18, 1, -36000)]       // a negative first indent is allowed
        [InlineData(21, 1, -36000)]
        [InlineData(6, 1, 12000)]
        [InlineData(3, 3, 2)]
        public void A_value_liblcm_loads_is_kept(int tpt, int variation, int value)
        {
            var dropped = new List<DroppedProperty>();
            var tss = TsStringConversion.ToTsString(new List<Run> { RunWith(tpt, variation, value) }, H, "en", dropped.Add);
            Assert.Empty(dropped);
            LoadBack(tss);
        }

        [Fact]
        public void Real_liblcm_strings_are_never_refused()
        {
            foreach (var tss in new[] { Mixed(), Arabic(), Nfd(), Empty(), WithPictureAndLink() })
            {
                var dropped = new List<DroppedProperty>();
                var runs = TsStringConversion.ToRuns(tss, H);
                var back = TsStringConversion.ToTsString(runs, H, "en", dropped.Add);
                Assert.Empty(dropped);
                Assert.Equal(Dump(tss), Dump(back));
            }
        }

        public static TheoryData<int, string, string> StringValuesLiblcmRefuses() => new TheoryData<int, string, string>
        {
            { NamedStyle, "Bad\u0001Style", "a control character XML cannot carry" },
            { 1, "Font\uD800", "a lone surrogate" },
            { 130, "wght=\u000B", "a vertical tab" },
            { 145, "￾", "a non-character" },
            { 2, "legacy\u0002", "charStyle is dropped on read but the writer still throws" },
            { 4, "tab\u0003", "tabList likewise" },
            { 5, "x", "tags are GUIDs: fewer than eight characters" },
            { 5, "", "empty tags" },
            { 6, "\u0003", "link object data with no GUID" },
            { 6, "\u0008abc", "moveable-object data with a short GUID" },
            { 6, "\u0004a\u0001b", "external path with an XML-invalid character" },
        };

        [Theory]
        [MemberData(nameof(StringValuesLiblcmRefuses))]
        public void A_string_value_liblcm_refuses_is_left_out_and_reported(int tpt, string value, string what)
        {
            var run = new Run { Text = "x", Ws = "en" };
            run.Props.StrProps[tpt] = value;
            var dropped = new List<DroppedProperty>();
            var tss = TsStringConversion.ToTsString(new List<Run> { run }, H, "en", dropped.Add);
            var d = Assert.Single(dropped);
            Assert.Equal((tpt, true, DroppedReason.ValueNotLoadable), (d.Number, d.IsString, d.Reason));
            Assert.False(string.IsNullOrEmpty(d.Detail), what);
            Assert.Equal(new[] { Ws }, Ints(LoadBack(tss)));
            Assert.Equal(value, run.Props.StrProps[tpt]);
        }

        [Fact]
        public void An_object_run_whose_data_liblcm_refuses_loses_only_the_data()
        {
            var run = new Run { Text = "￼", Ws = "en", ObjData = "\u0003short" };
            var dropped = new List<DroppedProperty>();
            var tss = TsStringConversion.ToTsString(new List<Run> { run }, H, "en", dropped.Add);
            var d = Assert.Single(dropped);
            Assert.Equal((6, true, 0), (d.Number, d.IsString, d.RunIndex));
            LoadBack(tss);
            Assert.Equal("\u0003short", run.ObjData);
        }

        [Fact]
        public void A_packed_StyleRules_value_is_checked_by_liblcm_and_a_run_does_not_write_it()
        {
            var paragraph = ParagraphWith(Bold, VarEnum, 1);
            paragraph.Props.StrProps[147] = "x";   // not a record
            var dropped = new List<DroppedProperty>();
            var rules = TsStringConversion.ToStyleRules(paragraph, dropped.Add)!;
            Assert.Equal(147, Assert.Single(dropped).Number);
            Assert.True(LiblcmOracle.Loads(true, rules));

            paragraph.Props.StrProps[147] = "\u0003\u0001\u0000";   // bold on: well formed
            dropped.Clear();
            Assert.True(LiblcmOracle.Loads(true, TsStringConversion.ToStyleRules(paragraph, dropped.Add)!));
            Assert.Empty(dropped);

            var run = new Run { Text = "x", Ws = "en" };
            run.Props.StrProps[147] = "x";
            TsStringConversion.ToTsString(new List<Run> { run }, H, "en", dropped.Add);
            Assert.Empty(dropped);   // liblcm's run serializer never writes it
        }

        // ---- every hand-off point -------------------------------------------------------------------------------------------------------

        [Fact]
        public void A_single_empty_run_is_checked_and_reported_at_index_zero()
        {
            var run = RunWith(152);
            run.Text = string.Empty;
            run.Props.IntProps[3] = new IntPropValue(VarEnum, 9);
            var dropped = new List<DroppedProperty>();
            var tss = TsStringConversion.ToTsString(new List<Run> { run }, H, "en", dropped.Add);
            Assert.Equal(new[] { 3, 152 }, dropped.Select(d => d.Number).OrderBy(n => n));
            Assert.All(dropped, d => Assert.Equal(0, d.RunIndex));
            Assert.Equal(0, tss.Length);
            Assert.Equal(new[] { Ws }, Ints(tss.get_Properties(0)));
            Assert.True(LiblcmOracle.LoadsString(tss));
        }

        [Fact]
        public void A_document_reports_the_paragraph_and_run()
        {
            var doc = new Document();
            doc.Paragraphs.Add(new Paragraph { Runs = new RunList(new[] { new Run { Text = "a", Ws = "en" } }), DefaultWs = "en" });
            doc.Paragraphs.Add(new Paragraph { Runs = new RunList(new[] { new Run { Text = "b", Ws = "en" }, RunWith(154) }), DefaultWs = "en" });
            var dropped = new List<DroppedProperty>();
            var result = TsStringConversion.ToTextParagraphs(doc, H, dropped.Add);
            var d = Assert.Single(dropped);
            Assert.Equal((1, 1, 154), (d.ParagraphIndex, d.RunIndex, d.Number));
            Assert.True(LiblcmOracle.LoadsString(result[1].Contents));
        }

        [Fact]
        public void Markdown_property_attributes_are_refused_at_the_adapter_in_a_paragraph_and_its_runs()
        {
            // Nothing in the Markdown reader stops these: MarginTop is ktptMarginTop (liblcm refuses it by number), p152 is cellBorderWidth
            // (likewise), padTop=-5 and rightToLeft=-1 are values liblcm refuses, and the run holds bold=7.
            var parse = MarkdownConverter.Parse("::: {MarginTop=5 padTop=-5 p152=1 rightToLeft=-1}\nplain [x]{p3=7:3}\n:::\n");
            var dropped = new List<DroppedProperty>();
            var paragraphs = TsStringConversion.ToTextParagraphs(parse.Document, H, dropped.Add);

            var style = dropped.Where(d => d.RunIndex == null).Select(d => d.Number).OrderBy(n => n).ToArray();
            Assert.Equal(new[] { 50, 128, 136, 152 }, style);
            Assert.All(dropped, d => Assert.Equal(0, d.ParagraphIndex));
            Assert.Contains(dropped, d => d.RunIndex != null && d.Number == 3 && d.Reason == DroppedReason.ValueNotLoadable);
            Assert.True(LiblcmOracle.LoadsString(paragraphs[0].Contents));
            Assert.True(paragraphs[0].StyleRules == null || LiblcmOracle.Loads(true, paragraphs[0].StyleRules!));
            // The document still holds what was written.
            Assert.Equal(-5, parse.Document.Paragraphs[0].Props.IntProps[136].Value);
        }

        [Fact]
        public void A_refused_rightToLeft_is_replaced_by_the_direction_the_editor_shows()
        {
            var parse = MarkdownConverter.Parse("::: {rightToLeft=-1}\ntext\n:::\n");
            var paragraph = parse.Document.Paragraphs[0];
            Assert.True(paragraph.Rtl);
            var dropped = new List<DroppedProperty>();
            var rules = TsStringConversion.ToStyleRules(paragraph, dropped.Add)!;

            var d = Assert.Single(dropped);
            Assert.Equal((128, DroppedReason.ValueReplaced), (d.Number, d.Reason));
            Assert.Contains("replaced", d.Message);
            Assert.Equal(DroppedProperty.ReplacedDiagnosticId, d.Id);
            Assert.NotEqual(DroppedProperty.DiagnosticId, d.Id);
            Assert.Equal("textediting.lcm.replaced-property", d.Id);
            Assert.True(LiblcmOracle.Loads(true, rules));
            var back = TsStringUtils.GetTextProps(XElement.Parse(TsStringUtils.GetXmlRep(rules, null!)), null!);
            Assert.Equal(1, back.GetIntPropValues(Rtl, out _));
            Assert.Equal(-1, paragraph.Props.IntProps[Rtl].Value);   // the bag is untouched
        }
        [Fact]
        public void A_copied_fragment_loads_back_without_the_refused_property()
        {
            var p = new Paragraph { Runs = new RunList(new[] { RunWith(160) }), DefaultWs = "en" };
            var dropped = new List<DroppedProperty>();
            var xml = TsStringClipboard.FragmentToXml(new[] { p }, H, onDropped: dropped.Add);
            Assert.Single(dropped);
            TsStringClipboard.FromXml(xml, H);
        }

        [Fact]
        public void A_copied_fragment_reports_the_index_into_its_joined_runs_and_no_paragraph()
        {
            var first = new Paragraph { Runs = new RunList(new[] { new Run { Text = "a", Ws = "en" } }), DefaultWs = "en" };
            var second = new Paragraph { Runs = new RunList(new[] { new Run { Text = "b", Ws = "en" }, RunWith(Bold, VarEnum, 9) }), DefaultWs = "en" };
            var dropped = new List<DroppedProperty>();
            var xml = TsStringClipboard.FragmentToXml(new[] { first, second }, H, onDropped: dropped.Add);
            var d = Assert.Single(dropped);
            // Runs: "a", the separator, "b", "cell": the refused value is on the fourth.
            Assert.Equal((3, (int?)null), (d.RunIndex, d.ParagraphIndex));
            TsStringClipboard.FromXml(xml, H);
        }

        [Fact]
        public void The_clipboard_form_reports_what_a_copy_leaves_out_and_still_writes_a_loadable_payload()
        {
            var form = new TsStringClipboardForm(H);
            var fragment = new[] { new Paragraph { Runs = new RunList(new[] { RunWith(152), RunWith(Bold, VarEnum, 5) }), DefaultWs = "en" } };

            Assert.NotNull(form.Write(fragment));   // no callback: dropped silently
            var dropped = new List<DroppedProperty>();
            form.OnDropped = dropped.Add;
            var xml = (string)form.Write(fragment)!;
            Assert.Equal(new[] { 3, 152 }, dropped.Select(d => d.Number).OrderBy(n => n));
            Assert.All(dropped, d => Assert.Null(d.ParagraphIndex));
            TsStringClipboard.FromXml(xml, H);
            Assert.Null(form.Write(new Paragraph[0]));
        }

        // ---- every swept failure, through the adapter -----------------------------------------------------------------------------------

        [Fact]
        public void Every_case_liblcm_throws_on_is_left_out_of_what_the_adapter_builds()
        {
            int cases = 0;
            var failures = new List<string>();
            foreach (var (tpt, variation, value) in LiblcmOracle.IntCases())
            {
                foreach (bool styleRules in new[] { true, false })
                {
                    if (LiblcmOracle.SweptInt(styleRules, tpt, variation, value) != LiblcmOracle.Outcome.Throws) continue;
                    cases++;
                    var dropped = new List<DroppedProperty>();
                    bool loads;
                    if (styleRules)
                    {
                        var rules = TsStringConversion.ToStyleRules(ParagraphWith(tpt, variation, value), dropped.Add);
                        loads = rules == null || LiblcmOracle.Loads(true, rules);
                    }
                    else
                    {
                        var run = new Run { Text = "x", Ws = "en" };
                        run.Props.IntProps[tpt] = new IntPropValue(variation, value);
                        loads = LiblcmOracle.LoadsString(TsStringConversion.ToTsString(new List<Run> { run }, H, "en", dropped.Add));
                    }
                    var expected = TsStringConversion.IsRefusedByLiblcm(tpt, styleRules) ? DroppedReason.PropertyNotLoadable
                        : styleRules && tpt == Rtl ? DroppedReason.ValueReplaced : DroppedReason.ValueNotLoadable;
                    if (!loads || dropped.Count != 1 || dropped[0].Number != tpt || dropped[0].Reason != expected)
                        failures.Add($"{(styleRules ? "StyleRules" : "run")} {tpt} var {variation} value {value}");
                }
            }
            foreach (var (tpt, value) in LiblcmOracle.StringCases())
            {
                foreach (bool styleRules in new[] { true, false })
                {
                    if (LiblcmOracle.SweptStr(styleRules, tpt, value) != LiblcmOracle.Outcome.Throws) continue;
                    cases++;
                    var dropped = new List<DroppedProperty>();
                    bool loads;
                    if (styleRules)
                    {
                        var p = new Paragraph { Style = tpt == NamedStyle ? value : string.Empty };
                        p.Props.StrProps[tpt] = value;
                        var rules = TsStringConversion.ToStyleRules(p, dropped.Add);
                        loads = rules == null || LiblcmOracle.Loads(true, rules);
                    }
                    else
                    {
                        var run = new Run { Text = "x", Ws = "en" };
                        run.Props.StrProps[tpt] = value;
                        loads = LiblcmOracle.LoadsString(TsStringConversion.ToTsString(new List<Run> { run }, H, "en", dropped.Add));
                    }
                    if (!loads || dropped.Count != 1 || dropped[0].Number != tpt || !dropped[0].IsString)
                        failures.Add($"{(styleRules ? "StyleRules" : "run")} string {tpt} '{value.Replace("\0", "\\0")}'");
                }
            }
            Assert.True(cases > 1000, "the sweep should find a large set of failures, found " + cases);
            Assert.True(failures.Count == 0, failures.Count + " of " + cases + " cases still reach liblcm, first: " + string.Join(" | ", failures.Take(15)));
        }
    }
}
