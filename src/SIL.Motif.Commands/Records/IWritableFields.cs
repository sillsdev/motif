using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Operations;

namespace SIL.Motif.Commands.Records;

/// <summary>A read-only lookup of Runner kinds for fields in a generated record schema.</summary>
public interface IWritableFields
{
    /// <summary>
    /// Resolves an exact schema object type and field key, such as <c>LexEntry</c> and <c>allomorphs</c>.
    /// For owning edits, pass the concrete child type. For ordered edits, pass the requested placement.
    /// A restricted shape must be established from the requested change. The Runner checks the payload,
    /// the anchor identities, and the project's state during execution.
    /// </summary>
    WritableFieldResolution Resolve(string className, string field, RecordWriteVerb verb,
        string? childClass = null, Placement? placement = null, FieldWriteShape shape = FieldWriteShape.Unspecified);
}

/// <summary>Why the catalog lookup cannot supply a Runner kind.</summary>
public enum UncoveredFieldReason
{
    UnknownField,
    VerbNotExpressible,
    ComputedField,
    CustomField,
    NoOperationKind,
    AmbiguousOperationKind,
    UnsupportedOwnerClass,
    MissingChildClass,
    UnsupportedChildClass,
    UnsupportedPlacement,
    MissingPlacement,
    UnsupportedShape,
    NotUserEditable,
}

/// <summary>The outcome of a writable-field lookup.</summary>
public abstract record WritableFieldResolution
{
    private WritableFieldResolution() { }

    public sealed record Covered(string Kind) : WritableFieldResolution;
    public sealed record Uncovered(UncoveredFieldReason Reason, string? ManifestClassification = null) : WritableFieldResolution;
}
