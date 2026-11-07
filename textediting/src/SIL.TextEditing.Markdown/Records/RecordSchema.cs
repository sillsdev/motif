using System;
using System.Collections.Generic;
using System.Linq;

namespace SIL.TextEditing.Markdown.Records
{
    /// <summary>What a field of a record holds, and so how its YAML is read and written (ADR 0032, owner decision R-7). The schema types every value by
    /// its path; YAML's own typing (<c>no</c> as false, <c>1.10</c> as 1.1) is never used.</summary>
    public enum RecordFieldKind
    {
        /// <summary>Plain text (FieldWorks' Unicode and MultiUnicode): a double-quoted string, no markup.</summary>
        Text,
        /// <summary>FieldWorks Markdown (MultiString, String, StText): a <c>|-</c> literal block. See <see cref="RecordField.Paragraphs"/>.</summary>
        Rich,
        /// <summary>Text in a field notation (an environment, a feature structure), named by <see cref="RecordField.Notation"/>: a double-quoted string that
        /// the reader hands to the host's <see cref="INotationChecker"/>.</summary>
        Notation,
        /// <summary>A whole number, plain: <c>2</c>.</summary>
        Integer,
        /// <summary><c>true</c> or <c>false</c>, plain.</summary>
        Boolean,
        /// <summary>A GUID, double-quoted, lowercase, with hyphens.</summary>
        Guid,
        /// <summary>A reference to another object: one FieldWorks Markdown link with <c>guid=</c>, double-quoted.</summary>
        Reference,
        /// <summary>A list of <see cref="RecordField.Item"/>s, in stored order.</summary>
        Sequence,
        /// <summary>One owned object of type <see cref="RecordField.ObjectType"/>.</summary>
        Object,
    }

    /// <summary>Which of the file's two default writing systems unmarked text in a single-writing-system text field means.</summary>
    public enum RecordWritingSystem
    {
        Analysis,
        Vernacular,
    }

    /// <summary>One field of a record object type: its key in the YAML, its kind and what the kind needs. Made by <see cref="RecordObjectTypeBuilder"/>.</summary>
    public sealed class RecordField
    {
        internal RecordField(string name, RecordFieldKind kind)
        {
            Name = name;
            Kind = kind;
        }

        /// <summary>The key in the YAML (<c>citation-form</c>). Empty for the item of a sequence.</summary>
        public string Name { get; }

        public RecordFieldKind Kind { get; }

        /// <summary>True when the field holds one value per writing system, as a mapping from writing-system id to value (<c>gloss: {en: "buy"}</c>).
        /// Only <see cref="RecordFieldKind.Text"/>, <see cref="RecordFieldKind.Rich"/> and <see cref="RecordFieldKind.Notation"/> fields can be.</summary>
        public bool MultiWritingSystem { get; internal set; }

        /// <summary>For <see cref="RecordFieldKind.Rich"/>: true for several paragraphs (an StText; read as a document), false for one paragraph of inline
        /// text (a MultiString or String; block syntax is <c>fwmd.field.block-syntax</c>).</summary>
        public bool Paragraphs { get; internal set; }

        internal BlockFeatures? Blocks { get; set; }

        /// <summary>For a rich field of paragraphs: the block kinds its text may hold, a field's block-features setting (ADR 0033, owner decision Q79). Default
        /// <see cref="SIL.TextEditing.BlockFeatures.All"/>. The reader reports <c>fwmd.record.block-not-allowed</c> for a block the field does not allow. For every
        /// other field, one paragraph of inline text included, it is <see cref="SIL.TextEditing.BlockFeatures.None"/>: such a field holds no block at all.</summary>
        public BlockFeatures BlockFeatures => Kind == RecordFieldKind.Rich && Paragraphs ? Blocks ?? BlockFeatures.All : BlockFeatures.None;

        /// <summary>For a field that is not per writing system: the default writing system its unmarked text means.</summary>
        public RecordWritingSystem WritingSystem { get; internal set; }

        /// <summary>For <see cref="RecordFieldKind.Notation"/>: the notation's name (<c>environment</c>).</summary>
        public string? Notation { get; internal set; }

        /// <summary>For <see cref="RecordFieldKind.Object"/>, and a sequence of objects: the object type's name.</summary>
        public string? ObjectTypeName { get; internal set; }

        /// <summary>For <see cref="RecordFieldKind.Object"/>: the object type, once the schema is built.</summary>
        public RecordObjectType? ObjectType { get; internal set; }

        /// <summary>For <see cref="RecordFieldKind.Sequence"/>: what each item is.</summary>
        public RecordField? Item { get; internal set; }

        /// <summary>The media type of a text value, as the JSON Schema gives it: <c>text/plain</c>, <c>text/x-fieldworks-markdown</c> (rich text and
        /// references) or <c>text/x-fieldworks-&lt;notation&gt;</c>; null for a value that is not text.</summary>
        public string? ContentMediaType
        {
            get
            {
                switch (Kind)
                {
                    case RecordFieldKind.Text: return "text/plain";
                    case RecordFieldKind.Rich: return Paragraphs ? RecordSchema.MarkdownMediaType : RecordSchema.InlineMarkdownMediaType;
                    case RecordFieldKind.Reference: return RecordSchema.InlineMarkdownMediaType;
                    case RecordFieldKind.Notation: return "text/x-fieldworks-" + Notation;
                    default: return null;
                }
            }
        }

        /// <summary>True for a kind whose value is a single scalar (everything but sequences, objects and per-writing-system fields).</summary>
        internal bool IsLeaf => !MultiWritingSystem && Kind != RecordFieldKind.Sequence && Kind != RecordFieldKind.Object;

        /// <summary>True when a string value of this field is written double-quoted (text, notations, references and GUIDs).</summary>
        internal bool IsQuotedText => Kind == RecordFieldKind.Text || Kind == RecordFieldKind.Notation || Kind == RecordFieldKind.Reference || Kind == RecordFieldKind.Guid;

        public override string ToString() => (Name.Length == 0 ? "item" : Name) + ": " + Kind + (MultiWritingSystem ? " per writing system" : string.Empty);
    }

    /// <summary>An object type: the fields an object of it may have, in the order the writer writes them. Its <c>guid</c> comes first when
    /// <see cref="HasGuid"/> (ADR 0032).</summary>
    public sealed class RecordObjectType
    {
        readonly Dictionary<string, RecordField> _byName;

        internal RecordObjectType(string name, bool hasGuid, IReadOnlyList<RecordField> fields)
        {
            Name = name;
            HasGuid = hasGuid;
            Fields = fields;
            _byName = fields.ToDictionary(f => f.Name, StringComparer.Ordinal);
        }

        public string Name { get; }

        /// <summary>True when objects of this type carry their identity as a <c>guid:</c> key, written first. An object with no GUID is a new one.</summary>
        public bool HasGuid { get; }

        /// <summary>The fields, in canonical order (the <c>guid</c> key is not one of them).</summary>
        public IReadOnlyList<RecordField> Fields { get; }

        /// <summary>The field of that key, or null.</summary>
        public RecordField? Field(string name) => _byName.TryGetValue(name, out var f) ? f : null;

        public override string ToString() => Name;
    }

    /// <summary>A record kind (<c>lexicon/entry</c>, <c>notebook/record</c>): the type of its root object, how many segments of a project path name the
    /// record itself, and the field written as the Markdown body, if the kind has one (owner decision R-6).</summary>
    public sealed class RecordKind
    {
        internal RecordKind(string name, RecordObjectType root, int pathSegments, RecordField? body)
        {
            Name = name;
            Root = root;
            PathSegments = pathSegments;
            BodyField = body;
        }

        /// <summary>What the file's <c>record:</c> key says.</summary>
        public string Name { get; }

        public RecordObjectType Root { get; }

        /// <summary>How many segments of a <c>root:</c> path name the record (<c>/lexicon/palit(2)</c> is 2). The segments after them walk the record's
        /// owned objects: a field name, then for a sequence a 1-based index (<c>/lexicon/palit(2)/senses/1</c>).</summary>
        public int PathSegments { get; }

        /// <summary>The root object's field whose value is the Markdown body below the front matter, or null when the kind has no body (an entry).
        /// It is a rich field of paragraphs in one writing system.</summary>
        public RecordField? BodyField { get; }

        public override string ToString() => Name;
    }

    /// <summary>The record schema: data, not code (owner decisions R-7 and Q54′). It names the record kinds, the object types and every field's kind,
    /// so the reader types each value by its path and the writer knows the key order. <c>SIL.TextEditing.Project</c> builds one from liblcm's metadata;
    /// tests build one by hand with <see cref="RecordSchemaBuilder"/>.</summary>
    public sealed class RecordSchema
    {
        /// <summary>The media type of FieldWorks Markdown text of paragraphs (an StText; a body).</summary>
        public const string MarkdownMediaType = "text/x-fieldworks-markdown";

        /// <summary>The media type of one paragraph of inline FieldWorks Markdown (a MultiString or String, and a reference's link).</summary>
        public const string InlineMarkdownMediaType = "text/x-fieldworks-markdown-inline";

        readonly Dictionary<string, RecordKind> _kinds;
        readonly Dictionary<string, RecordObjectType> _types;

        internal RecordSchema(string id, IReadOnlyList<RecordKind> kinds, IReadOnlyList<RecordObjectType> types)
        {
            Id = id;
            Kinds = kinds;
            ObjectTypes = types;
            _kinds = kinds.ToDictionary(k => k.Name, StringComparer.Ordinal);
            _types = types.ToDictionary(t => t.Name, StringComparer.Ordinal);
        }

        /// <summary>What a record file's <c>schema:</c> key says: the id of the schema, which is also the <c>$id</c> of the JSON Schema it publishes
        /// (<see cref="ToJsonSchema"/>), so the key points to that document.</summary>
        public string Id { get; }

        public IReadOnlyList<RecordKind> Kinds { get; }
        public IReadOnlyList<RecordObjectType> ObjectTypes { get; }

        public RecordKind? Kind(string name) => _kinds.TryGetValue(name, out var k) ? k : null;
        public RecordObjectType? ObjectType(string name) => _types.TryGetValue(name, out var t) ? t : null;

        /// <summary>The field at a schema path from a kind's root object: field names joined by <c>/</c>, no indices or writing systems
        /// (<c>senses/examples/translations/translation</c>). Null when the path names no field.</summary>
        public RecordField? FieldAt(string kind, string path)
        {
            var k = Kind(kind);
            if (k == null || path == null) return null;
            var type = k.Root;
            RecordField? field = null;
            foreach (string name in path.Split('/'))
            {
                if (type == null) return null;
                field = type.Field(name);
                if (field == null) return null;
                var target = field.Kind == RecordFieldKind.Sequence ? field.Item : field;
                type = target != null && target.Kind == RecordFieldKind.Object ? target.ObjectType : null;
            }
            return field;
        }

        /// <summary>The schema as a JSON Schema document (draft-07), for editors such as VS Code's YAML extension: the front matter's keys, each record
        /// kind's object types, and <c>contentMediaType</c> on every text value (owner decision R-7).</summary>
        public string ToJsonSchema() => RecordJsonSchema.Write(this);
    }

    /// <summary>Builds a <see cref="RecordSchema"/>. Object types are named, so a type can refer to itself (subsenses) or to one declared later.</summary>
    public sealed class RecordSchemaBuilder
    {
        readonly string _id;
        readonly List<RecordObjectTypeBuilder> _types = new List<RecordObjectTypeBuilder>();
        readonly List<(string Name, string Type, int Segments, string? Body)> _kinds = new List<(string, string, int, string?)>();

        /// <param name="id">What a file's <c>schema:</c> says: the schema's id, also its JSON Schema's <c>$id</c>.</param>
        public RecordSchemaBuilder(string id)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("A schema has an id.", nameof(id));
            _id = id;
        }

        /// <summary>Declares an object type and returns its builder. <paramref name="hasGuid"/>: objects of it carry <c>guid:</c> (owned FieldWorks objects do).</summary>
        public RecordObjectTypeBuilder ObjectType(string name, bool hasGuid = true)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("An object type has a name.", nameof(name));
            if (_types.Any(t => t.Name == name)) throw new ArgumentException("The object type " + name + " is declared twice.", nameof(name));
            var builder = new RecordObjectTypeBuilder(name, hasGuid);
            _types.Add(builder);
            return builder;
        }

        /// <summary>Declares a record kind whose root object is of <paramref name="rootType"/>.</summary>
        /// <param name="pathSegments">How many segments of a project path name one record of this kind (<c>/lexicon/palit(2)</c>: 2).</param>
        /// <param name="bodyField">The root type's field written as the Markdown body, or null for none: a rich field of paragraphs, one writing system.</param>
        public RecordSchemaBuilder Kind(string name, string rootType, int pathSegments, string? bodyField = null)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("A record kind has a name.", nameof(name));
            if (pathSegments < 1) throw new ArgumentOutOfRangeException(nameof(pathSegments), "A record's path has at least one segment.");
            if (_kinds.Any(k => k.Name == name)) throw new ArgumentException("The record kind " + name + " is declared twice.", nameof(name));
            _kinds.Add((name, rootType, pathSegments, bodyField));
            return this;
        }

        /// <exception cref="InvalidOperationException">The declarations do not fit together: a type that is not declared, a body field that is not a
        /// rich field of paragraphs, a per-writing-system field of a kind that cannot be.</exception>
        public RecordSchema Build()
        {
            var types = _types.Select(b => b.Build()).ToList();
            var byName = types.ToDictionary(t => t.Name, StringComparer.Ordinal);
            foreach (var type in types)
            {
                foreach (var field in type.Fields)
                {
                    Resolve(field, type, byName);
                    if (field.Item != null) Resolve(field.Item, type, byName);
                }
            }
            var kinds = new List<RecordKind>();
            foreach (var (name, rootType, segments, body) in _kinds)
            {
                if (!byName.TryGetValue(rootType, out var root)) throw new InvalidOperationException("The record kind " + name + " names the object type " + rootType + ", which is not declared.");
                RecordField? bodyField = null;
                if (body != null)
                {
                    bodyField = root.Field(body) ?? throw new InvalidOperationException("The body field " + body + " of " + name + " is not a field of " + rootType + ".");
                    if (bodyField.Kind != RecordFieldKind.Rich || !bodyField.Paragraphs || bodyField.MultiWritingSystem)
                        throw new InvalidOperationException("The body field " + body + " of " + name + " must be a rich field of paragraphs in one writing system.");
                }
                kinds.Add(new RecordKind(name, root, segments, bodyField));
            }
            return new RecordSchema(_id, kinds, types);
        }

        static void Resolve(RecordField field, RecordObjectType owner, Dictionary<string, RecordObjectType> types)
        {
            if (field.ObjectTypeName == null) return;
            if (!types.TryGetValue(field.ObjectTypeName, out var target))
                throw new InvalidOperationException("The field " + owner.Name + "." + (field.Name.Length == 0 ? "item" : field.Name) + " names the object type " + field.ObjectTypeName + ", which is not declared.");
            field.ObjectType = target;
        }
    }

    /// <summary>Adds the fields of one object type, in canonical order.</summary>
    public sealed class RecordObjectTypeBuilder
    {
        readonly List<RecordField> _fields = new List<RecordField>();

        internal RecordObjectTypeBuilder(string name, bool hasGuid)
        {
            Name = name;
            HasGuid = hasGuid;
        }

        internal string Name { get; }
        internal bool HasGuid { get; }

        internal RecordObjectType Build() => new RecordObjectType(Name, HasGuid, _fields.ToList());

        RecordObjectTypeBuilder Add(RecordField field)
        {
            if (string.IsNullOrEmpty(field.Name)) throw new ArgumentException("A field has a name.");
            // A field path joins names with / and puts a writing system after a dot; a fragment starts with #.
            foreach (char c in field.Name)
                if (c == '.' || c == '/' || c == '#' || c < ' ' || char.IsWhiteSpace(c)) throw new ArgumentException("A field name has no '.', '/', '#', space or control character: " + field.Name);
            if (field.Name == "guid" && HasGuid) throw new ArgumentException("guid is the identity key of " + Name + "; it is not declared as a field.");
            if (_fields.Any(f => f.Name == field.Name)) throw new ArgumentException("The field " + Name + "." + field.Name + " is declared twice.");
            _fields.Add(field);
            return this;
        }

        /// <summary>Plain text in one writing system.</summary>
        public RecordObjectTypeBuilder Text(string name, RecordWritingSystem ws = RecordWritingSystem.Analysis)
            => Add(new RecordField(name, RecordFieldKind.Text) { WritingSystem = ws });

        /// <summary>Plain text per writing system (MultiUnicode).</summary>
        public RecordObjectTypeBuilder MultiText(string name) => Add(new RecordField(name, RecordFieldKind.Text) { MultiWritingSystem = true });

        /// <summary>FieldWorks Markdown in one writing system: one paragraph (a String) or, with <paramref name="paragraphs"/>, several (an StText).</summary>
        /// <param name="blocks">For a field of paragraphs, the block kinds it allows; default <see cref="BlockFeatures.All"/>. A field of one paragraph allows none, and says
        /// <see cref="BlockFeatures.None"/> or nothing (ADR 0033).</param>
        /// <exception cref="ArgumentException">A field of one paragraph is given a block kind, or <paramref name="blocks"/> holds a bit that is not a kind.</exception>
        public RecordObjectTypeBuilder Rich(string name, bool paragraphs = false, RecordWritingSystem ws = RecordWritingSystem.Analysis, BlockFeatures? blocks = null)
            => Add(new RecordField(name, RecordFieldKind.Rich) { Paragraphs = paragraphs, WritingSystem = ws, Blocks = CheckBlocks(name, paragraphs, blocks) });

        /// <summary>FieldWorks Markdown per writing system (a MultiString: one paragraph each).</summary>
        public RecordObjectTypeBuilder MultiRich(string name, bool paragraphs = false, BlockFeatures? blocks = null)
            => Add(new RecordField(name, RecordFieldKind.Rich) { Paragraphs = paragraphs, MultiWritingSystem = true, Blocks = CheckBlocks(name, paragraphs, blocks) });

        static BlockFeatures? CheckBlocks(string name, bool paragraphs, BlockFeatures? blocks)
        {
            if (blocks == null) return null;
            if ((blocks.Value & ~BlockFeatures.All) != 0) throw new ArgumentException("Block features of " + name + " name tables, lists and headings only: " + blocks.Value, nameof(blocks));
            if (!paragraphs && blocks.Value != BlockFeatures.None) throw new ArgumentException("The field " + name + " holds one paragraph of inline text, so it holds no block: its block features are None.", nameof(blocks));
            return blocks;
        }

        /// <summary>Text in the named notation, in one writing system.</summary>
        public RecordObjectTypeBuilder Notation(string name, string notation, RecordWritingSystem ws = RecordWritingSystem.Analysis)
            => Add(new RecordField(name, RecordFieldKind.Notation) { Notation = NotationName(notation), WritingSystem = ws });

        /// <summary>Text in the named notation, per writing system.</summary>
        public RecordObjectTypeBuilder MultiNotation(string name, string notation)
            => Add(new RecordField(name, RecordFieldKind.Notation) { Notation = NotationName(notation), MultiWritingSystem = true });

        public RecordObjectTypeBuilder Integer(string name) => Add(new RecordField(name, RecordFieldKind.Integer));
        public RecordObjectTypeBuilder Boolean(string name) => Add(new RecordField(name, RecordFieldKind.Boolean));
        public RecordObjectTypeBuilder Guid(string name) => Add(new RecordField(name, RecordFieldKind.Guid));
        public RecordObjectTypeBuilder Reference(string name) => Add(new RecordField(name, RecordFieldKind.Reference));

        /// <summary>One owned object of the named type.</summary>
        public RecordObjectTypeBuilder Object(string name, string objectType)
            => Add(new RecordField(name, RecordFieldKind.Object) { ObjectTypeName = objectType });

        /// <summary>A sequence of owned objects of the named type.</summary>
        public RecordObjectTypeBuilder Objects(string name, string objectType)
            => Add(Sequence(name, new RecordField(string.Empty, RecordFieldKind.Object) { ObjectTypeName = objectType }));

        /// <summary>A sequence of references.</summary>
        public RecordObjectTypeBuilder References(string name) => Add(Sequence(name, new RecordField(string.Empty, RecordFieldKind.Reference)));

        /// <summary>A sequence of plain texts in one writing system.</summary>
        public RecordObjectTypeBuilder Texts(string name, RecordWritingSystem ws = RecordWritingSystem.Analysis)
            => Add(Sequence(name, new RecordField(string.Empty, RecordFieldKind.Text) { WritingSystem = ws }));

        /// <summary>A sequence of texts in the named notation.</summary>
        public RecordObjectTypeBuilder Notations(string name, string notation, RecordWritingSystem ws = RecordWritingSystem.Analysis)
            => Add(Sequence(name, new RecordField(string.Empty, RecordFieldKind.Notation) { Notation = NotationName(notation), WritingSystem = ws }));

        static RecordField Sequence(string name, RecordField item) => new RecordField(name, RecordFieldKind.Sequence) { Item = item };

        static string NotationName(string notation)
        {
            if (string.IsNullOrEmpty(notation)) throw new ArgumentException("A notation has a name.", nameof(notation));
            foreach (char c in notation)
            {
                if (!((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-')) throw new ArgumentException("A notation name is lowercase letters, digits and hyphens: " + notation, nameof(notation));
            }
            return notation;
        }
    }
}
