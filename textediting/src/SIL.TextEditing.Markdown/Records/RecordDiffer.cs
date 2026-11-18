using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SIL.TextEditing.Markdown.Records
{
    /// <summary>A pure diff of two record documents under one schema (ADR 0031 Q63): the base (an export) and the target (the edited file). It works by object
    /// GUID and field path, needs no project, and reads both documents without changing them.
    /// <list type="bullet">
    /// <item>A collection (<see cref="RecordFieldKind.Collection"/>) is unordered; reordering it is no change. A sequence reports moves, keeping them few.</item>
    /// <item>A partial target (<c>root:</c> names an owned object or a field) is diffed over the subtree it holds, and omission is never a change: a field,
    /// alternative or list item it leaves out is untouched. A value present but empty clears. A whole-record target reads omission as empty, as a file never
    /// holds an empty value; an object a whole-record target leaves out is reported as removed, flagged <see cref="RecordChange.ByOmission"/>.</item>
    /// <item>Computed fields, a changed <c>guid:</c> or <c>class:</c> and an object under another owner are refused, in <see cref="RecordChangeSet.Refusals"/>.</item>
    /// </list></summary>
    public static class RecordDiffer
    {
        /// <param name="baseDocument">The export.</param>
        /// <param name="target">The edited file.</param>
        /// <param name="schema">The schema both are read under.</param>
        /// <exception cref="ArgumentException">The documents are of another kind than the schema's, the target's root does not walk the schema, or the base does not hold
        /// the subtree a partial target names.</exception>
        public static RecordChangeSet Diff(RecordDocument baseDocument, RecordDocument target, RecordSchema schema)
        {
            if (baseDocument == null) throw new ArgumentNullException(nameof(baseDocument));
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (schema == null) throw new ArgumentNullException(nameof(schema));
            var kind = schema.Kind(target.Kind) ?? throw new ArgumentException("The schema has no record kind " + target.Kind + ".", nameof(schema));
            if (baseDocument.Kind != target.Kind) throw new ArgumentException("The documents are of different kinds: " + baseDocument.Kind + " and " + target.Kind + ".", nameof(target));
            var info = RecordRoot.Resolve(kind, target.Root, out string? error, target.Data.Class)
                ?? throw new ArgumentException("The target's root: " + error, nameof(target));
            bool partial = !(info.IsRecord && info.Field == null);

            RecordObject baseRoot;
            RecordObject baseWhole = baseDocument.Data;
            if (baseDocument.Root == target.Root) baseRoot = baseDocument.Data;
            else
            {
                var baseInfo = baseDocument.Root == null ? null : RecordRoot.Resolve(kind, baseDocument.Root, out _, baseDocument.Data.Class);
                if (baseDocument.Root != null && !(baseInfo != null && baseInfo.IsRecord && baseInfo.Field == null))
                    throw new ArgumentException("The base is a partial file with another root than the target's.", nameof(baseDocument));
                baseRoot = partial ? Locate(kind, schema, baseDocument.Data, target.Root!) : baseDocument.Data;
            }

            var differ = new Differ(partial);
            // Who owns what comes from the whole export; a duplicate GUID is refused only inside the subtree a partial target names.
            differ.IndexOwners(baseWhole, differ.BaseOwners, "export", null, TypeOfRoot(kind, baseDocument.Data, baseDocument.Root == target.Root ? info.Type : null, baseDocument.Root), report: !partial || ReferenceEquals(baseRoot, baseWhole));
            if (partial && !ReferenceEquals(baseRoot, baseWhole))
                differ.IndexOwners(baseRoot, new Dictionary<Guid, string>(), "export", baseRoot.Guid, info.Type, report: true);
            var targetRootType = info.Type;
            var rootGuid = targetRootType.HasGuid ? (baseRoot.Guid ?? target.Data.Guid) : null;
            differ.IndexOwners(target.Data, differ.TargetOwners, "file", rootGuid, targetRootType);
            // The object a partial file names must be unambiguous in the export, wherever else a duplicate is.
            if (rootGuid != null && differ.OutsideDuplicates.Contains(rootGuid.Value)) differ.RefuseDuplicate(rootGuid.Value, "export", string.Empty);

            differ.DiffRoot(baseRoot, target.Data, targetRootType, rootGuid);
            differ.Finish();
            return new RecordChangeSet(differ.Changes.AsReadOnly(), differ.Refusals.AsReadOnly(), partial);
        }

        static RecordObjectType TypeOfRoot(RecordKind kind, RecordObject data, RecordObjectType? known, string? root)
        {
            if (known != null) return known;
            return RecordRoot.Resolve(kind, root, out _, data.Class)?.Type ?? kind.Roots.FirstOrDefault(t => t.Name == data.Class) ?? kind.Root;
        }

        // Walks a partial root (/lexicon/palit(2)/senses/1) down from the whole record's data.
        static RecordObject Locate(RecordKind kind, RecordSchema schema, RecordObject whole, string root)
        {
            var path = ProjectPath.Parse(root)!;
            var current = whole;
            var type = kind.Roots.FirstOrDefault(t => t.Name == whole.Class) ?? kind.Root;
            for (int i = kind.PathSegments; i < path.Segments.Count; i++)
            {
                var field = type.Field(path.Segments[i].Name);
                if (field == null) throw new ArgumentException("The base has no " + path.Segments[i].Name + " to match the target's root.", nameof(root));
                var value = current[field.Name];
                var itemField = field;
                if (field.IsList)
                {
                    i++;
                    itemField = field.Item!;
                    value = value is RecordList list && int.TryParse(path.Segments[i].Name, NumberStyles.None, CultureInfo.InvariantCulture, out int n) && n >= 1 && n <= list.Items.Count
                        ? list.Items[n - 1] : null;
                }
                if (!(value is RecordObject next)) throw new ArgumentException("The base does not hold the object the target's root names: " + root + ".", nameof(root));
                current = next;
                type = Differ.TypeFor(itemField, next) ?? throw new ArgumentException("The base holds an object of an unknown class at " + root + ".", nameof(root));
            }
            return current;
        }

        readonly struct Addr
        {
            public Addr(Guid? guid, string prefix) { Guid = guid; Prefix = prefix; }
            public Guid? Guid { get; }
            /// <summary>The path from the GUID's object through objects with no GUID of their own, ending in a slash, or empty.</summary>
            public string Prefix { get; }
        }

        sealed class Differ
        {
            readonly bool _partial;
            readonly HashSet<Guid> _movedReported = new HashSet<Guid>();
            public readonly List<RecordChange> Changes = new List<RecordChange>();
            public readonly List<RecordRefusal> Refusals = new List<RecordRefusal>();
            public readonly Dictionary<Guid, string> BaseOwners = new Dictionary<Guid, string>();
            public readonly Dictionary<Guid, string> TargetOwners = new Dictionary<Guid, string>();

            public Differ(bool partial) { _partial = partial; }

            public static RecordObjectType? TypeFor(RecordField objectField, RecordObject o)
                => objectField.ObjectTypes.Count == 1 ? objectField.ObjectTypes[0] : objectField.ObjectTypes.FirstOrDefault(t => t.Name == o.Class);

            static string? ClassOf(RecordObject o, RecordField f) => o.Class ?? (f.ObjectTypes.Count == 1 ? f.ObjectTypes[0].Name : null);

            static string OwnerKey(Addr addr, string field) => (addr.Guid?.ToString("D") ?? string.Empty) + "|" + addr.Prefix + field;

            // ------------------------------------------------------------ who owns which object

            public void IndexOwners(RecordObject obj, Dictionary<Guid, string> map, string side, Guid? guid, RecordObjectType type, bool report = true)
            {
                _report = report;
                var rootGuid = guid ?? (type.HasGuid ? obj.Guid : null);
                if (rootGuid != null) map[rootGuid.Value] = "|";

                IndexOwners(obj, map, side, new Addr(rootGuid, string.Empty), type);
            }

            void IndexOwners(RecordObject obj, Dictionary<Guid, string> map, string side, Addr addr, RecordObjectType type)
            {
                foreach (var field in type.Fields)
                {
                    var value = obj[field.Name];
                    if (value == null) continue;
                    var itemField = field.IsList ? field.Item! : field;
                    if (itemField.Kind != RecordFieldKind.Object) continue;
                    var children = value is RecordList list ? list.Items.OfType<RecordObject>().ToList() : value is RecordObject one ? new List<RecordObject> { one } : new List<RecordObject>();
                    for (int i = 0; i < children.Count; i++)
                    {
                        var child = children[i];
                        var childType = TypeFor(itemField, child);
                        if (childType == null) continue;
                        if (child.Guid != null)
                        {
                            if (map.ContainsKey(child.Guid.Value))
                            {
                                if (!_report) OutsideDuplicates.Add(child.Guid.Value);
                                else RefuseDuplicate(child.Guid.Value, side, addr.Prefix + field.Name);
                            }
                            else map[child.Guid.Value] = OwnerKey(addr, field.Name);
                            IndexOwners(child, map, side, new Addr(child.Guid, string.Empty), childType);
                        }
                        else IndexOwners(child, map, side, new Addr(addr.Guid, addr.Prefix + field.Name + "/" + (i + 1).ToString(CultureInfo.InvariantCulture) + "/"), childType);
                    }
                }
            }

            bool _report = true;
            /// <summary>GUIDs found twice in an index that did not report them (the whole export, for a partial target).</summary>
            public readonly HashSet<Guid> OutsideDuplicates = new HashSet<Guid>();

            public void RefuseDuplicate(Guid guid, string side, string field)
            {
                if (Duplicates.Add(guid))
                    Refusals.Add(new RecordRefusal(RecordRefusalKind.DuplicateGuid, "The guid " + guid.ToString("D") + " is on more than one object in the " + side + "; none of them is compared.")
                        { ObjectGuid = guid, Field = field });
            }

            public readonly HashSet<Guid> Duplicates = new HashSet<Guid>();

            /// <summary>Marks every clear or removal of a whole-record target as made by omission.</summary>
            public void Finish()
            {
                if (_partial) return;
                foreach (var c in Changes)
                    switch (c.Kind)
                    {
                        case RecordChangeKind.Clear: case RecordChangeKind.ClearAlternative: case RecordChangeKind.ClearRich:
                        case RecordChangeKind.RemoveObject: case RecordChangeKind.RemoveReference: case RecordChangeKind.RemoveItem:
                            c.ByOmission = true;
                            break;
                    }
            }

            static RecordObject CloneObject(RecordObject o)
            {
                var c = new RecordObject { Guid = o.Guid, Class = o.Class };
                foreach (var f in o.Fields) c.Fields.Add(new KeyValuePair<string, RecordValue>(f.Key, CloneValue(f.Value)));
                return c;
            }

            static RecordValue CloneValue(RecordValue v)
            {
                switch (v)
                {
                    case RecordScalar s: return new RecordScalar(s.Text) { Target = s.Target, Paragraphs = s.Paragraphs };
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
                    default: return CloneObject((RecordObject)v);
                }
            }

            // ------------------------------------------------------------ the tree

            public void DiffRoot(RecordObject b, RecordObject t, RecordObjectType type, Guid? guid)
            {
                var addr = new Addr(guid, string.Empty);
                if (type.HasGuid && b.Guid != null && t.Guid != null && b.Guid != t.Guid)
                {
                    Refuse(RecordRefusalKind.IdentityChanged, addr, string.Empty, null, "The record's guid changed from " + b.Guid.Value.ToString("D") + " to " + t.Guid.Value.ToString("D") + ".",
                        b.Guid.Value.ToString("D"), t.Guid.Value.ToString("D"));
                    return;
                }
                if (b.Class != null && t.Class != null && b.Class != t.Class)
                {
                    Refuse(RecordRefusalKind.ClassChanged, addr, string.Empty, null, "The record's class changed from " + b.Class + " to " + t.Class + ".", b.Class, t.Class);
                    return;
                }
                DiffFields(b, t, type, addr);
            }

            void DiffFields(RecordObject b, RecordObject t, RecordObjectType type, Addr addr)
            {
                foreach (var field in type.Fields)
                {
                    var bv = b[field.Name];
                    var tv = t[field.Name];
                    if (tv == null && _partial) continue;
                    if (field.Computed)
                    {
                        if (!RecordDocument.AreEquivalent(bv, tv))
                            Refuse(RecordRefusalKind.ComputedField, addr, addr.Prefix + field.Name, null, "The field " + type.Name + "." + field.Name + " is computed by the host, so it cannot be edited.", null, null);
                        continue;
                    }
                    if (field.MultiWritingSystem) DiffAlternatives(field, bv as RecordAlternatives, tv as RecordAlternatives, type, addr);
                    else if (field.Kind == RecordFieldKind.Object) DiffObjectField(field, bv as RecordObject, tv as RecordObject, type, addr);
                    else if (field.IsList)
                    {
                        if (field.Item!.Kind == RecordFieldKind.Object) DiffObjectList(field, bv as RecordList, tv as RecordList, type, addr);
                        else DiffItemList(field, bv as RecordList, tv as RecordList, type, addr);
                    }
                    else DiffScalar(field, bv as RecordScalar, tv as RecordScalar, type, addr);
                }
            }

            RecordChange New(RecordChangeKind kind, RecordObjectType type, Addr addr, string field) => new RecordChange(kind)
            {
                ObjectGuid = addr.Guid, ObjectType = type.Name, Field = addr.Prefix + field,
            };

            void Refuse(RecordRefusalKind kind, Addr addr, string field, string? ws, string reason, string? old, string? @new)
                => Refusals.Add(new RecordRefusal(kind, reason) { ObjectGuid = addr.Guid, Field = field, WritingSystem = ws, Old = old, New = @new });

            static string? Text(RecordScalar? s) => s == null || s.IsEmpty ? null : s.Text;

            // ------------------------------------------------------------ scalars and alternatives

            void DiffScalar(RecordField field, RecordScalar? b, RecordScalar? t, RecordObjectType type, Addr addr)
            {
                string? old = Text(b), @new = Text(t);
                if (old == @new) return;
                bool rich = field.Kind == RecordFieldKind.Rich;
                var kind = @new == null ? (rich ? RecordChangeKind.ClearRich : RecordChangeKind.Clear) : rich ? RecordChangeKind.SetRich : RecordChangeKind.Set;
                var change = New(kind, type, addr, field.Name);
                Fill(change, field, b, t, old, @new);
                Changes.Add(change);
            }

            static void Fill(RecordChange change, RecordField field, RecordScalar? b, RecordScalar? t, string? old, string? @new)
            {
                change.Old = old;
                change.New = @new;
                if (field.Kind == RecordFieldKind.Reference)
                {
                    change.OldTarget = old == null ? null : b?.Target;
                    change.NewTarget = @new == null ? null : t?.Target;
                }
                if (field.Kind == RecordFieldKind.Rich && old != null && @new != null) change.Words = WordDiff.Summarize(old, @new);
            }

            void DiffAlternatives(RecordField field, RecordAlternatives? b, RecordAlternatives? t, RecordObjectType type, Addr addr)
            {
                var all = new SortedSet<string>(StringComparer.Ordinal);
                if (b != null) foreach (var i in b.Items) all.Add(i.Key);
                if (t != null) foreach (var i in t.Items) all.Add(i.Key);
                bool rich = field.Kind == RecordFieldKind.Rich;
                foreach (string ws in all)
                {
                    var tAlt = t?[ws];
                    if (tAlt == null && _partial) continue;
                    var bAlt = b?[ws];
                    string? old = Text(bAlt), @new = Text(tAlt);
                    if (old == @new) continue;
                    var kind = @new == null ? (rich ? RecordChangeKind.ClearRich : RecordChangeKind.ClearAlternative) : rich ? RecordChangeKind.SetRich : RecordChangeKind.SetAlternative;
                    var change = New(kind, type, addr, field.Name);
                    change.WritingSystem = ws;
                    Fill(change, field, bAlt, tAlt, old, @new);
                    Changes.Add(change);
                }
            }

            // ------------------------------------------------------------ owned objects

            void DiffObjectField(RecordField field, RecordObject? b, RecordObject? t, RecordObjectType type, Addr addr)
            {
                bool hasB = b != null && !b.IsEmpty, hasT = t != null && !t.IsEmpty;
                if (!hasB && !hasT) return;
                if ((b?.Guid != null && Duplicates.Contains(b.Guid.Value)) || (t?.Guid != null && Duplicates.Contains(t.Guid.Value))) return;
                if (!hasT)
                {
                    EmitRemove(field.Name, field, b!, type, addr, 1, byOmission: !_partial);
                    return;
                }
                if (!hasB)
                {
                    EmitAdd(field.Name, field, t!, type, addr, null, null, 1);
                    return;
                }
                if (b!.Guid != null && t!.Guid != null && b.Guid != t.Guid)
                {
                    Refuse(RecordRefusalKind.IdentityChanged, addr, addr.Prefix + field.Name, null,
                        "The guid of " + field.Name + " changed from " + b.Guid.Value.ToString("D") + " to " + t.Guid.Value.ToString("D") + "; an object's guid is never edited in place.",
                        b.Guid.Value.ToString("D"), t.Guid.Value.ToString("D"));
                    return;
                }
                DiffPair(field, b, t!, addr, field.Name + "/");
            }

            // Both sides hold the object: compare identity, then recurse. nestedPrefix is the path through this object if it has no GUID of its own.
            void DiffPair(RecordField itemField, RecordObject b, RecordObject t, Addr addr, string nestedPrefix)
            {
                string? cb = ClassOf(b, itemField), ct = ClassOf(t, itemField);
                var childType = TypeFor(itemField, b);
                var targetType = TypeFor(itemField, t);
                if (cb != null && ct != null && cb != ct)
                {
                    Refuse(RecordRefusalKind.ClassChanged, ChildAddr(b, t, addr, nestedPrefix), string.Empty, null, "The class of " + (b.Guid?.ToString("D") ?? nestedPrefix) + " changed from " + cb + " to " + ct + ".", cb, ct);
                    return;
                }
                childType = childType ?? targetType;
                if (childType == null)
                {
                    Refuse(RecordRefusalKind.UnknownClass, addr, addr.Prefix + nestedPrefix.TrimEnd('/'), null, "The class " + (b.Class ?? t.Class) + " is not one the field allows.", null, null);
                    return;
                }
                DiffFields(b, t, childType, ChildAddr(b, t, addr, nestedPrefix));
            }

            static Addr ChildAddr(RecordObject b, RecordObject t, Addr addr, string nestedPrefix)
            {
                Guid? guid = b.Guid ?? t.Guid;
                return guid != null ? new Addr(guid, string.Empty) : new Addr(addr.Guid, addr.Prefix + nestedPrefix);
            }

            // Returns the index of the change in Changes, or -1 when none was made (the object is refused as moved to another owner).
            int EmitAdd(string fieldName, RecordField itemField, RecordObject t, RecordObjectType type, Addr addr, int? index, RecordAnchor? anchor, int ordinal)
            {
                var change = New(RecordChangeKind.AddObject, type, addr, fieldName);
                SetObjectItem(change, t, ordinal);
                change.Index = index;
                change.Anchor = anchor;
                if (t.Guid != null && ReportMoved(t.Guid.Value, addr, fieldName)) return -1;
                Changes.Add(change);
                return Changes.Count - 1;
            }

            void EmitRemove(string fieldName, RecordField itemField, RecordObject b, RecordObjectType type, Addr addr, int ordinal, bool byOmission)
            {
                var change = New(RecordChangeKind.RemoveObject, type, addr, fieldName);
                SetObjectItem(change, b, ordinal);
                change.ByOmission = byOmission;
                if (b.Guid == null) change.OldPosition = ordinal;
                if (b.Guid != null && ReportMoved(b.Guid.Value, addr, fieldName)) return;
                Changes.Add(change);
            }

            static void SetObjectItem(RecordChange change, RecordObject o, int ordinal)
            {
                change.ItemGuid = o.Guid;
                change.ItemKey = o.Guid?.ToString("D") ?? "#" + ordinal.ToString(CultureInfo.InvariantCulture);
                change.Subtree = CloneObject(o);
            }

            // True when the object is under another owner or field on the other side: reported once as a refusal, and no add or remove is emitted for it.
            bool ReportMoved(Guid guid, Addr addr, string fieldName)
            {
                if (!BaseOwners.TryGetValue(guid, out string? from) || !TargetOwners.TryGetValue(guid, out string? to) || from == to) return false;
                if (_movedReported.Add(guid))
                    Refuse(RecordRefusalKind.MovedToOtherOwner, addr, addr.Prefix + fieldName, null,
                        "The object " + guid.ToString("D") + " is under " + Describe(to) + " in the file but under " + Describe(from) + " in the export; an owned object is not moved to another owner.",
                        from, to);
                return true;
            }

            static string Describe(string ownerKey)
            {
                int bar = ownerKey.IndexOf('|');
                string owner = bar <= 0 ? "the record" : ownerKey.Substring(0, bar);
                return (bar <= 0 ? owner : "object " + owner) + " field " + ownerKey.Substring(bar + 1);
            }

            void DiffObjectList(RecordField field, RecordList? b, RecordList? t, RecordObjectType type, Addr addr)
            {
                var itemField = field.Item!;
                var bItems = Objects(b);
                var tItems = Objects(t);
                bool ordered = field.Kind == RecordFieldKind.Sequence;
                if (!itemField.ObjectTypes.All(o => o.HasGuid))
                {
                    DiffPositional(field, itemField, bItems, tItems, type, addr, ordered);
                    return;
                }
                var bByGuid = FirstIndexByGuid(bItems);
                var tByGuid = FirstIndexByGuid(tItems);

                // Objects with no GUID on both sides (new objects in the file the export was made from) are matched by position among themselves.
                var unkeyedBase = Enumerable.Range(0, bItems.Count).Where(i => bItems[i].Guid == null).ToList();
                var unkeyedTarget = Enumerable.Range(0, tItems.Count).Where(i => tItems[i].Guid == null).ToList();
                var pairedBase = new Dictionary<int, int>();
                for (int k = 0; k < Math.Min(unkeyedBase.Count, unkeyedTarget.Count); k++) pairedBase[unkeyedTarget[k]] = unkeyedBase[k];
                // Removals of objects with no GUID come first and from the last, so each OldPosition is still the base position.
                if (!_partial)
                    for (int k = unkeyedBase.Count - 1; k >= unkeyedTarget.Count; k--)
                        EmitRemove(field.Name, itemField, bItems[unkeyedBase[k]], type, addr, unkeyedBase[k] + 1, byOmission: true);

                // Removals, in base order (a collection: in key order).
                var removed = Enumerable.Range(0, bItems.Count).Where(i => bItems[i].Guid != null && bByGuid[bItems[i].Guid!.Value] == i && !tByGuid.ContainsKey(bItems[i].Guid!.Value)).ToList();
                if (!ordered) removed = removed.OrderBy(i => bItems[i].Guid!.Value.ToString("D"), StringComparer.Ordinal).ToList();
                foreach (int i in removed)
                {
                    // A partial target does not remove, but an object that moved to another owner is still refused.
                    if (_partial) ReportMoved(bItems[i].Guid!.Value, addr, field.Name);
                    else EmitRemove(field.Name, itemField, bItems[i], type, addr, i + 1, byOmission: true);
                }

                // Target items (the first of a repeated GUID) in target order; a collection in key order, new objects with no GUID last.
                var order = Enumerable.Range(0, tItems.Count).Where(i => tItems[i].Guid == null || tByGuid[tItems[i].Guid!.Value] == i).ToList();
                if (!ordered) order = order.OrderBy(i => tItems[i].Guid == null ? 1 : 0).ThenBy(i => tItems[i].Guid == null ? string.Empty : tItems[i].Guid!.Value.ToString("D"), StringComparer.Ordinal).ThenBy(i => i).ToList();
                var moved = new HashSet<int>();
                if (ordered)
                {
                    var kept = order.Where(i => tItems[i].Guid != null && bByGuid.ContainsKey(tItems[i].Guid!.Value)).ToList();
                    var inLis = Lis(kept.Select(i => bByGuid[tItems[i].Guid!.Value]).ToList());
                    for (int k = 0; k < kept.Count; k++) if (!inLis.Contains(k)) moved.Add(kept[k]);
                }
                // What stands in the list once a target item has been handled: a typed anchor later items are placed after.
                var anchors = new Dictionary<int, RecordAnchor>();
                RecordAnchor? previousAnchor = RecordAnchor.Start;
                for (int p = 0; p < order.Count; p++)
                {
                    int i = order[p];
                    var item = tItems[i];
                    var after = ordered ? previousAnchor : null;
                    if (item.Guid == null && pairedBase.TryGetValue(i, out int pairedIndex))
                    {
                        DiffPair(itemField, bItems[pairedIndex], item, addr, field.Name + "/" + (pairedIndex + 1).ToString(CultureInfo.InvariantCulture) + "/");
                        previousAnchor = RecordAnchor.AtPosition(pairedIndex + 1);
                        continue;
                    }
                    if (item.Guid == null || !bByGuid.ContainsKey(item.Guid.Value))
                    {
                        var at = TypeFor(itemField, item);
                        if (at == null) { Refuse(RecordRefusalKind.UnknownClass, addr, addr.Prefix + field.Name, null, "The class " + item.Class + " is not one the field allows.", null, null); continue; }
                        int made = EmitAdd(field.Name, itemField, item, type, addr, ordered ? p + 1 : (int?)null, after, i + 1);
                        if (made >= 0) previousAnchor = item.Guid != null ? RecordAnchor.OfObject(item.Guid.Value) : RecordAnchor.AddedBy(made);
                        continue;
                    }
                    var b0 = bItems[bByGuid[item.Guid.Value]];
                    if (moved.Contains(i))
                    {
                        var move = New(RecordChangeKind.MoveObject, type, addr, field.Name);
                        move.ItemGuid = item.Guid;
                        move.ItemKey = KeyOf(item, i + 1);
                        move.Index = p + 1;
                        move.Anchor = after;
                        Changes.Add(move);
                    }
                    DiffPair(itemField, b0, item, addr, field.Name + "/");
                    previousAnchor = RecordAnchor.OfObject(item.Guid.Value);
                }
            }

            static Dictionary<Guid, int> FirstIndexByGuid(List<RecordObject> items)
            {
                var map = new Dictionary<Guid, int>();
                for (int i = 0; i < items.Count; i++) if (items[i].Guid != null && !map.ContainsKey(items[i].Guid!.Value)) map[items[i].Guid!.Value] = i;
                return map;
            }

            static string KeyOf(RecordObject o, int ordinal) => o.Guid?.ToString("D") ?? "#" + ordinal.ToString(CultureInfo.InvariantCulture);

            // The objects of a list, leaving out any whose GUID is on more than one object (those were refused).
            List<RecordObject> Objects(RecordList? list)
                => list == null ? new List<RecordObject>() : list.Items.OfType<RecordObject>().Where(o => o.Guid == null || !Duplicates.Contains(o.Guid.Value)).ToList();

            // Item types without GUIDs: items are matched by position.
            void DiffPositional(RecordField field, RecordField itemField, List<RecordObject> b, List<RecordObject> t, RecordObjectType type, Addr addr, bool ordered)
            {
                int common = Math.Min(b.Count, t.Count);
                for (int i = 0; i < common; i++)
                {
                    DiffPair(itemField, b[i], t[i], addr, field.Name + "/" + (i + 1).ToString(CultureInfo.InvariantCulture) + "/");
                }
                for (int i = common; i < t.Count; i++)
                {
                    var at = TypeFor(itemField, t[i]);
                    if (at == null) { Refuse(RecordRefusalKind.UnknownClass, addr, addr.Prefix + field.Name, null, "The class " + t[i].Class + " is not one the field allows.", null, null); continue; }
                    EmitAdd(field.Name, itemField, t[i], type, addr, ordered ? i + 1 : (int?)null, ordered ? (i == 0 ? RecordAnchor.Start : RecordAnchor.AtPosition(i)) : null, i + 1);
                }
                // Removed from the end, so each "#n" key (n being the position in the base) still names the right item when it is applied.
                if (!_partial)
                    for (int i = b.Count - 1; i >= common; i--)
                    {
                        EmitRemove(field.Name, itemField, b[i], type, addr, i + 1, byOmission: true);
                    }
            }

            // ------------------------------------------------------------ lists of scalars (references, texts)

            void DiffItemList(RecordField field, RecordList? b, RecordList? t, RecordObjectType type, Addr addr)
            {
                var itemField = field.Item!;
                bool ordered = field.Kind == RecordFieldKind.Sequence;
                var bItems = Keyed(itemField, b);
                var tItems = Keyed(itemField, t);
                var bKeys = new Dictionary<string, int>(StringComparer.Ordinal);
                for (int i = 0; i < bItems.Count; i++) bKeys[bItems[i].Key] = i;
                var tKeys = new Dictionary<string, int>(StringComparer.Ordinal);
                for (int i = 0; i < tItems.Count; i++) tKeys[tItems[i].Key] = i;
                bool reference = itemField.Kind == RecordFieldKind.Reference;
                var addKind = reference ? RecordChangeKind.AddReference : RecordChangeKind.AddItem;
                var removeKind = reference ? RecordChangeKind.RemoveReference : RecordChangeKind.RemoveItem;
                var moveKind = reference ? RecordChangeKind.MoveReference : RecordChangeKind.MoveItem;

                if (!_partial)
                {
                    var removed = bItems.Where(i => !tKeys.ContainsKey(i.Key));
                    if (!ordered) removed = removed.OrderBy(i => i.Key, StringComparer.Ordinal);
                    foreach (var item in removed)
                    {
                        var change = New(removeKind, type, addr, field.Name);
                        change.ItemKey = item.Key;
                        change.Old = item.Scalar.Text;
                        change.OldTarget = item.Scalar.Target;
                        Changes.Add(change);
                    }
                }
                var moved = new HashSet<string>(StringComparer.Ordinal);
                if (ordered)
                {
                    var kept = tItems.Where(i => bKeys.ContainsKey(i.Key)).ToList();
                    var inLis = Lis(kept.Select(i => bKeys[i.Key]).ToList());
                    for (int k = 0; k < kept.Count; k++) if (!inLis.Contains(k)) moved.Add(kept[k].Key);
                }
                var order = tItems.ToList();
                if (!ordered) order = order.OrderBy(i => i.Key, StringComparer.Ordinal).ToList();
                for (int p = 0; p < tItems.Count; p++)
                {
                    var item = order[p];
                    bool isNew = !bKeys.ContainsKey(item.Key);
                    if (!isNew && !moved.Contains(item.Key)) continue;
                    var change = New(isNew ? addKind : moveKind, type, addr, field.Name);
                    change.ItemKey = item.Key;
                    change.New = item.Scalar.Text;
                    change.NewTarget = item.Scalar.Target;
                    if (!isNew) { change.Old = bItems[bKeys[item.Key]].Scalar.Text; change.OldTarget = bItems[bKeys[item.Key]].Scalar.Target; }
                    if (ordered)
                    {
                        change.Index = p + 1;
                        change.Anchor = p == 0 ? RecordAnchor.Start : order[p - 1].Anchor;
                    }
                    Changes.Add(change);
                }
            }

            sealed class KeyedItem
            {
                public string Key = string.Empty;
                public RecordScalar Scalar = null!;
                public RecordAnchor Anchor = null!;
            }

            static List<KeyedItem> Keyed(RecordField itemField, RecordList? list)
            {
                var result = new List<KeyedItem>();
                if (list == null) return result;
                var seen = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (var s in list.Items.OfType<RecordScalar>())
                {
                    if (s.IsEmpty) continue;
                    string key = itemField.Kind == RecordFieldKind.Reference && s.Target != null ? s.Target.Value.ToString("D") : s.Text;
                    seen.TryGetValue(key, out int count);
                    seen[key] = count + 1;
                    result.Add(new KeyedItem
                    {
                        Key = count == 0 ? key : key + "#" + (count + 1).ToString(CultureInfo.InvariantCulture), Scalar = s,
                        Anchor = RecordAnchor.OfItem(s.Text, s.Target, count + 1),
                    });
                }
                return result;
            }

            /// <summary>The positions (in the given sequence) of a longest increasing subsequence; ties are broken the same way every time.</summary>
            static HashSet<int> Lis(List<int> values)
            {
                var tails = new List<int>();            // index in values of the smallest tail of an increasing subsequence of each length
                var previous = new int[values.Count];
                for (int i = 0; i < values.Count; i++)
                {
                    int lo = 0, hi = tails.Count;
                    while (lo < hi)
                    {
                        int mid = (lo + hi) / 2;
                        if (values[tails[mid]] < values[i]) lo = mid + 1; else hi = mid;
                    }
                    previous[i] = lo == 0 ? -1 : tails[lo - 1];
                    if (lo == tails.Count) tails.Add(i); else tails[lo] = i;
                }
                var result = new HashSet<int>();
                for (int at = tails.Count == 0 ? -1 : tails[tails.Count - 1]; at >= 0; at = previous[at]) result.Add(at);
                return result;
            }
        }

        /// <summary>The word-level summary of a rich value, for display.</summary>
        static class WordDiff
        {
            const long MaxCells = 1_000_000;

            public static IReadOnlyList<RecordWordSpan> Summarize(string old, string @new)
            {
                var a = Tokens(old);
                var b = Tokens(@new);
                var spans = new List<RecordWordSpan>();
                if ((long)a.Count * b.Count > MaxCells)
                {
                    spans.Add(new RecordWordSpan(RecordWordSpanKind.Removed, old));
                    spans.Add(new RecordWordSpan(RecordWordSpanKind.Added, @new));
                    return spans.AsReadOnly();
                }
                var lcs = new int[a.Count + 1, b.Count + 1];
                for (int i = a.Count - 1; i >= 0; i--)
                    for (int j = b.Count - 1; j >= 0; j--)
                        lcs[i, j] = a[i] == b[j] ? lcs[i + 1, j + 1] + 1 : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
                int x = 0, y = 0;
                while (x < a.Count || y < b.Count)
                {
                    if (x < a.Count && y < b.Count && a[x] == b[y]) { Append(spans, RecordWordSpanKind.Unchanged, a[x]); x++; y++; }
                    else if (x < a.Count && (y == b.Count || lcs[x + 1, y] >= lcs[x, y + 1])) { Append(spans, RecordWordSpanKind.Removed, a[x]); x++; }
                    else { Append(spans, RecordWordSpanKind.Added, b[y]); y++; }
                }
                return spans.AsReadOnly();
            }

            static void Append(List<RecordWordSpan> spans, RecordWordSpanKind kind, string text)
            {
                if (spans.Count > 0 && spans[spans.Count - 1].Kind == kind) spans[spans.Count - 1] = new RecordWordSpan(kind, spans[spans.Count - 1].Text + text);
                else spans.Add(new RecordWordSpan(kind, text));
            }

            // Runs of white space and runs of anything else, so the tokens join back into the text.
            static List<string> Tokens(string text)
            {
                var tokens = new List<string>();
                int start = 0;
                for (int i = 1; i <= text.Length; i++)
                    if (i == text.Length || char.IsWhiteSpace(text[i]) != char.IsWhiteSpace(text[i - 1]))
                    {
                        tokens.Add(text.Substring(start, i - start));
                        start = i;
                    }
                return tokens;
            }
        }
    }
}
