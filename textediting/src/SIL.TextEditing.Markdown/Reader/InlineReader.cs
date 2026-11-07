using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Markdig.Syntax.Inlines;
using SIL.TextEditing.Markdown.Notation;
using MdTable = Markdig.Extensions.Tables;

namespace SIL.TextEditing.Markdown.Reader
{
    internal enum CsState { Unset, None, Explicit }

    /// <summary>The formatting in force while reading inlines. Inner spans and marks override outer ones.</summary>
    internal sealed class Fmt
    {
        public string? Ws;
        public bool Vernacular;
        public CsState Cs;
        public string? CsName;
        public PropertyBag Props = new PropertyBag();
        public string? ObjData;
        public bool IsLink;
        /// <summary>Which object span the text belongs to (0 for none): the pieces of one link are one run.</summary>
        public int Scope;

        public Fmt Clone() => new Fmt { Ws = Ws, Vernacular = Vernacular, Cs = Cs, CsName = CsName, Props = Props.Clone(), ObjData = ObjData, IsLink = IsLink, Scope = Scope };
    }

    /// <summary>Turns the inline tree Markdig built into runs (spec 3.2).</summary>
    internal sealed class InlineReader
    {
        readonly ReaderContext _context;
        readonly MarkdownOptions _options;
        readonly List<Run> _runs = new List<Run>();

        public InlineReader(ReaderContext context)
        {
            _context = context;
            _options = context.Options;
        }

        public List<Run> Runs => _runs;

        /// <summary>Reads one paragraph's inlines. Hard breaks written <c>\</c> become U+2028, soft breaks one space.</summary>
        public static List<Run> Read(ContainerInline? inlines, ReaderContext context)
        {
            var reader = new InlineReader(context);
            if (inlines != null) reader.Walk(inlines, new Fmt());
            return reader._runs;
        }

        int Abs(int offset) => _context.Base + offset;

        void Add(string text, Fmt fmt)
        {
            if (text.Length == 0) return;
            var run = new Run { Text = text, Ws = ResolveWs(fmt), Props = fmt.Props.Clone(), ObjData = fmt.ObjData };
            string? cs = fmt.Cs == CsState.Explicit ? fmt.CsName : fmt.Cs == CsState.None ? null : (fmt.IsLink ? _options.StyleNames.Hyperlink : null);
            if (cs != null) run.Props.StrProps[TextPropertyNumbers.NamedStyle] = cs;
            // Markdig hands over one link's text in pieces (an escaped bracket is a piece of its own). Runs with object data never
            // merge afterwards, so the pieces of one object span are joined here.
            if (fmt.Scope != 0 && fmt.Scope == _lastScope && _runs.Count > 0)
            {
                var last = _runs[_runs.Count - 1];
                if (last.Ws == run.Ws && last.ObjData == run.ObjData && last.Props.ContentEquals(run.Props))
                {
                    last.Text += text;
                    return;
                }
            }
            _lastScope = fmt.Scope;
            _runs.Add(run);
        }

        int _scopes;
        int _lastScope;

        string ResolveWs(Fmt fmt)
        {
            if (fmt.Ws != null) return fmt.Ws;
            return fmt.Vernacular ? _context.DefaultVernacular : _context.DefaultAnalysis;
        }

        /// <summary>Reads the inlines under <paramref name="root"/> in document order. The walk keeps its own stack instead of calling itself,
        /// so how deeply the text nests (three thousand open brackets, say) cannot overflow the thread's stack.</summary>
        void Walk(ContainerInline root, Fmt rootFmt)
        {
            var pending = new Stack<KeyValuePair<Inline?, Fmt>>();
            pending.Push(new KeyValuePair<Inline?, Fmt>(root.FirstChild, rootFmt));
            while (pending.Count > 0)
            {
                var entry = pending.Pop();
                Inline? child = entry.Key;
                if (child == null) continue;
                Fmt fmt = entry.Value;
                pending.Push(new KeyValuePair<Inline?, Fmt>(child.NextSibling, fmt)); // what follows this child, after everything inside it
                switch (child)
                {
                    case LiteralInline literal:
                        // Pandoc turns a raw tab in text into a space; a tab that is meant is a character reference.
                        Add(literal.Content.ToString().Replace('\t', ' '), fmt);
                        break;
                    case HtmlEntityInline entity:
                        Add(entity.Transcoded.ToString(), fmt);
                        break;
                    case SupSubInline supSub:
                    {
                        var inner = fmt.Clone();
                        inner.Props.IntProps[TextPropertyNumbers.Superscript] = new IntPropValue(PropertyKeys.VarEnum, supSub.IsSuper ? 1 : 2);
                        Add(supSub.Text, inner);
                        break;
                    }
                    case EmphasisInline emphasis:
                    {
                        var inner = fmt.Clone();
                        int number = emphasis.DelimiterCount >= 2 ? TextPropertyNumbers.Bold : TextPropertyNumbers.Italic;
                        inner.Props.IntProps[number] = new IntPropValue(PropertyKeys.VarEnum, TextPropertyNumbers.ToggleOn);
                        pending.Push(new KeyValuePair<Inline?, Fmt>(emphasis.FirstChild, inner));
                        break;
                    }
                    case LineBreakInline lineBreak:
                        Add(lineBreak.IsHard ? Chars.LineSeparatorText : " ", fmt);
                        break;
                    case SpanInline span:
                    {
                        if (TryReadCharacterSpan(span, fmt)) break;
                        pending.Push(new KeyValuePair<Inline?, Fmt>(span.FirstChild, ReadSpan(span, fmt)));
                        break;
                    }
                    case AttrsInline _:
                        break; // consumed by the link before it
                    case LinkInline link:
                    {
                        var inner = ReadLink(link, fmt);
                        if (inner != null) pending.Push(new KeyValuePair<Inline?, Fmt>(link.FirstChild, inner));
                        break;
                    }
                    case Markdig.Extensions.Tables.PipeTableDelimiterInline pipe:
                        Add("|", fmt);
                        pending.Push(new KeyValuePair<Inline?, Fmt>(pipe.FirstChild, fmt));
                        break;
                    case CodeInline code:
                        Unsupported(code.Span.Start, code.Span.End, "A code span is not supported. Escape the backtick as \\`.");
                        Add(Source(code.Span.Start, code.Span.End), fmt);
                        break;
                    case AutolinkInline autolink:
                        Unsupported(autolink.Span.Start, autolink.Span.End, "An autolink is not supported. Escape the < as \\<.");
                        Add(Source(autolink.Span.Start, autolink.Span.End), fmt);
                        break;
                    case HtmlInline html:
                        Unsupported(html.Span.Start, html.Span.End, "Raw HTML is not supported. Escape the < as \\<.");
                        Add(Source(html.Span.Start, html.Span.End), fmt);
                        break;
                    case DelimiterInline delimiter:
                        Add(delimiter.ToLiteral(), fmt);
                        pending.Push(new KeyValuePair<Inline?, Fmt>(delimiter.FirstChild, fmt));
                        break;
                    case ContainerInline other:
                        pending.Push(new KeyValuePair<Inline?, Fmt>(other.FirstChild, fmt));
                        break;
                    default:
                        break;
                }
            }
        }

        string Source(int start, int end)
        {
            int a = Abs(start), b = Abs(end) + 1;
            if (a < 0 || b > _context.Source.Length || b < a) return string.Empty;
            return _context.Source.Substring(a, b - a);
        }

        /// <summary>Reports unsupported inline markup. The fix is the same text with each angle bracket and backtick escaped (or the
        /// first bracket of a reference link).</summary>
        void Unsupported(int start, int end, string message)
        {
            string observed = Source(start, end);
            string? fix = null;
            if (observed.IndexOf('<') >= 0 || observed.IndexOf('`') >= 0) fix = observed.Replace("<", "\\<").Replace("`", "\\`");
            else if (observed.StartsWith("[", StringComparison.Ordinal)) fix = "\\" + observed;
            _context.Report(MarkdownDiagnosticIds.UnsupportedMarkup, MarkdownSeverity.Error, message, Abs(start), Abs(end) + 1, fix);
        }

        // -------------------------------------------------------------- spans and links

        /// <summary><c>[]{.nul}</c> is a NUL and <c>[]{.surrogate x=D800}</c> an unpaired surrogate (spec 3.4): characters CommonMark cannot carry.
        /// They take the formatting of the run they stand in.</summary>
        bool TryReadCharacterSpan(SpanInline span, Fmt fmt)
        {
            AttrItem? special = null;
            foreach (var item in span.Attrs.Items) if (item.IsClass && (item.Key == "nul" || item.Key == "surrogate")) { special = item; break; }
            if (special == null) return false;
            int start = Abs(span.OpenAt), end = Abs(span.Attrs.End);
            if (span.FirstChild != null || span.Attrs.Items.Count != (special.Key == "nul" ? 1 : 2))
            {
                Malformed(special, "[]{." + special.Key + "}" + (special.Key == "nul" ? string.Empty : " with x=") + " stands alone: it has no text and no other attribute");
                return false;
            }
            if (special.Key == "nul") { Add("\0", fmt); return true; }
            AttrItem? x = null;
            foreach (var item in span.Attrs.Items) if (!item.IsClass) x = item;
            if (x == null || x.Key != "x" || x.IsFlag || x.Value!.Length == 0 || x.Value.Length > 4 || !int.TryParse(x.Value, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out int unit) || unit < 0xD800 || unit > 0xDFFF)
            {
                _context.Report(MarkdownDiagnosticIds.MalformedAttribute, MarkdownSeverity.Error, ".surrogate takes x= and the hex code unit, D800 to DFFF: []{.surrogate x=D800}", start, end);
                return true;
            }
            Add(((char)unit).ToString(), fmt);
            return true;
        }

        /// <summary>The formatting of the text inside a span.</summary>
        Fmt ReadSpan(SpanInline span, Fmt fmt)
            => ApplyAttrs(span.Attrs, fmt, new ObjectForm { Start = span.OpenAt, End = span.Attrs.End - 1 }, Ctx.Span);

        /// <summary>Reads a link or picture; returns the formatting of the text inside it, or null when there is none to read.</summary>
        Fmt? ReadLink(LinkInline link, Fmt fmt)
        {
            AttrBlock? attrs = (link.NextSibling as AttrsInline)?.Attrs;
            if (link.IsAutoLink)
            {
                Unsupported(link.Span.Start, link.Span.End, "An autolink is not supported. Escape the < as \\<.");
                Add(Source(link.Span.Start, link.Span.End), fmt);
                return null;
            }
            if (link.IsImage)
            {
                var form = new ObjectForm { IsPicture = true, Dest = link.Url ?? string.Empty, Caption = PlainText(link), Start = link.Span.Start, End = attrs == null ? link.Span.End : attrs.End - 1 };
                var picture = attrs == null ? Finish(fmt.Clone(), form) : ApplyAttrs(attrs, fmt, form, Ctx.Image);
                Add(Run.ObjectReplacement.ToString(), picture);
                return null;
            }
            if (link.IsShortcut)
            {
                Unsupported(link.Span.Start, link.Span.End, "A reference link is not supported. Escape the [ as \\[.");
                return fmt;
            }
            var linkForm = new ObjectForm { Dest = link.Url ?? string.Empty, Start = link.Span.Start, End = attrs == null ? link.Span.End : attrs.End - 1 };
            return attrs == null ? Finish(fmt.Clone(), linkForm) : ApplyAttrs(attrs, fmt, linkForm, Ctx.Link);
        }

        static string PlainText(ContainerInline container)
        {
            var sb = new StringBuilder();
            for (Inline? child = container.FirstChild; child != null; child = child.NextSibling)
            {
                if (child is LiteralInline literal) sb.Append(literal.Content.ToString());
                else if (child is HtmlEntityInline entity) sb.Append(entity.Transcoded.ToString());
                else if (child is ContainerInline inner) sb.Append(PlainText(inner));
            }
            return sb.ToString();
        }

        enum Ctx { Span, Link, Image }

        /// <summary>What the syntax around an attribute block says about the object the run carries.</summary>
        sealed class ObjectForm
        {
            public bool IsPicture;
            public string? Dest;
            public string? Caption;
            public Guid? Guid;
            public string? Tool;
            public string? Url;
            public bool UrlGiven;
            public bool Name, Example, Broken;
            public string? S6;
            public bool HasObject => IsPicture || Dest != null || S6 != null;
            public int Start, End;
        }

        static readonly string[] PairKeys = { "ws", "custom-style", "guid", "tool", "url", "x" };

        Fmt ApplyAttrs(AttrBlock attrs, Fmt parent, ObjectForm form, Ctx ctx)
        {
            var fmt = parent.Clone();
            var rest = new List<AttrItem>();
            AttrItem? wsItem = null, vItem = null, csItem = null, urlItem = null, s6Item = null, s133Item = null, nameItem = null, exampleItem = null, brokenItem = null, noStyleItem = null;
            foreach (var item in attrs.Items)
            {
                bool handled = true;
                if (item.IsClass)
                {
                    switch (item.Key)
                    {
                        case "v": if (vItem != null) Duplicate(item); vItem = vItem ?? item; break;
                        case "name": case "example": case "broken": case "nostyle":
                            if (ctx == Ctx.Span)
                            {
                                _context.Report(MarkdownDiagnosticIds.UnknownAttribute, MarkdownSeverity.Error, "'." + item.Key + "' belongs to a link: [text](/path){." + item.Key + " guid=...}.", Abs(item.Start), Abs(item.End));
                                break;
                            }
                            if (item.Key == "nostyle" && ctx == Ctx.Image) { handled = false; break; }
                            if (item.Key == "name") { if (nameItem != null) Duplicate(item); nameItem = nameItem ?? item; }
                            else if (item.Key == "example") { if (exampleItem != null) Duplicate(item); exampleItem = exampleItem ?? item; }
                            else if (item.Key == "broken") { if (brokenItem != null) Duplicate(item); brokenItem = brokenItem ?? item; }
                            else { if (noStyleItem != null) Duplicate(item); noStyleItem = noStyleItem ?? item; }
                            break;
                        case "empty":
                            _context.Report(MarkdownDiagnosticIds.UnsupportedMarkup, MarkdownSeverity.Error, "[]{.empty} stands alone as a paragraph; it is not part of a paragraph with text.", Abs(item.Start), Abs(item.End));
                            break;
                        case "nul": case "surrogate":
                            Malformed(item, "[]{." + item.Key + "} stands alone: it has no text and no other attribute");
                            break;
                        default: handled = false; break;
                    }
                }
                else
                {
                    switch (item.Key)
                    {
                        case "ws" when !item.IsFlag: if (wsItem != null) Duplicate(item); wsItem = wsItem ?? item; break;
                        case "custom-style" when !item.IsFlag: if (csItem != null) Duplicate(item); csItem = csItem ?? item; break;
                        case "guid" when !item.IsFlag:
                            if (form.Guid != null) Duplicate(item);
                            else if (System.Guid.TryParse(item.Value, out var g)) form.Guid = g;
                            else Malformed(item, "guid takes a GUID");
                            break;
                        case "tool" when !item.IsFlag: form.Tool = item.Value; break;
                        case "url" when !item.IsFlag: if (urlItem != null) Duplicate(item); urlItem = urlItem ?? item; break;
                        case "s6" when !item.IsFlag: if (s6Item != null) Duplicate(item); s6Item = s6Item ?? item; break;
                        case "s6x" when !item.IsFlag:
                            if (s6Item != null) Duplicate(item);
                            else if (!AttributeText.TryUnhex(item.Value!, out var unhexed)) Malformed(item, "s6x takes four hex digits for each UTF-16 code unit");
                            else { s6Item = item; form.S6 = unhexed; }
                            break;
                        case "s133" when !item.IsFlag: s133Item = item; handled = false; break;
                        case "ws" or "custom-style" or "guid" or "tool" or "url" or "s6" or "x":
                            _context.Report(MarkdownDiagnosticIds.UnknownAttribute, MarkdownSeverity.Error, "'" + item.Key + "' takes a value: " + item.Key + "=...", Abs(item.Start), Abs(item.End));
                            break;
                        default: handled = false; break;
                    }
                }
                if (!handled) rest.Add(item);
            }
            if (wsItem != null && vItem != null) _context.Report(MarkdownDiagnosticIds.DuplicateAttribute, MarkdownSeverity.Error, ".v and ws both name the writing system. Keep one.", Abs(vItem.Start), Abs(vItem.End));
            if (wsItem != null)
            {
                fmt.Ws = wsItem.Value!;
                fmt.Vernacular = false;
                _context.CheckWritingSystem(fmt.Ws, Abs(wsItem.Start), Abs(wsItem.End));
            }
            else if (vItem != null) { fmt.Ws = null; fmt.Vernacular = true; }
            if (csItem != null)
            {
                if (s133Item != null) Duplicate(s133Item);
                fmt.Cs = CsState.Explicit;
                fmt.CsName = csItem.Value;
                if (_options.Styles != null) _context.CheckStyle(csItem.Value!, false, Abs(csItem.Start), Abs(csItem.End));
                fmt.Props.StrProps.Remove(TextPropertyNumbers.NamedStyle);
            }
            else if (noStyleItem != null) { fmt.Cs = CsState.None; fmt.CsName = null; fmt.Props.StrProps.Remove(TextPropertyNumbers.NamedStyle); }
            if (urlItem != null) { form.Url = urlItem.Value; form.UrlGiven = true; }
            if (s6Item != null && form.S6 == null) form.S6 = s6Item.Value;
            form.Name = nameItem != null;
            form.Example = exampleItem != null;
            form.Broken = brokenItem != null;
            PropertyKeys.Apply(rest, fmt.Props, _context, item => ReportUnknownItem(item));
            if (s133Item != null && csItem == null && fmt.Props.StrProps.TryGetValue(TextPropertyNumbers.NamedStyle, out var styleName)) { fmt.Cs = CsState.Explicit; fmt.CsName = styleName; }
            return Finish(fmt, form);
        }

        void ReportUnknownItem(AttrItem item)
        {
            if (item.Key == "style" && !item.IsClass) { _context.Report(MarkdownDiagnosticIds.UnknownAttribute, MarkdownSeverity.Error, "'style' is not part of the notation. A run's character style is custom-style.", Abs(item.Start), Abs(item.End)); return; }
            _context.ReportUnknown(item, RunKeys());
        }

        static IEnumerable<string> RunKeys()
        {
            foreach (var k in new[] { ".v", ".name", ".example", ".broken", ".nostyle", "ws", "custom-style", "guid", "tool", "url" }) yield return k;
            foreach (var k in PropertyKeys.AllKeys()) yield return k;
        }

        void Duplicate(AttrItem item) => _context.Report(MarkdownDiagnosticIds.DuplicateAttribute, MarkdownSeverity.Error, "'" + item.Spelling + "' appears twice. Keep one.", Abs(item.Start), Abs(item.End));
        void Malformed(AttrItem item, string message) => _context.Report(MarkdownDiagnosticIds.MalformedAttribute, MarkdownSeverity.Error, message, Abs(item.Start), Abs(item.End));

        /// <summary>Gives the formatting its object data once the whole attribute block has been read.</summary>
        Fmt Finish(Fmt fmt, ObjectForm form)
        {
            if (!form.HasObject) return fmt;
            int start = Abs(form.Start), end = Abs(form.End) + 1;
            if (form.S6 != null)
            {
                if (form.IsPicture || form.Dest != null)
                    _context.Report(MarkdownDiagnosticIds.DuplicateAttribute, MarkdownSeverity.Error, "s6 is the object data; it cannot be combined with a link or picture.", start, end);
                fmt.ObjData = form.S6;
                fmt.Scope = ++_scopes;
                return fmt;
            }
            var objects = new ObjectBuilder(_context, form.Start, form.End);
            var target = form.IsPicture ? objects.BuildPicture(form.Dest!, form.Guid, form.Caption, form.Url, form.UrlGiven, form.Tool)
                : objects.BuildLink(form.Dest!, form.Guid, form.Url, form.UrlGiven, form.Tool, form.Name, form.Example, form.Broken);
            if (target == null) return fmt;
            target.Source = _context.RangeOf(start, end);
            _context.AddObject(target);
            bool resolved;
            string objData;
            MarkdownProblem? problem;
            if (_options.Objects is IObjectResolver resolver) resolved = resolver.TryResolve(target, out objData, out problem);
            else
            {
                resolved = _options.Objects.TryCreateObjData(target, out objData, out string? reason);
                problem = reason == null ? null : new MarkdownProblem(reason);
            }
            if (resolved)
            {
                target.ObjData = objData;
                fmt.ObjData = objData;
                fmt.Scope = ++_scopes;
                if (!form.IsPicture) fmt.IsLink = true;
            }
            // A host's warning or suggestion comes with the data (a deleted target); a failure is always an error.
            if (problem != null || !resolved)
            {
                problem = problem ?? new MarkdownProblem("The object cannot be resolved.");
                string id = problem.Id ?? (form.IsPicture ? MarkdownDiagnosticIds.PictureUnknown : MarkdownDiagnosticIds.LinkNotFound);
                _context.Report(id, resolved ? problem.Severity : MarkdownSeverity.Error, problem.Message, start, end, problem.Replacement,
                    problem.Choices.Count > 0 ? problem.Choices : null, problem.Nearest.Count > 0 ? problem.Nearest : null);
            }
            return fmt;
        }
    }
}
