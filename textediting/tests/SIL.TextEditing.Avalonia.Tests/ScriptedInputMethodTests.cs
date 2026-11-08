using System.Linq;
using Avalonia.Input.TextInput;
using Xunit;
using static SIL.TextEditing.Tests.TestDocs;

namespace SIL.TextEditing.Avalonia.Tests
{
    public class ScriptedInputMethodTests
    {
        static (RichTextField Field, ScriptedInputMethod Ime, SimpleUndoStack Undo) Rig(Document doc, int caret)
        {
            var field = Fixtures.Field(doc);
            field.Editor.SetCaret(new TextPosition(0, caret));
            var undo = new SimpleUndoStack();
            undo.Attach(field.Editor);
            return (field, new ScriptedInputMethod(field), undo);
        }

        static void State(RichTextField field, SimpleUndoStack undo, int caret, int steps, params string[] runs)
        {
            Assert.Equal(runs, field.Editor.Document.Paragraphs[0].Runs.Select(r => r.Ws + ":" + r.Text).ToArray());
            Assert.Equal(new TextPosition(0, caret), field.Editor.Caret);
            Assert.Equal(steps, undo.DoneSteps);
        }

        [Fact]
        public void Japanese_candidate_replaces_the_selection_only_when_committed() => Headless.Run(() =>
        {
            var (field, ime, undo) = Rig(OneParagraph("abc"), 3);
            ime.Selection = new TextSelection(1, 3);
            Assert.Equal("abc", ime.SurroundingText);
            Assert.Equal(1, ime.Selection.Start);
            Assert.Equal(3, ime.Selection.End);
            ime.Preedit("に", 1);
            Assert.Equal("に", field.Preedit);
            State(field, undo, 3, 0, "en:abc");
            ime.Preedit("にほんご", 4);
            Assert.Equal("にほんご", field.Preedit);
            State(field, undo, 3, 0, "en:abc");
            ime.Commit("日本語");
            Assert.Null(field.Preedit);
            State(field, undo, 4, 1, "en:a日本語");
            Assert.True(undo.Undo());
            State(field, undo, 3, 0, "en:abc");
            Assert.Equal(1, undo.UndoneSteps);
        });

        [Fact]
        public void Korean_jamo_rebuild_the_preedit_before_one_syllable_is_committed() => Headless.Run(() =>
        {
            var doc = OneParagraph("x");
            doc.Normalization = System.Text.NormalizationForm.FormD;
            var (field, ime, undo) = Rig(doc, 1);
            ime.Preedit("ㄱ", 1);
            State(field, undo, 1, 0, "en:x");
            Assert.Equal("ㄱ", field.Preedit);
            ime.Preedit("가", 1);
            State(field, undo, 1, 0, "en:x");
            Assert.Equal("가", field.Preedit);
            ime.Preedit("각", 1);
            State(field, undo, 1, 0, "en:x");
            Assert.Equal("각", field.Preedit);
            ime.Commit("각");
            State(field, undo, 4, 1, "en:x각");
            Assert.True(undo.Undo());
            State(field, undo, 1, 0, "en:x");
            Assert.Equal(1, undo.UndoneSteps);
        });

        [Fact]
        public void Keyman_backspaces_cross_runs_and_writing_systems_before_replacement() => Headless.Run(() =>
        {
            var (field, ime, undo) = Rig(Doc(Para("en", "en", "ab", "fr", "cd")), 4);
            Assert.Equal("abcd", ime.SurroundingText);
            ime.Backspace();
            State(field, undo, 3, 1, "en:ab", "fr:c");
            ime.Backspace();
            State(field, undo, 2, 2, "en:ab");
            ime.Backspace();
            State(field, undo, 1, 3, "en:a");
            Assert.Equal("a", ime.SurroundingText);
            ime.Commit("Z");
            State(field, undo, 2, 4, "en:aZ");
            Assert.True(undo.Undo());
            State(field, undo, 1, 3, "en:a");
            Assert.True(undo.Undo());
            Assert.True(undo.Undo());
            Assert.True(undo.Undo());
            State(field, undo, 4, 0, "en:ab", "fr:cd");
            Assert.Equal(4, undo.UndoneSteps);
        });

        [Fact]
        public void Commit_at_a_mixed_direction_boundary_places_the_caret_after_inserted_text() => Headless.Run(() =>
        {
            var (field, ime, undo) = Rig(Doc(Para("en", "en", "ab", "ar", "مرحبا", "en", "cd")), 7);
            field.SetCaretHit(new global::Avalonia.Media.CharacterHit(7));
            ime.Preedit("ش", 1);
            State(field, undo, 7, 0, "en:ab", "ar:مرحبا", "en:cd");
            ime.Commit("ش");
            State(field, undo, 8, 1, "en:ab", "ar:مرحباش", "en:cd");
            var layout = field.ParagraphLayoutFor(0);
            var expected = layout.CaretRect(8, false);
            var trailing = layout.CaretRect(8, false, layout.EndHit(8));
            Assert.NotEqual(expected.X, trailing.X);
            Assert.Equal(expected.X, field.CaretRect().X, 5);
            Assert.True(undo.Undo());
            State(field, undo, 7, 0, "en:ab", "ar:مرحبا", "en:cd");
            Assert.Equal(1, undo.UndoneSteps);
        });

        [Fact]
        public void Cancelling_composition_preserves_the_model_selection_and_undo_history() => Headless.Run(() =>
        {
            var (field, ime, undo) = Rig(Doc(Para("en", "en", "ab", "fr", "cd")), 4);
            ime.Selection = new TextSelection(1, 4);
            ime.Preedit("に", 1);
            State(field, undo, 4, 0, "en:ab", "fr:cd");
            ime.Preedit("日本", 2);
            State(field, undo, 4, 0, "en:ab", "fr:cd");
            ime.Cancel();
            Assert.Null(field.Preedit);
            State(field, undo, 4, 0, "en:ab", "fr:cd");
            Assert.Equal(1, ime.Selection.Start);
            Assert.Equal(4, ime.Selection.End);
            Assert.False(undo.Undo());
            Assert.Equal(0, undo.UndoneSteps);
        });
    }
}
