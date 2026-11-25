namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCodes
{
    public const string ProjectBackupUnreadable = "project.backup-unreadable";
    public const string ProjectBusy = "project.busy";
    public const string ProjectStartupBusy = "project.startup-busy";
    public const string ProjectInUse = "project.in-use";
    public const string ProjectInvalid = "project.invalid";
    public const string ProjectNotFound = "project.not-found";
    public const string ProjectOperationIo = "project.operation-io";
    public const string ProjectStoreIo = "project.store-io";
    public const string ProjectTagInvalid = "project.tag-invalid";
    public const string ProjectTagTaken = "project.tag-taken";
    public const string ProjectTagUnavailable = "project.tag-unavailable";
    public const string ProjectUnloadable = "project.unloadable";
    /// <summary>Waiting for the window's one project gate was cancelled before the command started.</summary>
    public const string ProjectWaitCancelled = "project.wait-cancelled";
    /// <summary>A project-level refusal with no more specific code.</summary>
    public const string ProjectRefused = "project.refused";
    /// <summary>FieldWorks is saving the project, so the command was refused as busy.</summary>
    public const string ProjectSaving = "project.saving";
    /// <summary>The project's saved data could not be read.</summary>
    public const string ProjectUnreadable = "project.unreadable";
    /// <summary>The project lacks a writing system the command needs.</summary>
    public const string ProjectWritingSystemMissing = "project.writing-system-missing";
}
