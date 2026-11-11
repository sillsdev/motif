using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media;
using SIL.TextEditing.Tests;
using Xunit;
using static SIL.TextEditing.Tests.TestDocs;

namespace SIL.TextEditing.Avalonia.Tests
{
    /// <summary>ADR 0035: a field shown and edited as its source form, and the return to rich text through the form's reader and checker as one undo step.</summary>
    public class SourceModeTests
    {
        /// <summary>A source form small enough to read at a glance: a line is a paragraph, "# " makes it a heading, "!" is an error and "?" a warning.</summary>
        sealed class LineForm : ISourceForm
        {
            public bool CannotWrite;
            public int Reads;

            public string Name => "Lines";

            public string? Write(IReadOnlyList<Paragraph> paragraphs, bool singleParagraph) =>
                CannotWrite ? null : string.Join("\n", paragraphs.Select(p => (!singleParagraph && p.Style == "Heading" ? "# " : "") + p.Text));

            public SourceReading Read(string source, bool singleParagraph)
            {
                Reads++;
                var paragraphs = new List<Paragraph>();
                var diagnostics = new List<LanguageDiagnostic>();
                var lines = source.Split('\n');
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    var p = new Paragraph { DefaultWs = "en" };
                    if (line.StartsWith("# ", StringComparison.Ordinal)) { p.Style = "Heading"; line = line.Substring(2); }
                    if (line.Length > 0) p.Runs.Add(new Run { Ws = "en", Text = line });
                    paragraphs.Add(p);
                    Mark(lines[i], i, '!', DiagnosticSeverity.Error, diagnostics);
                    Mark(lines[i], i, '?', DiagnosticSeverity.Warning, diagnostics);
                }
                return new SourceReading(paragraphs, diagnostics);
            }

            static void Mark(string line, int index, char c, DiagnosticSeverity severity, List<LanguageDiagnostic> into)
            {
                for (int at = line.IndexOf(c); at >= 0; at = line.IndexOf(c, at + 1))
                    into.Add(new LanguageDiagnostic(new LanguageRange(new LanguagePosition(index, at), new LanguagePosition(index, at + 1)), severity, "marked " + c));
            }

            public bool AreEquivalent(Paragraph original, Paragraph read) => original.Text == read.Text && original.Style == read.Style;
        }

        sealed class Rig
        {
            public readonly Window Window;
            public readonly RichTextField Field;
            public readonly LineForm Form = new LineForm();
            public readonly SimpleUndoStack Undo = new SimpleUndoStack();
            public readonly List<SourceModeChangedEventArgs> Changes = new List<SourceModeChangedEventArgs>();
            public readonly List<EditDelta> Deltas = new List<EditDelta>();

            public Rig(params string[] lines)
            {
                (Window, Field) = Fixtures.InWindow(Doc(lines.Select(l => Para("en", "en", l)).ToArray()));
                Field.SourceForm = Form;
                Undo.Attach(Field.Editor);
                Field.UndoRequested += () => Undo.Undo();
                Field.Editor.Committed += Deltas.Add;
                Field.SourceModeChanged += (o, e) => Changes.Add(e);
                Headless.Pump();
            }

            public RichTextField View => Field.SourceView!;

            public string Source => View.Editor.Document.PlainText();

            public string[] Texts => Field.Editor.Document.Paragraphs.Select(p => p.Text).ToArray();

            /// <summary>Puts the caret at the end of a source line and types there, as a person would.</summary>
            public void TypeAtEndOf(int line, string text)
            {
                View.Editor.SetCaret(new TextPosition(line, View.Editor.Document.Paragraphs[line].Length));
                View.Type(text);
                Headless.Pump();
            }
        }

        [Fact]
        public void Entering_shows_the_source_in_a_second_field_and_says_so() => Headless.Run(() =>
        {
            var r = new Rig("one", "two");
            Assert.True(r.Field.EnterSourceMode());
            Headless.Pump();
            Assert.True(r.Field.IsSourceMode);
            Assert.Equal("one\ntwo", r.Source);
            Assert.True(r.View.IsKeyboardFocusWithin);
            Assert.Contains(r.View, r.Field.GetLogicalDescendants().OfType<RichTextField>());
            var e = Assert.Single(r.Changes);
            Assert.True(e.IsSourceMode);
            Assert.False(e.Refused);
            Assert.Equal("Editing as Lines", e.Announcement);
            Assert.False(r.Field.EnterSourceMode());   // already there
            r.Window.Close();
        });

        [Fact]
        public void A_round_trip_with_no_edit_leaves_the_field_alone_and_adds_no_undo_step() => Headless.Run(() =>
        {
            var r = new Rig("one", "two");
            var before = r.Field.Editor.Document.Paragraphs.ToList();
            r.Field.EnterSourceMode();
            Assert.True(r.Field.LeaveSourceMode());
            Assert.False(r.Field.IsSourceMode);
            Assert.Null(r.Field.SourceView);
            Assert.Equal(before, r.Field.Editor.Document.Paragraphs);
            Assert.Empty(r.Deltas);
            Assert.Equal(0, r.Undo.DoneSteps);
            Assert.Equal("Back to rich text", r.Changes.Last().Announcement);
            r.Window.Close();
        });

        [Fact]
        public void An_edit_that_is_put_back_before_the_return_reaches_nothing() => Headless.Run(() =>
        {
            var r = new Rig("one", "two");
            r.Field.EnterSourceMode();
            r.TypeAtEndOf(1, "x");
            r.View.Editor.Backspace();
            Assert.True(r.Field.LeaveSourceMode());
            Assert.Empty(r.Deltas);
            r.Window.Close();
        });

        [Fact]
        public void Several_edits_in_source_mode_reach_the_host_as_one_undo_step_that_one_undo_restores() => Headless.Run(() =>
        {
            var r = new Rig("one", "two", "three");
            r.Field.EnterSourceMode();
            r.TypeAtEndOf(1, " more");
            r.View.Editor.SetCaret(new TextPosition(0, 0));
            r.View.Type("# ");
            r.TypeAtEndOf(2, " end");
            Assert.Equal(new[] { "one", "two", "three" }, r.Texts);   // nothing reaches the field before the return
            Assert.Empty(r.Deltas);
            Assert.True(r.Field.LeaveSourceMode());
            Assert.Equal(new[] { "one", "two more", "three end" }, r.Texts);
            Assert.Equal("Heading", r.Field.Editor.Document.Paragraphs[0].Style);
            Assert.Equal(new[] { "key0", "key1", "key2" }, r.Field.Editor.Document.Paragraphs.Select(p => p.Key));
            var d = Assert.Single(r.Deltas);
            Assert.Equal("Edit as Lines", d.Label);
            Assert.Equal(1, r.Undo.DoneSteps);
            r.Field.Focus();
            r.Field.Press(Key.Z, KeyModifiers.Control);
            Assert.Equal(new[] { "one", "two", "three" }, r.Texts);
            Assert.NotEqual("Heading", r.Field.Editor.Document.Paragraphs[0].Style);
            r.Window.Close();
        });

        [Fact]
        public void Undo_in_the_source_view_undoes_source_typing_not_the_host_s_text() => Headless.Run(() =>
        {
            var r = new Rig("one");
            r.Field.Editor.SetCaret(new TextPosition(0, 3));
            r.Field.Type("!");   // a host step before source mode
            r.Field.Editor.Backspace();
            int hostSteps = r.Undo.DoneSteps;
            r.Field.EnterSourceMode();
            r.TypeAtEndOf(0, " two");
            r.View.Press(Key.Z, KeyModifiers.Control);
            Headless.Pump();
            Assert.Equal("one", r.Source);
            Assert.Equal(hostSteps, r.Undo.DoneSteps);
            r.View.Press(Key.Y, KeyModifiers.Control);
            Assert.Equal("one two", r.Source);
            r.Window.Close();
        });

        [Fact]
        public void Errors_keep_source_mode_and_say_why_and_throw_away_restores_the_rich_text() => Headless.Run(() =>
        {
            var r = new Rig("one", "two");
            r.Field.EnterSourceMode();
            r.TypeAtEndOf(1, " oops!!");
            Assert.False(r.Field.LeaveSourceMode());
            Assert.True(r.Field.IsSourceMode);
            Assert.Equal("one\ntwo oops!!", r.Source);
            var refused = r.Changes.Last();
            Assert.True(refused.IsSourceMode);
            Assert.True(refused.Refused);
            Assert.Equal("The Lines has 2 errors. Fix what is marked, or throw away your Lines changes.", refused.Announcement);
            Assert.Empty(r.Deltas);
            r.Field.DiscardSourceChanges();
            Assert.False(r.Field.IsSourceMode);
            Assert.Equal(new[] { "one", "two" }, r.Texts);
            Assert.Empty(r.Deltas);
            Assert.Contains("thrown away", r.Changes.Last().Announcement);
            r.Window.Close();
        });

        [Fact]
        public void Fixing_the_errors_lets_the_return_through() => Headless.Run(() =>
        {
            var r = new Rig("one");
            r.Field.EnterSourceMode();
            r.TypeAtEndOf(0, "!");
            Assert.False(r.Field.LeaveSourceMode());
            r.View.Editor.Backspace();
            r.TypeAtEndOf(0, "s");
            Assert.True(r.Field.LeaveSourceMode());
            Assert.Equal(new[] { "ones" }, r.Texts);
            r.Window.Close();
        });

        [Fact]
        public void Warnings_never_block() => Headless.Run(() =>
        {
            var r = new Rig("one");
            r.Field.EnterSourceMode();
            r.TypeAtEndOf(0, "?");
            Assert.True(r.Field.LeaveSourceMode());
            Assert.Equal(new[] { "one?" }, r.Texts);
            r.Window.Close();
        });

        [Fact]
        public void The_source_view_marks_the_form_s_findings_as_a_language_service() => Headless.Run(() =>
        {
            var r = new Rig("one", "two");
            var posts = new List<Action>();
            r.Field.LanguageIdlePost = posts.Add;
            r.Field.EnterSourceMode();
            r.TypeAtEndOf(1, "!");
            for (int guard = 0; guard < 50 && posts.Count > 0; guard++) { var next = posts[0]; posts.RemoveAt(0); next(); }
            Headless.Pump();
            var d = Assert.Single(r.View.LanguageDiagnostics);
            Assert.Equal(DiagnosticSeverity.Error, d.Severity);
            Assert.Equal(new LanguagePosition(1, 3), d.Range.Start);
            Assert.Single(r.View.ParagraphLayoutFor(1).DiagnosticMarksDrawn);
            r.Window.Close();
        });

        [Fact]
        public void Ctrl_Shift_M_enters_and_leaves_and_the_field_wants_the_key_only_when_it_has_a_form() => Headless.Run(() =>
        {
            var r = new Rig("one");
            Assert.True(r.Field.WantsKey(Key.M, KeyModifiers.Control | KeyModifiers.Shift));
            r.Field.Focus();
            r.Field.Press(Key.M, KeyModifiers.Control | KeyModifiers.Shift);
            Assert.True(r.Field.IsSourceMode);
            Headless.Pump();
            r.TypeAtEndOf(0, " two");
            r.View.Press(Key.M, KeyModifiers.Control | KeyModifiers.Shift);
            Assert.False(r.Field.IsSourceMode);
            Assert.Equal(new[] { "one two" }, r.Texts);
            r.Field.SourceForm = null;
            Assert.False(r.Field.WantsKey(Key.M, KeyModifiers.Control | KeyModifiers.Shift));
            r.Field.Press(Key.M, KeyModifiers.Control | KeyModifiers.Shift);
            Assert.False(r.Field.IsSourceMode);
            r.Window.Close();
        });

        [Fact]
        public void Typing_in_the_source_view_never_edits_the_rich_text_through_the_field() => Headless.Run(() =>
        {
            var r = new Rig("one");
            r.Field.EnterSourceMode();
            Headless.Pump();
            r.TypeAtEndOf(0, "x");
            r.View.Press(Key.Back);
            r.View.Press(Key.Enter);
            Assert.Equal(new[] { "one" }, r.Texts);
            Assert.Empty(r.Deltas);
            Assert.Equal("one\n", r.Source);
            r.Window.Close();
        });

        [Fact]
        public void A_read_only_field_a_field_with_locked_text_and_text_the_form_cannot_write_are_not_offered() => Headless.Run(() =>
        {
            var r = new Rig("one");
            r.Field.Editor.IsReadOnly = true;
            Assert.False(r.Field.EnterSourceMode());
            r.Field.Editor.IsReadOnly = false;
            r.Form.CannotWrite = true;
            Assert.False(r.Field.EnterSourceMode());
            r.Form.CannotWrite = false;
            r.Field.Editor.Document.Paragraphs[0].Runs[0].Props.SetInt(TextPropertyNumbers.Editable, TextPropertyNumbers.EditableNo);
            Assert.False(r.Field.EnterSourceMode());
            Assert.Empty(r.Changes);
            r.Window.Close();
        });

        [Fact]
        public void A_single_paragraph_field_keeps_its_style_and_reads_its_source_as_one_paragraph() => Headless.Run(() =>
        {
            var (window, field) = Fixtures.InWindow(OneParagraph("one"));
            field.Editor.SingleParagraph = true;
            field.Editor.Document.Paragraphs[0].Style = "Heading";
            field.SourceForm = new LineForm();
            field.EnterSourceMode();
            Assert.Equal("one", field.SourceView!.Editor.Document.PlainText());
            field.SourceView.Editor.SetCaret(new TextPosition(0, 3));
            field.SourceView.Type(" two");
            Assert.True(field.LeaveSourceMode());
            var p = Assert.Single(field.Editor.Document.Paragraphs);
            Assert.Equal("one two", p.Text);
            Assert.Equal("Heading", p.Style);
            window.Close();
        });

        [Fact]
        public void An_input_method_composes_in_the_source_view_and_never_in_the_rich_text() => Headless.Run(() =>
        {
            var r = new Rig("one");
            r.Field.EnterSourceMode();
            Headless.Pump();
            r.View.Editor.SetCaret(new TextPosition(0, 3));
            var ime = new ScriptedInputMethod(r.View);   // the request bubbles through the field, which must leave the view's client in place
            ime.Preedit("にほ", 2);
            Assert.Equal("にほ", r.View.Preedit);
            Assert.Null(r.Field.Preedit);
            ime.Commit("日本");
            Assert.Equal("one日本", r.Source);
            Assert.Equal(new[] { "one" }, r.Texts);
            Assert.Empty(r.Deltas);
            Assert.True(r.Field.LeaveSourceMode());
            Assert.Equal(new[] { "one日本" }, r.Texts);
            r.Window.Close();
        });

        [Fact]
        public void A_field_made_read_only_in_source_mode_keeps_the_source_instead_of_losing_it() => Headless.Run(() =>
        {
            var r = new Rig("one");
            r.Field.EnterSourceMode();
            r.TypeAtEndOf(0, " two");
            r.Field.Editor.IsReadOnly = true;
            Assert.False(r.Field.LeaveSourceMode());
            Assert.True(r.Field.IsSourceMode);
            Assert.Equal("one two", r.Source);
            Assert.True(r.Changes.Last().Refused);
            Assert.Empty(r.Deltas);
            r.Field.Editor.IsReadOnly = false;
            Assert.True(r.Field.LeaveSourceMode());
            Assert.Equal(new[] { "one two" }, r.Texts);
            r.Window.Close();
        });

        static IInputElement? Focused(Window window) => window.FocusManager?.GetFocusedElement();

        static void Tab(Window window, KeyModifiers modifiers = KeyModifiers.None)
        {
            var target = (Focused(window) as InputElement) ?? window;
            target.Press(Key.Tab, modifiers);
            Headless.Pump();
        }

        [Fact]
        public void Tab_and_Shift_Tab_reach_both_strip_buttons_and_leave_the_field_both_ways_with_no_trap() => Headless.Run(() =>
        {
            var before = new TextBox { Name = "before" };
            var after = new TextBox { Name = "after" };
            var field = new RichTextField(Doc(Para("en", "en", "one"))) { SourceForm = new LineForm() };
            var panel = new StackPanel();
            panel.Children.Add(before);
            panel.Children.Add(field);
            panel.Children.Add(after);
            var window = new Window { Width = 520, Height = 400, Content = panel };
            window.Show();
            Headless.Pump();
            Assert.True(field.EnterSourceMode());
            Headless.Pump();
            var view = field.SourceView!;
            var buttons = field.GetLogicalDescendants().OfType<Button>().ToList();
            Assert.Equal(2, buttons.Count);
            Assert.False(field.IsTabStop);

            // Backwards from the view: the two strip buttons, then what comes before the field, never the field itself.
            view.Focus();
            Headless.Pump();
            var seen = new List<IInputElement?>();
            for (int i = 0; i < 3; i++) { Tab(window, KeyModifiers.Shift); seen.Add(Focused(window)); }
            Assert.Same(buttons[1], seen[0]);
            Assert.Same(buttons[0], seen[1]);
            Assert.Same(before, seen[2]);

            // Forwards from before: the strip buttons, the view, then what comes after the field.
            seen.Clear();
            for (int i = 0; i < 4; i++) { Tab(window); seen.Add(Focused(window)); }
            Assert.Same(buttons[0], seen[0]);
            Assert.Same(buttons[1], seen[1]);
            Assert.Same(view, seen[2]);
            Assert.Same(after, seen[3]);

            Assert.True(field.LeaveSourceMode());
            Assert.True(field.IsTabStop);
            Assert.True(field.Focusable);
            window.Close();
        });

        [Fact]
        public void The_view_s_context_menu_reaches_the_host_about_the_view_with_its_findings_and_their_fix() => Headless.Run(() =>
        {
            var r = new Rig("one");
            var form = new FixingForm();
            r.Field.SourceForm = form;
            var posts = new List<Action>();
            r.Field.LanguageIdlePost = posts.Add;
            ContextMenuRequestedEventArgs? seen = null;
            object? sender = null;
            r.Field.ContextMenuRequested += (o, e) => { sender = o; seen = e; };
            r.Field.EnterSourceMode();
            r.TypeAtEndOf(0, " bad");
            for (int guard = 0; guard < 50 && posts.Count > 0; guard++) { var next = posts[0]; posts.RemoveAt(0); next(); }
            Headless.Pump();
            r.View.Editor.SetCaret(new TextPosition(0, 5));
            r.View.Press(Key.F10, KeyModifiers.Shift);
            Headless.Pump();
            Assert.NotNull(seen);
            Assert.Same(r.Field, sender);
            Assert.Same(r.View, seen!.Field);
            var d = Assert.Single(seen.Info.Diagnostics);
            Assert.True(seen.Field!.ApplyDiagnosticFix(d));
            Assert.Equal("one good", r.Source);
            Assert.Equal(new[] { "one" }, r.Texts);
            r.Window.Close();
        });

        [Fact]
        public void A_host_that_only_sets_a_ContextMenu_on_the_field_gets_it_in_source_mode_too() => Headless.Run(() =>
        {
            // Headless has no popup to open, so the test checks what opens a field's ContextMenu: the request reaching the field unhandled.
            var r = new Rig("one");
            var reached = new List<bool>();
            r.Field.AddHandler(Control.ContextRequestedEvent, (o, e) => reached.Add(e.Handled), global::Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);
            r.Field.EnterSourceMode();
            Headless.Pump();
            r.View.Editor.SetCaret(new TextPosition(0, 2));
            r.View.Press(Key.F10, KeyModifiers.Shift);
            Headless.Pump();
            Assert.Equal(new[] { false }, reached);
            r.Window.Close();
        });

        [Fact]
        public void A_host_that_listens_and_sets_a_ContextMenu_gets_the_event_in_source_mode_and_the_menu_stays_shut() => Headless.Run(() =>
        {
            var r = new Rig("one");
            var reached = new List<bool>();
            r.Field.AddHandler(Control.ContextRequestedEvent, (o, e) => reached.Add(e.Handled), global::Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);
            ContextMenuRequestedEventArgs? seen = null;
            r.Field.ContextMenuRequested += (o, e) => seen = e;
            r.Field.EnterSourceMode();
            Headless.Pump();
            r.View.Press(Key.F10, KeyModifiers.Shift);
            Headless.Pump();
            Assert.Same(r.View, seen?.Field);
            Assert.Equal(new[] { true }, reached);   // handled, so a ContextMenu on the field stays shut
            r.Window.Close();
        });

        [Fact]
        public void A_host_commit_that_fails_after_the_text_changed_says_the_commit_failed_and_one_undo_restores() => Headless.Run(() =>
        {
            var r = new Rig("one", "two");
            bool fail = true;
            r.Field.Editor.Committed += d => { if (fail) { fail = false; throw new InvalidOperationException("the host broke"); } };
            r.Field.EnterSourceMode();
            r.TypeAtEndOf(1, " more");
            Assert.True(r.Field.LeaveSourceMode());
            Assert.False(r.Field.IsSourceMode);
            Assert.Equal(new[] { "one", "two more" }, r.Texts);
            var said = r.Changes.Last();
            Assert.False(said.IsSourceMode);
            Assert.False(said.Refused);
            Assert.Contains("the host broke", said.Announcement);
            Assert.DoesNotContain("kept", said.Announcement);
            Assert.Equal(1, r.Undo.DoneSteps);
            r.Undo.Undo();
            Assert.Equal(new[] { "one", "two" }, r.Texts);
            r.Field.Editor.SetCaret(new TextPosition(0, 3));
            r.Field.Editor.TypeText("!");
            Assert.Equal(1, r.Undo.DoneSteps);   // the next edit is a step of its own, not part of the failed one
            r.Window.Close();
        });

        [Fact]
        public void A_host_that_makes_the_field_read_only_in_source_mode_stops_the_source_being_edited_too() => Headless.Run(() =>
        {
            var r = new Rig("one");
            r.Field.EnterSourceMode();
            r.Field.IsReadOnly = true;
            Headless.Pump();
            Assert.True(r.View.IsReadOnly);
            r.View.Editor.SetCaret(new TextPosition(0, 3));
            r.View.Editor.TypeText("x");
            Assert.Equal("one", r.Source);
            r.Field.IsReadOnly = false;
            Headless.Pump();
            Assert.False(r.View.IsReadOnly);
            r.TypeAtEndOf(0, " two");
            Assert.True(r.Field.LeaveSourceMode());
            Assert.Equal(new[] { "one two" }, r.Texts);
            r.Window.Close();
        });

        [Fact]
        public void A_spelling_choice_or_a_language_fix_made_on_the_field_in_source_mode_is_refused() => Headless.Run(() =>
        {
            var (window, field) = Fixtures.InWindow(Doc(Para("en", "en", "two bad wrold")));
            var spelling = new SIL.TextEditing.Tests.FakeSpellHost("two", "bad");
            spelling.Checkers["en"].Suggestions = new List<string> { "world" };
            field.SpellIdlePost = a => { };
            field.SpellChecking = spelling;
            var posts = new List<Action>();
            field.LanguageIdlePost = posts.Add;
            field.LanguageService = new SIL.TextEditing.Tests.FakeLanguageService { Analysis = SIL.TextEditing.Tests.FakeLanguageService.Words("word", fixTo: "good") };
            for (int guard = 0; guard < 50 && posts.Count > 0; guard++) { var next = posts[0]; posts.RemoveAt(0); next(); }
            Headless.Pump();
            var seen = new List<ContextMenuRequestedEventArgs>();
            field.ContextMenuRequested += (o, e) => seen.Add(e);
            field.Editor.SetCaret(new TextPosition(0, 10));
            field.Focus();
            field.Press(Key.Apps);
            var word = Assert.Single(seen).Info.Spelling!;
            var fix = Assert.Single(field.LanguageDiagnostics);
            field.SourceForm = new LineForm();
            Assert.True(field.EnterSourceMode());

            Assert.False(field.ApplySpellingSuggestion(word, "world"));
            Assert.False(field.ApplySpellingSuggestion(word, new[] { new Run { Ws = "en", Text = "world" } }));
            Assert.False(field.ApplyDiagnosticFix(fix));
            Assert.Equal("two bad wrold", field.Editor.Document.Paragraphs[0].Text);
            window.Close();
        });

        [Fact]
        public void Text_input_raised_on_the_field_in_source_mode_goes_to_the_view() => Headless.Run(() =>
        {
            var r = new Rig("one");
            r.Field.EnterSourceMode();
            r.View.Editor.SetCaret(new TextPosition(0, 3));
            r.Field.Type("!x");
            Assert.Equal("one!x", r.Source);
            Assert.Equal(new[] { "one" }, r.Texts);
            Assert.Empty(r.Deltas);
            r.Window.Close();
        });

        [Fact]
        public void A_form_that_throws_on_reading_keeps_the_field_in_source_mode_with_the_source() => Headless.Run(() =>
        {
            var r = new Rig("one");
            r.Field.SourceForm = new ThrowingForm();
            r.Field.EnterSourceMode();
            r.TypeAtEndOf(0, " two");
            Assert.False(r.Field.LeaveSourceMode());
            Assert.True(r.Field.IsSourceMode);
            Assert.Equal("one two", r.Source);
            Assert.Contains("could not be read", r.Changes.Last().Announcement);
            r.Field.DiscardSourceChanges();
            Assert.Equal(new[] { "one" }, r.Texts);
            r.Window.Close();
        });

        [Fact]
        public void The_rich_editor_refuses_host_commands_while_the_field_is_in_source_mode() => Headless.Run(() =>
        {
            var r = new Rig("one");
            r.Field.EnterSourceMode();
            Assert.False(r.Field.Editor.CanEdit);
            r.Field.Editor.SetCaret(new TextPosition(0, 3));
            r.Field.Editor.TypeText("x");
            r.Field.Editor.PasteText("y");
            Assert.Equal(new[] { "one" }, r.Texts);
            r.TypeAtEndOf(0, " two");
            Assert.True(r.Field.LeaveSourceMode());
            Assert.Equal(new[] { "one two" }, r.Texts);
            Assert.True(r.Field.Editor.CanEdit);
            r.Window.Close();
        });

        [Fact]
        public void The_mute_wash_uses_the_field_s_background_or_else_the_theme_s_page() => Headless.Run(() =>
        {
            var r = new Rig("one");
            r.Field.Background = new SolidColorBrush(Color.FromRgb(10, 20, 30));
            Assert.Equal(Color.FromRgb(10, 20, 30), r.Field.MuteColour(false));
            r.Field.Background = new LinearGradientBrush();
            var light = r.Field.MuteColour(false);
            Assert.Equal(255, light.A);
            r.Field.Background = Brushes.Transparent;
            Assert.Equal(light, r.Field.MuteColour(false));
            r.Window.Close();
        });

        /// <summary>A form whose reader marks "bad" as a warning with the fix "good".</summary>
        sealed class FixingForm : ISourceForm
        {
            public string Name => "Lines";
            public string? Write(IReadOnlyList<Paragraph> paragraphs, bool singleParagraph) => string.Join("\n", paragraphs.Select(p => p.Text));
            public SourceReading Read(string source, bool singleParagraph)
            {
                var lines = source.Split('\n');
                var diagnostics = new List<LanguageDiagnostic>();
                for (int i = 0; i < lines.Length; i++)
                {
                    int at = lines[i].IndexOf("bad", StringComparison.Ordinal);
                    if (at < 0) continue;
                    var range = new LanguageRange(new LanguagePosition(i, at), new LanguagePosition(i, at + 3));
                    diagnostics.Add(new LanguageDiagnostic(range, DiagnosticSeverity.Warning, "bad word", fix: new LanguageFix("Use good", new[] { new LanguageTextEdit(range, "good") })));
                }
                return new SourceReading(lines.Select(l => Para("en", "en", l)).ToList(), diagnostics);
            }
            public bool AreEquivalent(Paragraph original, Paragraph read) => original.Text == read.Text;
        }

        sealed class ThrowingForm : ISourceForm
        {
            public string Name => "Lines";
            public string? Write(IReadOnlyList<Paragraph> paragraphs, bool singleParagraph) => string.Join("\n", paragraphs.Select(p => p.Text));
            public SourceReading Read(string source, bool singleParagraph) => throw new InvalidOperationException("broken reader");
            public bool AreEquivalent(Paragraph original, Paragraph read) => original.Text == read.Text;
        }

        [Fact]
        public void A_muted_token_style_draws_with_no_fill_and_its_strength_is_kept_between_0_and_1()
        {
            Assert.True(new TokenStyle(null, null, 0.55).Draws);
            Assert.False(new TokenStyle(null).Draws);
            Assert.Equal(1, new TokenStyle(null, null, 3).Mute);
            Assert.Equal(0, new TokenStyle(null, null, -1).Mute);
        }

        [Fact]
        public void Changing_the_form_in_source_mode_throws_the_source_away() => Headless.Run(() =>
        {
            var r = new Rig("one");
            r.Field.EnterSourceMode();
            r.TypeAtEndOf(0, " two");
            r.Field.SourceForm = new LineForm();
            Assert.False(r.Field.IsSourceMode);
            Assert.Equal(new[] { "one" }, r.Texts);
            r.Window.Close();
        });
    }
}
