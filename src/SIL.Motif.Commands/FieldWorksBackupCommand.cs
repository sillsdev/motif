using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.LcmUtils;

namespace SIL.Motif.Commands;

/// <summary>The project a FieldWorks backup was restored into.</summary>
/// <param name="ProjectPath">The restored project's <c>.fwdata</c> file, in a new folder beside the backup.</param>
public sealed record RestoredBackup(string ProjectPath);

/// <summary>
/// Opens a FieldWorks backup as a project: restores it into a new folder beside the backup, leaving the backup and
/// any earlier restore untouched, and returns the restored <c>.fwdata</c> for an ordinary open.
/// </summary>
public static class FieldWorksBackupCommand
{
    /// <summary>Whether <paramref name="path"/> names a FieldWorks backup rather than a project.</summary>
    public static bool IsBackup(string path) => FieldWorksBackup.IsBackup(path);

    /// <summary>Restores the backup, or refuses when it is missing or cannot be restored, pinned by `AFileThatIsNotABackupIsRefused`.</summary>
    public static CommandOutcome<RestoredBackup> Restore(string backupPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backupPath);
        try
        {
            return CommandOutcome<RestoredBackup>.Success(new RestoredBackup(FieldWorksBackup.Restore(backupPath)));
        }
        catch (FileNotFoundException exception)
        {
            return Refuse(RefusalCodes.ProjectNotFound, FailureReason.InvalidArgument, exception.Message, backupPath);
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or
            UnauthorizedAccessException)
        {
            return Refuse(RefusalCodes.ProjectBackupUnreadable, FailureReason.InvalidArgument,
                exception.InnerException is { } cause ? exception.Message + " " + cause.Message : exception.Message,
                backupPath);
        }
    }

    private static CommandOutcome<RestoredBackup> Refuse(
        string code, FailureReason reason, string message, string backupPath) =>
        CommandOutcome<RestoredBackup>.Refused(new Refusal(code, reason, message,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["backupPath"] = backupPath }));
}
