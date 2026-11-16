using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Json = SIL.TextEditing.Markdown.Records.SchemaJsonReader;
using J = SIL.TextEditing.Markdown.Records.RecordJsonSchema.J;

namespace SIL.TextEditing.Markdown.Records
{
    internal static class RecordSchemaJson
    {
        public static string Write(RecordSchema schema)
        {
            var types = new J.Arr();
            foreach (var type in schema.ObjectTypes)
            {
                var fields = new J.Arr(type.Fields.Select(f => (J.Node)Field(f)));
                types.Items.Add(new J.Obj().Add("name", type.Name)
                    .Add("guid", Bool(type.HasGuid)).Add("fields", fields));
            }
            var kinds = new J.Arr();
            foreach (var kind in schema.Kinds)
            {
                var value = new J.Obj().Add("name", kind.Name).Add("root", kind.Root.Name)
                    .Add("pathSegments", Number(kind.PathSegments));
                if (kind.BodyField != null) value.Add("body", kind.BodyField.Name);
                kinds.Items.Add(value);
            }
            var result = new StringBuilder();
            new J.Obj().Add("version", Number(RecordSchema.CurrentVersion)).Add("id", schema.Id)
                .Add("types", types).Add("kinds", kinds).Write(result, 0);
            return result.Append('\n').ToString();
        }

        static J.Raw Bool(bool value) => new J.Raw(value ? "true" : "false");
        static J.Raw Number(int value) => new J.Raw(value.ToString(CultureInfo.InvariantCulture));

        static J.Obj Field(RecordField field)
        {
            var value = new J.Obj();
            if (field.Name.Length != 0) value.Add("name", field.Name);
            value.Add("kind", field.Kind == RecordFieldKind.Sequence ? "seq" :
                field.Kind == RecordFieldKind.Collection ? "col" : field.Kind.ToString().ToLowerInvariant());
            if (field.Computed) value.Add("computed", Bool(true));
            if (field.MultiWritingSystem) value.Add("multi", Bool(true));
            if (field.Kind == RecordFieldKind.Text || field.Kind == RecordFieldKind.Rich || field.Kind == RecordFieldKind.Notation)
                if (!field.MultiWritingSystem)
                    value.Add("ws", field.WritingSystem == RecordWritingSystem.Vernacular ? "vernacular" : "analysis");
            if (field.Kind == RecordFieldKind.Rich)
            {
                value.Add("paragraphs", Bool(field.Paragraphs));
                if (field.Blocks.HasValue)
                {
                    var blocks = new J.Arr();
                    if ((field.BlockFeatures & BlockFeatures.Tables) != 0) blocks.Items.Add(new J.Str("tables"));
                    if ((field.BlockFeatures & BlockFeatures.Lists) != 0) blocks.Items.Add(new J.Str("lists"));
                    if ((field.BlockFeatures & BlockFeatures.Headings) != 0) blocks.Items.Add(new J.Str("headings"));
                    value.Add("blocks", blocks);
                }
            }
            if (field.Notation != null) value.Add("notation", field.Notation);
            if (field.ObjectTypeNames.Count == 1) value.Add("type", field.ObjectTypeNames[0]);
            else if (field.ObjectTypeNames.Count > 1)
                value.Add("types", new J.Arr(field.ObjectTypeNames.Select(n => (J.Node)new J.Str(n))));
            if (field.Item != null) value.Add("item", Field(field.Item));
            return value;
        }

        public static RecordSchema Read(string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            try
            {
                var json = Json.Obj(Json.Parse(text));
                int version = Json.OptInt(json, "version", 0);
                if (version < 1 || version > RecordSchema.CurrentVersion)
                    throw new FormatException("Unsupported record schema version " + version +
                        "; supported versions are 1 through " + RecordSchema.CurrentVersion + ".");
                return Build(json, version);
            }
            catch (Exception error) when (error is ArgumentException || error is InvalidOperationException ||
                error is KeyNotFoundException || error is InvalidCastException || error is OverflowException)
            {
                throw new FormatException("Invalid record schema: " + error.Message, error);
            }
        }

        static RecordSchema Build(Dictionary<string, object?> json, int version)
        {
            Keys(json, "version", "id", "types", "kinds");
            var builder = new RecordSchemaBuilder(Json.Str(json["id"]));
            foreach (var t in Json.Arr(json["types"]).Select(Json.Obj))
            {
                Keys(t, "name", "guid", "fields");
                var type = builder.ObjectType(Json.Str(t["name"]), Json.OptBool(t, "guid", true));
                var fields = Json.Arr(t["fields"]).Select(Json.Obj).ToList();
                type.AllowLegacyClass = fields.Any(f => Json.Str(f["name"]) == "class");
                foreach (var f in fields) AddField(type, f, version);
            }
            foreach (var k in Json.Arr(json["kinds"]).Select(Json.Obj))
            {
                Keys(k, "name", "root", "pathSegments", "body");
                builder.Kind(Json.Str(k["name"]), Json.Str(k["root"]), Json.OptInt(k, "pathSegments", 2), Json.OptStr(k, "body"));
            }
            return builder.Build();
        }

        static void Keys(Dictionary<string, object?> obj, params string[] allowed)
        {
            foreach (string key in obj.Keys)
                if (!allowed.Contains(key, StringComparer.Ordinal))
                    throw new FormatException("Unknown schema property: " + key);
        }

        static void FieldKeys(Dictionary<string, object?> field, string kind, bool item, int version)
        {
            var allowed = new List<string> { "kind" };
            if (!item)
            {
                allowed.Add("name");
                if (version >= 2) allowed.Add("computed");
            }
            if (kind == "text" || kind == "rich" || kind == "notation")
            {
                if (!item) allowed.Add("multi");
                if (!Json.OptBool(field, "multi", false)) allowed.Add("ws");
            }
            if (kind == "rich") { allowed.Add("paragraphs"); allowed.Add("blocks"); }
            if (kind == "notation") allowed.Add("notation");
            if (kind == "object")
            {
                allowed.Add("type");
                if (version >= 2) allowed.Add("types");
            }
            if (kind == "sequence" || (version >= 2 && (kind == "seq" || kind == "col"))) allowed.Add("item");
            Keys(field, allowed.ToArray());
        }

        static BlockFeatures? Blocks(Dictionary<string, object?> f)
        {
            if (!f.TryGetValue("blocks", out var list) || list == null) return null;
            var features = BlockFeatures.None;
            foreach (string name in Json.Arr(list).Select(Json.Str))
            {
                switch (name)
                {
                    case "tables": features |= BlockFeatures.Tables; break;
                    case "lists": features |= BlockFeatures.Lists; break;
                    case "headings": features |= BlockFeatures.Headings; break;
                    default: throw new InvalidOperationException("A block feature called " + name);
                }
            }
            return features;
        }

        static RecordWritingSystem Ws(Dictionary<string, object?> f)
        {
            switch (Json.OptStr(f, "ws"))
            {
                case null: case "analysis": return RecordWritingSystem.Analysis;
                case "vernacular": return RecordWritingSystem.Vernacular;
                default: throw new FormatException("A writing-system default is analysis or vernacular.");
            }
        }

        static string[] ObjectTypes(Dictionary<string, object?> field)
        {
            if (!field.TryGetValue("types", out var types)) return new[] { Json.Str(field["type"]) };
            if (field.ContainsKey("type")) throw new FormatException("An object field uses type or types, not both.");
            return Json.Arr(types).Select(Json.Str).ToArray();
        }

        static void AddField(RecordObjectTypeBuilder type, Dictionary<string, object?> f, int version)
        {
            string name = Json.Str(f["name"]);
            string kind = Json.Str(f["kind"]);
            FieldKeys(f, kind, false, version);
            if (version == 1 && (kind == "seq" || kind == "col"))
                throw new FormatException("seq and col require record schema version 2.");
            bool multi = Json.OptBool(f, "multi", false);
            if (multi && kind != "text" && kind != "rich" && kind != "notation")
                throw new FormatException("Only text, rich and notation fields can be per writing system.");
            switch (kind)
            {
                case "text": if (multi) type.MultiText(name); else type.Text(name, Ws(f)); break;
                case "rich":
                    if (multi) type.MultiRich(name, Json.OptBool(f, "paragraphs", false), Blocks(f));
                    else type.Rich(name, Json.OptBool(f, "paragraphs", false), Ws(f), Blocks(f));
                    break;
                case "notation": if (multi) type.MultiNotation(name, Json.Str(f["notation"])); else type.Notation(name, Json.Str(f["notation"]), Ws(f)); break;
                case "integer": type.Integer(name); break;
                case "boolean": type.Boolean(name); break;
                case "guid": type.Guid(name); break;
                case "reference": type.Reference(name); break;
                case "object": type.Object(name, ObjectTypes(f)); break;
                case "seq":
                case "col":
                case "sequence":
                {
                    var item = Json.Obj(f["item"]);
                    FieldKeys(item, Json.Str(item["kind"]), true, version);
                    switch (Json.Str(item["kind"]))
                    {
                        case "object": type.Objects(name, ObjectTypes(item)); break;
                        case "reference": type.References(name); break;
                        case "text": type.Texts(name, Ws(item)); break;
                        case "notation": type.Notations(name, Json.Str(item["notation"]), Ws(item)); break;
                        default: throw new InvalidOperationException("A sequence item of kind " + item["kind"]);
                    }
                    break;
                }
                default: throw new InvalidOperationException("A field of kind " + kind);
            }
            if (kind == "col") type.AsCollection(name);
            if (Json.OptBool(f, "computed", false)) type.Computed(name);
        }

    }
}
