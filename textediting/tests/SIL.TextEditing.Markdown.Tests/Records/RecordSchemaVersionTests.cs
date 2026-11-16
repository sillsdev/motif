using System;
using SIL.TextEditing.Markdown.Records;
using SIL.TextEditing.Markdown.Tests.Conformance;
using Xunit;

namespace SIL.TextEditing.Markdown.Tests.Records
{
    public class RecordSchemaVersionTests
    {
        static string SchemaJson(int version) => "{\"version\":" + version + ",\"id\":\"legacy\",\"types\":[" +
            "{\"name\":\"root\",\"fields\":[{\"name\":\"class\",\"kind\":\"text\"}," +
            "{\"name\":\"labels\",\"kind\":\"sequence\",\"item\":{\"kind\":\"text\"}}]}],\"kinds\":[]}";

        [Fact]
        public void VersionTwoRefusesAnOrdinaryClassField()
        {
            var error = Assert.Throws<FormatException>(() => RecordSchema.FromJson(SchemaJson(2)));
            Assert.Contains("class is reserved object metadata", error.Message);
        }

        [Fact]
        public void VersionOneClassFieldsRoundTripInVersionOneWithoutNewKindSpellings()
        {
            var schema = RecordSchema.FromJson(SchemaJson(1));
            Assert.Equal(RecordFieldKind.Text, schema.ObjectType("root")!.Field("class")!.Kind);
            string json = schema.ToJson();
            Assert.Equal(1L, Json.Obj(Json.Parse(json))["version"]);
            Assert.Contains("\"kind\": \"sequence\"", json);
            Assert.DoesNotContain("\"kind\": \"seq\"", json);
            Assert.Equal(json, RecordSchema.FromJson(json).ToJson());
        }
    }
}
