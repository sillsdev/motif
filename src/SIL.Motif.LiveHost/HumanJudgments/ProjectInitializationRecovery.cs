using System.Security.Cryptography;
using System.Text;

namespace SIL.Motif.LiveHost.HumanJudgments;

/// <summary>Keeps and verifies a private pre-write copy for project initialization recovery.</summary>
public static class ProjectInitializationRecovery
{
    private const string RecoveryDirectoryName = "initialization-recovery";
    private const int CopyBufferSize = 32 * 1024;

    /// <summary>Returns the stable recovery location for one project path under a worker root.</summary>
    public static string GetRecoveryCopyPath(string projectPath, string workerRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(workerRoot);
        var fullProjectPath = Path.GetFullPath(projectPath);
        var pathIdentity = OperatingSystem.IsWindows() ? fullProjectPath.ToUpperInvariant() : fullProjectPath;
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(pathIdentity))).ToLowerInvariant();
        return Path.Combine(Path.GetFullPath(workerRoot), RecoveryDirectoryName, digest + ".fwdata");
    }

    /// <summary>Copies the saved project bytes and verifies the copy before publishing it.</summary>
    public static void CreateOriginalCopy(string projectPath, string recoveryCopyPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(recoveryCopyPath);
        if (File.Exists(recoveryCopyPath))
            throw new IOException("A project-initialization recovery copy already exists.");

        var recoveryDirectory = Path.GetDirectoryName(Path.GetFullPath(recoveryCopyPath))
            ?? throw new IOException("The project-initialization recovery path has no parent directory.");
        Directory.CreateDirectory(recoveryDirectory);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(recoveryDirectory,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        var temporaryPath = recoveryCopyPath + ".partial-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var source = new FileStream(
                       projectPath, FileMode.Open, FileAccess.Read, FileShare.Read, CopyBufferSize,
                       FileOptions.SequentialScan))
            {
                var options = new FileStreamOptions
                {
                    Mode = FileMode.CreateNew,
                    Access = FileAccess.Write,
                    Share = FileShare.None,
                    BufferSize = CopyBufferSize,
                    Options = FileOptions.SequentialScan,
                };
                if (!OperatingSystem.IsWindows())
                    options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
                using var destination = new FileStream(temporaryPath, options);
                source.CopyTo(destination, CopyBufferSize);
                destination.Flush(flushToDisk: true);
            }

            if (!ContentsMatch(projectPath, temporaryPath))
                throw new IOException("The project changed while its initialization recovery copy was made.");
            File.Move(temporaryPath, recoveryCopyPath);
            if (!ContentsMatch(projectPath, recoveryCopyPath))
                throw new IOException("The project-initialization recovery copy failed its byte check.");
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    /// <summary>The manual restore sentence every message naming a recovery copy carries; Motif never restores itself.</summary>
    public static string RestoreStep(string projectPath, string recoveryCopyPath) =>
        $"Close FieldWorks, then copy {recoveryCopyPath} over {projectPath}.";

    /// <summary>Checks whether a recovery copy still matches the current saved project bytes.</summary>
    public static bool MatchesCurrentProject(string projectPath, string recoveryCopyPath) =>
        File.Exists(projectPath) && File.Exists(recoveryCopyPath) && ContentsMatch(projectPath, recoveryCopyPath);

    /// <summary>Deletes a recovery copy only after its caller has verified the saved schema.</summary>
    public static void DeleteVerifiedCopy(string recoveryCopyPath)
    {
        if (!File.Exists(recoveryCopyPath)) return;
        File.Delete(recoveryCopyPath);
        var directory = Path.GetDirectoryName(recoveryCopyPath);
        if (directory is not null && Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
            Directory.Delete(directory);
    }

    private static bool ContentsMatch(string leftPath, string rightPath)
    {
        var left = new FileInfo(leftPath);
        var right = new FileInfo(rightPath);
        if (left.Length != right.Length) return false;
        using var leftStream = File.OpenRead(leftPath);
        using var rightStream = File.OpenRead(rightPath);
        return CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(leftStream), SHA256.HashData(rightStream));
    }
}
