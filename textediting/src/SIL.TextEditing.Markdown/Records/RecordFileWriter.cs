using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace SIL.TextEditing.Markdown.Records
{
    /// <summary>Writes a <see cref="RecordDocument"/> as a canonical record file (ADR 0032, owner decisions R-2, R-5, R-6): YAML front matter in the strict
    /// profile, then the body when the record has one. The writer is a function of the document and the schema alone.</summary>
    public static class RecordFileWriter
    {
        /// <summary>The canonical text: block style, two-space indentation, <c>\n</c> line ends; the front-matter keys in the order <c>fwmd</c>,
        /// <c>record</c>, <c>schema</c>, <c>root</c>, <c>base</c>, <c>analysis</c>, <c>vernacular</c>, <c>writingSystems</c>, <c>record-data</c>; in each
        /// object <c>guid</c> first, then the schema's field order; every string double-quoted with only <c>\"</c> and <c>\\</c> escaped; every rich value a
        /// <c>|-</c> block; plain text that cannot be quoted so (a control character) a <c>|-</c> block of inline FieldWorks Markdown; integers and Booleans
        /// plain; empty values left out; no anchors, aliases, tags, flow collections or comments. The body field's value, if any, is the Markdown body after
        /// one blank line.</summary>
        /// <exception cref="RecordWriteException">The document does not fit the schema, or holds a value the profile cannot spell.</exception>
        public static string Write(RecordDocument document, RecordSchema schema)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            if (schema == null) throw new ArgumentNullException(nameof(schema));
            var kind = schema.Kind(document.Kind) ?? throw new RecordWriteException("The schema has no record kind " + document.Kind + ".");
            var root = RecordRoot.Resolve(kind, document.Root, out string? error) ?? throw new RecordWriteException(error!);

            var sb = new StringBuilder("---\n");
            sb.Append("fwmd: ").Append(MarkdownFrontMatter.CurrentVersion.ToString(CultureInfo.InvariantCulture)).Append('\n');
            Pair(sb, "record", document.Kind);
            Pair(sb, "schema", schema.Id);
            if (document.Root != null) Pair(sb, "root", document.Root);
            if (document.Base != null) Pair(sb, "base", document.Base);
            if (document.Analysis != null) Pair(sb, "analysis", document.Analysis);
            if (document.Vernacular != null) Pair(sb, "vernacular", document.Vernacular);
            if (document.WritingSystems.Count > 0)
            {
                sb.Append("writingSystems:\n");
                foreach (var ws in document.WritingSystems)
                {
                    if (!RecordProfile.CanQuote(ws.Key)) throw new RecordWriteException("The writing-system id " + ws.Key + " holds a control character or a line separator.");
                    sb.Append("  ").Append(RecordProfile.Key(ws.Key)).Append(":\n");
                    Pair(sb, "lang", ws.Value.Language, 4);
                    Pair(sb, "dir", ws.Value.Direction, 4);
                    Pair(sb, "font", ws.Value.Font, 4);
                }
            }

            // The body's value comes out of the data; everything else is the frame.
            var data = document.Data ?? throw new RecordWriteException("The document has no data.");
            RecordScalar? body = null;
            string? bodyField = root.Body?.Name;
            if (root.Body != null)
            {
                var value = data[root.Body.Name];
                if (root.Body.MultiWritingSystem)
                {
                    if (value != null && !(value is RecordAlternatives)) throw Shape(root.Body.Name, "alternatives per writing system");
                    var alts = (RecordAlternatives?)value;
                    if (alts != null && alts.Items.Any(i => i.Key != root.WritingSystem && !i.Value.IsEmpty))
                        throw new RecordWriteException("The file's root names " + root.Body.Name + "." + root.WritingSystem + ", but the data has other alternatives.");
                    body = alts?[root.WritingSystem!];
                }
                else
                {
                    if (value != null && !(value is RecordScalar)) throw Shape(root.Body.Name, "a rich value");
                    body = (RecordScalar?)value;
                }
            }
            if (root.Field != null)
            {
                foreach (var f in data.Fields)
                {
                    if (f.Key != root.Field.Name && !f.Value.IsEmpty) throw new RecordWriteException("The file's root names the field " + root.Field.Name + ", but the data also has " + f.Key + ".");
                }
                if (data.Guid != null) throw new RecordWriteException("The file's root names the field " + root.Field.Name + ", so its data has no GUID.");
            }

            var frame = new StringBuilder();
            WriteFields(frame, data, root.Type, 2, bodyField, string.Empty);
            if (frame.Length > 0) sb.Append("record-data:\n").Append(frame);
            sb.Append("---\n");
            if (body != null && !body.IsEmpty)
            {
                string? problem = RecordProfile.LiteralProblem(body.Text);
                if (problem != null) throw new RecordWriteException("The body (" + bodyField + ") cannot be written: " + problem + ".");
                sb.Append('\n').Append(body.Text).Append('\n');
            }
            return sb.ToString();
        }

        static void Pair(StringBuilder sb, string key, string value, int indent = 0)
        {
            if (!RecordProfile.CanQuote(value)) throw new RecordWriteException("The front-matter value of " + key + " holds a control character.");
            sb.Append(' ', indent).Append(key).Append(": ").Append(RecordProfile.Quote(value)).Append('\n');
        }

        static RecordWriteException Shape(string path, string expected) => new RecordWriteException("The value of " + path + " is not " + expected + ".");

        /// <summary>Writes the fields of an object at <paramref name="indent"/>: <c>guid</c> first, then the schema's order. <paramref name="skip"/> is the
        /// body field, which is not in the frame.</summary>
        static void WriteFields(StringBuilder sb, RecordObject o, RecordObjectType type, int indent, string? skip, string path)
        {
            if (o.Guid != null)
            {
                if (!type.HasGuid) throw new RecordWriteException("Objects of " + type.Name + " have no GUID (" + Display(path) + ").");
                sb.Append(' ', indent).Append("guid: ").Append(RecordProfile.Quote(o.Guid.Value.ToString("D"))).Append('\n');
            }
            foreach (var f in o.Fields)
            {
                if (type.Field(f.Key) == null) throw new RecordWriteException(type.Name + " has no field " + f.Key + " (" + Display(path) + ").");
            }
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var f in o.Fields) if (!seen.Add(f.Key)) throw new RecordWriteException("The field " + f.Key + " is in " + Display(path) + " twice.");
            foreach (var field in type.Fields)
            {
                if (field.Name == skip) continue;
                var value = o[field.Name];
                if (value == null || value.IsEmpty) continue;
                WriteField(sb, field, value, indent, Join(path, field.Name));
            }
        }

        static string Join(string path, string name) => path.Length == 0 ? name : path + "/" + name;
        static string Display(string path) => path.Length == 0 ? "the data" : path;

        static void WriteField(StringBuilder sb, RecordField field, RecordValue value, int indent, string path)
        {
            sb.Append(' ', indent).Append(RecordProfile.Key(field.Name)).Append(':');
            if (field.MultiWritingSystem)
            {
                if (!(value is RecordAlternatives alts)) throw Shape(path, "alternatives per writing system");
                sb.Append('\n');
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var alt in alts.Items)
                {
                    if (!seen.Add(alt.Key)) throw new RecordWriteException("The writing system " + alt.Key + " is in " + path + " twice.");
                    if (alt.Value.IsEmpty) continue;
                    if (!RecordProfile.CanQuote(alt.Key)) throw new RecordWriteException("The writing-system id " + alt.Key + " in " + path + " holds a control character.");
                    sb.Append(' ', indent + 2).Append(RecordProfile.Key(alt.Key)).Append(':');
                    WriteScalar(sb, field, alt.Value, indent + 4, path + "." + alt.Key);
                }
                return;
            }
            switch (field.Kind)
            {
                case RecordFieldKind.Sequence:
                {
                    if (!(value is RecordList list)) throw Shape(path, "a list");
                    sb.Append('\n');
                    for (int i = 0; i < list.Items.Count; i++)
                    {
                        string itemPath = path + "/" + (i + 1).ToString(CultureInfo.InvariantCulture);
                        WriteItem(sb, field.Item!, list.Items[i], indent + 2, itemPath);
                    }
                    return;
                }
                case RecordFieldKind.Object:
                {
                    if (!(value is RecordObject o)) throw Shape(path, "an object");
                    sb.Append('\n');
                    WriteFields(sb, o, field.ObjectType!, indent + 2, null, path);
                    return;
                }
                default:
                    WriteScalar(sb, field, value, indent + 2, path);
                    return;
            }
        }

        static void WriteItem(StringBuilder sb, RecordField item, RecordValue value, int indent, string path)
        {
            if (item.Kind == RecordFieldKind.Object)
            {
                if (!(value is RecordObject o)) throw Shape(path, "an object");
                var inner = new StringBuilder();
                WriteFields(inner, o, item.ObjectType!, indent + 2, null, path);
                if (inner.Length == 0) throw new RecordWriteException("The object at " + path + " has nothing in it, and an empty item cannot be written.");
                // The first line's indentation becomes the dash: "    guid: ..." is "  - guid: ...".
                sb.Append(' ', indent).Append("- ").Append(inner.ToString(), indent + 2, inner.Length - indent - 2);
                return;
            }
            sb.Append(' ', indent).Append('-');
            if (value.IsEmpty) throw new RecordWriteException("The item at " + path + " is empty, and an empty item cannot be written.");
            WriteScalar(sb, item, value, indent + 2, path);
        }

        /// <summary>Writes a scalar after its key or dash (which the caller has written, with no space after it), ending the line. Lines of a block go at
        /// <paramref name="blockIndent"/>.</summary>
        static void WriteScalar(StringBuilder sb, RecordField field, RecordValue value, int blockIndent, string path)
        {
            if (!(value is RecordScalar scalar)) throw Shape(path, "a single value");
            sb.Append(' ').Append(Spell(field, scalar.Text, blockIndent, path)).Append(field.Kind == RecordFieldKind.Rich || Block(field, scalar.Text) ? string.Empty : "\n");
        }

        static bool Block(RecordField field, string text) => (field.Kind == RecordFieldKind.Text || field.Kind == RecordFieldKind.Notation) && !RecordProfile.CanQuote(text);

        /// <summary>The canonical spelling of a scalar of <paramref name="field"/> written after <c>key: </c>; a block ends with its last line end.</summary>
        internal static string Spell(RecordField field, string text, int blockIndent, string path)
        {
            switch (field.Kind)
            {
                case RecordFieldKind.Integer:
                {
                    if (!long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long n)) throw new RecordWriteException("The value of " + path + " is not a whole number: " + text);
                    return n.ToString(CultureInfo.InvariantCulture);
                }
                case RecordFieldKind.Boolean:
                    if (text != "true" && text != "false") throw new RecordWriteException("The value of " + path + " is not true or false: " + text);
                    return text;
                case RecordFieldKind.Guid:
                    if (!System.Guid.TryParse(text, out var g)) throw new RecordWriteException("The value of " + path + " is not a GUID: " + text);
                    return RecordProfile.Quote(g.ToString("D"));
                case RecordFieldKind.Reference:
                    if (!RecordProfile.CanQuote(text)) throw new RecordWriteException("The reference at " + path + " holds a control character.");
                    return RecordProfile.Quote(text);
                case RecordFieldKind.Rich:
                {
                    string? problem = RecordProfile.LiteralProblem(text);
                    if (problem != null) throw new RecordWriteException("The rich value at " + path + " cannot be written as a literal block: " + problem + ".");
                    var sb = new StringBuilder();
                    RecordProfile.AppendLiteral(sb, text, blockIndent, field.Paragraphs);
                    return sb.ToString();
                }
                default:
                {
                    if (RecordProfile.CanQuote(text)) return RecordProfile.Quote(text);
                    // Plain text with a control character: one paragraph of inline FieldWorks Markdown, which spells it as a character reference
                    // (report 17, section 3.3, rule 2), in a block.
                    // NormalizeText off, as the reader reads it: the text is kept as it is (U+FFFE included).
                    var options = new MarkdownOptions { WritingSystems = new FixedWritingSystems("x", "x"), NormalizeText = false };
                    string markdown;
                    try { markdown = RecordProfile.ForBlock(MarkdownConverter.ToInlineMarkdown(new[] { new Run { Text = text, Ws = "x" } }, options)); }
                    catch (Exception e) when (e is ArgumentException || e is MarkdownWriteException)
                    {
                        throw new RecordWriteException("The text at " + path + " cannot be written: " + e.Message);
                    }
                    string? problem = RecordProfile.LiteralProblem(markdown);
                    if (problem != null) throw new RecordWriteException("The text at " + path + " cannot be written: " + problem + ".");
                    var sb = new StringBuilder();
                    RecordProfile.AppendLiteral(sb, markdown, blockIndent);
                    return sb.ToString();
                }
            }
        }
    }
}
