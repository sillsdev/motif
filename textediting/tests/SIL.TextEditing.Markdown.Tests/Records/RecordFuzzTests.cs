using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SIL.TextEditing.Markdown.Records;
using Xunit;

namespace SIL.TextEditing.Markdown.Tests.Records
{
    /// <summary>A seeded mutation fuzz over the canonical record fixtures (re-review of PR 147, P2-3): one to four edits from an alphabet YAML treats
    /// specially. The reader never throws, its counted work stays proportional to the file, and a file it reads without an error can be written and read back
    /// without one. A failure prints its seed and the file, so it is reproduced.</summary>
    public class RecordFuzzTests
    {
        static readonly string[] Alphabet =
        {
            "[", "]", "{", "}", ":", ": ", "-", "- ", "#", " #", "&", "*", "!", "!!str ", "|", "|-", ">", "'", "\"", "\\", "%", "@", "`", "?", ",", "~",
            "\t", "\n", "\r\n", " ", "  ", "---", "...", "\u2028", "\u0085", "\u0000", "\uFEFF", "<<: ", "\\u00e9", "\\t", "\\N", "no", "1.10", "x",
        };

        public static IEnumerable<object[]> Batches()
        {
            int count = int.TryParse(Environment.GetEnvironmentVariable("TEXTEDITING_RECORD_FUZZ"), out int n) ? n : 3000;
            for (int batch = 0; batch < count / 100; batch++) yield return new object[] { batch };
        }

        [Theory]
        [MemberData(nameof(Batches))]
        public void AMutatedRecordFileIsReadWithFindingsAndNeverThrows(int batch)
        {
            var seeds = RecordFixtures.All().Where(c => c.CanonicalText != null && c.SchemaName == "test").Select(c => c.CanonicalText!).ToList();
            var schema = RecordFixtures.Schema("test");
            for (int k = 0; k < 100; k++)
            {
                int seed = batch * 100 + k;
                var random = new Random(seed);
                var text = new StringBuilder(seeds[random.Next(seeds.Count)]);
                int edits = random.Next(1, 5);
                for (int e = 0; e < edits; e++)
                {
                    int at = random.Next(text.Length + 1);
                    switch (random.Next(3))
                    {
                        case 0: text.Insert(at, Alphabet[random.Next(Alphabet.Length)]); break;
                        case 1: if (at < text.Length) text.Remove(at, Math.Min(text.Length - at, random.Next(1, 4))); break;
                        default: if (at < text.Length) { text.Remove(at, 1); text.Insert(at, Alphabet[random.Next(Alphabet.Length)]); } break;
                    }
                }
                string file = text.ToString();
                RecordWork.Steps = 0;
                RecordReadResult read;
                try { read = RecordFileReader.Read(file, schema); }
                catch (Exception ex) { throw new Xunit.Sdk.XunitException("seed " + seed + ": the reader threw " + ex + "\n" + file); }
                long bound = 64L * file.Length + 4096;
                Assert.True(RecordWork.Steps <= bound, "seed " + seed + ": " + RecordWork.Steps + " steps for " + file.Length + " characters\n" + file);
                if (read.HasErrors || read.Document == null) continue;
                string written;
                try { written = RecordFileWriter.Write(read.Document, schema); }
                catch (Exception ex) { throw new Xunit.Sdk.XunitException("seed " + seed + ": a file read without an error cannot be written: " + ex.Message + "\n" + file); }
                var again = RecordFileReader.Read(written, schema);
                Assert.True(!again.HasErrors, "seed " + seed + ": what was written reads with errors:\n" + string.Join("\n", again.Diagnostics) + "\n" + written);
            }
        }
    }
}
