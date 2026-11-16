using System;
using System.Globalization;
using System.Collections.Generic;
using System.Linq;

namespace SIL.TextEditing.Markdown.Records
{
    /// <summary>What a file's <c>root:</c> names, against its record kind: the record itself, an owned object in it, or a field of one (ADR 0032). The
    /// first <see cref="RecordKind.PathSegments"/> segments name the record; each segment after them is a field of an owned object, followed by a 1-based
    /// index when the field is a sequence; the fragment names a field and, after a dot, one writing system.</summary>
    internal sealed class RecordRoot
    {
        /// <summary>The type of the object the path names (whose fields <c>record-data:</c> holds).</summary>
        public RecordObjectType Type = null!;

        /// <summary>True when the path names the record itself, so the kind's body field is the body.</summary>
        public bool IsRecord;

        /// <summary>The fragment's field, or null.</summary>
        public RecordField? Field;

        /// <summary>The fragment's writing system, or null.</summary>
        public string? WritingSystem;

        /// <summary>The field whose value is the body, or null: the kind's body field for a whole record; for a fragment, its field when that is one rich
        /// value (a field in one writing system, or one alternative).</summary>
        public RecordField? Body;

        /// <summary>Resolves <paramref name="root"/>; null (with <paramref name="error"/>) when it does not name anything the schema has.</summary>
        public static RecordRoot? Resolve(RecordKind kind, string? root, out string? error, string? objectClass = null)
            => Resolve(kind, root, out error, out _, objectClass);

        public static RecordRoot? Resolve(RecordKind kind, string? root, out string? error, out bool classError, string? objectClass)
        {
            classError = false;
            error = null;
            var result = new RecordRoot { Type = kind.Root, IsRecord = true };
            if (root != null)
            {
                var path = ProjectPath.Parse(root);
                if (path == null) { error = "'" + root + "' is not a project path."; return null; }
                if (path.Segments.Count < kind.PathSegments)
                {
                    error = "A " + kind.Name + " record's path has " + kind.PathSegments + " segments; '" + root + "' has " + path.Segments.Count + ".";
                    return null;
                }
                string folder = kind.Name.Split('/')[0];
                var first = path.Segments[0];
                if (first.Name != folder || first.Homograph != null || first.GuidSuffix != null)
                {
                    error = "A " + kind.Name + " record's path starts with /" + folder + ".";
                    return null;
                }
                var pending = new Stack<(RecordObjectType Type, int Index)>();
                var visited = new HashSet<(RecordObjectType Type, int Index)>();
                var candidates = new HashSet<RecordObjectType>();
                int furthestFailure = -1;
                string? pathError = null;
                void FailedAt(int index, string message)
                {
                    if (index <= furthestFailure) return;
                    furthestFailure = index;
                    pathError = message;
                }
                pending.Push((kind.Root, kind.PathSegments));
                while (pending.Count > 0)
                {
                    var state = pending.Pop();
                    if (!visited.Add(state)) continue;
                    if (state.Index == path.Segments.Count) { candidates.Add(state.Type); continue; }
                    var segment = path.Segments[state.Index];
                    var field = segment.Homograph == null && segment.GuidSuffix == null ? state.Type.Field(segment.Name) : null;
                    if (field == null || (field.Kind != RecordFieldKind.Object && (!field.IsList || field.Item!.Kind != RecordFieldKind.Object)))
                    {
                        FailedAt(state.Index, "In '" + root + "', " + segment + " is not an owned object of " + state.Type.Name + ".");
                        continue;
                    }
                    int next = state.Index + 1;
                    if (field.IsList)
                    {
                        if (next >= path.Segments.Count || !IsIndex(path.Segments[next]))
                        {
                            string listKind = field.Kind == RecordFieldKind.Collection ? "collection" : "sequence";
                            FailedAt(state.Index, "In '" + root + "', " + segment.Name + " is a " + listKind + ", so a 1-based index follows it.");
                            continue;
                        }
                        next++;
                        field = field.Item!;
                    }
                    for (int target = field.ObjectTypes.Count - 1; target >= 0; target--)
                        pending.Push((field.ObjectTypes[target], next));
                }
                var type = candidates.Count == 1 ? candidates.Single() : candidates.FirstOrDefault(t => t.Name == objectClass);
                if (type == null)
                {
                    classError = candidates.Count > 0;
                    error = candidates.Count == 0 ? pathError
                        : "The partial root '" + root + "' requires an allowed class in record-data: " + string.Join(", ", candidates.Select(t => t.Name)) + ".";
                    return null;
                }
                result.IsRecord = path.Segments.Count == kind.PathSegments;
                result.Type = type;
                if (path.Field != null)
                {
                    result.Field = type.Field(path.Field);
                    if (result.Field == null) { error = "In '" + root + "', " + path.Field + " is not a field of " + type.Name + "."; return null; }
                    if (path.FieldWritingSystem != null && !result.Field.MultiWritingSystem)
                    {
                        error = "In '" + root + "', " + path.Field + " is not a field per writing system.";
                        return null;
                    }
                    result.WritingSystem = path.FieldWritingSystem;
                    if (result.Field.Kind == RecordFieldKind.Rich && (result.WritingSystem != null || !result.Field.MultiWritingSystem)) result.Body = result.Field;
                    return result;
                }
            }
            if (result.IsRecord) result.Body = kind.BodyField;
            return result;
        }

        static bool IsIndex(ProjectPathSegment segment)
            => segment.Homograph == null && segment.GuidSuffix == null && segment.Name.Length > 0 && segment.Name[0] != '0'
               && int.TryParse(segment.Name, NumberStyles.None, CultureInfo.InvariantCulture, out int n) && n >= 1;
    }
}
