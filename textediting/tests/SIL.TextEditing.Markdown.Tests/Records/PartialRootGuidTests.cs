using System;
using SIL.TextEditing.Markdown.Records;
using Xunit;

namespace SIL.TextEditing.Markdown.Tests.Records
{
    public class PartialRootGuidTests
    {
        static readonly Guid Identity = Guid.Parse("01234567-89ab-4cde-8fab-0123456789ab");

        static RecordSchema Schema(bool hasGuid = true)
        {
            var builder = new RecordSchemaBuilder("partial.schema.json");
            builder.ObjectType("note", hasGuid).Text("title").Text("other")
                .Rich("description", paragraphs: true).MultiRich("summary")
                .Objects("children", "note");
            return builder.Kind("notes/record", "note", 2, "description").Build();
        }

        static RecordReadOptions Options() => new RecordReadOptions
        {
            Markdown = new MarkdownOptions { WritingSystems = new TestWritingSystems() },
        };

        static RecordDocument Document(string root) => new RecordDocument("notes/record")
        {
            Root = root,
            Base = "sha256:unchanged",
            Analysis = "en",
            Vernacular = "ceb",
            Data = new RecordObject { Guid = Identity },
        };

        [Theory]
        [InlineData("/notes/example#title", "title", false)]
        [InlineData("/notes/example/children/1#title", "title", false)]
        [InlineData("/notes/example#description", "description", true)]
        [InlineData("/notes/example/children/1#description", "description", true)]
        [InlineData("/notes/example#summary.en", "summary", true)]
        public void AFieldPartialPreservesTheObjectsGuidAndValue(string root, string field, bool body)
        {
            var schema = Schema();
            var document = Document(root);
            var value = new RecordScalar("The value.");
            document.Data[field] = field == "summary" ? new RecordAlternatives().Add("en", value) : value;

            string written = RecordFileWriter.Write(document, schema);
            Assert.Contains("record-data:\n  guid: \"" + Identity.ToString("D") + "\"\n", written);
            if (body)
            {
                Assert.EndsWith("---\n\nThe value.\n", written);
                Assert.DoesNotContain("  " + field + ":", written);
            }
            else Assert.Contains("  guid: \"" + Identity.ToString("D") + "\"\n  title: \"The value.\"\n", written);

            var read = RecordFileReader.Read(written, schema, Options());
            Assert.Empty(read.Diagnostics);
            Assert.Equal(Identity, read.Document!.Data.Guid);
            Assert.True(RecordDocument.AreEquivalent(document, read.Document));
            Assert.Equal(written, RecordFileWriter.Write(read.Document, schema));
        }

        [Fact]
        public void AFieldPartialWithOnlyAGuidStillPreservesItsIdentity()
        {
            var schema = Schema();
            var document = Document("/notes/example#description");
            string written = RecordFileWriter.Write(document, schema);
            var read = RecordFileReader.Read(written, schema, Options());
            Assert.Empty(read.Diagnostics);
            Assert.Equal(Identity, read.Document!.Data.Guid);
            Assert.Empty(read.Document.Data.Fields);
            Assert.Equal(written, RecordFileWriter.Write(read.Document, schema));
        }

        [Fact]
        public void AFieldPartialCanonicalizesAGuidReadFromTheFrame()
        {
            var schema = Schema();
            string canonical = RecordFileWriter.Write(Document("/notes/example#title"), schema);
            string source = canonical.Replace(Identity.ToString("D"), Identity.ToString("D").ToUpperInvariant());
            var read = RecordFileReader.Read(source, schema, Options());
            Assert.False(read.HasErrors);
            Assert.Equal(Identity, read.Document!.Data.Guid);
            Assert.Equal(canonical, RecordFileWriter.Write(read.Document, schema));
        }

        [Theory]
        [InlineData("title")]
        [InlineData("description")]
        public void AFieldPartialStillRefusesAGuidOnAnObjectWithoutOne(string field)
        {
            var document = Document("/notes/example#" + field);
            string source = RecordFileWriter.Write(document, Schema());
            var schema = Schema(hasGuid: false);
            Assert.Throws<RecordWriteException>(() => RecordFileWriter.Write(document, schema));
            var read = RecordFileReader.Read(source, schema, Options());
            Assert.Contains(read.Diagnostics, d => d.Id == RecordDiagnosticIds.UnknownField);
            Assert.Null(read.Document!.Data.Guid);
        }

        [Theory]
        [InlineData("title")]
        [InlineData("description")]
        public void AFieldPartialWithAGuidStillRefusesAnUnrelatedField(string field)
        {
            var schema = Schema();
            var document = Document("/notes/example#" + field);
            string source = RecordFileWriter.Write(document, schema);
            int end = source.LastIndexOf("---\n", StringComparison.Ordinal);
            source = source.Insert(end, "  other: \"Outside the root.\"\n");

            var read = RecordFileReader.Read(source, schema, Options());
            Assert.Contains(read.Diagnostics, d => d.Id == RecordDiagnosticIds.OutsideRoot);
            Assert.Equal(Identity, read.Document!.Data.Guid);
            Assert.Null(read.Document.Data["other"]);

            document.Data["other"] = new RecordScalar("Outside the root.");
            Assert.Throws<RecordWriteException>(() => RecordFileWriter.Write(document, schema));
        }
    }
}
