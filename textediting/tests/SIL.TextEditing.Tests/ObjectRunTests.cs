using System.Linq;
using Xunit;
using static SIL.TextEditing.Tests.TestDocs;

namespace SIL.TextEditing.Tests
{
    /// <summary>An object run (an ORC with ObjData) is atomic: one offset, deleted whole, kept through edits.</summary>
    public class ObjectRunTests
    {
        const string Data = "\u0008pic-guid-1234";

        static DocumentEditor WithObject()
        {
            var p = new Paragraph();
            p.Runs.Add(new Run { Ws = "en", Text = "before " });
            p.Runs.Add(Run.Object("en", Data));
            p.Runs.Add(new Run { Ws = "en", Text = " after" });
            return new DocumentEditor(Doc(p));
        }

        [Fact]
        public void Object_occupies_one_offset_and_never_merges()
        {
            var e = WithObject();
            var p = e.Document.Paragraphs[0];
            Assert.Equal(14, p.Length);
            Assert.Equal(3, p.Runs.Count);
            Assert.Equal("￼", p.Runs[1].Text);
            p.Normalize();
            Assert.Equal(3, p.Runs.Count);
        }

        [Fact]
        public void Backspace_deletes_the_whole_object_and_undo_restores_its_data()
        {
            var e = WithObject();
            var undo = new SimpleUndoStack();
            undo.Attach(e);
            e.SetCaret(new TextPosition(0, 8));
            e.Backspace();
            Assert.Equal("before  after", e.Document.Paragraphs[0].Text);
            undo.Undo();
            Assert.Contains(e.Document.Paragraphs[0].Runs, r => r.IsObject && r.ObjData == Data);
        }

        [Fact]
        public void Object_survives_nearby_edits_and_the_clipboard_round_trip()
        {
            var e = WithObject();
            e.SetCaret(new TextPosition(0, 3));
            e.TypeText("zz");
            e.SetCaret(new TextPosition(0, 12));
            e.TypeText("yy");
            Assert.Equal(Data, e.Document.Paragraphs[0].Runs.Single(r => r.IsObject).ObjData);

            e.Anchor = new TextPosition(0, 0);
            e.SetCaret(new TextPosition(0, e.Document.Paragraphs[0].Length), true);
            var back = Interchange.FromXml(Interchange.ToXml(e.GetFragment()));
            Assert.Contains(back[0].Runs, r => r.IsObject && r.ObjData == Data);
        }
    }
}
