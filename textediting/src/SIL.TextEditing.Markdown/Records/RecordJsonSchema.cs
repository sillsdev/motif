using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace SIL.TextEditing.Markdown.Records
{
    /// <summary>Writes a <see cref="RecordSchema"/> as a JSON Schema (draft-07) document for editors (owner decision R-7): the front matter's keys, one
    /// definition per object type, and for each record kind the type of <c>record-data</c>. Text values carry <c>contentMediaType</c>. Written by hand,
    /// two-space indented, keys in a fixed order, so the same schema always gives the same bytes.</summary>
    internal static class RecordJsonSchema
    {
        public const string GuidPattern = "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$";

        public static string Write(RecordSchema schema)
        {
            var root = new J.Obj()
                .Add("$schema", "http://json-schema.org/draft-07/schema#")
                .Add("$id", schema.Id)
                .Add("title", "FieldWorks record file front matter")
                .Add("type", "object");
            var props = new J.Obj()
                .Add("fwmd", new J.Obj().Add("const", new J.Raw("1")))
                .Add("record", new J.Obj().Add("enum", new J.Arr(schema.Kinds.Select(k => (J.Node)new J.Str(k.Name)))))
                .Add("schema", new J.Obj().Add("const", schema.Id))
                .Add("root", new J.Obj().Add("type", "string"))
                .Add("base", new J.Obj().Add("type", "string"))
                .Add("analysis", new J.Obj().Add("type", "string"))
                .Add("vernacular", new J.Obj().Add("type", "string"))
                .Add("writingSystems", new J.Obj()
                    .Add("type", "object")
                    .Add("additionalProperties", new J.Obj()
                        .Add("type", "object")
                        .Add("properties", new J.Obj()
                            .Add("lang", new J.Obj().Add("type", "string"))
                            .Add("dir", new J.Obj().Add("enum", new J.Arr(new J.Node[] { new J.Str("ltr"), new J.Str("rtl") })))
                            .Add("font", new J.Obj().Add("type", "string")))
                        .Add("additionalProperties", new J.Raw("false"))))
                .Add("record-data", new J.Obj().Add("type", "object"));
            root.Add("properties", props);
            root.Add("required", new J.Arr(new J.Node[] { new J.Str("fwmd"), new J.Str("record") }));
            root.Add("additionalProperties", new J.Raw("false"));

            // For each kind, a whole-record file's data (no root, or a root of the record's own segments) is its root type without the body field. A partial
            // file holds one of the kind's object types, or one field of one, which only the reader can tell apart; the editor checks only that it is an object.
            var all = new J.Arr();
            foreach (var kind in schema.Kinds)
            {
                var whole = new J.Obj()
                    .Add("if", new J.Obj().Add("properties", new J.Obj()
                        .Add("record", new J.Obj().Add("const", kind.Name))
                        .Add("root", new J.Obj().Add("pattern", "^(/[^/#]+){" + kind.PathSegments.ToString(CultureInfo.InvariantCulture) + "}$"))))
                    .Add("then", new J.Obj().Add("properties", new J.Obj().Add("record-data", Ref(DefinitionName(kind.Root, kind.BodyField)))));
                all.Items.Add(whole);
            }
            if (all.Items.Count > 0) root.Add("allOf", all);

            var defs = new J.Obj();
            foreach (var type in schema.ObjectTypes) defs.Add(type.Name, ObjectType(type, null));
            foreach (var kind in schema.Kinds)
            {
                if (kind.BodyField != null) defs.Add(DefinitionName(kind.Root, kind.BodyField), ObjectType(kind.Root, kind.BodyField));
            }
            root.Add("definitions", defs);
            var sb = new StringBuilder();
            root.Write(sb, 0);
            return sb.Append('\n').ToString();
        }

        static string DefinitionName(RecordObjectType type, RecordField? body) => body == null ? type.Name : type.Name + "--frame";

        static J.Obj Ref(string definition) => new J.Obj().Add("$ref", "#/definitions/" + definition);

        static J.Obj ObjectType(RecordObjectType type, RecordField? skip)
        {
            var props = new J.Obj();
            if (type.HasGuid) props.Add("guid", new J.Obj().Add("type", "string").Add("pattern", GuidPattern));
            foreach (var field in type.Fields)
            {
                if (field == skip) continue;
                props.Add(field.Name, Field(field));
            }
            return new J.Obj().Add("type", "object").Add("properties", props).Add("additionalProperties", new J.Raw("false"));
        }

        static J.Obj Field(RecordField field)
        {
            if (field.MultiWritingSystem)
                return new J.Obj().Add("type", "object").Add("additionalProperties", Leaf(field));
            switch (field.Kind)
            {
                case RecordFieldKind.Sequence: return new J.Obj().Add("type", "array").Add("items", Field(field.Item!));
                case RecordFieldKind.Object: return Ref(field.ObjectType!.Name);
                default: return Leaf(field);
            }
        }

        static J.Obj Leaf(RecordField field)
        {
            switch (field.Kind)
            {
                case RecordFieldKind.Integer: return new J.Obj().Add("type", "integer");
                case RecordFieldKind.Boolean: return new J.Obj().Add("type", "boolean");
                case RecordFieldKind.Guid: return new J.Obj().Add("type", "string").Add("pattern", GuidPattern);
                case RecordFieldKind.Object: return Ref(field.ObjectType!.Name);
                default:
                {
                    var text = new J.Obj().Add("type", "string").Add("contentMediaType", field.ContentMediaType!);
                    // The block kinds a rich field of paragraphs allows (ADR 0033), only where they are not all of them, so a schema that restricts nothing is unchanged.
                    if (field.Kind == RecordFieldKind.Rich && field.Paragraphs && field.BlockFeatures != BlockFeatures.All) text.Add("x-fieldworks-blocks", AllowedBlocks(field.BlockFeatures));
                    return text;
                }
            }
        }

        /// <summary>The allowed block kinds as lowercase names, in the order tables, lists, headings.</summary>
        static J.Arr AllowedBlocks(BlockFeatures features)
        {
            var names = new J.Arr();
            if ((features & BlockFeatures.Tables) != 0) names.Items.Add(new J.Str("tables"));
            if ((features & BlockFeatures.Lists) != 0) names.Items.Add(new J.Str("lists"));
            if ((features & BlockFeatures.Headings) != 0) names.Items.Add(new J.Str("headings"));
            return names;
        }

        /// <summary>A small JSON writer: objects keep the order keys were added.</summary>
        static class J
        {
            public abstract class Node
            {
                public abstract void Write(StringBuilder sb, int indent);
            }

            public sealed class Raw : Node
            {
                readonly string _text;
                public Raw(string text) { _text = text; }
                public override void Write(StringBuilder sb, int indent) => sb.Append(_text);
            }

            public sealed class Str : Node
            {
                readonly string _text;
                public Str(string text) { _text = text; }

                public override void Write(StringBuilder sb, int indent)
                {
                    sb.Append('"');
                    foreach (char c in _text)
                    {
                        switch (c)
                        {
                            case '"': sb.Append("\\\""); break;
                            case '\\': sb.Append("\\\\"); break;
                            case '\n': sb.Append("\\n"); break;
                            case '\r': sb.Append("\\r"); break;
                            case '\t': sb.Append("\\t"); break;
                            default:
                                if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                                else sb.Append(c);
                                break;
                        }
                    }
                    sb.Append('"');
                }
            }

            public sealed class Arr : Node
            {
                public readonly List<Node> Items = new List<Node>();
                public Arr() { }
                public Arr(IEnumerable<Node> items) { Items.AddRange(items); }

                public override void Write(StringBuilder sb, int indent)
                {
                    if (Items.All(i => i is Str || i is Raw))
                    {
                        sb.Append('[');
                        for (int i = 0; i < Items.Count; i++)
                        {
                            if (i > 0) sb.Append(", ");
                            Items[i].Write(sb, indent);
                        }
                        sb.Append(']');
                        return;
                    }
                    sb.Append("[\n");
                    for (int i = 0; i < Items.Count; i++)
                    {
                        sb.Append(' ', indent + 2);
                        Items[i].Write(sb, indent + 2);
                        sb.Append(i + 1 < Items.Count ? ",\n" : "\n");
                    }
                    sb.Append(' ', indent).Append(']');
                }
            }

            public sealed class Obj : Node
            {
                readonly List<KeyValuePair<string, Node>> _items = new List<KeyValuePair<string, Node>>();

                public Obj Add(string key, Node value)
                {
                    _items.Add(new KeyValuePair<string, Node>(key, value));
                    return this;
                }

                public Obj Add(string key, string value) => Add(key, new Str(value));

                public override void Write(StringBuilder sb, int indent)
                {
                    if (_items.Count == 0) { sb.Append("{}"); return; }
                    sb.Append("{\n");
                    for (int i = 0; i < _items.Count; i++)
                    {
                        sb.Append(' ', indent + 2);
                        new Str(_items[i].Key).Write(sb, indent + 2);
                        sb.Append(": ");
                        _items[i].Value.Write(sb, indent + 2);
                        sb.Append(i + 1 < _items.Count ? ",\n" : "\n");
                    }
                    sb.Append(' ', indent).Append('}');
                }
            }
        }
    }
}
