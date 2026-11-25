namespace SIL.Motif.Contract.Responses;

/// <summary>One file in a Motif backup.</summary>
/// <param name="Role">What the file is: <c>store</c>, <c>config</c>, <c>upgrade-report</c> or <c>trial-export</c>.</param>
/// <param name="Name">The file's path inside the backup folder, with forward slashes.</param>
/// <param name="Path">The file's full path.</param>
/// <param name="Bytes">The file's size in bytes.</param>
/// <param name="Sha256">The file's lowercase hexadecimal SHA-256.</param>
public sealed record StoreBackupFile(string Role, string Name, string Path, long Bytes, string Sha256);

/// <summary>
/// The receipt for a Motif backup. It is also the backup's <c>motif-backup.json</c>, which a restore checks every
/// file against.
/// </summary>
/// <param name="BackupDirectory">The folder holding the backup.</param>
/// <param name="ReceiptPath">The receipt file inside that folder.</param>
/// <param name="ProjectPath">The FieldWorks project data file the backed-up store is registered to; secondary to the identity.</param>
/// <param name="ProjectGuid">The LibLCM project GUID read from that file when the backup was taken; null when the file held none.</param>
/// <param name="ProjectIdentity">Motif's own project identity for it, the file's name without its extension.</param>
/// <param name="SchemaVersion">The store's schema version.</param>
/// <param name="ProductVersion">The Motif build that took the backup.</param>
/// <param name="CreatedUtc">When the backup was taken.</param>
/// <param name="Files">Each backed-up file with its size and sha256.</param>
/// <param name="FwDataIncluded">Always false: the FieldWorks project file is not part of a Motif backup.</param>
/// <param name="FwDataNote">What to use to back up the FieldWorks project file itself.</param>
public sealed record StoreBackupResponse(string BackupDirectory, string ReceiptPath, string ProjectPath,
    string? ProjectGuid, string ProjectIdentity, int SchemaVersion, string ProductVersion, DateTimeOffset CreatedUtc, IReadOnlyList<StoreBackupFile> Files,
    bool FwDataIncluded, string FwDataNote);

/// <summary>What a restore put back, and where the store it replaced was saved.</summary>
/// <param name="StorePath">The restored store.</param>
/// <param name="BackupDirectory">The backup that was restored.</param>
/// <param name="SchemaVersion">The restored store's schema version.</param>
/// <param name="RestoredUtc">When the swap completed.</param>
/// <param name="Restored">The files put in place, each verified against the backup's receipt.</param>
/// <param name="PreRestoreDirectory">The folder whose <c>moved</c> subfolder holds the store the restore replaced (with its sidecars) and every other file moved aside; null when nothing was moved.</param>
/// <param name="Quarantined">Every file the restore moved out of the project, each with where it went and why.</param>
public sealed record StoreRestoreResponse(string StorePath, string BackupDirectory, int SchemaVersion,
    DateTimeOffset RestoredUtc, IReadOnlyList<StoreBackupFile> Restored,
    string? PreRestoreDirectory, IReadOnlyList<ProjectQuarantinedFile> Quarantined);

/// <summary>A file a restore moved out of the project into the pre-restore folder instead of deleting or overwriting it.</summary>
/// <param name="OriginalPath">Where the file was.</param>
/// <param name="MovedTo">Where it is now.</param>
/// <param name="Reason"><c>replaced</c> when the backup holds a file for that place, or <c>not-in-backup</c> when it does not.</param>
public sealed record ProjectQuarantinedFile(string OriginalPath, string MovedTo, string Reason);
