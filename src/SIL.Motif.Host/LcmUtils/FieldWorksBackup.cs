using SIL.LCModel.DomainServices.BackupRestore;

namespace SIL.Motif.Host.LcmUtils;

/// <summary>
/// Restores a FieldWorks backup (<c>.fwbackup</c>) into a new project folder beside it, through LibLCM's own
/// restore, so a backup opens like any project and the backup itself is never changed. A folder that already
/// holds a project of the same name is left alone: the restore takes the next free name, <c>Koro 2</c> and on.
/// </summary>
public static class FieldWorksBackup
{
    /// <summary>The file extension FieldWorks gives its backups.</summary>
    public const string Extension = ".fwbackup";

    /// <summary>Whether <paramref name="path"/> names a FieldWorks backup rather than a project.</summary>
    public static bool IsBackup(string path) =>
        path.EndsWith(Extension, StringComparison.OrdinalIgnoreCase);

    /// <summary>Restores the backup beside itself and returns the restored project's <c>.fwdata</c> path.</summary>
    /// <exception cref="FileNotFoundException">The backup does not exist.</exception>
    /// <exception cref="InvalidDataException">The file is not a FieldWorks backup LibLCM can restore.</exception>
    public static string Restore(string backupPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backupPath);
        var fullBackup = Path.GetFullPath(backupPath);
        if (!File.Exists(fullBackup))
            throw new FileNotFoundException("The FieldWorks backup does not exist.", fullBackup);
        FwDataProjectLoader.Init();

        BackupFileSettings backup;
        try
        {
            backup = new BackupFileSettings(fullBackup, true);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            throw new InvalidDataException("The file is not a FieldWorks backup that can be restored.", exception);
        }
        if (string.IsNullOrWhiteSpace(backup.ProjectName))
            throw new InvalidDataException("The FieldWorks backup does not name its project.");

        var root = Path.GetDirectoryName(fullBackup)!;
        var name = FreeProjectName(root, backup.ProjectName);
        var settings = new RestoreProjectSettings(root, name, fullBackup, string.Empty);
        var progress = new LcmThreadedProgress();
        try
        {
            new ProjectRestoreService(settings, new HeadlessLcmUi(progress.SynchronizeInvoke), null, null)
                .RestoreProject(progress);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            throw new InvalidDataException("FieldWorks could not restore this backup.", exception);
        }

        var restored = Path.Combine(root, name, name + ".fwdata");
        if (!File.Exists(restored))
            throw new InvalidDataException("The FieldWorks backup did not contain a project file.");
        return restored;
    }

    /// <summary>
    /// The first of <paramref name="projectName"/>, <c>projectName 2</c>, <c>projectName 3</c>… with no folder in
    /// <paramref name="root"/>.
    /// </summary>
    public static string FreeProjectName(string root, string projectName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectName);
        if (!Directory.Exists(Path.Combine(root, projectName))) return projectName;
        for (var number = 2; ; number++)
        {
            var candidate = projectName + " " + number;
            if (!Directory.Exists(Path.Combine(root, candidate))) return candidate;
        }
    }
}
