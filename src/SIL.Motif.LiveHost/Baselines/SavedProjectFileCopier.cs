using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

namespace SIL.Motif.LiveHost.Baselines;

/// <summary>The saved files copied out of a held project, and the freshness evidence read while copying.</summary>
public sealed record SavedProjectFilesCopy(
    string FwDataPath,
    IReadOnlyList<string> WritingSystemPaths,
    DateTimeOffset SourceLastWriteUtc);

/// <summary>
/// Copies a FieldWorks project's saved <c>.fwdata</c> and writing systems into an owned directory
/// without ever asking FieldWorks' own save to wait.
/// </summary>
/// <remarks>
/// On Windows, a handle sharing only <see cref="FileShare.Read"/> blocks another process from
/// renaming the file it holds open; adding <see cref="FileShare.Delete"/> lets that rename proceed
/// while this reader keeps reading the original file's data to completion, which is exactly the
/// temp-then-rename shape FieldWorks' save uses. Neither the project's lock file nor its <c>.bak</c>
/// is ever opened or read; whether FieldWorks holds the project is observed elsewhere, from the lock
/// file's mere presence.
/// </remarks>
public sealed class SavedProjectFileCopier
{
    private const int CopyBufferSize = 32 * 1024;
    private const string WritingSystemStore = "WritingSystemStore";

    /// <summary>
    /// Awaited immediately after the <c>.fwdata</c> source handle opens and before any byte is copied.
    /// Exists so a test can pause the copier there to race a concurrent rename against it.
    /// </summary>
    internal Func<Task>? AfterFwDataHandleOpened { get; set; }

    /// <summary>
    /// Copies <paramref name="sourceFwDataPath"/> and its project's top-level
    /// <c>WritingSystemStore/*.ldml</c> files into <paramref name="destinationDirectory"/>, which is
    /// created if needed. Validates the copied <c>.fwdata</c> is a complete <c>languageproject</c>
    /// document before returning.
    /// </summary>
    /// <exception cref="InvalidDataException">
    /// The copied <c>.fwdata</c> ends before a closing <c>languageproject</c> element — the source was
    /// read mid-write rather than as a FieldWorks-completed save.
    /// </exception>
    public async Task<SavedProjectFilesCopy> CopyAsync(
        string sourceFwDataPath,
        string destinationDirectory,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sourceFwDataPath))
            throw new ArgumentException("A source .fwdata path is required.", nameof(sourceFwDataPath));
        if (string.IsNullOrWhiteSpace(destinationDirectory))
            throw new ArgumentException("A destination directory is required.", nameof(destinationDirectory));
        cancellationToken.ThrowIfCancellationRequested();

        Directory.CreateDirectory(destinationDirectory);
        var destinationFwDataPath = Path.Combine(destinationDirectory, Path.GetFileName(sourceFwDataPath));
        var sourceLastWriteUtc = await CopyFileAsync(
            sourceFwDataPath, destinationFwDataPath, AfterFwDataHandleOpened, cancellationToken).ConfigureAwait(false);
        ValidateCompleteLanguageProject(destinationFwDataPath);

        var writingSystemPaths = new List<string>();
        var sourceWritingSystemFolder = Path.Combine(Path.GetDirectoryName(sourceFwDataPath)!, WritingSystemStore);
        if (Directory.Exists(sourceWritingSystemFolder))
        {
            var destinationWritingSystemFolder = Path.Combine(destinationDirectory, WritingSystemStore);
            Directory.CreateDirectory(destinationWritingSystemFolder);
            foreach (var sourceLdmlPath in Directory
                         .EnumerateFiles(sourceWritingSystemFolder, "*.ldml", SearchOption.TopDirectoryOnly)
                         .OrderBy(path => Path.GetFileName(path), StringComparer.Ordinal))
            {
                var destinationLdmlPath = Path.Combine(destinationWritingSystemFolder, Path.GetFileName(sourceLdmlPath));
                await CopyFileAsync(sourceLdmlPath, destinationLdmlPath, null, cancellationToken).ConfigureAwait(false);
                writingSystemPaths.Add(destinationLdmlPath);
            }
        }

        return new SavedProjectFilesCopy(destinationFwDataPath, writingSystemPaths, sourceLastWriteUtc);
    }

    private static async Task<DateTimeOffset> CopyFileAsync(
        string sourcePath,
        string destinationPath,
        Func<Task>? afterHandleOpened,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var source = OpenSavedFile(sourcePath);
        var sourceLastWriteUtc = WindowsSavedFileMetadata.GetLastWriteTimeUtc(source.SafeFileHandle);
        if (afterHandleOpened is not null) await afterHandleOpened().ConfigureAwait(false);

        using var destination = new FileStream(
            destinationPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            CopyBufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var buffer = new byte[CopyBufferSize];
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
        return sourceLastWriteUtc;
    }

    /// <summary>
    /// Opens a saved project file the one way that lets FieldWorks' save-time rename proceed while
    /// this handle is still open: delete sharing, asynchronous, sequential-scan.
    /// </summary>
    internal static FileStream OpenSavedFile(string path) => new(
        path,
        FileMode.Open,
        FileAccess.Read,
        FileShare.Read | FileShare.Delete,
        CopyBufferSize,
        FileOptions.Asynchronous | FileOptions.SequentialScan);

    private static void ValidateCompleteLanguageProject(string copiedFwDataPath)
    {
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
        string? finalElementName = null;
        try
        {
            using var reader = XmlReader.Create(copiedFwDataPath, settings);
            while (reader.Read())
            {
                if (reader.NodeType is XmlNodeType.Element or XmlNodeType.EndElement)
                    finalElementName = reader.LocalName;
            }
        }
        catch (XmlException ex)
        {
            throw new InvalidDataException(
                $"The copied project file '{copiedFwDataPath}' is incomplete: {ex.Message}", ex);
        }

        if (!string.Equals(finalElementName, "languageproject", StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"The copied project file '{copiedFwDataPath}' does not end with a closing languageproject element.");
        }
    }
}
