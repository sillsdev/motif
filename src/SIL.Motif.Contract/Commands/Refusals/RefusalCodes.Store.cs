namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCodes
{
    /// <summary><c>store backup</c> found no Motif store beside the project.</summary>
    public const string StoreBackupNoStore = "store.backup-no-store";
    /// <summary><c>store backup</c> was given a destination folder that already holds files; nothing was written.</summary>
    public const string StoreBackupDestinationExists = "store.backup-destination-exists";
    /// <summary><c>store restore</c> was given a folder with no readable backup receipt, or a receipt that names a file the folder lacks.</summary>
    public const string StoreRestoreInvalid = "store.restore-invalid";
    /// <summary><c>store restore</c> was given a backup whose store schema is newer than this build understands.</summary>
    public const string StoreRestoreNewerSchema = "store.restore-newer-schema";
    /// <summary><c>store restore</c> found a backed-up file whose sha256 differs from its receipt; nothing was changed.</summary>
    public const string StoreRestoreChecksumMismatch = "store.restore-checksum-mismatch";
    /// <summary><c>store restore</c> was given the backup of a different project's store; nothing was changed.</summary>
    public const string StoreRestoreOtherProject = "store.restore-other-project";
    /// <summary><c>store backup</c> was given a destination inside the store's own files or a folder Motif owns, such as its trial exports; nothing was written.</summary>
    public const string StoreBackupDestinationOwned = "store.backup-destination-owned";
    public const string StoreInconsistent = "store.inconsistent";
    /// <summary>
    /// The project's Motif store was made by another version of Motif and is refused rather than migrated.
    /// Its facts carry the store's own path under <c>storePath</c>, so the window can name the file and,
    /// before 1.0, offer to delete it so Motif recreates it.
    /// </summary>
    public const string StoreOtherVersion = "store.other-version";
    /// <summary>
    /// The Motif store beside the project is registered to a different project, as when a project is copied
    /// or renamed together with its store. Its facts carry <c>storePath</c> and <c>registeredProjectPath</c>.
    /// </summary>
    public const string StoreOtherProject = "store.other-project";
    /// <summary><c>store relocate</c> was run without the confirmation phrase.</summary>
    public const string StoreRelocateConfirmationRequired = "store.relocate-confirmation-required";
    /// <summary><c>store relocate</c> found no project GUID to show the old and new paths hold one project.</summary>
    public const string StoreRelocateIdentityUnproven = "store.relocate-identity-unproven";
    /// <summary><c>store relocate</c> found no Motif store beside the project.</summary>
    public const string StoreRelocateNoStore = "store.relocate-no-store";
    public const string StoreUnsupported = "store.unsupported";
}
