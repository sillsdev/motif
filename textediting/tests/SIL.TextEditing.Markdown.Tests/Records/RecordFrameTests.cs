using System;
using System.Linq;
using Xunit;

namespace SIL.TextEditing.Markdown.Tests.Records
{
    public sealed class RecordFrameTests
    {
        [Fact]
        public void AFrameChecksMetadataWithoutInventingTypesForFieldsOrTheBody()
        {
            const string text = "---\nfwmd: 1\nrecord: \"entry\"\nschema: \"external\"\nanalysis: \"en\"\nvernacular: \"fr\"\nrecord-data:\n  unknown: \"text\"\n---\n**body**\n";
            var read = Markdown.Records.RecordFileReader.ReadFrame(text);
            Assert.Empty(read.Diagnostics);
            Assert.NotNull(read.Document);
            Assert.Equal("entry", read.Document!.Kind);
            Assert.Equal("external", read.Document.Schema);
            Assert.Equal("en", read.Document.Analysis);
            Assert.Equal("fr", read.Document.Vernacular);
            Assert.Empty(read.Document.Data.Fields);
        }

        [Fact]
        public void AFrameRetainsYamlErrorsAndTheirOriginalPlaces()
        {
            const string text = "---\nfwmd: 1\nrecord: \"entry\"\nrecord-data:\n  value: &anchor \"text\"\n  value: \"duplicate\"\n---\n";
            var read = Markdown.Records.RecordFileReader.ReadFrame(text);
            Assert.True(read.HasErrors);
            Assert.Contains(read.Diagnostics, d => d.Id == "fwmd.yaml.anchor" && d.Line == 5 && d.Column > 0);
            Assert.Contains(read.Diagnostics, d => d.Id == "fwmd.yaml.duplicate-key" && d.Line == 6 && d.Column == 1 && d.EndLine == 7 && d.EndColumn == 1);
            Assert.Equal(read.Diagnostics.OrderBy(d => d.Start), read.Diagnostics);
        }

        [Theory]
        [InlineData("---\nfwmd: 99\nrecord: \"entry\"\n---\n", "fwmd.version.unsupported")]
        [InlineData("---\nrecord: \"entry\"\n---\n", "fwmd.record.not-a-record")]
        [InlineData("---\nfwmd: 1\nrecord: \"entry\"\n", "fwmd.record.not-a-record")]
        public void InvalidFramesAreErrors(string text, string id)
        {
            var read = Markdown.Records.RecordFileReader.ReadFrame(text);
            Assert.True(read.HasErrors);
            Assert.Contains(read.Diagnostics, d => d.Id == id);
        }

        [Fact]
        public void NullInputIsRejected() => Assert.Throws<ArgumentNullException>(() => Markdown.Records.RecordFileReader.ReadFrame(null!));
    }
}
