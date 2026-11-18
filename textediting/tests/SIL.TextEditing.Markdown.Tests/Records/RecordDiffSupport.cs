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

        static string KeyOf(RecordValue item, int ordinal, List<RecordValue> list)
        {
            if (item is RecordObject o) return o.Guid?.ToString("D") ?? "#" + ordinal.ToString(CultureInfo.InvariantCulture);
            var s = (RecordScalar)item;
            string key = s.Target?.ToString("D") ?? s.Text;
            int seen = list.Take(ordinal - 1).Count(x => x is RecordScalar y && (y.Target?.ToString("D") ?? y.Text) == key);
            return seen == 0 ? key : key + "#" + (seen + 1).ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>Applies a change set, in order, to a copy of the base's data and returns the copy.</summary>
        public static RecordObject Apply(RecordObject baseData, RecordChangeSet changes)
        {
            var root = Clone(baseData);
            var byGuid = new Dictionary<Guid, RecordObject>();
            Index(root, byGuid);
            foreach (var c in changes.Changes)
            {
                var owner = c.ObjectGuid == null ? root : byGuid[c.ObjectGuid.Value];
                string[] path = c.Field.Split('/');
                var cur = owner;
                for (int i = 0; i < path.Length - 1; i++)
                {
                    var next = cur[path[i]];
                    if (next is RecordList l && i + 1 < path.Length - 1) { cur = (RecordObject)l.Items[int.Parse(path[i + 1], CultureInfo.InvariantCulture) - 1]; i++; }
                    else cur = (RecordObject)next!;
                }
                string field = path[path.Length - 1];
                switch (c.Kind)
                {
                    case RecordChangeKind.Set:
                    case RecordChangeKind.SetRich:
                    case RecordChangeKind.SetAlternative:
                        if (c.WritingSystem == null) cur[field] = new RecordScalar(c.New!) { Target = c.NewTarget };
                        else
                        {
                            var alts = cur[field] as RecordAlternatives ?? new RecordAlternatives();
                            alts[c.WritingSystem] = S(c.New!);
                            cur[field] = alts;
                        }
                        break;
                    case RecordChangeKind.Clear:
                    case RecordChangeKind.ClearRich:
                    case RecordChangeKind.ClearAlternative:
                        if (c.WritingSystem == null) cur[field] = null;
                        else if (cur[field] is RecordAlternatives a) a[c.WritingSystem] = null;
                        break;
                    default:
                        ApplyListChange(cur, field, c, byGuid);
                        break;
                }
            }
            return root;
        }

        static void Index(RecordObject o, Dictionary<Guid, RecordObject> map)
        {
            if (o.Guid != null) map[o.Guid.Value] = o;
            foreach (var f in o.Fields)
            {
                if (f.Value is RecordObject c) Index(c, map);
                else if (f.Value is RecordList l) foreach (var i in l.Items.OfType<RecordObject>()) Index(i, map);
            }
        }

        static void ApplyListChange(RecordObject owner, string field, RecordChange c, Dictionary<Guid, RecordObject> byGuid)
        {
            if (c.Kind == RecordChangeKind.AddObject && IsSingleField(c))
            {
                var added = Clone(c.Subtree!);
                owner[field] = added;
                Index(added, byGuid);
                return;
            }
            if (c.Kind == RecordChangeKind.RemoveObject && owner[field] is RecordObject)
            {
                owner[field] = null;
                return;
            }
            var list = owner[field] as RecordList ?? new RecordList();
            owner[field] = list;
            var items = list.Items;
            int Find(string key)
            {
                if (items.Count > 0 && items[0] is RecordObject first && first.Guid == null && key.StartsWith("#", StringComparison.Ordinal)) return int.Parse(key.Substring(1), CultureInfo.InvariantCulture) - 1;
                for (int i = 0; i < items.Count; i++) if (KeyOf(items[i], i + 1, items) == key) return i;
                return -1;
            }
            void Insert(RecordValue v)
            {
                int at;
                if (c.Index == null) at = items.Count;
                else if (c.After == null) at = 0;
                else if (c.After.StartsWith("#", StringComparison.Ordinal)) at = Math.Min(c.Index.Value - 1, items.Count);
                else at = Find(c.After) + 1;
                items.Insert(at, v);
            }
            switch (c.Kind)
            {
                case RecordChangeKind.AddObject:
                {
                    var o = Clone(c.Subtree!);
                    Insert(o);
                    Index(o, byGuid);
                    break;
                }
                case RecordChangeKind.RemoveObject:
                case RecordChangeKind.RemoveReference:
                case RecordChangeKind.RemoveItem:
                    items.RemoveAt(Find(c.ItemKey!));
                    break;
                case RecordChangeKind.AddReference:
                case RecordChangeKind.AddItem:
                    Insert(new RecordScalar(c.New!) { Target = c.NewTarget });
                    break;
                case RecordChangeKind.MoveObject:
                case RecordChangeKind.MoveReference:
                case RecordChangeKind.MoveItem:
                {
                    int from = Find(c.ItemKey!);
                    var v = items[from];
                    items.RemoveAt(from);
                    Insert(v);
                    break;
                }
            }
        }

        // An object field that holds one object, not a list: the only such field in the test schema.
        static bool IsSingleField(RecordChange c) => c.Field.EndsWith("etymology", StringComparison.Ordinal);

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
