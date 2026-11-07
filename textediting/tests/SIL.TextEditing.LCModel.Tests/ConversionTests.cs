using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.Core.Text;
using Xunit;
using static SIL.TextEditing.LCModel.Tests.Fixtures;

namespace SIL.TextEditing.LCModel.Tests
{
    public class ConversionTests
    {
        static readonly IWritingSystemHandles H = Handles.Instance;

        public static IEnumerable<object[]> Strings()
        {
            yield return new object[] { "mixed", (Func<ITsString>)Mixed };
            yield return new object[] { "arabic", (Func<ITsString>)Arabic };
            yield return new object[] { "nfd", (Func<ITsString>)Nfd };
            yield return new object[] { "empty", (Func<ITsString>)Empty };
            yield return new object[] { "picture and link", (Func<ITsString>)WithPictureAndLink };
        }

        [Theory]
        [MemberData(nameof(Strings))]
        public void ITsString_round_trips_unchanged(string name, Func<ITsString> make)
        {
            var original = make();
            var runs = TsStringConversion.ToRuns(original, H);
            var back = TsStringConversion.ToTsString(runs, H, "en");
            Assert.True(name.Length > 0);
            Assert.Equal(Dump(original), Dump(back));
        }

        [Fact]
        public void Bold_variants_keep_their_variation_and_value_in_the_bag()
        {
            var runs = TsStringConversion.ToRuns(Mixed(), H);
            Assert.Equal(new IntPropValue(VarEnum, ForceOn), runs[1].Props.IntProps[Bold]);
            Assert.Equal(new IntPropValue(VarEnum, Off), runs[2].Props.IntProps[Bold]);
            Assert.Equal(new IntPropValue(VarEnum, Invert), runs[3].Props.IntProps[Bold]);
            Assert.Equal("Emphasis", runs[4].Props.StrProps[NamedStyle]);
            Assert.Equal("fr", runs[4].Ws);
            Assert.Equal(new IntPropValue(VarMilli, 14000), runs[5].Props.IntProps[6]);
        }

        [Fact]
        public void Writing_system_and_object_data_are_run_fields_not_bag_entries()
        {
            var runs = TsStringConversion.ToRuns(WithPictureAndLink(), H);
            Assert.All(runs, r => Assert.False(r.Props.IntProps.ContainsKey(Ws)));
            Assert.All(runs, r => Assert.False(r.Props.StrProps.ContainsKey(ObjData)));
            Assert.True(runs[1].IsObject);
            Assert.Equal(Encoding.Unicode.GetString(TsStringUtils.GetObjData(PictureGuid, FwObjDataTypes.kodtGuidMoveableObjDisp)), runs[1].ObjData);
            Assert.Equal("\u0004C:\\data\\notes.pdf", runs[3].ObjData);
            Assert.Equal("\uFFFC", runs[1].Text);
        }

        [Fact]
        public void Rtl_run_has_its_writing_system_and_nfd_text_is_kept_exactly()
        {
            var runs = TsStringConversion.ToRuns(Arabic(), H);
            Assert.Equal("ar", runs[0].Ws);
            Assert.Equal(new IntPropValue(VarEnum, 1), runs[0].Props.IntProps[Rtl]);
            Assert.Equal("e\u0301cole\u0301 a\u0300", TsStringConversion.ToRuns(Nfd(), H)[0].Text);
        }

        [Fact]
        public void Empty_string_keeps_its_writing_system()
        {
            var runs = TsStringConversion.ToRuns(Empty(), H);
            Assert.Equal("fr", Assert.Single(runs).Ws);
            Assert.Equal("", runs[0].Text);
            Assert.Equal(Handles.Fr, TsStringUtils.GetWsOfRun(TsStringConversion.ToTsString(runs, H, "en"), 0));
        }

        [Fact]
        public void Paragraph_keeps_the_callers_key_and_style_rules()
        {
            var key = Guid.NewGuid();
            var rules = TsStringUtils.MakePropsBldr();
            rules.SetStrPropValue(NamedStyle, "Block Quote");
            rules.SetIntPropValues(Rtl, 0, 1);
            rules.SetIntPropValues(17, VarEnum, 2);          // alignment, carried untouched
            rules.SetIntPropValues(18, VarMilli, 36000);      // first indent
            var rulesProps = rules.GetTextProps();
            var p = TsStringConversion.ToParagraph(Mixed(), rulesProps, key, H);
            Assert.Equal(key, p.Key);
            Assert.Equal("Block Quote", p.Style);
            Assert.True(p.Rtl);
            Assert.Equal(Dump(rulesProps), Dump(TsStringConversion.ToStyleRules(p)));
        }

        [Fact]
        public void Edited_paragraph_style_and_direction_reach_the_style_rules()
        {
            var p = TsStringConversion.ToParagraph(Mixed(), TsStringUtils.MakePropsBldr().GetTextProps(), null, H);
            p.Style = "Heading 1";
            p.Rtl = true;
            var rules = TsStringConversion.ToStyleRules(p)!;
            Assert.Equal("i128=0:1,s133=" + string.Join(".", "Heading 1".Select(c => ((int)c).ToString("X"))), Dump(rules));
        }

        [Fact]
        public void Adapter_never_invents_a_key()
        {
            var doc = TsStringConversion.ToDocument(new[] { new TextParagraph(null, Nfd(), null) }, H);
            Assert.Null(doc.Paragraphs[0].Key);
            Assert.Equal(NormalizationForm.FormD, doc.Normalization);
        }

        [Fact]
        public void Document_round_trips_with_keys_and_style_rules()
        {
            var keys = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
            var rules = TsStringUtils.MakePropsBldr();
            rules.SetStrPropValue(NamedStyle, "Normal");
            rules.SetIntPropValues(18, VarMilli, 36000);
            var input = new[]
            {
                new TextParagraph(keys[0], Mixed(), rules.GetTextProps()),
                new TextParagraph(keys[1], Arabic(), null),
                new TextParagraph(keys[2], WithPictureAndLink(), rules.GetTextProps()),
            };
            var output = TsStringConversion.ToTextParagraphs(TsStringConversion.ToDocument(input, H), H);
            Assert.Equal(input.Length, output.Count);
            for (int i = 0; i < input.Length; i++)
            {
                Assert.Equal(input[i].Key, output[i].Key);
                Assert.Equal(Dump(input[i].Contents), Dump(output[i].Contents));
                Assert.Equal(Dump(input[i].StyleRules), Dump(output[i].StyleRules));
            }
        }

        [Fact]
        public void Properties_that_are_not_drawn_survive_an_edit_of_another_run()
        {
            var original = WithPictureAndLink();
            var doc = TsStringConversion.ToDocument(new[] { new TextParagraph(Guid.NewGuid(), original, null) }, H);
            var editor = new DocumentEditor(doc);
            editor.SetCaret(new TextPosition(0, 2));
            editor.TypeText("X");                             // inside the first run "see "
            var edited = TsStringConversion.ToTextParagraphs(doc, H)[0].Contents;
            Assert.Equal("seXe \uFFFC and \uFFFC end", edited.Text);
            Assert.Equal(original.RunCount, edited.RunCount);
            for (int i = 1; i < original.RunCount; i++)
            {
                Assert.Equal(Dump(original.get_Properties(i)), Dump(edited.get_Properties(i)));
                Assert.Equal(original.get_RunText(i), edited.get_RunText(i));
            }
        }

        [Fact]
        public void Editing_text_inside_a_bold_invert_run_keeps_the_variant()
        {
            var original = Mixed();
            var doc = TsStringConversion.ToDocument(new[] { new TextParagraph(null, original, null) }, H);
            var editor = new DocumentEditor(doc);
            original.GetBoundsOfRun(3, out int min, out _);
            editor.SetCaret(new TextPosition(0, min + 2));
            editor.TypeText("Z");
            var edited = TsStringConversion.ToTextParagraphs(doc, H)[0].Contents;
            Assert.Equal(original.RunCount, edited.RunCount);
            Assert.Equal(Dump(original.get_Properties(3)), Dump(edited.get_Properties(3)));
            Assert.Equal("boZld invert ", edited.get_RunText(3));
        }

        [Fact]
        public void Multi_paragraph_paste_gives_the_last_paragraph_the_fragments_style_and_props_together()
        {
            var headRules = TsStringUtils.MakePropsBldr();
            headRules.SetStrPropValue(NamedStyle, "Normal");
            headRules.SetIntPropValues(18, VarMilli, 1000);
            var fragRules = TsStringUtils.MakePropsBldr();
            fragRules.SetStrPropValue(NamedStyle, "Quote");
            fragRules.SetIntPropValues(18, VarMilli, 9000);
            var doc = TsStringConversion.ToDocument(new[] { new TextParagraph(null, Build(("headtail", Props(Handles.En))), headRules.GetTextProps()) }, H);
            var fragment = new List<Paragraph>
            {
                TsStringConversion.ToParagraph(Build(("one", Props(Handles.En))), headRules.GetTextProps(), null, H),
                TsStringConversion.ToParagraph(Build(("two", Props(Handles.En))), fragRules.GetTextProps(), null, H),
            };
            var editor = new DocumentEditor(doc);
            editor.SetCaret(new TextPosition(0, 4));
            editor.Paste(fragment);
            var paras = doc.Paragraphs;
            Assert.Equal("headone", paras[0].Text);
            Assert.Equal("Normal", paras[0].Style);                                   // the head keeps its own
            Assert.Equal(1000, paras[0].Props.IntProps[18].Value);
            Assert.Equal("twotail", paras[1].Text);
            Assert.Equal("Quote", paras[1].Style);
            Assert.Equal("Quote", paras[1].Props.StrProps[NamedStyle]);               // never disagree
            Assert.Equal(9000, paras[1].Props.IntProps[18].Value);
        }
    }
}
