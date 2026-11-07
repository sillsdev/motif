using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using SIL.TextEditing.Tests;
using Xunit;
using static SIL.TextEditing.Tests.TestDocs;

namespace SIL.TextEditing.Avalonia.Tests
{
    /// <summary>Issue 101: a host that changes the document itself, not through the editor, is followed by the field without a call to
    /// <c>RefreshFromDocument</c>: the paragraph is laid out again, only that one, and the editor's read-only flag tells automation however it is set.</summary>
    public class HostEditTests
    {
        const string Long = "The quick brown fox jumps over the lazy dog and then it keeps on running through the whole field until the line wraps.";

        static Document Five() => Doc(
            Para("en", "en", "one"), Para("en", "en", "two"), Para("en", "en", Long), Para("en", "en", "four"), Para("en", "en", "five"));

        // ---- the layout follows a paragraph changed behind the editor's back

        [Fact]
        public void A_run_the_host_changes_is_laid_out_again_at_the_next_measure_and_the_other_paragraphs_are_not() => Headless.Run(() =>
        {
            var doc = Five();
            var f = Fixtures.Field(doc, 220);
            double before = f.DesiredSize.Height;
            long built = f.LayoutCount;
            doc.Paragraphs[0].Runs[0].Text = Long + " " + Long;      // no RefreshFromDocument
            Assert.False(f.IsMeasureValid);                          // the field heard of it
            Fixtures.Layout(f, 220);
            Assert.True(f.DesiredSize.Height > before + 20, f.DesiredSize.Height + " is not taller than " + before);
            Assert.Equal(Long + " " + Long, f.ParagraphLayoutFor(0).Paragraph.Text);
            Assert.Equal(built + 1, f.LayoutCount);                  // one paragraph again, not all five
        });

        [Fact]
        public void A_property_the_host_changes_in_place_on_a_run_changes_the_layout() => Headless.Run(() =>
        {
            var doc = Five();
            var f = Fixtures.Field(doc, 520);
            double before = f.ParagraphLayoutFor(1).Height;
            long built = f.LayoutCount;
            doc.Paragraphs[1].Runs[0].Props.SetInt(TextPropertyNumbers.FontSize, 48000, TextPropertyNumbers.VariationMilliPoint);
            Fixtures.Layout(f, 520);
            Assert.True(f.ParagraphLayoutFor(1).Height > before + 10, f.ParagraphLayoutFor(1).Height + " is not taller than " + before);
            Assert.Equal(built + 1, f.LayoutCount);
        });

        [Fact]
        public void A_property_the_host_changes_in_place_on_a_paragraph_changes_its_format() => Headless.Run(() =>
        {
            var doc = Five();
            var f = Fixtures.Field(doc, 520);
            double before = f.ParagraphLayoutFor(0).Height;
            doc.Paragraphs[0].Props.SetInt(TextPropertyNumbers.LineHeight, 60000, TextPropertyNumbers.VariationMilliPoint);   // at least 60 points
            Fixtures.Layout(f, 520);
            Assert.True(f.ParagraphLayoutFor(0).Height > before + 20);
        });

        [Fact]
        public void A_bullet_scheme_the_host_sets_in_place_gives_the_paragraph_a_label_and_renumbers_the_ones_after_it() => Headless.Run(() =>
        {
            var doc = Five();
            foreach (var p in doc.Paragraphs.Skip(1)) p.Props.SetInt(TextPropertyNumbers.BulNumScheme, 10);     // paragraphs 1 to 4 are numbered
            var f = Fixtures.Field(doc, 520);
            Assert.Equal(string.Empty, f.ParagraphLayoutFor(0).LabelText);
            string second = f.ParagraphLayoutFor(2).LabelText;           // the third paragraph is the second in the list
            Assert.NotEqual(string.Empty, second);
            doc.Paragraphs[0].Props.SetInt(TextPropertyNumbers.BulNumScheme, 10);     // the list now starts a paragraph earlier
            Fixtures.Layout(f, 520);
            Assert.NotEqual(string.Empty, f.ParagraphLayoutFor(0).LabelText);
            Assert.NotEqual(second, f.ParagraphLayoutFor(2).LabelText);   // numbered one higher
        });

        [Fact]
        public void A_direct_ParagraphLayoutFor_after_the_host_changes_an_earlier_paragraphs_bullet_scheme_gives_the_new_number() => Headless.Run(() =>
        {
            var doc = Five();
            foreach (var p in doc.Paragraphs.Skip(1)) p.Props.SetInt(TextPropertyNumbers.BulNumScheme, 10);
            var f = Fixtures.Field(doc, 520);
            string second = f.ParagraphLayoutFor(2).LabelText;
            doc.Paragraphs[0].Props.SetInt(TextPropertyNumbers.BulNumScheme, 10);     // no measure in between: only the public call looks at paragraph 2
            Assert.NotEqual(second, f.ParagraphLayoutFor(2).LabelText);
            Assert.NotEqual(string.Empty, f.ParagraphLayoutFor(0).LabelText);
        });

        [Fact]
        public void A_second_host_edit_before_the_next_measure_still_forgets_the_find_highlight_and_current_match() => Headless.Run(() =>
        {
            var doc = Five();
            var f = Fixtures.Field(doc, 520);
            var options = new FindOptions { Pattern = "four" };
            doc.Paragraphs[0].Runs[0].Text = "one one";                  // the first host edit: nothing is measured after it
            Assert.True(f.Find(options));                               // a match worked out from the text after the first edit
            Assert.NotNull(f.CurrentMatch);
            doc.Paragraphs[3].Runs[0].Text = "xx four";                 // the second edit, before any measure: the match no longer means anything
            Assert.Null(f.CurrentMatch);
        });

        [Fact]
        public void A_direction_the_host_sets_changes_the_layout() => Headless.Run(() =>
        {
            var doc = Five();
            var f = Fixtures.Field(doc, 520);
            double left = f.ParagraphLayoutFor(0).CaretRect(0, false, null).X;
            Assert.True(left < 20);
            doc.Paragraphs[0].Rtl = true;
            Fixtures.Layout(f, 520);
            Assert.True(f.ParagraphLayoutFor(0).CaretRect(0, false, null).X > 300, "the start of a right-to-left paragraph is at the right");
        });

        [Fact]
        public void A_change_the_host_makes_without_any_other_call_is_drawn_in_a_window() => Headless.Run(() =>
        {
            var doc = Five();
            var (window, f) = Fixtures.InWindow(doc, 220);
            double before = f.DesiredSize.Height;
            doc.Paragraphs[0].Runs[0].Text = Long + " " + Long;
            Headless.Pump();
            Assert.True(f.DesiredSize.Height > before + 20, f.DesiredSize.Height + " is not taller than " + before);
            window.Close();
        });

        [Fact]
        public void A_change_made_on_another_thread_is_followed_on_the_fields_own() => Headless.Run(() =>
        {
            var doc = Five();
            var (window, f) = Fixtures.InWindow(doc, 220);
            double before = f.DesiredSize.Height;
            var host = new Thread(() => doc.Paragraphs[0].Runs[0].Text = Long + " " + Long);
            host.Start();
            host.Join();
            Headless.Pump();
            Assert.True(f.DesiredSize.Height > before + 20, f.DesiredSize.Height + " is not taller than " + before);
            window.Close();
        });

        [Fact]
        public void A_paragraph_taken_out_of_the_document_by_the_editor_stops_telling_the_field() => Headless.Run(() =>
        {
            var doc = Five();
            var f = Fixtures.Field(doc, 220);
            var gone = doc.Paragraphs[4];
            f.Editor.SetCaret(new TextPosition(4, 0));
            f.Editor.Backspace();                          // joins "five" onto "four"
            Assert.Equal(4, doc.Paragraphs.Count);
            Fixtures.Layout(f, 220);
            Assert.True(f.IsMeasureValid);
            gone.Runs[0].Text = "changed after leaving";
            Assert.True(f.IsMeasureValid);                 // the field no longer listens to it
        });

        [Fact]
        public void The_editors_own_typing_still_lays_out_one_paragraph() => Headless.Run(() =>
        {
            var f = Fixtures.Field(Five(), 220);
            long built = f.LayoutCount;
            f.Editor.SetCaret(new TextPosition(0, 3));
            f.Editor.TypeText("x");
            Fixtures.Layout(f, 220);
            Assert.Equal(built + 1, f.LayoutCount);
        });

        [Fact]
        public void A_dropped_field_is_not_kept_alive_by_the_document() => Headless.Run(() =>
        {
            var doc = Five();
            WeakReference Make()
            {
                var f = Fixtures.Field(doc, 220);
                return new WeakReference(f);
            }
            var weak = Make();
            for (int i = 0; i < 3; i++) { GC.Collect(); GC.WaitForPendingFinalizers(); }
            Assert.False(weak.IsAlive);
            doc.Paragraphs[0].Runs[0].Text = "still fine";      // the paragraph forgets the dead field's link
            doc.Paragraphs[0].Runs[0].Text = "and again";
        });

        // ---- spelling follows a host's change too

        [Fact]
        public void A_text_the_host_changes_is_checked_again_without_a_call() => Headless.Run(() =>
        {
            var doc = Doc(Para("en", "en", "the cat"), Para("en", "en", "the dog"));
            var (window, f) = Fixtures.InWindow(doc);
            var idle = new List<Action>();
            f.SpellIdlePost = idle.Add;
            f.SpellChecking = new FakeSpellHost("the", "cat", "dog");
            while (idle.Count > 0) { var step = idle[0]; idle.RemoveAt(0); step(); }   // the idle work runs until everything in view is checked
            Assert.True(f.IsSpellingComplete);
            doc.Paragraphs[1].Runs[0].Text = "the dgo";
            Assert.NotEmpty(idle);                           // idle time was asked for
            Assert.Empty(f.SpellingMarks(1));                // the old marks do not stand for the new text
            while (idle.Count > 0) { var step = idle[0]; idle.RemoveAt(0); step(); }
            Assert.Equal(new[] { "dgo" }, f.SpellingMarks(1).Select(m => doc.Paragraphs[1].Text.Substring(m.Start, m.Length)));
            window.Close();
        });

        // ---- read-only

        static (AutomationPeer Peer, List<(object? Old, object? New)> Seen) Watch(RichTextField f)
        {
            var peer = ControlAutomationPeer.CreatePeerForElement(f);
            var seen = new List<(object?, object?)>();
            peer.PropertyChanged += (s, e) => { if (e.Property == ValuePatternIdentifiers.IsReadOnlyProperty) seen.Add((e.OldValue, e.NewValue)); };
            return (peer, seen);
        }

        [Fact]
        public void Setting_the_editors_read_only_flag_directly_raises_the_automation_change_too() => Headless.Run(() =>
        {
            var f = Fixtures.Field(OneParagraph("ab"));
            var (peer, seen) = Watch(f);
            f.Editor.IsReadOnly = true;
            Assert.Equal(new[] { ((object?)false, (object?)true) }, seen);
            Assert.True(((global::Avalonia.Automation.Provider.IValueProvider)peer).IsReadOnly);
            f.Editor.IsReadOnly = false;
            Assert.Equal(2, seen.Count);
            Assert.Equal(((object?)true, (object?)false), seen[1]);
        });

        [Fact]
        public void Setting_the_fields_read_only_flag_raises_the_automation_change_once() => Headless.Run(() =>
        {
            var f = Fixtures.Field(OneParagraph("ab"));
            var (_, seen) = Watch(f);
            f.IsReadOnly = true;
            f.IsReadOnly = true;
            Assert.Equal(new[] { ((object?)false, (object?)true) }, seen);
            f.IsReadOnly = false;
            Assert.Equal(2, seen.Count);
        });

        [Fact]
        public void A_read_only_flag_set_on_the_editor_ends_a_composition_as_the_fields_does() => Headless.Run(() =>
        {
            var f = Fixtures.Field(OneParagraph("ab"));
            f.Editor.SetCaret(new TextPosition(0, 2));
            f.SetPreedit("x", 1);
            Assert.NotNull(f.Preedit);
            f.Editor.IsReadOnly = true;
            Assert.Null(f.Preedit);
        });

        [Fact]
        public void A_read_only_flag_set_on_the_editor_from_another_thread_is_reported_on_the_fields_own() => Headless.Run(() =>
        {
            var f = Fixtures.Field(OneParagraph("ab"));
            var (_, seen) = Watch(f);
            var t = new Thread(() => f.Editor.IsReadOnly = true);
            t.Start();
            t.Join();
            Assert.Empty(seen);                               // not on that thread
            Headless.Pump();
            Assert.Single(seen);
        });
    }
}
