using System;
using SIL.TextEditing.Markdown.Records;
using Xunit;

namespace SIL.TextEditing.Markdown.Tests.Records
{
    public class RecordListSchemaTests
    {
        [Fact]
        public void WriterEmitsVersionTwoSeqAndColAndComputedMetadata()
        {
            var builder = new RecordSchemaBuilder("lists");
            builder.ObjectType("note", false).Texts("ordered").Texts("unordered").AsCollection("unordered")
                .Text("result").Computed("result");
            builder.Kind("notes/note", "note", 2);
            var schema = RecordSchema.FromJson(builder.Build().ToJson());
            Assert.Equal(2, RecordSchema.CurrentVersion);
            Assert.Contains("\"kind\": \"seq\"", schema.ToJson());
            Assert.Contains("\"kind\": \"col\"", schema.ToJson());
            Assert.Equal(RecordFieldKind.Sequence, schema.ObjectType("note")!.Field("ordered")!.Kind);
            Assert.Equal(RecordFieldKind.Collection, schema.ObjectType("note")!.Field("unordered")!.Kind);
            Assert.True(schema.ObjectType("note")!.Field("result")!.Computed);
            Assert.False(schema.ObjectType("note")!.Field("ordered")!.Computed);
            var document = new RecordDocument("notes/note");
            document.Data.Set("ordered", new RecordList().Add(new RecordScalar("z")).Add(new RecordScalar("a")))
                .Set("unordered", new RecordList().Add(new RecordScalar("b")).Add(new RecordScalar("a")))
                .Set("result", new RecordScalar("computed text"));
            string text = RecordFileWriter.Write(document, schema);
            var read = RecordFileReader.Read(text, schema);
            Assert.False(read.HasErrors, string.Join("; ", read.Diagnostics));
            Assert.True(RecordDocument.AreEquivalent(document, read.Document!));
            Assert.Equal(text, RecordFileWriter.Write(read.Document!, schema));
        }

        [Theory]
        [InlineData(1, "sequence", RecordFieldKind.Sequence)]
        [InlineData(2, "sequence", RecordFieldKind.Sequence)]
        [InlineData(2, "seq", RecordFieldKind.Sequence)]
        [InlineData(2, "col", RecordFieldKind.Collection)]
        public void VersionedListKindsAreRead(int version, string kind, RecordFieldKind expected)
        {
            var schema = RecordSchema.FromJson(Json(version, "{\"name\":\"list\",\"kind\":\"" + kind + "\",\"item\":{\"kind\":\"text\"}}"));
            Assert.Equal(expected, schema.ObjectType("note")!.Field("list")!.Kind);
        }

        [Theory]
        [InlineData("{\"name\":\"x\",\"kind\":\"col\",\"item\":{\"kind\":\"text\"}}")]
        [InlineData("{\"name\":\"x\",\"kind\":\"seq\",\"item\":{\"kind\":\"text\"}}")]
        [InlineData("{\"name\":\"x\",\"kind\":\"text\",\"computed\":true}")]
        public void VersionOneRefusesVersionTwoFeatures(string field)
            => Assert.Throws<FormatException>(() => RecordSchema.FromJson(Json(1, field)));

        [Fact]
        public void FieldModifiersRequireAnExistingFieldAndCollectionRequiresAList()
        {
            var type = new RecordSchemaBuilder("s").ObjectType("note").Text("text");
            Assert.Throws<ArgumentException>(() => type.AsCollection("text"));
            Assert.Throws<ArgumentException>(() => type.AsCollection("absent"));
            Assert.Throws<ArgumentException>(() => type.Computed("absent"));
        }

        static string Json(int version, string field) => "{\"version\":" + version +
            ",\"id\":\"s\",\"types\":[{\"name\":\"note\",\"fields\":[" + field + "]}],\"kinds\":[]}";
    }
}
