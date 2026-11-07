using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Input;
using Xunit;
using static SIL.TextEditing.Tests.TestDocs;

namespace SIL.TextEditing.Avalonia.Tests
{
    public class SurfaceBasicsTests
    {
        [Fact]
        public void Runs_the_expected_Avalonia_generation()
        {
#if NET10_0_OR_GREATER
            Assert.Equal(12, Headless.AvaloniaMajor);
#else
            Assert.Equal(11, Headless.AvaloniaMajor);
#endif
        }

        [Fact]
        public void Loads_with_a_size_and_wraps_to_the_width() => Headless.Run(() =>
        {
            var doc = Doc(Para("en", "en", string.Join(" ", Enumerable.Repeat("lorem ipsum", 20))));
            var field = Fixtures.Field(doc, 700);
            Assert.True(field.Bounds.Width > 50 && field.Bounds.Height > 10);
            int wide = field.ParagraphLayoutFor(0).Lines.Count;
            Fixtures.Layout(field, 380);
            int narrow = field.ParagraphLayoutFor(0).Lines.Count;
            Assert.True(narrow > wide, "wide " + wide + " narrow " + narrow);
            Assert.All(field.ParagraphLayoutFor(0).Lines, l => Assert.True(l.Line.Width <= 380.5));
        });

        [Fact]
        public void Typing_through_text_input_uses_the_active_ws_and_the_ws_label_event_fires() => Headless.Run(() =>
        {
            var (window, field) = Fixtures.InWindow(Basic());   // announced only while the field has focus (#59)
            try
            {
                field.Focus();
                Headless.Pump();
                var seen = new List<string>();
                field.ActiveWsChanged += (o, n) => seen.Add(o + ">" + n);
                field.Editor.SetCaret(new TextPosition(0, 9));
                field.Editor.SetCaret(new TextPosition(0, 2));
                Assert.Equal(new[] { "en>fr", "fr>en" }, seen);
                field.Editor.SetCaret(new TextPosition(0, 13));
                field.Editor.SetActiveWs("en");
                field.Type("X");
                Assert.Equal("en", field.Editor.Document.Paragraphs[0].RunAfter(13, out _)!.Ws);
            }
            finally { window.Close(); }
        });

        [Fact]
        public void Selection_state_reports_mixed_writing_systems() => Headless.Run(() =>
        {
            var field = Fixtures.Field(Basic());
            string state = "";
            field.SelectionWsState += s => state = s;
            field.Editor.SetCaret(new TextPosition(0, 2));
            field.Editor.SetCaret(new TextPosition(0, 10), true);
            Assert.Equal("mixed:en,fr", state);
            field.Editor.SetCaret(new TextPosition(0, 7));
            field.Editor.SetCaret(new TextPosition(0, 11), true);
            Assert.Equal("fr", state);
        });

        [Fact]
        public void Arrow_keys_move_one_cluster_over_a_combining_sequence() => Headless.Run(() =>
        {
            var field = Fixtures.Field(OneParagraph("aéb"));
            field.Editor.SetCaret(new TextPosition(0, 1));
            field.Press(Key.Right);
            Assert.Equal(3, field.Editor.Caret.Offset);
            field.Press(Key.Left);
            Assert.Equal(1, field.Editor.Caret.Offset);
        });

        [Fact]
        public void Caret_stops_never_fall_inside_a_surrogate_pair_or_combining_cluster() => Headless.Run(() =>
        {
            var text = "café naïve | x\U0001F600y\U00020000z | pq\U000F0001r";
            var field = Fixtures.Field(OneParagraph(text), 2000);
            var layout = field.ParagraphLayoutFor(0);
            var clusters = GraphemeClusters.Starts(text).ToList();
            clusters.Add(text.Length);
            var stops = layout.Lines.SelectMany(l => layout.Stops(l)).Select(k => k.Key).Distinct().ToList();
            Assert.All(stops, o => Assert.Contains(o, clusters));
        });

        [Fact]
        public void Formatter_text_length_is_the_model_length_because_the_source_ends_without_an_end_of_paragraph_run() => Headless.Run(() =>
        {
            var p = new Paragraph();
            p.Runs.Add(new Run { Ws = "en", Text = "a\U0001F600" });
            p.Runs.Add(Run.Object("fr", "\u0008pic"));
            p.Runs.Add(new Run { Ws = "fr", Text = "éz" });
            var field = Fixtures.Field(Doc(p), 600);
            var layout = field.ParagraphLayoutFor(0);
            Assert.Equal(p.Length, layout.Lines.Sum(l => l.Length));
        });

        [Fact]
        public void Caret_stops_inside_a_line_that_ends_in_a_right_to_left_run_are_all_distinct() => Headless.Run(() =>
        {
            // With a TextEndOfParagraph run after the text, Avalonia (11.3.17 and 12.1.2) reports every offset of a final
            // right-to-left run at the same x, so a purely Arabic paragraph had one caret stop. The source now ends by
            // returning null instead.
            var p = Para("ar", "ar", "مرحبا بالعالم");
            p.Rtl = true;
            var field = Fixtures.Field(Doc(p), 400);
            var layout = field.ParagraphLayoutFor(0);
            var stops = layout.VisualStops(layout.Lines[0]);
            Assert.Equal(p.Length + 1, stops.Count);
            Assert.True(stops.Zip(stops.Skip(1), (a, b) => b.X - a.X).All(d => d > 2));
        });

        [Fact]
        public void Editing_relays_out_only_the_edited_paragraph() => Headless.Run(() =>
        {
            var doc = Doc(Enumerable.Range(0, 50).Select(i => Para("en", "en", "paragraph " + i)).ToArray());
            var field = Fixtures.Field(doc);
            field.Editor.SetCaret(new TextPosition(25, 4));
            long before = field.LayoutCount;
            for (int i = 0; i < 10; i++)
            {
                field.Type("x");
                Fixtures.Layout(field, 520);
            }
            Assert.Equal(10, field.LayoutCount - before);
        });

        [Fact]
        public void Ctrl_Z_only_asks_the_host_to_undo() => Headless.Run(() =>
        {
            var field = Fixtures.Field(OneParagraph("hello world"));
            var undo = new SimpleUndoStack();
            undo.Attach(field.Editor);
            field.UndoRequested += () => undo.Undo();
            field.Editor.SetCaret(new TextPosition(0, 11));
            foreach (var c in "abc") field.Type(c.ToString());
            Assert.Equal(3, undo.Done.Count);
            field.Press(Key.Z, KeyModifiers.Control);
            Assert.Equal("hello world", field.Editor.Document.Paragraphs[0].Text);   // the typing was one undo step (ADR 0023)
            Assert.Equal(1, undo.UndoneSteps);
        });

        [Fact]
        public void Tab_is_left_to_the_host() => Headless.Run(() =>
        {
            var field = Fixtures.Field(OneParagraph("x"));
            Assert.False(field.HandleKey(Key.Tab, KeyModifiers.None));
        });

        [Fact]
        public void Object_run_is_one_caret_stop_with_a_32_by_24_box() => Headless.Run(() =>
        {
            var p = new Paragraph();
            p.Runs.Add(new Run { Ws = "en", Text = "before " });
            p.Runs.Add(Run.Object("en", "\u0008pic-guid-1234"));
            p.Runs.Add(new Run { Ws = "en", Text = " after" });
            var field = Fixtures.Field(Doc(p), 520, f => f.ObjectImage = Fixtures.Picture);
            field.Editor.SetCaret(new TextPosition(0, 6));
            var offsets = new List<int> { 6 };
            for (int i = 0; i < 3; i++)
            {
                field.Press(Key.Right);
                offsets.Add(field.Editor.Caret.Offset);
            }
            Assert.Equal(new[] { 6, 7, 8, 9 }, offsets);
            var bounds = field.ParagraphLayoutFor(0).Lines[0].Line.GetTextBounds(7, 1);
            Assert.Single(bounds);
            Assert.True(bounds[0].Rectangle.Width > 20);
        });

        [Fact]
        public void Automation_peer_is_an_edit_with_a_name_and_the_text_value() => Headless.Run(() =>
        {
            var field = Fixtures.Field(Basic());
            global::Avalonia.Automation.AutomationProperties.SetName(field, "Rich text field");
            var peer = ControlAutomationPeer.CreatePeerForElement(field);
            Assert.Equal(AutomationControlType.Edit, peer.GetAutomationControlType());
            Assert.Equal("Rich text field", peer.GetName());
            var value = (global::Avalonia.Automation.Provider.IValueProvider)peer;
            Assert.StartsWith("Hello bonjour world", value.Value);
            // Gap: no TextPattern provider yet (see docs/reference/limitations.md).
        });
    }
}
