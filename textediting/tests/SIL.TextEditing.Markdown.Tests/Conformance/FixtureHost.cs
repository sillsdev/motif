using System;
using System.Collections.Generic;
using System.Linq;

namespace SIL.TextEditing.Markdown.Tests.Conformance
{
    /// <summary>The host a fixture's <c>project</c> object describes (tests/conformance/README.md): writing systems, styles and the objects with their paths,
    /// GUIDs and tools. It is the whole project the conformance cases need, and a second implementation builds the same one from the same JSON.</summary>
    internal sealed class FixtureHost : IWritingSystemMapper, IKnownWritingSystems, IWritingSystemDetails, IStyleCatalog, IKnownStyles, IProjectPathResolver, IObjectMapper
    {
        public const char PictureType = (char)8;

        sealed class Entry
        {
            public Guid Guid;
            public string Kind = "link";
            public ProjectPath? Path;
            public string? Tool, Name, File, Caption;
            public bool Example, Deleted;
        }

        readonly List<Entry> _entries = new List<Entry>();
        readonly Dictionary<string, WritingSystemInfo> _details = new Dictionary<string, WritingSystemInfo>();
        readonly List<string> _known = new List<string>();
        readonly List<string> _paragraphStyles = new List<string>(), _characterStyles = new List<string>();

        /// <summary>The table styles the project names (spec 3.1: the host names them; a GDR template creates two), or null for the library's defaults.</summary>
        public string? TableHeaderStyle { get; private set; }
        public string? TableRowStyle { get; private set; }

        public string DefaultAnalysis { get; private set; } = "en";
        public string DefaultVernacular { get; private set; } = "ceb";

        public static FixtureHost From(Dictionary<string, object?>? project)
        {
            var host = new FixtureHost();
            if (project == null)
            {
                host._known.AddRange(new[] { "en", "ceb", "ceb-x-old", "ar" });
                host.TableHeaderStyle = "GDR Table Header"; // the tests supply table styles; the library has none
                host.TableRowStyle = "GDR Table Row";
                return host;
            }
            host.DefaultAnalysis = Json.OptStr(project, "analysis") ?? "en";
            host.DefaultVernacular = Json.OptStr(project, "vernacular") ?? "ceb";
            if (project.TryGetValue("writingSystems", out var wss) && wss != null)
            {
                foreach (var kv in Json.Obj(wss))
                {
                    host._known.Add(kv.Key);
                    if (kv.Value is Dictionary<string, object?> d)
                        host._details[kv.Key] = new WritingSystemInfo(Json.OptStr(d, "lang") ?? kv.Key, Json.OptStr(d, "dir") ?? "ltr", Json.OptStr(d, "font") ?? "Charis SIL");
                }
            }
            else host._known.AddRange(new[] { "en", "ceb", "ceb-x-old", "ar" });
            if (project.TryGetValue("tableStyles", out var tableStyles) && tableStyles != null)
            {
                host.TableHeaderStyle = Json.OptStr(Json.Obj(tableStyles), "header");
                host.TableRowStyle = Json.OptStr(Json.Obj(tableStyles), "row");
            }
            if (project.TryGetValue("styles", out var styles) && styles != null)
            {
                var s = Json.Obj(styles);
                if (s.TryGetValue("paragraph", out var p)) host._paragraphStyles.AddRange(Json.Arr(p).Select(Json.Str));
                if (s.TryGetValue("character", out var c)) host._characterStyles.AddRange(Json.Arr(c).Select(Json.Str));
            }
            if (project.TryGetValue("objects", out var objects) && objects != null)
            {
                foreach (var o in Json.Arr(objects).Select(Json.Obj))
                {
                    var entry = new Entry
                    {
                        Guid = Guid.Parse(Json.Str(o["guid"])),
                        Kind = Json.OptStr(o, "kind") ?? "link",
                        Tool = Json.OptStr(o, "tool"),
                        Name = Json.OptStr(o, "name"),
                        File = Json.OptStr(o, "file"),
                        Caption = Json.OptStr(o, "caption"),
                        Example = Json.OptBool(o, "example", false),
                        Deleted = Json.OptBool(o, "deleted", false),
                    };
                    string? path = Json.OptStr(o, "path");
                    if (path != null) entry.Path = ProjectPath.Parse(path);
                    host._entries.Add(entry);
                }
            }
            return host;
        }

        public bool HasStyles => _paragraphStyles.Count > 0 || _characterStyles.Count > 0;

        // ---------------------------------------------------------------- writing systems and styles

        public bool IsKnown(string tag) => _known.Contains(tag);
        public IReadOnlyList<string> KnownTags => _known;
        public WritingSystemInfo? DescribeWritingSystem(string id) => _details.TryGetValue(id, out var info) ? info : null;

        public bool IsParagraphStyle(string name) => _paragraphStyles.Contains(name);
        public bool IsCharacterStyle(string name) => _characterStyles.Contains(name);
        public IReadOnlyList<string> ParagraphStyleNames => _paragraphStyles;
        public IReadOnlyList<string> CharacterStyleNames => _characterStyles;

        // ---------------------------------------------------------------- paths

        static bool SameParent(ProjectPath a, ProjectPath b)
        {
            if (a.Segments.Count != b.Segments.Count) return false;
            for (int i = 0; i < a.Segments.Count - 1; i++) if (!a.Segments[i].Equals(b.Segments[i])) return false;
            return true;
        }

        public Guid? Resolve(ProjectPath path, out IReadOnlyList<Guid> candidates)
        {
            var bare = new ProjectPath(path.Segments);
            var exact = _entries.Where(e => e.Kind == "link" && !e.Deleted && e.Path != null && e.Path.Equals(bare)).ToList();
            if (exact.Count == 1) { candidates = new Guid[0]; return exact[0].Guid; }
            if (exact.Count > 1) { candidates = exact.Select(e => e.Guid).ToList(); return null; }
            // A name with no homograph number matches every homograph of it.
            var last = bare.Segments.Count > 0 ? bare.Segments[bare.Segments.Count - 1] : null;
            var loose = last == null || last.Homograph != null || last.GuidSuffix != null ? new List<Entry>()
                : _entries.Where(e => e.Kind == "link" && !e.Deleted && e.Path != null && SameParent(e.Path, bare) && e.Path.Segments[e.Path.Segments.Count - 1].Name == last.Name).ToList();
            candidates = loose.Select(e => e.Guid).ToList();
            return loose.Count == 1 ? loose[0].Guid : (Guid?)null;
        }

        public ProjectPath? PathOf(Guid guid)
        {
            var entry = _entries.FirstOrDefault(e => e.Guid == guid && !e.Deleted);
            return entry?.Path;
        }

        public string? ToolOf(Guid guid) => _entries.FirstOrDefault(e => e.Guid == guid)?.Tool;

        // ---------------------------------------------------------------- object data

        public static string PictureData(Guid guid)
        {
            var bytes = guid.ToByteArray();
            var chars = new char[8];
            for (int i = 0; i < 8; i++) chars[i] = (char)(bytes[2 * i] | (bytes[2 * i + 1] << 8));
            return PictureType + new string(chars);
        }

        static Guid DecodeGuid(string text)
        {
            var bytes = new byte[16];
            for (int i = 0; i < 8; i++) { bytes[2 * i] = (byte)text[i]; bytes[2 * i + 1] = (byte)(text[i] >> 8); }
            return new Guid(bytes);
        }

        public MarkdownObject? Describe(string objData)
        {
            if (objData.Length == 9 && objData[0] == PictureType)
            {
                var guid = DecodeGuid(objData.Substring(1));
                var entry = _entries.FirstOrDefault(e => e.Guid == guid && e.Kind == "picture");
                if (entry == null) return null;
                return new MarkdownObject { Kind = MarkdownObjectKind.Picture, Guid = guid, Caption = entry.Caption ?? string.Empty, Path = entry.File };
            }
            var described = DefaultObjectMapper.Instance.Describe(objData);
            if (described != null && described.Kind == MarkdownObjectKind.InternalLink && described.Guid != null)
            {
                var entry = _entries.FirstOrDefault(e => e.Guid == described.Guid.Value);
                if (entry != null)
                {
                    described.Name = entry.Name;
                    described.Example = entry.Example;
                    described.Broken = entry.Deleted;
                }
            }
            return described;
        }

        public bool TryCreateObjData(MarkdownObject target, out string objData, out string? problem)
        {
            if (target.Kind == MarkdownObjectKind.Picture)
            {
                var entry = target.Guid == null ? null : _entries.FirstOrDefault(e => e.Guid == target.Guid && e.Kind == "picture");
                if (entry == null) { objData = string.Empty; problem = "no picture " + (target.Guid?.ToString() ?? target.Path); return false; }
                objData = PictureData(entry.Guid);
                problem = null;
                return true;
            }
            return DefaultObjectMapper.Instance.TryCreateObjData(target, out objData, out problem);
        }
    }
}
