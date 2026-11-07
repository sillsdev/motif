using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SIL.TextEditing.Markdown.Records;
using SIL.TextEditing.Markdown.Tests.Conformance;

namespace SIL.TextEditing.Markdown.Tests.Records
{
    /// <summary>One case of a record-file fixture (tests/conformance/README.md, "Record files").</summary>
    internal sealed class RecordCase
    {
        public string File = string.Empty, Name = string.Empty;
        public string Id => "records/" + File + "::" + Name;
        public string SchemaName = "test";
        public RecordSchema Schema = null!;
        /// <summary>The input text; null for a write case.</summary>
        public string? Text;
        public Dictionary<string, object?>? Document;
        public List<ExpectedFinding> Findings = new List<ExpectedFinding>();
        public bool CanonicalSpecified;
        public string? Canonical;
        public bool Pandoc = true;
        public string? PandocReason;
        public Dictionary<string, object?>? Project;
        public Dictionary<string, object?>? Notations;
        public Dictionary<string, List<Paragraph>> Paragraphs = new Dictionary<string, List<Paragraph>>();
        public Dictionary<string, string?> Targets = new Dictionary<string, string?>();

        public bool IsRead => Text != null;
        public bool HasErrors => Findings.Any(f => f.Severity == "error");

        /// <summary>The text that is canonical for this case, or null: the canonical string, or the input when <c>"canonical": null</c>.</summary>
        public string? CanonicalText => !CanonicalSpecified ? null : Canonical ?? Text;

        public override string ToString() => Id;
    }

    /// <summary>Loads tests/conformance/records: <c>schemas.json</c> (named schemas as data) and the case files.</summary>
    internal static class RecordFixtures
    {
        static List<RecordCase>? _all;
        static Dictionary<string, RecordSchema>? _schemas;
        static Dictionary<string, Dictionary<string, object?>>? _schemaJson;

        public static string Directory => Path.Combine(ConformanceLoader.Directory, "records");

        public static RecordSchema Schema(string name)
        {
            Load();
            return _schemas![name];
        }

        public static Dictionary<string, object?> SchemaJson(string name)
        {
            Load();
            return _schemaJson![name];
        }

        public static IReadOnlyList<RecordCase> All() => _all ?? (_all = Load());

        public static RecordCase Find(string id) => All().First(c => c.Id == id);

        static List<RecordCase> Load()
        {
            if (_schemas == null)
            {
                _schemas = new Dictionary<string, RecordSchema>(StringComparer.Ordinal);
                _schemaJson = new Dictionary<string, Dictionary<string, object?>>(StringComparer.Ordinal);
                foreach (var kv in Json.Obj(Json.Parse(ConformanceLoader.ReadText(Path.Combine(Directory, "schemas.json")))))
                {
                    _schemaJson[kv.Key] = Json.Obj(kv.Value);
                    _schemas[kv.Key] = BuildSchema(Json.Obj(kv.Value));
                }
            }
            if (_all != null) return _all;
            var cases = new List<RecordCase>();
            foreach (string path in System.IO.Directory.GetFiles(Directory, "*.json").OrderBy(p => p, StringComparer.Ordinal))
            {
                string name = Path.GetFileName(path);
                if (name == "schemas.json" || name.EndsWith(".schema.json", StringComparison.Ordinal)) continue;
                foreach (var item in Json.Arr(Json.Parse(ConformanceLoader.ReadText(path)))) cases.Add(LoadCase(name, Json.Obj(item)));
            }
            var duplicate = cases.GroupBy(c => c.Id).FirstOrDefault(g => g.Count() > 1);
            if (duplicate != null) throw new InvalidOperationException("Two record cases are called " + duplicate.Key);
            return _all = cases;
        }

        static RecordCase LoadCase(string file, Dictionary<string, object?> o)
        {
            var c = new RecordCase { File = file, Name = Json.Str(o["name"]) };
            c.SchemaName = Json.OptStr(o, "schema") ?? "test";
            c.Schema = _schemas![c.SchemaName];
            c.Text = Json.OptStr(o, "file");
            if (o.TryGetValue("document", out var d) && d != null) c.Document = Json.Obj(d);
            if (o.TryGetValue("findings", out var fs) && fs != null)
            {
                foreach (var f in Json.Arr(fs).Select(Json.Obj))
                {
                    var finding = new ExpectedFinding
                    {
                        Id = Json.Str(f["id"]),
                        Severity = Json.OptStr(f, "severity") ?? "error",
                        Line = Json.OptInt(f, "line", 1),
                        Column = Json.OptInt(f, "column", 1),
                        EndLine = Json.OptInt(f, "endLine", Json.OptInt(f, "line", 1)),
                        EndColumn = Json.OptInt(f, "endColumn", 1),
                        HasFix = f.ContainsKey("fix"),
                        Fix = Json.OptStr(f, "fix"),
                    };
                    if (f.TryGetValue("choices", out var ch) && ch != null) finding.Choices = Json.Arr(ch).Select(Json.Str).ToList();
                    if (f.TryGetValue("nearest", out var ne) && ne != null) finding.Nearest = Json.Arr(ne).Select(Json.Str).ToList();
                    c.Findings.Add(finding);
                }
            }
            if (o.ContainsKey("canonical")) { c.CanonicalSpecified = true; c.Canonical = Json.OptStr(o, "canonical"); }
            if (o.TryGetValue("pandoc", out var pd))
            {
                if (pd is bool b) c.Pandoc = b;
                else if (pd is string reason) { c.Pandoc = false; c.PandocReason = reason; }
            }
            // A record case runs against the "records" project of projects.json unless it names another.
            object? pr = o.TryGetValue("project", out var named) && named != null ? named : "records";
            c.Project = pr is string name ? Json.Obj(Json.Obj(Json.Parse(ConformanceLoader.ReadText(Path.Combine(ConformanceLoader.Directory, "projects.json"))))[name]) : Json.Obj(pr);
            if (o.TryGetValue("notations", out var nt) && nt != null) c.Notations = Json.Obj(nt);
            if (o.TryGetValue("paragraphs", out var ps) && ps != null)
                foreach (var kv in Json.Obj(ps)) c.Paragraphs[kv.Key] = Json.Arr(kv.Value).Select(p => ConformanceLoader.Paragraph(Json.Obj(p))).ToList();
            if (o.TryGetValue("targets", out var ts) && ts != null)
                foreach (var kv in Json.Obj(ts)) c.Targets[kv.Key] = (string?)kv.Value;
            return c;
        }

        // ---------------------------------------------------------------- the schema as data

        /// <summary>Builds a schema from its JSON form: <c>id</c>, <c>types</c> (name, <c>guid</c>, fields) and <c>kinds</c> (name, root, pathSegments, body).</summary>
        public static RecordSchema BuildSchema(Dictionary<string, object?> json)
        {
            var builder = new RecordSchemaBuilder(Json.Str(json["id"]));
            foreach (var t in Json.Arr(json["types"]).Select(Json.Obj))
            {
                var type = builder.ObjectType(Json.Str(t["name"]), Json.OptBool(t, "guid", true));
                foreach (var f in Json.Arr(t["fields"]).Select(Json.Obj)) AddField(type, f);
            }
            foreach (var k in Json.Arr(json["kinds"]).Select(Json.Obj))
                builder.Kind(Json.Str(k["name"]), Json.Str(k["root"]), Json.OptInt(k, "pathSegments", 2), Json.OptStr(k, "body"));
            return builder.Build();
        }

        /// <summary>The block kinds a rich field allows, from its <c>blocks</c> list of <c>tables</c>, <c>lists</c> and <c>headings</c> (empty: none); absent: the default.</summary>
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

        static RecordWritingSystem Ws(Dictionary<string, object?> f) => Json.OptStr(f, "ws") == "vernacular" ? RecordWritingSystem.Vernacular : RecordWritingSystem.Analysis;

        static void AddField(RecordObjectTypeBuilder type, Dictionary<string, object?> f)
        {
            string name = Json.Str(f["name"]);
            string kind = Json.Str(f["kind"]);
            bool multi = Json.OptBool(f, "multi", false);
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
                case "object": type.Object(name, Json.Str(f["type"])); break;
                case "sequence":
                {
                    var item = Json.Obj(f["item"]);
                    switch (Json.Str(item["kind"]))
                    {
                        case "object": type.Objects(name, Json.Str(item["type"])); break;
                        case "reference": type.References(name); break;
                        case "text": type.Texts(name, Ws(item)); break;
                        case "notation": type.Notations(name, Json.Str(item["notation"]), Ws(item)); break;
                        default: throw new InvalidOperationException("A sequence item of kind " + item["kind"]);
                    }
                    break;
                }
                default: throw new InvalidOperationException("A field of kind " + kind);
            }
        }

        // ---------------------------------------------------------------- documents as data

        /// <summary>A document from its JSON form: the front-matter keys, <c>writingSystems</c> as a list of {id, lang, dir, font}, and <c>data</c>, whose
        /// objects, alternatives, lists and values the schema tells apart. A value is a string, or a JSON number or Boolean for an integer or Boolean field.</summary>
        public static RecordDocument BuildDocument(Dictionary<string, object?> json, RecordSchema schema)
        {
            var document = new RecordDocument(Json.Str(json["record"]))
            {
                Schema = Json.OptStr(json, "schema"),
                Root = Json.OptStr(json, "root"),
                Base = Json.OptStr(json, "base"),
                Analysis = Json.OptStr(json, "analysis"),
                Vernacular = Json.OptStr(json, "vernacular"),
            };
            if (json.TryGetValue("writingSystems", out var wss) && wss != null)
            {
                foreach (var w in Json.Arr(wss).Select(Json.Obj))
                    document.WritingSystems.Add(new KeyValuePair<string, WritingSystemInfo>(Json.Str(w["id"]), new WritingSystemInfo(Json.Str(w["lang"]), Json.Str(w["dir"]), Json.Str(w["font"]))));
            }
            var kind = schema.Kind(document.Kind)!;
            var type = RootType(kind, document.Root);
            if (json.TryGetValue("data", out var data) && data != null) document.Data = Object(Json.Obj(data), type);
            return document;
        }

        /// <summary>The object type a root names (the test walks the path the way the reader does).</summary>
        static RecordObjectType RootType(RecordKind kind, string? root)
        {
            var type = kind.Root;
            if (root == null) return type;
            var path = ProjectPath.Parse(root)!;
            for (int i = kind.PathSegments; i < path.Segments.Count; i++)
            {
                var field = type.Field(path.Segments[i].Name)!;
                if (field.Kind == RecordFieldKind.Sequence) { type = field.Item!.ObjectType!; i++; }
                else type = field.ObjectType!;
            }
            return type;
        }

        static RecordObject Object(Dictionary<string, object?> json, RecordObjectType type)
        {
            var o = new RecordObject();
            foreach (var kv in json)
            {
                if (kv.Key == "guid" && type.HasGuid) { o.Guid = Guid.Parse(Json.Str(kv.Value)); continue; }
                var field = type.Field(kv.Key) ?? throw new InvalidOperationException(type.Name + " has no field " + kv.Key + " (fixture)");
                o[kv.Key] = Value(kv.Value, field);
            }
            return o;
        }

        static RecordValue Value(object? json, RecordField field)
        {
            if (field.MultiWritingSystem)
            {
                var alts = new RecordAlternatives();
                foreach (var kv in Json.Obj(json)) alts.Add(kv.Key, Scalar(kv.Value));
                return alts;
            }
            switch (field.Kind)
            {
                case RecordFieldKind.Sequence:
                {
                    var list = new RecordList();
                    foreach (var item in Json.Arr(json)) list.Add(Value(item, field.Item!));
                    return list;
                }
                case RecordFieldKind.Object: return Object(Json.Obj(json), field.ObjectType!);
                default: return Scalar(json);
            }
        }

        static RecordScalar Scalar(object? json)
        {
            switch (json)
            {
                case string s: return new RecordScalar(s);
                case bool b: return RecordScalar.Of(b);
                case long l: return RecordScalar.Of(l);
                default: throw new InvalidOperationException("A fixture value of type " + (json?.GetType().Name ?? "null"));
            }
        }

        /// <summary>The options rich values are read with: the case's project's writing systems and styles, and links read verbatim (no project objects).</summary>
        public static RecordReadOptions Options(RecordCase c)
        {
            var host = FixtureHost.From(c.Project);
            var markdown = new MarkdownOptions { WritingSystems = host, Styles = host.HasStyles ? host : null };
            if (host.TableHeaderStyle != null) markdown.StyleNames.TableHeader = host.TableHeaderStyle;
            if (host.TableRowStyle != null) markdown.StyleNames.TableRow = host.TableRowStyle;
            if (c.Project != null && c.Project.ContainsKey("objects")) { markdown.Objects = host; markdown.Paths = host; }
            return new RecordReadOptions { Markdown = markdown, Notations = c.Notations == null ? null : new FixtureNotations(c.Notations) };
        }
    }

    /// <summary>The notation checker a case describes: for each notation, the strings it does not accept, each with the finding's code, message and fix.</summary>
    internal sealed class FixtureNotations : INotationChecker
    {
        readonly Dictionary<string, object?> _rules;

        public FixtureNotations(Dictionary<string, object?> rules) { _rules = rules; }

        public IReadOnlyList<LanguageDiagnostic> Check(string notation, string text, string path)
        {
            var found = new List<LanguageDiagnostic>();
            if (!_rules.TryGetValue(notation, out var list) || list == null) return found;
            foreach (var rule in Json.Arr(list).Select(Json.Obj))
            {
                string match = Json.Str(rule["match"]);
                int at = text.IndexOf(match, StringComparison.Ordinal);
                if (at < 0) continue;
                var range = new LanguageRange(at, at + match.Length);
                string? fix = Json.OptStr(rule, "fix");
                found.Add(new LanguageDiagnostic(range, DiagnosticSeverity.Error, Json.Str(rule["message"]), Json.OptStr(rule, "code"),
                    fix: fix == null ? null : new LanguageFix("fix", new[] { new LanguageTextEdit(range, fix) })));
            }
            return found;
        }
    }
}
