using System.Linq;
using System.Text;

namespace SIL.TextEditing.Markdown.Tests
{
    /// <summary>A compact text form of a document, so a test states the whole expected model in one line per paragraph.
    /// A paragraph is <c>Style| run run</c>; a run is <c>ws"text"</c> followed by its properties and object data.</summary>
    internal static class ModelDump
    {
        public static string Of(Document document) => string.Join("\n", document.Paragraphs.Select(Of));

        public static string Of(Paragraph paragraph)
        {
            var sb = new StringBuilder();
            sb.Append(paragraph.Style).Append('|');
            var other = paragraph.Props.Clone();
            other.StrProps.Remove(TextPropertyNumbers.NamedStyle);
            if (other.Count > 0) sb.Append(" para{").Append(Bag(other)).Append('}');
            if (paragraph.Runs.Count == 0) sb.Append(" (empty ").Append(paragraph.DefaultWs).Append(')');
            foreach (var run in paragraph.Runs) sb.Append(' ').Append(Of(run));
            return sb.ToString();
        }

        public static string Of(Run run)
        {
            var sb = new StringBuilder();
            sb.Append(run.Ws).Append('"').Append(Escape(run.Text)).Append('"');
            if (run.Props.Count > 0) sb.Append('{').Append(Bag(run.Props)).Append('}');
            if (run.ObjData != null) sb.Append("<obj ").Append(Escape(run.ObjData)).Append('>');
            return sb.ToString();
        }

        static string Bag(PropertyBag bag)
        {
            var parts = bag.IntProps.Select(kv => "p" + kv.Key + "=" + kv.Value.Value + ":" + kv.Value.Variation)
                .Concat(bag.StrProps.Select(kv => "s" + kv.Key + "=" + Escape(kv.Value)));
            return string.Join(",", parts);
        }

        public static string Escape(string text)
        {
            var sb = new StringBuilder();
            foreach (char c in text)
            {
                if (c == '"') sb.Append("\\\"");
                else if (c == '\\') sb.Append("\\\\");
                else if (c < 32 || c == 0x7F || c == 0x2028 || c == 0x2029 || c == 0xFFFC || c == 0xA0 || char.IsSurrogate(c)) sb.Append("\\x").Append(((int)c).ToString("X4"));
                else sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
