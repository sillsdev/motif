namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCodes
{
    public const string SelectionDefaultMissing = "selection.default-missing";
    public const string SelectionEmpty = "selection.empty";
    public const string SelectionInvalid = "selection.invalid";
    public const string SelectionNotFound = "selection.not-found";
    public const string SelectionParsimonyNameIsDefault = "selection.parsimony-name-is-default";
    public const string SelectionTextNotFound = "selection.text-not-found";
    /// <summary>A Selection limit policy is missing or not valid.</summary>
    public const string SelectionInvalidLimits = "selection.invalid-limits";
    /// <summary>The retry source is not a Baseline ParseTime Assessment.</summary>
    public const string SelectionRetrySourceInvalid = "selection.retry-source-invalid";
    /// <summary>The retry source Assessment belongs to a different Baseline.</summary>
    public const string SelectionRetrySourceMismatch = "selection.retry-source-mismatch";
    /// <summary>The retry source Assessment is not in the store.</summary>
    public const string SelectionRetrySourceNotFound = "selection.retry-source-not-found";
    /// <summary>The retry source Assessment belongs to a different project.</summary>
    public const string SelectionRetrySourceProjectMismatch = "selection.retry-source-project-mismatch";
    /// <summary>A retry Selection was asked for without a source Assessment id.</summary>
    public const string SelectionRetrySourceRequired = "selection.retry-source-required";
    /// <summary>A source Assessment id was given without a retry kind.</summary>
    public const string SelectionRetrySourceWithoutRetry = "selection.retry-source-without-retry";
    /// <summary>The retry parser-work threshold is invalid.</summary>
    public const string SelectionRetryWorkInvalid = "selection.retry-work-invalid";
    /// <summary>The saved Selection changed since the window last read it.</summary>
    public const string SelectionRevisionConflict = "selection.revision-conflict";
}
