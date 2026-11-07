using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using SIL.TextEditing.Markdown.Notation;
using SIL.TextEditing.Markdown.Reader;

namespace SIL.TextEditing.Markdown.Tests.Conformance
{
    /// <summary>Something Pandoc read that the format has no meaning for, or read differently from the profile.</summary>
    internal sealed class PandocMismatch : Exception
    {
        public PandocMismatch(string message) : base(message) { }
    }

    /// <summary>Maps Pandoc's reading of a FieldWorks Markdown file (<c>-t json</c>) to the run model, as the spec's profile says each construct means: a span is a
    /// run with attributes, a div is a style, a link is an object. The conformance test compares the result with what this library reads, so a spelling Pandoc
    /// would read differently shows up as a failure (spec 6.4). The meaning of an attribute is the library's own (<see cref="PropertyKeys"/>); what Pandoc is asked
    /// is which text, which spans, which attributes and which destination there are.</summary>
    internal sealed class PandocModel
    {
        readonly MarkdownOptions _options;
        readonly ReaderContext _context;
        string _analysis, _vernacular;

        PandocModel(MarkdownOptions options)
        {
            _options = options;
            _context = new ReaderContext(options, string.Empty);
            _analysis = options.WritingSystems.DefaultAnalysis;
            _vernacular = options.WritingSystems.DefaultVernacular;
        }

        public static List<Paragraph> Convert(string json, MarkdownOptions options)
        {
            var model = new PandocModel(options);
            var root = Json.Obj(Json.Parse(json));
            var meta = Json.Obj(root["meta"]);
            if (meta.TryGetValue("analysis", out var a)) model._analysis = MetaText(a);
            if (meta.TryGetValue("vernacular", out var v)) model._vernacular = MetaText(v);
            var paragraphs = new List<Paragraph>();
            model.Blocks(Json.Arr(root["blocks"]), null, null, paragraphs);
            return paragraphs;
        }

        static string MetaText(object? meta)
        {
            var o = Json.Obj(meta);
            string type = Json.Str(o["t"]);
            if (type == "MetaString") return Json.Str(o["c"]);
            if (type == "MetaInlines") return string.Concat(Json.Arr(o["c"]).Select(i => Json.OptStr(Json.Obj(i), "c") ?? string.Empty));
            throw new PandocMismatch("front matter value of type " + type);
        }

        // ---------------------------------------------------------------- blocks

        MarkdownStyleNames Names => _options.StyleNames;

        void Blocks(List<object?> blocks, string? style, PropertyBag? props, List<Paragraph> into)
        {
            foreach (var item in blocks)
            {
                var block = Json.Obj(item);
                string type = Json.Str(block["t"]);
                switch (type)
                {
                    case "Para":
                    case "Plain":
                        into.Add(ParagraphOf(Json.Arr(block["c"]), style ?? Names.Normal, props));
                        break;
                    case "Header":
                    {
                        var c = Json.Arr(block["c"]);
                        int level = (int)(long)c[0]!;
                        string? headingStyle = Names.Heading(level);
                        if (headingStyle == null) throw new PandocMismatch("a heading of level " + level);
                        into.Add(ParagraphOf(Json.Arr(c[2]), headingStyle, null));
                        break;
                    }
                    case "BulletList":
                        foreach (var entry in Json.Arr(block["c"])) ListItem(Json.Arr(entry), Names.BulletedList, into);
                        break;
                    case "OrderedList":
                        foreach (var entry in Json.Arr(Json.Arr(block["c"])[1])) ListItem(Json.Arr(entry), Names.NumberedList, into);
                        break;
                    case "BlockQuote":
                        foreach (var inner in Json.Arr(block["c"]))
                        {
                            var innerBlock = Json.Obj(inner);
                            string innerType = Json.Str(innerBlock["t"]);
                            if (innerType != "Para" && innerType != "Plain") throw new PandocMismatch("a block quote holding " + innerType);
                            into.Add(ParagraphOf(Json.Arr(innerBlock["c"]), Names.BlockQuote, null));
                        }
                        break;
                    case "Div":
                    {
                        var c = Json.Arr(block["c"]);
                        var attr = Attr(c[0]);
                        string? divStyle = null;
                        var items = new List<AttrItem>();
                        foreach (var kv in attr.Pairs)
                        {
                            if (kv.Key == "custom-style") divStyle = kv.Value;
                            else items.Add(new AttrItem(kv.Key, kv.Value, false, 0, 0));
                        }
                        var divProps = new PropertyBag();
                        PropertyKeys.Apply(items, divProps, _context, item => throw new PandocMismatch("a div attribute " + item.Key));
                        divProps.StrProps.Remove(TextPropertyNumbers.NamedStyle);
                        Blocks(Json.Arr(c[1]), divStyle ?? Names.Normal, divProps, into);
                        break;
                    }
                    case "Table":
                        Table(Json.Arr(block["c"]), into);
                        break;
                    default:
                        throw new PandocMismatch("a block of type " + type);
                }
            }
        }

        void ListItem(List<object?> blocks, string style, List<Paragraph> into)
        {
            if (blocks.Count == 0) { into.Add(ParagraphOf(new List<object?>(), style, null)); return; } // a marker with nothing after it
            if (blocks.Count != 1) throw new PandocMismatch("a list item with " + blocks.Count + " blocks");
            var block = Json.Obj(blocks[0]);
            string type = Json.Str(block["t"]);
            if (type != "Para" && type != "Plain") throw new PandocMismatch("a list item holding " + type);
            into.Add(ParagraphOf(Json.Arr(block["c"]), style, null));
        }

        void Table(List<object?> c, List<Paragraph> into)
        {
            var head = Json.Arr(c[3]);
            foreach (var row in Json.Arr(head[1])) into.Add(Row(Json.Arr(row), Names.TableHeader!));
            foreach (var body in Json.Arr(c[4]))
            {
                var b = Json.Arr(body);
                foreach (var row in Json.Arr(b[3])) into.Add(Row(Json.Arr(row), Names.TableRow!));
            }
        }

        Paragraph Row(List<object?> row, string style)
        {
            var runs = new List<Run>();
            bool first = true;
            foreach (var cell in Json.Arr(row[1]))
            {
                if (!first) runs.Add(new Run { Text = "\t", Ws = _analysis });
                first = false;
                foreach (var block in Json.Arr(Json.Arr(cell)[4]))
                {
                    var b = Json.Obj(block);
                    if (Json.Str(b["t"]) != "Plain" && Json.Str(b["t"]) != "Para") throw new PandocMismatch("a table cell holding " + Json.Str(b["t"]));
                    Inlines(Json.Arr(b["c"]), new Fmt(), runs);
                }
            }
            return Make(style, new PropertyBag(), runs, null);
        }

        Paragraph ParagraphOf(List<object?> inlines, string style, PropertyBag? props)
        {
            // [] {.empty ...} alone is an empty paragraph.
            if (inlines.Count == 1 && Json.Str(Json.Obj(inlines[0])["t"]) == "Span")
            {
                var c = Json.Arr(Json.Obj(inlines[0])["c"]);
                var attr = Attr(c[0]);
                if (attr.Classes.Contains("empty") && Json.Arr(c[1]).Count == 0)
                {
                    string? ws = null;
                    if (attr.Classes.Contains("v")) ws = _vernacular;
                    var markerProps = props == null ? new PropertyBag() : props.Clone();
                    var items = new List<AttrItem>();
                    foreach (var kv in attr.Pairs)
                    {
                        if (kv.Key == "ws") ws = kv.Value;
                        else if (kv.Key == "custom-style") style = kv.Value;
                        else items.Add(new AttrItem(kv.Key, kv.Value, false, 0, 0));
                    }
                    PropertyKeys.Apply(items, markerProps, _context, item => throw new PandocMismatch("an attribute " + item.Key));
                    return Make(style, markerProps, new List<Run>(), ws);
                }
            }
            var runs = new List<Run>();
            Inlines(inlines, new Fmt(), runs);
            return Make(style, props ?? new PropertyBag(), runs, null);
        }

        Paragraph Make(string style, PropertyBag props, List<Run> runs, string? emptyWs)
        {
            var paragraph = new Paragraph { Style = style, Props = props.Clone() };
            foreach (var run in runs) paragraph.Runs.Add(run);
            if (style.Length > 0) paragraph.Props.StrProps[TextPropertyNumbers.NamedStyle] = style;
            paragraph.Rtl = paragraph.Props.TryGetInt(128, out int rtl) && rtl != 0;
            paragraph.Normalize();
            if (_options.NormalizeText) foreach (var run in paragraph.Runs) run.Text = TextNormalizer.Normalize(run.Text, NormalizationForm.FormD);
            paragraph.DefaultWs = paragraph.Runs.Count > 0 ? paragraph.Runs[0].Ws : emptyWs ?? _analysis;
            return paragraph;
        }

        // ---------------------------------------------------------------- inlines

        sealed class PandocAttr
        {
            public string Id = string.Empty;
            public List<string> Classes = new List<string>();
            public List<KeyValuePair<string, string>> Pairs = new List<KeyValuePair<string, string>>();
        }

        static PandocAttr Attr(object? value)
        {
            var a = Json.Arr(value);
            var attr = new PandocAttr { Id = Json.Str(a[0]) };
            foreach (var c in Json.Arr(a[1])) attr.Classes.Add(Json.Str(c));
            foreach (var kv in Json.Arr(a[2])) { var p = Json.Arr(kv); attr.Pairs.Add(new KeyValuePair<string, string>(Json.Str(p[0]), Json.Str(p[1]))); }
            return attr;
        }

        void Add(List<Run> runs, string text, Fmt fmt)
        {
            if (text.Length == 0) return;
            var run = new Run { Text = text, Ws = fmt.Ws ?? (fmt.Vernacular ? _vernacular : _analysis), Props = fmt.Props.Clone(), ObjData = fmt.ObjData };
            string? cs = fmt.Cs == CsState.Explicit ? fmt.CsName : fmt.Cs == CsState.None ? null : (fmt.IsLink ? _options.StyleNames.Hyperlink : null);
            if (cs != null) run.Props.StrProps[TextPropertyNumbers.NamedStyle] = cs;
            runs.Add(run);
        }

        void Inlines(List<object?> inlines, Fmt fmt, List<Run> runs)
        {
            foreach (var item in inlines)
            {
                var node = Json.Obj(item);
                string type = Json.Str(node["t"]);
                switch (type)
                {
                    case "Str": Add(runs, Json.Str(node["c"]), fmt); break;
                    case "Space": case "SoftBreak": Add(runs, " ", fmt); break;
                    case "LineBreak": Add(runs, Chars.LineSeparatorText, fmt); break;
                    case "Emph": Inlines(Json.Arr(node["c"]), With(fmt, TextPropertyNumbers.Italic, TextPropertyNumbers.ToggleOn), runs); break;
                    case "Strong": Inlines(Json.Arr(node["c"]), With(fmt, TextPropertyNumbers.Bold, TextPropertyNumbers.ToggleOn), runs); break;
                    case "Superscript": Inlines(Json.Arr(node["c"]), With(fmt, TextPropertyNumbers.Superscript, 1), runs); break;
                    case "Subscript": Inlines(Json.Arr(node["c"]), With(fmt, TextPropertyNumbers.Superscript, 2), runs); break;
                    case "Span": Span(Json.Arr(node["c"]), fmt, runs); break;
                    case "Link": Link(Json.Arr(node["c"]), fmt, runs, false); break;
                    case "Image": Link(Json.Arr(node["c"]), fmt, runs, true); break;
                    default: throw new PandocMismatch("an inline of type " + type);
                }
            }
        }

        static Fmt With(Fmt fmt, int number, int value)
        {
            var inner = fmt.Clone();
            inner.Props.IntProps[number] = new IntPropValue(PropertyKeys.VarEnum, value);
            return inner;
        }

        void Span(List<object?> c, Fmt fmt, List<Run> runs)
        {
            var attr = Attr(c[0]);
            var content = Json.Arr(c[1]);
            if (attr.Classes.Contains("nul")) { Add(runs, "\0", fmt); return; }
            if (attr.Classes.Contains("surrogate"))
            {
                string x = attr.Pairs.First(p => p.Key == "x").Value;
                Add(runs, ((char)int.Parse(x, NumberStyles.HexNumber, CultureInfo.InvariantCulture)).ToString(), fmt);
                return;
            }
            var form = new LinkForm();
            var inner = Interpret(attr, fmt, form);
            if (form.S6 != null)
            {
                inner.ObjData = form.S6;
                var text = new List<Run>();
                Inlines(content, inner, text);
                Add(runs, string.Concat(text.Select(r => r.Text)), inner);
                return;
            }
            Inlines(content, inner, runs);
        }

        sealed class LinkForm
        {
            public Guid? Guid;
            public string? Tool, Url, S6;
            public bool UrlGiven, Name, Example, Broken;
        }

        /// <summary>The formatting an attribute block gives: the writing system, the character style, the properties, and what it says about a link.</summary>
        Fmt Interpret(PandocAttr attr, Fmt parent, LinkForm form)
        {
            var fmt = parent.Clone();
            var rest = new List<AttrItem>();
            bool vernacular = false;
            string? ws = null, customStyle = null;
            foreach (string cls in attr.Classes)
            {
                switch (cls)
                {
                    case "v": vernacular = true; break;
                    case "nostyle": fmt.Cs = CsState.None; fmt.CsName = null; break;
                    case "name": form.Name = true; break;
                    case "example": form.Example = true; break;
                    case "broken": form.Broken = true; break;
                    default: throw new PandocMismatch("a class " + cls);
                }
            }
            foreach (var kv in attr.Pairs)
            {
                switch (kv.Key)
                {
                    case "ws": ws = kv.Value; break;
                    case "custom-style": customStyle = kv.Value; break;
                    case "guid": form.Guid = System.Guid.Parse(kv.Value); break;
                    case "tool": form.Tool = kv.Value; break;
                    case "url": form.Url = kv.Value; form.UrlGiven = true; break;
                    case "s6": form.S6 = kv.Value; break;
                    case "s6x": AttributeText.TryUnhex(kv.Value, out var s6); form.S6 = s6; break;
                    default: rest.Add(new AttrItem(kv.Key, kv.Value, false, 0, 0)); break;
                }
            }
            if (ws != null) { fmt.Ws = ws; fmt.Vernacular = false; }
            else if (vernacular) { fmt.Ws = null; fmt.Vernacular = true; }
            if (customStyle != null) { fmt.Cs = CsState.Explicit; fmt.CsName = customStyle; }
            PropertyKeys.Apply(rest, fmt.Props, _context, item => throw new PandocMismatch("an attribute " + item.Key));
            return fmt;
        }

        void Link(List<object?> c, Fmt fmt, List<Run> runs, bool image)
        {
            var attr = Attr(c[0]);
            var content = Json.Arr(c[1]);
            string dest = Json.Str(Json.Arr(c[2])[0]);
            var form = new LinkForm();
            var inner = Interpret(attr, fmt, form);
            var objects = new ObjectBuilder(_context, 0, 0);
            MarkdownObject? target;
            if (image)
            {
                var alt = new List<Run>();
                Inlines(content, new Fmt(), alt);
                target = objects.BuildPicture(dest, form.Guid, string.Concat(alt.Select(r => r.Text)), form.Url, form.UrlGiven, form.Tool);
            }
            else
            {
                // Pandoc percent-encodes the characters a URL cannot hold as they are; the destination as written is what the format means.
                string pandocDest = dest;
                foreach (var pair in new[] { "%20| ", "%7C||", "%22|\"", "%3C|<", "%3E|>", "%5C|\\", "%5E|^", "%60|`", "%7B|{", "%7D|}", "%5B|[", "%5D|]" }) pandocDest = pandocDest.Replace(pair.Substring(0, 3), pair.Substring(4));
                target = objects.BuildLink(pandocDest, form.Guid, form.Url, form.UrlGiven, form.Tool, form.Name, form.Example, form.Broken);
            }
            if (target == null) throw new PandocMismatch("a link the library cannot build: " + dest);
            if (!_options.Objects.TryCreateObjData(target, out string objData, out string? problem)) throw new PandocMismatch("a link the host cannot resolve: " + problem);
            inner.ObjData = objData;
            if (!image) inner.IsLink = true;
            if (image) { Add(runs, Run.ObjectReplacement.ToString(), inner); return; }
            var text = new List<Run>();
            Inlines(content, inner, text);
            Add(runs, string.Concat(text.Select(r => r.Text)), inner);
        }
    }
}
