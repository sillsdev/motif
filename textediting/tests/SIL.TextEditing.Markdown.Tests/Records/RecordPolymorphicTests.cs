using System;
using System.Collections.Generic;
using System.Linq;
using SIL.TextEditing.Markdown.Records;
using SIL.TextEditing.Markdown.Tests.Conformance;
using Xunit;

namespace SIL.TextEditing.Markdown.Tests.Records
{
    public class RecordPolymorphicTests
    {
        static readonly Guid Identity = Guid.Parse("01234567-89ab-4cde-8fab-0123456789ab");

        static RecordSchema Schema()
        {
            var builder = new RecordSchemaBuilder("classes.schema.json");
            builder.ObjectType("root").Object("child", "note", "tag")
                .Objects("children", "note", "tag").Object("single", "note")
                .Objects("collection", "note", "tag").AsCollection("collection");
            builder.ObjectType("note").Text("text").Object("next", "note", "tag");
            builder.ObjectType("tag", false).Text("label");
            return builder.Kind("records/record", "root", 2).Build();
        }

        static string File(string data) => "---\nfwmd: " + MarkdownFrontMatter.CurrentVersion +
            "\nrecord: \"records/record\"\nschema: \"classes.schema.json\"\nrecord-data:\n" + data + "---\n";

        static RecordDocument Document(RecordObject child) => new RecordDocument("records/record")
        {
            Data = new RecordObject().Set("child", child),
        };

        [Fact]
        public void AllowedTypesKeepTheirDeclaredOrderAndTheSingularAccessorsRemainUnambiguous()
        {
            var schema = Schema();
            var root = schema.ObjectType("root")!;
            foreach (var field in new[] { root.Field("child")!, root.Field("children")!.Item!, root.Field("collection")!.Item! })
            {
                Assert.Equal(new[] { "note", "tag" }, field.ObjectTypeNames);
                Assert.Equal(new[] { "note", "tag" }, field.ObjectTypes.Select(t => t.Name));
                Assert.Same(schema.ObjectType("note"), field.ObjectTypes[0]);
                Assert.Same(schema.ObjectType("tag"), field.ObjectTypes[1]);
                Assert.Null(field.ObjectTypeName);
                Assert.Null(field.ObjectType);
            }
            Assert.Equal("note", root.Field("single")!.ObjectTypeName);
            Assert.Same(schema.ObjectType("note"), root.Field("single")!.ObjectType);
        }

        [Fact]
        public void MutatingTheSuppliedTypeNamesCannotChangeAFieldDeclaration()
        {
            var names = new[] { "note", "tag" };
            var builder = new RecordSchemaBuilder("copy");
            builder.ObjectType("root").Object("child", names).Objects("children", names);
            names[0] = "absent";
            builder.ObjectType("note");
            builder.ObjectType("tag");
            var schema = builder.Build();
            Assert.Equal(new[] { "note", "tag" }, schema.ObjectType("root")!.Field("child")!.ObjectTypeNames);
            Assert.Equal(new[] { "note", "tag" }, schema.ObjectType("root")!.Field("children")!.Item!.ObjectTypeNames);
            names[1] = "also-absent";
            Assert.Equal(new[] { "note", "tag" }, schema.ObjectType("root")!.Field("child")!.ObjectTypes.Select(t => t.Name));
        }

        [Fact]
        public void ClassCannotBeDeclaredAsAnOrdinaryFieldByTheBuilder()
            => Assert.Throws<ArgumentException>(() => new RecordSchemaBuilder("reserved").ObjectType("root").Text("class"));

        [Fact]
        public void LegacyJsonCanRetainAnExplicitlyDeclaredClassField()
        {
            const string json = "{\"version\":1,\"id\":\"legacy\",\"types\":[{\"name\":\"root\",\"fields\":[" +
                "{\"name\":\"class\",\"kind\":\"text\"}]}],\"kinds\":[]}";
            var schema = RecordSchema.FromJson(json);
            Assert.Equal(RecordFieldKind.Text, schema.ObjectType("root")!.Field("class")!.Kind);
            var restored = RecordSchema.FromJson(schema.ToJson());
            Assert.Equal(RecordFieldKind.Text, restored.ObjectType("root")!.Field("class")!.Kind);
        }

        [Fact]
        public void VersionTwoJsonPreservesAlternativesAndRecursiveTypeReferences()
        {
            var schema = Schema();
            string json = schema.ToJson();
            var root = Json.Obj(Json.Parse(json));
            Assert.Equal(2L, root["version"]);
            var fields = Json.Arr(Json.Obj(Json.Arr(root["types"])[0])["fields"]).Select(Json.Obj).ToList();
            Assert.Equal(new[] { "note", "tag" }, Json.Arr(fields[0]["types"]).Select(Json.Str));
            Assert.False(fields[0].ContainsKey("type"));
            Assert.Equal(new[] { "note", "tag" }, Json.Arr(Json.Obj(fields[1]["item"])["types"]).Select(Json.Str));
            Assert.Equal("note", Json.Str(fields[2]["type"]));
            Assert.False(fields[2].ContainsKey("types"));
            var restored = RecordSchema.FromJson(json);
            Assert.Equal(json, restored.ToJson());
            Assert.Equal(schema.ToJsonSchema(), restored.ToJsonSchema());
            Assert.Equal(new[] { "note", "tag" }, restored.ObjectType("note")!.Field("next")!.ObjectTypeNames);
            Assert.Same(restored.ObjectType("note"), restored.ObjectType("note")!.Field("next")!.ObjectTypes[0]);
        }

        [Theory]
        [InlineData("  child:\n    text: \"A note.\"\n")]
        [InlineData("  child:\n    class: \"absent\"\n")]
        [InlineData("  child:\n    class: \"root\"\n")]
        [InlineData("  child:\n    class:\n      label: \"tag\"\n")]
        [InlineData("  child:\n    class:\n      - \"note\"\n")]
        [InlineData("  children:\n    - text: \"A note.\"\n")]
        [InlineData("  collection:\n    - class: \"root\"\n")]
        public void MissingDisallowedAndNonscalarClassesProduceTheClassFinding(string data)
        {
            var read = RecordFileReader.Read(File(data), Schema());
            Assert.Contains(read.Diagnostics, d => d.Id == RecordDiagnosticIds.ClassNotAllowed && d.Severity == MarkdownSeverity.Error);
        }

        [Theory]
        [InlineData("  child:\n    class: \"note\"\n    label: \"Foreign field.\"\n")]
        [InlineData("  child:\n    class: \"tag\"\n    text: \"Foreign field.\"\n")]
        public void UnknownFieldsAreCheckedAgainstTheChosenClass(string data)
        {
            var read = RecordFileReader.Read(File(data), Schema());
            Assert.Contains(read.Diagnostics, d => d.Id == RecordDiagnosticIds.UnknownField);
            Assert.DoesNotContain(read.Diagnostics, d => d.Id == RecordDiagnosticIds.ClassNotAllowed);
        }

        [Fact]
        public void AClassAfterTheFieldsSelectsTheirTypeBeforeTheyAreRead()
        {
            var read = RecordFileReader.Read(File("  child:\n    label: \"A tag.\"\n    class: \"tag\"\n"), Schema());
            Assert.False(read.HasErrors, string.Join("; ", read.Diagnostics));
            var child = Assert.IsType<RecordObject>(read.Document!.Data["child"]);
            Assert.Equal("tag", child.Class);
            Assert.Equal("A tag.", Assert.IsType<RecordScalar>(child["label"]).Text);
            Assert.Null(child["class"]);
        }

        [Fact]
        public void WriterAndReaderPreserveClassIdentityAndCanonicalKeyOrder()
        {
            var schema = Schema();
            var note = new RecordObject { Class = "note", Guid = Identity };
            note.Set("text", new RecordScalar("A note.")).Set("next", new RecordObject { Class = "tag" });
            var document = Document(note);
            document.Data["children"] = new RecordList().Add(new RecordObject { Class = "tag" })
                .Add(new RecordObject { Class = "note" }.Set("text", new RecordScalar("Another note.")));
            document.Data["collection"] = new RecordList().Add(new RecordObject { Class = "tag" });
            string written = RecordFileWriter.Write(document, schema);
            Assert.Contains("  child:\n    guid: \"" + Identity.ToString("D") + "\"\n    class: \"note\"\n    text:", written);
            var read = RecordFileReader.Read(written, schema);
            Assert.False(read.HasErrors, string.Join("; ", read.Diagnostics));
            Assert.True(RecordDocument.AreEquivalent(document, read.Document!));
            Assert.Equal(written, RecordFileWriter.Write(read.Document!, schema));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("absent")]
        [InlineData("root")]
        public void WriterRefusesAMissingOrDisallowedPolymorphicClass(string? name)
        {
            var child = new RecordObject { Class = name };
            child.Set("text", new RecordScalar("A note."));
            Assert.Throws<RecordWriteException>(() => RecordFileWriter.Write(Document(child), Schema()));
        }

        [Theory]
        [InlineData("children")]
        [InlineData("collection")]
        public void WriterRequiresAnAllowedClassOnEveryListItem(string field)
        {
            var document = new RecordDocument("records/record");
            document.Data[field] = new RecordList().Add(new RecordObject { Class = "tag" })
                .Add(new RecordObject().Set("text", new RecordScalar("Missing class.")));
            Assert.Throws<RecordWriteException>(() => RecordFileWriter.Write(document, Schema()));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void MonomorphicAndRootClassesAreOptionalButMustMatchWhenPresent(bool root)
        {
            var schema = Schema();
            var document = new RecordDocument("records/record");
            var target = root ? document.Data : new RecordObject().Set("text", new RecordScalar("A note."));
            if (!root) document.Data["single"] = target;
            var expected = root ? "root" : "note";
            foreach (string? name in new string?[] { null, expected })
            {
                target.Class = name;
                string text = RecordFileWriter.Write(document, schema);
                var read = RecordFileReader.Read(text, schema);
                Assert.False(read.HasErrors, string.Join("; ", read.Diagnostics));
                Assert.True(RecordDocument.AreEquivalent(document, read.Document!));
            }
            target.Class = "tag";
            Assert.Throws<RecordWriteException>(() => RecordFileWriter.Write(document, schema));
            string wrong = root ? "  class: \"tag\"\n" : "  single:\n    class: \"tag\"\n";
            Assert.Contains(RecordFileReader.Read(File(wrong), schema).Diagnostics, d => d.Id == RecordDiagnosticIds.ClassNotAllowed);
        }

        [Fact]
        public void ARedundantClassIsEquivalentToNoneOnlyWhenTheSchemaSaysTheFieldHasOneType()
        {
            var schema = Schema();
            RecordDocument Single(string? name) => new RecordDocument("records/record")
            {
                Data = new RecordObject().Set("single", new RecordObject { Class = name }.Set("text", new RecordScalar("x"))),
            };
            RecordDocument Many(string? name) => Document(new RecordObject { Class = name }.Set("text", new RecordScalar("x")));
            RecordDocument NoteInList(string? name) => new RecordDocument("records/record")
            {
                Data = new RecordObject().Set("children", new RecordList().Add(new RecordObject { Class = name }.Set("text", new RecordScalar("x")))),
            };
            Assert.True(RecordDocument.AreEquivalent(Single("note"), Single(null), schema));
            Assert.True(RecordDocument.AreEquivalent(Single(null), Single("note"), schema));
            Assert.False(RecordDocument.AreEquivalent(Single("note"), Single(null)));
            Assert.False(RecordDocument.AreEquivalent(Single("note"), Single("tag"), schema));
            Assert.False(RecordDocument.AreEquivalent(Many("note"), Many(null), schema));
            Assert.False(RecordDocument.AreEquivalent(NoteInList("note"), NoteInList(null), schema));
        }

        [Fact]
        public void ClassIsPartOfObjectEmptinessAndDocumentEquivalence()
        {
            var empty = new RecordObject();
            var note = new RecordObject { Class = "note" };
            var tag = new RecordObject { Class = "tag" };
            Assert.True(empty.IsEmpty);
            Assert.False(note.IsEmpty);
            Assert.False(RecordDocument.AreEquivalent(note, empty));
            Assert.False(RecordDocument.AreEquivalent(note, tag));
            Assert.True(RecordDocument.AreEquivalent(note, new RecordObject { Class = "note" }));
            Assert.False(RecordDocument.AreEquivalent(Document(note), Document(tag)));
        }

        [Fact]
        public void DraftSevenSchemaRequiresAClassForEachPolymorphicAlternative()
        {
            var schema = Json.Obj(Json.Parse(Schema().ToJsonSchema()));
            var definitions = Json.Obj(schema["definitions"]);
            var properties = Json.Obj(Json.Obj(definitions["root"])["properties"]);
            foreach (var field in new[] { Json.Obj(properties["child"]), Json.Obj(Json.Obj(properties["children"])["items"]) })
            {
                var branches = Json.Arr(field["anyOf"]).Select(Json.Obj).ToList();
                Assert.Equal(2, branches.Count);
                for (int i = 0; i < branches.Count; i++)
                {
                    var nodes = SchemaNodes(branches[i], definitions).ToList();
                    string expected = i == 0 ? "note" : "tag";
                    Assert.Contains(nodes, n => n.TryGetValue("properties", out var p) && Json.Obj(p).TryGetValue("class", out var c)
                        && Json.Obj(c).TryGetValue("const", out var value) && Json.Str(value) == expected);
                    Assert.Contains(nodes, n => n.TryGetValue("required", out var r) && Json.Arr(r).Select(Json.Str).Contains("class"));
                }
            }
        }

        static IEnumerable<Dictionary<string, object?>> SchemaNodes(Dictionary<string, object?> node, Dictionary<string, object?> definitions)
        {
            yield return node;
            if (node.TryGetValue("$ref", out var reference))
            {
                string name = Json.Str(reference).Substring("#/definitions/".Length);
                yield return Json.Obj(definitions[name]);
            }
            if (node.TryGetValue("allOf", out var children))
                foreach (var child in Json.Arr(children).Select(Json.Obj))
                    foreach (var found in SchemaNodes(child, definitions)) yield return found;
        }
    }
}
