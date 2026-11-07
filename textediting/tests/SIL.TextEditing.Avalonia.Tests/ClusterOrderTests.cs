using System.Linq;
using Xunit;
using static SIL.TextEditing.Tests.TestDocs;

namespace SIL.TextEditing.Avalonia.Tests
{
    /// <summary>Avalonia 12 gives the spaces a wrapped line ends with, after a run of Arabic in a left-to-right paragraph, glyph clusters that fall
    /// from one glyph to the next although the run says it runs left to right, and answers distances and hits for it that do not add up (a click at
    /// the end of the line lands one space short). <c>RunSlot</c> places such a run of spaces by counting them (<see cref="SpacedArabicClickTests"/>).</summary>
    public class ClusterOrderTests
    {
        [Fact]
        public void Avalonia_12_numbers_the_clusters_of_trailing_spaces_after_a_right_to_left_run_against_their_direction_and_11_does_not() => Headless.Run(() =>
        {
            // If the 12 case fails, Avalonia fixed it: delete RunSlot.ClustersAgainstDirection and the counting it selects, and this test.
            var p = Para("en", "en", "12 فقرة   طويلة عربية   12 ab (جمل) ");
            p.Props.SetInt(TextPropertyNumbers.Align, 2);
            var li = Fixtures.Field(Doc(p), 175).ParagraphLayoutFor(0).Lines[0];
            var tail = ParagraphLayout.RunSlots(li.Line).Last();
            Assert.True(tail.IsSpaces && tail.Length == 3);
            Assert.Equal(Headless.AvaloniaMajor >= 12, tail.ClustersAgainstDirection);
        });
    }
}
