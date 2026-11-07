using Xunit;

namespace SIL.TextEditing.Tests
{
    /// <summary>The language-service data shapes (ADR 0030): positions are the Language Server Protocol's, zero-based, in UTF-16 units.</summary>
    public class LanguageTextTests
    {
        [Theory]
        [InlineData("abc", 0, 0, 0)]
        [InlineData("abc", 2, 0, 2)]
        [InlineData("abc", 3, 0, 3)]
        [InlineData("ab\ncd", 3, 1, 0)]
        [InlineData("ab\ncd", 5, 1, 2)]
        [InlineData("ab\r\ncd", 4, 1, 0)]
        [InlineData("ab\r\ncd", 3, 0, 3)]   // between the CR and the LF it is still on the first line, past its end
        [InlineData("ab\rcd", 3, 1, 0)]
        [InlineData("a\u2028b", 2, 0, 2)]    // the hard line break is not a line end
        [InlineData("a\U0001F600b", 3, 0, 3)] // a surrogate pair is two units
        public void An_offset_gives_the_protocols_line_and_character(string text, int offset, int line, int character)
        {
            var p = LanguageText.PositionOf(text, offset);
            Assert.Equal(line, p.Line);
            Assert.Equal(character, p.Character);
        }

        [Theory]
        [InlineData("ab\ncd", 1, 1, 4)]
        [InlineData("ab\ncd", 0, 5, 2)]       // a character past the end of its line gives the end of the line
        [InlineData("ab\ncd", 9, 0, 5)]       // a line past the last gives the end of the text
        [InlineData("ab\r\ncd", 1, 0, 4)]
        [InlineData("abc", 0, 2, 2)]
        public void A_position_gives_an_offset_and_clamps(string text, int line, int character, int offset)
        {
            Assert.Equal(offset, LanguageText.OffsetOf(text, new LanguagePosition(line, character)));
        }

        [Fact]
        public void Offsets_and_positions_round_trip()
        {
            const string text = "one\ntwo\r\nthree\rfour";
            for (int i = 0; i <= text.Length; i++)
            {
                if (i == 8) continue;   // between CR and LF
                Assert.Equal(i, LanguageText.OffsetOf(text, LanguageText.PositionOf(text, i)));
            }
        }

        [Fact]
        public void A_range_of_offsets_is_on_line_zero_and_values_compare()
        {
            var r = new LanguageRange(2, 5);
            Assert.Equal(new LanguageRange(new LanguagePosition(0, 2), new LanguagePosition(0, 5)), r);
            Assert.False(r.IsEmpty);
            Assert.True(new LanguageRange(3, 3).IsEmpty);
            Assert.True(new LanguagePosition(0, 9).CompareTo(new LanguagePosition(1, 0)) < 0);
            Assert.Equal(new LanguageToken(r, "class", "k"), new LanguageToken(r, "class", "k"));
            Assert.NotEqual(new LanguageToken(r, "class"), new LanguageToken(r, "segment"));
        }
    }
}
