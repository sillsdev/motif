using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Xunit;

namespace SIL.TextEditing.Playground.Tests
{
    /// <summary>The sample environment service (ADR 0030) against what FieldWorks accepts and reports for an environment string. The inputs are
    /// the notation's own examples; the phonemes and classes are a fake list.</summary>
    public class EnvironmentServiceTests
    {
        static readonly string[] Segments = { "a", "ai", "b", "c", "d", "e", "f", "fl", "fr", "a", "í", "H" };

        static SampleEnvironmentService Service() => new SampleEnvironmentService(Segments, new[]
        {
            new SampleNaturalClass("V", "Vowels"), new SampleNaturalClass("C", "Consonants"), new SampleNaturalClass("+son", "Sonorants"),
            new SampleNaturalClass("+lab, +vd", "Voiced labials"), new SampleNaturalClass("+ant, -cor, -vd", "Anterior"),
        });

        static IReadOnlyList<LanguageDiagnostic> Check(SampleEnvironmentService service, string text) => service.AnalyzeAsync(text, CancellationToken.None).GetAwaiter().GetResult().Diagnostics;

        static LanguageAnalysis Analyze(string text) => Service().AnalyzeAsync(text, CancellationToken.None).GetAwaiter().GetResult();

        [Theory]
        [InlineData("/ # _")]
        [InlineData("/ _ #")]
        [InlineData("/ # _ #")]
        [InlineData("/ # _ a")]
        [InlineData("/ a _ #")]
        [InlineData("/ a _")]
        [InlineData("/ _ b")]
        [InlineData("/ a b _ ai d #")]
        [InlineData("/ ab _ ab")]                          // segments written together
        [InlineData("/ flaid _ flaid")]                    // the phonemes of a word need not be the longest ones
        [InlineData("/ (a) b _")]
        [InlineData("/(c) d _ (a) b")]
        [InlineData("/ (f) (fl) _ (d) (c)")]
        [InlineData("/ [V] _ ")]
        [InlineData("/ [V] _ [V]")]
        [InlineData("/ [+lab, +vd] _ [+lab, +vd]")]       // an abbreviation of two words
        [InlineData("/ [+lab,  +vd] _ ")]                   // spaces between the words mean one space
        [InlineData("/ [+ant, -cor, -vd] _ ")]              // three words
        [InlineData("/ ([C]) [V] _ ")]
        [InlineData("/# ([V]) b fr [C] _ a (c) #")]
        [InlineData("/ _ [C^1][V^1]")]                    // reduplication: a caret and a digit after a known class
        [InlineData("/ í _")]
        [InlineData("/ í _")]                       // the same letter written apart
        [InlineData("")]                                   // nothing to describe
        public void A_well_formed_environment_has_no_diagnostic(string text)
        {
            Assert.Empty(Analyze(text).Diagnostics);
        }

        [Theory]
        [InlineData("/ /_", SampleEnvironmentService.Codes.Syntax, "/", 2, 3)]                 // two slashes
        [InlineData("/ a _ _", SampleEnvironmentService.Codes.Syntax, "_", 6, 7)]              // two underscores
        [InlineData("/ _ _ a", SampleEnvironmentService.Codes.Syntax, "_", 4, 5)]
        [InlineData("/ # #_", SampleEnvironmentService.Codes.Syntax, "#", 4, 5)]               // two boundaries on the left
        [InlineData("/ _##", SampleEnvironmentService.Codes.Syntax, "#", 4, 5)]
        [InlineData("/ _# a", SampleEnvironmentService.Codes.Syntax, "a", 5, 6)]               // something after the boundary
        [InlineData("/a # _", SampleEnvironmentService.Codes.Syntax, "#", 3, 4)]               // something before the boundary
        [InlineData("/#", SampleEnvironmentService.Codes.Syntax, "#", 1, 2)]                   // no underscore
        [InlineData("/ _", SampleEnvironmentService.Codes.Syntax, "_", 2, 3)]                  // nothing before or after it
        [InlineData("a _", SampleEnvironmentService.Codes.Syntax, "a", 0, 1)]                  // no slash
        [InlineData("/ (a _", SampleEnvironmentService.Codes.MissingCloseParen, "(a", 2, 4)]
        [InlineData("/ a) _", SampleEnvironmentService.Codes.MissingOpenParen, "a)", 2, 4)]
        [InlineData("/ _ (a", SampleEnvironmentService.Codes.MissingCloseParen, "(a", 4, 6)]
        [InlineData("/ _ a)", SampleEnvironmentService.Codes.MissingOpenParen, "a)", 4, 6)]
        [InlineData("/ [C _", SampleEnvironmentService.Codes.MissingCloseBracket, "[C", 2, 4)]
        [InlineData("/  a] _", SampleEnvironmentService.Codes.MissingOpenBracket, "a]", 3, 5)]
        [InlineData("/ _ [C", SampleEnvironmentService.Codes.MissingCloseBracket, "[C", 4, 6)]
        [InlineData("/ _ a]", SampleEnvironmentService.Codes.MissingOpenBracket, "a]", 4, 6)]
        [InlineData("/ C] _", SampleEnvironmentService.Codes.MissingOpenBracket, "C]", 2, 4)]      // not a phoneme, but the bracket decides
        [InlineData("/ chr _", SampleEnvironmentService.Codes.UnknownSegment, "hr", 3, 5)]     // c is a phoneme, h is the first letter nothing carries on from
        [InlineData("/ _ chr", SampleEnvironmentService.Codes.UnknownSegment, "hr", 5, 7)]
        [InlineData("/ frage _", SampleEnvironmentService.Codes.UnknownSegment, "ge", 5, 7)]
        [InlineData("/ [+lab] _", SampleEnvironmentService.Codes.UnknownClass, "+lab", 3, 7)]
        [InlineData("/ _ [+lab, -vd]", SampleEnvironmentService.Codes.UnknownClass, "+lab, -vd", 5, 14)]
        [InlineData("/ _ [C^]", SampleEnvironmentService.Codes.UnknownClass, "C^", 5, 7)]      // the caret needs a digit
        [InlineData("/ _ [C^C]", SampleEnvironmentService.Codes.UnknownClass, "C^C", 5, 8)]
        [InlineData("/ _ [C^1C]", SampleEnvironmentService.Codes.UnknownClass, "C^1C", 5, 9)]
        [InlineData("/ _ [X^1]", SampleEnvironmentService.Codes.UnknownClass, "X^1", 5, 8)]
        [InlineData("/ _ [X][Y]", SampleEnvironmentService.Codes.UnknownClass, "X", 5, 6)]      // the first one is reported
        public void A_malformed_environment_gets_the_error_of_its_kind_at_the_place(string text, string code, string underlined, int start, int end)
        {
            var d = Assert.Single(Analyze(text).Diagnostics);
            Assert.Equal(code, d.Code);
            Assert.Equal(DiagnosticSeverity.Error, d.Severity);
            Assert.Equal(new LanguageRange(start, end), d.Range);
            Assert.Equal(underlined, text.Substring(start, end - start).Trim());
            Assert.Equal(SampleEnvironmentService.SourceName, d.Source);
            Assert.False(string.IsNullOrWhiteSpace(d.Message));
        }

        [Theory]
        [InlineData("/ [C _")]            // an empty or over-long class, an optional item with nothing in it
        [InlineData("/ [] _")]
        [InlineData("/ [a b c d e] _")]
        [InlineData("/ () _")]
        [InlineData("/ (a b) _")]
        public void Other_malformed_environments_are_reported_once(string text)
        {
            Assert.Single(Analyze(text).Diagnostics);
        }

        [Theory]
        [InlineData("   ")]          // only empty text has nothing to describe, as in FieldWorks
        [InlineData("/ [")]
        [InlineData("/ [ _")]
        [InlineData("/ [ (a)")]
        public void White_space_alone_and_a_lone_opening_bracket_are_syntax_problems(string text)
        {
            Assert.Equal(SampleEnvironmentService.Codes.Syntax, Assert.Single(Analyze(text).Diagnostics).Code);
        }

        [Fact]
        public void Class_abbreviations_are_compared_as_canonical_equivalents()
        {
            var service = new SampleEnvironmentService(new[] { "a" }, new[] { new SampleNaturalClass("é", "e acute") });
            Assert.Empty(Check(service, "/ [é] _"));
        }

        [Fact]
        public void Ranges_are_in_the_protocols_lines_when_the_text_holds_line_ends()
        {
            var analysis = Analyze("/ a\n_ [X]");
            var d = Assert.Single(analysis.Diagnostics);
            Assert.Equal(new LanguageRange(new LanguagePosition(1, 3), new LanguagePosition(1, 4)), d.Range);
            Assert.Equal(new LanguagePosition(1, 0), analysis.Tokens[2].Range.Start);
            var fixes = Analyze("/ a\n[v] _").Diagnostics.Single().Fix!;
            Assert.Equal(new LanguageRange(new LanguagePosition(1, 1), new LanguagePosition(1, 2)), fixes.Edits[0].Range);
        }

        [Fact]
        public void Only_the_first_problem_is_reported_and_an_earlier_syntax_error_beats_a_later_unknown_class()
        {
            var d = Assert.Single(Analyze("/ _ _ [X]").Diagnostics);
            Assert.Equal(SampleEnvironmentService.Codes.Syntax, d.Code);
            var e = Assert.Single(Analyze("/ [X] _ _").Diagnostics);
            Assert.Equal(SampleEnvironmentService.Codes.UnknownClass, e.Code);
        }

        [Fact]
        public void The_exact_fixes_make_the_text_well_formed()
        {
            foreach (var text in new[] { "/ (a _", "/ a) _", "/ [C _", "/ V] _", "/ _ [v]", "a _" })
            {
                var d = Assert.Single(Analyze(text).Diagnostics);
                var fix = Assert.IsType<LanguageFix>(d.Fix);
                string fixedText = text;
                foreach (var edit in fix.Edits.OrderByDescending(x => x.Range.Start.Character))
                    fixedText = fixedText.Substring(0, edit.Range.Start.Character) + edit.NewText + fixedText.Substring(edit.Range.End.Character);
                Assert.Empty(Analyze(fixedText).Diagnostics);
            }
            Assert.Null(Assert.Single(Analyze("/ chr _").Diagnostics).Fix);     // a phoneme that is not there has no exact fix
            Assert.Null(Assert.Single(Analyze("/ _ [Q]").Diagnostics).Fix);
        }

        [Fact]
        public void Tokens_are_by_kind_with_their_real_offsets_and_spaces_between_them_are_not_tokens()
        {
            var tokens = Analyze("/ ( a ) [V]_#x").Tokens;
            Assert.Equal(new[] { "punct", "optional", "segment", "optional", "class", "slot", "boundary", "unknown" }, tokens.Select(t => t.Kind).ToArray());
            Assert.Equal(new[] { 0, 2, 4, 6, 8, 11, 12, 13 }, tokens.Select(t => t.Range.Start.Character).ToArray());
            Assert.Equal(new[] { 1, 3, 5, 7, 11, 12, 13, 14 }, tokens.Select(t => t.Range.End.Character).ToArray());
            Assert.Equal("V", tokens[4].Target);
            Assert.Equal("a", tokens[2].Target);
            Assert.Empty(Analyze("").Tokens);
        }

        [Fact]
        public void Tokens_go_on_after_a_problem_so_the_rest_of_the_text_stays_highlighted()
        {
            var tokens = Analyze("/ [X] _ [V]").Tokens;
            Assert.Equal(new[] { "punct", "unknown", "slot", "class" }, tokens.Select(t => t.Kind).ToArray());
        }

        [Fact]
        public void A_class_the_project_no_longer_has_is_reported_after_the_service_says_it_changed()
        {
            var service = Service();
            int raised = 0;
            service.Changed += (o, e) => raised++;
            Assert.Empty(Check(service, "/ [V] _"));
            service.RemoveClass("V");
            Assert.Equal(1, raised);
            Assert.Equal(SampleEnvironmentService.Codes.UnknownClass, Check(service, "/ [V] _").Single().Code);
            service.AddClass("V", "Vowels");
            Assert.Equal(2, raised);
        }

        // ------------------------------------------------------------------ insert rules

        static bool Can(string kind, string text, int anchor, int end) =>
            Service().CanInsert(text, new LanguageRange(anchor, end), kind);

        [Theory]
        [InlineData("", true)]
        [InlineData("a", true)]
        [InlineData("/_", false)]
        [InlineData("a/", false)]
        public void The_slash_goes_in_while_the_text_has_none(string text, bool allowed) => Assert.Equal(allowed, Can("slash", text, 0, 0));

        [Theory]
        [InlineData("/a", 1, 1, true)]
        [InlineData("/a", 2, 2, true)]
        [InlineData("/a", 0, 0, false)]          // before the slash
        [InlineData("/a", 0, 2, false)]          // a selection that starts before it
        [InlineData("/a", 2, 0, false)]          // a selection made backwards that reaches before it
        [InlineData("/_", 2, 2, false)]          // there is one already
        [InlineData("a", 1, 1, false)]
        [InlineData("", 0, 0, false)]
        public void The_bar_goes_in_after_the_slash_once(string text, int anchor, int end, bool allowed) => Assert.Equal(allowed, Can("bar", text, anchor, end));

        [Theory]
        [InlineData("/_", 1, 1, true)]
        [InlineData("/_", 2, 2, true)]
        [InlineData("/a_b", 2, 2, true)]
        [InlineData("/_", 0, 0, false)]
        [InlineData("a", 1, 1, false)]
        [InlineData("", 0, 0, false)]
        [InlineData("/ # _", 2, 2, false)]       // before the boundary on the left
        [InlineData("/ # _", 3, 3, true)]        // just after it
        [InlineData("/ _ #", 4, 4, true)]        // just before the boundary on the right
        [InlineData("/ _ #", 5, 5, false)]       // after it
        [InlineData("/ # _ #", 2, 2, false)]
        [InlineData("/ # _ #", 4, 4, true)]
        [InlineData("/ # _ #", 7, 7, false)]
        [InlineData("/ # _ #", 1, 4, true)]      // a selection that covers a boundary
        public void An_item_goes_in_after_the_slash_and_inside_the_boundaries(string text, int anchor, int end, bool allowed)
        {
            Assert.Equal(allowed, Can("natural-class", text, anchor, end));
            Assert.Equal(allowed, Can("optional-item", text, anchor, end));
        }

        [Theory]
        [InlineData("/_", 1, 1, true)]
        [InlineData("/_", 2, 2, true)]
        [InlineData("/a_b", 2, 2, false)]        // in the middle of a context
        [InlineData("/_#", 2, 2, false)]
        [InlineData("/_", 0, 0, false)]
        [InlineData("a", 1, 1, false)]
        [InlineData("/ a _ b", 1, 1, true)]      // the very start of the left context
        [InlineData("/ a _ b", 7, 7, true)]      // the very end of the right one
        [InlineData("/ # _ b", 7, 7, true)]      // one on the left, so one on the right
        [InlineData("/ # _ b", 1, 1, false)]     // ... and not another on the left
        [InlineData("/ a _ #", 1, 1, true)]
        [InlineData("/ a _ #", 7, 7, false)]
        [InlineData("/ # _ #", 7, 7, false)]     // two already
        [InlineData("/ # _ b", 1, 4, true)]      // a selection over the boundary there is gives one boundary, still at the edge
        [InlineData("/ # _ b", 2, 3, true)]      // a selection that is the boundary itself
        [InlineData("/ a # b", 3, 3, false)]     // no underscore: in the middle of the text
        [InlineData("/ a", 1, 1, true)]          // no underscore: at either end
        [InlineData("/ a", 3, 3, true)]
        [InlineData("/ a _ b", 0, 7, false)]     // a selection that starts before the slash
        [InlineData("/ a _ b", 7, 1, true)]      // a selection made backwards over the whole of the contexts leaves one boundary
        [InlineData("/ a _ b", 4, 4, false)]     // just before the underscore, the end of the left context
        [InlineData("/ a _ b", 5, 5, false)]     // just after it, the start of the right context
        [InlineData("/ #", 1, 1, false)]         // no underscore, one boundary that opens: not a second at the same edge
        [InlineData("/ #", 3, 3, true)]          // ... only at the very end
        [InlineData("/ # a", 1, 1, false)]
        [InlineData("/ # a", 5, 5, true)]
        [InlineData("/ a #", 1, 1, true)]        // one that closes: the new one goes right after the slash only
        [InlineData("/ a #", 5, 5, false)]
        [InlineData("/#", 1, 1, false)]          // one that is both first and last counts as opening
        [InlineData("/#", 2, 2, true)]
        [InlineData("/ a # b", 1, 1, false)]     // one in the middle: no new one at an edge
        [InlineData("/ a # b", 7, 7, false)]
        [InlineData("/ a # b", 3, 5, false)]     // a selection over it puts the new one in the middle
        [InlineData("/ a # b", 1, 7, true)]      // a selection over all of it leaves one boundary
        public void A_word_boundary_goes_in_at_the_edge_of_a_context_once(string text, int anchor, int end, bool allowed) => Assert.Equal(allowed, Can("word-boundary", text, anchor, end));

        [Fact]
        public void An_insert_kind_the_service_does_not_know_is_not_allowed() => Assert.False(Can("picture", "/_", 1, 1));

        // ------------------------------------------------------------------ completion and hover

        static IReadOnlyList<CompletionItem> Complete(string text, int caret, CompletionTriggerKind kind = CompletionTriggerKind.Invoked) =>
            Service().CompleteAsync(text, new LanguagePosition(0, caret), kind, CancellationToken.None).Result;

        [Fact]
        public void In_an_empty_box_the_slash_is_offered_and_it_types_a_space_after_it()
        {
            var items = Complete("", 0);
            var slash = Assert.Single(items, i => i.Label == "/");
            Assert.Equal("/ ", slash.Inserted);
            Assert.DoesNotContain(items, i => i.Label == "_");
        }

        [Fact]
        public void After_the_slash_the_bar_the_boundary_the_optional_item_the_classes_and_the_phonemes_are_offered_and_the_optional_item_puts_the_caret_inside()
        {
            var items = Complete("/ ", 2);
            Assert.Contains(items, i => i.Label == "_");
            Assert.Contains(items, i => i.Label == "#");
            var optional = Assert.Single(items, i => i.Label == "()");
            Assert.Equal(1, optional.CaretOffset);
            Assert.Equal(new[] { "[V]", "[C]", "[+son]", "[+lab, +vd]", "[+ant, -cor, -vd]" }, items.Where(i => i.Kind == CompletionItemKind.Class).Select(i => i.Label).ToArray());
            Assert.Contains(items, i => i.Kind == CompletionItemKind.Value && i.Label == "fl");
            Assert.DoesNotContain(items, i => i.Label == "/");
            Assert.All(items, i => Assert.Null(i.ReplaceRange));
        }

        [Fact]
        public void Inside_an_open_bracket_only_the_classes_that_start_with_what_is_typed_are_offered_and_they_replace_it()
        {
            var items = Complete("/ [", 3, CompletionTriggerKind.TriggerCharacter);
            Assert.Equal(5, items.Count);
            var v = Complete("/ [v", 4).Single();
            Assert.Equal("[V]", v.Inserted);
            Assert.Equal("Vowels", v.Detail);
            Assert.Equal(new LanguageRange(2, 4), v.ReplaceRange);
            Assert.Equal(new LanguageRange(2, 5), Complete("/ [v] _", 4).Single().ReplaceRange);   // a closing bracket already there is replaced, not doubled
            Assert.Equal(3, Complete("/ [+", 4).Count);
            Assert.Empty(Complete("/ [x", 4));
        }

        [Fact]
        public void A_word_being_typed_is_completed_with_the_phonemes_that_start_with_it()
        {
            var items = Complete("/ f", 3);
            Assert.Equal(new[] { "f", "fl", "fr" }, items.Select(i => i.Label).ToArray());
            Assert.All(items, i => Assert.Equal(new LanguageRange(2, 3), i.ReplaceRange));
            Assert.Empty(Complete("/ zz", 4));
        }

        [Fact]
        public void The_service_opens_the_popup_on_an_opening_bracket_only()
        {
            Assert.Equal(new[] { '[' }, Service().CompletionTriggerCharacters.ToArray());
        }

        [Fact]
        public void Hover_names_the_class_the_segment_and_the_marks_and_says_nothing_for_what_is_not_there()
        {
            var service = Service();
            string? Hover(string text, int at) => service.HoverAsync(text, new LanguagePosition(0, at), CancellationToken.None).Result?.Contents;
            Assert.Equal("Natural class V: Vowels.", Hover("/ [V] _", 3));
            Assert.Equal("Segment ai.", Hover("/ ai _", 3));
            Assert.Contains("Where the allomorph stands", Hover("/ a _", 4));
            Assert.Contains("Word boundary", Hover("/ _ #", 4));
            Assert.Null(Hover("/ a _", 1));     // between items
            Assert.Null(Hover("/ zz _", 3));    // an unknown segment is the diagnostic's to explain
        }
    }
}
