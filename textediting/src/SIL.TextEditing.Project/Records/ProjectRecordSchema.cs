using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using SIL.LCModel;
using SIL.LCModel.Core.Cellar;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.Infrastructure;
using SIL.TextEditing.Markdown.Records;

namespace SIL.TextEditing.Project
{
    /// <summary>Builds a detached record schema from a loaded project's metadata and an editorial overlay.</summary>
    public static class ProjectRecordSchema
    {
        /// <summary>The shipped XML overlay for lexicon entries.</summary>
        public static string LexiconEntryOverlay
        {
            get
            {
                using var stream = typeof(ProjectRecordSchema).Assembly.GetManifestResourceStream("SIL.TextEditing.Project.Records.LexiconEntry.xml")
                    ?? throw new InvalidOperationException("The lexicon entry overlay is missing.");
                using var reader = new StreamReader(stream);
                return reader.ReadToEnd();
            }
        }

        /// <summary>Combines metadata with an XML overlay. The root <c>overlay</c> has <c>version="1"</c> and an <c>id</c>.
        /// Each <c>class name="..."</c> lists declared metadata fields in order as <c>field name="..." key="..."</c>,
        /// or excludes them with <c>exclude name="..."</c>. Decisions on a base class apply to its subclasses.
        /// A field can specify <c>notation="..."</c>, or <c>blocks="tables,lists,headings"</c> on a paragraph field;
        /// an empty blocks attribute allows no blocks. Each <c>kind</c> gives <c>name</c>, <c>root</c>,
        /// <c>pathSegments</c> and optional <c>body</c>. Every metadata field of each declared class must be accounted for,
        /// and each class must be reachable from a kind root through owned-object fields or be a base of a reachable class.
        /// The metadata's analysis/vernacular hint determines the default writing system; an absent hint uses analysis.
        /// Ordered analysis-vernacular and vernacular-analysis hints cannot be represented and are rejected.
        /// Timestamps, binary and general dates have no record-schema representation and must be excluded.</summary>
        /// <exception cref="ArgumentException">An overlay declaration is invalid or leaves a metadata field unmentioned.</exception>
        /// <exception cref="NotSupportedException">An included metadata type or writing-system hint cannot be represented.</exception>
        public static RecordSchema Generate(LcmCache cache, string overlayXml)
        {
            if (cache == null) throw new ArgumentNullException(nameof(cache));
            if (overlayXml == null) throw new ArgumentNullException(nameof(overlayXml));
            var metadata = (IFwMetaDataCache)cache.MetaDataCacheAccessor;
            var managed = (IFwMetaDataCacheManaged)metadata;
            var root = XDocument.Parse(overlayXml).Root ?? throw new ArgumentException("The overlay has no root.", nameof(overlayXml));
            if (root.Name != "overlay" || (string?)root.Attribute("version") != "1") throw new ArgumentException("Expected overlay version 1.");
            if (root.Elements().Any(e => e.Name != "class" && e.Name != "kind") || !root.Elements("kind").Any())
                throw new ArgumentException("An overlay contains class declarations and at least one kind.");
            var classes = root.Elements("class").ToDictionary(e => Required(e, "name"), StringComparer.Ordinal);
            foreach (string name in classes.Keys)
                if (!managed.ClassExists(name)) throw new ArgumentException("Unknown metadata class " + name + ".");
            var ids = classes.Keys.ToDictionary(n => n, n => metadata.GetClassId(n), StringComparer.Ordinal);
            foreach (var kind in root.Elements("kind"))
            {
                string rootName = Required(kind, "root");
                if (rootName == "CmObject" || !classes.ContainsKey(rootName))
                    throw new ArgumentException("Unknown kind root " + rootName + ".");
            }
            var builder = new RecordSchemaBuilder(Required(root, "id"));
            foreach (var pair in classes)
            {
                if (pair.Key == "CmObject") continue;
                int id = ids[pair.Key];
                var type = builder.ObjectType(pair.Key);
                var chain = new Stack<int>();
                int current = id;
                while (true)
                {
                    chain.Push(current);
                    if (metadata.GetClassName(current) == "CmObject") break;
                    current = metadata.GetBaseClsId(current);
                }
                var decisions = new HashSet<int>();
                foreach (int ancestor in chain)
                {
                    string className = metadata.GetClassName(ancestor);
                    if (!classes.TryGetValue(className, out var declaration)) throw new ArgumentException("Missing overlay class " + className + ".");
                    foreach (var entry in declaration.Elements())
                    {
                        if (entry.Name != "field" && entry.Name != "exclude") throw new ArgumentException("Unknown overlay element " + entry.Name + ".");
                        string fieldName = Required(entry, "name");
                        if (!managed.FieldExists(ancestor, fieldName, false)) throw new ArgumentException("Unknown declared field " + className + "." + fieldName + ".");
                        int field = metadata.GetFieldId2(ancestor, fieldName, false);
                        if (field == 0 || metadata.GetOwnClsId(field) != ancestor) throw new ArgumentException("Unknown declared field " + className + "." + fieldName + ".");
                        if (!decisions.Add(field)) throw new ArgumentException("Duplicate field decision " + className + "." + fieldName + ".");
                        if (entry.Name == "field") Add(type, entry, field, metadata, managed, classes);
                    }
                }
                foreach (int field in managed.GetFields(id, true, -1))
                    if (!decisions.Contains(field)) throw new ArgumentException("Unmentioned metadata field " + metadata.GetOwnClsName(field) + "." + metadata.GetFieldName(field) + " on " + pair.Key + ".");
            }
            foreach (var kind in root.Elements("kind"))
                builder.Kind(Required(kind, "name"), Required(kind, "root"), (int?)kind.Attribute("pathSegments") ?? throw new ArgumentException("A kind needs pathSegments."), (string?)kind.Attribute("body"));
            var schema = builder.Build();
            CheckReachability(schema, classes, metadata);
            return schema;
        }

        static void CheckReachability(RecordSchema schema, Dictionary<string, XElement> classes, IFwMetaDataCache metadata)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var reachableClasses = new HashSet<string>(StringComparer.Ordinal);
            var pending = new Stack<RecordObjectType>(schema.Kinds.Select(k => k.Root));
            while (pending.Count > 0)
            {
                var type = pending.Pop();
                if (!seen.Add(type.Name)) continue;
                int id = metadata.GetClassId(type.Name);
                while (true)
                {
                    string name = metadata.GetClassName(id);
                    reachableClasses.Add(name);
                    if (name == "CmObject") break;
                    id = metadata.GetBaseClsId(id);
                }
                foreach (var field in type.Fields)
                {
                    var value = field.IsList ? field.Item! : field;
                    foreach (var target in value.ObjectTypes) pending.Push(target);
                }
            }
            foreach (string name in classes.Keys)
                if (!reachableClasses.Contains(name)) throw new ArgumentException("Unreachable overlay class " + name + ".");
        }

        static string Required(XElement e, string name) => (string?)e.Attribute(name) is string value && value.Length > 0
            ? value : throw new ArgumentException("Missing " + name + " on " + e.Name + ".");

        static void Add(RecordObjectTypeBuilder b, XElement entry, int field, IFwMetaDataCache m,
            IFwMetaDataCacheManaged managed, Dictionary<string, XElement> classes)
        {
            string key = Required(entry, "key");
            string? notation = (string?)entry.Attribute("notation");
            string? blockText = (string?)entry.Attribute("blocks");
            var t = (CellarPropertyType)m.GetFieldType(field);
            bool multi = t == CellarPropertyType.MultiUnicode || t == CellarPropertyType.MultiString;
            bool isText = t == CellarPropertyType.Unicode || t == CellarPropertyType.MultiUnicode;
            bool isRich = t == CellarPropertyType.String || t == CellarPropertyType.MultiString;
            bool paragraph = t == CellarPropertyType.OwningAtomic && m.GetDstClsName(field) == "StText";
            if (notation != null && !isText && !isRich) throw new ArgumentException("Notation requires a string field: " + key);
            if (blockText != null && !paragraph) throw new ArgumentException("Blocks require a paragraph field: " + key);
            var ws = (isText || isRich || paragraph) && !multi
                ? WritingSystem(m.GetFieldWs(field), key) : RecordWritingSystem.Analysis;
            if (notation != null) { if (multi) b.MultiNotation(key, notation); else b.Notation(key, notation, ws); }
            else if (isText) { if (multi) b.MultiText(key); else b.Text(key, ws); }
            else if (isRich) { if (multi) b.MultiRich(key); else b.Rich(key, ws: ws); }
            else if (paragraph) b.Rich(key, paragraphs: true, ws: ws, blocks: Blocks(blockText));
            else if (t == CellarPropertyType.Integer) b.Integer(key);
            else if (t == CellarPropertyType.Boolean) b.Boolean(key);
            else if (t == CellarPropertyType.Guid) b.Guid(key);
            else if (t == CellarPropertyType.ReferenceAtom) b.Reference(key);
            else if (t == CellarPropertyType.ReferenceSequence || t == CellarPropertyType.ReferenceCollection)
            {
                b.References(key);
                if (t == CellarPropertyType.ReferenceCollection) b.AsCollection(key);
            }
            else if (t == CellarPropertyType.OwningAtomic || t == CellarPropertyType.OwningSequence || t == CellarPropertyType.OwningCollection)
            {
                int dest = m.GetDstClsId(field);
                var types = new[] { dest }.Concat(managed.GetAllSubclasses(dest)).Distinct().Where(i => !m.GetAbstract(i))
                    .Select(m.GetClassName).OrderBy(n => n, StringComparer.Ordinal).ToArray();
                if (types.Length == 0 || types.Any(n => !classes.ContainsKey(n))) throw new ArgumentException("Missing concrete overlay class for " + key + ".");
                if (t == CellarPropertyType.OwningAtomic) b.Object(key, types);
                else { b.Objects(key, types); if (t == CellarPropertyType.OwningCollection) b.AsCollection(key); }
            }
            else throw new NotSupportedException("Unsupported metadata type " + t + " for " + key + ".");
            if (m.get_IsVirtual(field)) b.Computed(key);
        }

        static RecordWritingSystem WritingSystem(int hint, string key)
        {
            switch (hint)
            {
                case 0: case -1: case -3: return RecordWritingSystem.Analysis;
                case -2: case -4: return RecordWritingSystem.Vernacular;
                default: throw new NotSupportedException("Unsupported writing-system hint " + hint + " for " + key + ".");
            }
        }

        static BlockFeatures? Blocks(string? text)
        {
            if (text == null) return null;
            var result = BlockFeatures.None;
            foreach (string part in text.Split(','))
                switch (part.Trim())
                {
                    case "": break;
                    case "tables": result |= BlockFeatures.Tables; break;
                    case "lists": result |= BlockFeatures.Lists; break;
                    case "headings": result |= BlockFeatures.Headings; break;
                    default: throw new ArgumentException("Unknown block kind " + part + ".");
                }
            return result;
        }
    }
}
