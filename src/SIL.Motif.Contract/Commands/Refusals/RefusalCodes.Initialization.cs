namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCodes
{
    /// <summary>The project has no analysis writing system to initialize with.</summary>
    public const string InitializationAnalysisWritingSystemRequired = "initialization.analysis-writing-system-required";
    /// <summary>Initialization was cancelled before the field definition changed.</summary>
    public const string InitializationCancelled = "initialization.cancelled";
    /// <summary>Initialization was asked for without the confirmation phrase.</summary>
    public const string InitializationConfirmationRequired = "initialization.confirmation-required";
    /// <summary>Initialization did not save, so the project is unchanged.</summary>
    public const string InitializationNotSaved = "initialization.not-saved";
    /// <summary>Initialization may or may not have saved; the project needs inspection.</summary>
    public const string InitializationOutcomeUnknown = "initialization.outcome-unknown";
    /// <summary>A private recovery copy could not be made, so the project was not changed.</summary>
    public const string InitializationRecoveryCopyFailed = "initialization.recovery-copy-failed";
}
