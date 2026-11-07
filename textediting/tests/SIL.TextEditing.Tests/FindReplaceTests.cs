using System.Collections.Generic;
using System.Linq;
using System.Text;
using Xunit;
using static SIL.TextEditing.Tests.TestDocs;

namespace SIL.TextEditing.Tests
{
    /// <summary>ADR 0021 and 0025: find and replace over one field's text.</summary>
    public class FindReplaceTests
    {
        const string Acute = "\u0301";

        static FindOptions Opt(string pattern, bool matchCase = false, bool matchDiacritics = true, bool wholeWord = false) =>
            new FindOptions { Pattern = pattern, MatchCase = matchCase, MatchDiacritics = matchDiacritics, WholeWord = wholeWord };

        static DocumentEditor One(string text, NormalizationForm? form = null)
        {
            var doc = OneParagraph(text);
            doc.Normalization = form;
            return new DocumentEditor(doc);
        }

        static string[] Found(DocumentEditor e, FindOptions o) =>
            e.FindAll(o).Select(r => e.Document.Paragraphs[r.Start.Paragraph].Text.Substring(r.Start.Offset, r.End.Offset - r.Start.Offset)).ToArray();

        static string Text(DocumentEditor e, int paragraph = 0) => e.Document.Paragraphs[paragraph].Text;

        [Fact]
        public void A_plain_find_ignores_case_by_default_and_matches_it_on_request()
        {
            var e = One("Cat cat CAT");
            Assert.Equal(new[] { "Cat", "cat", "CAT" }, Found(e, Opt("cat")));
            Assert.Equal(new[] { "cat" }, Found(e, Opt("cat", matchCase: true)));
        }

        [Fact]
        public void Find_next_starts_after_the_selection_and_find_previous_before_it_and_neither_wraps_unless_asked()
        {
            var e = One("ab ab ab");
            Assert.True(e.TryFind(Opt("ab"), true, false, out var m, out _));
            Assert.Equal(new TextRange(0, 0, 2), m);
            e.SelectRange(m);
            Assert.True(e.TryFind(Opt("ab"), true, false, out m, out _));
            Assert.Equal(new TextRange(0, 3, 5), m);
            e.SelectRange(new TextRange(0, 6, 8));
            Assert.False(e.TryFind(Opt("ab"), true, false, out _, out _));
            Assert.True(e.TryFind(Opt("ab"), true, true, out m, out bool wrapped));
            Assert.Equal(new TextRange(0, 0, 2), m);
            Assert.True(wrapped);
            Assert.True(e.TryFind(Opt("ab"), false, false, out m, out wrapped));
            Assert.Equal(new TextRange(0, 3, 5), m);
            Assert.False(wrapped);
            e.SelectRange(new TextRange(0, 0, 2));
            Assert.False(e.TryFind(Opt("ab"), false, false, out _, out _));
            Assert.True(e.TryFind(Opt("ab"), false, true, out m, out wrapped));
            Assert.Equal(new TextRange(0, 6, 8), m);
            Assert.True(wrapped);
        }

        [Fact]
        public void A_search_crosses_paragraphs_in_order()
        {
            var e = new DocumentEditor(Doc(Para("en", "en", "one"), Para("en", "en", "two one"), Para("en", "en", "one")));
            Assert.Equal(new[] { new TextRange(0, 0, 3), new TextRange(1, 4, 7), new TextRange(2, 0, 3) }, e.FindAll(Opt("one")));
            e.SetCaret(new TextPosition(1, 0));
            Assert.True(e.TryFind(Opt("one"), true, false, out var m, out _));
            Assert.Equal(new TextRange(1, 4, 7), m);
            e.SetCaret(new TextPosition(2, 0));
            Assert.True(e.TryFind(Opt("one"), false, false, out m, out _));
            Assert.Equal(new TextRange(1, 4, 7), m);
        }

        [Fact]
        public void A_backward_search_finds_overlapping_matches_a_forward_one_steps_over()
        {
            var e = One("aaa");
            Assert.Equal(new[] { new TextRange(0, 0, 2) }, e.FindAll(Opt("aa")));
            e.SetCaret(new TextPosition(0, 3));
            Assert.True(e.TryFind(Opt("aa"), false, false, out var m, out _));
            Assert.Equal(new TextRange(0, 1, 3), m);
        }

        [Fact]
        public void Precomposed_and_decomposed_text_find_each_other_whatever_form_the_document_stores()
        {
            string composed = "caf\u00E9", decomposed = "cafe" + Acute;
            foreach (var stored in new[] { composed, decomposed })
            foreach (var pattern in new[] { composed, decomposed })
            {
                var e = One("a " + stored + " b");
                var all = e.FindAll(Opt(pattern));
                Assert.Single(all);
                Assert.Equal(2, all[0].Start.Offset);
                Assert.Equal(2 + stored.Length, all[0].End.Offset);
            }
        }

        [Fact]
        public void With_diacritics_matched_a_base_letter_does_not_find_a_letter_that_carries_a_mark()
        {
            foreach (var stored in new[] { "e" + Acute, "\u00E9" })
            {
                var e = One("x " + stored + " e");
                Assert.Equal(new[] { new TextRange(0, e.Document.Paragraphs[0].Length - 1, e.Document.Paragraphs[0].Length) }, e.FindAll(Opt("e")));
            }
        }

        [Fact]
        public void With_diacritics_ignored_marks_are_skipped_in_the_text_and_in_the_pattern_and_the_whole_letter_is_matched()
        {
            var e = One("cafe" + Acute + " CAFE");
            var o = Opt("cafe", matchDiacritics: false);
            Assert.Equal(new[] { "cafe" + Acute, "CAFE" }, Found(e, o));
            Assert.Equal(new[] { "cafe" + Acute, "CAFE" }, Found(e, Opt("caf\u00E9", matchDiacritics: false)));
            // The marks of the pattern are not required of the text either.
            Assert.Equal(new[] { "cafe" + Acute, "CAFE" }, Found(e, Opt("cafe" + Acute, matchDiacritics: false)));
        }

        [Fact]
        public void With_diacritics_matched_the_marks_must_agree()
        {
            var e = One("cafe cafe" + Acute);
            Assert.Equal(new[] { "cafe" }, Found(e, Opt("cafe")));
            Assert.Equal(new[] { "cafe" + Acute }, Found(e, Opt("cafe" + Acute)));
        }

        [Fact]
        public void Matching_case_matches_diacritics_exactly_too_as_FieldWorks_collation_does()
        {
            var e = One("cafe" + Acute + " cafe");
            Assert.Equal(new[] { "cafe" }, Found(e, Opt("cafe", matchCase: true, matchDiacritics: false)));
        }

        [Fact]
        public void A_match_cannot_start_inside_a_precomposed_letter_but_can_in_decomposed_text()
        {
            Assert.Empty(One("\u00E9").FindAll(Opt(Acute)));
            Assert.Equal(new[] { new TextRange(0, 1, 2) }, One("e" + Acute).FindAll(Opt(Acute)));
        }

        [Fact]
        public void A_whole_word_match_starts_and_ends_at_word_boundaries()
        {
            var e = One("cat concat cat-cap category cat.");
            Assert.Equal(5, Found(e, Opt("cat")).Length);
            Assert.Equal(new[] { new TextRange(0, 0, 3), new TextRange(0, 11, 14), new TextRange(0, 28, 31) }, e.FindAll(Opt("cat", wholeWord: true)));
        }

        [Fact]
        public void Whole_word_matching_works_for_a_word_with_a_combining_mark()
        {
            var e = One("se" + Acute + " se sea");
            Assert.Equal(new[] { new TextRange(0, 0, 3) }, e.FindAll(Opt("se" + Acute, wholeWord: true)));
            Assert.Equal(new[] { new TextRange(0, 0, 3), new TextRange(0, 4, 6) }, e.FindAll(Opt("se", matchDiacritics: false, wholeWord: true)));
        }

        [Fact]
        public void Surrogate_pairs_are_matched_whole_and_never_split()
        {
            var e = One("a\uD83D\uDE00b");
            Assert.Equal(new[] { new TextRange(0, 1, 3) }, e.FindAll(Opt("\uD83D\uDE00")));
            Assert.Empty(e.FindAll(Opt("\uDE00")));
        }

        [Fact]
        public void An_embedded_object_is_ignorable_inside_a_match_and_never_part_of_its_edges()
        {
            var p = new Paragraph { DefaultWs = "en" };
            p.Runs.Add(new Run { Ws = "en", Text = "ab" });
            p.Runs.Add(Run.Object("en", "\u0008pic"));
            p.Runs.Add(new Run { Ws = "en", Text = "cd" });
            var e = new DocumentEditor(Doc(p));
            Assert.Equal(new[] { new TextRange(0, 0, 5) }, e.FindAll(Opt("abcd")));
            Assert.Equal(new[] { new TextRange(0, 0, 2) }, e.FindAll(Opt("ab")));
            Assert.Equal(new[] { new TextRange(0, 3, 5) }, e.FindAll(Opt("cd")));
        }

        [Fact]
        public void Searching_by_writing_system_and_by_character_style_checks_every_run_a_match_touches()
        {
            var styled = new Run { Ws = "en", Text = "hot" };
            styled.Props.SetStr(TextPropertyNumbers.NamedStyle, "Emphasis");
            var p = new Paragraph { DefaultWs = "en" };
            p.Runs.Add(new Run { Ws = "en", Text = "hot " });
            p.Runs.Add(new Run { Ws = "fr", Text = "hot " });
            p.Runs.Add(styled);
            var e = new DocumentEditor(Doc(p));
            Assert.Equal(new[] { new TextRange(0, 4, 7) }, e.FindAll(new FindOptions { Pattern = "hot", WritingSystem = "fr" }));
            Assert.Equal(new[] { new TextRange(0, 8, 11) }, e.FindAll(new FindOptions { Pattern = "hot", CharacterStyle = "Emphasis" }));
            Assert.Equal(new[] { new TextRange(0, 0, 3), new TextRange(0, 4, 7) }, e.FindAll(new FindOptions { Pattern = "hot", CharacterStyle = "" }));
        }

        [Fact]
        public void An_empty_pattern_with_a_writing_system_finds_each_stretch_of_that_writing_system()
        {
            var e = new DocumentEditor(Doc(Para("en", "en", "ab", "fr", "cd", "fr", "ef", "en", "gh")));
            Assert.Equal(new[] { new TextRange(0, 2, 6) }, e.FindAll(new FindOptions { WritingSystem = "fr" }));
            Assert.Empty(e.FindAll(new FindOptions()));
        }

        [Fact]
        public void A_regular_expression_searches_the_text_and_a_bad_one_matches_nothing_and_says_why()
        {
            var e = One("a1 b22 c333");
            Assert.Equal(new[] { "1", "22", "333" }, Found(e, new FindOptions { Pattern = "[0-9]+", UseRegularExpression = true }));
            Assert.Equal(new[] { "A" }, Found(One("a A"), new FindOptions { Pattern = "A", UseRegularExpression = true, MatchCase = true }));
            var bad = new FindOptions { Pattern = "(", UseRegularExpression = true };
            Assert.False(bad.IsValid(out var error));
            Assert.False(string.IsNullOrEmpty(error));
            Assert.Empty(e.FindAll(bad));
            Assert.False(e.TryFind(bad, true, true, out _, out _));
        }

        [Fact]
        public void Text_hidden_by_a_line_limit_is_not_found()
        {
            var e = One("abc abc");
            e.CaretLimit = p => 3;
            Assert.Equal(new[] { new TextRange(0, 0, 3) }, e.FindAll(Opt("abc")));
        }

        [Fact]
        public void Selecting_a_match_in_read_only_text_selects_exactly_that_text()
        {
            var ro = new Run { Ws = "en", Text = "locked " };
            ro.Props.SetInt(TextPropertyNumbers.Editable, TextPropertyNumbers.EditableNo);
            var p = new Paragraph { DefaultWs = "en" };
            p.Runs.Add(ro);
            p.Runs.Add(new Run { Ws = "en", Text = "free" });
            var e = new DocumentEditor(Doc(p));
            Assert.True(e.TryFind(Opt("lock"), true, false, out var m, out _));
            e.SelectRange(m);
            Assert.Equal(new TextPosition(0, 0), e.Anchor);
            Assert.Equal(new TextPosition(0, 4), e.Caret);
            Assert.False(e.CanReplace(m));
            Assert.False(e.ReplaceMatch(m, Opt("lock"), "x"));
            Assert.Equal("locked free", Text(e));
        }

        [Fact]
        public void Replace_takes_the_properties_of_the_first_matched_run_and_is_one_undo_step()
        {
            var bold = new Run { Ws = "fr", Text = "chat" };
            bold.Props.SetInt(TextPropertyNumbers.Bold, TextPropertyNumbers.ToggleOn);
            var p = new Paragraph { DefaultWs = "en" };
            p.Runs.Add(new Run { Ws = "en", Text = "a " });
            p.Runs.Add(bold);
            p.Runs.Add(new Run { Ws = "en", Text = " z" });
            var e = new DocumentEditor(Doc(p));
            var undo = new SimpleUndoStack();
            undo.Attach(e);
            var o = Opt("chat");
            Assert.True(e.TryFind(o, true, false, out var m, out _));
            e.SelectRange(m);
            Assert.True(e.ReplaceCurrent(o, "chien"));
            Assert.Equal("a chien z", Text(e));
            var run = e.Document.Paragraphs[0].Runs[1];
            Assert.Equal("fr", run.Ws);
            Assert.True(run.Props.TryGetInt(TextPropertyNumbers.Bold, out _));
            Assert.Equal(1, undo.DoneSteps);
            Assert.Equal(new TextPosition(0, 7), e.Caret);
            undo.Undo();
            Assert.Equal("a chat z", Text(e));
        }

        [Fact]
        public void Replace_can_give_the_new_text_another_writing_system_and_style()
        {
            var e = One("say hello now");
            var o = Opt("hello");
            Assert.True(e.ReplaceMatch(new TextRange(0, 4, 9), o, "bonjour", "fr", "Emphasis"));
            var runs = e.Document.Paragraphs[0].Runs;
            Assert.Equal(3, runs.Count);
            Assert.Equal("fr", runs[1].Ws);
            Assert.Equal("Emphasis", runs[1].Props.StrProps[TextPropertyNumbers.NamedStyle]);
            Assert.Equal("en", runs[0].Ws);
        }

        [Fact]
        public void Replace_refuses_a_range_that_is_not_a_match_now()
        {
            var e = One("abc abd");
            Assert.False(e.ReplaceMatch(new TextRange(0, 4, 7), Opt("abc"), "x"));
            Assert.False(e.ReplaceCurrent(Opt("abc"), "x"));
            Assert.Equal("abc abd", Text(e));
        }

        [Fact]
        public void Replace_keeps_embedded_objects_inside_the_match_after_the_new_text()
        {
            var p = new Paragraph { DefaultWs = "en" };
            p.Runs.Add(new Run { Ws = "en", Text = "ab" });
            p.Runs.Add(Run.Object("en", "\u0008pic"));
            p.Runs.Add(new Run { Ws = "en", Text = "cd" });
            var e = new DocumentEditor(Doc(p));
            Assert.True(e.ReplaceMatch(new TextRange(0, 0, 5), Opt("abcd"), "X"));
            var runs = e.Document.Paragraphs[0].Runs;
            Assert.Equal(new[] { "X", "\uFFFC" }, runs.Select(r => r.Text).ToArray());
            Assert.True(runs[1].IsObject);
        }

        [Fact]
        public void Replace_all_is_one_undo_step_does_not_search_the_new_text_and_keeps_the_selection()
        {
            var e = new DocumentEditor(Doc(Para("en", "en", "a a a"), Para("en", "en", "xa ba")));
            var undo = new SimpleUndoStack();
            undo.Attach(e);
            e.SelectRange(new TextRange(0, 2, 5));
            Assert.Equal(5, e.ReplaceAll(Opt("a"), "aa"));
            Assert.Equal("aa aa aa", Text(e, 0));
            Assert.Equal("xaa baa", Text(e, 1));
            Assert.Equal(1, undo.DoneSteps);
            Assert.Equal(new TextPosition(0, 3), e.Anchor);
            Assert.Equal(new TextPosition(0, 8), e.Caret);
            undo.Undo();
            Assert.Equal("a a a", Text(e, 0));
            Assert.Equal("xa ba", Text(e, 1));
            Assert.Equal(0, undo.DoneSteps);
        }

        [Fact]
        public void Replace_all_with_nothing_deletes_and_leaves_matches_that_cannot_be_replaced()
        {
            var ro = new Run { Ws = "en", Text = "ab" };
            ro.Props.SetInt(TextPropertyNumbers.Editable, TextPropertyNumbers.EditableNo);
            var p = new Paragraph { DefaultWs = "en" };
            p.Runs.Add(new Run { Ws = "en", Text = "ab-" });
            p.Runs.Add(ro);
            p.Runs.Add(new Run { Ws = "en", Text = "-ab" });
            var e = new DocumentEditor(Doc(p));
            Assert.Equal(2, e.ReplaceAll(Opt("ab"), ""));
            Assert.Equal("-ab-", Text(e));
            e.IsReadOnly = true;
            Assert.Equal(0, e.ReplaceAll(Opt("ab"), "x"));
        }

        [Fact]
        public void A_regular_expression_replacement_can_use_groups()
        {
            var e = One("john smith, jane doe");
            var o = new FindOptions { Pattern = "(\\w+) (\\w+)", UseRegularExpression = true };
            Assert.Equal(2, e.ReplaceAll(o, "$2 $1"));
            Assert.Equal("smith john, doe jane", Text(e));
        }

        [Fact]
        public void A_replacement_with_a_line_break_becomes_a_hard_line_break_not_a_paragraph()
        {
            var e = One("a-b");
            Assert.True(e.ReplaceMatch(new TextRange(0, 1, 2), Opt("-"), "\r\n"));
            Assert.Single(e.Document.Paragraphs);
            Assert.Equal("a\u2028b", Text(e));
        }

        [Fact]
        public void Replacing_only_the_writing_system_keeps_the_text()
        {
            var e = new DocumentEditor(Doc(Para("en", "en", "ab", "fr", "cd")));
            Assert.Equal(1, e.ReplaceAll(new FindOptions { WritingSystem = "fr" }, "", "en"));
            Assert.Equal("abcd", Text(e));
            Assert.Single(e.Document.Paragraphs[0].Runs);
            Assert.Equal("en", e.Document.Paragraphs[0].Runs[0].Ws);
        }

        [Fact]
        public void The_replacement_is_normalised_to_the_stored_form()
        {
            var e = One("x", NormalizationForm.FormD);
            Assert.True(e.ReplaceMatch(new TextRange(0, 0, 1), Opt("x"), "caf\u00E9"));
            Assert.Equal("cafe" + Acute, Text(e));
        }

        [Fact]
        public void A_regular_expression_that_would_cut_a_letter_from_its_marks_does_not_match_and_the_answer_does_not_depend_on_the_stored_form()
        {
            foreach (var original in new[] { "caf\u00E9 x", "cafe" + Acute + " x" })
            foreach (NormalizationForm? form in new NormalizationForm?[] { null, NormalizationForm.FormC, NormalizationForm.FormD })
            {
                // The letters run stops before the accent, so it is not a whole letter: refused, in every form.
                var e = One(original, form);
                string before = Text(e);
                var letters = new FindOptions { Pattern = "(\\p{L}+)", UseRegularExpression = true };
                Assert.Equal(1, e.ReplaceAll(letters, "<$1>"));
                Assert.Equal(before.Replace(" x", " <x>"), Text(e));
                var e2 = One(original, form);
                Assert.Equal(0, e2.ReplaceAll(new FindOptions { Pattern = "e", UseRegularExpression = true }, "X"));
                // A pattern that takes the marks with the letter replaces whole letters, in every form.
                var e3 = One(original, form);
                Assert.Equal(5, e3.ReplaceAll(new FindOptions { Pattern = "\\p{L}\\p{M}*", UseRegularExpression = true }, "<$0>"));
                Assert.Equal("<c><a><f><\u00E9> <x>", TextNormalizer.Normalize(Text(e3), NormalizationForm.FormC));
            }
        }

        [Fact]
        public void A_regular_expression_skips_embedded_objects_like_the_plain_search_does()
        {
            var p = new Paragraph { DefaultWs = "en" };
            p.Runs.Add(new Run { Ws = "en", Text = "ab" });
            p.Runs.Add(Run.Object("en", ((char)8) + "pic"));
            p.Runs.Add(new Run { Ws = "en", Text = "cd" });
            var e = new DocumentEditor(Doc(p));
            Assert.Equal(new[] { new TextRange(0, 0, 5) }, e.FindAll(new FindOptions { Pattern = "abcd", UseRegularExpression = true }));
            Assert.Empty(e.FindAll(new FindOptions { Pattern = "b.c", UseRegularExpression = true }));   // the object is not a character to match
        }

        [Fact]
        public void Whole_word_treats_an_embedded_object_inside_a_word_as_ignorable_on_both_sides()
        {
            var p = new Paragraph { DefaultWs = "en" };
            p.Runs.Add(new Run { Ws = "en", Text = "cat" });
            p.Runs.Add(Run.Object("en", ((char)8) + "pic"));
            p.Runs.Add(new Run { Ws = "en", Text = "s" });
            var e = new DocumentEditor(Doc(p));
            Assert.Empty(e.FindAll(Opt("cat", wholeWord: true)));
            Assert.Single(e.FindAll(Opt("cats", wholeWord: true)));
        }

        [Fact]
        public void A_regular_expression_that_runs_too_long_matches_nothing_and_says_it_timed_out_in_every_search()
        {
            var e = One(new string('a', 40) + "!");
            var slow = new FindOptions { Pattern = "(a+)+$", UseRegularExpression = true, RegexTimeout = System.TimeSpan.FromMilliseconds(20) };
            Assert.Empty(e.FindAll(slow));
            Assert.True(e.FindTimedOut);
            Assert.False(e.TryFind(slow, true, true, out _, out _));
            Assert.True(e.FindTimedOut);
            Assert.Equal(0, e.ReplaceAll(slow, "x"));
            Assert.True(e.FindTimedOut);
            Assert.Empty(e.FindAllIn(0, slow));
            Assert.True(e.FindTimedOut);
            Assert.NotEmpty(e.FindAll(Opt("a")));
            Assert.False(e.FindTimedOut);   // the next search starts afresh
        }

        [Fact]
        public void A_timeout_anywhere_makes_every_search_return_nothing_and_Replace_All_change_nothing()
        {
            var e = new DocumentEditor(Doc(Para("en", "en", "x"), Para("en", "en", "x"), Para("en", "en", new string('a', 40) + "!"), Para("en", "en", "x")));
            var slow = new FindOptions { Pattern = "x|(a+)+$", UseRegularExpression = true, RegexTimeout = System.TimeSpan.FromMilliseconds(20) };
            Assert.Equal(0, e.ReplaceAll(slow, "Y"));
            Assert.True(e.FindTimedOut);
            Assert.Equal("x", Text(e, 0));
            Assert.Equal("x", Text(e, 1));
            Assert.Equal("x", Text(e, 3));
            Assert.Empty(e.FindAll(slow));
            Assert.Empty(e.FindAllIn(2, slow));
            e.SetCaret(new TextPosition(1, 1));   // the search reaches the slow paragraph before the match in the last one
            Assert.False(e.TryFind(slow, true, true, out _, out _));
        }

        [Fact]
        public void A_pattern_that_would_split_a_Hangul_syllable_matches_nothing_in_either_stored_form_and_a_whole_syllable_matches_in_both()
        {
            string syllable = ((char)0xD55C).ToString();                       // one precomposed syllable
            string jamo = new string(new[] { (char)0x1112, (char)0x1161, (char)0x11AB });   // the same syllable decomposed
            foreach (var stored in new[] { syllable, jamo })
            {
                var e = One("a" + stored + "b");
                foreach (var pattern in new[] { ((char)0x1112).ToString(), ((char)0x1161).ToString(), new string(new[] { (char)0x1112, (char)0x1161 }) })
                {
                    Assert.Empty(e.FindAll(Opt(pattern)));
                    Assert.Empty(e.FindAll(new FindOptions { Pattern = pattern, UseRegularExpression = true }));
                    Assert.Empty(e.FindAll(new FindOptions { Pattern = pattern, MatchDiacritics = false }));
                }
                Assert.Equal(new[] { new TextRange(0, 1, 1 + stored.Length) }, e.FindAll(Opt(jamo)));
                Assert.Equal(new[] { new TextRange(0, 1, 1 + stored.Length) }, e.FindAll(Opt(syllable)));
            }
        }
    }
}
