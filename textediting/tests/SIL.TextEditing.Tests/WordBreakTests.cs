using Xunit;

namespace SIL.TextEditing.Tests
{
    public class WordBreakTests
    {
        static string Word(string text, int at)
        {
            WordBreaks.WordAt(text, at, out int s, out int e);
            return text.Substring(s, e - s);
        }

        [Fact]
        public void A_double_click_takes_the_word_the_white_space_run_or_the_punctuation_run()
        {
            const string t = "Hello,  big_world!! 42";
            Assert.Equal("Hello", Word(t, 2));
            Assert.Equal(",", Word(t, 5));
            Assert.Equal("  ", Word(t, 7));
            Assert.Equal("big", Word(t, 9));              // an underscore is punctuation, as in FieldWorks and in the spell checker
            Assert.Equal("_", Word(t, 11));
            Assert.Equal("world", Word(t, 12));
            Assert.Equal("!!", Word(t, 17));
            Assert.Equal("42", Word(t, 21));
        }

        [Fact]
        public void An_apostrophe_between_letters_stays_in_the_word_but_a_quote_does_not()
        {
            Assert.Equal("don't", Word("say don't go", 6));
            Assert.Equal("don\u2019t", Word("say don\u2019t go", 6));
            Assert.Equal("go", Word("'go'", 1));
            Assert.Equal("'", Word("'go'", 0));
        }

        [Fact]
        public void Combining_marks_and_surrogate_pairs_stay_with_their_word_and_an_object_is_alone()
        {
            Assert.Equal("cafe\u0301 au", Word("cafe\u0301 au", 3) + " au");
            Assert.Equal("cafe\u0301", Word("cafe\u0301 au", 4));
            Assert.Equal("\U0001D4D0\U0001D4D1", Word("\U0001D4D0\U0001D4D1 x", 1));
            Assert.Equal("\uFFFC", Word("ab\uFFFC\uFFFCcd", 2));
        }

        [Fact]
        public void Arabic_words_split_at_spaces_and_the_text_edges_clamp()
        {
            const string t = "مرحبا بالعالم";
            Assert.Equal("مرحبا", Word(t, 2));
            Assert.Equal("بالعالم", Word(t, t.Length));
            Assert.Equal("", Word("", 0));
        }

        [Fact]
        public void Next_and_previous_word_start_skip_white_space_and_stop_at_the_ends()
        {
            const string t = "one  two, three";
            Assert.Equal(5, WordBreaks.NextWordStart(t, 0));
            Assert.Equal(5, WordBreaks.NextWordStart(t, 3));
            Assert.Equal(8, WordBreaks.NextWordStart(t, 5));
            Assert.Equal(10, WordBreaks.NextWordStart(t, 8));
            Assert.Equal(15, WordBreaks.NextWordStart(t, 10));
            Assert.Equal(15, WordBreaks.NextWordStart(t, 15));
            Assert.Equal(10, WordBreaks.PreviousWordStart(t, 15));
            Assert.Equal(8, WordBreaks.PreviousWordStart(t, 10));
            Assert.Equal(5, WordBreaks.PreviousWordStart(t, 8));
            Assert.Equal(5, WordBreaks.PreviousWordStart(t, 7));
            Assert.Equal(0, WordBreaks.PreviousWordStart(t, 5));
            Assert.Equal(0, WordBreaks.PreviousWordStart(t, 0));
        }
    }
}
