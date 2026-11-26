namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCodes
{
    /// <summary>The request names neither one record nor all records of a kind, or both, or an unusable value.</summary>
    public const string RecordExportInvalidRequest = "record-export.invalid-request";
    /// <summary>The project has no such record, or no record kind of that name.</summary>
    public const string RecordExportNotFound = "record-export.not-found";
    /// <summary>The record file cannot be written at the path given, or a file is already there.</summary>
    public const string RecordExportOutputUnwritable = "record-export.output-unwritable";
}
