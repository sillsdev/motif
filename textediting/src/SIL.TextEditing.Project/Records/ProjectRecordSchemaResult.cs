using System;
using System.Collections.Generic;
using System.Linq;
using SIL.LCModel.Infrastructure;
using SIL.TextEditing.Markdown.Records;

namespace SIL.TextEditing.Project
{
    /// <summary>A generated schema and custom fields that it cannot or should not represent.</summary>
    public sealed class ProjectRecordSchemaResult
    {
        readonly Dictionary<string, Dictionary<string, (string DeclaringClass, string Field, bool IsCustom)>> _fieldNames;

        internal ProjectRecordSchemaResult(RecordSchema schema, OmittedCustomField[] omitted, SchemaBindings bindings,
            IFwMetaDataCacheManaged metadata)
        {
            Schema = schema;
            Bindings = bindings;
            OmittedCustomFields = System.Array.AsReadOnly(omitted);
            _fieldNames = bindings.Keys.ToDictionary(type => type.Key, type => type.Value.ToDictionary(field => field.Key,
                field => (metadata.GetOwnClsName(field.Value), metadata.GetFieldName(field.Value), metadata.IsCustom(field.Value)),
                StringComparer.Ordinal), StringComparer.Ordinal);
        }

        /// <summary>Which metadata field each schema key of each object type reads, and the fields the type leaves out (for export).</summary>
        internal SchemaBindings Bindings { get; }

        /// <summary>The detached schema built from the overlay and supported custom fields.</summary>
        public RecordSchema Schema { get; }

        /// <summary>The stable model names for a schema key, including the declaring class of an inherited field.
        /// The binding stays available after the project closes. A key outside the schema has no binding.</summary>
        public (string DeclaringClass, string Field, bool IsCustom)? FieldBinding(string objectType, string key)
            => _fieldNames.TryGetValue(objectType, out var fields) && fields.TryGetValue(key, out var binding) ? binding : null;

        /// <summary>Each omitted custom field, once per declaring class and field name.</summary>
        public IReadOnlyList<OmittedCustomField> OmittedCustomFields { get; }
    }

    /// <summary>For each concrete object type: schema key to field id, and the ids of the fields the overlay or the generator left out. The ids are
    /// those of the cache the schema was generated from.</summary>
    internal sealed class SchemaBindings
    {
        public Dictionary<string, Dictionary<string, int>> Keys { get; } = new Dictionary<string, Dictionary<string, int>>(System.StringComparer.Ordinal);
        public Dictionary<string, List<int>> Excluded { get; } = new Dictionary<string, List<int>>(System.StringComparer.Ordinal);
    }

    /// <summary>A custom field absent from the generated schema and the reason for its omission.</summary>
    public sealed class OmittedCustomField
    {
        internal OmittedCustomField(string declaringClass, string field, string reason)
        {
            Class = declaringClass;
            Field = field;
            Reason = reason;
        }

        /// <summary>The metadata class declaring the field.</summary>
        public string Class { get; }
        /// <summary>The metadata field name.</summary>
        public string Field { get; }
        /// <summary>Why the field was omitted.</summary>
        public string Reason { get; }
    }
}
