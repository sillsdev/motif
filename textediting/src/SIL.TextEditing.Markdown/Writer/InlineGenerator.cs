using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using SIL.TextEditing.Markdown.Notation;
using SIL.TextEditing.Markdown.Reader;

namespace SIL.TextEditing.Markdown.Writer
{
    /// <summary>The block a paragraph is written in, as far as writing its inline text needs to know.</summary>
    internal sealed class BlockContext
    {
        /// <summary>Text before the first line of the paragraph (<c>"# "</c>, <c>"- "</c>).</summary>
        public string FirstPrefix = string.Empty;
        /// <summary>Text before each later line, after a backslash line end (the list indent, <c>"> "</c>).</summary>
        public string ContinuationPrefix = string.Empty;
        /// <summary>Whether a line separator may be written as a backslash at the end of a line. A heading, a table cell and
        /// the empty paragraph marker are one line.</summary>
        public bool AllowsContinuation;
        public bool InTable;
        /// <summary>A heading line: a closing run of <c>#</c> and a trailing <c>{...}</c> are markup there.</summary>
        public bool IsHeading;
        /// <summary>Whether a line of the text can start a block (a heading, a list, a quote): true for everything but a heading and a table row.</summary>
        public bool BlockStart = true;
        /// <summary>Wraps the inline text into the whole block text the parser reads back, for the check.</summary>
        public Func<string, string> Wrap = text => text;

        public static BlockContext Plain() => new BlockContext { AllowsContinuation = true };
        public static BlockContext Heading(int level) => new BlockContext { FirstPrefix = new string('#', level) + " ", IsHeading = true, BlockStart = false };
        public static BlockContext Bullet() => new BlockContext { FirstPrefix = "- ", ContinuationPrefix = "  ", AllowsContinuation = true };
        public static BlockContext Number(int number)
        {
            string marker = number.ToString(CultureInfo.InvariantCulture) + ". ";
            return new BlockContext { FirstPrefix = marker, ContinuationPrefix = new string(' ', marker.Length), AllowsContinuation = true };
        }
        public static BlockContext Quote() => new BlockContext { FirstPrefix = "> ", ContinuationPrefix = "> ", AllowsContinuation = true };
        public static BlockContext Div(string header) => new BlockContext
        {
            AllowsContinuation = true,
            // A paragraph follows: a line that ends in a backslash would continue into the next line, so the check needs one.
            Wrap = text => "::: " + header + "\n" + text + "\n\nx\n:::",
        };
        public static BlockContext TableRow() => new BlockContext { InTable = true, BlockStart = false };
    }

    internal struct GenMode
    {
        /// <summary>The safe spelling: no stars, and every punctuation character that could mean anything escaped. Used only when the canonical spelling
        /// does not read back, which the rules are meant to prevent.</summary>
        public bool Safe;
        /// <summary>Every character of text is a numeric reference: the form that always reads back.</summary>
        public bool Numeric;
        /// <summary>Every object run is written as the generic span with its data in hex, which no object data can fail to fit.</summary>
        public bool GenericObjects;
    }

    /// <summary>Writes the inline text of one paragraph (or of the cells of one table row) from its runs (spec 3.2 to 3.4), by the rules of the spec and not by
    /// asking a parser: the text becomes a list of units, literal characters and pieces of syntax, and each literal character is escaped by the escape table
    /// of spec 3.4 from what is next to it.</summary>
    internal sealed class InlineGenerator
    {
        readonly MarkdownOptions _options;
        readonly BlockContext _context;
        readonly string _analysis, _vernacular;
        GenMode _mode;
        // The units this generator looked at (see WriterWork.Steps); published when a paragraph is rendered.
        long _steps;

        struct Unit
        {
            public string Text;        // syntax text; for a literal the character(s) as written when unescaped
            public bool Lit;           // a literal character the escape rules decide about
            public char Ch;            // for a literal: its first UTF-16 code unit
            public int Cp;             // for a literal: its code point
            public int Scope;          // the bracket scope of a literal
            public int EdgeScope;      // for syntax: the scope whose bracket this opens or closes; -1 for a cell or line boundary
            public bool Break;         // a hard line break (backslash, line end and the next line's prefix)
            public bool IsSpace;       // a literal that is a plain space
        }

        readonly List<Unit> _units = new List<Unit>();
        int _scopes;
        int _scope;

        public InlineGenerator(MarkdownOptions options, BlockContext context)
        {
            _options = options;
            _context = context;
            _analysis = options.WritingSystems.DefaultAnalysis;
            _vernacular = options.WritingSystems.DefaultVernacular;
        }

        MarkdownStyleNames Names => _options.StyleNames;

        /// <summary>The writing systems a run or empty paragraph names, in the order the text uses them, for the front matter.</summary>
        public static void CollectWritingSystems(IEnumerable<Run> runs, ICollection<string> into)
        {
            foreach (var run in runs) if (!into.Contains(run.Ws)) into.Add(run.Ws);
        }

        /// <summary>Writes the cells (one, outside a table).</summary>
        public string Generate(IReadOnlyList<List<Run>> cells, GenMode mode)
        {
            _mode = mode;
            _units.Clear();
            _scopes = 0;
            _scope = 0;
            bool table = _context.InTable;
            var all = new List<Run>();
            foreach (var cell in cells) all.AddRange(cell);
            var stars = mode.Safe || mode.Numeric ? null : ChooseStars(cells);
            int runIndex = 0;
            if (table) Syn("|");
            for (int c = 0; c < cells.Count; c++)
            {
                if (table) { Syn(" ", -1); }
                else if (c == 0) Syn(string.Empty, -1);
                var runs = cells[c];
                _cellText = string.Concat(runs.Select(r => r.Text));
                _cellOffset = 0;
                for (int i = 0; i < runs.Count; i++)
                {
                    WriteRun(runs[i], i == runs.Count - 1, stars == null ? 0 : stars[runIndex++]);
                    _cellOffset += runs[i].Text.Length;
                }
                if (table) Syn(" |", -1);
                else if (c == cells.Count - 1) Syn(string.Empty, -1);
            }
            return Render(all);
        }

        // ---------------------------------------------------------------- units

        void Syn(string text, int edge = 0)
        {
            _units.Add(new Unit { Text = text, EdgeScope = edge });
        }

        void Lit(int cp, string text)
        {
            _units.Add(new Unit { Text = text, Lit = true, Ch = text[0], Cp = cp, Scope = _scope });
        }

        // ---------------------------------------------------------------- stars

        /// <summary>0 for no star, 1 italic, 2 bold, 3 both, for every run of every cell. A run is written with stars only when the star rule of spec 3.2 holds:
        /// it has no other attribute, its first and last characters are letters or numbers, the run after it does not start with a combining mark, and no other
        /// star-written run touches it; if two would touch, every bold and italic run of the paragraph takes the attribute form.</summary>
        int[] ChooseStars(IReadOnlyList<List<Run>> cells)
        {
            var flat = new List<Run>();
            var cellOf = new List<int>();
            for (int c = 0; c < cells.Count; c++) foreach (var run in cells[c]) { flat.Add(run); cellOf.Add(c); }
            var result = new int[flat.Count];
            var candidate = new int[flat.Count];
            bool any = false;
            for (int i = 0; i < flat.Count; i++)
            {
                var run = flat[i];
                if (run.ObjData != null) continue;
                bool bold = IsOn(run, TextPropertyNumbers.Bold), italic = IsOn(run, TextPropertyNumbers.Italic);
                if (!bold && !italic) continue;
                any = true;
                if (!StarEligible(run.Text)) continue;
                if (i + 1 < flat.Count && cellOf[i + 1] == cellOf[i] && StartsWithCombiningMark(flat[i + 1].Text)) continue;
                var items = RunItems(run, bold, italic, null, false);
                if (items.Count > 0) continue;
                candidate[i] = (bold ? 2 : 0) | (italic ? 1 : 0);
            }
            if (!any) return result;
            for (int i = 0; i + 1 < flat.Count; i++)
                if (candidate[i] != 0 && candidate[i + 1] != 0 && cellOf[i] == cellOf[i + 1]) return result; // two would touch: attribute form for all
            return candidate;
        }

        static bool IsOn(Run run, int number)
            => run.Props.IntProps.TryGetValue(number, out var v) && v.Variation == PropertyKeys.VarEnum && v.Value == TextPropertyNumbers.ToggleOn;

        static bool StarEligible(string text)
        {
            if (text.Length == 0) return false;
            return LetterOrNumber(text, 0) && LetterOrNumber(text, text.Length - 1);
        }

        static bool LetterOrNumber(string text, int index)
        {
            if (index > 0 && char.IsLowSurrogate(text[index]) && char.IsHighSurrogate(text[index - 1])) index--;
            switch (CharUnicodeInfo.GetUnicodeCategory(text, index))
            {
                case UnicodeCategory.UppercaseLetter:
                case UnicodeCategory.LowercaseLetter:
                case UnicodeCategory.TitlecaseLetter:
                case UnicodeCategory.ModifierLetter:
                case UnicodeCategory.OtherLetter:
                case UnicodeCategory.DecimalDigitNumber:
                case UnicodeCategory.LetterNumber:
                case UnicodeCategory.OtherNumber:
                    return true;
                default:
                    return false;
            }
        }

        static bool StartsWithCombiningMark(string text)
        {
            if (text.Length == 0) return false;
            var category = CharUnicodeInfo.GetUnicodeCategory(text, 0);
            return category == UnicodeCategory.NonSpacingMark || category == UnicodeCategory.SpacingCombiningMark || category == UnicodeCategory.EnclosingMark;
        }

        // ---------------------------------------------------------------- runs

        void WriteRun(Run run, bool lastInCell, int stars)
        {
            if (run.ObjData == null) { WriteTextRun(run, stars); return; }
            var described = _mode.GenericObjects ? null : _options.Objects.Describe(run.ObjData);
            if (described == null || !Spellable(described)) { WriteGeneric(run); return; }
            switch (described.Kind)
            {
                case MarkdownObjectKind.Picture when run.Text == Chars.ObjectReplacement.ToString() && described.Guid != null:
                    WritePicture(run, described);
                    return;
                case MarkdownObjectKind.InternalLink when described.Guid != null:
                    WriteInternalLink(run, described);
                    return;
                case MarkdownObjectKind.ExternalLink when described.Url != null:
                    WriteExternalLink(run, described);
                    return;
                default:
                    WriteGeneric(run);
                    return;
            }
        }

        /// <summary>False when a string of the object holds a NUL or a lone surrogate: a quoted value cannot carry either, so the object is written in hex.</summary>
        static bool Spellable(MarkdownObject o)
        {
            foreach (string? s in new[] { o.Url, o.Tool, o.Project, o.Field, o.FieldWritingSystem, o.Path })
                if (s != null && (s.IndexOf((char)0) >= 0 || AttributeText.HasLoneSurrogate(s))) return false;
            return true;
        }

        void WriteTextRun(Run run, int stars)
        {
            var items = RunItems(run, stars != 0 && (stars & 2) != 0, stars != 0 && (stars & 1) != 0, null, generic: false);
            if (stars != 0)
            {
                string marks = stars == 3 ? "***" : stars == 2 ? "**" : "*";
                Syn(marks);
                Text(run.Text);
                Syn(marks);
                return;
            }
            if (items.Count == 0) { Text(run.Text, allowBreak: true); return; }
            OpenScope();
            Text(run.Text);
            CloseScope("]" + Block(items));
        }

        int _openScope;
        string _cellText = string.Empty;
        int _cellOffset;

        void OpenScope()
        {
            _scopes++;
            _scope = _scopes;
            _openScope = _scope;
            Syn("[", _scope);
        }

        void CloseScope(string closing)
        {
            Syn(closing, _scope);
            _scope = 0;
        }

        /// <summary>Object data the host does not describe: <c>[&amp;#xFFFC;]{s6x="..."}</c>. Every string with a control character takes the hex form, and
        /// object data starts with one.</summary>
        void WriteGeneric(Run run)
        {
            var items = RunItems(run, false, false, null, generic: true);
            OpenScope();
            Text(run.Text);
            CloseScope("]" + Block(items));
        }

        void WritePicture(Run run, MarkdownObject picture)
        {
            var items = RunItems(run, false, false, null, generic: false);
            Guid guid = picture.Guid!.Value;
            string dest = picture.Path ?? ProjectPath.ForObject(guid).ToString();
            bool alias = picture.Path == null;
            if (!alias) items.Insert(InsertionPoint(items), new PropertyKeys.Item("guid", guid.ToString("D")));
            Syn("![", 0);
            _scopes++;
            _scope = _scopes;
            Syn(string.Empty, _scope);
            Text(picture.Caption ?? string.Empty);
            Syn("](" + LinkSyntax.Wrap(dest, _context.InTable) + ")" + (items.Count > 0 ? Block(items) : string.Empty), _scope);
            _scope = 0;
        }

        /// <summary>Where <c>guid=</c> goes among the items: after the classes, <c>ws=</c> and <c>custom-style=</c>, before everything else.</summary>
        static int InsertionPoint(List<PropertyKeys.Item> items)
        {
            int i = 0;
            while (i < items.Count && (items[i].Key[0] == '.' || items[i].Key == "ws" || items[i].Key == "custom-style")) i++;
            return i;
        }

        void WriteInternalLink(Run run, MarkdownObject link)
        {
            Guid guid = link.Guid!.Value;
            var paths = _options.Paths;
            ProjectPath? path = null;
            // A link into another project has no path in this one, and this project's resolver must not be asked about its GUID.
            if (link.Project == null)
            {
                if (link.Path != null) path = ProjectPath.Parse(link.Path);
                if (path == null && paths != null) path = paths.PathOf(guid);
            }
            string? derivedTool = link.Project != null ? null : paths?.ToolOf(guid) ?? (path != null ? PathTools.ToolOf(path) : null);
            var form = _options.LinkForm;
            // A form that needs something the caller or the project has not given falls back: no file location, A; no project name, A; no path, E.
            if (form == MarkdownLinkForm.FileRelative && _options.LinkBase == null) form = MarkdownLinkForm.RootPath;
            if (form == MarkdownLinkForm.MotifUrl && _options.ProjectName == null && link.Project == null) form = MarkdownLinkForm.RootPath;
            if ((form == MarkdownLinkForm.RootPath || form == MarkdownLinkForm.FileRelative) && path == null) form = MarkdownLinkForm.ObjectAlias;
            if (path != null && (link.Field != null) && path.Field == null) path = new ProjectPath(path.Segments, link.Field, link.FieldWritingSystem);
            if (path != null) path = WithDotSuffixes(path, guid);
            string fieldTag = link.Field == null ? string.Empty : link.FieldWritingSystem == null ? link.Field : link.Field + "." + link.FieldWritingSystem;

            string dest;
            bool guidAttr = true, urlAttr = link.Url != null;
            switch (form)
            {
                case MarkdownLinkForm.SilfwUrl:
                    string tag = link.Field == null ? string.Empty : link.FieldWritingSystem == null ? link.Field : link.Field + "." + link.FieldWritingSystem;
                    dest = link.Url ?? SilfwUrl.Build(guid, link.Tool, tag, link.Project ?? _options.SilfwDatabase);
                    guidAttr = false;
                    urlAttr = false;
                    break;
                case MarkdownLinkForm.MotifUrl:
                    {
                        string full = (path ?? new ProjectPath(ProjectPath.ForObject(guid).Segments, link.Field, link.FieldWritingSystem)).ToString();
                        int hash = full.IndexOf('#');
                        // The query comes before the fragment, as in any URL.
                        dest = "motif://projects/" + EncodeProject(link.Project ?? _options.ProjectName!) + (hash < 0 ? full : full.Substring(0, hash)) + "?guid=" + guid.ToString("D") + (hash < 0 ? string.Empty : full.Substring(hash));
                    }
                    guidAttr = false;
                    break;
                case MarkdownLinkForm.FileRelative:
                    dest = LinkSyntax.RelativeTo(_options.LinkBase!, path!);
                    break;
                case MarkdownLinkForm.ObjectAlias:
                    dest = ProjectPath.ForObject(guid).ToString() + (link.Field == null ? string.Empty : "#" + link.Field + (link.FieldWritingSystem == null ? string.Empty : "." + link.FieldWritingSystem));
                    guidAttr = false;
                    break;
                default:
                    dest = path!.ToString();
                    break;
            }
            string? storedUrl = link.Url;
            // A stored URL without a tool, for an object the project can name a tool for, would come back with that tool: it travels in url=.
            if (storedUrl == null && link.Tool == null && derivedTool != null && form != MarkdownLinkForm.SilfwUrl) { storedUrl = SilfwUrl.Build(guid, null, fieldTag, link.Project); urlAttr = true; }
            var extra = new List<PropertyKeys.Item>();
            if (guidAttr) extra.Add(new PropertyKeys.Item("guid", guid.ToString("D")));
            if (!urlAttr && link.Tool != null && link.Tool != derivedTool && form != MarkdownLinkForm.SilfwUrl) extra.Add(new PropertyKeys.Item("tool", link.Tool));
            if (urlAttr) extra.Add(new PropertyKeys.Item("url", storedUrl));
            var flags = new List<PropertyKeys.Item>();
            if (link.Name != null && run.Text == link.Name) flags.Add(new PropertyKeys.Item(".name", null));
            if (link.Example) flags.Add(new PropertyKeys.Item(".example", null));
            if (link.Broken) flags.Add(new PropertyKeys.Item(".broken", null));
            var items = RunItems(run, false, false, extra, generic: false, isLink: true, linkFlags: flags);
            OpenScope();
            Text(run.Text);
            CloseScope("](" + LinkSyntax.Wrap(dest, _context.InTable) + ")" + (items.Count > 0 ? Block(items) : string.Empty));
        }

        /// <summary>A segment that is only dots would be a step in a relative path, and no encoding survives a URI parser (spec 4.3), so it gets the eight GUID digits.</summary>
        static ProjectPath WithDotSuffixes(ProjectPath path, Guid guid)
        {
            bool any = false;
            foreach (var segment in path.Segments) if (segment.Name.Length > 0 && segment.Name.Trim((char)46).Length == 0 && segment.Homograph == null && segment.GuidSuffix == null) any = true;
            if (!any) return path;
            var segments = new List<ProjectPathSegment>();
            foreach (var segment in path.Segments)
                segments.Add(segment.Name.Length > 0 && segment.Name.Trim((char)46).Length == 0 && segment.Homograph == null && segment.GuidSuffix == null
                    ? new ProjectPathSegment(segment.Name, null, guid.ToString("N").Substring(0, 8)) : segment);
            return new ProjectPath(segments, path.Field, path.FieldWritingSystem);
        }

        static string EncodeProject(string name)
        {
            var sb = new StringBuilder();
            foreach (byte b in Encoding.UTF8.GetBytes(name))
            {
                char c = (char)b;
                if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '-' || c == '_' || c == '.' || c == '~') sb.Append(c);
                else sb.Append('%').Append(((int)b).ToString("X2", CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }

        void WriteExternalLink(Run run, MarkdownObject link)
        {
            string url = link.Url!;
            bool plain = DestinationCarries(url) && LinkSyntax.ReadsAsExternal(url, _options);
            var extra = new List<PropertyKeys.Item>();
            if (!plain) extra.Add(new PropertyKeys.Item("url", url));
            var items = RunItems(run, false, false, extra, generic: false, isLink: true);
            OpenScope();
            Text(run.Text);
            CloseScope("](" + (plain ? LinkSyntax.Wrap(url, _context.InTable) : "<>") + ")" + (items.Count > 0 ? Block(items) : string.Empty));
        }

        /// <summary>True when a URL can stand as a link destination and be read back as it is: no control character, angle bracket, backslash or lone
        /// surrogate, parentheses balanced, no ampersand that reads as a character reference, and not empty.</summary>
        static bool DestinationCarries(string url)
        {
            if (url.Length == 0 || url[0] == ' ' || url[url.Length - 1] == ' ') return false;
            int depth = 0;
            for (int i = 0; i < url.Length; i++)
            {
                char c = url[i];
                if (c < ' ' || (c >= '\x7F' && c <= '\x9F') || c == '<' || c == '>' || c == '\\') return false;
                if (char.IsWhiteSpace(c) && c != (char)32) return false; // Pandoc trims or changes these in a destination
                if (c == '(') depth++;
                else if (c == ')') { depth--; if (depth < 0) return false; }
                else if (c == '&' && AttributeText.ReadsAsReference(url, i)) return false;
                else if (char.IsHighSurrogate(c) && i + 1 < url.Length && char.IsLowSurrogate(url[i + 1])) i++;
                else if (char.IsSurrogate(c)) return false;
                else if (c == Chars.LineSeparator || c == Chars.ParagraphSeparator || c == Chars.ByteOrderMark) return false;
            }
            return depth == 0;
        }

        // ---------------------------------------------------------------- attributes

        /// <summary>The attribute items of a run in canonical order (spec 3.3): classes, <c>ws=</c>, <c>custom-style=</c>, the link's own items (<c>guid</c>, <c>tool</c>,
        /// <c>url</c>), then the properties. <paramref name="strong"/> and <paramref name="emphasis"/> are the toggles written as stars instead.</summary>
        List<PropertyKeys.Item> RunItems(Run run, bool strong, bool emphasis, List<PropertyKeys.Item>? linkItems, bool generic, bool isLink = false, List<PropertyKeys.Item>? linkFlags = null)
        {
            var items = new List<PropertyKeys.Item>();
            bool vernacular = run.Ws != _analysis && run.Ws == _vernacular;
            var bag = run.Props.Clone();
            bool hasStyle = bag.StrProps.TryGetValue(TextPropertyNumbers.NamedStyle, out string? style);
            // Classes, in the order .v .name .example .broken .nul .surrogate .nostyle.
            if (vernacular) items.Add(new PropertyKeys.Item(".v", null));
            if (linkFlags != null) items.AddRange(linkFlags);
            bool noStyle = isLink && !hasStyle;
            if (noStyle) items.Add(new PropertyKeys.Item(".nostyle", null));
            if (run.Ws != _analysis && !vernacular) items.Add(new PropertyKeys.Item("ws", run.Ws));
            bool styleWritten = false;
            if (hasStyle && !(isLink && style == Names.Hyperlink)) { items.Add(new PropertyKeys.Item("custom-style", style)); styleWritten = true; }
            if (linkItems != null) items.AddRange(linkItems);
            if (strong) bag.IntProps.Remove(TextPropertyNumbers.Bold);
            if (emphasis) bag.IntProps.Remove(TextPropertyNumbers.Italic);
            bag.StrProps.Remove(TextPropertyNumbers.NamedStyle);
            if (generic) bag.StrProps[6] = run.ObjData!;
            items.AddRange(PropertyKeys.ToItems(bag));
            _ = styleWritten;
            return items;
        }

        string Block(List<PropertyKeys.Item> items)
        {
            var sb = new StringBuilder("{");
            for (int i = 0; i < items.Count; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(items[i].Key);
                if (items[i].Value != null) sb.Append('=').Append(AttributeText.Value(items[i].Value!, _context.InTable));
            }
            return sb.Append('}').ToString();
        }

        // ---------------------------------------------------------------- text

        static string Ref(int codePoint) => "&#x" + codePoint.ToString("X", CultureInfo.InvariantCulture) + ";";

        /// <summary>Turns the text of a run into units: literal characters, and syntax for what a literal cannot carry (spec 3.4).</summary>
        void Text(string text, bool allowBreak = false)
        {
            int n = text.Length;
            for (int k = 0; k < n; k++)
            {
                char c = text[k];
                if (_mode.Numeric)
                {
                    if (char.IsHighSurrogate(c) && k + 1 < n && char.IsLowSurrogate(text[k + 1])) { Syn(Ref(char.ConvertToUtf32(c, text[k + 1]))); k++; }
                    else if (c == 0) Syn("[]{.nul}");
                    else if (char.IsSurrogate(c)) Syn("[]{.surrogate x=" + ((int)c).ToString("X4", CultureInfo.InvariantCulture) + "}");
                    else Syn(Ref(c));
                    continue;
                }
                if (c == 0) { Syn("[]{.nul}"); continue; }
                if (c == Chars.LineSeparator)
                {
                    // A backslash line end only between two characters of the paragraph, in plain text: at the start or end of the paragraph, next to another separator, or inside a bracket it is a reference.
                    int at = _cellOffset + k;
                    bool backslash = allowBreak && _context.AllowsContinuation && !_context.InTable && at > 0 && at < _cellText.Length - 1
                        && _cellText[at - 1] != Chars.LineSeparator && _cellText[at + 1] != Chars.LineSeparator;
                    if (backslash) _units.Add(new Unit { Text = "\\\n" + _context.ContinuationPrefix, Break = true, EdgeScope = -1 });
                    else Syn(Ref(c));
                    continue;
                }
                if (c == ' ') { _units.Add(new Unit { Text = " ", Lit = true, Ch = ' ', Cp = ' ', IsSpace = true, Scope = _scope }); continue; }
                if (char.IsHighSurrogate(c) && k + 1 < n && char.IsLowSurrogate(text[k + 1])) { Lit(char.ConvertToUtf32(c, text[k + 1]), text.Substring(k, 2)); k++; continue; }
                if (char.IsSurrogate(c)) { Syn("[]{.surrogate x=" + ((int)c).ToString("X4", CultureInfo.InvariantCulture) + "}"); continue; }
                if (c == Chars.NoBreakSpace) { Syn("&nbsp;"); continue; }
                if (c < ' ' || (c >= '\x7F' && c <= '\x9F') || char.IsWhiteSpace(c) || c == Chars.ObjectReplacement || c == Chars.ByteOrderMark
                    || Chars.IsInvisible(c) && !(c == Chars.ZeroWidthJoiner && Chars.JoinsEmoji(text, k)))
                {
                    Syn(Ref(c));
                    continue;
                }
                Lit(c, c.ToString());
            }
        }

        // ---------------------------------------------------------------- escaping

        enum CharClass { Space, Punct, Other }

        static CharClass Classify(int cp)
        {
            if (cp == ' ' || cp == '\t' || cp == '\n' || cp == '\r' || cp == '\f') return CharClass.Space;
            if (cp < 0x80)
            {
                if ((cp >= '0' && cp <= '9') || (cp >= 'A' && cp <= 'Z') || (cp >= 'a' && cp <= 'z')) return CharClass.Other;
                return cp > ' ' && cp != 0x7F ? CharClass.Punct : CharClass.Space;
            }
            string s = char.ConvertFromUtf32(cp > 0x10FFFF || (cp >= 0xD800 && cp <= 0xDFFF) ? 0xFFFD : cp);
            switch (CharUnicodeInfo.GetUnicodeCategory(s, 0))
            {
                case UnicodeCategory.SpaceSeparator: return CharClass.Space;
                case UnicodeCategory.ConnectorPunctuation: case UnicodeCategory.DashPunctuation: case UnicodeCategory.OpenPunctuation: case UnicodeCategory.ClosePunctuation:
                case UnicodeCategory.InitialQuotePunctuation: case UnicodeCategory.FinalQuotePunctuation: case UnicodeCategory.OtherPunctuation:
                case UnicodeCategory.MathSymbol: case UnicodeCategory.CurrencySymbol: case UnicodeCategory.ModifierSymbol: case UnicodeCategory.OtherSymbol:
                    return CharClass.Punct;
                default: return CharClass.Other;
            }
        }

        /// <summary>The class of the character before unit i as the parser sees it (the end of the previous unit's text); whitespace at the start of a line.</summary>
        CharClass Before(int i)
        {
            if (i == 0) return CharClass.Space;
            var u = _units[i - 1];
            if (u.Break || u.EdgeScope == -1 && u.Text.Length == 0) return CharClass.Space;
            if (u.Lit) return Classify(u.Cp);
            if (u.Text.Length == 0) return i - 1 == 0 ? CharClass.Space : Before(i - 1);
            return ClassifyLast(u.Text);
        }

        CharClass After(int i)
        {
            if (i + 1 >= _units.Count) return CharClass.Space;
            for (int j = i + 1; j < _units.Count; j++)
            {
                _steps++;
                var u = _units[j];
                if (u.Break) return CharClass.Punct; // a backslash
                if (u.Lit) return Classify(u.Cp);
                if (u.Text.Length == 0) { if (u.EdgeScope == -1) return CharClass.Space; continue; }
                return Classify(char.IsHighSurrogate(u.Text[0]) && u.Text.Length > 1 ? char.ConvertToUtf32(u.Text[0], u.Text[1]) : u.Text[0]);
            }
            return CharClass.Space;
        }

        static CharClass ClassifyLast(string text)
        {
            char c = text[text.Length - 1];
            if (char.IsLowSurrogate(c) && text.Length > 1 && char.IsHighSurrogate(text[text.Length - 2])) return Classify(char.ConvertToUtf32(text[text.Length - 2], c));
            return Classify(c);
        }

        /// <summary>The next code point after unit i that the parser would see, or -1 at the end of the line.</summary>
        int NextChar(int i)
        {
            for (int j = i + 1; j < _units.Count; j++)
            {
                _steps++;
                var u = _units[j];
                if (u.Break) return '\\';
                if (u.Lit) return u.Cp;
                if (u.Text.Length == 0) { if (u.EdgeScope == -1) return -1; continue; }
                return u.Text[0] == ' ' && u.EdgeScope == -1 ? -1 : u.Text[0];
            }
            return -1;
        }

        int PrevChar(int i)
        {
            for (int j = i - 1; j >= 0; j--)
            {
                _steps++;
                var u = _units[j];
                if (u.Break) return -1;
                if (u.Lit) return u.Cp;
                if (u.Text.Length == 0) { if (u.EdgeScope == -1) return -1; continue; }
                char last = u.Text[u.Text.Length - 1];
                if (u.EdgeScope == -1 && last == ' ') return -1;
                return last;
            }
            return -1;
        }

        string Render(List<Run> all)
        {
            int n = _units.Count;
            _steps += n;
            var escape = new bool[n];
            var hexSpace = new bool[n];
            ResolveSpaces(hexSpace);
            ResolveEscapes(escape);
            var sb = new StringBuilder();
            var offsets = new int[n + 1];
            for (int i = 0; i < n; i++)
            {
                offsets[i] = sb.Length;
                var u = _units[i];
                if (!u.Lit) { sb.Append(u.Text); continue; }
                if (u.IsSpace) { sb.Append(hexSpace[i] ? "&#x20;" : " "); continue; }
                if (escape[i]) { if (u.Ch == '&' && !_mode.Safe) { sb.Append("\\&"); continue; } sb.Append('\\'); }
                sb.Append(u.Text);
            }
            offsets[n] = sb.Length;
            string line = sb.ToString();
            if (_context.BlockStart) line = EscapeLineStarts(line, offsets);
            WriterWork.Steps += _steps;
            _steps = 0;
            return line;
        }

        /// <summary>A space is written as itself only between two characters that are not spaces and not the edge of its own bracket or of a line or cell: leading and
        /// trailing spaces and runs of spaces are <c>&amp;#x20;</c> (spec 3.4), since Pandoc and CommonMark would trim or collapse them.</summary>
        void ResolveSpaces(bool[] hex)
        {
            int n = _units.Count;
            for (int i = 0; i < n; i++)
            {
                var u = _units[i];
                if (!u.IsSpace) continue;
                bool boundary = false;
                // The previous unit that is not an empty piece of syntax.
                int p = i - 1;
                while (p >= 0 && !_units[p].Lit && _units[p].Text.Length == 0 && _units[p].EdgeScope != -1) { _steps++; p--; }
                if (p < 0) boundary = true;
                else
                {
                    var q = _units[p];
                    if (q.IsSpace || q.Break || (!q.Lit && q.EdgeScope == -1) || (!q.Lit && q.EdgeScope == u.Scope && u.Scope != 0)) boundary = true;
                    else if (!q.Lit && q.Text.Length > 0 && q.Text[q.Text.Length - 1] == ';' && q.Text.StartsWith("&#x", StringComparison.Ordinal) && (q.Text == "&#x20;")) boundary = true;
                }
                if (!boundary)
                {
                    int s = i + 1;
                    while (s < n && !_units[s].Lit && _units[s].Text.Length == 0 && _units[s].EdgeScope != -1) { _steps++; s++; }
                    if (s >= n) boundary = true;
                    else
                    {
                        var q = _units[s];
                        if (q.IsSpace || q.Break || (!q.Lit && q.EdgeScope == -1) || (!q.Lit && q.EdgeScope == u.Scope && u.Scope != 0)) boundary = true;
                        else if (!q.Lit && q.Text == "&#x20;") boundary = true;
                    }
                }
                hex[i] = boundary;
            }
        }

        const int MaxBareBracketDepth = 8;

        const string SafeSet = "\\!#&()*+-.:=>[]_{}~|^$@<`";

        void ResolveEscapes(bool[] escape)
        {
            int n = _units.Count;
            // Counts for the characters that pair up across a paragraph.
            int dollars = 0, carets = 0, tildes = 0;
            // Pandoc pairs these across the whole line: a literal $ opens math that a $ in a destination or an attribute value closes, so every occurrence counts, written or not.
            for (int i = 0; i < n; i++)
            {
                string text = _units[i].Lit ? null! : _units[i].Text;
                if (_units[i].Lit)
                {
                    switch (_units[i].Ch) { case '$': dollars++; break; case '^': carets++; break; case '~': tildes++; break; }
                }
                else foreach (char ch in text) { if (ch == '$') dollars++; else if (ch == '^') carets++; else if (ch == '~') tildes++; }
            }
            // Brackets: a literal pair that is balanced inside its scope stays bare unless a (, [ or { follows its ]; every other bracket is escaped.
            var stacks = new Dictionary<int, Stack<int>>();
            var pairedWith = new Dictionary<int, int>();
            var deep = new HashSet<int>();
            for (int i = 0; i < n; i++)
            {
                var u = _units[i];
                if (!u.Lit) continue;
                if (u.Ch == '[')
                {
                    if (!stacks.TryGetValue(u.Scope, out var st)) { st = new Stack<int>(); stacks[u.Scope] = st; }
                    st.Push(i);
                }
                else if (u.Ch == ']')
                {
                    if (stacks.TryGetValue(u.Scope, out var st) && st.Count > 0)
                    {
                        int open = st.Pop();
                        pairedWith[open] = i;
                        pairedWith[i] = open;
                        // Pandoc 3.9 does not finish on dozens of nested balanced brackets, so the inner pairs beyond a small depth are escaped.
                        if (st.Count >= MaxBareBracketDepth) { deep.Add(open); deep.Add(i); }
                    }
                }
            }
            for (int i = 0; i < n; i++)
            {
                var u = _units[i];
                if (!u.Lit || u.IsSpace) continue;
                char c = u.Ch;
                if (u.Cp > 0xFFFF) continue;
                if (_mode.Safe)
                {
                    if (SafeSet.IndexOf(c) >= 0 && !(c == '|' && !_context.InTable)) escape[i] = true;
                    else if (c == '|' && _context.InTable) escape[i] = true;
                    continue;
                }
                switch (c)
                {
                    case '\\': case '`': case '*':
                        escape[i] = true;
                        break;
                    case '|':
                        escape[i] = _context.InTable;
                        break;
                    case '_':
                    {
                        // A run of underscores is one delimiter run: it is judged once, by what is around it.
                        int first = i;
                        int last = i;
                        while (last + 1 < n && _units[last + 1].Lit && _units[last + 1].Ch == '_') last++;
                        var before = Before(first);
                        var after = After(last);
                        bool left = after != CharClass.Space && (after != CharClass.Punct || before == CharClass.Space || before == CharClass.Punct);
                        bool right = before != CharClass.Space && (before != CharClass.Punct || after == CharClass.Space || after == CharClass.Punct);
                        bool canOpen = left && (!right || before == CharClass.Punct);
                        bool canClose = right && (!left || after == CharClass.Punct);
                        if (canOpen || canClose) for (int k = first; k <= last; k++) escape[k] = true;
                        i = last;
                        break;
                    }
                    case '<':
                    {
                        int next = NextChar(i);
                        escape[i] = next >= 0 && (next == '/' || next == '!' || next == '?' || (next < 0x80 && char.IsLetter((char)next)));
                        break;
                    }
                    case '&':
                    {
                        var sb = new StringBuilder();
                        for (int j = i + 1; j < n && j < i + 36 && _units[j].Lit && !_units[j].IsSpace; j++) { _steps++; sb.Append(_units[j].Text); } // an entity name is at most 32 characters
                        string after = sb.ToString();
                        escape[i] = after.Length > 0 && (after[0] == '#' || AttributeText.ReadsAsReference("&" + after, 0));
                        break;
                    }
                    case '$': escape[i] = dollars >= 2; break;
                    case '^': escape[i] = carets >= 2 || NextChar(i) == '[' || PrevChar(i) == '['; break; // [^ starts a footnote reference
                    case '~': escape[i] = tildes >= 2; break;
                    case '@':
                    {
                        int prev = PrevChar(i);
                        escape[i] = !(prev >= 0 && (prev < 0x80 ? char.IsLetterOrDigit((char)prev) : IsLetterOrNumber(prev)));
                        break;
                    }
                    case '[':
                    case ']':
                    {
                        if (!pairedWith.TryGetValue(i, out int mate) || deep.Contains(i)) { escape[i] = true; break; }
                        int close = c == '[' ? mate : i;
                        int next = NextChar(close);
                        escape[i] = next == '(' || next == '[' || next == '{';
                        break;
                    }
                    case '!':
                        escape[i] = NextChar(i) == '[';
                        break;
                    case '{':
                    {
                        // The closing parenthesis of a link or picture (syntax, not a literal) right before: its attribute block would take this brace.
                        int p = i - 1;
                        while (p >= 0 && !_units[p].Lit && _units[p].Text.Length == 0 && _units[p].EdgeScope != -1) p--;
                        bool afterLink = p >= 0 && !_units[p].Lit && _units[p].Text.EndsWith(")", StringComparison.Ordinal);
                        bool headingAttr = false;
                        if (_context.IsHeading)
                        {
                            var raw = new StringBuilder();
                            for (int j = i; j < n; j++) raw.Append(_units[j].Lit ? _units[j].Text : _units[j].Text);
                            headingAttr = raw.Length > 0 && raw[raw.Length - 1] == '}';
                        }
                        escape[i] = afterLink || headingAttr;
                        break;
                    }
                    case '#':
                        if (_context.IsHeading)
                        {
                            // A closing sequence of # ends the heading: the trailing ones are text, so they are escaped.
                            int j = i;
                            while (j < n && _units[j].Lit && _units[j].Ch == '#') j++;
                            escape[i] = j == n || (!_units[j].Lit && _units[j].Text.Length == 0);
                        }
                        break;
                }
            }
        }

        static bool IsLetterOrNumber(int cp)
        {
            string s = char.ConvertFromUtf32(cp > 0x10FFFF || (cp >= 0xD800 && cp <= 0xDFFF) ? 0xFFFD : cp);
            return LetterOrNumber(s, 0);
        }

        /// <summary>A line that starts with a literal character Markdown or Pandoc would read as block markup gets a backslash before it (spec 3.4).</summary>
        string EscapeLineStarts(string text, int[] offsets)
        {
            // Line starts: the first unit, and the unit after each hard break.
            var starts = new List<int>();
            starts.Add(0);
            for (int i = 0; i < _units.Count; i++) if (_units[i].Break) starts.Add(i + 1);
            for (int s = starts.Count - 1; s >= 0; s--)
            {
                int unit = starts[s];
                // Skip empty syntax at the line start (a cell or line boundary).
                while (unit < _units.Count && !_units[unit].Lit && _units[unit].Text.Length == 0) unit++;
                if (unit >= _units.Count) continue;
                int from = offsets[unit];
                int to = text.Length;
                for (int j = unit; j < _units.Count; j++) if (_units[j].Break) { to = offsets[j]; break; }
                int at = LineStart.EscapeAt(text.Substring(from, to - from));
                if (at < 0) continue;
                // Only literal characters can be escaped; find the unit at the insertion point.
                int target = unit;
                while (target < _units.Count && offsets[target] - from < at) target++;
                if (target >= _units.Count || !_units[target].Lit || offsets[target] - from != at) continue;
                text = text.Substring(0, from + at) + "\\" + text.Substring(from + at);
            }
            return text;
        }
    }
}
