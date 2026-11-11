using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using SIL.TextEditing.Avalonia;
using Xunit;

namespace SIL.TextEditing.Playground.Tests
{
    /// <summary>ADR 0035 in the playground, with real FieldWorks Markdown: the bar's "Markdown" and Ctrl+Shift+M show a field as Markdown, and the
    /// return goes through the same reader and checker as paste, as one undo step.</summary>
    public class SourceModePlaygroundTests
    {
        static void TypeInSource(Session s, int line, string text)
        {
            var view = s.Field.SourceView!;
            view.Editor.SetCaret(new TextPosition(line, view.Editor.Document.Paragraphs[line].Length));
            view.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = text, Source = view });
            Headless.Pump();
        }

        [Fact]
        public void The_bar_s_Markdown_shows_the_field_as_Markdown_and_a_return_with_no_edit_changes_nothing() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.MixedBidi, developerMode: false))
            {
                var before = s.Editor.Document.Paragraphs.ToList();
                s.Select(0, 0, 3);
                Headless.Pump();
                s.ClickLogical(PlaygroundView.Names.BarSource);
                Assert.True(s.Field.IsSourceMode);
                Assert.False(s.View.SelectionBarOpen);
                string source = s.Field.SourceView!.Editor.Document.PlainText();
                Assert.StartsWith("The word [كتاب]{ws=ar}", source);
                Assert.Equal(s.View.MarkdownText.TrimEnd('\n'), source);
                Assert.Contains("source mode: Editing as Markdown", s.Log);

                Assert.True(s.Field.LeaveSourceMode());
                Assert.Equal(before, s.Editor.Document.Paragraphs);
                Assert.Equal(0, s.View.Undo.DoneSteps);
                Assert.Contains("source mode: Back to rich text", s.Log);
            }
        });

        [Fact]
        public void Edits_in_Markdown_come_back_as_one_undo_step_with_the_paragraph_keys_and_one_undo_restores_them() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.MixedBidi))
            {
                var keys = s.Editor.Document.Paragraphs.Select(p => p.Key).ToList();
                int lastIndex = s.Editor.Document.Paragraphs.Count - 1;
                string first = s.ParagraphText(0), last = s.ParagraphText(lastIndex);
                s.Press(Key.M, KeyModifiers.Control | KeyModifiers.Shift);
                Assert.True(s.Field.IsSourceMode);
                TypeInSource(s, 0, " **bold**");
                int lastLine = s.Field.SourceView!.Editor.Document.Paragraphs.Count - 1;
                TypeInSource(s, lastLine, " end");
                s.Press(Key.M, KeyModifiers.Control | KeyModifiers.Shift);
                Headless.Pump();
                Assert.False(s.Field.IsSourceMode);
                Assert.Equal(first + " bold", s.ParagraphText(0));
                var bold = s.Editor.Document.Paragraphs[0].Runs.Last();
                Assert.Equal("bold", bold.Text);
                Assert.Equal(TextPropertyNumbers.ToggleOn, bold.Props.IntProps[TextPropertyNumbers.Bold].Value);
                // The playground stores NFD (Document.Normalization), and what comes back from Markdown is prepared as pasted text is.
                Assert.Equal((last + " end").Normalize(System.Text.NormalizationForm.FormD), s.ParagraphText(lastIndex));
                Assert.Equal(keys, s.Editor.Document.Paragraphs.Select(p => p.Key));
                Assert.Equal(1, s.View.Undo.DoneSteps);
                Assert.Contains("edit: Edit as Markdown", s.Log);

                s.Press(Key.Z, KeyModifiers.Control);
                Assert.Equal(first, s.ParagraphText(0));
                Assert.Equal(last, s.ParagraphText(lastIndex));
            }
        });

        [Fact]
        public void An_error_keeps_the_field_in_Markdown_a_warning_does_not_and_throw_away_restores_the_text() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.MixedBidi))
            {
                string first = s.ParagraphText(0);
                s.View.EditAsSource();
                TypeInSource(s, 0, " [x](/lexicon/palit)");   // a path with no project to look it up in: fwmd.link.not-found
                Assert.False(s.Field.LeaveSourceMode());
                Assert.True(s.Field.IsSourceMode);
                Assert.Contains("The Markdown has 1 error", s.Log);
                Assert.Equal(first, s.ParagraphText(0));

                s.Field.DiscardSourceChanges();
                Assert.False(s.Field.IsSourceMode);
                Assert.Equal(first, s.ParagraphText(0));
                Assert.Equal(0, s.View.Undo.DoneSteps);
                Assert.Contains("thrown away", s.Log);
            }
        });

        [Fact]
        public void The_context_menu_in_Markdown_shows_the_finding_and_its_Cut_and_Paste_work_on_the_Markdown() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.MixedBidi))
            {
                string first = s.ParagraphText(0);
                s.View.EditAsSource();
                TypeInSource(s, 0, " [x](/lexicon/palit)");
                var view = s.Field.SourceView!;
                for (int i = 0; i < 20 && !view.IsLanguageAnalysisCurrent; i++) Headless.Pump();
                int end = view.Editor.Document.Paragraphs[0].Length;
                view.Editor.SetCaret(new TextPosition(0, end - 3));
                s.Press(Key.F10, KeyModifiers.Shift);   // on the field: it goes to the view
                Headless.Pump();
                Assert.Contains(s.View.LastContextMenuItems, c => c.StartsWith("Error: ", System.StringComparison.Ordinal));
                Assert.Contains("Paste", s.View.LastContextMenuItems);
                s.View.CloseContextMenu();

                // The menu's Cut goes to the Markdown, not to the rich text, which is held while the field is in source mode.
                view.Editor.SetCaret(new TextPosition(0, end - " [x](/lexicon/palit)".Length));
                view.Editor.SetCaret(new TextPosition(0, end), extend: true);
                s.View.CopyAsync(true, view).GetAwaiter().GetResult();
                Headless.Pump();
                Assert.DoesNotContain("palit", view.Editor.Document.PlainText());
                Assert.Equal(first, s.ParagraphText(0));
                Assert.True(s.Field.LeaveSourceMode());
            }
        });

        [Fact]
        public void The_host_s_formatting_and_undo_are_refused_while_the_field_is_in_Markdown() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.MixedBidi))
            {
                s.Select(0, 0, 3);
                s.View.ToggleBold();
                Assert.Equal(1, s.View.Undo.DoneSteps);
                s.View.EditAsSource();
                s.Field.Editor.SetCaret(new TextPosition(0, 0));
                s.Field.Editor.SetCaret(new TextPosition(0, 3), extend: true);
                Assert.False(s.View.ToggleBold());
                Assert.False(s.View.UndoLast());
                Assert.Equal(1, s.View.Undo.DoneSteps);
                Assert.Contains("refused, the field is in source mode", s.Log);
                s.Field.DiscardSourceChanges();
                Assert.True(s.View.UndoLast());
            }
        });

        [Fact]
        public void A_link_s_guid_is_muted_in_the_Markdown() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Picture))
            {
                Assert.True(s.View.EditAsSource());
                var view = s.Field.SourceView!;
                for (int i = 0; i < 20 && !view.IsLanguageAnalysisCurrent; i++) Headless.Pump();
                Assert.Contains(view.LanguageTokens, t => t.Kind == SourceTokenKinds.Muted);
                Assert.DoesNotContain(view.LanguageDiagnostics, d => d.Severity == DiagnosticSeverity.Error);
                Assert.True(s.Field.LeaveSourceMode());
            }
        });
    }
}
