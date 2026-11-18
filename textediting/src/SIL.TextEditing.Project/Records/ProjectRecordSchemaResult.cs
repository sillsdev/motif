using System.Collections.Generic;
using SIL.TextEditing.Markdown.Records;

namespace SIL.TextEditing.Project
{
    /// <summary>A generated schema and custom fields that it cannot or should not represent.</summary>
    public sealed class ProjectRecordSchemaResult
    {
        internal ProjectRecordSchemaResult(RecordSchema schema, OmittedCustomField[] omitted)
        {
            Schema = schema;
            OmittedCustomFields = System.Array.AsReadOnly(omitted);
        }

        /// <summary>The detached schema built from the overlay and supported custom fields.</summary>
        public RecordSchema Schema { get; }

        /// <summary>Each omitted custom field, once per declaring class and field name.</summary>
        public IReadOnlyList<OmittedCustomField> OmittedCustomFields { get; }
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
