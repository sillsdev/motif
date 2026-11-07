using System.Linq;
using Xunit;
using static SIL.TextEditing.Tests.TestDocs;

namespace SIL.TextEditing.Tests
{
    public class ClipboardFormsTests
    {
        [Fact]
        public void Paste_prefers_TsString_then_FieldWorks_Markdown_then_plain_text_by_default()
        {
            Assert.Equal(new[] { "TsString", "FieldWorks Markdown", "text/plain" }, new ClipboardForms().PasteOrder.ToArray());
        }

        [Fact]
        public void A_form_with_the_same_id_replaces_the_earlier_one_and_is_found_by_id()
        {
            var forms = new ClipboardForms();
            var first = new FakeClipboardForm("a");
            var second = new FakeClipboardForm("a");
            forms.Add(first).Add(new FakeClipboardForm("b")).Add(second);
            Assert.Same(second, forms.Find("a"));
            Assert.Equal(2, forms.Count());
            Assert.True(forms.Remove("a"));
            Assert.Null(forms.Find("a"));
        }

        [Fact]
        public void Copy_as_lists_only_the_forms_that_offer_it()
        {
            var forms = new ClipboardForms().Add(new FakeClipboardForm("tsstring", offersCopyAs: false)).Add(new FakeClipboardForm("md"));
            Assert.Equal(new[] { "md" }, forms.CopyAsForms.Select(f => f.Id).ToArray());
        }

        [Fact]
        public void Writing_systems_the_target_lacks_take_the_selections_writing_system_and_neighbours_merge()
        {
            var fragment = new[] { Para("zzz", "zzz", "one ", "en", "two ", "xyz", "three"), Para("fr", "fr", "four") };
            var mapping = ClipboardWritingSystems.MapMissing(fragment, ws => ws == "en" || ws == "fr", "fr");
            Assert.True(mapping.Any);
            Assert.Equal(new[] { "zzz", "xyz" }, mapping.Missing.ToArray());
            Assert.Equal("fr", mapping.Target);
            Assert.Equal(new[] { "fr", "en", "fr" }, fragment[0].Runs.Select(r => r.Ws).ToArray());
            Assert.Equal("fr", fragment[0].DefaultWs);
            Assert.Equal("fr", fragment[1].Runs[0].Ws);
        }

        [Fact]
        public void Nothing_is_mapped_or_reported_when_every_writing_system_is_known()
        {
            var fragment = new[] { Para("en", "en", "a", "fr", "b") };
            var mapping = ClipboardWritingSystems.MapMissing(fragment, ws => true, "fr");
            Assert.False(mapping.Any);
            Assert.Equal(new[] { "en", "fr" }, fragment[0].Runs.Select(r => r.Ws).ToArray());
        }

        [Fact]
        public void A_run_with_no_writing_system_id_takes_the_target_without_being_reported_as_missing()
        {
            var fragment = new[] { Para("", "", "text") };
            var mapping = ClipboardWritingSystems.MapMissing(fragment, ws => true, "en");
            Assert.False(mapping.Any);
            Assert.Equal("en", fragment[0].Runs[0].Ws);
            Assert.Equal("en", fragment[0].DefaultWs);
        }

        [Fact]
        public void The_debug_dump_form_round_trips_runs_and_properties()
        {
            var p = Para("en", "en", "plain ", "fr", "opaque");
            p.Runs[1].Props.SetInt(4242, 42);
            var form = new InterchangeClipboardForm();
            var back = form.Read(form.Write(new[] { p })!)!;
            Assert.Equal(Interchange.Json(Doc(p)), Interchange.Json(Doc(back.ToArray())));
        }
    }
}
