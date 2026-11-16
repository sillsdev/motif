using System;
using SIL.TextEditing.Markdown.Records;
using SIL.TextEditing.Markdown.Tests.Conformance;
using Xunit;

namespace SIL.TextEditing.Markdown.Tests.Records
{
    public class RecordSchemaSerializationTests
    {
        [Theory]
        [InlineData("test")]
        [InlineData("blocks")]
        public void EveryFixtureSchemaRoundTripsWithoutLosingItsModel(string name)
        {
            var schema = RecordFixtures.Schema(name);
            var restored = RecordSchema.FromJson(schema.ToJson());
            Equal(schema, restored);
            Assert.Equal(schema.ToJson(), restored.ToJson());
            Assert.Equal(schema.ToJsonSchema(), restored.ToJsonSchema());
        }

        [Fact]
        public void EveryBuilderShapeAndJsonEscapeRoundTrips()
        {
            var builder = new RecordSchemaBuilder("opaque\"\\\n\r\t\0\b\f雪");
            builder.ObjectType("root\"\\\n", false)
                .Text("text", RecordWritingSystem.Vernacular).MultiText("texts")
                .Rich("rich", true, RecordWritingSystem.Vernacular, BlockFeatures.Lists | BlockFeatures.Headings)
                .Rich("inline", false, RecordWritingSystem.Vernacular, BlockFeatures.None)
                .MultiRich("riches", true, BlockFeatures.Tables).MultiRich("inlines")
                .Notation("notation", "environment", RecordWritingSystem.Vernacular).MultiNotation("notations", "features")
                .Integer("integer").Boolean("boolean").Guid("identity").Reference("reference")
                .Object("child", "child").Objects("children", "child").References("references")
                .Texts("words", RecordWritingSystem.Vernacular).Notations("expressions", "environment", RecordWritingSystem.Vernacular);
            builder.ObjectType("child").Object("recursive", "child");
            builder.Kind("test/kind", "root\"\\\n", 3, "rich").Kind("test/other", "child", 2);
            var schema = builder.Build();
            Equal(schema, RecordSchema.FromJson(schema.ToJson()));
        }

        [Fact]
        public void EmptySchemasRoundTrip()
        {
            Equal(new RecordSchemaBuilder("empty").Build(),
                RecordSchema.FromJson(new RecordSchemaBuilder("empty").Build().ToJson()));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(3)]
        [InlineData(2147483647)]
        public void UnsupportedVersionsAreRefusedClearly(int version)
        {
            var error = Assert.Throws<FormatException>(() => RecordSchema.FromJson(
                "{\"version\":" + version + "}"));
            Assert.Contains("Unsupported record schema version " + version, error.Message);
        }

        [Theory]
        [InlineData("")]
        [InlineData("{")]
        [InlineData("{\"version\"")]
        [InlineData("[1,")]
        [InlineData("null")]
        [InlineData("{\"version\":1,\"version\":1}")]
        [InlineData("{\"version\":1.0}")]
        [InlineData("{\"version\":01}")]
        [InlineData("{\"version\":1,}")]
        [InlineData("{\"version\":\"1\"}")]
        [InlineData("{\"version\":1,\"id\":\"\\u0")]
        [InlineData("{\"version\":1,\"id\":\"\\x\"}")]
        [InlineData("{\"version\":1,\"id\":\"x\",\"types\":[],\"kinds\":[]} trailing")]
        [InlineData("{\"version\":1,\"id\":\"x\",\"types\":[],\"kinds\":[],\"extra\":true}")]
        [InlineData("{\"id\":\"x\",\"types\":[],\"kinds\":[]}")]
        public void MalformedOrUnversionedDataIsRefused(string json)
            => Assert.Throws<FormatException>(() => RecordSchema.FromJson(json));

        [Theory]
        [InlineData("{\"name\":\"x\",\"kind\":\"unknown\"}")]
        [InlineData("{\"name\":\"x\",\"kind\":\"object\",\"type\":\"missing\"}")]
        [InlineData("{\"name\":\"x\",\"kind\":\"text\",\"ws\":\"other\"}")]
        [InlineData("{\"name\":\"x\",\"kind\":\"integer\",\"multi\":true}")]
        [InlineData("{\"name\":\"x\",\"kind\":\"rich\",\"blocks\":[\"unknown\"]}")]
        [InlineData("{\"name\":\"x\",\"kind\":\"rich\",\"blocks\":[\"tables\"]}")]
        [InlineData("{\"name\":\"x\",\"kind\":\"text\",\"multi\":\"yes\"}")]
        [InlineData("{\"name\":\"x\",\"kind\":\"sequence\",\"item\":{\"kind\":\"rich\"}}")]
        public void InvalidDeclarationsAreRefused(string field)
            => Assert.Throws<FormatException>(() => RecordSchema.FromJson(
                "{\"version\":1,\"id\":\"x\",\"types\":[{\"name\":\"root\",\"fields\":[" + field + "]}],\"kinds\":[]}"));

        [Fact]
        public void ExcessiveNestingIsRefused()
            => Assert.Throws<FormatException>(() => RecordSchema.FromJson(new string('[', 1000)));

        [Fact]
        public void EveryTruncatedPrefixProducesAFormatException()
        {
            string json = new RecordSchemaBuilder("quoted\"\\\n\0").Build().ToJson().TrimEnd();
            for (int length = 0; length < json.Length; length++)
                Assert.Throws<FormatException>(() => RecordSchema.FromJson(json.Substring(0, length)));
        }

        [Fact]
        public void NullInputIsRefused()
            => Assert.Throws<ArgumentNullException>(() => RecordSchema.FromJson(null!));

        static void Equal(RecordSchema expected, RecordSchema actual)
        {
            Assert.Equal(expected.Id, actual.Id);
            Assert.Equal(expected.Kinds.Count, actual.Kinds.Count);
            for (int i = 0; i < expected.Kinds.Count; i++)
            {
                var a = expected.Kinds[i]; var b = actual.Kinds[i];
                Assert.Equal(a.Name, b.Name);
                Assert.Equal(a.Root.Name, b.Root.Name);
                Assert.Equal(a.PathSegments, b.PathSegments);
                Assert.Equal(a.BodyField?.Name, b.BodyField?.Name);
                Assert.Same(b.Root, actual.ObjectType(b.Root.Name));
            }
            Assert.Equal(expected.ObjectTypes.Count, actual.ObjectTypes.Count);
            for (int i = 0; i < expected.ObjectTypes.Count; i++)
            {
                var a = expected.ObjectTypes[i]; var b = actual.ObjectTypes[i];
                Assert.Equal(a.Name, b.Name);
                Assert.Equal(a.HasGuid, b.HasGuid);
                Assert.Equal(a.Fields.Count, b.Fields.Count);
                for (int j = 0; j < a.Fields.Count; j++) Equal(a.Fields[j], b.Fields[j]);
            }
        }

        static void Equal(RecordField a, RecordField b)
        {
            Assert.Equal(a.Name, b.Name);
            Assert.Equal(a.Kind, b.Kind);
            Assert.Equal(a.Computed, b.Computed);
            Assert.Equal(a.MultiWritingSystem, b.MultiWritingSystem);
            Assert.Equal(a.Paragraphs, b.Paragraphs);
            Assert.Equal(a.BlockFeatures, b.BlockFeatures);
            Assert.Equal(a.Blocks, b.Blocks);
            Assert.Equal(a.WritingSystem, b.WritingSystem);
            Assert.Equal(a.Notation, b.Notation);
            Assert.Equal(a.ObjectTypeName, b.ObjectTypeName);
            Assert.Equal(a.ObjectType?.Name, b.ObjectType?.Name);
            if (a.Item == null) Assert.Null(b.Item);
            else { Assert.NotNull(b.Item); Equal(a.Item, b.Item!); }
        }
    }
}
