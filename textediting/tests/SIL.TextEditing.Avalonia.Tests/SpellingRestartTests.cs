using System;
using System.Collections.Generic;
using System.Linq;
using SIL.TextEditing.Tests;
using Xunit;

namespace SIL.TextEditing.Avalonia.Tests
{
    /// <summary>Issue 92: checking starts over when the editor's read-only flag changes and when a paragraph is changed behind the editor's back.</summary>
    public class SpellingRestartTests
    {
        static FakeSpellHost Host() => new FakeSpellHost("The", "cat", "down", "all", "good", "here", "and", "end", "the", "seat", "sat").With("fr", "bon", "jour");

        static Document Doc3() => TestDocs.Doc(
            TestDocs.Para("en", "en", "The cat saat down"),
            TestDocs.Para("en", "en", "all good here"),
            TestDocs.Para("en", "en", "and teh end"));

        static RichTextField Field(Document doc, List<Action>? posts = null, ISpellCheckHost? host = null)
        {
            var field = Fixtures.Field(doc);
            field.SpellIdlePost = a => posts?.Add(a);
            field.SpellChecking = host ?? Host();
            return field;
        }

        static void CheckAll(RichTextField f) { while (!f.CheckSpellingStep()) { } }

        static string[] Words(RichTextField f, int p) => f.SpellingMarks(p).Select(m => f.Editor.Document.Paragraphs[p].Text.Substring(m.Start, m.Length)).ToArray();

        // ---- the editor's read-only flag

        [Fact]
        public void Changing_the_editors_read_only_flag_directly_restarts_checking_and_the_field_flag_does_it_once() => Headless.Run(() =>
        {
            var f = Field(Doc3());
            CheckAll(f);
            Assert.Equal(new[] { "saat" }, Words(f, 0));
            int restarts = f.SpellRestarts;
            f.Editor.IsReadOnly = true;                    // not through RichTextField.IsReadOnly
            Assert.Equal(restarts + 1, f.SpellRestarts);
            Assert.Empty(f.SpellingMarks(0));
            CheckAll(f);
            Assert.Empty(f.SpellingMarks(0));              // read-only text is not checked
            f.Editor.IsReadOnly = false;
            Assert.Equal(restarts + 2, f.SpellRestarts);
            CheckAll(f);
            Assert.Equal(new[] { "saat" }, Words(f, 0));

            f.Editor.IsReadOnly = false;                   // no change, no restart
            Assert.Equal(restarts + 2, f.SpellRestarts);
            f.IsReadOnly = true;                           // through the field: one restart, not two
            Assert.Equal(restarts + 3, f.SpellRestarts);
            f.IsReadOnly = true;
            Assert.Equal(restarts + 3, f.SpellRestarts);
        });

        [Fact]
        public void A_read_only_change_made_on_another_thread_restarts_checking_on_the_fields_own() => Headless.Run(async () =>
        {
            var f = Field(Doc3());
            CheckAll(f);
            int restarts = f.SpellRestarts;
            await System.Threading.Tasks.Task.Run(() => f.Editor.IsReadOnly = true);
            Headless.Pump();
            Assert.Equal(restarts + 1, f.SpellRestarts);
            Assert.Empty(f.SpellingMarks(0));
        });

        [Fact]
        public void The_editor_tells_its_listeners_when_the_read_only_flag_changes_and_not_when_it_stays()
        {
            var e = new DocumentEditor(TestDocs.OneParagraph("abc"));
            int raised = 0;
            e.IsReadOnlyChanged += () => raised++;
            e.IsReadOnly = false;
            Assert.Equal(0, raised);
            e.IsReadOnly = true;
            e.IsReadOnly = true;
            e.IsReadOnly = false;
            Assert.Equal(2, raised);
        }

        // ---- a paragraph changed behind the editor's back

        [Fact]
        public void A_direct_change_to_a_run_makes_its_paragraph_stale_so_its_marks_go_and_it_is_checked_again() => Headless.Run(() =>
        {
            var posts = new List<Action>();
            var f = Field(Doc3(), posts);
            CheckAll(f);
            Assert.Equal(new[] { "saat" }, Words(f, 0));
            var p = f.Editor.Document.Paragraphs[0];
            p.Runs[0].Text = "The cat sat doown";       // the host changes the run; the editor is not told
            Assert.Empty(f.SpellingMarks(0));
            Assert.Equal(new[] { "teh" }, Words(f, 2));  // other paragraphs keep theirs
            Assert.False(f.IsSpellingComplete);
            f.RefreshFromDocument();
            Fixtures.Layout(f, 520);
            CheckAll(f);
            Assert.Equal(new[] { "doown" }, Words(f, 0));
        });

        [Fact]
        public void A_direct_change_of_a_writing_system_a_replaced_bag_or_the_style_is_noticed() => Headless.Run(() =>
        {
            var f = Field(TestDocs.Doc(TestDocs.Para("en", "en", "bon jour"), TestDocs.Para("en", "fr", "xx yy"), TestDocs.Para("en", "en", "zzz")));
            CheckAll(f);
            Assert.Equal(new[] { "bon", "jour" }, Words(f, 0));
            Assert.Equal(new[] { "xx", "yy" }, Words(f, 1));
            var doc = f.Editor.Document;
            doc.Paragraphs[0].Runs[0].Ws = "fr";
            Assert.Empty(f.SpellingMarks(0));
            var bag = new PropertyBag();
            bag.SetInt(TextPropertyNumbers.SpellCheck, TextPropertyNumbers.SpellCheckDoNot);
            doc.Paragraphs[1].Runs[0].Props = bag;
            Assert.Empty(f.SpellingMarks(1));
            Assert.Equal(new[] { "zzz" }, Words(f, 2));
            doc.Paragraphs[2].Style = "Other";
            Assert.Empty(f.SpellingMarks(2));
            doc.Paragraphs[2].Props = doc.Paragraphs[2].Props.Clone();
            f.RefreshFromDocument();
            Fixtures.Layout(f, 520);
            CheckAll(f);
            Assert.Empty(Words(f, 0));                       // "bon jour" is French now
            Assert.Empty(Words(f, 1));                       // not checked
            Assert.Equal(new[] { "zzz" }, Words(f, 2));
        });

        [Fact]
        public void A_stale_paragraph_is_drawn_without_its_old_marks_and_asks_for_idle_time() => Headless.Run(() =>
        {
            var posts = new List<Action>();
            var (window, f) = Fixtures.InWindow(Doc3());
            f.SpellIdlePost = posts.Add;
            f.SpellChecking = Host();
            while (posts.Count > 0) { var next = posts[0]; posts.RemoveAt(0); next(); }   // the idle work checks everything in view
            Assert.True(f.IsSpellingComplete);
            Assert.NotEmpty(f.ParagraphLayoutFor(0).SpellingMarksDrawn);
            f.Editor.Document.Paragraphs[0].Runs[0].Text = "changed behind the back";
            Fixtures.Render(f);                               // drawing is when the field sees the paragraph has changed
            Assert.Empty(f.ParagraphLayoutFor(0).SpellingMarksDrawn);
            Assert.NotEmpty(posts);                           // the paragraph is due again
            window.Close();
        });

        [Fact]
        public void A_property_bag_changed_in_place_is_noticed_without_the_host_refreshing_the_field() => Headless.Run(() =>
        {
            var f = Field(Doc3());
            CheckAll(f);
            f.Editor.Document.Paragraphs[0].Runs[0].Props.SetInt(TextPropertyNumbers.SpellCheck, TextPropertyNumbers.SpellCheckDoNot);
            Assert.Empty(f.SpellingMarks(0));                // the marks of the changed paragraph are gone at once
            Fixtures.Layout(f, 520);
            CheckAll(f);
            Assert.Empty(Words(f, 0));                       // and it was checked again, with the run now not checked
            Assert.Equal(new[] { "teh" }, Words(f, 2));      // the other paragraphs were not touched
        });

        [Fact]
        public void Refreshing_the_field_gives_a_failed_writing_system_another_chance() => Headless.Run(() =>
        {
            var host = Host();
            host.Checkers["en"].Throws = new InvalidOperationException("boom");
            var f = Field(Doc3(), host: host);
            int failures = 0;
            f.SpellCheckerFailed += (ws, e) => failures++;
            CheckAll(f);
            Assert.Equal(1, failures);
            host.Checkers["en"].Throws = null;
            f.RefreshFromDocument();
            Fixtures.Layout(f, 520);
            CheckAll(f);
            Assert.Equal(1, failures);
            Assert.Equal(new[] { "saat" }, Words(f, 0));
        });

        [Fact]
        public void The_change_stamp_moves_on_each_noticed_change_and_is_never_shared_between_paragraphs()
        {
            var a = TestDocs.Para("en", "en", "abc");
            var b = TestDocs.Para("en", "en", "abc");
            Assert.NotEqual(a.ChangeStamp, b.ChangeStamp);
            long s0 = a.ChangeStamp;
            a.Runs[0].Text = "abcd";
            long s1 = a.ChangeStamp;
            a.Runs[0].Ws = "fr";
            long s2 = a.ChangeStamp;
            a.Runs.Add(new Run { Ws = "fr", Text = "x" });
            long s3 = a.ChangeStamp;
            a.Style = "S";
            long s4 = a.ChangeStamp;
            a.Runs[0].ObjData = "d";
            long s5 = a.ChangeStamp;
            Assert.Equal(6, new[] { s0, s1, s2, s3, s4, s5 }.Distinct().Count());
            a.Runs[0].Props.SetInt(TextPropertyNumbers.Bold, 1);   // in place: seen too (issue 101)
            long s6 = a.ChangeStamp;
            Assert.NotEqual(s5, s6);
            Assert.Equal(s6, a.ChangeStamp);
            var clone = a.Clone();
            Assert.NotEqual(a.ChangeStamp, clone.ChangeStamp);
        }
    }
}
