using System;
using System.Collections.Generic;
using System.Linq;
using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using SIL.TextEditing.Markdown;
using SIL.TextEditing.Markdown.Records;

namespace SIL.TextEditing.Project
{
    /// <summary>Whether the file's <c>base:</c> matches a fresh export of its record.</summary>
    public enum ProjectRecordBaseVerdict
    {
        /// <summary>The base was not compared: the file did not read as a record of the project, or the record could not be found or exported.</summary>
        NotChecked,
        /// <summary>The base equals the fresh export's, so the project holds what the file was made from.</summary>
        Fresh,
        /// <summary>The base differs from the fresh export's: the project changed after the file was made. The file is refused whole (<see cref="RecordRefusalKind.StaleBase"/>).</summary>
        Stale,
        /// <summary>The file has no <c>base:</c> (<see cref="RecordRefusalKind.MissingBase"/>).</summary>
        Missing,
    }

    /// <summary>What <see cref="ProjectRecordImporter.Plan(string, IWritableFields?)"/> made of a record file: the changes it would make and what it refuses, and whether the file was made from the
    /// project as it is. It changes nothing in the project.</summary>
    public sealed class ProjectRecordPlan
    {
        internal ProjectRecordPlan(RecordChangeSet changeSet, ProjectRecordBaseVerdict verdict, string? kind, Guid? recordGuid, ProjectRecordExport? export, IReadOnlyList<MarkdownDiagnostic> diagnostics)
        {
            ChangeSet = changeSet;
            BaseVerdict = verdict;
            Kind = kind;
            RecordGuid = recordGuid;
            Export = export;
            Diagnostics = diagnostics;
        }

        /// <summary>The changes to make, in the order to apply them (<see cref="RecordChange.Anchor"/>), and every refusal: the differ's and the project's. A change a refusal
        /// is about is not in <see cref="RecordChangeSet.Changes"/>. A refusal that applies to the whole file (a stale base, say) leaves no change at all.</summary>
        public RecordChangeSet ChangeSet { get; }

        /// <summary>Whether the file's <c>base:</c> matches a fresh export. It only reports the comparison and never decides by itself: whether the file may be used is
        /// <see cref="RecordChangeSet.Refusals"/> (<see cref="RecordChangeSet.IsRefusedWhole"/>).</summary>
        public ProjectRecordBaseVerdict BaseVerdict { get; }

        /// <summary>The record kind the file names, or null.</summary>
        public string? Kind { get; }

        /// <summary>The GUID of the project's record object the file is about (for a partial file, the record that holds the object it names), or null when it could not be found.</summary>
        public Guid? RecordGuid { get; }

        /// <summary>The fresh export the file was compared with, or null when there was none. For diagnostics and display; a lowerer needs only <see cref="ChangeSet"/>.</summary>
        public ProjectRecordExport? Export { get; }

        /// <summary>Every finding of the record-file reader, whatever its severity.</summary>
        public IReadOnlyList<MarkdownDiagnostic> Diagnostics { get; }

        /// <summary>True when there is nothing to change and nothing refused.</summary>
        public bool IsEmpty => ChangeSet.IsEmpty;
    }

    /// <summary>Plans the import of an edited record file, read-only (ADR 0031 Q63, R1d). It exports the file's record again, refuses the file whole when the file's
    /// <c>base:</c> is not that export's (the project changed after the file was made; there is no merge), diffs the file against the fresh export with
    /// <see cref="RecordDiffer"/>, and checks each change against the project: references, writing systems, styles, values the export could not hold, deletion by omission, and
    /// what the host can write. Anything it refuses is a typed <see cref="RecordRefusal"/>. It writes nothing: lowering a plan to project operations is the host's.
    /// The caller owns the cache and serializes access to it.</summary>
    public sealed class ProjectRecordImporter
    {
        readonly ProjectRecordExporter _exporter;
        readonly LcmCache _cache;
        readonly IFwMetaDataCache _metadata;
        readonly ProjectPathResolver _paths;

        public ProjectRecordImporter(ProjectRecordExporter exporter)
        {
            _exporter = exporter ?? throw new ArgumentNullException(nameof(exporter));
            _cache = exporter.Cache;
            _metadata = (IFwMetaDataCache)_cache.MetaDataCacheAccessor;
            _paths = new ProjectPathResolver(_cache);
        }

        /// <summary>Plans the import of <paramref name="fileText"/> into the project.</summary>
        /// <param name="writable">What the host can write; null skips that check.</param>
        public static ProjectRecordPlan Plan(LcmCache cache, ProjectRecordSchemaResult schemaResult, string fileText, IWritableFields? writable = null)
            => new ProjectRecordImporter(new ProjectRecordExporter(cache, schemaResult)).Plan(fileText, writable);

        /// <summary>Tests replace the fresh export to reach paths a healthy project cannot.</summary>
        internal Func<string, Guid, ProjectRecordExport>? ExportOverride { get; set; }

        RecordSchema Schema => _exporter.Schema;

        /// <summary>Plans the import of <paramref name="fileText"/>. It never throws for a bad file: whatever is wrong with it is a refusal.</summary>
        /// <param name="writable">What the host can write; null skips that check.</param>
        public ProjectRecordPlan Plan(string fileText, IWritableFields? writable = null)
        {
            if (fileText == null) throw new ArgumentNullException(nameof(fileText));
            var read = RecordFileReader.Read(fileText, Schema, _exporter.CreateReadOptions());
            var file = read.Document;
            if (file == null || Schema.Kind(file.Kind) == null)
                return Whole(file, ProjectRecordBaseVerdict.NotChecked, null, null, read, MapDiagnostics(read, RecordRefusalKind.UnreadableFile, "The text is not a record file of this project's schema."));

            var kind = Schema.Kind(file.Kind)!;
            var record = Locate(kind, file, out string? problem);
            if (record == null)
                return Whole(file, ProjectRecordBaseVerdict.NotChecked, null, null, read, new[] { Refusal(RecordRefusalKind.UnknownRecord, problem!) });

            ProjectRecordExport export;
            try { export = ExportOverride != null ? ExportOverride(kind.Name, record.Value) : _exporter.Export(kind.Name, record.Value); }
            catch (ArgumentException e)
            {
                return Whole(file, ProjectRecordBaseVerdict.NotChecked, record, null, read, new[] { Refusal(RecordRefusalKind.UnknownRecord, e.Message, record) });
            }
            if (export.Text == null)
                return Whole(file, ProjectRecordBaseVerdict.NotChecked, record, export, read, new[] { Refusal(RecordRefusalKind.ExportFailed, "The project's record could not be written as a file, so the file cannot be compared with it.", record) });

            if (file.Base == null)
                return Whole(file, ProjectRecordBaseVerdict.Missing, record, export, read,
                    new[] { Refusal(RecordRefusalKind.MissingBase, "The file has no base:, so it cannot be shown to be made from the project as it is. Export the record again and edit that file.", record) });
            if (!string.Equals(file.Base, export.Base, StringComparison.Ordinal))
                return Whole(file, ProjectRecordBaseVerdict.Stale, record, export, read,
                    new[] { Refusal(RecordRefusalKind.StaleBase, "The project's record changed after the file was exported. Export it again and redo the edits; there is no merge.", record) });

            var errors = MapDiagnostics(read, null, null);
            if (errors.Count > 0) return Whole(file, ProjectRecordBaseVerdict.Fresh, record, export, read, errors);

            var baseRead = RecordFileReader.Read(export.Text, Schema, _exporter.CreateReadOptions());
            if (baseRead.Document == null || baseRead.HasErrors)
                return Whole(file, ProjectRecordBaseVerdict.Fresh, record, export, read, new[] { Refusal(RecordRefusalKind.ExportFailed, "The project's record does not read back as a record file, so the file cannot be compared with it.", record) });

            RecordChangeSet diff;
            try { diff = RecordDiffer.Diff(baseRead.Document, file, Schema); }
            catch (ArgumentException e)
            {
                return Whole(file, ProjectRecordBaseVerdict.Fresh, record, export, read, new[] { Refusal(RecordRefusalKind.UnreadableFile, "The file cannot be compared with the project's record: " + e.Message, record) });
            }

            var run = new CheckRun(this, export, baseRead.Document, writable);
            var accepted = new List<RecordChange>();
            var refusals = new List<RecordRefusal>(diff.Refusals);
            var dropped = new HashSet<int>();
            for (int i = 0; i < diff.Changes.Count; i++)
            {
                var change = diff.Changes[i];
                var refusal = change.Anchor != null && change.Anchor.Kind == RecordAnchorKind.Added && dropped.Contains(change.Anchor.ChangeIndex)
                    ? ForChange(RecordRefusalKind.DependsOnRefusedChange, "The change is placed after an added object whose change was refused.", change)
                    : run.Check(change);
                if (refusal != null) { refusals.Add(refusal); dropped.Add(i); }
                else accepted.Add(change);
            }
            if (dropped.Count > 0)
            {
                // An anchor names the added object by the index of the change that adds it; the indices shift when changes are taken out.
                foreach (var change in accepted)
                    if (change.Anchor != null && change.Anchor.Kind == RecordAnchorKind.Added)
                        change.Anchor = RecordAnchor.AddedBy(accepted.IndexOf(diff.Changes[change.Anchor.ChangeIndex]));
            }
            return new ProjectRecordPlan(new RecordChangeSet(accepted.AsReadOnly(), refusals.AsReadOnly(), diff.IsPartial), ProjectRecordBaseVerdict.Fresh, kind.Name, record, export, read.Diagnostics);
        }

        // ------------------------------------------------------------------ the file

        ProjectRecordPlan Whole(RecordDocument? file, ProjectRecordBaseVerdict verdict, Guid? record, ProjectRecordExport? export, RecordReadResult read, IReadOnlyList<RecordRefusal> refusals)
        {
            foreach (var r in refusals) r.WholeFile = true;
            return new ProjectRecordPlan(new RecordChangeSet(new List<RecordChange>().AsReadOnly(), refusals.ToList().AsReadOnly(), file?.Root != null), verdict, file?.Kind, record, export, read.Diagnostics);
        }

        static RecordRefusal Refusal(RecordRefusalKind kind, string reason, Guid? obj = null, string field = "")
            => new RecordRefusal(kind, reason) { ObjectGuid = obj, Field = field };

        static RecordRefusal ForChange(RecordRefusalKind kind, string reason, RecordChange c)
            => new RecordRefusal(kind, reason) { ObjectGuid = c.ObjectGuid, Field = c.Field, WritingSystem = c.WritingSystem, Old = c.Old, New = c.New };

        /// <summary>One refusal per error finding of the reader. With a <paramref name="fallback"/> and no error, it is refused for that reason alone.</summary>
        IReadOnlyList<RecordRefusal> MapDiagnostics(RecordReadResult read, RecordRefusalKind? fallback, string? fallbackReason)
        {
            var result = new List<RecordRefusal>();
            foreach (var d in read.Diagnostics.Where(d => d.Severity == MarkdownSeverity.Error))
            {
                RecordRefusalKind kind;
                switch (d.Id)
                {
                    case MarkdownDiagnosticIds.WsUnknown: kind = RecordRefusalKind.UnknownWritingSystem; break;
                    case MarkdownDiagnosticIds.StyleUnknown: kind = RecordRefusalKind.UndefinedStyle; break;
                    case MarkdownDiagnosticIds.LinkNotFound:
                    case MarkdownDiagnosticIds.LinkAmbiguous: kind = RecordRefusalKind.UnknownReference; break;
                    case RecordDiagnosticIds.UnknownField: kind = RecordRefusalKind.UnknownField; break;
                    default: kind = RecordRefusalKind.UnreadableFile; break;
                }
                result.Add(new RecordRefusal(kind, d.Id + " at line " + d.Line + ": " + d.Message) { Field = read.Document == null ? string.Empty : PathAt(read.Document.Data, d.Start) ?? string.Empty, New = d.Observed });
            }
            if (result.Count == 0 && fallback != null) result.Add(Refusal(fallback.Value, fallbackReason!));
            return result;
        }

        /// <summary>The field path of the innermost value whose source range holds the offset.</summary>
        static string? PathAt(RecordValue value, int offset)
        {
            string? best = value.Range != null && value.Range[0] <= offset && offset < value.Range[1] && value.Path.Length > 0 ? value.Path : null;
            IEnumerable<RecordValue> children = value is RecordObject o ? o.Fields.Select(f => f.Value)
                : value is RecordList l ? l.Items : value is RecordAlternatives a ? a.Items.Select(i => (RecordValue)i.Value) : Enumerable.Empty<RecordValue>();
            foreach (var child in children)
            {
                var inner = PathAt(child, offset);
                if (inner != null) best = inner;
            }
            return best;
        }

        /// <summary>The GUID of the project's record object the file is about.</summary>
        Guid? Locate(RecordKind kind, RecordDocument file, out string? problem)
        {
            problem = null;
            var path = file.Root == null ? null : ProjectPath.Parse(file.Root);
            bool whole = file.Root == null || (path != null && path.Segments.Count == kind.PathSegments && path.Field == null);
            if (whole)
            {
                if (file.Data.Guid != null) return file.Data.Guid;
                problem = "The file's record-data has no guid:, so the record it is about cannot be found.";
                return null;
            }
            if (file.Data.Guid is Guid named)
            {
                if (_cache.ServiceLocator.ObjectRepository.TryGetObject(named, out var obj))
                {
                    var top = TopRoot(kind, obj);
                    if (top != null) return top.Guid;
                }
                problem = "The project has no " + kind.Name + " record holding the object the file names.";
                return null;
            }
            if (path != null)
            {
                var recordPath = new ProjectPath(path.Segments.Take(kind.PathSegments).ToList());
                var found = _paths.Resolve(recordPath, out _);
                if (found == null) found = NotebookRecord(kind, recordPath);
                if (found != null) return found;
            }
            problem = "The root: of the file names no record of the project.";
            return null;
        }

        static ICmObject? TopRoot(RecordKind kind, ICmObject obj)
        {
            ICmObject? top = null;
            for (var at = obj; at != null; at = at.Owner)
                if (kind.Roots.Any(r => r.Name == at.ClassName)) top = at;
            return top;
        }

        Guid? NotebookRecord(RecordKind kind, ProjectPath path)
        {
            if (kind.Name != "notebook/record" || path.Segments.Count < 2) return null;
            var notebook = _cache.LangProject.ResearchNotebookOA;
            if (notebook == null) return null;
            var segment = path.Segments[1];
            var matches = notebook.RecordsOC.Where(r => segment.GuidSuffix != null ? r.Guid.ToString("N").StartsWith(segment.GuidSuffix, StringComparison.OrdinalIgnoreCase) : r.Guid.ToString("D") == segment.Name).ToList();
            return matches.Count == 1 ? matches[0].Guid : (Guid?)null;
        }

        // ------------------------------------------------------------------ the checks

        sealed class Leaf
        {
            public RecordObjectType Owner = null!;
            public RecordField Field = null!;
            public int Flid;
        }

        sealed class CheckRun
        {
            readonly ProjectRecordImporter _in;
            readonly ProjectRecordExport _export;
            readonly IWritableFields? _writable;
            readonly HashSet<Guid> _documentGuids = new HashSet<Guid>();
            readonly HashSet<string> _locked = new HashSet<string>(StringComparer.Ordinal);
            readonly SchemaBindings _bindings;
            readonly IFwMetaDataCache _metadata;
            readonly LcmCache _cache;
            readonly Guid _record;

            public CheckRun(ProjectRecordImporter importer, ProjectRecordExport export, RecordDocument baseDocument, IWritableFields? writable)
            {
                _in = importer;
                _export = export;
                _writable = writable;
                _bindings = importer._exporter.Result.Bindings;
                _metadata = importer._metadata;
                _cache = importer._cache;
                _record = export.Guid;
                Collect(baseDocument.Data);
                foreach (var u in export.Unexported) Lock(u);
            }

            void Collect(RecordObject o)
            {
                if (o.Guid != null) _documentGuids.Add(o.Guid.Value);
                foreach (var f in o.Fields)
                {
                    if (f.Value is RecordObject child) Collect(child);
                    else if (f.Value is RecordList list) foreach (var item in list.Items) if (item is RecordObject c) Collect(c);
                }
            }

            /// <summary>An unexported value locks the record field that holds it: the object with a GUID in the file that is, or owns, the object with the value, and the key of its field.</summary>
            void Lock(UnexportedValue u)
            {
                // A timestamp is bookkeeping that liblcm stamps itself when the field is written, so it never locks a field.
                if (u.Field == "(record)" || IsTimestamp(u) || !_cache.ServiceLocator.ObjectRepository.TryGetObject(u.Object, out var obj)) return;
                if (_documentGuids.Contains(obj.Guid))
                {
                    int flid = FieldId(obj.ClassName, u.Field);
                    string? key = flid == 0 ? null : KeyOf(obj.ClassName, flid);
                    if (key != null) _locked.Add(LockKey(obj.Guid, key));
                    return;
                }
                for (var child = obj; child.Owner != null; child = child.Owner)
                {
                    var parent = child.Owner;
                    if (!_documentGuids.Contains(parent.Guid)) continue;
                    string? key = KeyOf(parent.ClassName, child.OwningFlid);
                    if (key != null) _locked.Add(LockKey(parent.Guid, key));
                    return;
                }
            }

            static string LockKey(Guid guid, string key) => guid.ToString("D") + "|" + key;

            int FieldId(string className, string field)
            {
                try { return _metadata.GetFieldId(className, field, true); }
                catch (Exception e) when (e is ArgumentException || e is InvalidOperationException || e is KeyNotFoundException) { return 0; }
            }

            string? KeyOf(string className, int flid)
            {
                if (!_bindings.Keys.TryGetValue(className, out var keys)) return null;
                foreach (var pair in keys) if (pair.Value == flid) return pair.Key;
                return null;
            }

            public RecordRefusal? Check(RecordChange c)
            {
                Guid owner = c.ObjectGuid ?? _record;
                string top = c.Field.Split('/')[0];
                if (_locked.Contains(LockKey(owner, top)))
                    return ForChange(RecordRefusalKind.UnexportedValue, "The project holds more in " + top + " than the file can show (the export lists it as unexported), so writing it would lose that.", c);

                // Fail closed: a change that cannot be mapped to a project field is never accepted unchecked.
                var leaf = Resolve(c);
                if (leaf == null)
                    return ForChange(RecordRefusalKind.UnknownField, "The change names a field that could not be resolved to a project field, so it cannot be checked.", c);
                if (_writable != null)
                {
                    string declaring = _metadata.GetOwnClsName(leaf.Flid), name = _metadata.GetFieldName(leaf.Flid);
                    if (c.Kind == RecordChangeKind.AddObject)
                    {
                        string created = c.Subtree?.Class ?? leaf.Field.ObjectTypeNames.FirstOrDefault() ?? leaf.Field.Item?.ObjectTypeNames.FirstOrDefault() ?? string.Empty;
                        if (!_writable.CanCreate(declaring, name, created))
                            return ForChange(RecordRefusalKind.CreateNotWritable, "Motif cannot create a " + created + " in " + declaring + "." + name + ".", c);
                    }
                    else if (c.Kind == RecordChangeKind.RemoveObject)
                    {
                        var removed = RemovedObject(c);
                        string deleted = removed?.ClassName ?? c.Subtree?.Class ?? leaf.Field.ObjectTypeNames.FirstOrDefault() ?? leaf.Field.Item?.ObjectTypeNames.FirstOrDefault() ?? string.Empty;
                        if (!_writable.CanDelete(declaring, name, deleted))
                            return ForChange(RecordRefusalKind.DeleteNotWritable, "Motif cannot delete a " + deleted + " from " + declaring + "." + name + ".", c);
                    }
                    else if (!_writable.CanWrite(declaring, name))
                        return ForChange(RecordRefusalKind.FieldNotWritable, "Motif cannot write " + declaring + "." + name + ".", c);
                }

                switch (c.Kind)
                {
                    case RecordChangeKind.Set when leaf != null && leaf.Field.Kind == RecordFieldKind.Reference:
                    case RecordChangeKind.AddReference:
                        return CheckTarget(c, c.NewTarget, leaf);
                    case RecordChangeKind.AddObject:
                        return CheckAdded(c, leaf);
                    case RecordChangeKind.RemoveObject:
                        return CheckRemoved(c);
                    default:
                        return null;
                }
            }

            // ---- the field a change writes

            Leaf? Resolve(RecordChange c)
            {
                var type = c.ObjectType == null ? null : _in.Schema.ObjectType(c.ObjectType);
                if (type == null) return null;
                RecordField? field = null;
                RecordObjectType? owner = null;
                foreach (string segment in c.Field.Split('/'))
                {
                    if (segment.Length == 0) continue;
                    if (segment.All(char.IsDigit))
                    {
                        var item = field != null && field.IsList ? field.Item : null;
                        if (item == null || item.Kind != RecordFieldKind.Object || item.ObjectTypes.Count != 1) return null;
                        type = item.ObjectTypes[0];
                        continue;
                    }
                    if (field != null && field.Kind == RecordFieldKind.Object)
                    {
                        if (field.ObjectTypes.Count != 1) return null;
                        type = field.ObjectTypes[0];
                    }
                    field = type.Field(segment);
                    if (field == null) return null;
                    owner = type;
                }
                if (field == null || owner == null || !_bindings.Keys.TryGetValue(owner.Name, out var keys) || !keys.TryGetValue(field.Name, out int flid)) return null;
                return new Leaf { Owner = owner, Field = field, Flid = flid };
            }

            // ---- references

            RecordRefusal? CheckTarget(RecordChange c, Guid? target, Leaf? leaf)
            {
                string? problem = TargetProblem(target, leaf?.Flid ?? 0, out bool wrongClass);
                return problem == null ? null : ForChange(wrongClass ? RecordRefusalKind.WrongReferenceClass : RecordRefusalKind.UnknownReference, problem, c);
            }

            string? TargetProblem(Guid? target, int flid, out bool wrongClass)
            {
                wrongClass = false;
                if (flid < 0) return "The class of a new object holding this reference could not be determined, so the reference cannot be checked.";
                if (target == null) return "The reference names no object of the project (its link has no guid).";
                if (!_cache.ServiceLocator.ObjectRepository.TryGetObject(target.Value, out var obj)) return "The project has no object " + target.Value.ToString("D") + ".";
                if (flid == 0) return "The field that holds the reference could not be resolved, so its class cannot be checked.";
                int wanted = _metadata.GetDstClsId(flid);
                int have = _metadata.GetClassId(obj.ClassName);
                for (int at = have; at != 0; at = _metadata.GetBaseClsId(at)) if (at == wanted) return null;
                wrongClass = true;
                return obj.ClassName + " " + target.Value.ToString("D") + " is not a " + _metadata.GetClassName(wanted) + ", which " + _metadata.GetFieldName(flid) + " holds.";
            }

            // ---- an added object

            RecordRefusal? CheckAdded(RecordChange c, Leaf? leaf)
            {
                var subtree = c.Subtree;
                if (subtree == null) return null;
                foreach (var guid in Guids(subtree))
                    if (_cache.ServiceLocator.ObjectRepository.TryGetObject(guid, out _))
                        return ForChange(RecordRefusalKind.GuidInUse, "The project already has an object " + guid.ToString("D") + ".", c);
                var type = leaf == null ? null : TypeOf(subtree, leaf.Field.IsList ? leaf.Field.Item! : leaf.Field);
                if (type == null)
                    return ForChange(RecordRefusalKind.UnknownField, "The class of the new object could not be determined, so its fields cannot be checked.", c);
                foreach (var (target, flid) in References(subtree, type))
                {
                    string? problem = TargetProblem(target, flid, out bool wrongClass);
                    if (problem != null) return ForChange(wrongClass ? RecordRefusalKind.WrongReferenceClass : RecordRefusalKind.UnknownReference, problem, c);
                }
                return null;
            }

            static RecordObjectType? TypeOf(RecordObject o, RecordField field)
                => field.ObjectTypes.FirstOrDefault(t => t.Name == o.Class) ?? (field.ObjectTypes.Count == 1 ? field.ObjectTypes[0] : null);

            static IEnumerable<Guid> Guids(RecordObject o)
            {
                if (o.Guid != null) yield return o.Guid.Value;
                foreach (var f in o.Fields)
                {
                    if (f.Value is RecordObject child) foreach (var g in Guids(child)) yield return g;
                    else if (f.Value is RecordList list)
                        foreach (var item in list.Items) if (item is RecordObject c) foreach (var g in Guids(c)) yield return g;
                }
            }

            IEnumerable<(Guid? Target, int Flid)> References(RecordObject o, RecordObjectType type)
            {
                _bindings.Keys.TryGetValue(type.Name, out var keys);
                foreach (var pair in o.Fields)
                {
                    var field = type.Field(pair.Key);
                    if (field == null) continue;
                    int flid = 0;
                    keys?.TryGetValue(field.Name, out flid);
                    if (pair.Value is RecordScalar scalar && field.Kind == RecordFieldKind.Reference) yield return (scalar.Target, flid);
                    else if (pair.Value is RecordObject child && field.Kind == RecordFieldKind.Object)
                    {
                        var childType = TypeOf(child, field);
                        if (childType != null) foreach (var r in References(child, childType)) yield return r;
                        else yield return (null, -1);
                    }
                    else if (pair.Value is RecordList list && field.Item != null)
                    {
                        foreach (var item in list.Items)
                        {
                            if (item is RecordScalar s && field.Item.Kind == RecordFieldKind.Reference) yield return (s.Target, flid);
                            else if (item is RecordObject c && field.Item.Kind == RecordFieldKind.Object)
                            {
                                var childType = TypeOf(c, field.Item);
                                if (childType != null) foreach (var r in References(c, childType)) yield return r;
                                else yield return (null, -1);
                            }
                        }
                    }
                }
            }

            // ---- a removed object (delete by omission, or any removal)

            ICmObject? RemovedObject(RecordChange c)
            {
                Guid? guid = c.ItemGuid ?? c.Subtree?.Guid;
                return guid != null && _cache.ServiceLocator.ObjectRepository.TryGetObject(guid.Value, out var obj) ? obj : null;
            }

            RecordRefusal? CheckRemoved(RecordChange c)
            {
                var obj = RemovedObject(c);
                if (obj == null)
                    return ForChange(RecordRefusalKind.UnknownDeleteTarget, "The object to delete has no guid or is not in the project, so what it holds cannot be checked.", c);
                var owned = new HashSet<Guid>(obj.AllOwnedObjects.Select(o => o.Guid)) { obj.Guid };
                foreach (var u in _export.Unexported)
                {
                    if (!owned.Contains(u.Object) || IsTimestamp(u)) continue;
                    return ForChange(RecordRefusalKind.DeleteHasUnexportedData, "The " + obj.ClassName + " " + obj.Guid.ToString("D") + " holds data the file cannot show (" + u.Class + "." + u.Field + "), so deleting it would lose that.", c);
                }
                foreach (var o in new[] { obj }.Concat(obj.AllOwnedObjects))
                    foreach (var referrer in o.ReferringObjects)
                        if (!owned.Contains(referrer.Guid))
                            return ForChange(RecordRefusalKind.DeleteHasIncomingReferences, "Another object of the project (" + referrer.ClassName + " " + referrer.Guid.ToString("D") + ") refers to the " + obj.ClassName + " " + obj.Guid.ToString("D") + " (or an object it owns), so deleting it would break that reference.", c);
                // A link inside rich text is not a liblcm reference; the project's rich text is searched, once per plan and only when something is deleted.
                var links = RichLinks();
                foreach (var g in owned)
                    if (links.TryGetValue(g, out var linkers))
                        foreach (var linker in linkers)
                            if (!owned.Contains(linker))
                                return ForChange(RecordRefusalKind.DeleteHasIncomingReferences, "A link in the text of another object (" + linker.ToString("D") + ") points at the " + obj.ClassName + " " + obj.Guid.ToString("D") + " (or an object it owns), so deleting it would leave that link dangling.", c);
                return null;
            }

            Dictionary<Guid, HashSet<Guid>>? _richLinks;

            /// <summary>For every object GUID that a rich-text link (an object-data run property) names: the GUIDs of the objects whose strings hold such a link. Built on first use by one pass over
            /// every String and MultiString field of every object of the project.</summary>
            Dictionary<Guid, HashSet<Guid>> RichLinks()
            {
                if (_richLinks != null) return _richLinks;
                var found = new Dictionary<Guid, HashSet<Guid>>();
                var data = _cache.DomainDataByFlid;
                var managed = (IFwMetaDataCacheManaged)_metadata;
                var fieldsByClass = new Dictionary<int, List<(int Flid, bool Multi)>>();
                foreach (var obj in _cache.ServiceLocator.ObjectRepository.AllInstances())
                {
                    if (!fieldsByClass.TryGetValue(obj.ClassID, out var fields))
                    {
                        fields = new List<(int, bool)>();
                        foreach (int flid in managed.GetFields(obj.ClassID, true, -1))
                        {
                            if (_metadata.get_IsVirtual(flid)) continue;
                            var type = (SIL.LCModel.Core.Cellar.CellarPropertyType)_metadata.GetFieldType(flid);
                            if (type == SIL.LCModel.Core.Cellar.CellarPropertyType.String) fields.Add((flid, false));
                            else if (type == SIL.LCModel.Core.Cellar.CellarPropertyType.MultiString) fields.Add((flid, true));
                        }
                        fieldsByClass[obj.ClassID] = fields;
                    }
                    foreach (var (flid, multi) in fields)
                    {
                        if (!multi) Scan(obj, data.get_StringProp(obj.Hvo, flid), found);
                        else
                        {
                            var m = data.get_MultiStringProp(obj.Hvo, flid);
                            for (int i = 0; i < m.StringCount; i++) Scan(obj, m.GetStringFromIndex(i, out _), found);
                        }
                    }
                }
                return _richLinks = found;
            }

            static readonly System.Text.RegularExpressions.Regex GuidText = new System.Text.RegularExpressions.Regex(
                "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

            static void Scan(ICmObject owner, ITsString? s, Dictionary<Guid, HashSet<Guid>> found)
            {
                if (s == null || s.Length == 0) return;
                for (int i = 0; i < s.RunCount; i++)
                {
                    string? objData = s.get_Properties(i).GetStrPropValue((int)FwTextPropType.ktptObjData);
                    if (string.IsNullOrEmpty(objData)) continue;
                    foreach (System.Text.RegularExpressions.Match m in GuidText.Matches(objData!))
                        if (Guid.TryParse(m.Value, out var g)) Add(found, g, owner.Guid);
                    // Hot links to objects keep a type character and the GUID's 16 bytes as eight UTF-16 characters.
                    if (objData!.Length == 9)
                        Add(found, new Guid(System.Text.Encoding.Unicode.GetBytes(objData.Substring(1))), owner.Guid);
                }
            }

            static void Add(Dictionary<Guid, HashSet<Guid>> found, Guid target, Guid linker)
            {
                if (!found.TryGetValue(target, out var set)) found[target] = set = new HashSet<Guid>();
                set.Add(linker);
            }

            /// <summary>A timestamp (the non-custom DateCreated and DateModified, matched by name) is stamped by liblcm when the object is written and goes with the object when it is deleted. It locks no field and is no data a delete loses.</summary>
            bool IsTimestamp(UnexportedValue u)
            {
                // Only liblcm's own two stamps, by name: a custom field, or any other Time field, may hold a user's data.
                if (u.Field != "DateCreated" && u.Field != "DateModified") return false;
                int flid = FieldId(u.Class, u.Field);
                return flid != 0 && !((IFwMetaDataCacheManaged)_metadata).IsCustom(flid)
                    && (SIL.LCModel.Core.Cellar.CellarPropertyType)_metadata.GetFieldType(flid) == SIL.LCModel.Core.Cellar.CellarPropertyType.Time;
            }
        }
    }
}
