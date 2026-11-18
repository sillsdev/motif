using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using SIL.TextEditing.Markdown.Records;

namespace SIL.TextEditing.Markdown.Tests.Records
{
    /// <summary>The schema, builders, a canonical dump and a small applier the record-diff tests share.</summary>
    internal static class RecordDiffSupport
    {
        public static readonly Guid EntryGuid = Guid.Parse("00000000-0000-4000-8000-0000000000e1");

        public static Guid G(int n) => Guid.Parse("00000000-0000-4000-8000-" + n.ToString("D12", CultureInfo.InvariantCulture));

        public static RecordSchema Schema()
        {
            var b = new RecordSchemaBuilder("diff.schema.json");
            b.ObjectType("entry").Text("lexeme").MultiText("citation").MultiRich("note").Rich("comment").Integer("homograph").Boolean("flag")
                .Reference("category").References("seeAlso").References("tags").AsCollection("tags").Texts("variants").Texts("labels").AsCollection("labels")
                .Objects("senses", "sense").Objects("pronunciations", "pron").AsCollection("pronunciations")
                .Object("etymology", "etym", "etym2").Objects("glosses", "gloss").Text("headword").Computed("headword");
            b.ObjectType("sense").MultiText("gloss").MultiRich("definition").References("domains").AsCollection("domains")
                .Objects("examples", "example").Objects("subsenses", "sense");
            b.ObjectType("example").Rich("text").Text("translation");
            b.ObjectType("pron").Text("form");
            b.ObjectType("etym").Text("source");
            b.ObjectType("etym2").Text("language");
            b.ObjectType("gloss", false).Text("text");
            return b.Kind("lexicon/entry", "entry", 2).Build();
        }

        public static RecordScalar S(string text) => new RecordScalar(text);

        public static RecordScalar Ref(Guid target) => new RecordScalar("[x](/y){guid=" + target.ToString("D") + "}") { Target = target };

        public static RecordAlternatives Alts(params (string Ws, string Text)[] items)
        {
            var a = new RecordAlternatives();
            foreach (var i in items) a.Add(i.Ws, S(i.Text));
            return a;
        }

        public static RecordList List(params RecordValue[] items)
        {
            var l = new RecordList();
            foreach (var i in items) l.Add(i);
            return l;
        }

        public static RecordObject Sense(int n, string? gloss = null)
        {
            var s = new RecordObject { Guid = G(n) };
            if (gloss != null) s.Set("gloss", Alts(("en", gloss)));
            return s;
        }

        public static RecordDocument Doc(RecordObject? data = null, string? root = null)
            => new RecordDocument("lexicon/entry") { Root = root, Data = data ?? new RecordObject { Guid = EntryGuid } };

        public static RecordObject Entry() => new RecordObject { Guid = EntryGuid };

        public static RecordChangeSet Diff(RecordDocument b, RecordDocument t) => RecordDiffer.Diff(b, t, Schema());

        // ---------------------------------------------------------------- canonical dump (collections sorted, empties dropped)

        public static string Dump(RecordValue? v, RecordSchema schema)
        {
            var sb = new StringBuilder();
            Dump(v, sb, null);
            return sb.ToString();
        }

        static void Dump(RecordValue? v, StringBuilder sb, bool? unordered)
        {
            switch (v)
            {
                case null: return;
                case RecordScalar s: sb.Append('"').Append(s.Text).Append('"'); return;
                case RecordAlternatives a:
                    sb.Append('{');
                    foreach (var i in a.Items.Where(i => !i.Value.IsEmpty).OrderBy(i => i.Key, StringComparer.Ordinal)) sb.Append(i.Key).Append('=').Append('"').Append(i.Value.Text).Append("\";");
                    sb.Append('}');
                    return;
                case RecordList l:
                {
                    var parts = l.Items.Select(i => { var x = new StringBuilder(); Dump(i, x, null); return x.ToString(); }).ToList();
                    if (unordered == true) parts.Sort(StringComparer.Ordinal);
                    sb.Append('[').Append(string.Join(",", parts)).Append(']');
                    return;
                }
                case RecordObject o:
                    sb.Append('<').Append(o.Guid?.ToString("D")).Append(o.Class != null ? ":" + o.Class : string.Empty);
                    foreach (var f in o.Fields.Where(f => !f.Value.IsEmpty).OrderBy(f => f.Key, StringComparer.Ordinal))
                    {
                        sb.Append(' ').Append(f.Key).Append('=');
                        Dump(f.Value, sb, UnorderedFields.Contains(f.Key));
                    }
                    sb.Append('>');
                    return;
            }
        }

        static readonly HashSet<string> UnorderedFields = new HashSet<string> { "tags", "labels", "pronunciations", "domains" };

        // ---------------------------------------------------------------- the applier

        public static RecordObject Clone(RecordObject o)
        {
            var c = new RecordObject { Guid = o.Guid, Class = o.Class };
            foreach (var f in o.Fields) c.Fields.Add(new KeyValuePair<string, RecordValue>(f.Key, CloneValue(f.Value)));
            return c;
        }

        static RecordValue CloneValue(RecordValue v)
        {
            switch (v)
            {
                case RecordScalar s: return new RecordScalar(s.Text) { Target = s.Target };
                case RecordAlternatives a:
                {
                    var r = new RecordAlternatives();
                    foreach (var i in a.Items) r.Add(i.Key, (RecordScalar)CloneValue(i.Value));
                    return r;
                }
                case RecordList l:
                {
                    var r = new RecordList();
                    foreach (var i in l.Items) r.Add(CloneValue(i));
                    return r;
                }
                default: return Clone((RecordObject)v);
            }
        }

        // ---------------------------------------------------------------- the applier
        // It works from the documented contract of a change (object GUID, field path, kind, Old/New, ItemGuid, Index, After) and the schema, and it checks Old
        // against what is there before it writes: a wrong Old fails here, so `expected` is proved, not copied.

        static string TextOf(RecordValue? v) => v is RecordScalar s && !s.IsEmpty ? s.Text : null!;

        static RecordObject? FindByGuid(RecordObject o, Guid guid)
        {
            if (o.Guid == guid) return o;
            foreach (var f in o.Fields)
            {
                if (f.Value is RecordObject c) { var r = FindByGuid(c, guid); if (r != null) return r; }
                else if (f.Value is RecordList l)
                    foreach (var i in l.Items.OfType<RecordObject>()) { var r = FindByGuid(i, guid); if (r != null) return r; }
            }
            return null;
        }

        static RecordField FieldOf(RecordSchema schema, RecordChange c)
        {
            var type = schema.ObjectType(c.ObjectType!)!;
            string[] path = c.Field.Split('/');
            for (int i = 0; ; i++)
            {
                var f = type.Field(path[i])!;
                if (i == path.Length - 1) return f;
                type = (f.IsList ? f.Item! : f).ObjectTypes[0];
                if (f.IsList) i++; // the index
            }
        }

        static void Expect(string? old, string? actual, RecordChange c)
        {
            if (old != actual) throw new InvalidOperationException("Old does not match the value in the base for " + c + ": base has '" + actual + "'.");
        }

        /// <summary>Applies a change set, in order, to a copy of the base's data and returns the copy.</summary>
        public static RecordObject Apply(RecordObject baseData, RecordChangeSet changes, RecordSchema schema)
        {
            var root = Clone(baseData);
            _added.Clear();
            foreach (var c in changes.Changes)
            {
                var cur = c.ObjectGuid == null ? root : FindByGuid(root, c.ObjectGuid.Value) ?? throw new InvalidOperationException("No object " + c.ObjectGuid + " for " + c);
                string[] path = c.Field.Split('/');
                for (int i = 0; i < path.Length - 1; i++)
                {
                    var next = cur[path[i]];
                    if (next is RecordList l) { cur = (RecordObject)l.Items[int.Parse(path[i + 1], CultureInfo.InvariantCulture) - 1]; i++; }
                    else cur = (RecordObject)next!;
                }
                string field = path[path.Length - 1];
                switch (c.Kind)
                {
                    case RecordChangeKind.Set:
                    case RecordChangeKind.SetRich:
                    case RecordChangeKind.SetAlternative:
                    case RecordChangeKind.Clear:
                    case RecordChangeKind.ClearRich:
                    case RecordChangeKind.ClearAlternative:
                    {
                        bool set = c.Kind == RecordChangeKind.Set || c.Kind == RecordChangeKind.SetRich || c.Kind == RecordChangeKind.SetAlternative;
                        if (c.WritingSystem == null)
                        {
                            Expect(c.Old, TextOf(cur[field]), c);
                            cur[field] = set ? new RecordScalar(c.New!) { Target = c.NewTarget } : null;
                        }
                        else
                        {
                            var alts = cur[field] as RecordAlternatives;
                            Expect(c.Old, TextOf(alts?[c.WritingSystem]), c);
                            if (set) { alts = alts ?? new RecordAlternatives(); alts[c.WritingSystem] = S(c.New!); cur[field] = alts; }
                            else if (alts != null) alts[c.WritingSystem] = null;
                        }
                        break;
                    }
                    default:
                        ApplyListChange(cur, field, c, FieldOf(schema, c));
                        break;
                }
            }
            return root;
        }

        // An object with no GUID added by this change set, by its list and key: a later change names it as `After` by that "#n" key.
        static readonly Dictionary<(RecordObject Owner, string Field, string Key), RecordValue> _added = new Dictionary<(RecordObject, string, string), RecordValue>();

        static bool SameScalar(RecordValue v, string? text, Guid? target) => v is RecordScalar s && s.Text == text && s.Target == target;

        static void ApplyListChange(RecordObject owner, string field, RecordChange c, RecordField schemaField)
        {
            string DumpOf(RecordValue? v) => Dump(v, null!);
            if (schemaField.Kind == RecordFieldKind.Object)
            {
                if (c.Kind == RecordChangeKind.AddObject)
                {
                    if (owner[field] != null) throw new InvalidOperationException("The field is not empty for " + c);
                    owner[field] = Clone(c.Subtree!);
                }
                else
                {
                    if (DumpOf(owner[field]) != DumpOf(c.Subtree)) throw new InvalidOperationException("The object to remove differs from the base for " + c);
                    owner[field] = null;
                }
                return;
            }
            var list = owner[field] as RecordList ?? new RecordList();
            owner[field] = list;
            var items = list.Items;
            int FindObject()
            {
                if (c.ItemGuid != null) return items.FindIndex(i => i is RecordObject o && o.Guid == c.ItemGuid);
                return int.Parse(c.ItemKey!.Substring(1), CultureInfo.InvariantCulture) - 1; // documented: "#n" is the base position
            }
            int FindScalar(string? text, Guid? target) => items.FindIndex(i => SameScalar(i, text, target));
            int Place()
            {
                if (c.Index == null) return items.Count;
                if (c.After == null) return 0;
                int at = items.FindIndex(i => i is RecordObject o ? o.Guid?.ToString("D") == c.After : i is RecordScalar s && (s.Target?.ToString("D") ?? s.Text) == c.After);
                if (at >= 0) return at + 1;
                if (_added.TryGetValue((owner, field, c.After), out var earlier) && items.FindIndex(i => ReferenceEquals(i, earlier)) is int e && e >= 0) return e + 1;
                // In a list of objects that have no GUIDs, "#n" after a pair means: after the item now at position n.
                if (c.After.StartsWith("#", StringComparison.Ordinal) && items.OfType<RecordObject>().Any() && int.TryParse(c.After.Substring(1), out int position)) return Math.Min(position, items.Count);
                // a repeated text: "a#2" is the second "a"
                int hash = c.After.LastIndexOf('#');
                string text = c.After.Substring(0, hash);
                int nth = int.Parse(c.After.Substring(hash + 1), CultureInfo.InvariantCulture);
                int seen = 0;
                for (int i = 0; i < items.Count; i++) if (items[i] is RecordScalar s && s.Text == text && ++seen == nth) return i + 1;
                throw new InvalidOperationException("No predecessor " + c.After + " for " + c);
            }
            int index;
            switch (c.Kind)
            {
                case RecordChangeKind.AddObject:
                {
                    var added = Clone(c.Subtree!);
                    items.Insert(Place(), added);
                    if (c.ItemGuid == null) _added[(owner, field, c.ItemKey!)] = added;
                    break;
                }
                case RecordChangeKind.RemoveObject:
                    index = FindObject();
                    if (index < 0 || index >= items.Count || DumpOf(items[index]) != DumpOf(c.Subtree)) throw new InvalidOperationException("The object to remove differs from the base for " + c);
                    items.RemoveAt(index);
                    break;
                case RecordChangeKind.MoveObject:
                    index = FindObject();
                    var moving = items[index];
                    items.RemoveAt(index);
                    items.Insert(Math.Min(Place(), items.Count), moving);
                    break;
                case RecordChangeKind.AddReference:
                case RecordChangeKind.AddItem:
                    items.Insert(Place(), new RecordScalar(c.New!) { Target = c.NewTarget });
                    break;
                case RecordChangeKind.RemoveReference:
                case RecordChangeKind.RemoveItem:
                    index = FindScalar(c.Old, c.OldTarget);
                    if (index < 0) throw new InvalidOperationException("Old is not in the base for " + c);
                    items.RemoveAt(index);
                    break;
                case RecordChangeKind.MoveReference:
                case RecordChangeKind.MoveItem:
                {
                    index = FindScalar(c.Old, c.OldTarget);
                    if (index < 0) throw new InvalidOperationException("Old is not in the base for " + c);
                    var v = items[index];
                    items.RemoveAt(index);
                    items.Insert(Math.Min(Place(), items.Count), v);
                    break;
                }
            }
        }

        /// <summary>Everything about a change set that matters, as text, for determinism checks.</summary>
        public static string Describe(RecordChangeSet set)
        {
            var sb = new StringBuilder();
            foreach (var c in set.Changes)
                sb.Append(c.Kind).Append('|').Append(c.Slot).Append('|').Append(c.Old).Append('|').Append(c.New).Append('|').Append(c.Index).Append('|').Append(c.After)
                    .Append('|').Append(c.ByOmission).Append('|').Append(c.Words == null ? string.Empty : string.Join("", c.Words.Select(w => w.ToString()))).Append('\n');
            foreach (var r in set.Refusals) sb.Append("refuse ").Append(r.Kind).Append('|').Append(r.ObjectGuid).Append('|').Append(r.Field).Append('|').Append(r.Reason).Append('\n');
            return sb.ToString();
        }
    }
}
