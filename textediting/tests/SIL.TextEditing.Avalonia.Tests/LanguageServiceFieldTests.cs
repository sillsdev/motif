using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using SIL.TextEditing.Tests;
using Xunit;
using static SIL.TextEditing.Tests.TestDocs;

namespace SIL.TextEditing.Avalonia.Tests
{
    /// <summary>ADR 0030: the field's side of the language-service hook: tokens drawn as chips, diagnostics drawn as squiggles and never written into the
    /// document, results applied only to the text they were asked about, fixes, and the context menu's view of them.</summary>
    public class LanguageServiceFieldTests
    {
        // ------------------------------------------------ asking when idle

        [Fact]
        public void Nothing_is_asked_until_the_field_is_idle_and_then_the_text_is_asked_once() => Headless.Run(() =>
        {
            var r = new LanguageRig("one bad two");
            Assert.Empty(r.Service.AnalyzedTexts);
            Assert.Single(r.Posts);
            Assert.Empty(r.Field.LanguageTokens);
            Assert.False(r.Field.IsLanguageAnalysisCurrent);
            r.Idle();
            Assert.Equal(new[] { "one bad two" }, r.Service.AnalyzedTexts);
            Assert.True(r.Field.IsLanguageAnalysisCurrent);
            Assert.Equal(new[] { "word", "unknown", "word" }, r.Field.LanguageTokens.Select(t => t.Kind).ToArray());
            var d = Assert.Single(r.Field.LanguageDiagnostics);
            Assert.Equal(new LanguageRange(4, 7), d.Range);
            r.Idle();
            Assert.Single(r.Service.AnalyzedTexts);   // nothing changed: nothing asked again
            r.Close();
        });

        [Fact]
        public void A_field_that_is_not_in_the_visual_tree_asks_nothing_until_it_is() => Headless.Run(() =>
        {
            var field = Fixtures.Field(OneParagraph("a bad b"));
            var service = new FakeLanguageService { Analysis = FakeLanguageService.Words("word") };
            var posts = new List<Action>();
            field.LanguageIdlePost = posts.Add;
            field.LanguageService = service;
            Assert.Empty(posts);
            var window = new Window { Width = 400, Height = 200, Content = field };
            window.Show();
            Headless.Pump();
            Assert.Single(posts);
            posts[0]();
            Assert.Single(service.AnalyzedTexts);
            window.Close();
        });

        [Fact]
        public void The_field_listens_to_the_service_while_it_is_in_the_visual_tree_only() => Headless.Run(() =>
        {
            var r = new LanguageRig("a b");
            Assert.Equal(1, r.Service.Subscribers);
            r.Window.Content = null;
            Headless.Pump();
            Assert.Equal(0, r.Service.Subscribers);
            r.Close();
        });

        [Fact]
        public void The_service_saying_it_changed_has_the_text_asked_again_and_the_old_answer_stays_until_the_new_one_comes() => Headless.Run(() =>
        {
            var r = new LanguageRig("a bad b");
            r.Idle();
            Assert.Single(r.Field.LanguageDiagnostics);
            r.Service.Analysis = t => LanguageAnalysis.Empty;
            r.Service.RaiseChanged();
            Headless.Pump();
            Assert.Single(r.Field.LanguageDiagnostics);   // still the old one
            Assert.False(r.Field.IsLanguageAnalysisCurrent);
            r.Idle();
            Assert.Equal(2, r.Service.AnalyzedTexts.Count);
            Assert.Empty(r.Field.LanguageDiagnostics);
            Assert.True(r.Field.IsLanguageAnalysisCurrent);
            r.Close();
        });

        [Fact]
        public void A_field_of_several_paragraphs_has_no_language_service_at_work() => Headless.Run(() =>
        {
            var (window, field) = Fixtures.InWindow(Doc(Para("en", "en", "a bad"), Para("en", "en", "b")));
            var service = new FakeLanguageService { Analysis = FakeLanguageService.Words("word") };
            var posts = new List<Action>();
            field.LanguageIdlePost = posts.Add;
            field.LanguageService = service;
            Assert.Empty(posts);
            Assert.Empty(field.LanguageTokens);
            Assert.True(field.IsLanguageAnalysisCurrent);
            Assert.False(field.CanInsert("x"));
            window.Close();
        });

        // ------------------------------------------------ staleness

        [Fact]
        public void An_answer_for_text_that_has_changed_is_dropped_and_the_new_text_is_asked() => Headless.Run(() =>
        {
            var r = new LanguageRig("one two", new FakeLanguageService { Manual = true });
            r.Idle();
            var (askedText, source) = Assert.Single(r.Service.PendingAnalyses);
            Assert.Equal("one two", askedText);
            r.Field.Editor.SetCaret(new TextPosition(0, 7));
            r.Field.Editor.TypeText(" bad");
            Headless.Pump();
            Assert.True(r.Service.Tokens[0].IsCancellationRequested);   // the service is told it need not go on
            source.SetResult(new LanguageAnalysis(new[] { new LanguageToken(new LanguageRange(0, 3), "old") }, new LanguageDiagnostic[0]));
            Headless.Pump();
            Assert.Empty(r.Field.LanguageTokens);                       // dropped, not applied
            r.Idle();
            Assert.Equal(2, r.Service.PendingAnalyses.Count);
            Assert.Equal("one two bad", r.Service.PendingAnalyses[1].Text);
            r.Service.PendingAnalyses[1].Source.SetResult(FakeLanguageService.Words("word")("one two bad"));
            r.Idle();
            Assert.Equal(3, r.Field.LanguageTokens.Count);
            Assert.True(r.Field.IsLanguageAnalysisCurrent);
            r.Close();
        });

        [Fact]
        public void An_answer_that_arrives_late_is_applied_when_the_text_is_as_it_was() => Headless.Run(() =>
        {
            var r = new LanguageRig("one bad", new FakeLanguageService { Manual = true });
            r.Idle();
            Assert.False(r.Field.IsLanguageAnalysisCurrent);
            r.Service.PendingAnalyses[0].Source.SetResult(FakeLanguageService.Words("word")("one bad"));
            r.Idle();
            Assert.Single(r.Field.LanguageDiagnostics);
            Assert.True(r.Field.IsLanguageAnalysisCurrent);
            r.Close();
        });

        [Fact]
        public void Changing_the_service_drops_an_answer_that_was_out_for_the_old_one() => Headless.Run(() =>
        {
            var r = new LanguageRig("one bad", new FakeLanguageService { Manual = true });
            r.Idle();
            var other = new FakeLanguageService { Analysis = t => new LanguageAnalysis(new[] { new LanguageToken(new LanguageRange(0, 3), "other") }, new LanguageDiagnostic[0]) };
            r.Field.LanguageService = other;
            r.Service.PendingAnalyses[0].Source.SetResult(FakeLanguageService.Words("word")("one bad"));
            r.Idle();
            Assert.Equal(new[] { "other" }, r.Field.LanguageTokens.Select(t => t.Kind).ToArray());
            r.Close();
        });

        [Fact]
        public void Results_clear_of_an_edit_are_carried_to_where_the_text_moved_them_and_a_diagnostic_carried_loses_its_fix() => Headless.Run(() =>
        {
            var r = new LanguageRig("aa bad cc", new FakeLanguageService { Analysis = FakeLanguageService.Words("word", fixTo: "good") });
            r.Idle();
            Assert.NotNull(r.Field.LanguageDiagnostics[0].Fix);
            r.Service.Manual = true;
            r.Field.Editor.SetCaret(new TextPosition(0, 0));
            r.Field.Editor.TypeText("xyz ");
            Headless.Pump();
            Assert.False(r.Field.IsLanguageAnalysisCurrent);
            var tokens = r.Field.LanguageTokens;
            Assert.Equal(new[] { 4, 7, 11 }, tokens.Select(t => t.Range.Start.Character).ToArray());   // "xyz aa bad cc": the three words after the typed one
            var d = Assert.Single(r.Field.LanguageDiagnostics);
            Assert.Equal(new LanguageRange(7, 10), d.Range);
            Assert.Null(d.Fix);
            Assert.False(r.Field.ApplyDiagnosticFix(d));
            r.Close();
        });

        [Fact]
        public void A_result_the_edit_touched_is_dropped_until_the_new_answer() => Headless.Run(() =>
        {
            var r = new LanguageRig("aa bad cc");
            r.Idle();
            r.Service.Manual = true;
            r.Field.Editor.SetCaret(new TextPosition(0, 5));
            r.Field.Editor.TypeText("x");   // inside "bad"
            Headless.Pump();
            Assert.Equal(new[] { 0, 8 }, r.Field.LanguageTokens.Select(t => t.Range.Start.Character).ToArray());
            Assert.Empty(r.Field.LanguageDiagnostics);
            r.Close();
        });

        [Fact]
        public void A_paragraph_changed_behind_the_editors_back_loses_its_results_at_once() => Headless.Run(() =>
        {
            var r = new LanguageRig("aa bad");
            r.Idle();
            Assert.Single(r.Field.LanguageDiagnostics);
            r.Field.Editor.Document.Paragraphs[0].Runs[0].Text = "other text";
            Headless.Pump();
            Assert.Empty(r.Field.LanguageDiagnostics);
            Assert.Empty(r.Field.LanguageTokens);
            r.Idle();
            Assert.Equal("other text", r.Service.AnalyzedTexts.Last());
            r.Close();
        });

        // ------------------------------------------------ failures

        [Fact]
        public void A_service_that_fails_is_reported_and_the_same_text_is_not_asked_again_until_it_changes_or_the_service_is_restarted() => Headless.Run(() =>
        {
            var r = new LanguageRig("a bad", new FakeLanguageService { Throws = new InvalidOperationException("boom") });
            var failures = new List<Exception>();
            r.Field.LanguageServiceFailed += failures.Add;
            r.Idle();
            Assert.Equal("boom", Assert.Single(failures).Message);
            r.Field.Editor.SetCaret(new TextPosition(0, 5));
            r.Field.Editor.TypeText("x");
            r.Idle();
            Assert.Equal(2, failures.Count);               // a failure holds off that text only: the edit asks again, and fails again
            Assert.Equal(2, r.Service.AnalyzedTexts.Count);
            r.Service.Throws = null;
            r.Service.Analysis = FakeLanguageService.Words("word");
            r.Field.RestartLanguageAnalysis();
            r.Idle();
            Assert.Equal(3, r.Service.AnalyzedTexts.Count);
            Assert.Equal(2, r.Field.LanguageTokens.Count);
            r.Close();
        });

        [Fact]
        public void A_service_that_throws_when_asked_is_reported_too_and_the_field_goes_on() => Headless.Run(() =>
        {
            var r = new LanguageRig("a bad", new FakeLanguageService { Throws = new InvalidOperationException("sync"), ThrowsSynchronously = true });
            var failures = new List<Exception>();
            r.Field.LanguageServiceFailed += failures.Add;
            r.Idle();
            Assert.Equal("sync", Assert.Single(failures).Message);
            r.Field.Editor.SetCaret(new TextPosition(0, 5));
            r.Field.Editor.TypeText("x");
            Assert.Equal("a badx", r.Field.Editor.Document.Paragraphs[0].Text);
            r.Close();
        });

        // ------------------------------------------------ diagnostics are a view

        [Fact]
        public void Diagnostics_are_drawn_by_severity_under_the_text_and_hints_are_not_drawn() => Headless.Run(() =>
        {
            var r = new LanguageRig("a b c d", new FakeLanguageService
            {
                Analysis = t => new LanguageAnalysis(new LanguageToken[0], new[]
                {
                    new LanguageDiagnostic(new LanguageRange(0, 1), DiagnosticSeverity.Error, "e"),
                    new LanguageDiagnostic(new LanguageRange(2, 3), DiagnosticSeverity.Warning, "w"),
                    new LanguageDiagnostic(new LanguageRange(4, 5), DiagnosticSeverity.Information, "i"),
                    new LanguageDiagnostic(new LanguageRange(6, 7), DiagnosticSeverity.Hint, "h"),
                }),
            });
            r.Idle();
            var marks = r.Field.ParagraphLayoutFor(0).DiagnosticMarksDrawn;
            Assert.Equal(new[] { (0, 1), (2, 3), (4, 5) }, marks.Select(m => (m.Start, m.End)).ToArray());
            Assert.Equal(new IBrush[] { Brushes.Red, Brushes.Blue, Brushes.Gray }, marks.Select(m => m.Brush).ToArray());
            Assert.Equal(4, r.Field.LanguageDiagnostics.Count);   // the hint is reported, not drawn
            r.Close();
        });

        [Fact]
        public void Overlapping_diagnostics_are_all_drawn() => Headless.Run(() =>
        {
            var r = new LanguageRig("abcdef", new FakeLanguageService
            {
                Analysis = t => new LanguageAnalysis(new LanguageToken[0], new[]
                {
                    new LanguageDiagnostic(new LanguageRange(0, 4), DiagnosticSeverity.Error, "long"),
                    new LanguageDiagnostic(new LanguageRange(1, 2), DiagnosticSeverity.Warning, "short"),
                }),
            });
            r.Idle();
            Assert.Equal(2, r.Field.ParagraphLayoutFor(0).DiagnosticMarksDrawn.Count);
            var here = r.Field.DiagnosticsAt(new TextPosition(0, 1));
            Assert.Equal(new[] { "long", "short" }, here.Select(d => d.Message).ToArray());   // worst first
            Assert.Empty(r.Field.DiagnosticsAt(new TextPosition(0, 5)));
            r.Close();
        });

        [Fact]
        public void A_diagnostic_never_changes_the_document_its_runs_or_the_undo_history() => Headless.Run(() =>
        {
            var r = new LanguageRig("one bad two", new FakeLanguageService { Analysis = FakeLanguageService.Words("word", fixTo: "good") });
            var before = r.Field.Editor.Document.Paragraphs[0].Clone();
            r.Idle();
            Assert.Single(r.Field.LanguageDiagnostics);
            var after = r.Field.Editor.Document.Paragraphs[0];
            Assert.Equal(before.Text, after.Text);
            Assert.Equal(before.Runs.Count, after.Runs.Count);
            for (int i = 0; i < after.Runs.Count; i++)
            {
                Assert.Equal(before.Runs[i].Ws, after.Runs[i].Ws);
                Assert.Empty(after.Runs[i].Props.IntProps);    // no squiggle (property 5) and no colour was put in the run
                Assert.Empty(after.Runs[i].Props.StrProps);
            }
            Assert.Empty(r.Undo.Done);
            r.Close();
        });

        [Fact]
        public void Diagnostics_and_spelling_marks_are_drawn_together_and_each_is_its_own() => Headless.Run(() =>
        {
            var r = new LanguageRig("teh bad");
            r.Field.SpellIdlePost = a => { };
            r.Field.SpellChecking = new FakeSpellHost("bad");
            r.Idle();
            r.Field.CheckSpellingStep();
            var layout = r.Field.ParagraphLayoutFor(0);
            Assert.Equal(new[] { (0, 3) }, layout.SpellingMarksDrawn.Select(m => (m.Start, m.End)).ToArray());
            Assert.Equal(new[] { (4, 7) }, layout.DiagnosticMarksDrawn.Select(m => (m.Start, m.End)).ToArray());
            r.Close();
        });

        [Fact]
        public void A_diagnostic_is_not_drawn_across_the_text_of_an_input_method() => Headless.Run(() =>
        {
            var r = new LanguageRig("abcd", new FakeLanguageService
            {
                Analysis = t => new LanguageAnalysis(new LanguageToken[0], new[] { new LanguageDiagnostic(new LanguageRange(0, 4), DiagnosticSeverity.Error, "all") }),
            });
            r.Idle();
            Assert.Single(r.Field.ParagraphLayoutFor(0).DiagnosticMarksDrawn);
            r.Field.Editor.SetCaret(new TextPosition(0, 2));
            r.Field.SetPreedit("zz", null);
            Assert.Empty(r.Field.ParagraphLayoutFor(0).DiagnosticMarksDrawn);
            r.Field.SetPreedit(null, null);
            r.Close();
        });

        // ------------------------------------------------ chips

        static TokenStyleMap Styles() => new TokenStyleMap()
            .Set("word", new TokenStyle(Brushes.LightBlue, Brushes.Blue), new TokenStyle(Brushes.DarkBlue, Brushes.LightBlue))
            .Set("unknown", new TokenStyle(null, Brushes.Red));

        [Fact]
        public void Chips_are_drawn_for_the_kinds_the_map_has_and_not_for_the_others() => Headless.Run(() =>
        {
            var r = new LanguageRig("one bad two");
            r.Idle();
            Assert.Empty(r.Field.ChipsDrawn());                    // no map yet
            r.Field.TokenStyles = new TokenStyleMap().Set("word", new TokenStyle(Brushes.LightBlue));
            Assert.Equal(new[] { (0, 3), (8, 11) }, r.Field.ChipsDrawn().Select(c => (c.Start, c.End)).ToArray());
            r.Field.TokenStyles = Styles();
            Assert.Equal(3, r.Field.ChipsDrawn().Count);
            Assert.Same(Brushes.Red, r.Field.ChipsDrawn()[1].Style.Outline);
            r.Close();
        });

        [Fact]
        public void Chips_follow_the_theme_and_a_high_contrast_theme_gets_none() => Headless.Run(() =>
        {
            var r = new LanguageRig("one bad two");
            r.Idle();
            r.Field.TokenStyles = Styles();
            r.Window.RequestedThemeVariant = ThemeVariant.Light;
            Assert.Same(Brushes.LightBlue, r.Field.ChipsDrawn()[0].Style.Fill);
            r.Window.RequestedThemeVariant = ThemeVariant.Dark;
            Assert.Same(Brushes.DarkBlue, r.Field.ChipsDrawn()[0].Style.Fill);
            var probe = SystemColours.Probe;
            SystemColours.Probe = () => true;
            try { Assert.Empty(r.Field.ChipsDrawn()); }
            finally { SystemColours.Probe = probe; }
            Assert.NotEmpty(r.Field.ChipsDrawn());
            r.Close();
        });

        [Fact]
        public void Chips_are_decoration_behind_plain_text_and_change_nothing_in_the_document() => Headless.Run(() =>
        {
            var r = new LanguageRig("one bad two");
            r.Idle();
            var plain = Fixtures.Render(r.Field);
            r.Field.TokenStyles = new TokenStyleMap().Set("word", new TokenStyle(Brushes.LightBlue, Brushes.Blue));
            var chipped = Fixtures.Render(r.Field);
            Assert.True(CountOf(chipped, Colors.LightBlue) > 0);
            Assert.Equal(0, CountOf(plain, Colors.LightBlue));
            Assert.Equal("one bad two", r.Field.Editor.Document.Paragraphs[0].Text);
            Assert.Single(r.Field.Editor.Document.Paragraphs[0].Runs);
            Assert.Empty(r.Undo.Done);
            r.Close();
        });

        static int CountOf(Fixtures.Shot shot, Color colour)
        {
            int n = 0;
            for (int y = 0; y < shot.Height; y++)
                for (int x = 0; x < shot.Width; x++)
                    if (shot.Pixel(x, y) == colour) n++;
            return n;
        }

        // ------------------------------------------------ fixes

        [Fact]
        public void A_fix_is_applied_as_one_undo_step_and_the_text_is_asked_again() => Headless.Run(() =>
        {
            var r = new LanguageRig("one bad two", new FakeLanguageService { Analysis = FakeLanguageService.Words("word", fixTo: "good") });
            r.Idle();
            var d = r.Field.LanguageDiagnostics.Single();
            Assert.True(r.Field.ApplyDiagnosticFix(d));
            Assert.Equal("one good two", r.Field.Editor.Document.Paragraphs[0].Text);
            Assert.Equal(1, r.Undo.DoneSteps);
            r.Idle();
            Assert.Empty(r.Field.LanguageDiagnostics);
            Assert.True(r.Undo.Undo());
            Assert.Equal("one bad two", r.Field.Editor.Document.Paragraphs[0].Text);
            r.Close();
        });

        [Fact]
        public void A_fix_with_several_edits_is_one_step_and_keeps_the_text_between_them() => Headless.Run(() =>
        {
            var edits = new[] { new LanguageTextEdit(new LanguageRange(0, 1), "A"), new LanguageTextEdit(new LanguageRange(6, 7), "E") };
            var r = new LanguageRig("a bcd e", new FakeLanguageService
            {
                Analysis = t => new LanguageAnalysis(new LanguageToken[0], new[] { new LanguageDiagnostic(new LanguageRange(0, 7), DiagnosticSeverity.Warning, "caps", fix: new LanguageFix("Capitalise", edits)) }),
            });
            r.Idle();
            Assert.True(r.Field.ApplyDiagnosticFix(r.Field.LanguageDiagnostics[0]));
            Assert.Equal("A bcd E", r.Field.Editor.Document.Paragraphs[0].Text);
            Assert.Equal(1, r.Undo.DoneSteps);
            r.Close();
        });

        [Fact]
        public void A_fix_is_refused_when_the_field_is_read_only_the_text_changed_or_the_answer_is_still_out() => Headless.Run(() =>
        {
            var r = new LanguageRig("one bad", new FakeLanguageService { Analysis = FakeLanguageService.Words("word", fixTo: "good") });
            r.Idle();
            var d = r.Field.LanguageDiagnostics.Single();
            r.Field.IsReadOnly = true;
            Assert.False(r.Field.ApplyDiagnosticFix(d));
            r.Field.IsReadOnly = false;
            r.Service.Manual = true;
            r.Field.Editor.SetCaret(new TextPosition(0, 0));
            r.Field.Editor.TypeText("x");
            Assert.False(r.Field.ApplyDiagnosticFix(d));           // an old diagnostic of text that is not there now
            Assert.Equal("xone bad", r.Field.Editor.Document.Paragraphs[0].Text);
            Assert.False(r.Field.ApplyDiagnosticFix(new LanguageDiagnostic(new LanguageRange(0, 1), DiagnosticSeverity.Error, "not one of ours")));
            r.Close();
        });

        [Fact]
        public void A_fix_is_refused_where_the_text_may_not_be_edited() => Headless.Run(() =>
        {
            var doc = Doc(Para("en", "en", "one bad"));
            doc.Paragraphs[0].Runs[0].Props.SetInt(TextPropertyNumbers.Editable, TextPropertyNumbers.EditableNo);
            var (window, field) = Fixtures.InWindow(doc);
            var posts = new List<Action>();
            field.LanguageIdlePost = posts.Add;
            field.LanguageService = new FakeLanguageService { Analysis = FakeLanguageService.Words("word", fixTo: "good") };
            posts.ForEach(a => a());
            Assert.False(field.ApplyDiagnosticFix(field.LanguageDiagnostics.Single()));
            Assert.Equal("one bad", field.Editor.Document.Paragraphs[0].Text);
            window.Close();
        });

        // ------------------------------------------------ the context menu and hover

        [Fact]
        public void A_context_request_carries_the_diagnostics_at_the_place_with_their_fixes() => Headless.Run(() =>
        {
            var r = new LanguageRig("one bad two", new FakeLanguageService { Analysis = FakeLanguageService.Words("word", fixTo: "good") });
            r.Idle();
            var seen = new List<ContextMenuRequestedEventArgs>();
            r.Field.ContextMenuRequested += (o, e) => seen.Add(e);
            r.Field.Editor.SetCaret(new TextPosition(0, 5));
            r.Field.Focus();
            Headless.Pump();
            r.Field.Press(Key.Apps);
            var info = Assert.Single(seen).Info;
            var d = Assert.Single(info.Diagnostics);
            Assert.Equal("'bad' is bad", d.Message);
            Assert.Equal("Change to good", d.Fix!.Title);
            Assert.True(r.Field.ApplyDiagnosticFix(d));
            Assert.Equal("one good two", r.Field.Editor.Document.Paragraphs[0].Text);
            r.Field.Editor.SetCaret(new TextPosition(0, 1));
            r.Field.Press(Key.Apps);
            Assert.Empty(seen[1].Info.Diagnostics);
            r.Close();
        });

        [Fact]
        public void Hover_is_the_diagnostics_messages_then_the_services_help_and_is_dropped_when_the_text_changes_meanwhile() => Headless.Run(async () =>
        {
            var r = new LanguageRig("one bad two", new FakeLanguageService { Analysis = FakeLanguageService.Words("word"), HoverOf = (t, p) => new LanguageHover("help at " + p.Character) });
            r.Idle();
            Assert.Equal("'bad' is bad\nhelp at 5", await r.Field.GetLanguageHoverAsync(new TextPosition(0, 5)));
            Assert.Equal("help at 1", await r.Field.GetLanguageHoverAsync(new TextPosition(0, 1)));
            r.Service.HoverOf = (t, p) => null;
            Assert.Null(await r.Field.GetLanguageHoverAsync(new TextPosition(0, 1)));
            r.Close();
        });

        [Fact]
        public void The_pointer_resting_on_text_sets_the_tooltip_and_leaving_it_clears_it() => Headless.Run(() =>
        {
            var r = new LanguageRig("one bad two", new FakeLanguageService { Analysis = FakeLanguageService.Words("word") });
            r.Idle();
            var rect = r.Field.ParagraphLayoutFor(0).SelectionRects(4, 7)[0];
            var over = r.Field.TranslatePoint(new Point(rect.X + rect.Width / 2, rect.Y + rect.Height / 2), r.Window)!.Value;
            r.Window.MouseMove(over);
            Headless.Pump();
            Assert.Equal("'bad' is bad", ToolTip.GetTip(r.Field));
            r.Window.MouseMove(new Point(over.X, over.Y + 300));
            Headless.Pump();
            Assert.Null(ToolTip.GetTip(r.Field));
            r.Close();
        });

        // ------------------------------------------------ insert commands

        [Fact]
        public void Whether_an_insert_command_is_allowed_is_the_services_answer_for_the_selection() => Headless.Run(() =>
        {
            var r = new LanguageRig("ab", new FakeLanguageService { CanInsertOf = (t, sel, kind) => kind == "here" && sel.Start.Character == 1 && sel.End.Character == 2 });
            r.Field.Editor.SetCaret(new TextPosition(0, 2));
            r.Field.Editor.SetCaret(new TextPosition(0, 1), true);   // selection made backwards: the service gets it in order
            Assert.True(r.Field.CanInsert("here"));
            Assert.False(r.Field.CanInsert("there"));
            r.Field.IsReadOnly = true;
            Assert.False(r.Field.CanInsert("here"));
            r.Close();
        });

        // ------------------------------------------------ single line

        [Fact]
        public void A_single_line_field_passes_Enter_to_the_host_and_makes_no_hard_line_break() => Headless.Run(() =>
        {
            var r = new LanguageRig("ab");
            int enters = 0;
            r.Field.EnterRequested += m => enters++;
            r.Field.Focus();
            r.Field.Editor.SetCaret(new TextPosition(0, 1));
            r.Field.Press(Key.Enter);
            Assert.Equal(1, enters);
            r.Field.Press(Key.Enter, KeyModifiers.Shift);
            Assert.Equal("ab", r.Field.Editor.Document.Paragraphs[0].Text);
            Assert.Single(r.Field.Editor.Document.Paragraphs);
            r.Close();
        });
    }
}
