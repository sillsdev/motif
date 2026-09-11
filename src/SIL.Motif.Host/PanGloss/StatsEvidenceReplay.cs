namespace SIL.Motif.Host.PanGloss;

/// <summary>Verified disposable copies of retained source and statistics for a parser query that may write to its inputs.</summary>
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

    /// <summary>Copies the artifacts first, then verifies the exact copied bytes the parser will receive.</summary>
    public static StatsEvidenceReplay Create(BatchInvocationEvidence evidence, string cachePath, string cacheDigest)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        if (string.IsNullOrWhiteSpace(evidence.SourcePath) || string.IsNullOrWhiteSpace(evidence.SourceBytesSha256) ||
            string.IsNullOrWhiteSpace(cachePath) || string.IsNullOrWhiteSpace(cacheDigest))
            throw new InvalidDataException("The Assessment is missing retained source or statistics evidence.");
        var replay = new StatsEvidenceReplay(Directory.CreateTempSubdirectory("motif-stats-replay-").FullName);
        try
        {
            File.Copy(evidence.SourcePath, replay.GrammarPath);
            File.Copy(cachePath, replay.CachePath);
            if (!string.Equals(BatchInvocationEvidence.DigestFile(replay.GrammarPath), evidence.SourceBytesSha256,
                    StringComparison.Ordinal) ||
                !string.Equals(BatchInvocationEvidence.DigestFile(replay.CachePath), cacheDigest, StringComparison.Ordinal))
                throw new InvalidDataException("The retained source or statistics bytes do not match the recorded Assessment.");
            return replay;
        }
        catch
        {
            replay.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
