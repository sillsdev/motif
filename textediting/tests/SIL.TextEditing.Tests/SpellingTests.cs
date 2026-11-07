using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SIL.TextEditing.Tests
{
    public class SpellingTests
    {
        static List<SpellingMark> Marks(Paragraph p, ISpellCheckHost host, bool readOnly = false, IStyleResolver? styles = null) => SpellScanner.Check(p, host, styles, readOnly);
        static string[] Words(Paragraph p, List<SpellingMark> marks) => marks.Select(m => p.Text.Substring(m.Start, m.Length)).ToArray();
        static Paragraph En(string text) => TestDocs.Para("en", "en", text);

        [Fact]
        public void Words_the_dictionary_lacks_are_marked_and_known_words_are_not()
        {
            var p = En("The cat sat on teh mat.");
            var marks = Marks(p, new FakeSpellHost("The", "cat", "sat", "on", "mat"));
            Assert.Equal(new[] { "teh" }, Words(p, marks));
            Assert.Equal(SpellingProblem.Misspelled, marks[0].Problem);
        }

        [Fact]
        public void A_writing_system_with_no_dictionary_is_not_checked()
        {
            var p = TestDocs.Para("en", "en", "good ", "fr", "mauvais");
            Assert.Empty(Marks(p, new FakeSpellHost("good")));
        }

        [Fact]
        public void The_word_goes_to_the_checker_of_its_own_writing_system_in_NFC()
        {
            var p = TestDocs.Para("en", "en", "hello ", "fr", "café");
            var host = new FakeSpellHost("hello").With("fr", "café");
            Assert.Empty(Marks(p, host));
            Assert.Equal(new[] { "café" }, host.Checkers["fr"].Asked);
            Assert.Equal(new[] { "hello" }, host.Checkers["en"].Asked);
        }

        [Fact]
        public void A_word_across_two_runs_of_one_writing_system_is_one_word()
        {
            var p = TestDocs.Para("en", "en", "wor", "en", "ld");
            p.Runs[1].Props.SetInt(TextPropertyNumbers.Bold, 1);
            var host = new FakeSpellHost("world");
            Assert.Empty(Marks(p, host));
            Assert.Equal(new[] { "world" }, host.Checkers["en"].Asked);
        }

        [Fact]
        public void Punctuation_and_hyphens_end_words_but_an_apostrophe_between_letters_does_not()
        {
            var p = En("don't well-known 'quoted' d’accord");
            var host = new FakeSpellHost("don't", "well", "known", "quoted", "d’accord");
            Assert.Empty(Marks(p, host));
            Assert.Equal(new[] { "don't", "well", "known", "quoted", "d’accord" }, host.Checkers["en"].Asked);
        }

        [Fact]
        public void A_writing_system_can_make_the_apostrophe_or_hyphen_a_letter()
        {
            var host = new FakeSpellHost("well-known", "'tis");
            host.WordForming.Add('-');
            host.WordForming.Add('\'');
            Assert.Empty(Marks(En("well-known 'tis"), host));
            Assert.Equal(new[] { "well-known", "'tis" }, host.Checkers["en"].Asked);
        }

        [Fact]
        public void Numbers_count_as_word_forming_so_a_word_with_digits_is_one_word_but_numbers_alone_are_not_asked()
        {
            var p = En("abc123 4567 x2");
            var host = new FakeSpellHost("abc123", "x2");
            Assert.Empty(Marks(p, host));
            Assert.Equal(new[] { "abc123", "x2" }, host.Checkers["en"].Asked);
        }

        [Fact]
        public void An_all_capitals_word_is_checked_like_any_other()
        {
            var p = En("NASA nasa");
            var marks = Marks(p, new FakeSpellHost("nasa"));
            Assert.Equal(new[] { "NASA" }, Words(p, marks));
        }

        [Fact]
        public void The_marks_keep_their_offsets_in_a_paragraph_of_several_runs()
        {
            var p = TestDocs.Para("en", "en", "ok ", "en", "bad", "fr", " ok");
            var marks = Marks(p, new FakeSpellHost("ok").With("fr", "ok"));
            Assert.Single(marks);
            Assert.Equal(3, marks[0].Start);
            Assert.Equal(3, marks[0].Length);
        }

        [Fact]
        public void Text_marked_do_not_check_is_skipped()
        {
            var p = TestDocs.Para("en", "en", "bad ", "en", "worse ", "en", "bad");
            p.Runs[1].Props.SetInt(TextPropertyNumbers.SpellCheck, TextPropertyNumbers.SpellCheckDoNot);
            var marks = Marks(p, new FakeSpellHost());
            Assert.Equal(new[] { "bad", "bad" }, Words(p, marks));
        }

        [Fact]
        public void Text_that_may_not_be_edited_is_skipped_unless_the_run_forces_checking()
        {
            var p = TestDocs.Para("en", "en", "aaa ", "en", "bbb ", "en", "ccc ", "en", "ddd");
            p.Runs[1].Props.SetInt(TextPropertyNumbers.Editable, TextPropertyNumbers.EditableNo);
            p.Runs[2].Props.SetInt(TextPropertyNumbers.Editable, TextPropertyNumbers.EditableNo).SetInt(TextPropertyNumbers.SpellCheck, TextPropertyNumbers.SpellCheckForce);
            p.Runs[3].Props.SetInt(TextPropertyNumbers.Editable, TextPropertyNumbers.EditableSemi);
            Assert.Equal(new[] { "aaa", "ccc", "ddd" }, Words(p, Marks(p, new FakeSpellHost())));
        }

        [Fact]
        public void A_read_only_field_checks_only_the_runs_that_force_checking()
        {
            var p = TestDocs.Para("en", "en", "aaa ", "en", "bbb");
            p.Runs[1].Props.SetInt(TextPropertyNumbers.SpellCheck, TextPropertyNumbers.SpellCheckForce);
            Assert.Equal(new[] { "bbb" }, Words(p, Marks(p, new FakeSpellHost(), readOnly: true)));
        }

        [Fact]
        public void The_spell_check_property_comes_from_a_style_and_the_run_overrides_it()
        {
            var styles = new FakeStyles();
            styles.Bags["Quiet"] = new PropertyBag().SetInt(TextPropertyNumbers.SpellCheck, TextPropertyNumbers.SpellCheckDoNot);
            var p = TestDocs.Para("en", "en", "aaa ", "en", "bbb ", "en", "ccc");
            p.Runs[0].Props.SetStr(TextPropertyNumbers.NamedStyle, "Quiet");
            p.Runs[1].Props.SetStr(TextPropertyNumbers.NamedStyle, "Quiet").SetInt(TextPropertyNumbers.SpellCheck, TextPropertyNumbers.SpellCheckNormal);
            Assert.Equal(new[] { "bbb", "ccc" }, Words(p, Marks(p, new FakeSpellHost(), styles: styles)));
            var para = TestDocs.Para("en", "en", "zzz");
            para.Style = "Quiet";
            Assert.Empty(Marks(para, new FakeSpellHost(), styles: styles));
        }

        sealed class FakeStyles : IStyleResolver
        {
            public readonly Dictionary<string, PropertyBag> Bags = new Dictionary<string, PropertyBag>();
            public PropertyBag? Resolve(string styleName, string ws) => Bags.TryGetValue(styleName, out var b) ? b : null;
        }

        [Fact]
        public void An_unchecked_edge_is_cut_off_and_the_rest_is_checked_alone()
        {
            // "good" followed by a do-not-check number run: the word is "good" only.
            var p = TestDocs.Para("en", "en", "good", "en", "12");
            p.Runs[1].Props.SetInt(TextPropertyNumbers.SpellCheck, TextPropertyNumbers.SpellCheckDoNot);
            var host = new FakeSpellHost("good");
            Assert.Empty(Marks(p, host));
            Assert.Equal(new[] { "good" }, host.Checkers["en"].Asked);
        }

        [Fact]
        public void A_word_with_unchecked_text_inside_is_marked_mixed_without_asking_the_dictionary()
        {
            var p = TestDocs.Para("en", "en", "ab", "en", "12", "en", "cd");
            p.Runs[1].Props.SetInt(TextPropertyNumbers.SpellCheck, TextPropertyNumbers.SpellCheckDoNot);
            var host = new FakeSpellHost();
            var marks = Marks(p, host);
            Assert.Equal(new[] { new SpellingMark(0, 6, SpellingProblem.Mixed) }, marks);
            Assert.Empty(host.Checkers["en"].Asked);
        }

        [Fact]
        public void A_word_in_two_writing_systems_is_marked_mixed_even_with_no_dictionaries()
        {
            var p = TestDocs.Para("en", "en", "ab", "fr", "cd", "en", " ok");
            var marks = Marks(p, new FakeSpellHost("ok"));
            Assert.Equal(new[] { new SpellingMark(0, 4, SpellingProblem.Mixed) }, marks);
        }

        [Fact]
        public void An_embedded_object_in_a_word_marks_the_word_embedded_and_a_lone_object_marks_nothing_but_its_own_word()
        {
            var p = new Paragraph { DefaultWs = "en" };
            p.Runs.Add(new Run { Ws = "en", Text = "ab" });
            p.Runs.Add(Run.Object("en", "pic"));
            p.Runs.Add(new Run { Ws = "en", Text = "cd ok" });
            var host = new FakeSpellHost("ok");
            var marks = Marks(p, host);
            Assert.Equal(new[] { new SpellingMark(0, 5, SpellingProblem.Embedded) }, marks);
            Assert.DoesNotContain(host.Checkers["en"].Asked, a => a.Contains("￼"));
        }

        [Fact]
        public void A_surrogate_pair_letter_is_word_forming()
        {
            var p = En("\U0001D4D0\U0001D4D1 x");
            var host = new FakeSpellHost("x");
            var marks = Marks(p, host);
            Assert.Equal(new[] { new SpellingMark(0, 4, SpellingProblem.Misspelled) }, marks);
        }

        [Fact]
        public void An_empty_paragraph_has_no_marks()
        {
            Assert.Empty(Marks(new Paragraph { DefaultWs = "en" }, new FakeSpellHost()));
        }

        [Fact]
        public void Suggestions_are_for_the_word_at_the_offset_and_split_at_seven()
        {
            var p = En("fine wrnog fine");
            var host = new FakeSpellHost("fine");
            host.Checkers["en"].Suggestions = Enumerable.Range(1, 10).Select(i => "s" + i).ToList();
            var s = SpellScanner.SuggestionsAt(p, 3, 7, host);
            Assert.NotNull(s);
            Assert.Equal("wrnog", s!.Word);
            Assert.Equal(new TextPosition(3, 5), s.Start);
            Assert.Equal(new TextPosition(3, 10), s.End);
            Assert.Equal(7, s.Inline.Count());
            Assert.Equal(new[] { "s8", "s9", "s10" }, s.Additional.ToArray());
            Assert.True(s.CanAddToDictionary);
            Assert.Null(SpellScanner.SuggestionsAt(p, 0, 1, host));
        }

        [Fact]
        public void A_wrong_word_with_no_suggestions_still_answers_with_an_empty_list()
        {
            var s = SpellScanner.SuggestionsAt(En("zzz"), 0, 0, new FakeSpellHost());
            Assert.NotNull(s);
            Assert.Empty(s!.All);
        }

        [Fact]
        public void A_mixed_or_embedded_word_offers_no_dictionary_add()
        {
            var p = TestDocs.Para("en", "en", "ab", "fr", "cd");
            var s = SpellScanner.SuggestionsAt(p, 0, 1, new FakeSpellHost());
            Assert.NotNull(s);
            Assert.Equal(SpellingProblem.Mixed, s!.Problem);
            Assert.False(s.CanAddToDictionary);
            Assert.Empty(s.All);
        }

        sealed class OnlyChecker : SpellCheckHostBase
        {
            public readonly FakeChecker Checker = new FakeChecker();
            public override ISpellChecker? GetChecker(string ws) => ws == "en" ? Checker : null;
        }

        [Fact]
        public void A_host_derived_from_the_base_writes_only_GetChecker_and_gets_the_default_word_rules()
        {
            var host = new OnlyChecker();
            host.Checker.Known.Add("don't");
            Assert.True(host.IsWordForming("en", 'a'));
            Assert.False(host.IsWordForming("en", ' '));
            Assert.True(host.JoinsWords("en", (char)39));
            Assert.False(host.JoinsWords("en", 'a'));
            Assert.Empty(SpellScanner.Check(TestDocs.Para("en", "en", "don't"), host));
            Assert.Single(SpellScanner.Check(TestDocs.Para("en", "en", "dont"), host));
        }

        [Fact]
        public async System.Threading.Tasks.Task The_base_raises_dictionaries_changed_to_every_subscriber_from_any_thread()
        {
            var host = new OnlyChecker();
            int a = 0, b = 0;
            host.RaiseDictionariesChanged();   // nobody listening
            EventHandler one = (s, e) => { a++; Assert.Same(host, s); };
            EventHandler two = (s, e) => b++;
            host.DictionariesChanged += one;
            host.DictionariesChanged += two;
            host.RaiseDictionariesChanged();
            await System.Threading.Tasks.Task.Run(() => host.RaiseDictionariesChanged());
            host.DictionariesChanged -= one;
            host.RaiseDictionariesChanged();
            Assert.Equal(2, a);
            Assert.Equal(3, b);
        }
    }
}
