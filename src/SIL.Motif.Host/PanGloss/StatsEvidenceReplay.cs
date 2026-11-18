using System.Security.Cryptography;

namespace SIL.Motif.Host.PanGloss;

/// <summary>Verified disposable copies of retained source and, when collected, statistics for a parser query.</summary>
public sealed class StatsEvidenceReplay : IDisposable
{
    private readonly string _directory;

    private StatsEvidenceReplay(string directory)
    {
        _directory = directory;
        GrammarPath = Path.Combine(directory, "source.fwdata");
        CachePath = Path.Combine(directory, "statistics.sqlite");
    }

    public string GrammarPath { get; }
    public string CachePath { get; }

    /// <summary>
    /// Copies the artifacts while hashing the bytes streamed through the copy, then verifies those digests, so
    /// a large statistics cache is read once rather than copied and read again.
    /// </summary>
    public static StatsEvidenceReplay Create(BatchInvocationEvidence evidence, string cachePath, string cacheDigest,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(cachePath) || string.IsNullOrWhiteSpace(cacheDigest))
            throw new InvalidDataException("The Assessment is missing retained source or statistics evidence.");
        return CreateCore(evidence, cachePath, cacheDigest, cancellationToken);
    }

    /// <summary>Copies and verifies retained source for a Handoff whose Assessment skipped object timing.</summary>
    public static StatsEvidenceReplay CreateSourceOnly(BatchInvocationEvidence evidence,
        CancellationToken cancellationToken = default) => CreateCore(evidence, null, null, cancellationToken);

    private static StatsEvidenceReplay CreateCore(BatchInvocationEvidence evidence, string? cachePath,
        string? cacheDigest, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        if (string.IsNullOrWhiteSpace(evidence.SourcePath) || string.IsNullOrWhiteSpace(evidence.SourceBytesSha256))
            throw new InvalidDataException("The Assessment is missing retained source evidence.");
        var replay = new StatsEvidenceReplay(Directory.CreateTempSubdirectory("motif-stats-replay-").FullName);
        try
        {
            string sourceDigest;
            using (StatsCacheLock.AcquireForFile(evidence.SourcePath, cancellationToken))
                sourceDigest = CopyAndDigest(evidence.SourcePath, replay.GrammarPath);
            if (!string.Equals(sourceDigest, evidence.SourceBytesSha256, StringComparison.Ordinal))
                throw new InvalidDataException("The retained source bytes do not match the recorded Assessment.");
            if (cachePath is null) return replay;
            string statsDigest;
            using var cacheLock = StatsCacheLock.AcquireForFile(cachePath, cancellationToken);
            try { statsDigest = CopyAndDigest(cachePath, replay.CachePath); }
            catch (FileNotFoundException exception)
            {
                throw new InvalidDataException("The statistics cache is no longer available; run a new Assessment.", exception);
            }
            catch (DirectoryNotFoundException exception)
            {
                throw new InvalidDataException("The statistics cache is no longer available; run a new Assessment.", exception);
            }
            if (!string.Equals(statsDigest, cacheDigest, StringComparison.Ordinal))
                throw new InvalidDataException("The retained source or statistics bytes do not match the recorded Assessment.");
            return replay;
        }
        catch
        {
            replay.Dispose();
            throw;
        }
    }

    // The same "sha256:" lowercase-hex form DigestFile produces, taken from the bytes as they are copied.
    private static string CopyAndDigest(string from, string to)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using (var input = new FileStream(from, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20))
        using (var output = new FileStream(to, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 20))
        {
            var buffer = new byte[1 << 20];
            int read;
            while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                hash.AppendData(buffer, 0, read);
                output.Write(buffer, 0, read);
            }
        }
        return "sha256:" + Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
