using System.Text;
using SIL.TextEditing.Markdown.Records;
using Xunit;

namespace SIL.TextEditing.Markdown.Tests.Records
{
    /// <summary>The reader's work grows with the file, not with its square (review of PR 147, P2-2). Work is counted (<c>RecordWork.Steps</c>), not timed.</summary>
    public class RecordScaleTests
    {
        static long WorkOf(string text)
        {
            RecordWork.Steps = 0;
            RecordFileReader.Read(text, RecordFixtures.Schema("test"));
            return RecordWork.Steps;
        }

        /// <summary>An entry of <paramref name="senses"/> senses, each with a block whose line has a tab after the block's indentation and a raw line
        /// separator, which make the reader look up the block a line is in.</summary>
        static string Entry(int senses, bool separators)
        {
            var sb = new StringBuilder("---\nfwmd: 1\nrecord: \"lexicon/entry\"\nrecord-data:\n  senses:\n");
            for (int i = 0; i < senses; i++)
                sb.Append("    - scientific-name: |-\n        x\n        \ty").Append(separators ? ((char)0x2028).ToString() : string.Empty).Append('\n');
            return sb.Append("---\n").ToString();
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void TabsInBlocksAndLineSeparatorsAreReadInLinearWork(bool separators)
        {
            long small = WorkOf(Entry(2_000, separators));
            long large = WorkOf(Entry(8_000, separators));
            Assert.True(small > 0);
            Assert.True(large < 8 * small, "steps: " + small + " for 2,000 blocks, " + large + " for 8,000");   // four times the blocks: about four times the work, not sixteen
        }

        [Fact]
        public void ManyWritingSystemsAreReadInLinearWork()
        {
            static string File(int count)
            {
                var sb = new StringBuilder("---\nfwmd: 1\nrecord: \"lexicon/entry\"\nwritingSystems:\n");
                for (int i = 0; i < count; i++) sb.Append("  w").Append(i).Append(":\n    lang: \"x\"\n");
                return sb.Append("---\n").ToString();
            }
            long small = WorkOf(File(2_000));
            long large = WorkOf(File(8_000));
            Assert.True(small > 0);
            Assert.True(large < 8 * small, "steps: " + small + " for 2,000 writing systems, " + large + " for 8,000");
        }
    }
}
