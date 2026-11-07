using System.Linq;
using Xunit;

namespace SIL.TextEditing.Tests
{
    public class GraphemeClusterTests
    {
        const string Cafe = "café"; // decomposed: e + combining acute
        const string Family = "a\U0001F468‍\U0001F469‍\U0001F467b"; // man ZWJ woman ZWJ girl between a and b

        [Fact]
        public void Empty_text_has_no_clusters()
        {
            Assert.Empty(GraphemeClusters.Starts(""));
            Assert.Equal(0, GraphemeClusters.Next("", 0));
            Assert.Equal(0, GraphemeClusters.Previous("", 0));
        }

        [Fact]
        public void Combining_mark_stays_with_its_base()
        {
            Assert.Equal(new[] { 0, 1, 2, 3 }, GraphemeClusters.Starts(Cafe).ToArray());
            Assert.Equal(5, GraphemeClusters.Next(Cafe, 3));
            Assert.Equal(3, GraphemeClusters.Previous(Cafe, 5));
        }

        [Fact]
        public void Surrogate_pair_is_one_cluster()
        {
            var text = "x\U0001F600y\U00020000z";
            Assert.Equal(new[] { 0, 1, 3, 4, 6 }, GraphemeClusters.Starts(text).ToArray());
        }

        [Fact]
        public void Zwj_sequence_is_one_cluster_on_every_runtime()
        {
            // a, then the whole family (8 UTF-16 units: 2+1+2+1+2), then b.
            Assert.Equal(new[] { 0, 1, 9 }, GraphemeClusters.Starts(Family).ToArray());
            Assert.Equal(9, GraphemeClusters.Next(Family, 1));
            Assert.Equal(1, GraphemeClusters.Previous(Family, 9));
        }

        // Expected cluster starts below come from UAX #29 (rules GB9, GB9a, GB9c, GB11, GB12, GB13), not from any
        // implementation. Every case must hold on net48 (netstandard2.0 build) and net10.0 alike.

        [Fact]
        public void Several_combining_marks_stay_with_their_base()
        {
            // a + acute + dot below + grave, then b (GB9).
            Assert.Equal(new[] { 0, 4 }, GraphemeClusters.Starts("a\u0301\u0323\u0300b").ToArray());
        }

        [Fact]
        public void Devanagari_conjunct_is_one_cluster()
        {
            // KA + VIRAMA + SSA (GB9c), then KA alone; a virama then a non-consonant does not fuse.
            Assert.Equal(new[] { 0, 3 }, GraphemeClusters.Starts("\u0915\u094D\u0937\u0915").ToArray());
            Assert.Equal(new[] { 0 }, GraphemeClusters.Starts("\u0915\u094D\u0937").ToArray());
            Assert.Equal(new[] { 0, 2 }, GraphemeClusters.Starts("\u0915\u094Da").ToArray());
        }

        [Fact]
        public void Devanagari_vowel_sign_is_part_of_the_cluster()
        {
            // KA + vowel sign AA (a spacing mark, GB9a), then KA.
            Assert.Equal(new[] { 0, 2 }, GraphemeClusters.Starts("\u0915\u093E\u0915").ToArray());
        }

        [Fact]
        public void Flag_pair_is_one_cluster_and_pairs_do_not_overlap()
        {
            string de = "\U0001F1E9\U0001F1EA", fr = "\U0001F1EB\U0001F1F7";
            Assert.Equal(new[] { 0 }, GraphemeClusters.Starts(de).ToArray());
            Assert.Equal(new[] { 0, 4 }, GraphemeClusters.Starts(de + fr).ToArray());
            // Three indicators: a pair and a single (GB12, GB13).
            Assert.Equal(new[] { 0, 4 }, GraphemeClusters.Starts(de + "\U0001F1EB").ToArray());
        }

        [Fact]
        public void Skin_tone_modifier_stays_with_its_emoji()
        {
            Assert.Equal(new[] { 0, 4 }, GraphemeClusters.Starts("\U0001F44D\U0001F3FDx").ToArray());
        }

        [Fact]
        public void Zwj_sequence_with_a_skin_tone_is_one_cluster()
        {
            // woman, skin tone, ZWJ, rocket (GB11 with an Extend in the middle).
            Assert.Equal(new[] { 0, 7 }, GraphemeClusters.Starts("\U0001F469\U0001F3FD\u200D\U0001F680x").ToArray());
        }

        [Fact]
        public void Zwj_before_a_non_pictograph_breaks_after_the_joiner()
        {
            // a ZWJ b: ZWJ extends a (GB9) but b starts a new cluster (GB11 needs a pictograph before the ZWJ).
            Assert.Equal(new[] { 0, 2 }, GraphemeClusters.Starts("a\u200Db").ToArray());
        }

        [Fact]
        public void Lone_surrogates_are_clusters_of_their_own()
        {
            Assert.Equal(new[] { 0, 1, 2 }, GraphemeClusters.Starts("a\uD83Db").ToArray()); // lone high
            Assert.Equal(new[] { 0, 1, 2 }, GraphemeClusters.Starts("a\uDE00b").ToArray()); // lone low
        }

        [Fact]
        public void Next_and_previous_clamp_at_the_ends()
        {
            Assert.Equal(1, GraphemeClusters.Next("ab", 0));
            Assert.Equal(2, GraphemeClusters.Next("ab", 2));
            Assert.Equal(0, GraphemeClusters.Previous("ab", 0));
            Assert.Equal(1, GraphemeClusters.Previous("ab", 2));
        }

        [Fact]
        public void Backspace_removes_one_code_point_and_never_half_a_surrogate_pair()
        {
            Assert.Equal(4, GraphemeClusters.BackspaceTarget(Cafe, 5)); // mark first, not the whole cluster
            Assert.Equal(1, GraphemeClusters.BackspaceTarget("a\U0001F600b", 3));
            Assert.Equal(0, GraphemeClusters.BackspaceTarget("abc", 0));
            Assert.Equal(0, GraphemeClusters.BackspaceTarget("a", 1));
        }
    }
}
