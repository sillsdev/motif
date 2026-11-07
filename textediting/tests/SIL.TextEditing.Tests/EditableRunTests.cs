using System.Collections.Generic;
using System.Linq;
using Xunit;
using static SIL.TextEditing.Tests.TestDocs;

namespace SIL.TextEditing.Tests
{
    /// <summary>ADR 0019: editability is a run property (ktptEditable) and a field-level flag; selection always works.</summary>
    public class EditableRunTests
    {
        static Run Marked(string text, int editable)
        {
            var r = new Run { Ws = "en", Text = text };
            r.Props.SetInt(TextPropertyNumbers.Editable, editable);
            return r;
        }

        /// <summary>"Gloss: " (not editable) + "abc" (editable) + " [fixed]" (not editable) + "xyz" (editable).</summary>
        static DocumentEditor Labelled(int labelKind = TextPropertyNumbers.EditableNo)
        {
            var p = new Paragraph { DefaultWs = "en" };
            p.Runs.Add(Marked("Gloss: ", labelKind));
            p.Runs.Add(new Run { Ws = "en", Text = "abc" });
            p.Runs.Add(Marked(" [fixed]", labelKind));
            p.Runs.Add(new Run { Ws = "en", Text = "xyz" });
            return new DocumentEditor(Doc(p));
        }

        static string Text(DocumentEditor e) => e.Document.Paragraphs[0].Text;

        [Fact]
        public void A_run_without_the_property_is_editable_and_the_values_map_to_FieldWorks_numbers()
        {
            Assert.Equal(RunEditable.Editable, new Run { Text = "a" }.Editable);
            Assert.Equal(RunEditable.NotEditable, Marked("a", 0).Editable);
            Assert.Equal(RunEditable.Editable, Marked("a", 1).Editable);
            Assert.Equal(RunEditable.SemiEditable, Marked("a", 2).Editable);
            Assert.Equal(155, TextPropertyNumbers.Editable);
        }

        [Fact]
        public void A_caret_placed_in_read_only_text_moves_to_the_nearest_place_where_editing_is_allowed()
        {
            var e = Labelled();
            e.SetCaret(new TextPosition(0, 3));
            Assert.Equal(7, e.Caret.Offset);
            e.SetCaret(new TextPosition(0, 12));
            Assert.Equal(10, e.Caret.Offset);
            e.SetCaret(new TextPosition(0, 20));
            Assert.Equal(20, e.Caret.Offset);
        }

        [Fact]
        public void A_direction_makes_the_caret_step_over_read_only_text_instead_of_bouncing_back()
        {
            var e = Labelled();
            e.SetCaret(new TextPosition(0, 10));
            e.SetCaret(new TextPosition(0, 11), false, +1);
            Assert.Equal(18, e.Caret.Offset);
            e.SetCaret(new TextPosition(0, 12), false, -1);
            Assert.Equal(10, e.Caret.Offset);
        }

        [Fact]
        public void Selection_reaches_read_only_text()
        {
            var e = Labelled();
            e.SetCaret(new TextPosition(0, 7));
            e.SetCaret(new TextPosition(0, 2), true);
            Assert.True(e.HasSelection);
            Assert.Equal(2, e.Caret.Offset);
            e.SetCaret(new TextPosition(0, 0));
            e.Anchor = new TextPosition(0, 0);
            e.SetCaret(new TextPosition(0, 13), true);
            Assert.Equal(13, e.Caret.Offset);
        }

        [Fact]
        public void Typing_at_the_edge_of_read_only_text_lands_in_the_editable_run_without_its_editable_property()
        {
            var e = Labelled();
            e.SetCaret(new TextPosition(0, 7));
            e.TypeText("Q");
            Assert.Equal("Gloss: Qabc [fixed]xyz", Text(e));
            var typed = e.Document.Paragraphs[0].Runs[1];
            Assert.Equal("Qabc", typed.Text);
            Assert.False(typed.Props.TryGetInt(TextPropertyNumbers.Editable, out _));
            Assert.Equal(RunEditable.NotEditable, e.Document.Paragraphs[0].Runs[0].Editable);
        }

        [Fact]
        public void Typing_over_a_selection_that_crosses_read_only_text_changes_nothing()
        {
            var e = Labelled();
            e.Anchor = new TextPosition(0, 8);
            e.SetCaret(new TextPosition(0, 12), true);
            e.TypeText("Z");
            Assert.Equal("Gloss: abc [fixed]xyz", Text(e));
            Assert.True(e.HasSelection);
        }

        [Fact]
        public void Delete_backspace_cut_and_paste_are_refused_across_read_only_text()
        {
            var e = Labelled();
            var undo = new SimpleUndoStack();
            undo.Attach(e);
            e.Anchor = new TextPosition(0, 5);
            e.SetCaret(new TextPosition(0, 9), true);
            e.DeleteSelection("cut");
            e.Backspace();
            e.DeleteForward();
            e.Paste(new List<Paragraph> { Para("en", "en", "P") });
            e.ReplaceRange(new TextPosition(0, 5), new TextPosition(0, 9), null, "cut");
            Assert.Equal("Gloss: abc [fixed]xyz", Text(e));
            Assert.Empty(undo.Done);
        }

        [Fact]
        public void Backspace_and_Delete_at_the_edge_of_read_only_text_do_not_eat_it()
        {
            var e = Labelled();
            e.SetCaret(new TextPosition(0, 7));
            e.Backspace();
            Assert.Equal("Gloss: abc [fixed]xyz", Text(e));
            e.SetCaret(new TextPosition(0, 10));
            e.DeleteForward();
            Assert.Equal("Gloss: abc [fixed]xyz", Text(e));
            e.Backspace();
            Assert.Equal("Gloss: ab [fixed]xyz", Text(e));
        }

        [Fact]
        public void Editable_text_around_read_only_text_still_edits_and_selection_can_be_copied()
        {
            var e = Labelled();
            e.Anchor = new TextPosition(0, 0);
            e.SetCaret(new TextPosition(0, 21), true);
            Assert.Equal("Gloss: abc [fixed]xyz", string.Concat(e.GetFragment()[0].Text));
            e.Anchor = new TextPosition(0, 8);
            e.SetCaret(new TextPosition(0, 9), true);
            e.DeleteSelection();
            Assert.Equal("Gloss: ac [fixed]xyz", Text(e));
        }

        [Fact]
        public void Semi_editable_text_takes_typing_and_deletion_like_editable_text_but_cannot_be_formatted()
        {
            // FieldWorks' views count semi-editable (type-ahead) text as editable and refuse only formatting in it.
            var e = Labelled(TextPropertyNumbers.EditableSemi);   // "Gloss: " and " [fixed]" are semi-editable
            e.SetCaret(new TextPosition(0, 3));
            Assert.Equal(3, e.Caret.Offset);                       // the caret stays where it is put
            e.TypeText("X");
            Assert.Equal("GloXss: abc [fixed]xyz", Text(e));
            e.Anchor = new TextPosition(0, 0);
            e.SetCaret(new TextPosition(0, 4), true);
            Assert.True(e.CanEdit);
            Assert.False(e.CanFormatSelection);
            e.DeleteSelection();
            Assert.Equal("ss: abc [fixed]xyz", Text(e));
            e.Anchor = new TextPosition(0, 4);
            e.SetCaret(new TextPosition(0, 7), true);              // "abc", an editable run
            Assert.True(e.CanFormatSelection);
        }

        [Fact]
        public void A_read_only_field_refuses_every_edit_but_keeps_the_caret_and_selection_working()
        {
            var e = new DocumentEditor(Basic()) { IsReadOnly = true };
            var undo = new SimpleUndoStack();
            undo.Attach(e);
            e.SetCaret(new TextPosition(0, 3));
            Assert.Equal(3, e.Caret.Offset);
            e.TypeText("x");
            e.Enter();
            e.Backspace();
            e.DeleteForward();
            e.Paste(new List<Paragraph> { Para("en", "en", "P") });
            e.Anchor = new TextPosition(0, 1);
            e.SetCaret(new TextPosition(0, 5), true);
            Assert.True(e.HasSelection);
            e.DeleteSelection();
            Assert.Equal("Hello bonjour world", Text(e));
            Assert.Empty(undo.Done);
            Assert.False(e.CanEdit);

            e.IsReadOnly = false;
            e.TypeText("x");
            Assert.Equal("Hx bonjour world", Text(e));
        }

        [Fact]
        public void The_editable_places_are_the_stretches_between_read_only_text_and_Tab_steps_through_them()
        {
            var e = Labelled();   // places: [7, 10] and [18, 21]
            e.SetCaret(new TextPosition(0, 8));
            Assert.True(e.TryGetEditablePlace(EditablePlace.Next, out var p));
            Assert.Equal(new TextPosition(0, 18), p);
            e.SetCaret(p);
            Assert.False(e.TryGetEditablePlace(EditablePlace.Next, out _));    // none after the last place: leave
            Assert.True(e.TryGetEditablePlace(EditablePlace.Previous, out p));
            Assert.Equal(new TextPosition(0, 10), p);                          // the end of the previous place
            e.SetCaret(p);
            Assert.False(e.TryGetEditablePlace(EditablePlace.Previous, out _));
            Assert.True(e.TryGetEditablePlace(EditablePlace.Last, out p));
            Assert.Equal(new TextPosition(0, 21), p);
            Assert.True(e.TryGetEditablePlace(EditablePlace.First, out p));
            Assert.Equal(new TextPosition(0, 7), p);
            e.SetCaret(new TextPosition(0, 21));
            Assert.False(e.TryGetEditablePlace(EditablePlace.Last, out _));    // already at the last place
        }

        [Fact]
        public void Tab_over_a_large_document_finds_the_editable_places_from_run_boundaries_without_scanning_every_offset()
        {
            var doc = new Document();
            for (int i = 0; i < 100; i++)
            {
                var p = new Paragraph { DefaultWs = "en" };
                p.Runs.Add(new Run { Ws = "en", Text = new string('a', 100000) });
                p.Runs.Add(Marked("LOCK", TextPropertyNumbers.EditableNo));
                p.Runs.Add(new Run { Ws = "en", Text = "tail" });
                doc.Paragraphs.Add(p);
            }
            var e = new DocumentEditor(doc);
            e.SetCaret(new TextPosition(0, 0));
            // Counted, not timed, so a busy machine cannot fail it: finding the places must not ask whether each insertion point
            // takes typing (100 paragraphs of 100,000 characters would be ten million questions). The probe throws at the
            // budget, so a scan fails at once instead of running to its end.
            int asked = 0;
            e.EditableAtProbe = () => { if (++asked > 1000) throw new System.InvalidOperationException("scanned the insertion points one by one"); };
            for (int i = 0; i < 10; i++)
            {
                Assert.True(e.TryGetEditablePlace(EditablePlace.Next, out var next));
                Assert.Equal(new TextPosition(0, 100004), next);          // the start of the place after the locked run
                Assert.True(e.TryGetEditablePlace(EditablePlace.Last, out var last));
                Assert.Equal(new TextPosition(99, 100008), last);
            }
            Assert.True(asked <= 1000);
        }

        [Fact]
        public void Setting_the_caret_inside_a_very_long_read_only_run_finds_the_nearest_editable_point_from_the_run_boundaries()
        {
            var p = new Paragraph { DefaultWs = "en" };
            p.Runs.Add(new Run { Ws = "en", Text = "ab" });
            p.Runs.Add(Marked(new string('L', 100000), TextPropertyNumbers.EditableNo));
            p.Runs.Add(new Run { Ws = "en", Text = "cd" });
            var e = new DocumentEditor(Doc(p));
            // Counted, not timed: the question "does this point take typing?" is asked a handful of times, not once per offset of the
            // locked run (now each run looked at counts too, a handful per call). The probe throws at the budget so a scan fails at once.
            int asked = 0;
            e.EditableAtProbe = () => { if (++asked > 300) throw new System.InvalidOperationException("scanned the insertion points one by one"); };
            for (int i = 0; i < 10; i++)
            {
                Assert.Equal(new TextPosition(0, 2), e.NearestEditable(new TextPosition(0, 50000)));          // the nearer side: the start of the lock
                Assert.Equal(new TextPosition(0, 100002), e.NearestEditable(new TextPosition(0, 60000)));
                Assert.Equal(new TextPosition(0, 100002), e.NearestEditable(new TextPosition(0, 50000), 1));    // a direction searches that way first
                Assert.Equal(new TextPosition(0, 2), e.NearestEditable(new TextPosition(0, 99999), -1));
                Assert.Equal(new TextPosition(0, 1), e.NearestEditable(new TextPosition(0, 1)));                // already editable
            }
            Assert.True(asked <= 300);
        }

        [Fact]
        public void NearestEditable_agrees_with_asking_every_insertion_point_in_paragraphs_of_mixed_runs_and_line_limits()
        {
            var rng = new System.Random(60);
            int checkedPoints = 0;
            for (int trial = 0; trial < 1500; trial++)
            {
                var p = new Paragraph { DefaultWs = "en" };
                int runs = rng.Next(0, 10);
                for (int i = 0; i < runs; i++)
                {
                    string text = new string('a', rng.Next(0, 4)) + (rng.Next(4) == 0 ? DocumentEditor.LineSeparator.ToString() : string.Empty);
                    p.Runs.Add(Marked(text, new[] { TextPropertyNumbers.EditableYes, TextPropertyNumbers.EditableNo, TextPropertyNumbers.EditableSemi }[rng.Next(3)]));
                }
                var e = new DocumentEditor(Doc(p));
                int len = p.Length;
                if (rng.Next(2) == 0) { int limit = rng.Next(0, len + 1); e.CaretLimit = para => limit; }
                int visible = e.CaretLimit == null ? len : e.CaretLimit(0);
                for (int offset = 0; offset <= len; offset++)
                    foreach (int direction in new[] { -1, 0, 1 })
                    {
                        var at = new TextPosition(0, offset);
                        Assert.Equal(ByAskingEveryPoint(e, at, direction, visible), e.NearestEditable(at, direction));
                        checkedPoints++;
                    }
            }
            Assert.True(checkedPoints > 1000);
        }

        /// <summary>A paragraph of many runs that do not merge (their writing systems alternate) with locked runs in the middle.</summary>
        static DocumentEditor ManyRuns(int runs, int lockedFrom, int lockedCount)
        {
            var p = new Paragraph { DefaultWs = "en" };
            for (int i = 0; i < runs; i++)
            {
                bool locked = i >= lockedFrom && i < lockedFrom + lockedCount;
                var r = locked ? Marked("LL", TextPropertyNumbers.EditableNo) : new Run { Ws = i % 2 == 0 ? "en" : "fr", Text = "ab" };
                if (locked) r.Ws = i % 2 == 0 ? "en" : "fr";
                p.Runs.Add(r);
            }
            return new DocumentEditor(Doc(p));
        }

        [Fact]
        public void NearestEditable_walks_outward_from_the_point_so_its_cost_follows_the_distance_not_the_run_count()
        {
            // Counted, not timed (the probe fires once per run looked at): a point inside one locked run among 20,000 runs finds its
            // neighbours after a few runs, and a stretch of six locked runs costs about as many runs as the way out of it.
            var one = ManyRuns(20000, 10000, 1);
            int asked = 0;
            one.EditableAtProbe = () => asked++;
            Assert.Equal(new TextPosition(0, 20000), one.NearestEditable(new TextPosition(0, 20001)));   // the middle of the locked run: back wins the tie
            Assert.True(asked <= 12, asked + " runs looked at");

            var six = ManyRuns(20000, 10000, 6);
            asked = 0;
            six.EditableAtProbe = () => asked++;
            var at = new TextPosition(0, 20000 + 2 * 3 + 1);                                            // the middle of the stretch
            var found = six.NearestEditable(at);
            Assert.True(found.Offset == 20000 || found.Offset == 20000 + 12, found.ToString());
            Assert.True(asked <= 30, asked + " runs looked at");

            asked = 0;
            Assert.Equal(new TextPosition(0, 5), one.NearestEditable(new TextPosition(0, 5)));           // already editable: asked once
            Assert.True(asked <= 2);
        }
        /// <summary>What NearestEditable answers, worked out the slow way: ask every insertion point up to the visible end.</summary>
        static TextPosition ByAskingEveryPoint(DocumentEditor e, TextPosition p, int direction, int visibleEnd)
        {
            if (e.IsEditableAt(p)) return p;
            int Find(int from, int step)
            {
                for (int o = from; o >= 0 && o <= visibleEnd; o += step)
                    if (e.IsEditableAt(new TextPosition(p.Paragraph, o))) return o;
                return -1;
            }
            int fwd = Find(p.Offset + 1, 1), back = Find(p.Offset - 1, -1);
            int pick;
            if (fwd < 0) pick = back;
            else if (back < 0) pick = fwd;
            else if (direction > 0) pick = fwd;
            else if (direction < 0) pick = back;
            else pick = p.Offset - back <= fwd - p.Offset ? back : fwd;
            return pick < 0 ? p : new TextPosition(p.Paragraph, pick);
        }

        [Fact]
        public void Editable_places_merge_touching_runs_and_stop_at_every_read_only_run()
        {
            var p = new Paragraph { DefaultWs = "en" };
            p.Runs.Add(new Run { Ws = "en", Text = "ab" });
            p.Runs.Add(Marked("cd", TextPropertyNumbers.EditableSemi));   // semi-editable counts as editable
            p.Runs.Add(Marked("LOCK", TextPropertyNumbers.EditableNo));
            p.Runs.Add(Marked("", TextPropertyNumbers.EditableNo));       // an empty run is nothing
            p.Runs.Add(new Run { Ws = "fr", Text = "e" });
            p.Runs.Add(new Run { Ws = "en", Text = "f" });
            p.Runs.Add(Marked("LOCK", TextPropertyNumbers.EditableNo));
            var e = new DocumentEditor(Doc(Para("en"), p));               // an empty paragraph is a place of its own
            e.SetCaret(new TextPosition(0, 0));
            var starts = new List<TextPosition>();
            while (e.TryGetEditablePlace(EditablePlace.Next, out var next)) { starts.Add(next); e.SetCaret(next); }
            Assert.Equal(new[] { new TextPosition(1, 0), new TextPosition(1, 8) }, starts);
            Assert.True(e.TryGetEditablePlace(EditablePlace.Previous, out var back));
            Assert.Equal(new TextPosition(1, 4), back);                   // the end of the first place in the paragraph
        }


        [Fact]
        public void A_read_only_run_of_one_character_still_splits_the_editable_places_around_it()
        {
            var p = new Paragraph { DefaultWs = "en" };
            p.Runs.Add(new Run { Ws = "en", Text = "ab" });
            p.Runs.Add(Marked("L", TextPropertyNumbers.EditableNo));      // offsets 2 and 3 are adjacent, but the character between them is not editable
            p.Runs.Add(new Run { Ws = "en", Text = "cd" });
            var e = new DocumentEditor(Doc(Para("en"), p));
            e.SetCaret(new TextPosition(0, 0));
            var starts = new List<TextPosition>();
            while (e.TryGetEditablePlace(EditablePlace.Next, out var next)) { starts.Add(next); e.SetCaret(next); }
            Assert.Equal(new[] { new TextPosition(1, 0), new TextPosition(1, 3) }, starts);
            Assert.True(e.TryGetEditablePlace(EditablePlace.Previous, out var back));
            Assert.Equal(new TextPosition(1, 2), back);                   // the end of the first place
        }
        [Fact]
        public void Each_paragraph_is_an_editable_place_and_a_read_only_field_has_none()
        {
            var locked = Para("en", "en", "locked");
            locked.Runs[0].Props.SetInt(TextPropertyNumbers.Editable, TextPropertyNumbers.EditableNo);
            var e = new DocumentEditor(Doc(Para("en", "en", "one"), locked, Para("en", "en", "three")));
            e.SetCaret(new TextPosition(0, 1));
            Assert.True(e.TryGetEditablePlace(EditablePlace.Next, out var p));
            Assert.Equal(new TextPosition(2, 0), p);                           // over the read-only paragraph
            e.IsReadOnly = true;
            Assert.False(e.TryGetEditablePlace(EditablePlace.Next, out _));
            Assert.False(e.TryGetEditablePlace(EditablePlace.Last, out _));
        }

        [Fact]
        public void Pasting_a_copied_read_only_run_gives_editable_text_that_can_be_deleted_again()
        {
            var p = new Paragraph { DefaultWs = "en" };
            p.Runs.Add(new Run { Ws = "en", Text = "ed" });
            p.Runs.Add(Marked("LOCKED", TextPropertyNumbers.EditableNo));
            p.Runs.Add(new Run { Ws = "en", Text = "it" });
            var e = new DocumentEditor(Doc(p));
            e.Anchor = new TextPosition(0, 2);
            e.SetCaret(new TextPosition(0, 8), true);
            var fragment = e.GetFragment();
            Assert.Equal(RunEditable.NotEditable, fragment[0].Runs[0].Editable);   // copying keeps the property on the clipboard copy

            e.SetCaret(new TextPosition(0, 1));
            e.Paste(fragment);
            Assert.Equal("eLOCKEDdLOCKEDit", Text(e));
            var pasted = e.Document.Paragraphs[0].Runs;
            Assert.Equal(new[] { RunEditable.Editable, RunEditable.NotEditable, RunEditable.Editable }, new[] { pasted[0].Editable, pasted[1].Editable, pasted[2].Editable });
            Assert.Equal("eLOCKEDd", pasted[0].Text);
            Assert.Equal(RunEditable.NotEditable, fragment[0].Runs[0].Editable);   // the caller's fragment is untouched

            e.Anchor = new TextPosition(0, 1);
            e.SetCaret(new TextPosition(0, 7), true);
            Assert.True(e.CanEdit);
            e.DeleteSelection();
            Assert.Equal("edLOCKEDit", Text(e));
        }

        static DocumentEditor SemiThen(string semi, string after = "")
        {
            var p = new Paragraph { DefaultWs = "en" };
            p.Runs.Add(Marked(semi, TextPropertyNumbers.EditableSemi));
            if (after.Length > 0) p.Runs.Add(new Run { Ws = "en", Text = after });
            return new DocumentEditor(Doc(p));
        }

        [Fact]
        public void Text_pasted_into_a_semi_editable_run_is_semi_editable_and_leaves_the_run_whole()
        {
            var e = SemiThen("semi");
            e.SetCaret(new TextPosition(0, 2));
            e.Paste(new List<Paragraph> { Para("en", "en", "Q") });
            Assert.Equal("seQmi", Text(e));
            var run = Assert.Single(e.Document.Paragraphs[0].Runs);   // typing there also leaves one run
            Assert.Equal(RunEditable.SemiEditable, run.Editable);
            e.Anchor = new TextPosition(0, 0);
            e.SetCaret(new TextPosition(0, 5), true);
            Assert.False(e.CanFormatSelection);                       // the pasted letter is refused formatting like the rest
        }

        [Fact]
        public void Pasted_text_at_the_edge_of_a_semi_editable_run_takes_the_semi_run_it_follows_or_precedes()
        {
            var after = SemiThen("semi", "tail");
            after.SetCaret(new TextPosition(0, 4));
            after.Paste(new List<Paragraph> { Para("en", "en", "Q") });
            Assert.Equal(new[] { RunEditable.SemiEditable, RunEditable.Editable }, after.Document.Paragraphs[0].Runs.Select(r => r.Editable).ToArray());
            Assert.Equal("semiQ", after.Document.Paragraphs[0].Runs[0].Text);

            var p = new Paragraph { DefaultWs = "en" };
            p.Runs.Add(new Run { Ws = "en", Text = "head" });
            p.Runs.Add(Marked("semi", TextPropertyNumbers.EditableSemi));
            var before = new DocumentEditor(Doc(p));
            before.SetCaret(new TextPosition(0, 4));   // between the editable and the semi run: typing goes in the editable one
            before.Paste(new List<Paragraph> { Para("en", "en", "Q") });
            Assert.Equal("headQ", before.Document.Paragraphs[0].Runs[0].Text);
            Assert.Equal(RunEditable.Editable, before.Document.Paragraphs[0].Runs[0].Editable);
        }

        [Fact]
        public void Paste_over_a_selection_and_across_paragraphs_in_a_semi_editable_run_stays_semi_editable()
        {
            var e = SemiThen("semi");
            e.Anchor = new TextPosition(0, 1);
            e.SetCaret(new TextPosition(0, 3), true);
            e.Paste(new List<Paragraph> { Para("en", "en", "AB"), Para("en", "en", "CD") });
            Assert.Equal(2, e.Document.Paragraphs.Count);
            Assert.Equal("sAB", e.Document.Paragraphs[0].Text);
            Assert.Equal("CDi", e.Document.Paragraphs[1].Text);
            Assert.All(e.Document.Paragraphs, p => Assert.All(p.Runs, r => Assert.Equal(RunEditable.SemiEditable, r.Editable)));
        }

        [Fact]
        public void The_host_can_still_apply_changes_to_a_read_only_field()
        {
            var e = new DocumentEditor(OneParagraph("abc")) { IsReadOnly = true };
            var d = new EditDelta { Start = 0, CaretAfter = new TextPosition(0, 1) };
            d.Before.Add(e.Document.Paragraphs[0].Clone());
            d.After.Add(Para("en", "en", "xyz"));
            e.Apply(d);
            Assert.Equal("xyz", Text(e));
        }
    }
}
