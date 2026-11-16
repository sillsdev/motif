using SIL.TextEditing.Markdown.Records;
using Xunit;

namespace SIL.TextEditing.Markdown.Tests.Records
{
    public class RecordRootErrorTests
    {
        static RecordSchema Schema()
        {
            var builder = new RecordSchemaBuilder("root-errors");
            builder.ObjectType("root").Objects("children", "note").Objects("tags", "tag").AsCollection("tags")
                .Object("choice", "tag", "note").Text("title").Texts("labels");
            builder.ObjectType("note").Object("detail", "detail").Text("text");
            builder.ObjectType("tag").Text("label");
            builder.ObjectType("detail").Text("text");
            return builder.Kind("records/record", "root", 2).Build();
        }

        [Theory]
        [InlineData("/records/x/children", "In '/records/x/children', children is a sequence, so a 1-based index follows it.")]
        [InlineData("/records/x/children/0", "In '/records/x/children/0', children is a sequence, so a 1-based index follows it.")]
        [InlineData("/records/x/children/name", "In '/records/x/children/name', children is a sequence, so a 1-based index follows it.")]
        [InlineData("/records/x/tags", "In '/records/x/tags', tags is a collection, so a 1-based index follows it.")]
        [InlineData("/records/x/absent", "In '/records/x/absent', absent is not an owned object of root.")]
        [InlineData("/records/x/title", "In '/records/x/title', title is not an owned object of root.")]
        [InlineData("/records/x/labels", "In '/records/x/labels', labels is not an owned object of root.")]
        [InlineData("/records/x/children/1/absent", "In '/records/x/children/1/absent', absent is not an owned object of note.")]
        [InlineData("/records/x/choice/detail/absent", "In '/records/x/choice/detail/absent', absent is not an owned object of detail.")]
        public void PartialRootFailuresNameTheFurthestOffendingSegmentAndOwningType(string root, string expected)
        {
            var schema = Schema();
            var document = new RecordDocument("records/record") { Root = root };
            var writeError = Assert.Throws<RecordWriteException>(() => RecordFileWriter.Write(document, schema));
            Assert.Equal(expected, writeError.Message);
            string text = "---\nfwmd: 1\nrecord: \"records/record\"\nroot: \"" + root + "\"\n---\n";
            var read = RecordFileReader.Read(text, schema);
            Assert.Equal(expected, Assert.Single(read.Diagnostics, d => d.Id == RecordDiagnosticIds.BadRoot).Message);
        }
    }
}
