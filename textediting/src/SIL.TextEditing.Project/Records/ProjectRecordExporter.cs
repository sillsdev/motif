using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SIL.LCModel;
using SIL.LCModel.Core.Cellar;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using SIL.LCModel.Utils;
using SIL.TextEditing.LCModel;
using SIL.TextEditing.Markdown;
using SIL.TextEditing.Markdown.Records;

namespace SIL.TextEditing.Project
{
    /// <summary>A value of a project object that an exported record file does not hold. Export never drops data silently: each such value is listed here with
    /// its place and reason, and with its value where <see cref="Value"/> keeps it. A null or described <see cref="Value"/> means the report alone does not hold the data.</summary>
    public sealed class UnexportedValue
    {
        internal UnexportedValue(Guid owner, string className, string field, string reason, string? value)
        {
            Object = owner;
            Class = className;
            Field = field;
            Reason = reason;
            Value = value;
        }

        /// <summary>The GUID of the object that holds the value.</summary>
        public Guid Object { get; }

        /// <summary>The metadata class of that object.</summary>
        public string Class { get; }

        /// <summary>The metadata field name, or <c>(record)</c> when the whole record could not be written.</summary>
        public string Field { get; }

        /// <summary>Why the file does not hold it.</summary>
        public string Reason { get; }

        /// <summary>What the report keeps of the value, as invariant text. For most values it is the value itself: a timestamp as ISO 8601 UTC, an integer in
        /// digits, text as it is, binary up to 1,024 bytes as base64, the GUIDs of related objects separated by commas. It is only a description for binary over 1,024 bytes
        /// (its length and SHA-256: the bytes are not kept), and it is null when the value could not be read or is a style or a record-level failure; read those from the
        /// project.</summary>
        public string? Value { get; }

        public override string ToString() => Object.ToString("D") + " " + Class + "." + Field + ": " + Reason + (Value == null ? string.Empty : " [" + Value + "]");
    }

    /// <summary>One exported record: the file text (null only when <see cref="Unexported"/> says the record could not be written), the document it was
    /// written from, the project path that names it, and every value the file leaves out.</summary>
    public sealed class ProjectRecordExport
    {
        internal ProjectRecordExport(string kind, Guid guid, string path, string? text, RecordDocument document, IReadOnlyList<UnexportedValue> unexported)
        {
            Kind = kind;
            Guid = guid;
            Path = path;
            Text = text;
            Document = document;
            Unexported = unexported;
        }

        internal ProjectRecordExport WithPath(string path) => new ProjectRecordExport(Kind, Guid, path, Text, Document, Unexported);

        /// <summary>The record kind (<c>lexicon/entry</c>).</summary>
        public string Kind { get; }

        /// <summary>The GUID of the record's root object.</summary>
        public Guid Guid { get; }

        /// <summary>The project path that names the record, with as many segments as the kind's <see cref="RecordKind.PathSegments"/>. A host maps it to a
        /// file; the file's own front matter has no <c>root:</c>, which for a whole record is absent.</summary>
        public string Path { get; }

        /// <summary>The record file, in the canonical spelling of <see cref="RecordFileWriter"/>; null when the writer refused the document.</summary>
        public string? Text { get; }

        /// <summary>The <c>base:</c> value the file carries (<c>sha256:</c> and hex), a digest of the file text without it and of <see cref="Unexported"/>; null when
        /// <see cref="Text"/> is. An import compares it with a fresh export of the same record to tell whether the project changed since.</summary>
        public string? Base => Document.Base;

        /// <summary>The record as data. It is what the file reads back as.</summary>
        public RecordDocument Document { get; }

        /// <summary>Every value of the record's objects that the file does not hold, in a fixed order.</summary>
        public IReadOnlyList<UnexportedValue> Unexported { get; }
    }

    /// <summary>Writes record files from a loaded project, read-only (ADR 0032, Motif ADR 0003). Given the result of
    /// <see cref="ProjectRecordSchema.Generate"/>, it turns an object of a kind, or every root object of a kind, into the text of a record file. It changes nothing in
    /// the project. The caller owns the cache and serializes access to it.</summary>
    public sealed class ProjectRecordExporter
    {
        const string Plumbing = "|Self|Owner|OwningFlid|OwnOrd|Guid|ClassID|ShortNameTSS|";

        readonly LcmCache _cache;
        readonly ProjectRecordSchemaResult _result;
        readonly ISilDataAccess _data;
        readonly IFwMetaDataCache _metadata;
        readonly IFwMetaDataCacheManaged _managed;
        readonly ProjectWritingSystems _writingSystems;
        readonly ProjectStyles _styles;
        readonly ProjectPathResolver _paths;
        readonly LinkMapper _objects;

        public ProjectRecordExporter(LcmCache cache, ProjectRecordSchemaResult schema)
        {
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _result = schema ?? throw new ArgumentNullException(nameof(schema));
            _data = cache.DomainDataByFlid;
            _metadata = (IFwMetaDataCache)cache.MetaDataCacheAccessor;
            _managed = (IFwMetaDataCacheManaged)_metadata;
            _writingSystems = new ProjectWritingSystems(cache);
            _styles = new ProjectStyles(cache);
            _paths = new ProjectPathResolver(cache);
            _objects = new LinkMapper(_paths);
        }

        /// <summary>Runs before each record of <see cref="ExportAll"/>, so a test can make one record fail.</summary>
        internal Action<ICmObject>? BeforeRecord { get; set; }

        internal ProjectRecordSchemaResult Result => _result;
        internal LcmCache Cache => _cache;

        /// <summary>The schema the files are written against.</summary>
        public RecordSchema Schema => _result.Schema;

        /// <summary>Options that read an exported file back against this project: its writing systems, styles and links.</summary>
        public RecordReadOptions CreateReadOptions() => new RecordReadOptions { Markdown = Options(_writingSystems.DefaultAnalysis, _writingSystems.DefaultVernacular) };

        /// <summary>Exports the object as the first kind whose root classes include its class.</summary>
        /// <exception cref="ArgumentException">The project has no such object, or no kind of the schema has its class as a root.</exception>
        public ProjectRecordExport Export(Guid guid)
        {
            var obj = Find(guid);
            var kind = Schema.Kinds.FirstOrDefault(k => k.Roots.Any(r => r.Name == obj.ClassName))
                ?? throw new ArgumentException("No record kind of the schema has " + obj.ClassName + " as a root.");
            return Export(kind, obj);
        }

        /// <summary>Exports the object as the named kind.</summary>
        /// <exception cref="ArgumentException">The schema has no such kind, the project no such object, or the object's class is not a root of the kind.</exception>
        public ProjectRecordExport Export(string kind, Guid guid)
        {
            var found = Schema.Kind(kind ?? throw new ArgumentNullException(nameof(kind))) ?? throw new ArgumentException("The schema has no record kind " + kind + ".", nameof(kind));
            return Export(found, Find(guid));
        }

        /// <summary>Exports every root object of the kind, ordered by project path and then GUID. A list item is a root when it is a top-level item of its list:
        /// its sub-items are inside its record. A notebook record is a root when it is not a sub-record.</summary>
        /// <exception cref="NotSupportedException">The kind is not one of <c>lexicon/entry</c>, <c>notebook/record</c> and <c>lists/item</c>.</exception>
        public IReadOnlyList<ProjectRecordExport> ExportAll(string kind)
        {
            var found = Schema.Kind(kind ?? throw new ArgumentNullException(nameof(kind))) ?? throw new ArgumentException("The schema has no record kind " + kind + ".", nameof(kind));
            var roots = new List<ICmObject>();
            switch (found.Name)
            {
                case "lexicon/entry":
                    roots.AddRange(_cache.ServiceLocator.GetInstance<ILexEntryRepository>().AllInstances().Cast<ICmObject>());
                    break;
                case "notebook/record":
                    var notebook = _cache.LangProject.ResearchNotebookOA;
                    if (notebook != null) roots.AddRange(notebook.RecordsOC.Cast<ICmObject>());
                    break;
                case "lists/item":
                    foreach (var list in _cache.ServiceLocator.GetInstance<ICmPossibilityListRepository>().AllInstances())
                        roots.AddRange(list.PossibilitiesOS.Cast<ICmObject>());
                    break;
                default:
                    throw new NotSupportedException("Exporting every root of " + found.Name + " is not supported; export objects by GUID.");
            }
            var outside = roots.FirstOrDefault(r => found.Roots.All(t => t.Name != r.ClassName));
            if (outside != null) throw new InvalidOperationException(outside.ClassName + " is not a root class of " + found.Name + ", and would have been left out.");
            var exports = roots.Select(r => ExportOrReport(found, r)).ToList();
            // Two records never share a path: a path that more than one record has takes the first eight hex digits of its record's GUID.
            var shared = new HashSet<string>(exports.GroupBy(e => e.Path, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key), StringComparer.Ordinal);
            if (shared.Count > 0)
                exports = exports.Select(e => shared.Contains(e.Path) ? e.WithPath(WithGuidSuffix(e.Path, e.Guid)) : e).ToList();
            return exports.OrderBy(e => e.Path, StringComparer.Ordinal).ThenBy(e => e.Guid).ToList();
        }

        /// <summary>One record that cannot be exported is reported in its own <see cref="ProjectRecordExport.Unexported"/> and leaves the rest of the batch alone.</summary>
        ProjectRecordExport ExportOrReport(RecordKind kind, ICmObject root)
        {
            try { BeforeRecord?.Invoke(root); return Export(kind, root); }
            catch (Exception e) when (!(e is OutOfMemoryException) && !(e is StackOverflowException))
            {
                var data = new RecordObject { Guid = root.Guid };
                var document = new RecordDocument(kind.Name) { Data = data };
                var unexported = new[] { new UnexportedValue(root.Guid, root.ClassName, "(record)", "The record could not be exported (" + e.GetType().Name + "); the exception text is withheld because it can quote project values.", null) };
                return new ProjectRecordExport(kind.Name, root.Guid, FallbackPath(kind, root.Guid), null, document, unexported);
            }
        }

        static string FallbackPath(RecordKind kind, Guid guid)
        {
            var segments = new List<string> { kind.Name.Split('/')[0] };
            while (segments.Count < kind.PathSegments - 1) segments.Add("unowned");
            segments.Add(guid.ToString("D"));
            return new ProjectPath(segments.ToArray()).ToString();
        }

        static string WithGuidSuffix(string path, Guid guid)
        {
            var parsed = ProjectPath.Parse(path)!;
            var segments = parsed.Segments.ToList();
            var last = segments[segments.Count - 1];
            segments[segments.Count - 1] = new ProjectPathSegment(last.Name, null, guid.ToString("N").Substring(0, 8));
            return new ProjectPath(segments).ToString();
        }

        ICmObject Find(Guid guid)
            => _cache.ServiceLocator.ObjectRepository.TryGetObject(guid, out var obj) ? obj : throw new ArgumentException("The project has no object " + guid.ToString("D") + ".", nameof(guid));

        ProjectRecordExport Export(RecordKind kind, ICmObject root)
        {
            var rootType = kind.Roots.FirstOrDefault(t => t.Name == root.ClassName)
                ?? throw new ArgumentException(root.ClassName + " is not a root class of " + kind.Name + ".");
            var run = new ExportRun();
            var document = new RecordDocument(kind.Name)
            {
                Analysis = _writingSystems.DefaultAnalysis,
                Vernacular = _writingSystems.DefaultVernacular,
            };
            var data = Object(root, rootType, kind.Roots.Count > 1, run);
            document.Data = data;
            foreach (string ws in run.WritingSystems)
                document.WritingSystems.Add(new KeyValuePair<string, WritingSystemInfo>(ws, _writingSystems.DescribeWritingSystem(ws) ?? WritingSystemInfo.Default(ws)));
            string? text = null;
            try
            {
                // The digest covers the file without its base: line and every unexported value, then the file is written again with it.
                string bare = RecordFileWriter.Write(document, Schema);
                var unexported = run.Unexported.ToArray();
                document.Base = RecordBaseDigest.Compute(bare, unexported);
                text = RecordFileWriter.Write(document, Schema);
            }
            catch (RecordWriteException e)
            {
                document.Base = null;
                run.Unexported.Add(new UnexportedValue(root.Guid, root.ClassName, "(record)", "The record file could not be written: " + e.Message, null));
            }
            return new ProjectRecordExport(kind.Name, root.Guid, PathOf(kind, root), text, document, run.Unexported.ToArray());
        }

        // ------------------------------------------------------------------ paths

        string PathOf(RecordKind kind, ICmObject root)
        {
            switch (kind.Name)
            {
                case "lexicon/entry":
                {
                    var path = _paths.PathOf(root.Guid);
                    return path != null && path.Segments.Count == 2 && path.Segments[0].Name == "lexicon"
                        ? path.ToString() : new ProjectPath("lexicon", root.Guid.ToString("D")).ToString();
                }
                case "notebook/record":
                {
                    string title = _data.get_StringProp(root.Hvo, RnGenericRecTitle()).Text ?? string.Empty;
                    return title.Length == 0 ? new ProjectPath("notebook", root.Guid.ToString("D")).ToString()
                        : new ProjectPath(new[] { new ProjectPathSegment("notebook"), new ProjectPathSegment(title, null, root.Guid.ToString("N").Substring(0, 8)) }).ToString();
                }
                case "lists/item":
                {
                    var owner = root.OwnerOfClass<ICmPossibilityList>();
                    var listPath = owner == null ? null : _paths.PathOf(owner.Guid);
                    string list = listPath != null && listPath.Segments.Count == 2 && listPath.Segments[0].Name == "lists"
                        ? listPath.Segments[1].Name : owner?.Guid.ToString("D") ?? "unowned";
                    return new ProjectPath("lists", list, root.Guid.ToString("D")).ToString();
                }
                default:
                    throw new NotSupportedException("No path convention for " + kind.Name + ".");
            }
        }

        int RnGenericRecTitle() => _metadata.GetFieldId("RnGenericRec", "Title", false);

        // ------------------------------------------------------------------ objects

        sealed class ExportRun
        {
            public List<UnexportedValue> Unexported { get; } = new List<UnexportedValue>();
            public List<string> WritingSystems { get; } = new List<string>();
            public void Use(string ws) { if (ws.Length > 0 && !WritingSystems.Contains(ws)) WritingSystems.Add(ws); }
        }

        RecordObject Object(ICmObject obj, RecordObjectType type, bool withClass, ExportRun run)
        {
            var result = new RecordObject { Guid = obj.Guid };
            if (withClass) result.Class = type.Name;
            var keys = _result.Bindings.Keys[type.Name];
            foreach (var field in type.Fields)
            {
                if (!keys.TryGetValue(field.Name, out int flid)) continue;
                RecordValue? value;
                try { value = Value(obj, flid, field, run); }
                catch (Exception e) when (e is MarkdownWriteException || e is ArgumentException || e is InvalidOperationException || e is NotSupportedException)
                {
                    string? kept = null;
                    try { Describe(obj.Hvo, flid, out kept); } catch (Exception) { kept = null; }
                    run.Unexported.Add(new UnexportedValue(obj.Guid, obj.ClassName, _metadata.GetFieldName(flid), "The value could not be converted: " + e.Message, kept));
                    continue;
                }
                if (value != null && !value.IsEmpty) result[field.Name] = value;
            }
            ReportExcluded(obj, _result.Bindings.Excluded[type.Name], run);
            return result;
        }

        RecordValue? Value(ICmObject obj, int flid, RecordField field, ExportRun run)
        {
            int hvo = obj.Hvo;
            var type = (CellarPropertyType)_metadata.GetFieldType(flid);
            if (field.MultiWritingSystem) return Alternatives(obj, flid, field, run);
            switch (field.Kind)
            {
                case RecordFieldKind.Text:
                case RecordFieldKind.Notation:
                    if (type == CellarPropertyType.String)
                    {
                        var s = _data.get_StringProp(hvo, flid);
                        return PlainFromString(obj, flid, s, run);
                    }
                    return new RecordScalar(_data.get_UnicodeProp(hvo, flid) ?? string.Empty);
                case RecordFieldKind.Rich:
                    if (field.Paragraphs) return Paragraphs(obj, flid, field, run);
                    return RichScalar(_data.get_StringProp(hvo, flid), Default(field), run, obj, flid);
                case RecordFieldKind.Integer:
                    return RecordScalar.Of(_data.get_IntProp(hvo, flid));
                case RecordFieldKind.Boolean:
                    return RecordScalar.Of(_data.get_BooleanProp(hvo, flid));
                case RecordFieldKind.Guid:
                    return RecordScalar.Of(_data.get_GuidProp(hvo, flid));
                case RecordFieldKind.Reference:
                {
                    int target = _data.get_ObjectProp(hvo, flid);
                    return target == 0 ? null : Link(Hvo(target), run);
                }
                case RecordFieldKind.Object:
                {
                    int target = _data.get_ObjectProp(hvo, flid);
                    return target == 0 ? null : Owned(Hvo(target), field, run);
                }
                case RecordFieldKind.Sequence:
                case RecordFieldKind.Collection:
                {
                    var items = Vector(hvo, flid);
                    if (field.Kind == RecordFieldKind.Collection) items = items.OrderBy(i => i.Guid).ToList();
                    var list = new RecordList();
                    foreach (var item in items)
                        list.Add(field.Item!.Kind == RecordFieldKind.Reference ? Link(item, run) : Owned(item, field.Item, run));
                    return list;
                }
                default:
                    throw new NotSupportedException("Unsupported record field kind " + field.Kind + ".");
            }
        }

        ICmObject Hvo(int hvo) => _cache.ServiceLocator.GetObject(hvo);

        List<ICmObject> Vector(int hvo, int flid)
        {
            int count = _data.get_VecSize(hvo, flid);
            var items = new List<ICmObject>(count);
            for (int i = 0; i < count; i++) items.Add(Hvo(_data.get_VecItem(hvo, flid, i)));
            return items;
        }

        RecordObject Owned(ICmObject obj, RecordField field, ExportRun run)
        {
            var type = field.ObjectTypes.FirstOrDefault(t => t.Name == obj.ClassName)
                ?? throw new InvalidOperationException(obj.ClassName + " is not an allowed class of the field (" + string.Join(", ", field.ObjectTypeNames) + ")");
            return Object(obj, type, field.ObjectTypes.Count > 1, run);
        }

        string Default(RecordField field) => field.WritingSystem == RecordWritingSystem.Vernacular ? _writingSystems.DefaultVernacular : _writingSystems.DefaultAnalysis;

        // ------------------------------------------------------------------ text

        RecordAlternatives? Alternatives(ICmObject obj, int flid, RecordField field, ExportRun run)
        {
            var multi = _data.get_MultiStringProp(obj.Hvo, flid);
            var result = new RecordAlternatives();
            var found = new List<KeyValuePair<string, ITsString>>();
            for (int i = 0; i < multi.StringCount; i++)
            {
                var s = multi.GetStringFromIndex(i, out int ws);
                if (s == null || s.Length == 0) continue;
                found.Add(new KeyValuePair<string, ITsString>(_writingSystems.GetId(ws), s));
            }
            foreach (var alt in found.OrderBy(a => a.Key, StringComparer.Ordinal))
            {
                if (field.Kind == RecordFieldKind.Rich) { run.Use(alt.Key); result.Add(alt.Key, RichScalar(alt.Value, alt.Key, run, obj, flid)!); }
                else
                {
                    ReportPlainFormatting(obj, flid, alt.Value, alt.Key, run);
                    result.Add(alt.Key, new RecordScalar(alt.Value.Text ?? string.Empty));
                }
            }
            return result;
        }

        RecordScalar? PlainFromString(ICmObject obj, int flid, ITsString s, ExportRun run)
        {
            ReportPlainFormatting(obj, flid, s, null, run);
            return new RecordScalar(s.Text ?? string.Empty);
        }

        /// <summary>A plain or notation field over a formatted string keeps the characters; any property other than the writing system is reported.</summary>
        void ReportPlainFormatting(ICmObject obj, int flid, ITsString s, string? ws, ExportRun run)
        {
            for (int i = 0; i < s.RunCount; i++)
            {
                var props = s.get_Properties(i);
                bool formatted = props.StrPropCount > 0;
                for (int k = 0; k < props.IntPropCount && !formatted; k++)
                {
                    props.GetIntProp(k, out int tpt, out _);
                    formatted = tpt != (int)FwTextPropType.ktptWs;
                }
                if (!formatted) continue;
                run.Unexported.Add(new UnexportedValue(obj.Guid, obj.ClassName, _metadata.GetFieldName(flid),
                    "The record field is plain text; the string's formatting is not exported" + (ws == null ? "." : " (alternative " + ws + ")."), null));
                return;
            }
        }

        RecordScalar? RichScalar(ITsString s, string defaultWs, ExportRun run, ICmObject obj, int flid)
        {
            if (s == null || s.Length == 0) return null;
            var runs = TsStringConversion.ToRuns(s, _writingSystems);
            foreach (var r in runs) run.Use(r.Ws);
            KnownStyles(runs, run, obj, flid);
            var options = Options(defaultWs, _writingSystems.DefaultVernacular);
            var scalar = RecordScalar.RichInline(runs, options);
            var source = new Paragraph { Style = options.StyleNames.Normal };
            source.Props.StrProps[TextPropertyNumbers.NamedStyle] = options.StyleNames.Normal;
            foreach (var r in runs) source.Runs.Add(r.Clone());
            if (source.Runs.Count > 0) source.DefaultWs = source.Runs[0].Ws;
            var again = new Paragraph { Style = options.StyleNames.Normal };
            again.Props.StrProps[TextPropertyNumbers.NamedStyle] = options.StyleNames.Normal;
            foreach (var r in MarkdownConverter.ParseInline(scalar.Text, options).Runs) again.Runs.Add(r);
            if (again.Runs.Count > 0) again.DefaultWs = again.Runs[0].Ws;
            if (!MarkdownConverter.AreEquivalent(source, again)) ReportNotPreserved(obj, flid, run);
            return scalar;
        }

        void ReportNotPreserved(ICmObject obj, int flid, ExportRun run)
        {
            string? kept = null;
            try { Describe(obj.Hvo, flid, out kept); } catch (Exception) { kept = null; }
            run.Unexported.Add(new UnexportedValue(obj.Guid, obj.ClassName, _metadata.GetFieldName(flid),
                "Direct formatting that the file's Markdown does not spell, so the file reads back differently from the project.", kept));
        }

        RecordScalar? Paragraphs(ICmObject obj, int flid, RecordField field, ExportRun run)
        {
            int target = _data.get_ObjectProp(obj.Hvo, flid);
            if (target == 0) return null;
            var text = Hvo(target) as IStText ?? throw new InvalidOperationException("The field does not hold a structured text.");
            var paragraphs = new List<Paragraph>();
            foreach (var p in text.ParagraphsOS)
            {
                if (p is IStTxtPara para)
                {
                    var converted = TsStringConversion.ToParagraph(para.Contents, para.StyleRules, null, _writingSystems);
                    foreach (var r in converted.Runs) run.Use(r.Ws);
                    KnownStyles(converted.Runs, run, obj, flid);
                    KnownStyle(converted, run, obj, flid);
                    paragraphs.Add(converted);
                    ReportStTextPara(para, run);
                }
                else run.Unexported.Add(new UnexportedValue(p.Guid, p.ClassName, "(paragraph)", "Only structured text paragraphs are exported.", null));
            }
            ReportStText(text, run);
            if (paragraphs.Count == 0) return null;
            var options = Options(Default(field), _writingSystems.DefaultVernacular);
            var scalar = RecordScalar.Rich(paragraphs, options);
            if (!MarkdownConverter.AreEquivalent(paragraphs, MarkdownConverter.Parse(scalar.Text, options).Document.Paragraphs.ToList()))
                ReportNotPreserved(obj, flid, run);
            return scalar;
        }

        /// <summary>A character style the project does not define cannot be read back (<c>fwmd.style.unknown</c>): the run is exported without it and the style is
        /// reported. A computed head word names Homograph-Number whether or not the project has it.</summary>
        void KnownStyles(IEnumerable<SIL.TextEditing.Run> runs, ExportRun export, ICmObject obj, int flid)
        {
            foreach (var r in runs)
                if (r.Props.TryGetStr(TextPropertyNumbers.NamedStyle, out var style) && style.Length > 0 && !_styles.IsCharacterStyle(style))
                {
                    r.Props.StrProps.Remove(TextPropertyNumbers.NamedStyle);
                    export.Unexported.Add(new UnexportedValue(obj.Guid, obj.ClassName, _metadata.GetFieldName(flid),
                        "The project does not define this character style, so the text is exported without it.", style));
                }
        }

        /// <summary>A paragraph with no style, or with one the project does not define, is exported in the Normal style; the undefined name is reported.</summary>
        void KnownStyle(Paragraph paragraph, ExportRun export, ICmObject obj, int flid)
        {
            string normal = new MarkdownOptions().StyleNames.Normal;
            string style = paragraph.Style;
            if (style.Length > 0 && style != normal && !_styles.IsParagraphStyle(style))
                export.Unexported.Add(new UnexportedValue(obj.Guid, obj.ClassName, _metadata.GetFieldName(flid),
                    "The project does not define this paragraph style, so the paragraph is exported in the Normal style.", style));
            if (style.Length == 0 || (style != normal && !_styles.IsParagraphStyle(style)))
            {
                paragraph.Style = normal;
                paragraph.Props.StrProps[TextPropertyNumbers.NamedStyle] = normal;
            }
        }

        // ------------------------------------------------------------------ links

        RecordScalar Link(ICmObject target, ExportRun run)
        {
            string data = DefaultObjectMapper.ExternalPathNameType + SilfwUrl.Build(target.Guid, _paths.ToolOf(target.Guid));
            var name = _paths.Describe(target.Guid)?.Name;
            var runObject = SIL.TextEditing.Run.Object(_writingSystems.DefaultAnalysis, data);
            runObject.Text = string.IsNullOrEmpty(name) ? target.Guid.ToString("D") : name!;
            return RecordScalar.RichInline(new[] { runObject }, Options(_writingSystems.DefaultAnalysis, _writingSystems.DefaultVernacular));
        }

        MarkdownOptions Options(string analysis, string vernacular) => new MarkdownOptions
        {
            WritingSystems = new Defaults(_writingSystems, analysis, vernacular),
            Styles = _styles,
            Objects = _objects,
            Paths = _paths,
            WriteFrontMatter = false,
        };

        sealed class Defaults : IWritingSystemMapper, IWritingSystemDetails, IKnownWritingSystems
        {
            readonly ProjectWritingSystems _inner;
            public Defaults(ProjectWritingSystems inner, string analysis, string vernacular) { _inner = inner; DefaultAnalysis = analysis; DefaultVernacular = vernacular; }
            public string DefaultAnalysis { get; }
            public string DefaultVernacular { get; }
            public bool IsKnown(string tag) => _inner.IsKnown(tag);
            public WritingSystemInfo? DescribeWritingSystem(string id) => _inner.DescribeWritingSystem(id);
            public IReadOnlyList<string> KnownTags => _inner.KnownTags;
        }

        /// <summary>The default mapper, with the target's current name and path so a link reads <c>[name](/path){guid=...}</c>.</summary>
        sealed class LinkMapper : IObjectMapper
        {
            readonly ProjectPathResolver _paths;
            public LinkMapper(ProjectPathResolver paths) { _paths = paths; }

            public MarkdownObject? Describe(string objData)
            {
                var link = DefaultObjectMapper.Instance.Describe(objData);
                if (link != null && link.Kind == MarkdownObjectKind.InternalLink && link.Guid != null)
                {
                    var description = _paths.Describe(link.Guid.Value);
                    if (description != null) link.Name = description.Name.Length > 0 ? description.Name : link.Guid.Value.ToString("D");
                }
                return link;
            }

            public bool TryCreateObjData(MarkdownObject target, out string objData, out string? problem)
                => DefaultObjectMapper.Instance.TryCreateObjData(target, out objData, out problem);
        }

        // ------------------------------------------------------------------ what the file leaves out

        void ReportStText(IStText text, ExportRun run)
            => ReportFields(text, _metadata.GetClassId("StText"), "|Paragraphs|", "A structured text's own property is not part of the record file.", run);

        void ReportStTextPara(IStTxtPara para, ExportRun run)
        {
            // Segments are liblcm's parse of Contents, made again whenever the contents change; they are reported only when they carry analysis.
            if (para.SegmentsOS.Any(s => s.AnalysesRS.Count > 0 || s.NotesOS.Count > 0 || s.FreeTranslation.AvailableWritingSystemIds.Length > 0
                || s.LiteralTranslation.AvailableWritingSystemIds.Length > 0))
                Report(para, _metadata.GetFieldId("StTxtPara", "Segments", false), "A paragraph's analysis data is not part of the record file.", run);
            ReportFields(para, _metadata.GetClassId("StTxtPara"), "|Contents|StyleRules|ParaProperties|Segments|", "A paragraph's analysis data is not part of the record file.", run);
        }

        void ReportFields(ICmObject obj, int classId, string skip, string reason, ExportRun run)
        {
            foreach (int flid in _managed.GetFields(classId, true, -1).OrderBy(f => f))
            {
                if (IsPlumbing(flid) || IsNamed(flid, skip) || _metadata.get_IsVirtual(flid)) continue;
                Report(obj, flid, reason, run);
            }
        }

        /// <summary>The model's own bookkeeping fields. A custom field is never plumbing, whatever it is called.</summary>
        bool IsPlumbing(int flid) => IsNamed(flid, Plumbing);

        bool IsNamed(int flid, string names) => !_managed.IsCustom(flid) && names.Contains("|" + _metadata.GetFieldName(flid) + "|");

        void ReportExcluded(ICmObject obj, IEnumerable<int> excluded, ExportRun run)
        {
            foreach (int flid in excluded.Distinct().OrderBy(f => f))
            {
                if (IsPlumbing(flid) || _metadata.get_IsVirtual(flid)) continue;
                Report(obj, flid, ExcludedReason((CellarPropertyType)_metadata.GetFieldType(flid)), run);
            }
        }

        static string ExcludedReason(CellarPropertyType type)
        {
            switch (type)
            {
                case CellarPropertyType.Time: return "A timestamp has no record value kind.";
                case CellarPropertyType.GenDate: return "A general date has no record value kind.";
                case CellarPropertyType.Binary:
                case CellarPropertyType.Image: return "Binary data has no record value kind.";
                case CellarPropertyType.OwningAtomic:
                case CellarPropertyType.OwningCollection:
                case CellarPropertyType.OwningSequence: return "Owned objects of this field are not part of the record schema.";
                default: return "The schema leaves this field out.";
            }
        }

        void Report(ICmObject obj, int flid, string reason, ExportRun run)
        {
            string? value;
            try { if (!Describe(obj.Hvo, flid, out value)) return; }
            catch (Exception e) when (e is InvalidOperationException || e is NotSupportedException || e is ArgumentException || e is NotImplementedException)
            {
                value = null;
                reason += " Its value could not be read: " + e.Message;
            }
            run.Unexported.Add(new UnexportedValue(obj.Guid, obj.ClassName, _metadata.GetFieldName(flid), reason, value));
        }

        /// <summary>The value of any field as invariant text; false when it holds nothing (the default of its type).</summary>
        bool Describe(int hvo, int flid, out string? value)
        {
            value = null;
            switch ((CellarPropertyType)_metadata.GetFieldType(flid))
            {
                case CellarPropertyType.Boolean:
                    if (!_data.get_BooleanProp(hvo, flid)) return false;
                    value = "true";
                    return true;
                case CellarPropertyType.Integer:
                case CellarPropertyType.Numeric:
                {
                    int n = _data.get_IntProp(hvo, flid);
                    if (n == 0) return false;
                    value = n.ToString(CultureInfo.InvariantCulture);
                    return true;
                }
                case CellarPropertyType.GenDate:
                {
                    var date = ReadGenDate(hvo, flid);
                    if (date.IsEmpty) return false;
                    value = (date.IsAD ? "AD " : "BC ") + date.Year.ToString(CultureInfo.InvariantCulture) + "-" + date.Month.ToString(CultureInfo.InvariantCulture)
                        + "-" + date.Day.ToString(CultureInfo.InvariantCulture) + " " + date.Precision;
                    return true;
                }
                case CellarPropertyType.Time:
                {
                    long ticks = _data.get_TimeProp(hvo, flid);
                    if (ticks == 0) return false;
                    value = DateTimeOffsetText(ticks);
                    return true;
                }
                case CellarPropertyType.Guid:
                {
                    var g = _data.get_GuidProp(hvo, flid);
                    if (g == Guid.Empty) return false;
                    value = g.ToString("D");
                    return true;
                }
                case CellarPropertyType.Unicode:
                {
                    string s = _data.get_UnicodeProp(hvo, flid) ?? string.Empty;
                    if (s.Length == 0) return false;
                    value = s;
                    return true;
                }
                case CellarPropertyType.String:
                {
                    var s = _data.get_StringProp(hvo, flid);
                    if (s == null || s.Length == 0) return false;
                    value = s.Text;
                    return true;
                }
                case CellarPropertyType.MultiUnicode:
                case CellarPropertyType.MultiString:
                {
                    var multi = _data.get_MultiStringProp(hvo, flid);
                    var parts = new List<string>();
                    for (int i = 0; i < multi.StringCount; i++)
                    {
                        var s = multi.GetStringFromIndex(i, out int ws);
                        if (s != null && s.Length > 0) parts.Add(_writingSystems.GetId(ws) + "=" + s.Text);
                    }
                    if (parts.Count == 0) return false;
                    parts.Sort(StringComparer.Ordinal);
                    value = string.Join("; ", parts);
                    return true;
                }
                case CellarPropertyType.Binary:
                case CellarPropertyType.Image:
                {
                    var bytes = BinaryValue(hvo, flid);
                    if (bytes.Length == 0) return false;
                    if (bytes.Length <= 1024) value = Convert.ToBase64String(bytes);
                    else
                    {
                        using (var sha = System.Security.Cryptography.SHA256.Create())
                            value = bytes.Length.ToString(CultureInfo.InvariantCulture) + " bytes, sha256 "
                                + string.Concat(sha.ComputeHash(bytes).Select(b => b.ToString("x2", CultureInfo.InvariantCulture)));
                    }
                    return true;
                }
                case CellarPropertyType.ReferenceAtom:
                case CellarPropertyType.OwningAtomic:
                {
                    int target = _data.get_ObjectProp(hvo, flid);
                    if (target == 0) return false;
                    value = Hvo(target).Guid.ToString("D");
                    return true;
                }
                case CellarPropertyType.ReferenceCollection:
                case CellarPropertyType.ReferenceSequence:
                case CellarPropertyType.OwningCollection:
                case CellarPropertyType.OwningSequence:
                {
                    var items = Vector(hvo, flid);
                    if (items.Count == 0) return false;
                    var guids = items.Select(i => i.Guid);
                    var type = (CellarPropertyType)_metadata.GetFieldType(flid);
                    if (type == CellarPropertyType.ReferenceCollection || type == CellarPropertyType.OwningCollection) guids = guids.OrderBy(g => g);
                    value = string.Join(",", guids.Select(g => g.ToString("D")));
                    return true;
                }
                default:
                    throw new NotSupportedException("Unsupported metadata type " + (CellarPropertyType)_metadata.GetFieldType(flid) + ".");
            }
        }

        /// <summary>A general date. liblcm answers <c>get_IntProp</c> for a custom field only; a model field is read through its class's property of the same name.</summary>
        GenDate ReadGenDate(int hvo, int flid)
        {
            if (_managed.IsCustom(flid)) return new GenDate(_data.get_IntProp(hvo, flid));
            var obj = Hvo(hvo);
            var property = obj.GetType().GetProperty(_metadata.GetFieldName(flid));
            if (property != null && property.PropertyType == typeof(GenDate)) return (GenDate)property.GetValue(obj, null)!;
            throw new NotSupportedException("Cannot read the general date " + _metadata.GetFieldName(flid) + " of " + obj.ClassName + ".");
        }

        byte[] BinaryValue(int hvo, int flid)
        {
            int length;
            // liblcm's BinaryPropRgb throws a NullReferenceException for a binary field that was never set on this object, and offers no test for that case that is safe for a
            // value not yet loaded; the catch covers this one call only, and a test pins the never-set case.
            try { _data.BinaryPropRgb(hvo, flid, ArrayPtr.Null, 0, out length); }
            catch (NullReferenceException) { return new byte[0]; }
            if (length == 0) return new byte[0];
            using (var buffer = MarshalEx.ArrayToNative<byte>(length))
            {
                _data.BinaryPropRgb(hvo, flid, buffer, length, out int read);
                return MarshalEx.NativeToArray<byte>(buffer, read);
            }
        }

        static string DateTimeOffsetText(long ticks)
        {
            try { return SilTime.ConvertFromSilTime(ticks).ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture); }
            catch (ArgumentOutOfRangeException) { return ticks.ToString(CultureInfo.InvariantCulture); }
        }
    }
}
