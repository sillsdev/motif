using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using SIL.TextEditing.Tests;
using Xunit;
using static SIL.TextEditing.Tests.TestDocs;

namespace SIL.TextEditing.Avalonia.Tests
{
    /// <summary>ADR 0030 for a field of several paragraphs (source mode's Markdown, ADR 0035): the service is given the paragraphs joined by line feeds, the line
    /// being the paragraph's index, and its findings are drawn, hovered, carried over edits and fixed in the paragraph they are on.</summary>
    public class LanguageParagraphsTests
    {
        sealed class Rig
        {
            public readonly Window Window;
            public readonly RichTextField Field;
            public readonly FakeLanguageService Service;
            readonly List<Action> _posts = new List<Action>();

            public Rig(FakeLanguageService service, params string[] lines)
            {
                (Window, Field) = Fixtures.InWindow(Doc(lines.Select(l => Para("en", "en", l)).ToArray()));
                Service = service;
                Field.LanguageIdlePost = _posts.Add;
                Field.LanguageService = Service;
            }

            public void Idle()
            {
                for (int guard = 0; guard < 50 && _posts.Count > 0; guard++)
                {
                    var next = _posts[0];
                    _posts.RemoveAt(0);
                    next();
                }
                Headless.Pump();
            }
        }

        static FakeLanguageService Words(string? fixTo = null) => new FakeLanguageService { Analysis = FakeLanguageService.Words("word", fixTo: fixTo) };

        [Fact]
        public void A_diagnostic_on_a_line_is_drawn_and_found_on_that_paragraph_only() => Headless.Run(() =>
        {
            var r = new Rig(Words(), "one two", "three bad");
            r.Idle();
            var d = Assert.Single(r.Field.LanguageDiagnostics);
            Assert.Equal(new LanguageRange(new LanguagePosition(1, 6), new LanguagePosition(1, 9)), d.Range);
            Assert.Empty(r.Field.ParagraphLayoutFor(0).DiagnosticMarksDrawn);
            var mark = Assert.Single(r.Field.ParagraphLayoutFor(1).DiagnosticMarksDrawn);
            Assert.Equal((6, 9), (mark.Start, mark.End));
            Assert.Single(r.Field.DiagnosticsAt(new TextPosition(1, 7)));
            Assert.Empty(r.Field.DiagnosticsAt(new TextPosition(0, 7)));
            r.Window.Close();
        });

        [Fact]
        public void A_diagnostic_spanning_two_lines_is_drawn_on_both_paragraphs() => Headless.Run(() =>
        {
            var range = new LanguageRange(new LanguagePosition(0, 4), new LanguagePosition(1, 2));
            var service = new FakeLanguageService { Analysis = t => new LanguageAnalysis(new LanguageToken[0], new[] { new LanguageDiagnostic(range, DiagnosticSeverity.Warning, "spans") }) };
            var r = new Rig(service, "one two", "three");
            r.Idle();
            Assert.Equal((4, 7), (r.Field.ParagraphLayoutFor(0).DiagnosticMarksDrawn.Single().Start, r.Field.ParagraphLayoutFor(0).DiagnosticMarksDrawn.Single().End));
            Assert.Equal((0, 2), (r.Field.ParagraphLayoutFor(1).DiagnosticMarksDrawn.Single().Start, r.Field.ParagraphLayoutFor(1).DiagnosticMarksDrawn.Single().End));
            r.Window.Close();
        });

        [Fact]
        public void An_edit_above_carries_a_diagnostic_below_to_its_new_line_until_the_text_is_asked_again() => Headless.Run(() =>
        {
            var r = new Rig(Words(), "one", "two bad");
            r.Idle();
            r.Field.Editor.SetCaret(new TextPosition(0, 3));
            r.Field.Editor.Enter();
            Assert.False(r.Field.IsLanguageAnalysisCurrent);
            var carried = Assert.Single(r.Field.LanguageDiagnostics);
            Assert.Equal(new LanguagePosition(2, 4), carried.Range.Start);
            Assert.Null(carried.Fix);
            Assert.Single(r.Field.ParagraphLayoutFor(2).DiagnosticMarksDrawn);
            r.Field.Editor.TypeText("x");   // typing in the new empty paragraph moves nothing below it
            Assert.Equal(new LanguagePosition(2, 4), Assert.Single(r.Field.LanguageDiagnostics).Range.Start);
            r.Idle();
            Assert.True(r.Field.IsLanguageAnalysisCurrent);
            Assert.Equal("one\nx\ntwo bad", r.Service.AnalyzedTexts.Last());
            r.Window.Close();
        });

        [Fact]
        public void An_edit_on_the_same_line_before_a_diagnostic_moves_it_along_the_line() => Headless.Run(() =>
        {
            var r = new Rig(Words(), "one", "two bad");
            r.Idle();
            r.Field.Editor.SetCaret(new TextPosition(1, 0));
            r.Field.Editor.TypeText("zz");
            Assert.Equal(new LanguageRange(new LanguagePosition(1, 6), new LanguagePosition(1, 9)), Assert.Single(r.Field.LanguageDiagnostics).Range);
            r.Window.Close();
        });

        [Fact]
        public void An_answer_for_paragraphs_that_changed_is_dropped() => Headless.Run(() =>
        {
            var r = new Rig(new FakeLanguageService { Manual = true }, "one", "two");
            r.Idle();
            var (_, source) = Assert.Single(r.Service.PendingAnalyses);
            r.Field.Editor.SetCaret(new TextPosition(1, 3));
            r.Field.Editor.TypeText(" bad");
            source.SetResult(FakeLanguageService.Words("word")("one\ntwo"));
            r.Idle();
            Assert.Empty(r.Field.LanguageTokens);
            Assert.Equal("one\ntwo bad", r.Service.PendingAnalyses.Last().Text);
            r.Window.Close();
        });

        [Fact]
        public void A_fix_on_a_line_replaces_the_text_in_that_paragraph_as_one_step() => Headless.Run(() =>
        {
            var r = new Rig(Words(fixTo: "good"), "one", "two bad three");
            var undo = new SimpleUndoStack();
            undo.Attach(r.Field.Editor);
            r.Idle();
            Assert.True(r.Field.ApplyDiagnosticFix(r.Field.LanguageDiagnostics.Single()));
            Assert.Equal("two good three", r.Field.Editor.Document.Paragraphs[1].Text);
            Assert.Equal("one", r.Field.Editor.Document.Paragraphs[0].Text);
            Assert.Equal(1, undo.DoneSteps);
            r.Window.Close();
        });

        [Fact]
        public void A_fix_whose_edits_cross_a_line_end_is_refused() => Headless.Run(() =>
        {
            var range = new LanguageRange(new LanguagePosition(0, 1), new LanguagePosition(1, 1));
            var fix = new LanguageFix("join", new[] { new LanguageTextEdit(range, "") });
            var service = new FakeLanguageService { Analysis = t => new LanguageAnalysis(new LanguageToken[0], new[] { new LanguageDiagnostic(range, DiagnosticSeverity.Error, "x", fix: fix) }) };
            var r = new Rig(service, "one", "two");
            r.Idle();
            Assert.False(r.Field.ApplyDiagnosticFix(r.Field.LanguageDiagnostics.Single()));
            Assert.Equal(2, r.Field.Editor.Document.Paragraphs.Count);
            r.Window.Close();
        });

        [Fact]
        public void Hover_on_a_later_line_is_asked_at_that_line_and_names_its_diagnostic() => Headless.Run(() =>
        {
            var service = Words();
            LanguagePosition? asked = null;
            service.HoverOf = (t, p) => { asked = p; return new LanguageHover("help"); };
            var r = new Rig(service, "one", "two bad");
            r.Idle();
            var task = r.Field.GetLanguageHoverAsync(new TextPosition(1, 5));
            Headless.Pump();
            Assert.Equal("'bad' is bad\nhelp", task.Result);
            Assert.Equal(new LanguagePosition(1, 5), asked);
            r.Window.Close();
        });
    }
}
