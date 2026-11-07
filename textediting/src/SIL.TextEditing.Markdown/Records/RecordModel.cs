using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SIL.TextEditing.Markdown.Records
{
    /// <summary>A value in a record: a <see cref="RecordScalar"/>, the alternatives of a per-writing-system field (<see cref="RecordAlternatives"/>), a
    /// sequence (<see cref="RecordList"/>) or an owned object (<see cref="RecordObject"/>). The tree is generic: the schema says what each value means.</summary>
    public abstract class RecordValue
    {
        internal RecordValue() { }

        /// <summary>The value's field path from the file's data root, set by the reader: field names, 1-based sequence indices and a writing system after a
        /// dot, joined by <c>/</c> (<c>senses/1/definition.en</c>). Empty for the data root and for a value built in code.</summary>
        public string Path { get; internal set; } = string.Empty;

        /// <summary>The [start, end) of the value in the file it was read from, or null.</summary>
        public int[]? Range { get; internal set; }

        /// <summary>True when the value holds nothing, so the writer leaves it out (an empty string, list, object or set of alternatives).</summary>
        public abstract bool IsEmpty { get; }
    }

    /// <summary>One value: text, a whole number, a Boolean, a GUID, a reference or FieldWorks Markdown, always held as the text the file spells it with
    /// (<see cref="Text"/>). A rich value read from a file also has its <see cref="Paragraphs"/>.</summary>
    public sealed class RecordScalar : RecordValue
    {
        public RecordScalar(string text)
        {
            Text = text ?? throw new ArgumentNullException(nameof(text));
        }

        /// <summary>The value as text: the string; an integer's digits; <c>true</c> or <c>false</c>; a GUID lowercase with hyphens; a reference's link; a
        /// rich value's FieldWorks Markdown, its paragraphs one per line with a blank line between them and no line end after the last.</summary>
        public string Text { get; }

        public override bool IsEmpty => Text.Length == 0;

        /// <summary>For a rich value read from a file: what the FieldWorks Markdown reader made of it (one paragraph for inline text). Null otherwise.</summary>
        public IReadOnlyList<Paragraph>? Paragraphs { get; internal set; }

        /// <summary>For a reference read from a file: the GUID of its link, when it has one.</summary>
        public Guid? Target { get; internal set; }

        public static RecordScalar Of(long value) => new RecordScalar(value.ToString(CultureInfo.InvariantCulture));
        public static RecordScalar Of(bool value) => new RecordScalar(value ? "true" : "false");
        public static RecordScalar Of(Guid value) => new RecordScalar(value.ToString("D"));

        /// <summary>A rich value of paragraphs, as canonical FieldWorks Markdown with no front matter, with the few characters a YAML block cannot hold
        /// (U+2028, U+2029 and U+FEFF in a quoted attribute value) written as character references. Unmarked text means <paramref name="options"/>' default
        /// analysis, so a caller writing an alternative of a per-writing-system field sets that to the alternative's.</summary>
        public static RecordScalar Rich(IEnumerable<Paragraph> paragraphs, MarkdownOptions options)
        {
            if (paragraphs == null) throw new ArgumentNullException(nameof(paragraphs));
            if (options == null) throw new ArgumentNullException(nameof(options));
            bool front = options.WriteFrontMatter;
            options.WriteFrontMatter = false;
            try { return new RecordScalar(RecordProfile.ForBlock(MarkdownConverter.ToMarkdown(paragraphs, options).TrimEnd('\n'))); }
            finally { options.WriteFrontMatter = front; }
        }

        /// <summary>A rich value of one paragraph's runs, as canonical inline FieldWorks Markdown, spelled for a YAML block as <see cref="Rich"/> is.</summary>
        public static RecordScalar RichInline(IEnumerable<Run> runs, MarkdownOptions options)
            => new RecordScalar(RecordProfile.ForBlock(MarkdownConverter.ToInlineMarkdown(runs ?? throw new ArgumentNullException(nameof(runs)), options ?? throw new ArgumentNullException(nameof(options)))));

        /// <summary>The value as a whole number, or null when it is not one.</summary>
        public long? AsInteger() => long.TryParse(Text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long v) ? v : (long?)null;

        /// <summary>The value as a Boolean, or null when it is neither <c>true</c> nor <c>false</c>.</summary>
        public bool? AsBoolean() => Text == "true" ? true : Text == "false" ? false : (bool?)null;

        /// <summary>The value as a GUID, or null.</summary>
        public Guid? AsGuid() => System.Guid.TryParse(Text, out var g) ? g : (Guid?)null;

        public override string ToString() => Text;
    }

    /// <summary>The alternatives of a per-writing-system field, in order: writing-system id, value.</summary>
    public sealed class RecordAlternatives : RecordValue
    {
        public List<KeyValuePair<string, RecordScalar>> Items { get; } = new List<KeyValuePair<string, RecordScalar>>();

        public override bool IsEmpty => Items.All(i => i.Value.IsEmpty);

        /// <summary>The alternative of a writing system, or null.</summary>
        public RecordScalar? this[string ws]
        {
            get
            {
                foreach (var item in Items) if (item.Key == ws) return item.Value;
                return null;
            }
            set
            {
                int at = Items.FindIndex(i => i.Key == ws);
                if (value == null) { if (at >= 0) Items.RemoveAt(at); }
                else if (at >= 0) Items[at] = new KeyValuePair<string, RecordScalar>(ws, value);
                else Items.Add(new KeyValuePair<string, RecordScalar>(ws, value));
            }
        }

        public RecordAlternatives Add(string ws, RecordScalar value)
        {
            this[ws] = value;
            return this;
        }
    }

    /// <summary>A sequence, in stored order.</summary>
    public sealed class RecordList : RecordValue
    {
        public List<RecordValue> Items { get; } = new List<RecordValue>();

        public override bool IsEmpty => Items.Count == 0;

        public RecordList Add(RecordValue item)
        {
            Items.Add(item ?? throw new ArgumentNullException(nameof(item)));
            return this;
        }
    }

    /// <summary>An object: its identity (<see cref="Guid"/>; none means a new object) and its fields, in order. The writer writes them in the schema's order
    /// whatever the order here.</summary>
    public sealed class RecordObject : RecordValue
    {
        public Guid? Guid { get; set; }

        public List<KeyValuePair<string, RecordValue>> Fields { get; } = new List<KeyValuePair<string, RecordValue>>();

        public override bool IsEmpty => Guid == null && Fields.All(f => f.Value.IsEmpty);

        /// <summary>The value of a field, or null. Setting null removes the field.</summary>
        public RecordValue? this[string field]
        {
            get
            {
                foreach (var item in Fields) if (item.Key == field) return item.Value;
                return null;
            }
            set
            {
                int at = Fields.FindIndex(i => i.Key == field);
                if (value == null) { if (at >= 0) Fields.RemoveAt(at); }
                else if (at >= 0) Fields[at] = new KeyValuePair<string, RecordValue>(field, value);
                else Fields.Add(new KeyValuePair<string, RecordValue>(field, value));
            }
        }

        public RecordObject Set(string field, RecordValue value)
        {
            this[field] = value;
            return this;
        }
    }

    /// <summary>One record file as data: the front matter's keys and the record's values (<see cref="Data"/>), whether the file spells a value in the frame
    /// or as the body. A partial file's data is the object its <see cref="Root"/> names, or for a root that names a field, an object with that field only.</summary>
    public sealed class RecordDocument
    {
        public RecordDocument(string kind)
        {
            Kind = kind ?? throw new ArgumentNullException(nameof(kind));
        }

        /// <summary>The <c>record:</c> key: the record kind (<c>lexicon/entry</c>).</summary>
        public string Kind { get; set; }

        /// <summary>The <c>schema:</c> key as read (the writer writes its schema's id).</summary>
        public string? Schema { get; set; }

        /// <summary>The <c>root:</c> key: the project path of what the file holds, or null. For a whole record, the record's path; for a partial file, an
        /// owned object's path or a field (<c>/lexicon/palit(2)/senses/1#definition.en</c>).</summary>
        public string? Root { get; set; }

        /// <summary>The <c>base:</c> key: the export the file was made from (a digest), or null.</summary>
        public string? Base { get; set; }

        /// <summary>The <c>analysis:</c> key: the writing system unmarked text means, or null.</summary>
        public string? Analysis { get; set; }

        /// <summary>The <c>vernacular:</c> key: the writing system <c>.v</c> means, or null.</summary>
        public string? Vernacular { get; set; }

        /// <summary>The <c>writingSystems:</c> table, in order.</summary>
        public List<KeyValuePair<string, WritingSystemInfo>> WritingSystems { get; } = new List<KeyValuePair<string, WritingSystemInfo>>();

        /// <summary>The record's values: <c>record-data:</c> and the body.</summary>
        public RecordObject Data { get; set; } = new RecordObject();

        /// <summary>The value at a field path (<see cref="RecordValue.Path"/>), or null.</summary>
        public RecordValue? Find(string path)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));
            if (path.Length == 0) return Data;
            RecordValue? current = Data;
            foreach (string segment in path.Split('/'))
            {
                switch (current)
                {
                    case RecordObject o:
                    {
                        int dot = segment.IndexOf('.');
                        string name = dot < 0 ? segment : segment.Substring(0, dot);
                        current = o[name];
                        if (dot >= 0) current = current is RecordAlternatives alts ? alts[segment.Substring(dot + 1)] : null;
                        break;
                    }
                    case RecordList list:
                        current = int.TryParse(segment, NumberStyles.None, CultureInfo.InvariantCulture, out int index) && index >= 1 && index <= list.Items.Count ? list.Items[index - 1] : null;
                        break;
                    default:
                        return null;
                }
                if (current == null) return null;
            }
            return current;
        }

        /// <summary>Every object in the data that has a GUID, with its field path, in document order.</summary>
        public IEnumerable<KeyValuePair<string, RecordObject>> Objects()
        {
            var found = new List<KeyValuePair<string, RecordObject>>();
            Collect(Data, string.Empty, found);
            return found;
        }

        static void Collect(RecordValue value, string path, List<KeyValuePair<string, RecordObject>> found)
        {
            switch (value)
            {
                case RecordObject o:
                    if (o.Guid != null) found.Add(new KeyValuePair<string, RecordObject>(path, o));
                    foreach (var f in o.Fields) Collect(f.Value, path.Length == 0 ? f.Key : path + "/" + f.Key, found);
                    break;
                case RecordList list:
                    for (int i = 0; i < list.Items.Count; i++) Collect(list.Items[i], path + "/" + (i + 1).ToString(CultureInfo.InvariantCulture), found);
                    break;
            }
        }

        /// <summary>True when two documents say the same: the same front matter and the same values, comparing objects field by field whatever their order,
        /// and leaving out empty values (which a file never holds). Ranges, paths and parsed paragraphs are not compared.</summary>
        public static bool AreEquivalent(RecordDocument a, RecordDocument b)
        {
            if (a == null) throw new ArgumentNullException(nameof(a));
            if (b == null) throw new ArgumentNullException(nameof(b));
            if (a.Kind != b.Kind || a.Root != b.Root || a.Base != b.Base || a.Analysis != b.Analysis || a.Vernacular != b.Vernacular) return false;
            if (a.WritingSystems.Count != b.WritingSystems.Count) return false;
            for (int i = 0; i < a.WritingSystems.Count; i++)
            {
                var x = a.WritingSystems[i];
                var y = b.WritingSystems[i];
                if (x.Key != y.Key || x.Value.Language != y.Value.Language || x.Value.Direction != y.Value.Direction || x.Value.Font != y.Value.Font) return false;
            }
            return AreEquivalent(a.Data, b.Data);
        }

        /// <summary>True when two values say the same (see <see cref="AreEquivalent(RecordDocument, RecordDocument)"/>).</summary>
        public static bool AreEquivalent(RecordValue? a, RecordValue? b)
        {
            bool emptyA = a == null || a.IsEmpty, emptyB = b == null || b.IsEmpty;
            if (emptyA || emptyB) return emptyA && emptyB;
            switch (a)
            {
                case RecordScalar s: return b is RecordScalar t && s.Text == t.Text;
                case RecordAlternatives alts:
                {
                    if (!(b is RecordAlternatives other)) return false;
                    var x = alts.Items.Where(i => !i.Value.IsEmpty).ToList();
                    var y = other.Items.Where(i => !i.Value.IsEmpty).ToList();
                    return x.Count == y.Count && x.All(i => other[i.Key] is RecordScalar v && v.Text == i.Value.Text);
                }
                case RecordList list:
                {
                    if (!(b is RecordList other) || list.Items.Count != other.Items.Count) return false;
                    for (int i = 0; i < list.Items.Count; i++) if (!AreEquivalentItem(list.Items[i], other.Items[i])) return false;
                    return true;
                }
                case RecordObject o:
                {
                    if (!(b is RecordObject other) || o.Guid != other.Guid) return false;
                    var x = o.Fields.Where(f => !f.Value.IsEmpty).ToList();
                    var y = other.Fields.Where(f => !f.Value.IsEmpty).ToList();
                    return x.Count == y.Count && x.All(f => AreEquivalent(f.Value, other[f.Key]));
                }
            }
            return false;
        }

        // An item of a list is kept even when it is empty in itself (a new object with no fields cannot be written, but it is not "nothing").
        static bool AreEquivalentItem(RecordValue a, RecordValue b)
            => a is RecordObject x && b is RecordObject y && x.IsEmpty && y.IsEmpty ? true : AreEquivalent(a, b);
    }
}
