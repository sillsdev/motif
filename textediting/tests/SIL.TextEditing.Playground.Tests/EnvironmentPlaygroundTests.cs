using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Xunit;

namespace SIL.TextEditing.Playground.Tests
{
    /// <summary>ADR 0030 through the playground: the Environment scenario is a single-line field with the sample environment language service, driven the way a
    /// person drives it (typing, keys, the Insert buttons, a right click). The popup here is a real one: the playground has the Fluent theme.</summary>
    public class EnvironmentPlaygroundTests
    {
        static string[] Kinds(Session s) => s.Field.LanguageTokens.Select(t => t.Kind).ToArray();

        static void Replace(Session s, string text)
        {
            s.Editor.SetCaret(new TextPosition(0, 0));
            s.Editor.SetCaret(new TextPosition(0, s.ParagraphText(0).Length), true);
            s.Type(text);
            Headless.Pump();
        }

        [Fact]
        public void The_scenario_is_a_single_line_field_with_the_service_and_its_text_is_coloured_by_kind() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Environment))
            {
                Assert.Equal("/ [V] _ #", s.ParagraphText(0));
                Assert.True(s.Field.SingleParagraph);
                Assert.True(s.Field.SingleLine);
                Assert.True(s.Find<CheckBox>(PlaygroundView.Names.SingleParagraph).IsChecked);
                Assert.NotNull(s.Field.LanguageService);
                Assert.NotNull(s.Field.TokenStyles);
                Assert.Equal(new[] { "punct", "class", "slot", "boundary" }, Kinds(s));
                Assert.Empty(s.Field.LanguageDiagnostics);
                Assert.Equal(4, s.Field.ChipsDrawn().Count);
                Assert.Contains("language service: on", s.Log);
                Assert.Single(s.Editor.Document.Paragraphs[0].Runs);   // chips are not runs
            }
        });

        [Fact]
        public void Enter_goes_to_the_host_and_there_is_no_hard_line_break() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Environment))
            {
                s.Press(Key.Enter);
                Assert.Contains("Enter passed to the host", s.Log);
                s.Press(Key.Enter, KeyModifiers.Shift);
                Assert.Equal("/ [V] _ #", s.ParagraphText(0));
                s.Type("x\ny");
                Assert.Single(s.Editor.Document.Paragraphs);
                Assert.DoesNotContain(DocumentEditor.LineSeparator, s.ParagraphText(0));
            }
        });

        [Fact]
        public void A_problem_is_a_squiggle_with_a_message_and_is_never_in_the_text_and_goes_when_it_is_fixed_by_typing() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Environment))
            {
                Replace(s, "/ [X] _");
                Assert.Equal("/ [X] _", s.ParagraphText(0));
                var d = Assert.Single(s.Field.LanguageDiagnostics);
                Assert.Equal(SampleEnvironmentService.Codes.UnknownClass, d.Code);
                Assert.Equal(new LanguageRange(3, 4), d.Range);
                Assert.Equal(new[] { (3, 4) }, s.Field.ParagraphLayoutFor(0).DiagnosticMarksDrawn.Select(m => (m.Start, m.End)).ToArray());
                Assert.Single(s.Editor.Document.Paragraphs[0].Runs);
                Assert.Empty(s.Editor.Document.Paragraphs[0].Runs[0].Props.IntProps);
                Assert.Contains("fwmd.env.unknown-class", s.Log);
                Replace(s, "/ [C] _");
                Assert.Empty(s.Field.LanguageDiagnostics);
                Assert.Empty(s.Field.ParagraphLayoutFor(0).DiagnosticMarksDrawn);
            }
        });

        [Fact]
        public void Editing_an_error_away_leaves_no_squiggle_for_text_that_is_not_there() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Environment))
            {
                Replace(s, "/ _ _");
                Assert.Equal(SampleEnvironmentService.Codes.Syntax, Assert.Single(s.Field.LanguageDiagnostics).Code);
                s.Press(Key.Back);
                s.Press(Key.Back);
                s.Type("a");
                Assert.Equal("/ _a", s.ParagraphText(0));
                Assert.Empty(s.Field.LanguageDiagnostics);
                Assert.Empty(s.Field.ParagraphLayoutFor(0).DiagnosticMarksDrawn);
            }
        });

        [Fact]
        public void Typing_an_opening_bracket_opens_a_real_popup_of_the_classes_and_Enter_puts_the_chosen_one_in_place_of_the_bracket() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Environment))
            {
                Replace(s, "/ ");
                s.Type("[");
                Assert.True(s.Field.IsCompletionOpen);
                Assert.True(s.Field.CompletionPopupIsOpen);
                Assert.Equal(new[] { "[C]", "[V]", "[+son]", "[+lab, +vd]" }, s.Field.CompletionItems.Select(i => i.Label).ToArray());
                Assert.Contains("completion: 4 items", s.Log);
                s.Press(Key.Down);
                s.Press(Key.Enter);
                Assert.Equal("/ [V]", s.ParagraphText(0));
                Assert.False(s.Field.IsCompletionOpen);
                Assert.False(s.Field.CompletionPopupIsOpen);
                Assert.DoesNotContain("Enter passed to the host", s.Log);
                Assert.Equal(new TextPosition(0, 5), s.Editor.Caret);
            }
        });

        [Fact]
        public void A_click_on_a_row_of_the_popup_accepts_that_item() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Environment))
            {
                Replace(s, "/ ");
                s.Type("[");
                Assert.True(s.Field.CompletionPopupIsOpen);
                var row = s.Field.CompletionRow(2)!;
                var at = row.TranslatePoint(new Point(row.Bounds.Width / 2, row.Bounds.Height / 2), s.Window)!.Value;
                s.MouseClick(at);
                Assert.Equal("/ [+son]", s.ParagraphText(0));
                Assert.False(s.Field.IsCompletionOpen);
            }
        });

        [Fact]
        public void Typing_a_whole_environment_closes_the_popup_at_the_closing_bracket_and_Enter_and_Tab_then_reach_the_host() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Environment))
            {
                Replace(s, "/ ");
                s.Type("[");
                Assert.True(s.Field.IsCompletionOpen);
                s.Type("V");
                Headless.Pump();
                Assert.True(s.Field.IsCompletionOpen);                       // still completing the class name
                s.Type("]");
                Headless.Pump();
                Assert.False(s.Field.IsCompletionOpen);                      // the context ended
                foreach (var c in " _ a") { s.Type(c.ToString()); Headless.Pump(); Assert.False(s.Field.IsCompletionOpen); }
                Assert.False(s.Field.CompletionPopupIsOpen);
                s.Press(Key.Enter);
                Assert.Equal("/ [V] _ a", s.ParagraphText(0));
                Assert.Contains("Enter passed to the host", s.Log);
                s.Press(Key.Tab);
                Assert.Contains("leave requested", s.Log);
            }
        });

        [Fact]
        public void Typing_after_the_bracket_narrows_the_list_and_Esc_closes_it_and_leaves_the_text() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Environment))
            {
                Replace(s, "/ [");
                Assert.True(s.Field.IsCompletionOpen);
                s.Type("+");
                Headless.Pump();
                Assert.Equal(new[] { "[+son]", "[+lab, +vd]" }, s.Field.CompletionItems.Select(i => i.Label).ToArray());
                s.Press(Key.Escape);
                Assert.False(s.Field.IsCompletionOpen);
                Assert.False(s.Field.CompletionPopupIsOpen);
                Assert.Equal("/ [+", s.ParagraphText(0));
            }
        });

        [Fact]
        public void Ctrl_Space_in_an_empty_box_offers_the_slash_and_Tab_accepts_it() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Environment))
            {
                Replace(s, "x");
                s.Press(Key.Back);
                Assert.Equal(string.Empty, s.ParagraphText(0));
                s.Press(Key.Space, KeyModifiers.Control);
                Assert.Equal("/", s.Field.CompletionItems.Single().Label);
                s.Press(Key.Tab);
                Assert.Equal("/ ", s.ParagraphText(0));
                Assert.DoesNotContain("leave requested", s.Log);
            }
        });

        [Fact]
        public void The_optional_item_completion_leaves_the_caret_between_the_parentheses() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Environment))
            {
                Replace(s, "/ ");
                s.Press(Key.Space, KeyModifiers.Control);
                int index = s.Field.CompletionItems.ToList().FindIndex(i => i.Label == "()");
                Assert.True(index > 0);
                for (int i = 0; i < index; i++) s.Press(Key.Down);
                s.Press(Key.Enter);
                Assert.Equal("/ ()", s.ParagraphText(0));
                Assert.Equal(new TextPosition(0, 3), s.Editor.Caret);
            }
        });

        [Fact]
        public void The_Insert_buttons_are_enabled_as_the_service_says_for_the_caret_and_each_insert_is_one_undo_step() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Environment))
            {
                bool Enabled(string name) => s.Find<Button>(name).IsEnabled;
                s.Caret(0, s.ParagraphText(0).Length);
                Assert.False(Enabled(PlaygroundView.Names.EnvSlash));       // there is one
                Assert.False(Enabled(PlaygroundView.Names.EnvBar));
                Assert.False(Enabled(PlaygroundView.Names.EnvHash));        // after the boundary on the right
                Assert.False(s.Find<ComboBox>(PlaygroundView.Names.EnvClass).IsEnabled);
                s.Caret(0, 2);
                Assert.True(Enabled(PlaygroundView.Names.EnvHash));
                Assert.True(Enabled(PlaygroundView.Names.EnvOptional));
                Assert.True(s.Find<ComboBox>(PlaygroundView.Names.EnvClass).IsEnabled);
                Assert.False(Enabled(PlaygroundView.Names.EnvBar));          // there is one
                s.Click(PlaygroundView.Names.EnvHash);
                Assert.Equal("/ #[V] _ #", s.ParagraphText(0));
                Assert.Empty(s.Field.LanguageDiagnostics);
                Assert.False(Enabled(PlaygroundView.Names.EnvHash));         // both edges have one now
                int steps = s.View.Undo.DoneSteps;
                s.Click(PlaygroundView.Names.EnvOptional);
                Assert.Equal("/ #()[V] _ #", s.ParagraphText(0));
                Assert.Equal(new TextPosition(0, 4), s.Editor.Caret);        // between the parentheses
                Assert.Equal(steps + 1, s.View.Undo.DoneSteps);
                Assert.True(s.View.UndoLast());
                Assert.Equal("/ #[V] _ #", s.ParagraphText(0));
            }
        });

        [Fact]
        public void An_empty_box_can_take_only_the_slash_and_after_it_the_bar() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Environment))
            {
                Replace(s, "x");
                s.Press(Key.Back);
                bool Enabled(string name) => s.Find<Button>(name).IsEnabled;
                Assert.True(Enabled(PlaygroundView.Names.EnvSlash));
                Assert.False(Enabled(PlaygroundView.Names.EnvBar));
                s.Click(PlaygroundView.Names.EnvSlash);
                Assert.Equal("/", s.ParagraphText(0));
                Assert.False(Enabled(PlaygroundView.Names.EnvSlash));
                Assert.True(Enabled(PlaygroundView.Names.EnvBar));
                s.Click(PlaygroundView.Names.EnvBar);
                Assert.Equal("/_", s.ParagraphText(0));
            }
        });

        [Fact]
        public void A_right_click_on_a_problem_tells_the_host_the_diagnostic_and_its_fix_and_applying_it_is_one_undo_step() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Environment))
            {
                Replace(s, "/ [v] _");
                s.RightClick(s.PointAt(0, 3));
                var info = s.View.LastContextInfo!;
                var d = Assert.Single(info.Diagnostics);
                Assert.Equal(SampleEnvironmentService.Codes.UnknownClass, d.Code);
                Assert.Contains("Change to V", s.View.LastContextMenuItems);
                Assert.Contains(s.View.LastContextMenuItems, c => c.StartsWith("Error: ") && c.Contains("'v'"));
                Assert.Contains("Insert word boundary", s.View.LastContextMenuItems);
                s.View.CloseContextMenu();
                int steps = s.View.Undo.DoneSteps;
                Assert.True(s.Field.ApplyDiagnosticFix(d));
                Assert.Equal("/ [V] _", s.ParagraphText(0));
                Assert.Equal(steps + 1, s.View.Undo.DoneSteps);
                Headless.Pump();
                Assert.Empty(s.Field.LanguageDiagnostics);
                Assert.True(s.View.UndoLast());
                Assert.Equal("/ [v] _", s.ParagraphText(0));
            }
        });

        [Fact]
        public void A_right_click_away_from_the_problem_offers_no_fix() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Environment))
            {
                Replace(s, "/ [v] _");
                s.RightClick(s.PointAt(0, 0));
                Assert.Empty(s.View.LastContextInfo!.Diagnostics);
                Assert.DoesNotContain("Change to V", s.View.LastContextMenuItems);
                s.View.CloseContextMenu();
            }
        });

        [Fact]
        public void Dropping_a_class_makes_the_service_say_it_changed_and_every_environment_that_names_it_is_marked_until_it_is_restored() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Environment))
            {
                Assert.Empty(s.Field.LanguageDiagnostics);
                s.Click(PlaygroundView.Names.EnvDropClass);
                Headless.Pump();
                Assert.Equal(SampleEnvironmentService.Codes.UnknownClass, Assert.Single(s.Field.LanguageDiagnostics).Code);
                Assert.Contains("natural classes: [C] [+son] [+lab, +vd]", s.Log);
                s.Click(PlaygroundView.Names.EnvRestoreClass);
                Headless.Pump();
                Assert.Empty(s.Field.LanguageDiagnostics);
            }
        });

        [Fact]
        public void Hover_over_a_class_gives_its_name_and_over_a_problem_its_message() => Headless.Run(async () =>
        {
            using (var s = new Session(Scenarios.Environment))
            {
                Assert.Equal("Natural class V: Vowels.", await s.Field.GetLanguageHoverAsync(new TextPosition(0, 3)));
                Replace(s, "/ [X] _");
                var text = await s.Field.GetLanguageHoverAsync(new TextPosition(0, 3));
                Assert.Contains("'X'", text);
            }
        });

        [Fact]
        public void Other_scenarios_have_no_language_service_and_disabled_Insert_buttons() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Empty))
            {
                Assert.Null(s.Field.LanguageService);
                Assert.Null(s.Field.TokenStyles);
                Assert.False(s.Field.SingleLine);
                Assert.False(s.Find<Button>(PlaygroundView.Names.EnvSlash).IsEnabled);
                s.Pick(Scenarios.Environment);
                Assert.NotNull(s.Field.LanguageService);
                s.Pick(Scenarios.Empty);
                Assert.Null(s.Field.LanguageService);
                Assert.False(s.Field.SingleLine);
            }
        });

        [Fact]
        public void Spelling_and_the_language_service_work_in_one_field_without_disturbing_each_other() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Environment))
            {
                s.Field.SpellChecking = new SIL.TextEditing.Tests.FakeSpellHost("a");
                Replace(s, "/ teh _ [X]");
                Headless.Pump();
                while (!s.Field.IsSpellingComplete) s.Field.CheckSpellingStep();
                Assert.Single(s.Field.LanguageDiagnostics);
                var layout = s.Field.ParagraphLayoutFor(0);
                Assert.NotEmpty(layout.SpellingMarksDrawn);
                Assert.NotEmpty(layout.DiagnosticMarksDrawn);
                Assert.Equal("/ teh _ [X]", s.ParagraphText(0));
            }
        });
    }
}
