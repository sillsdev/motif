using Xunit;

namespace SIL.TextEditing.Tests
{
    /// <summary>The bullet scheme numbers are stored in documents, so what each draws is persisted compatibility data (ADR 0010).</summary>
    public class BulletNumberingTests
    {
        // Scheme number and the code point it draws. Schemes 116 and 117, and 121 and 122, draw the same glyph on purpose:
        // both numbers exist in stored documents.
        public static readonly int[][] Schemes =
        {
            new[] { 100, 0x00B7 }, new[] { 101, 0x2022 }, new[] { 102, 0x25CF }, new[] { 103, 0x274D }, new[] { 104, 0x25AA },
            new[] { 105, 0x25A0 }, new[] { 106, 0x25AB }, new[] { 107, 0x25A1 }, new[] { 108, 0x2751 }, new[] { 109, 0x2752 },
            new[] { 110, 0x2B27 }, new[] { 111, 0x29EB }, new[] { 112, 0x25C6 }, new[] { 113, 0x2756 }, new[] { 114, 0x2318 },
            new[] { 115, 0x261E }, new[] { 116, 0x271E }, new[] { 117, 0x271E }, new[] { 118, 0x2730 }, new[] { 119, 0x27A2 },
            new[] { 120, 0x27B2 }, new[] { 121, 0x2794 }, new[] { 122, 0x2794 }, new[] { 123, 0x21E8 }, new[] { 124, 0x2713 },
        };

        [Fact]
        public void Every_bullet_scheme_number_draws_the_glyph_it_always_has_including_the_deliberate_duplicates()
        {
            Assert.Equal(25, Schemes.Length);
            foreach (var pair in Schemes)
            {
                Assert.True(BulletNumbers.IsBullet(pair[0]), "scheme " + pair[0]);
                Assert.Equal(pair[1], BulletNumbers.BulletGlyph(pair[0]));
                Assert.Equal(char.ConvertFromUtf32(pair[1]), BulletNumbers.Label(new ParaFormat { BulNumScheme = pair[0] }, 0));
            }
            Assert.Equal(BulletNumbers.BulletGlyph(116), BulletNumbers.BulletGlyph(117));
            Assert.Equal(BulletNumbers.BulletGlyph(121), BulletNumbers.BulletGlyph(122));
            foreach (int notBullet in new[] { 0, 10, 15, 99, 125, 1000 })
            {
                Assert.False(BulletNumbers.IsBullet(notBullet), "scheme " + notBullet);
                Assert.Equal(0, BulletNumbers.BulletGlyph(notBullet));
            }
            Assert.Equal("Quivira", BulletNumbers.BulletFont);
        }

        [Fact]
        public void Roman_numerals_follow_the_standard_rule_up_to_14999_and_become_plain_digits_beyond_it()
        {
            Assert.Equal("i", BulletNumbers.Number(BulletNumbers.RomanLower, 1));
            Assert.Equal("iv", BulletNumbers.Number(BulletNumbers.RomanLower, 4));
            Assert.Equal("xlix", BulletNumbers.Number(BulletNumbers.RomanLower, 49));
            Assert.Equal("CMXCIX", BulletNumbers.Number(BulletNumbers.RomanUpper, 999));
            Assert.Equal("MCMXCIV", BulletNumbers.Number(BulletNumbers.RomanUpper, 1994));
            Assert.Equal("MMMM", BulletNumbers.Number(BulletNumbers.RomanUpper, 4000));
            Assert.Equal(new string('M', 14) + "CMXCIX", BulletNumbers.Number(BulletNumbers.RomanUpper, 14999));
            Assert.Equal("15000", BulletNumbers.Number(BulletNumbers.RomanUpper, 15000));
            Assert.Equal("0", BulletNumbers.Number(BulletNumbers.RomanLower, 0));
        }

        [Fact]
        public void Letters_repeat_to_count_past_26_and_start_over_after_780()
        {
            Assert.Equal("a", BulletNumbers.Number(BulletNumbers.LetterLower, 1));
            Assert.Equal("Z", BulletNumbers.Number(BulletNumbers.LetterUpper, 26));
            Assert.Equal("AA", BulletNumbers.Number(BulletNumbers.LetterUpper, 27));
            Assert.Equal("ZZ", BulletNumbers.Number(BulletNumbers.LetterUpper, 52));
            Assert.Equal("AAA", BulletNumbers.Number(BulletNumbers.LetterUpper, 53));
            Assert.Equal(new string('Z', 30), BulletNumbers.Number(BulletNumbers.LetterUpper, 780));
            Assert.Equal("A", BulletNumbers.Number(BulletNumbers.LetterUpper, 781));
            Assert.Equal("B", BulletNumbers.Number(BulletNumbers.LetterUpper, 782));
            Assert.Equal("0", BulletNumbers.Number(BulletNumbers.LetterUpper, 0));
        }
    }
}
