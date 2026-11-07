using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using SIL.TextEditing.Markdown.Records;
using SIL.TextEditing.Markdown.Tests.Conformance;
using Xunit;
using YamlDotNet.RepresentationModel;

namespace SIL.TextEditing.Markdown.Tests.Records
{
    /// <summary>The JSON Schema a record schema publishes (owner decision R-7): the expected document for the fixtures' schema is in
    /// tests/conformance/records/test.schema.json, every text value carries its media type, and every canonical whole-record fixture validates against it, as an
    /// editor's YAML extension would check it (YAML's core typing: plain numbers and Booleans, everything else a string).</summary>
    public class RecordJsonSchemaTests
    {
        static RecordSchema Schema => RecordFixtures.Schema("test");

        [Fact]
        public void TheTestSchemaPublishesTheExpectedDocument()
        {
            string actual = Schema.ToJsonSchema();
            // Set TEXTEDITING_WRITE_SCHEMA to a path to write the document there after a deliberate change.
            string? write = Environment.GetEnvironmentVariable("TEXTEDITING_WRITE_SCHEMA");
            if (!string.IsNullOrEmpty(write)) File.WriteAllText(write, actual);
            Assert.Equal(ConformanceLoader.ReadText(Path.Combine(RecordFixtures.Directory, "test.schema.json")), actual);
        }

        [Fact]
        public void EveryTextValueCarriesItsMediaType()
        {
            var root = Json.Obj(Json.Parse(Schema.ToJsonSchema()));
            Assert.Equal("test.schema.json", Json.Str(root["$id"]));
            var defs = Json.Obj(root["definitions"]);
            Dictionary<string, object?> Prop(string type, string field) => Json.Obj(Json.Obj(Json.Obj(defs[type])["properties"])[field]);
            Assert.Equal("text/plain", Json.Str(Json.Obj(Prop("sense", "gloss")["additionalProperties"])["contentMediaType"]));
            Assert.Equal(RecordSchema.InlineMarkdownMediaType, Json.Str(Json.Obj(Prop("sense", "definition")["additionalProperties"])["contentMediaType"]));
            Assert.Equal(RecordSchema.InlineMarkdownMediaType, Json.Str(Prop("sense", "grammatical-info")["contentMediaType"]));
            Assert.Equal(RecordSchema.MarkdownMediaType, Json.Str(Prop("notebook-record", "conclusions")["contentMediaType"]));
            Assert.Equal("text/x-fieldworks-feature-structure", Json.Str(Prop("sense", "features")["contentMediaType"]));
            Assert.Equal("text/x-fieldworks-environment", Json.Str(Json.Obj(Prop("allomorph", "environment-strings")["items"])["contentMediaType"]));
            Assert.Equal("integer", Json.Str(Prop("entry", "homograph-number")["type"]));
            Assert.Equal("boolean", Json.Str(Prop("entry", "do-not-use-for-parsing")["type"]));
            // The body field is not a key of a whole record's frame.
            Assert.False(Json.Obj(Json.Obj(defs["notebook-record--frame"])["properties"]).ContainsKey("description"));
            Assert.True(Json.Obj(Json.Obj(defs["notebook-record"])["properties"]).ContainsKey("description"));
        }

        public static IEnumerable<object[]> WholeCanonicalCases()
            => RecordFixtures.All().Where(c => c.CanonicalText != null && c.SchemaName == "test" && !(RecordFileReader.Read(c.CanonicalText, c.Schema).Document?.Root?.Contains("#") ?? false))
                .Select(c => new object[] { c.Id });

        [Theory]
        [MemberData(nameof(WholeCanonicalCases))]
        public void ACanonicalFixtureValidatesAgainstTheJsonSchema(string id)
        {
            var c = RecordFixtures.Find(id);
            string text = c.CanonicalText!;
            int close = text.IndexOf("\n---\n", 3, StringComparison.Ordinal);
            var stream = new YamlStream();
            stream.Load(new StringReader(text.Substring(4, close + 1 - 4)));
            var schema = Json.Obj(Json.Parse(Schema.ToJsonSchema()));
            var errors = new List<string>();
            new Validator(schema).Check(schema, Value(stream.Documents[0].RootNode), "$", errors);
            Assert.True(errors.Count == 0, string.Join("\n", errors));
        }

        [Fact]
        public void AFrameWithAnUnknownFieldOrAWrongTypeDoesNotValidate()
        {
            var schema = Json.Obj(Json.Parse(Schema.ToJsonSchema()));
            var stream = new YamlStream();
            stream.Load(new StringReader("fwmd: 1\nrecord: \"lexicon/entry\"\nrecord-data:\n  sense: \"x\"\n  homograph-number: \"two\"\n  do-not-use-for-parsing: \"yes\"\n"));
            var errors = new List<string>();
            new Validator(schema).Check(schema, Value(stream.Documents[0].RootNode), "$", errors);
            Assert.Contains(errors, e => e.Contains("sense"));
            Assert.Contains(errors, e => e.Contains("homograph-number"));
            Assert.Contains(errors, e => e.Contains("do-not-use-for-parsing"));
        }

        /// <summary>A YAML node as a JSON value, typed as YAML's core schema types plain scalars (what an editor's YAML extension does).</summary>
        static object? Value(YamlNode node)
        {
            switch (node)
            {
                case YamlScalarNode s:
                    if (s.Style == YamlDotNet.Core.ScalarStyle.Plain)
                    {
                        if (s.Value == "true") return true;
                        if (s.Value == "false") return false;
                        if (long.TryParse(s.Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long l)) return l;
                    }
                    return s.Value;
                case YamlSequenceNode q: return q.Children.Select(Value).ToList();
                case YamlMappingNode m:
                    var map = new Dictionary<string, object?>(StringComparer.Ordinal);
                    foreach (var kv in m.Children) map[((YamlScalarNode)kv.Key).Value!] = Value(kv.Value);
                    return map;
            }
            throw new InvalidOperationException(node.GetType().Name);
        }

        /// <summary>The part of JSON Schema draft-07 the published document uses: type, const, enum, pattern, properties, additionalProperties, required, items,
        /// $ref to definitions, allOf, if/then and not. contentMediaType is an annotation and is not checked.</summary>
        sealed class Validator
        {
            readonly Dictionary<string, object?> _root;
            public Validator(Dictionary<string, object?> root) { _root = root; }

            public void Check(Dictionary<string, object?> schema, object? value, string at, List<string> errors)
            {
                if (schema.TryGetValue("$ref", out var r))
                {
                    string name = Json.Str(r).Substring("#/definitions/".Length);
                    Check(Json.Obj(Json.Obj(_root["definitions"])[name]), value, at, errors);
                    return;
                }
                if (schema.TryGetValue("type", out var t))
                {
                    string type = Json.Str(t);
                    bool ok = type == "object" ? value is Dictionary<string, object?> : type == "array" ? value is List<object?> : type == "string" ? value is string
                        : type == "integer" ? value is long : type == "boolean" ? value is bool : false;
                    if (!ok) { errors.Add(at + ": not " + type); return; }
                }
                if (schema.TryGetValue("const", out var k) && !Same(k, value)) errors.Add(at + ": not " + k);
                if (schema.TryGetValue("enum", out var e) && !Json.Arr(e).Any(x => Same(x, value))) errors.Add(at + ": not one of the values");
                if (schema.TryGetValue("pattern", out var p) && value is string s && !Regex.IsMatch(s, Json.Str(p))) errors.Add(at + ": does not match " + p);
                if (value is Dictionary<string, object?> map)
                {
                    var props = schema.TryGetValue("properties", out var ps) ? Json.Obj(ps) : new Dictionary<string, object?>();
                    foreach (var kv in map)
                    {
                        if (props.TryGetValue(kv.Key, out var sub)) Check(Json.Obj(sub), kv.Value, at + "." + kv.Key, errors);
                        else if (schema.TryGetValue("additionalProperties", out var add))
                        {
                            if (add is bool b) { if (!b) errors.Add(at + "." + kv.Key + ": not allowed"); }
                            else Check(Json.Obj(add), kv.Value, at + "." + kv.Key, errors);
                        }
                    }
                    if (schema.TryGetValue("required", out var req)) foreach (var name in Json.Arr(req).Select(Json.Str)) if (!map.ContainsKey(name)) errors.Add(at + ": " + name + " is required");
                }
                if (value is List<object?> list && schema.TryGetValue("items", out var items))
                    for (int i = 0; i < list.Count; i++) Check(Json.Obj(items), list[i], at + "[" + i + "]", errors);
                if (schema.TryGetValue("allOf", out var all)) foreach (var sub in Json.Arr(all)) Check(Json.Obj(sub), value, at, errors);
                if (schema.TryGetValue("not", out var not))
                {
                    var inner = new List<string>();
                    Check(Json.Obj(not), value, at, inner);
                    if (inner.Count == 0) errors.Add(at + ": matches what it must not");
                }
                if (schema.TryGetValue("if", out var iff))
                {
                    var inner = new List<string>();
                    Check(Json.Obj(iff), value, at, inner);
                    if (inner.Count == 0 && schema.TryGetValue("then", out var then)) Check(Json.Obj(then), value, at, errors);
                }
            }

            static bool Same(object? a, object? b) => a is long x && b is long y ? x == y : Equals(a, b);
        }
    }
}
