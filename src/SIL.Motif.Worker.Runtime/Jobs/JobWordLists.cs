using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using SIL.Motif.Host.Store;

namespace SIL.Motif.Worker.Jobs;

/// <summary>A word list a job reads from a file beside the project's store, named by its content digest.</summary>
/// <param name="Sha256">Lower-case hex SHA-256 of the file's UTF-8 JSON array of words.</param>
/// <param name="Count">How many words the list holds.</param>
public sealed record JobWordList(
    [property: JsonPropertyName("sha256")] string Sha256,
    [property: JsonPropertyName("count")] int Count);

/// <summary>
/// Keeps word lists too large for a job's input document as files in a folder beside the project's store, so a long Trial
/// word list never pushes the job past its structured JSON bound.
/// </summary>
/// <remarks>
/// Files are content-addressed and shared by every job that names the same list. A file no remaining job names is
/// removed when archived jobs are purged, once it is older than <see cref="UnreferencedGrace"/>, so a list written
/// for a job still being created is never taken from under it.
/// </remarks>
public static class JobWordLists
{
    /// <summary>How long an unreferenced list file is kept, covering the moment between writing it and its job.</summary>
    public static readonly TimeSpan UnreferencedGrace = TimeSpan.FromHours(1);

    private const string DirectorySuffix = ".job-inputs";
    private const string Suffix = ".words.json";

    /// <summary>Writes the list, or refreshes the existing identical file, and returns the reference to it.</summary>
    public static JobWordList Store(MotifDatabase database, IReadOnlyList<string> words)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(words);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(words);
        var sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var directory = DirectoryOf(database);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, sha256 + Suffix);
        if (File.Exists(path))
        {
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
        }
        else
        {
            var temporary = Path.Combine(directory, $"{sha256}.{Guid.NewGuid():N}.tmp");
            File.WriteAllBytes(temporary, bytes);
            try
            {
                File.Move(temporary, path, overwrite: false);
            }
            catch (IOException) when (File.Exists(path))
            {
                File.Delete(temporary);
            }
        }
        return new JobWordList(sha256, words.Count);
    }

    /// <summary>Reads a stored list, refusing one that is missing or no longer matches its reference.</summary>
    /// <exception cref="InvalidOperationException">The file is missing, altered, or not the named list.</exception>
    public static IReadOnlyList<string> Read(MotifDatabase database, JobWordList list)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(list);
        var path = Path.Combine(DirectoryOf(database), list.Sha256 + Suffix);
        if (!File.Exists(path))
            throw new InvalidOperationException($"The job's word list {list.Sha256} is no longer stored.");
        var bytes = File.ReadAllBytes(path);
        if (!string.Equals(Convert.ToHexStringLower(SHA256.HashData(bytes)), list.Sha256, StringComparison.Ordinal))
            throw new InvalidOperationException($"The job's word list {list.Sha256} was altered after it was stored.");
        var words = JsonSerializer.Deserialize<string[]>(bytes)
            ?? throw new InvalidOperationException($"The job's word list {list.Sha256} is not a list of words.");
        if (words.Length != list.Count)
            throw new InvalidOperationException(
                $"The job's word list {list.Sha256} holds {words.Length} words, not {list.Count}.");
        return words;
    }

    /// <summary>Deletes list files older than the grace period that no remaining job input names.</summary>
    /// <returns>The number of files removed.</returns>
    public static int RemoveUnreferenced(MotifDatabase database, IEnumerable<string> remainingInputs, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(remainingInputs);
        var directory = DirectoryOf(database);
        if (!Directory.Exists(directory)) return 0;
        var inputs = remainingInputs.ToArray();
        var removed = 0;
        foreach (var path in Directory.EnumerateFiles(directory))
        {
            if (nowUtc - File.GetLastWriteTimeUtc(path) < UnreferencedGrace) continue;
            var name = Path.GetFileName(path);
            var sha256 = name.Split('.')[0];
            if (name.EndsWith(Suffix, StringComparison.Ordinal) &&
                inputs.Any(input => input.Contains(sha256, StringComparison.Ordinal)))
                continue;
            try
            {
                File.Delete(path);
                removed++;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return removed;
    }

    // One folder per store, because several projects' stores may share a parent folder.
    private static string DirectoryOf(MotifDatabase database) => Path.GetFullPath(database.FullPath) + DirectorySuffix;
}
