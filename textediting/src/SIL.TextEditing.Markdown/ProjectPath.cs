using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SIL.TextEditing.Markdown
{
    /// <summary>One segment of a <see cref="ProjectPath"/>: a name, and either a homograph number (<c>palit(2)</c>) or the first hex digits of a GUID
    /// (<c>Epenthesis(3fa29b1c)</c>) that tells siblings of one name apart. A segment never has both.</summary>
    public sealed class ProjectPathSegment : IEquatable<ProjectPathSegment>
    {
        public ProjectPathSegment(string name, int? homograph = null, string? guidSuffix = null)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            if (homograph != null && guidSuffix != null) throw new ArgumentException("A segment has a homograph number or a GUID suffix, not both.");
            Homograph = homograph;
            GuidSuffix = guidSuffix == null ? null : guidSuffix.ToLowerInvariant();
        }

        /// <summary>The name as text: percent-decoded, with no suffix.</summary>
        public string Name { get; }
        /// <summary>The homograph number, 1 to 999, when the segment has one.</summary>
        public int? Homograph { get; }
        /// <summary>Eight or more lowercase hex digits of the target's GUID, when the segment has them.</summary>
        public string? GuidSuffix { get; }

        /// <summary>The segment as text with no encoding: <c>palit(2)</c>.</summary>
        public override string ToString()
            => Homograph != null ? Name + "(" + Homograph.Value.ToString(CultureInfo.InvariantCulture) + ")" : GuidSuffix != null ? Name + "(" + GuidSuffix + ")" : Name;

        /// <summary>True when the segments name the same thing: names equal under canonical equivalence (case-sensitive), same suffix.</summary>
        public bool Equals(ProjectPathSegment? other)
            => other != null && Homograph == other.Homograph && GuidSuffix == other.GuidSuffix && SameName(Name, other.Name);

        public override bool Equals(object? obj) => Equals(obj as ProjectPathSegment);
        public override int GetHashCode() => TextNormalizer.Normalize(Name, NormalizationForm.FormD).GetHashCode();

        internal static bool SameName(string a, string b)
            => string.Equals(a, b, StringComparison.Ordinal)
               || string.Equals(TextNormalizer.Normalize(a, NormalizationForm.FormD), TextNormalizer.Normalize(b, NormalizationForm.FormD), StringComparison.Ordinal);
    }

    /// <summary>A path in the designed project tree (<c>/lexicon/palit(2)</c>, <c>/lists/GDR/GDR-47#description.en</c>): the grammar of spec 4.2 and 4.3, with
    /// no knowledge of any project. The core reads, writes and compares paths; a host turns them into objects (<see cref="IProjectPathResolver"/>).
    /// Segments are Unicode text. A path is written in NFC and compared under canonical equivalence, case-sensitively.</summary>
    public sealed class ProjectPath : IEquatable<ProjectPath>
    {
        /// <summary>The fixed top folders of the tree.</summary>
        static readonly string[] TopFolderNames = { "lexicon", "texts", "grammar", "notebook", "lists", "reversals", "objects", "files" };
        public static IReadOnlyList<string> TopFolders => TopFolderNames;

        public ProjectPath(IReadOnlyList<ProjectPathSegment> segments, string? field = null, string? fieldWritingSystem = null)
        {
            Segments = segments ?? throw new ArgumentNullException(nameof(segments));
            Field = field;
            FieldWritingSystem = fieldWritingSystem;
        }

        public ProjectPath(params string[] names) : this(Array.ConvertAll(names, n => new ProjectPathSegment(n))) { }

        public IReadOnlyList<ProjectPathSegment> Segments { get; }

        /// <summary>The field the fragment names (<c>definition</c> in <c>#definition.en</c>), or null. Reserved: written and read, not resolved in 1.0.</summary>
        public string? Field { get; }

        /// <summary>The writing-system alternative of the field (<c>en</c> in <c>#definition.en</c>), or null.</summary>
        public string? FieldWritingSystem { get; }

        /// <summary>The first segment's name (<c>lexicon</c>), or null for the root.</summary>
        public string? TopFolder => Segments.Count > 0 ? Segments[0].Name : null;

        /// <summary>True when the first segment is one of the tree's fixed top folders.</summary>
        public bool HasKnownTopFolder => TopFolder != null && Array.IndexOf(TopFolderNames, TopFolder) >= 0;

        /// <summary>The path of the GUID alias: <c>/objects/&lt;guid&gt;</c>.</summary>
        public static ProjectPath ForObject(Guid guid) => new ProjectPath("objects", guid.ToString("D"));

        /// <summary>The GUID when this is an <c>/objects/&lt;guid&gt;</c> alias.</summary>
        public Guid? ObjectGuid
            => Segments.Count == 2 && Segments[0].Name == "objects" && Segments[1].Homograph == null && Segments[1].GuidSuffix == null && Guid.TryParse(Segments[1].Name, out var g) ? g : (Guid?)null;

        /// <summary>Reads a path as written in a destination: <c>/lexicon/palit(2)</c>, percent-encoded, with an optional fragment. Returns null when the
        /// text is not a root path (it does not start with <c>/</c>). The reader splits on <c>/</c> before it decodes, so a <c>%2F</c> stays inside its segment.</summary>
        public static ProjectPath? Parse(string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            if (text.Length == 0 || text[0] != '/') return null;
            string? fragment = null;
            int hash = text.IndexOf('#');
            if (hash >= 0) { fragment = text.Substring(hash + 1); text = text.Substring(0, hash); }
            var segments = new List<ProjectPathSegment>();
            foreach (string raw in text.Substring(1).Split('/'))
            {
                if (raw.Length == 0 && segments.Count == 0 && text.Length == 1) break;
                segments.Add(ParseSegment(raw));
            }
            string? field = null, ws = null;
            if (fragment != null && fragment.Length > 0)
            {
                int dot = fragment.IndexOf('.');
                field = PercentDecode(dot < 0 ? fragment : fragment.Substring(0, dot));
                if (dot >= 0 && dot + 1 < fragment.Length) ws = PercentDecode(fragment.Substring(dot + 1));
            }
            return new ProjectPath(segments, field, ws);
        }

        static ProjectPathSegment ParseSegment(string raw)
        {
            // The suffix is taken from the raw text, before decoding: a name that itself ends in (2) has its parentheses written %28 and %29.
            int open = raw.LastIndexOf('(');
            if (open > 0 && raw.Length > 0 && raw[raw.Length - 1] == ')')
            {
                string inner = raw.Substring(open + 1, raw.Length - open - 2);
                if (inner.Length >= 1 && inner.Length <= 3 && AllDigits(inner) && inner[0] != '0')
                    return new ProjectPathSegment(PercentDecode(raw.Substring(0, open)), int.Parse(inner, CultureInfo.InvariantCulture));
                if (inner.Length >= 8 && AllHex(inner)) return new ProjectPathSegment(PercentDecode(raw.Substring(0, open)), null, inner);
            }
            // A subscript number at the end (palit₂) is read as a homograph number too.
            int sub = raw.Length;
            while (sub > 0 && raw[sub - 1] >= '₀' && raw[sub - 1] <= '₉') sub--;
            if (sub < raw.Length && sub > 0 && raw.Length - sub <= 3)
            {
                int number = 0;
                for (int i = sub; i < raw.Length; i++) number = number * 10 + (raw[i] - '₀');
                if (number > 0) return new ProjectPathSegment(PercentDecode(raw.Substring(0, sub)), number);
            }
            return new ProjectPathSegment(PercentDecode(raw));
        }

        static bool AllDigits(string s) { foreach (char c in s) if (c < '0' || c > '9') return false; return true; }
        static bool AllHex(string s) { foreach (char c in s) if (!Uri.IsHexDigit(c)) return false; return true; }

        /// <summary>Decodes <c>%XX</c> sequences as UTF-8; a sequence that is not valid UTF-8 stays as written. Surrogates that
        /// the encoder wrote as three-byte sequences come back as the lone code unit.</summary>
        internal static string PercentDecode(string text)
        {
            if (text.IndexOf('%') < 0) return text;
            var sb = new StringBuilder(text.Length);
            var bytes = new List<byte>();
            int i = 0;
            while (i < text.Length)
            {
                if (text[i] == '%' && i + 2 < text.Length && Uri.IsHexDigit(text[i + 1]) && Uri.IsHexDigit(text[i + 2]))
                {
                    bytes.Clear();
                    while (i + 2 < text.Length && text[i] == '%' && Uri.IsHexDigit(text[i + 1]) && Uri.IsHexDigit(text[i + 2]))
                    {
                        bytes.Add(byte.Parse(text.Substring(i + 1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                        i += 3;
                    }
                    DecodeBytes(bytes, sb);
                    continue;
                }
                sb.Append(text[i]);
                i++;
            }
            return sb.ToString();
        }

        static void DecodeBytes(List<byte> bytes, StringBuilder sb)
        {
            int i = 0;
            while (i < bytes.Count)
            {
                int b = bytes[i];
                int len = b < 0x80 ? 1 : (b & 0xE0) == 0xC0 ? 2 : (b & 0xF0) == 0xE0 ? 3 : (b & 0xF8) == 0xF0 ? 4 : 0;
                if (len == 0 || i + len > bytes.Count) { sb.Append('%').Append(b.ToString("X2", CultureInfo.InvariantCulture)); i++; continue; }
                int cp = len == 1 ? b : b & (0xFF >> (len + 1));
                bool ok = true;
                for (int k = 1; k < len; k++)
                {
                    if ((bytes[i + k] & 0xC0) != 0x80) { ok = false; break; }
                    cp = (cp << 6) | (bytes[i + k] & 0x3F);
                }
                if (!ok || cp > 0x10FFFF) { sb.Append('%').Append(b.ToString("X2", CultureInfo.InvariantCulture)); i++; continue; }
                if (cp >= 0x10000) sb.Append(char.ConvertFromUtf32(cp)); else sb.Append((char)cp);
                i += len;
            }
        }

        /// <summary>The path as the writer writes it: segments in NFC, percent-encoded where spec 4.3 says, a fragment last. The result may hold
        /// spaces; the link writer puts such a destination in angle brackets.</summary>
        public override string ToString()
        {
            var sb = new StringBuilder();
            if (Segments.Count == 0) sb.Append('/');
            foreach (var segment in Segments) sb.Append('/').Append(EncodeSegment(segment));
            if (Field != null)
            {
                sb.Append('#').Append(EncodePart(Field));
                if (FieldWritingSystem != null) sb.Append('.').Append(EncodePart(FieldWritingSystem));
            }
            return sb.ToString();
        }

        static string EncodePart(string text) => EncodeName(TextNormalizer.Normalize(text, NormalizationForm.FormC), false);

        static string EncodeSegment(ProjectPathSegment segment)
        {
            string name = TextNormalizer.Normalize(segment.Name, NormalizationForm.FormC);
            // A name that itself ends in (digits) or (8+ hex digits) has those parentheses encoded, so it is not read as a suffix.
            bool endsLikeSuffix = false;
            if (segment.Homograph == null && segment.GuidSuffix == null && name.Length > 2 && name[name.Length - 1] == ')')
            {
                int open = name.LastIndexOf('(');
                if (open > 0)
                {
                    string inner = name.Substring(open + 1, name.Length - open - 2);
                    endsLikeSuffix = (inner.Length >= 1 && inner.Length <= 3 && AllDigits(inner)) || (inner.Length >= 8 && AllHex(inner));
                }
            }
            // A subscript number at the end would be read as a homograph number: encode it.
            string text = EncodeName(name, endsLikeSuffix);
            if (segment.Homograph != null) text += "(" + segment.Homograph.Value.ToString(CultureInfo.InvariantCulture) + ")";
            else if (segment.GuidSuffix != null) text += "(" + segment.GuidSuffix + ")";
            return text;
        }

        static string EncodeName(string name, bool encodeLastParens)
        {
            // Parentheses are raw when balanced; an unbalanced one is encoded.
            var unmatched = new HashSet<int>();
            var stack = new Stack<int>();
            for (int i = 0; i < name.Length; i++)
            {
                if (name[i] == '(') stack.Push(i);
                else if (name[i] == ')') { if (stack.Count > 0) stack.Pop(); else unmatched.Add(i); }
            }
            foreach (int i in stack) unmatched.Add(i);
            int lastOpen = encodeLastParens ? name.LastIndexOf('(') : -1;
            var sb = new StringBuilder();
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                bool encode = c == '/' || c == '?' || c == '#' || c == '%' || c == '\\' || c == '<' || c == '>'
                    || c < ' ' || (c >= '\x7F' && c <= '\x9F') || c == Chars.LineSeparator || c == Chars.ParagraphSeparator
                    || ((c == '(' || c == ')') && (unmatched.Contains(i) || (encodeLastParens && (i == lastOpen || i == name.Length - 1))))
                    || (c >= '₀' && c <= '₉' && i == name.Length - 1);
                if (char.IsHighSurrogate(c) && i + 1 < name.Length && char.IsLowSurrogate(name[i + 1])) { sb.Append(c).Append(name[i + 1]); i++; continue; }
                if (char.IsSurrogate(c)) encode = true;
                if (!encode) { sb.Append(c); continue; }
                AppendPercent(sb, c);
            }
            return sb.ToString();
        }

        static void AppendPercent(StringBuilder sb, char c)
        {
            int cp = c;
            if (cp < 0x80) sb.Append('%').Append(cp.ToString("X2", CultureInfo.InvariantCulture));
            else if (cp < 0x800) sb.Append('%').Append((0xC0 | (cp >> 6)).ToString("X2", CultureInfo.InvariantCulture)).Append('%').Append((0x80 | (cp & 0x3F)).ToString("X2", CultureInfo.InvariantCulture));
            else sb.Append('%').Append((0xE0 | (cp >> 12)).ToString("X2", CultureInfo.InvariantCulture)).Append('%').Append((0x80 | ((cp >> 6) & 0x3F)).ToString("X2", CultureInfo.InvariantCulture))
                    .Append('%').Append((0x80 | (cp & 0x3F)).ToString("X2", CultureInfo.InvariantCulture));
        }

        /// <summary>True when both paths name the same place: the same segments under canonical equivalence, the same fragment.</summary>
        public bool Equals(ProjectPath? other)
        {
            if (other == null || Segments.Count != other.Segments.Count) return false;
            for (int i = 0; i < Segments.Count; i++) if (!Segments[i].Equals(other.Segments[i])) return false;
            return ProjectPathSegment.SameName(Field ?? string.Empty, other.Field ?? string.Empty) && ProjectPathSegment.SameName(FieldWritingSystem ?? string.Empty, other.FieldWritingSystem ?? string.Empty);
        }

        public override bool Equals(object? obj) => Equals(obj as ProjectPath);
        public override int GetHashCode() => Segments.Count == 0 ? 0 : Segments[0].GetHashCode();
    }

    /// <summary>What the host knows about a project's objects and their paths. The core defines the path grammar and the GUID rule and knows no
    /// project; with no resolver it reads and writes paths verbatim, keeps <c>guid=</c>, and writes a link whose path is unknown as
    /// <c>/objects/&lt;guid&gt;</c>.</summary>
    public interface IProjectPathResolver
    {
        /// <summary>Read side: the object at a path. Returns its GUID when exactly one object matches. When none or several do it returns null, and
        /// <paramref name="candidates"/> lists the GUIDs of the ones that match several, or of the nearest ones (it is empty for none).</summary>
        Guid? Resolve(ProjectPath path, out IReadOnlyList<Guid> candidates);

        /// <summary>Write side: the current path of an object, or null when it has none or the GUID is unknown in this project.</summary>
        ProjectPath? PathOf(Guid guid);

        /// <summary>The FLEx tool that edits the object, to rebuild a <c>silfw://</c> URL; null when unknown.</summary>
        string? ToolOf(Guid guid);
    }
}
