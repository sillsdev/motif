namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCodes
{
    /// <summary>An import plan declines its whole file, so nothing in it was lowered to operations.</summary>
    public const string RecordImportPlanRefused = "record-import.plan-refused";
    /// <summary>No Runner kind covers the verb a record-file change needs on this class and field.</summary>
    public const string RecordImportFieldUncovered = "record-import.field-uncovered";
    /// <summary>A kind covers the field, but no payload shape is authored for this change.</summary>
    public const string RecordImportShapeUnsupported = "record-import.shape-unsupported";
    /// <summary>The project's value is no longer the one the file started from.</summary>
    public const string RecordImportValueStale = "record-import.value-stale";
    /// <summary>The change names an object, list member or text the project cannot take as written.</summary>
    public const string RecordImportNotExecutable = "record-import.not-executable";
    /// <summary>The importer declines this one change (its kind says why); the rest of the file is still planned.</summary>
    public const string RecordImportChangeRefused = "record-import.change-refused";
    /// <summary>The record file cannot be read from the path given.</summary>
    public const string RecordImportFileUnreadable = "record-import.file-unreadable";
    /// <summary>The project changed between the preview and the stage, so what would be staged is no longer what was previewed.</summary>
    public const string RecordImportChangedSincePreview = "record-import.changed-since-preview";
    /// <summary>Strict was asked for without a Draft name, so there is no stage for it to govern.</summary>
    public const string RecordImportStrictNeedsDraft = "record-import.strict-needs-draft";
    /// <summary>Strict mode refused to stage an import in which some change was refused.</summary>
    public const string RecordImportStrictRefused = "record-import.strict-refused";
    /// <summary>Staging was asked for, but the file lowers to no operation, so no Draft was made.</summary>
    public const string RecordImportNothingToStage = "record-import.nothing-to-stage";
    /// <summary>The file would create a record, and a record file cannot create this kind (a list item, a phonological rule), or it plans the create but no operation makes it (a complex form, a notebook record).</summary>
    public const string RecordImportCreateKindNotCreatable = "record-import.create-kind-not-creatable";
    /// <summary>A new record must be a whole file, but the file's root: names part of a record.</summary>
    public const string RecordImportCreatePartial = "record-import.create-partial";
    /// <summary>The file's base: comes from a record the project no longer has.</summary>
    public const string RecordImportRecordGone = "record-import.record-gone";
    /// <summary>A new object's GUID is the nil or all-ones GUID, not RFC 4122 variant, or not version 4, 5 or 7.</summary>
    public const string RecordImportGuidInvalid = "record-import.guid-invalid";
    /// <summary>Another open Draft or Proposal already creates the new record's GUID.</summary>
    public const string RecordImportGuidPending = "record-import.guid-pending";
    /// <summary>The Drafts and Proposals could not be read, so whether one already creates the new record's GUID is unknown.</summary>
    public const string RecordImportGuidPendingUnchecked = "record-import.guid-pending-unchecked";
    /// <summary>The new record's GUID belonged to an object the project's home has deleted.</summary>
    public const string RecordImportGuidRetired = "record-import.guid-retired";
    /// <summary>A new entry lacks a part FieldWorks makes with one (a lexeme form with a morph type, a sense with a gloss and grammatical info, or a variant link), or its parts do not fit together.</summary>
    public const string RecordImportCreateShape = "record-import.create-shape";
    /// <summary>A new record's file sets a field FieldWorks computes for it.</summary>
    public const string RecordImportCreateComputedField = "record-import.create-computed-field";
    /// <summary>A new notebook record's file sets a field only Motif's judgment commands write.</summary>
    public const string RecordImportCreateReservedField = "record-import.create-reserved-field";
    /// <summary>The page asked for is not a page number from 1.</summary>
    public const string RecordImportInvalidPage = "record-import.invalid-page";
}
