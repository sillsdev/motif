using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SIL.TextEditing.Markdown
{
    /// <summary>Where a piece of notation is in the text that was read: UTF-16 offsets (the end is exclusive) and the 1-based line and
    /// column of each end, the same numbers a finding carries.</summary>
    public sealed class MarkdownSourceRange
    {
        public MarkdownSourceRange(int start, int end, int line, int column, int endLine, int endColumn)
        {
            Start = start;
            End = end;
            Line = line;
            Column = column;
            EndLine = endLine;
            EndColumn = endColumn;
        }

        public int Start { get; }
        public int End { get; }
        public int Line { get; }
        public int Column { get; }
        public int EndLine { get; }
        public int EndColumn { get; }
    }

    /// <summary>What a host found wrong, or worth saying, about an object the notation spells: the same parts as a finding. The parser places it
    /// on the object's range and reports it. A problem that comes with a failure is an error; one that comes with success is a warning
    /// (<c>fwmd.link.broken</c>) or a suggestion.</summary>
    public sealed class MarkdownProblem
    {
        /// <summary>A problem with the default id: <c>fwmd.link.not-found</c> for a link, <c>fwmd.picture.unknown</c> for a picture.</summary>
        public MarkdownProblem(string message) : this(null, MarkdownSeverity.Error, message) { }

        public MarkdownProblem(string? id, MarkdownSeverity severity, string message)
        {
            Id = id;
            Severity = severity;
            Message = message ?? throw new ArgumentNullException(nameof(message));
        }

        /// <summary>The finding's id, for example <see cref="MarkdownDiagnosticIds.LinkAmbiguous"/>; null for the default of the object's kind.</summary>
        public string? Id { get; }
        public MarkdownSeverity Severity { get; }
        public string Message { get; }

        /// <summary>The ways to fix it when intent is ambiguous, for example one path for each entry that matches.</summary>
        public IReadOnlyList<string> Choices { get; set; } = Array.Empty<string>();

        /// <summary>The names the host suggests (<c>/lexicon/palit</c>, <c>/lexicon/palito</c> for <c>/lexicon/palt</c>).</summary>
        public IReadOnlyList<string> Nearest { get; set; } = Array.Empty<string>();

        /// <summary>One exact replacement of the observed text that fixes it, or null.</summary>
        public string? Replacement { get; set; }
    }

    /// <summary>What kind of target an object run's data holds, as far as the notation distinguishes them.</summary>
    public enum MarkdownObjectKind
    {
        /// <summary>A link to a path or URL that is not in the project: <c>[text](https://example.org/)</c>.</summary>
        ExternalLink,
        /// <summary>A link to an object of the project (spec 4): <c>[text](/lexicon/palit(2)){guid=...}</c> in any of its five forms.</summary>
        InternalLink,
        /// <summary>A picture: <c>![caption](/files/pictures/hut.jpg){guid=...}</c>.</summary>
        Picture,
    }

    /// <summary>The notation-level description of an object run's data: one link or picture as a record, of which every spelling in spec 4.4 is a projection.
    /// The host fills it when exporting (<see cref="IObjectMapper.Describe"/>) and receives it when importing; the converter never looks inside the data
    /// string.</summary>
    public sealed class MarkdownObject
    {
        public MarkdownObjectKind Kind { get; set; }

        /// <summary>External link: the path or URL. Internal link: the stored <c>silfw://</c> URL, set only when it differs from what the core builds from
        /// the GUID and tool (written <c>url="..."</c>).</summary>
        public string? Url { get; set; }

        /// <summary>Internal link or picture: the target's GUID, the identity.</summary>
        public Guid? Guid { get; set; }

        /// <summary>Internal link: the target's current name, which a <c>.name</c> link's text follows.</summary>
        public string? Name { get; set; }

        /// <summary>Internal link: the FLEx tool, when the host's path table cannot derive it (written <c>tool=...</c>).</summary>
        public string? Tool { get; set; }

        /// <summary>Internal link: the target was deleted (<c>.broken</c>).</summary>
        public bool Broken { get; set; }

        /// <summary>Internal link to a text segment or an example: <c>.example</c>. The host says so on export.</summary>
        public bool Example { get; set; }

        /// <summary>Picture: the caption shown as alt text on export; ignored on import.</summary>
        public string? Caption { get; set; }

        /// <summary>Picture: the file's LinkedFiles path (<c>/files/pictures/hut.jpg</c>) on export. Internal link: the target's path, if the host knows it
        /// (the writer otherwise asks <see cref="IProjectPathResolver.PathOf"/>). On import: the path as written, for the host to check.</summary>
        public string? Path { get; set; }

        /// <summary>The field the link's fragment names (<c>definition</c> in <c>#definition.en</c>). Reserved (spec 4.6): read and kept, not resolved in 1.0.</summary>
        public string? Field { get; set; }

        /// <summary>The writing-system alternative of <see cref="Field"/> (<c>en</c> in <c>#definition.en</c>).</summary>
        public string? FieldWritingSystem { get; set; }

        /// <summary>The project a back-link names when it is not this one (a cross-project link: not resolved here, never broken or rebound). Null for this project.</summary>
        public string? Project { get; set; }

        /// <summary>Import: the link was marked <c>.name</c>, so its text follows the target's name and the host may refresh it.</summary>
        public bool FollowsName { get; set; }

        /// <summary>Import: where the object is in the text (the whole <c>[text](dest){...}</c> or <c>![caption](dest){...}</c>). The host can place its own
        /// findings with it. Null on export.</summary>
        public MarkdownSourceRange? Source { get; set; }

        /// <summary>Import: the object data the host made for it, once it has; null when it could not.</summary>
        public string? ObjData { get; set; }
    }

    /// <summary>Maps an object run's data string (property 6) to the notation and back. Data it does not describe is written as
    /// <c>[...]{s6x="..."}</c>, so nothing is lost.</summary>
    public interface IObjectMapper
    {
        /// <summary>Export: describes the data, or returns null for "write it generically".</summary>
        MarkdownObject? Describe(string objData);

        /// <summary>Import: builds the data for an object the notation spelled. On failure <paramref name="problem"/> says what
        /// was not found, and the parser reports <c>fwmd.link.not-found</c> or <c>fwmd.picture.unknown</c>.</summary>
        bool TryCreateObjData(MarkdownObject target, out string objData, out string? problem);
    }

    /// <summary>Optional: an object mapper that can say more than yes or no. When <see cref="MarkdownOptions.Objects"/> implements it, the parser
    /// calls <see cref="TryResolve"/> instead of <see cref="IObjectMapper.TryCreateObjData"/>, so the host's own findings (<c>fwmd.link.not-found</c>
    /// with its nearest names, <c>fwmd.link.ambiguous</c> with its choices, <c>fwmd.link.broken</c> as a warning) reach the result with the line and
    /// column of the object.</summary>
    public interface IObjectResolver
    {
        /// <summary>Import: builds the data for an object the notation spelled. Returns true with the data when it can, and then
        /// <paramref name="problem"/> may still hold a warning or suggestion to report (a deleted target: <c>fwmd.link.broken</c>). Returns false when it
        /// cannot, and <paramref name="problem"/> says why (null for a plain "cannot be resolved"); that is always an error.
        /// <paramref name="target"/> carries the object's place in the text (<see cref="MarkdownObject.Source"/>).</summary>
        bool TryResolve(MarkdownObject target, out string objData, out MarkdownProblem? problem);
    }

    /// <summary>The mapper with no project: an object whose data is FieldWorks' external path name type (U+0004) and a URL is an external link, or,
    /// when the URL is a <c>silfw://</c> link with a GUID, a link to an object of the project; every other object is generic. Pictures need a host.</summary>
    public sealed class DefaultObjectMapper : IObjectMapper
    {
        public static readonly DefaultObjectMapper Instance = new DefaultObjectMapper();

        /// <summary>FieldWorks' kodtExternalPathName (4): an ObjData string is this character, then the path or URL.</summary>
        public const char ExternalPathNameType = '\u0004';

        public MarkdownObject? Describe(string objData)
        {
            if (objData.Length == 0 || objData[0] != ExternalPathNameType) return null;
            string url = objData.Substring(1);
            if (SilfwUrl.TryParse(url, out var parts) && parts.Guid != null)
            {
                var link = new MarkdownObject { Kind = MarkdownObjectKind.InternalLink, Guid = parts.Guid, Tool = parts.Tool };
                bool otherProject = parts.Database != null && parts.Database != SilfwUrl.CurrentProject;
                if (otherProject) link.Project = parts.Database;
                string tag = parts.Tag ?? string.Empty;
                bool fieldTag = tag.Length > 0 && !IsAllDigits(tag);
                if (fieldTag)
                {
                    int dot = tag.IndexOf('.');
                    link.Field = dot < 0 ? tag : tag.Substring(0, dot);
                    if (dot >= 0) link.FieldWritingSystem = tag.Substring(dot + 1);
                }
                // The URL travels in url= unless the core would build exactly this one from what the record holds.
                if (otherProject || SilfwUrl.Build(parts.Guid.Value, parts.Tool, fieldTag ? tag : string.Empty) != url) link.Url = url;
                return link;
            }
            return new MarkdownObject { Kind = MarkdownObjectKind.ExternalLink, Url = url };
        }

        static bool IsAllDigits(string s) { foreach (char c in s) if (c < '0' || c > '9') return false; return s.Length > 0; }

        public bool TryCreateObjData(MarkdownObject target, out string objData, out string? problem)
        {
            problem = null;
            if (target.Kind == MarkdownObjectKind.ExternalLink && target.Url != null)
            {
                objData = ExternalPathNameType + target.Url;
                return true;
            }
            if (target.Kind == MarkdownObjectKind.InternalLink && target.Guid != null)
            {
                string tag = target.Field == null ? string.Empty : target.FieldWritingSystem == null ? target.Field : target.Field + "." + target.FieldWritingSystem;
                objData = ExternalPathNameType + (target.Url ?? SilfwUrl.Build(target.Guid.Value, target.Tool, tag));
                return true;
            }
            objData = string.Empty;
            problem = target.Kind == MarkdownObjectKind.Picture ? "pictures need a host" : "links to project objects need a host";
            return false;
        }
    }

    /// <summary>The parts of a <c>silfw://</c> URL, the link FieldWorks stores in an object run for a link to a project object (spec 4.5).</summary>
    public sealed class SilfwUrlParts
    {
        /// <summary><c>database=</c>: the project, or <c>this$</c> for the current one.</summary>
        public string? Database { get; set; }
        public string? Tool { get; set; }
        public Guid? Guid { get; set; }
        /// <summary><c>tag=</c>: the field, a flid in FieldWorks.</summary>
        public string? Tag { get; set; }
        /// <summary>Every other parameter, in order.</summary>
        public List<KeyValuePair<string, string>> Extras { get; } = new List<KeyValuePair<string, string>>();
    }

    /// <summary>Builds and reads <c>silfw://localhost/link?...</c> URLs the way FieldWorks writes them: the whole query is URL-encoded, <c>database=</c> comes
    /// first and <c>tag=</c> is always written. This is built from the spec's example and the FieldWorks source; whether it is exactly what a real project
    /// stores is still to be verified (spec 4.5).</summary>
    public static class SilfwUrl
    {
        /// <summary>What FieldWorks writes for <c>database=</c> when the link is to the current project.</summary>
        public const string CurrentProject = "this$";

        const string Prefix = "silfw://localhost/link?";

        public static string Build(Guid guid, string? tool, string? tag = null, string? database = null)
        {
            var query = new StringBuilder("database=").Append(database ?? CurrentProject);
            if (!string.IsNullOrEmpty(tool)) query.Append("&tool=").Append(tool);
            query.Append("&guid=").Append(guid.ToString("D"));
            query.Append("&tag=").Append(tag ?? string.Empty);
            return Prefix + Encode(query.ToString());
        }

        public static bool TryParse(string url, out SilfwUrlParts parts)
        {
            parts = new SilfwUrlParts();
            if (!url.StartsWith("silfw://", StringComparison.OrdinalIgnoreCase)) return false;
            int q = url.IndexOf('?');
            if (q < 0) return false;
            string query = url.Substring(q + 1);
            // FieldWorks encodes the whole query, so = and & are %3d and %26; a hand-written one has them raw.
            if (query.IndexOf("%3d", StringComparison.OrdinalIgnoreCase) >= 0 || query.IndexOf("%26", StringComparison.OrdinalIgnoreCase) >= 0) query = Decode(query);
            foreach (string pair in query.Split('&'))
            {
                if (pair.Length == 0) continue;
                int eq = pair.IndexOf('=');
                string key = eq < 0 ? pair : pair.Substring(0, eq);
                string value = eq < 0 ? string.Empty : pair.Substring(eq + 1);
                switch (key)
                {
                    case "database": parts.Database = value; break;
                    case "tool": parts.Tool = value; break;
                    case "tag": parts.Tag = value; break;
                    case "guid": if (System.Guid.TryParse(value, out var g)) parts.Guid = g; else parts.Extras.Add(new KeyValuePair<string, string>(key, value)); break;
                    default: parts.Extras.Add(new KeyValuePair<string, string>(key, value)); break;
                }
            }
            return true;
        }

        static string Encode(string text)
        {
            var sb = new StringBuilder();
            foreach (byte b in Encoding.UTF8.GetBytes(text))
            {
                char c = (char)b;
                if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '-' || c == '_' || c == '.' || c == '!' || c == '*' || c == '(' || c == ')') sb.Append(c);
                else sb.Append('%').Append(((int)b).ToString("x2", CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }

        static string Decode(string text) => ProjectPath.PercentDecode(text.Replace('+', ' '));
    }
}
