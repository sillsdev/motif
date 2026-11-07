using Xunit;
using static SIL.TextEditing.Tests.TestDocs;

namespace SIL.TextEditing.Avalonia.Tests
{
    /// <summary>A layout works out the runs of each line once (and a justified line's natural width once), whatever the line needs them for: its height with
    /// shifted runs, its caret and mouse, its justification, its hanging spaces. Counted, not timed (issues 66 and 44).</summary>
    public class LineWorkCountTests
    {
        const string Text = "The quick brown fox jumps over the lazy dog while the five boxing wizards jump quickly and a mad boxer shot a quick, gloved jab to the jaw of his dizzy opponent. ";

        [Theory]
        [InlineData(5, false)]   // justified
        [InlineData(5, true)]    // justified, with a raised run on the line
        [InlineData(0, true)]
        [InlineData(3, false)]
        public void Each_line_has_its_runs_worked_out_once(int align, bool raised) => Headless.Run(() =>
        {
            var p = Para("en", "en", Text + Text, "fr", "bonjour le monde " + Text, "en", Text);
            if (raised) p.Runs[1].Props.SetInt(TextPropertyNumbers.Offset, 3000, TextPropertyNumbers.VariationMilliPoint);
            p.Props.SetInt(TextPropertyNumbers.Align, align);
            ParagraphLayout pl;
            long built;
            using (var probe = LayoutProbe.Start())
            {
                pl = Fixtures.Field(Doc(p), 260).ParagraphLayoutFor(0);
                built = probe.RunSlotBuilds;
            }
            Assert.True(pl.Lines.Count > 8);
            Assert.True(built <= pl.Lines.Count, "run slots were worked out " + built + " times for " + pl.Lines.Count + " lines");
        });

        [Theory]
        [InlineData(0, 0)]
        [InlineData(0, -40000)]
        [InlineData(5, -40000)]
        public void A_list_paragraph_formats_each_line_once_the_first_beside_its_label_too(int align, int firstIndent) => Headless.Run(() =>
        {
            var p = Para("en", "en", Text + Text);
            p.Props.SetInt(TextPropertyNumbers.BulNumScheme, BulletNumbers.Arabic);
            p.Props.SetInt(TextPropertyNumbers.Align, align);
            p.Props.SetInt(TextPropertyNumbers.FirstIndent, firstIndent, 1);
            using var probe = LayoutProbe.Start();
            var pl = Fixtures.Field(Doc(p), 260).ParagraphLayoutFor(0);
            Assert.True(pl.LabelRect.Width > 0);
            Assert.Equal(pl.Lines.Count, (int)probe.LinesFormatted);
        });

        [Fact]
        public void The_work_counts_are_a_test_seam_nothing_counts_without_a_probe_and_another_thread_never_writes_one() => Headless.Run(() =>
        {
            Assert.Null(ParagraphLayout.Probe);
            Fixtures.Field(Doc(Para("en", "en", Text)), 260);   // lays out and counts nothing, and does not throw
            using (var probe = LayoutProbe.Start())
            {
                Fixtures.Field(Doc(Para("en", "en", Text)), 260);
                Assert.True(probe.LinesFormatted > 0);
                long seen = probe.LinesFormatted;
                // A probe belongs to the thread that started it: a field laid out on another thread (a second dispatcher) leaves its counts alone.
                LayoutProbe? other = probe;
                var thread = new System.Threading.Thread(() => { other = ParagraphLayout.Probe; ParagraphLayout.CountRangeProbes(1000); });
                thread.Start();
                thread.Join();
                Assert.Null(other);
                Assert.Equal(seen, probe.LinesFormatted);
                Assert.True(probe.RangeProbes < 1000);
            }
            Assert.Null(ParagraphLayout.Probe);
        });
    }
}
