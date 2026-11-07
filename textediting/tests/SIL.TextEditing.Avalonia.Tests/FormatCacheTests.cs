using System.Collections.Generic;
using Xunit;
using static SIL.TextEditing.Tests.TestDocs;

namespace SIL.TextEditing.Avalonia.Tests
{
    /// <summary>One layout of a paragraph works each character format out once: the host's styles are asked once for each style and writing system, and runs that
    /// format alike share one set of text run properties, however many runs there are. Counted, not timed (issue 21).</summary>
    public class FormatCacheTests
    {
        sealed class CountingStyles : IStyleResolver
        {
            public int Asked;
            public PropertyBag? Resolve(string styleName, string ws)
            {
                Asked++;
                return styleName == "Body" ? new PropertyBag().SetInt(TextPropertyNumbers.FontSize, 11000, 1)
                    : styleName == "Emphasis" ? new PropertyBag().SetInt(TextPropertyNumbers.Italic, TextPropertyNumbers.ToggleOn) : null;
            }
        }

        static Paragraph Many(int runs)
        {
            var p = Para("en");
            p.Style = "Body";
            for (int i = 0; i < runs; i++)
            {
                var r = new Run { Ws = i % 2 == 0 ? "en" : "fr", Text = "word " };
                if (i % 4 < 2) r.Props.SetStr(TextPropertyNumbers.NamedStyle, "Emphasis");
                if (i % 3 == 0) r.Props.SetInt(TextPropertyNumbers.Bold, TextPropertyNumbers.ToggleOn);
                p.Runs.Add(r);
            }
            return p;
        }

        [Theory]
        [InlineData(40)]
        [InlineData(2000)]
        public void The_host_is_asked_once_per_style_and_writing_system_and_equal_runs_share_their_properties(int runs) => Headless.Run(() =>
        {
            var styles = new CountingStyles();
            var field = Fixtures.Field(Doc(Many(runs)), 400, f => f.StyleResolver = styles);
            var pl = field.ParagraphLayoutFor(0);
            // Two writing systems, a paragraph style and a character style: four lookups for the runs, and the paragraph's own format asks a few more, whatever the number of runs.
            Assert.True(styles.Asked <= 8, "the host was asked " + styles.Asked + " times for " + runs + " runs");
            Assert.True(pl.StyleLookups <= 4, "lookups " + pl.StyleLookups);
            Assert.True(pl.FormatsBuilt <= 8 + 2, "sets of run properties made: " + pl.FormatsBuilt + " for " + runs + " runs");
        });

        [Fact]
        public void Formats_that_differ_stay_apart() => Headless.Run(() =>
        {
            var field = Fixtures.Field(Doc(Many(12)), 400, f => f.StyleResolver = new CountingStyles());
            Assert.True(field.ParagraphLayoutFor(0).FormatsBuilt >= 6);   // en and fr, bold or not, emphasised or not: more than a single shared set
        });
    }
}
