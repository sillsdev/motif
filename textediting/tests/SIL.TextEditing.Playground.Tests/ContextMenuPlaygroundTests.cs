using Avalonia;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Xunit;

namespace SIL.TextEditing.Playground.Tests
{
    /// <summary>ADR 0021: a context-menu request reports the clicked word and its range, its writing system, the run, any link or
    /// object and the selection; the host (here the playground) builds the menu.</summary>
    public class ContextMenuPlaygroundTests
    {
        static int Requests(Session s) => s.View.EventLog.Count(l => l.StartsWith("context menu ("));

        // A menu that is still open swallows the next click outside it, as a menu does, so close it first.
        static void Right(Session s, Point at)
        {
            s.View.CloseContextMenu();
            s.RightClick(at);
        }

        /// <summary>The middle of the first picture in a paragraph, in window coordinates.</summary>
        static Point PointOnPicture(Session s, int paragraph)
        {
            var centre = s.Field.ParagraphLayoutFor(paragraph).ObjectRects().First().Key.Center;
            return s.Field.TranslatePoint(new Point(centre.X, s.Field.ParagraphTop(paragraph) + centre.Y), TopLevel.GetTopLevel(s.Field)!)!.Value;
        }

        [Fact]
        public void A_right_click_on_a_word_moves_the_caret_there_and_reports_the_word_its_writing_system_and_run() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.FindReplace))
            {
                s.Caret(0, 0);
                Right(s, s.PointAt(2, 6));
                var info = s.View.LastContextInfo!;
                Assert.Equal("bonjour", info.Word);
                Assert.Equal(new TextRange(2, 4, 11), info.WordRange);
                Assert.Equal("fr", info.WritingSystem);
                Assert.Equal("bonjour", info.Run!.Text);
                Assert.Equal("fr", info.Run.Ws);
                Assert.Equal(2, s.Editor.Caret.Paragraph);
                Assert.False(s.Editor.HasSelection);
                Assert.Equal(1, Requests(s));
                Assert.Contains("context menu (Pointer)", s.Log);
                Assert.Contains("Word: bonjour (fr)", s.View.LastContextMenuItems);
                Assert.Contains("Find \"bonjour\"", s.View.LastContextMenuItems);
            }
        });

        [Fact]
        public void The_spelling_suggestion_slot_is_there_and_empty_until_the_spell_checker_fills_it() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.FindReplace))
            {
                Right(s, s.PointAt(0, 5));
                Assert.Empty(s.View.LastContextInfo!.SpellingSuggestions);
                Assert.Contains("(no spelling suggestions)", s.View.LastContextMenuItems);
            }
        });

        [Fact]
        public void A_right_click_inside_the_selection_keeps_it_and_one_beside_or_outside_it_moves_the_caret_first() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.FindReplace))
            {
                s.Select(0, 4, 11);   // "cat sat"
                Right(s, s.PointAt(0, 6));
                Assert.Equal(new TextRange(0, 4, 11), s.Editor.SelectionRange);
                Assert.True(s.View.LastContextInfo!.HasSelection);
                Assert.Equal("cat sat", s.View.LastContextInfo.SelectedText);
                Assert.Equal("cat", s.View.LastContextInfo.Word);

                Right(s, s.PointAt(0, 10));   // the last selected character
                Assert.Equal(new TextRange(0, 4, 11), s.Editor.SelectionRange);

                Right(s, s.PointAt(0, 11));   // the space just after the selection
                Assert.False(s.Editor.HasSelection);
                Assert.Equal(0, s.Editor.Caret.Paragraph);
                Assert.True(s.Editor.Caret.Offset >= 11 && s.Editor.Caret.Offset <= 12);
                Assert.False(s.View.LastContextInfo!.HasSelection);

                s.Select(0, 4, 11);
                Right(s, s.PointAt(1, 2));   // another paragraph
                Assert.False(s.Editor.HasSelection);
                Assert.Equal(1, s.Editor.Caret.Paragraph);
                Assert.Equal(4, Requests(s));
            }
        });

        [Fact]
        public void The_Menu_key_and_Shift_F10_ask_about_the_caret_and_each_gives_one_request() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.FindReplace))
            {
                s.Caret(2, 7);   // inside "bonjour"
                s.Press(Key.Apps);
                Assert.Equal(1, Requests(s));
                Assert.Contains("context menu (Keyboard)", s.Log);
                Assert.Equal("bonjour", s.View.LastContextInfo!.Word);
                Assert.Equal("fr", s.View.LastContextInfo.WritingSystem);

                s.Caret(0, 7);   // the end of "cat"
                s.Press(Key.F10, KeyModifiers.Shift);
                Assert.Equal(2, Requests(s));
                Assert.Equal("cat", s.View.LastContextInfo.Word);
                Assert.Equal(new TextPosition(0, 7), s.View.LastContextInfo.Position);
            }
        });

        [Fact]
        public void A_right_click_in_the_empty_space_past_the_end_of_a_line_reports_no_word() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.FindReplace))
            {
                var onText = s.PointAt(4, 20);
                Right(s, new Point(onText.X + 250, onText.Y));
                var info = s.View.LastContextInfo!;
                Assert.Equal(string.Empty, info.Word);
                Assert.Null(info.EmbeddedObject);
                Right(s, onText);
                Assert.NotEqual(string.Empty, s.View.LastContextInfo!.Word);
            }
        });

        [Fact]
        public void A_picture_and_its_data_are_reported_when_it_is_under_the_pointer() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.FindReplace))
            {
                Right(s, PointOnPicture(s, 3));
                var obj = s.View.LastContextInfo!.EmbeddedObject;
                Assert.NotNull(obj);
                Assert.Equal(new TextRange(3, 23, 24), obj!.Range);
                Assert.Equal(Scenarios.RedPicture, obj.Data);
                Assert.False(obj.IsLink);
                Assert.Equal(string.Empty, s.View.LastContextInfo.Word);
                Assert.Contains("object at", s.Log);
            }
        });
    }
}
