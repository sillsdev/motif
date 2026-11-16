using System;
using SIL.TextEditing.Markdown.Records;
using Xunit;

namespace SIL.TextEditing.Markdown.Tests.Records
{
    public class RecordPolymorphicRootTests
    {
        static RecordSchema Schema()
        {
            var builder = new RecordSchemaBuilder("partial-classes");
            builder.ObjectType("root").Object("child", "note", "tag")
                .Objects("children", "note", "tag").AsCollection("children");
            builder.ObjectType("note").Text("text").Rich("body", paragraphs: true).Object("child", "tag", "note");
            builder.ObjectType("tag").Text("label").Object("child", "note", "tag");
            return builder.Kind("records/record", "root", 2).Build();
        }

        [Theory]
        [InlineData("/records/x/child#text", "note", "text")]
        [InlineData("/records/x/children/1#label", "tag", "label")]
        [InlineData("/records/x/child/child#body", "note", "body")]
        [InlineData("/records/x/children/1/child#text", "note", "text")]
        public void APartialObjectClassSelectsItsFieldsAndPreservesItsGuid(string root, string type, string field)
        {
            var document = new RecordDocument("records/record") { Root = root };
            document.Data.Class = type;
            document.Data.Guid = Guid.Parse("01234567-89ab-4cde-8fab-0123456789ab");
            document.Data.Set(field, new RecordScalar("Text."));
            string text = RecordFileWriter.Write(document, Schema());
            var read = RecordFileReader.Read(text, Schema());
            Assert.Empty(read.Diagnostics);
            Assert.True(RecordDocument.AreEquivalent(document, read.Document!));
            Assert.Equal(text, RecordFileWriter.Write(read.Document!, Schema()));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("absent")]
        public void AnAmbiguousPartialRootNeverChoosesTheFirstAllowedType(string? type)
        {
            var document = new RecordDocument("records/record") { Root = "/records/x/child#text" };
            document.Data.Class = type;
            document.Data.Set("text", new RecordScalar("Text."));
            Assert.Throws<RecordWriteException>(() => RecordFileWriter.Write(document, Schema()));
            string text = "---\nfwmd: 1\nrecord: \"records/record\"\nroot: \"/records/x/child#text\"\nrecord-data:\n" +
                (type == null ? "" : "  class: \"" + type + "\"\n") + "  text: \"Text.\"\n---\n";
            Assert.Contains(RecordFileReader.Read(text, Schema()).Diagnostics, d => d.Id == RecordDiagnosticIds.ClassNotAllowed);
        }

        [Fact]
        public void InvalidAllowedTypeListsAndLegacyClassAlternativesAreRefused()
        {
            var builder = new RecordSchemaBuilder("s");
            var type = builder.ObjectType("root");
            Assert.Throws<ArgumentException>(() => type.Object("x", Array.Empty<string>()));
            Assert.Throws<ArgumentException>(() => type.Object("x", "note", "note"));
            Assert.Throws<ArgumentException>(() => type.Object("x", new string[] { null! }));
            type.Object("child", "note", "missing");
            builder.ObjectType("note");
            Assert.Throws<InvalidOperationException>(() => builder.Build());
            string legacy = "{\"version\":2,\"id\":\"s\",\"types\":[" +
                "{\"name\":\"root\",\"fields\":[{\"name\":\"child\",\"kind\":\"object\",\"types\":[\"a\",\"b\"]}]}," +
                "{\"name\":\"a\",\"fields\":[{\"name\":\"class\",\"kind\":\"text\"}]}," +
                "{\"name\":\"b\",\"fields\":[]}],\"kinds\":[]}";
            Assert.Throws<FormatException>(() => RecordSchema.FromJson(legacy));
        }
    }
}
