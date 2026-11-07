using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Xml;

namespace SIL.TextEditing
{
    /// <summary>The debug and test interchange of paragraphs (ADR 0029 retired it from the clipboard; <see cref="InterchangeClipboardForm"/> lets a test or the playground put it there): an XML form that carries every run, writing system
    /// and property, and a JSON dump for comparing documents in tests. Control characters and U+FFFC are escaped,
    /// so any XML reader gets the exact UTF-16 text back.</summary>
    public static class Interchange
    {
        public const string ClipboardFormat = "SIL.TextEditing.Runs.v1";
        const string IntPrefix = "i_";
        const string StrPrefix = "s_";

        public static string ToXml(IEnumerable<Paragraph> paragraphs)
        {
            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { OmitXmlDeclaration = true };
            using (var w = XmlWriter.Create(sb, settings))
            {
                w.WriteStartElement("Runs");
                foreach (var p in paragraphs)
                {
                    w.WriteStartElement("Para");
                    w.WriteAttributeString("style", p.Style);
                    w.WriteAttributeString("rtl", p.Rtl ? "1" : "0");
                    w.WriteAttributeString("defws", p.DefaultWs);
                    WriteBag(w, p.Props);
                    foreach (var r in p.Runs)
                    {
                        w.WriteStartElement("Run");
                        w.WriteAttributeString("ws", r.Ws);
                        if (r.ObjData != null) w.WriteAttributeString("objdata", Escape(r.ObjData));
                        WriteBag(w, r.Props);
                        w.WriteString(Escape(r.Text));
                        w.WriteEndElement();
                    }
                    w.WriteEndElement();
                }
                w.WriteEndElement();
            }
            return sb.ToString();
        }

        public static List<Paragraph> FromXml(string xml)
        {
            var result = new List<Paragraph>();
            var doc = new XmlDocument { XmlResolver = null };
            doc.LoadXml(xml);
            var root = doc.DocumentElement;
            if (root == null) return result;
            foreach (XmlElement pe in root.SelectNodes("Para")!)
            {
                var p = new Paragraph
                {
                    Style = pe.GetAttribute("style"),
                    Rtl = pe.GetAttribute("rtl") == "1",
                    DefaultWs = pe.GetAttribute("defws"),
                };
                ReadBag(pe, p.Props);
                foreach (XmlElement re in pe.SelectNodes("Run")!)
                {
                    var r = new Run { Ws = re.GetAttribute("ws"), Text = Unescape(re.InnerText) };
                    if (re.HasAttribute("objdata")) r.ObjData = Unescape(re.GetAttribute("objdata"));
                    ReadBag(re, r.Props);
                    p.Runs.Add(r);
                }
                result.Add(p);
            }
            return result;
        }

        static void WriteBag(XmlWriter w, PropertyBag bag)
        {
            foreach (var kv in bag.IntProps)
                w.WriteAttributeString(IntPrefix + kv.Key.ToString(CultureInfo.InvariantCulture), kv.Value.Variation.ToString(CultureInfo.InvariantCulture) + ":" + kv.Value.Value.ToString(CultureInfo.InvariantCulture));
            foreach (var kv in bag.StrProps)
                w.WriteAttributeString(StrPrefix + kv.Key.ToString(CultureInfo.InvariantCulture), Escape(kv.Value));
        }

        static void ReadBag(XmlElement element, PropertyBag bag)
        {
            foreach (XmlAttribute a in element.Attributes)
            {
                if (a.Name.StartsWith(IntPrefix, StringComparison.Ordinal))
                {
                    int split = a.Value.IndexOf(':');
                    bag.SetInt(int.Parse(a.Name.Substring(IntPrefix.Length), CultureInfo.InvariantCulture),
                        int.Parse(a.Value.Substring(split + 1), CultureInfo.InvariantCulture),
                        int.Parse(a.Value.Substring(0, split), CultureInfo.InvariantCulture));
                }
                else if (a.Name.StartsWith(StrPrefix, StringComparison.Ordinal))
                {
                    bag.SetStr(int.Parse(a.Name.Substring(StrPrefix.Length), CultureInfo.InvariantCulture), Unescape(a.Value));
                }
            }
        }

        // XML cannot carry control characters; U+FFFC is legal but escaped too so it survives readers that drop it.
        static string Escape(string s)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                bool pair = char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]);
                if (pair) { sb.Append(c).Append(s[++i]); continue; }
                if (c == '\\') sb.Append("\\\\");
                else if (c < 0x20 || char.IsSurrogate(c) || c == Run.ObjectReplacement) sb.Append("\\u").Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
                else sb.Append(c);
            }
            return sb.ToString();
        }

        static string Unescape(string s)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '\\' && i + 1 < s.Length)
                {
                    if (s[i + 1] == '\\') { sb.Append('\\'); i++; continue; }
                    if (s[i + 1] == 'u' && i + 6 <= s.Length)
                    {
                        sb.Append((char)int.Parse(s.Substring(i + 2, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                        i += 5;
                        continue;
                    }
                }
                sb.Append(s[i]);
            }
            return sb.ToString();
        }

        /// <summary>A JSON dump of paragraph identity, runs and properties, for exact comparison in tests.</summary>
        public static string Json(Document document)
        {
            var sb = new StringBuilder("{\"paras\":[");
            for (int i = 0; i < document.Paragraphs.Count; i++)
            {
                var p = document.Paragraphs[i];
                if (i > 0) sb.Append(',');
                sb.Append("{\"key\":").Append(p.Key == null ? "null" : Quote(Convert.ToString(p.Key, System.Globalization.CultureInfo.InvariantCulture) ?? "")).Append(",\"style\":").Append(Quote(p.Style)).Append(",\"rtl\":").Append(p.Rtl ? "true" : "false").Append(",\"runs\":[");
                int pos = 0;
                for (int k = 0; k < p.Runs.Count; k++)
                {
                    var r = p.Runs[k];
                    if (k > 0) sb.Append(',');
                    sb.Append("{\"start\":").Append(pos).Append(",\"len\":").Append(r.Text.Length).Append(",\"ws\":").Append(Quote(r.Ws)).Append(",\"text\":").Append(Quote(r.Text));
                    if (r.ObjData != null) sb.Append(",\"objdata\":").Append(Quote(r.ObjData));
                    if (r.Props.IntProps.Count > 0)
                        sb.Append(",\"int\":{").Append(string.Join(",", r.Props.IntProps.Select(kv => "\"" + kv.Key + "\":[" + kv.Value.Variation + "," + kv.Value.Value + "]"))).Append('}');
                    if (r.Props.StrProps.Count > 0)
                        sb.Append(",\"str\":{").Append(string.Join(",", r.Props.StrProps.Select(kv => "\"" + kv.Key + "\":" + Quote(kv.Value)))).Append('}');
                    sb.Append('}');
                    pos += r.Text.Length;
                }
                sb.Append("]}");
            }
            return sb.Append("]}").ToString();
        }

        static string Quote(string s)
        {
            var sb = new StringBuilder("\"");
            foreach (var c in s)
            {
                if (c == '"') sb.Append("\\\"");
                else if (c == '\\') sb.Append("\\\\");
                else if (c < 0x20 || (c >= 0xD800 && c <= 0xF8FF) || c == Run.ObjectReplacement || (c >= 0x300 && c <= 0x36F)) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                else sb.Append(c);
            }
            return sb.Append('"').ToString();
        }

        /// <summary>UTF-16 code units as hex, for byte-exact comparison.</summary>
        public static string Utf16Hex(string s) => string.Join(" ", s.Select(c => ((int)c).ToString("X4", CultureInfo.InvariantCulture)));
    }
}
