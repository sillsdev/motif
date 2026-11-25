namespace SIL.Motif.Contract.Requests;

/// <summary>Backs up a project's Motif store and the files Motif owns for it.</summary>
/// <param name="ProjectPath">The FieldWorks project's data-file path.</param>
/// <param name="To">The folder to write the backup into; null for a timestamped folder beside the store.</param>
public sealed record StoreBackupRequest(string ProjectPath, string? To = null);

/// <summary>Restores a project's Motif store and owned files from a backup folder.</summary>
/// <param name="ProjectPath">The FieldWorks project's data-file path.</param>
/// <param name="BackupPath">The backup folder, or its <c>motif-backup.json</c> receipt.</param>
/// <param name="Force">Restore a backup of a different project (a different LibLCM project GUID or identity) into this one.</param>
public sealed record StoreRestoreRequest(string ProjectPath, string BackupPath, bool Force = false);
