using System.Linq;
using Xunit;
using static SIL.TextEditing.Tests.TestDocs;

namespace SIL.TextEditing.Tests
{
    /// <summary>Properties the editor does not understand survive every edit, undo, paste and round trip.</summary>
    public class OpaquePropertyTests
    {
        const int UnknownInt = 4242;
        const int UnknownStr = 4243;
        const string Weird = "a;b=c<&>é";

        static Document OpaqueDoc()
        {
            var p = new Paragraph();
            p.Runs.Add(new Run { Ws = "en", Text = "plain " });
            var r = new Run { Ws = "en", Text = "opaque run" };
            r.Props.SetInt(UnknownInt, 42, 3).SetStr(UnknownStr, Weird);
            p.Runs.Add(r);
            p.Runs.Add(new Run { Ws = "en", Text = " tail" });
            return Doc(p);
        }

        static int OpaqueChars(DocumentEditor e) => e.Document.Paragraphs
            .SelectMany(p => p.Runs)
            .Where(r => r.Props.TryGetInt(UnknownInt, out var v) && v == 42 && r.Props.TryGetStr(UnknownStr, out var s) && s == Weird)
            .Sum(r => r.Text.Length);

        [Fact]
        public void Baseline_counts_the_opaque_run()
        {
            Assert.Equal(10, OpaqueChars(new DocumentEditor(OpaqueDoc())));
        }

        [Fact]
        public void Typing_at_the_far_ends_does_not_touch_the_bag()
        {
            var e = new DocumentEditor(OpaqueDoc());
            e.SetCaret(new TextPosition(0, 0));
            e.TypeText("A");
            e.SetCaret(new TextPosition(0, 20));
            e.TypeText("B");
            Assert.Equal(10, OpaqueChars(e));
        }

        [Fact]
        public void Typing_inside_the_run_inherits_the_bag()
        {
            var e = new DocumentEditor(OpaqueDoc());
            e.SetCaret(new TextPosition(0, 10));
            e.TypeText("MID");
            Assert.Equal(13, OpaqueChars(e));
        }

        [Fact]
        public void Split_undo_redo_and_join_keep_the_bag()
        {
            var e = new DocumentEditor(OpaqueDoc());
            var undo = new SimpleUndoStack();
            undo.Attach(e);
            var original = Interchange.Json(e.Document);
            e.SetCaret(new TextPosition(0, 10));
            e.Enter();
            Assert.Equal(10, OpaqueChars(e));
            Assert.All(e.Document.Paragraphs, p => Assert.Single(p.Runs, r => r.Props.StrProps.ContainsKey(UnknownStr)));

            undo.Undo();
            Assert.Equal(original, Interchange.Json(e.Document));
            undo.Redo();
            Assert.Equal(2, e.Document.Paragraphs.Count);
            Assert.Equal(10, OpaqueChars(e));

            e.SetCaret(new TextPosition(1, 0));
            e.Backspace();
            Assert.Single(e.Document.Paragraphs);
            Assert.Single(e.Document.Paragraphs[0].Runs, r => r.Props.StrProps.ContainsKey(UnknownStr));
            Assert.Equal(10, OpaqueChars(e));
            undo.Undo();
            Assert.Equal(2, e.Document.Paragraphs.Count);
        }

        [Fact]
        public void Paste_of_a_fragment_keeps_the_bag()
        {
            var e = new DocumentEditor(OpaqueDoc());
            e.Anchor = new TextPosition(0, 6);
            e.SetCaret(new TextPosition(0, 16), true);
            var fragment = e.GetFragment();
            e.SetCaret(new TextPosition(0, 21));
            e.Paste(fragment);
            Assert.Equal(20, OpaqueChars(e));
        }

        [Fact]
        public void Xml_round_trip_keeps_integer_and_string_properties_apart()
        {
            var e = new DocumentEditor(OpaqueDoc());
            e.Anchor = new TextPosition(0, 6);
            e.SetCaret(new TextPosition(0, 16), true);
            var back = Interchange.FromXml(Interchange.ToXml(e.GetFragment()));
            var run = back[0].Runs[0];
            Assert.Equal(new IntPropValue(3, 42), run.Props.IntProps[UnknownInt]);
            Assert.Equal(Weird, run.Props.StrProps[UnknownStr]);
            Assert.Equal("opaque run", run.Text);
        }

        [Fact]
        public void Property_numbers_in_the_two_spaces_do_not_collide()
        {
            var bag = new PropertyBag().SetInt(3, 1).SetStr(3, "para style");
            Assert.True(bag.TryGetInt(3, out var i));
            Assert.Equal(1, i);
            Assert.True(bag.TryGetStr(3, out var s));
            Assert.Equal("para style", s);
            Assert.Equal(2, bag.Count);
            Assert.True(bag.ContentEquals(bag.Clone()));
        }

        [Fact]
        public void Runs_with_different_bags_do_not_merge()
        {
            var p = new Paragraph();
            p.Runs.Add(new Run { Ws = "en", Text = "a" });
            var bold = new Run { Ws = "en", Text = "b" };
            bold.Props.SetInt(TextPropertyNumbers.Bold, TextPropertyNumbers.ToggleOn);
            p.Runs.Add(bold);
            p.Runs.Add(new Run { Ws = "en", Text = "c" });
            p.Normalize();
            Assert.Equal(3, p.Runs.Count);
            p.Runs[1].Props = new PropertyBag();
            p.Normalize();
            Assert.Single(p.Runs);
            Assert.Equal("abc", p.Text);
        }
    }
}
