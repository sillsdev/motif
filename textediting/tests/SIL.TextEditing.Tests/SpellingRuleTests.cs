using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SIL.TextEditing.Tests
{
    /// <summary>The scanner's rules one by one: what a word is, what is skipped, what a lone object does, and what a menu is given.</summary>
    public class SpellingRuleTests
    {
        static Paragraph En(string text) => TestDocs.Para("en", "en", text);
        static List<SpellingMark> Marks(Paragraph p, ISpellCheckHost host, bool readOnly = false, IStyleResolver? styles = null, int limit = int.MaxValue) => SpellScanner.Check(p, host, styles, readOnly, limit);
        static string[] Asked(FakeSpellHost h, string ws = "en") => h.Checkers[ws].Asked.ToArray();

        sealed class Styles : IStyleResolver
        {
            public readonly Dictionary<string, PropertyBag> Bags = new Dictionary<string, PropertyBag>();
            public PropertyBag? Resolve(string styleName, string ws) => Bags.TryGetValue(styleName, out var b) ? b : null;
        }

        static Paragraph WithObject(params object[] parts)
        {
            var p = new Paragraph { DefaultWs = "en" };
            foreach (var part in parts)
                p.Runs.Add(part is Run r ? r : new Run { Ws = "en", Text = (string)part });
            return p;
        }

        // ---- a lone object is not a word to mark (P2-1)

        [Fact]
        public void An_object_between_spaces_gets_no_mark()
        {
            var p = WithObject("hello ", Run.Object("en", "pic"), " world");
            Assert.Empty(Marks(p, new FakeSpellHost("hello", "world")));
        }

        [Fact]
        public void A_footnote_marker_of_an_object_after_a_zero_width_no_break_space_gets_no_mark()
        {
            var p = WithObject("hello﻿", Run.Object("en", "note"), " world");
            var host = new FakeSpellHost("hello", "world");
            Assert.Empty(Marks(p, host));
            Assert.Equal(new[] { "hello", "world" }, Asked(host));
        }

        [Fact]
        public void Objects_and_the_no_break_space_alone_make_no_word_even_side_by_side()
        {
            var p = WithObject(Run.Object("en", "a"), Run.Object("en", "b"), "﻿", Run.Object("en", "c"));
            Assert.Empty(Marks(p, new FakeSpellHost()));
        }

        [Fact]
        public void An_object_with_letters_beside_it_is_still_an_embedded_word()
        {
            var p = WithObject("ab", Run.Object("en", "x"));
            Assert.Equal(new[] { new SpellingMark(0, 3, SpellingProblem.Embedded) }, Marks(p, new FakeSpellHost()));
        }

        // ---- what a word is

        [Fact]
        public void Letters_of_other_scripts_form_words()
        {
            var p = TestDocs.Para("ar", "ar", "كتاب جديد");
            var host = new FakeSpellHost().With("ar");
            Marks(p, host);
            Assert.Equal(new[] { "كتاب", "جديد" }, Asked(host, "ar"));
        }

        [Fact]
        public void Modifier_letters_titlecase_letters_spacing_marks_and_modifier_symbols_stay_in_the_word()
        {
            foreach (var word in new[] { "kʰa", "ǅa", "हि", "a˜b" })
            {
                var host = new FakeSpellHost();
                Marks(En(word), host);
                Assert.Equal(new[] { word }, Asked(host));
            }
        }

        [Fact]
        public void Roman_numeral_and_other_number_characters_count_as_numbers_in_a_word_and_alone()
        {
            var host = new FakeSpellHost();
            Marks(En("aⅧ x²"), host);
            Assert.Equal(new[] { "aⅧ", "x²" }, Asked(host));
            var alone = new FakeSpellHost();
            Assert.Empty(Marks(En("Ⅷ ½"), alone));
            Assert.Empty(Asked(alone));
        }

        [Fact]
        public void A_zero_width_joiner_or_non_joiner_between_letters_stays_in_the_word()
        {
            var host = new FakeSpellHost();
            Marks(En("a‍b c‌d"), host);
            Assert.Equal(new[] { "a‍b", "c‌d" }, Asked(host));
        }

        [Fact]
        public void An_apostrophe_at_the_end_of_the_paragraph_or_before_a_space_ends_the_word_before_it()
        {
            var p = En("dogs'");
            var host = new FakeSpellHost();
            Assert.Equal(new[] { new SpellingMark(0, 4, SpellingProblem.Misspelled) }, Marks(p, host));
            var q = En("dogs' x");
            Assert.Equal(new[] { new SpellingMark(0, 4, SpellingProblem.Misspelled), new SpellingMark(6, 1, SpellingProblem.Misspelled) }, Marks(q, new FakeSpellHost()));
        }

        [Fact]
        public void A_host_can_turn_the_apostrophe_and_joiner_rule_off()
        {
            var host = new FakeSpellHost { Joiners = false };
            Marks(En("don't a‍b"), host);
            Assert.Equal(new[] { "don", "t", "a", "b" }, Asked(host));
        }

        [Fact]
        public void The_default_word_forming_rule_answers_for_every_code_point_without_throwing()
        {
            Assert.True(SpellScanner.DefaultIsWordForming('a'));
            Assert.False(SpellScanner.DefaultIsWordForming(' '));
            Assert.True(SpellScanner.DefaultIsWordForming(0x1D4D0));   // a supplementary-plane letter
            Assert.False(SpellScanner.DefaultIsWordForming(0xD800));   // a lone surrogate
            Assert.False(SpellScanner.DefaultIsWordForming(0xDFFF));
            Assert.False(SpellScanner.DefaultIsWordForming(-1));
            Assert.False(SpellScanner.DefaultIsWordForming(0x110000));
        }

        [Fact]
        public void A_lone_surrogate_in_the_text_ends_a_word_and_is_not_asked_about()
        {
            var host = new FakeSpellHost();
            Marks(En("ab\uD800cd"), host);
            Assert.Equal(new[] { "ab", "cd" }, Asked(host));
        }

        // ---- skip rules

        [Fact]
        public void A_paragraph_property_turns_checking_off_and_a_run_can_turn_it_back_on()
        {
            var p = TestDocs.Para("en", "en", "aaa ", "en", "bbb");
            p.Props.SetInt(TextPropertyNumbers.SpellCheck, TextPropertyNumbers.SpellCheckDoNot);
            Assert.Empty(Marks(p, new FakeSpellHost()));
            p.Runs[1].Props.SetInt(TextPropertyNumbers.SpellCheck, TextPropertyNumbers.SpellCheckNormal);
            Assert.Equal(new[] { new SpellingMark(4, 3, SpellingProblem.Misspelled) }, Marks(p, new FakeSpellHost()));
        }

        [Fact]
        public void A_paragraph_style_named_in_the_paragraph_properties_gives_the_property_when_the_style_field_is_empty()
        {
            var styles = new Styles();
            styles.Bags["Quiet"] = new PropertyBag().SetInt(TextPropertyNumbers.SpellCheck, TextPropertyNumbers.SpellCheckDoNot);
            var p = En("zzz");
            p.Props.SetStr(TextPropertyNumbers.NamedStyle, "Quiet");
            Assert.Empty(Marks(p, new FakeSpellHost(), styles: styles));
            Assert.Single(Marks(p, new FakeSpellHost()));   // without the host's styles the name means nothing
        }

        [Fact]
        public void A_run_property_wins_over_the_paragraph_and_a_character_style_wins_over_the_paragraph_style()
        {
            var styles = new Styles();
            styles.Bags["Para"] = new PropertyBag().SetInt(TextPropertyNumbers.SpellCheck, TextPropertyNumbers.SpellCheckDoNot);
            styles.Bags["Char"] = new PropertyBag().SetInt(TextPropertyNumbers.SpellCheck, TextPropertyNumbers.SpellCheckNormal);
            var p = TestDocs.Para("en", "en", "aaa ", "en", "bbb");
            p.Style = "Para";
            p.Runs[1].Props.SetStr(TextPropertyNumbers.NamedStyle, "Char");
            var marks = Marks(p, new FakeSpellHost(), styles: styles);
            Assert.Equal(new[] { new SpellingMark(4, 3, SpellingProblem.Misspelled) }, marks);
        }

        [Fact]
        public void An_unchecked_start_of_a_word_is_cut_off_as_the_end_is()
        {
            var p = TestDocs.Para("en", "en", "12", "en", "good");
            p.Runs[0].Props.SetInt(TextPropertyNumbers.SpellCheck, TextPropertyNumbers.SpellCheckDoNot);
            var host = new FakeSpellHost("good");
            Assert.Empty(Marks(p, host));
            Assert.Equal(new[] { "good" }, Asked(host));
            var bad = TestDocs.Para("en", "en", "12", "en", "baad");
            bad.Runs[0].Props.SetInt(TextPropertyNumbers.SpellCheck, TextPropertyNumbers.SpellCheckDoNot);
            Assert.Equal(new[] { new SpellingMark(2, 4, SpellingProblem.Misspelled) }, Marks(bad, new FakeSpellHost()));
        }

        // ---- mixed and embedded words

        [Fact]
        public void An_object_whose_writing_system_differs_at_the_start_does_not_make_the_word_mixed()
        {
            var p = WithObject(Run.Object("fr", "pic"), "abc");
            Assert.Equal(new[] { new SpellingMark(0, 4, SpellingProblem.Embedded) }, Marks(p, new FakeSpellHost()));
        }

        [Fact]
        public void An_object_in_another_writing_system_inside_a_word_makes_it_embedded_not_mixed()
        {
            var p = WithObject("ab", Run.Object("fr", "pic"), "cd");
            Assert.Equal(new[] { new SpellingMark(0, 5, SpellingProblem.Embedded) }, Marks(p, new FakeSpellHost()));
        }

        [Fact]
        public void A_mixed_word_with_an_object_inside_is_mixed()
        {
            var p = new Paragraph { DefaultWs = "en" };
            p.Runs.Add(new Run { Ws = "en", Text = "ab" });
            p.Runs.Add(Run.Object("en", "pic"));
            p.Runs.Add(new Run { Ws = "fr", Text = "cd" });
            Assert.Equal(new[] { new SpellingMark(0, 5, SpellingProblem.Mixed) }, Marks(p, new FakeSpellHost()));
        }

        // ---- text a line limit hides (P3-9)

        [Fact]
        public void Words_that_start_at_or_after_the_limit_are_not_checked_and_one_that_crosses_it_is()
        {
            var p = En("aa bb cc dd");
            var host = new FakeSpellHost();
            var marks = Marks(p, host, limit: 6);   // "aa bb " shows; "cc" starts at 6
            Assert.Equal(new[] { "aa", "bb" }, Asked(host));
            Assert.Equal(2, marks.Count);
            var crossing = new FakeSpellHost();
            Marks(p, crossing, limit: 7);   // "cc" starts before 7, so it is checked whole
            Assert.Equal(new[] { "aa", "bb", "cc" }, Asked(crossing));
        }

        // ---- suggestions: what the menu is given

        [Fact]
        public void Suggestions_are_asked_for_only_the_word_at_the_offset()
        {
            var host = new FakeSpellHost();
            var p = En("one two three four");
            var s = SpellScanner.SuggestionsAt(p, 0, 5, host);
            Assert.Equal("two", s!.Word);
            Assert.Equal(new[] { "two" }, Asked(host));   // not one, not three, not four
        }

        [Fact]
        public void A_word_end_is_inside_the_word_and_the_offset_after_the_gap_is_not()
        {
            var host = new FakeSpellHost("fine");
            var p = En("wrnog  fine");
            Assert.NotNull(SpellScanner.SuggestionsAt(p, 0, 5, host));
            Assert.Null(SpellScanner.SuggestionsAt(p, 0, 6, host));
            Assert.NotNull(SpellScanner.SuggestionsAt(p, 0, 0, host));
        }

        [Fact]
        public void The_word_of_a_mixed_suggestion_is_in_NFC_like_the_others()
        {
            var p = TestDocs.Para("en", "en", "café", "fr", "x");
            var s = SpellScanner.SuggestionsAt(p, 0, 1, new FakeSpellHost())!;
            Assert.Equal(SpellingProblem.Mixed, s.Problem);
            Assert.Equal("caféx", s.Word);
        }

        [Fact]
        public void An_embedded_word_cannot_be_added_or_ignored()
        {
            var p = WithObject("ab", Run.Object("en", "x"));
            var s = SpellScanner.SuggestionsAt(p, 0, 1, new FakeSpellHost())!;
            Assert.Equal(SpellingProblem.Embedded, s.Problem);
            Assert.False(s.CanAddToDictionary);
            Assert.False(s.CanIgnore);
            Assert.False(s.AddToDictionary());
            Assert.False(s.IgnoreWord());
        }

        [Fact]
        public void Ignoring_needs_a_checker_that_can()
        {
            var bare = new FakeBareChecker();
            var host = new FakeSpellHost();
            host.Custom["en"] = bare;
            var s = SpellScanner.SuggestionsAt(En("zzz"), 0, 1, host)!;
            Assert.True(s.CanAddToDictionary);
            Assert.False(s.CanIgnore);
            Assert.False(s.IgnoreWord());
            Assert.True(s.AddToDictionary());
            Assert.Equal(new[] { "zzz" }, bare.Added);
            var ignoring = new FakeSpellHost();
            var t = SpellScanner.SuggestionsAt(En("zzz"), 0, 1, ignoring)!;
            Assert.True(t.CanIgnore);
            Assert.True(t.IgnoreWord());
            Assert.Equal(new[] { "zzz" }, ignoring.Checkers["en"].Ignored);
        }

        // ---- the parts of a word, for a replacement that keeps them (P2-2)

        [Fact]
        public void A_mixed_word_gives_its_parts_with_their_writing_systems_and_spaces_between_them()
        {
            var p = TestDocs.Para("en", "en", "ab", "fr", "cd");
            var s = SpellScanner.SuggestionsAt(p, 0, 1, new FakeSpellHost())!;
            Assert.Equal(new[] { "ab", "cd" }, s.Parts.Select(r => r.Text));
            var runs = s.WithMissingSpaces();
            Assert.Equal(new[] { ("en", "ab"), ("en", " "), ("fr", "cd") }, runs.Select(r => (r.Ws, r.Text)));
        }

        [Fact]
        public void An_embedded_word_gives_its_object_with_spaces_around_it()
        {
            var p = WithObject("ab", Run.Object("en", "pic"), "cd");
            var s = SpellScanner.SuggestionsAt(p, 0, 1, new FakeSpellHost())!;
            var runs = s.WithMissingSpaces();
            Assert.Equal(new[] { "ab", " ", "￼", " ", "cd" }, runs.Select(r => r.Text));
            Assert.Equal("pic", runs[2].ObjData);
        }

        [Fact]
        public void Parts_of_the_same_writing_system_and_checking_get_no_space()
        {
            var p = TestDocs.Para("en", "en", "ab", "en", "cd");
            p.Runs[1].Props.SetInt(TextPropertyNumbers.Bold, 1);
            var s = SpellScanner.SuggestionsAt(p, 0, 1, new FakeSpellHost())!;
            Assert.Equal(new[] { "ab", "cd" }, s.WithMissingSpaces().Select(r => r.Text));
        }

        [Fact]
        public void A_part_that_is_not_checked_is_separated_from_the_checked_text_beside_it()
        {
            var p = TestDocs.Para("en", "en", "ab", "en", "12", "en", "cd");
            p.Runs[1].Props.SetInt(TextPropertyNumbers.SpellCheck, TextPropertyNumbers.SpellCheckDoNot);
            var s = SpellScanner.SuggestionsAt(p, 0, 1, new FakeSpellHost())!;
            Assert.Equal(new[] { "ab", " ", "12", " ", "cd" }, s.WithMissingSpaces().Select(r => r.Text));
        }

        [Fact]
        public void A_part_that_may_not_be_edited_is_separated_from_the_editable_text_beside_it()
        {
            var p = TestDocs.Para("en", "en", "ab", "en", "12", "en", "cd");
            p.Runs[1].Props.SetInt(TextPropertyNumbers.Editable, TextPropertyNumbers.EditableNo);
            var s = SpellScanner.SuggestionsAt(p, 0, 1, new FakeSpellHost())!;
            Assert.Equal(SpellingProblem.Mixed, s.Problem);
            Assert.Equal(new[] { "ab", " ", "12", " ", "cd" }, s.WithMissingSpaces().Select(r => r.Text));
        }

        [Fact]
        public void Editable_and_semi_editable_parts_are_both_checked_but_the_rule_still_puts_a_space_between_them()
        {
            var p = TestDocs.Para("en", "en", "ab", "en", "cd");
            p.Runs[1].Props.SetInt(TextPropertyNumbers.Editable, TextPropertyNumbers.EditableSemi);
            var s = SpellScanner.SuggestionsAt(p, 0, 1, new FakeSpellHost())!;
            Assert.Equal(SpellingProblem.Misspelled, s.Problem);   // one checked word, so the dictionary was asked
            Assert.Equal(new[] { "ab", " ", "cd" }, s.WithMissingSpaces().Select(r => r.Text));
        }
    }
}
