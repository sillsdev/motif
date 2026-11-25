namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCodes
{
    public const string SelectionReaderCancelled = "selection-reader.cancelled";
    public const string SelectionReaderDisposed = "selection-reader.disposed";
    public const string SelectionReaderBusy = "selection-reader.busy";
    public const string SelectionReaderInvalid = "selection-reader.invalid";
    public const string SelectionReaderNoBaseline = "selection-reader.no-baseline";
    public const string SelectionReaderRangeInvalid = "selection-reader.range-invalid";
    public const string SelectionReaderOwnershipLimit = "selection-reader.ownership-limit";
    public const string SelectionReaderProjectPathRequired = "selection-reader.project-path-required";
    /// <summary>The window could not open the Selection reader.</summary>
    public const string SelectionReaderOpenFailed = "selection-reader.open-failed";
    /// <summary>The saved texts changed under the reader since it was opened.</summary>
    public const string SelectionReaderPresentationChanged = "selection-reader.presentation-changed";
}
