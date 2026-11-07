using System.Linq;
using System.Text;
using SIL.LCModel.Core.KernelInterfaces;
using Xunit;
using static SIL.TextEditing.LCModel.Tests.Fixtures;

namespace SIL.TextEditing.LCModel.Tests
{
    public class ClipboardTests
    {
        static readonly IWritingSystemHandles H = Handles.Instance;

        [Theory]
        [InlineData("mixed")]
        [InlineData("arabic")]
        [InlineData("nfd")]
        [InlineData("picture")]
        [InlineData("empty")]
        public void TsString_xml_round_trips_through_LCModels_serializer(string which)
        {
            var original = which switch { "mixed" => Mixed(), "arabic" => Arabic(), "nfd" => Nfd(), "picture" => WithPictureAndLink(), _ => Empty() };
            var xml = TsStringClipboard.ToXml(original, H);
            Assert.StartsWith("<Str", xml.TrimStart());
            Assert.Equal(Dump(original), Dump(TsStringClipboard.FromXml(xml, H)));
        }

        [Fact]
        public void Fragment_payload_has_tsstring_xml_and_nfc_plain_text()
        {
            var doc = TsStringConversion.ToDocument(new[]
            {
                new TextParagraph(null, Nfd(), null),
                new TextParagraph(null, Mixed(), null),
            }, H);
            var xml = TsStringClipboard.FragmentToXml(doc.Paragraphs, H);
            Assert.Contains("<Str", xml);
            Assert.Equal("\u00E9col\u00E9 \u00E0; Plain bold on bold off bold invert styled big red",
                TsStringClipboard.FromXml(xml, H).Text.Normalize(NormalizationForm.FormC));
        }

        [Fact]
        public void Paragraph_separator_is_a_parameter_that_defaults_to_FieldWorks()
        {
            var doc = TsStringConversion.ToDocument(new[] { new TextParagraph(null, Nfd(), null), new TextParagraph(null, Fr2(), null) }, H);
            Assert.Equal("écolé à; deux", TsStringClipboard.FromXml(TsStringClipboard.FragmentToXml(doc.Paragraphs, H), H).Text.Normalize(NormalizationForm.FormC));
            var lf = TsStringClipboard.FragmentToXml(doc.Paragraphs, H, "\n");
            Assert.Equal("écolé à\ndeux", TsStringClipboard.FromXml(lf, H).Text.Normalize(NormalizationForm.FormC));
            Assert.Equal(2, TsStringClipboard.FromXmlToFragment(lf, H, "\n").Count);
            Assert.Single(TsStringClipboard.FromXmlToFragment(lf, H));      // FieldWorks pastes one paragraph
        }

        static ITsString Fr2() => Build(("deux", Props(Handles.Fr)));

        [Fact]
        public void Hangul_and_composition_exclusions_serialize_and_read_back_without_ICU_setup()
        {
            // Hangul syllables decompose to jamo in NFD; U+0958 (Devanagari QA) is a composition exclusion.
            var s = Build(("\uD55C\uAE00 \u0958 \u212B", Props(Handles.En)));
            var back = TsStringClipboard.FromXml(TsStringClipboard.ToXml(s, H), H);
            Assert.Equal(s.Text.Normalize(NormalizationForm.FormD), back.Text.Normalize(NormalizationForm.FormD));
        }

        [Fact]
        public void Pasted_payload_becomes_paragraphs_with_their_properties_and_no_keys()
        {
            var doc = TsStringConversion.ToDocument(new[]
            {
                new TextParagraph(System.Guid.NewGuid(), Nfd(), null),
                new TextParagraph(System.Guid.NewGuid(), Mixed(), null),
            }, H);
            var xml = TsStringClipboard.FragmentToXml(doc.Paragraphs, H, "\n");
            var fragment = TsStringClipboard.FromXmlToFragment(xml, H, "\n");
            Assert.Equal(2, fragment.Count);
            Assert.All(fragment, p => Assert.Null(p.Key));
            Assert.Equal("e\u0301cole\u0301 a\u0300", fragment[0].Text);
            Assert.Equal(Dump(Mixed()), Dump(TsStringConversion.ToTsString(fragment[1], H)));
        }

        [Fact]
        public void The_form_writes_TsString_xml_with_FieldWorks_separator_and_pastes_one_paragraph()
        {
            var form = new TsStringClipboardForm(H);
            Assert.Equal(TsStringClipboard.XmlFormatName, form.Id);   // its own id: a host's TsString wrapper form keeps "TsString"
            Assert.Equal(TsStringClipboard.XmlFormatName, form.FormatName);
            Assert.NotEqual("TsString", form.FormatName);   // that name is FieldWorks' wrapper, which a host registers
            Assert.False(form.OffersCopyAs);
            var doc = TsStringConversion.ToDocument(new[] { new TextParagraph(null, Nfd(), null), new TextParagraph(null, Fr2(), null) }, H);
            var xml = (string)form.Write(doc.Paragraphs)!;
            Assert.Contains("<Str", xml);
            var pasted = Assert.Single(form.Read(xml)!);
            Assert.Equal("e\u0301cole\u0301 a\u0300; deux", pasted.Text);
        }

        [Fact]
        public void The_form_can_split_a_pasted_string_and_refuses_text_that_is_not_TsString()
        {
            var form = new TsStringClipboardForm(H) { WriteParagraphSeparator = "\n", PasteParagraphSeparator = "\n" };
            var doc = TsStringConversion.ToDocument(new[] { new TextParagraph(null, Nfd(), null), new TextParagraph(null, Fr2(), null) }, H);
            Assert.Equal(2, form.Read(form.Write(doc.Paragraphs)!)!.Count);
            Assert.Null(form.Read("just some text"));
            Assert.Null(form.Write(new System.Collections.Generic.List<Paragraph>()));
        }

        sealed class WideHandles : IWritingSystemHandles
        {
            public int GetHandle(string wsId) => wsId == "xx" ? 9 : Handles.Instance.GetHandle(wsId);
            public string GetId(int handle) => handle == 9 ? "xx" : Handles.Instance.GetId(handle);
        }

        [Fact]
        public void A_writing_system_the_host_does_not_know_keeps_its_id_so_the_field_can_map_it()
        {
            var wide = new WideHandles();
            var xml = new TsStringClipboardForm(wide).Write(TsStringConversion.ToDocument(new[]
            {
                new TextParagraph(null, Build(("known ", Props(Handles.Fr)), ("stranger", Props(9))), null),
            }, wide).Paragraphs)!;
            var fragment = new TsStringClipboardForm(H).Read(xml)!;
            Assert.Equal(new[] { "fr", "xx" }, fragment[0].Runs.Select(r => r.Ws).ToArray());
            var mapping = ClipboardWritingSystems.MapMissing(fragment, ws => ws == "fr", "en");
            Assert.Equal(new[] { "xx" }, mapping.Missing.ToArray());
            Assert.Equal(new[] { "fr", "en" }, fragment[0].Runs.Select(r => r.Ws).ToArray());
        }

        [Fact]
        public void Text_that_only_looks_like_TsString_xml_reads_as_not_ours_instead_of_throwing()
        {
            var form = new TsStringClipboardForm(H);
            Assert.Null(form.Read("<Str><Run ws=\"en\""));
            Assert.Null(form.Read("garbage <Str but not xml"));
        }
    }
}
