using Microsoft.Data.Sqlite;

namespace SIL.Motif.Host.Parsimony;

/// <summary>What a failed open of a published Parsimony bundle says about the bundle itself.</summary>
public enum ParsimonyBundleFault
{
    /// <summary>A bundle file or its folder is gone, so the bundle is unavailable.</summary>
    Missing,

    /// <summary>The bytes read do not match the stored digest, or the artifact is malformed.</summary>
    Corrupt,

    /// <summary>Another process holds a file open, so the bundle is intact and the request can be retried.</summary>
    Busy,
}

/// <summary>Classifies a failed bundle open so the view, the session and the repository agree on it.</summary>
public static class ParsimonyBundleFaults
{
    /// <summary>
    /// Returns the fault for an exception raised while opening a bundle, or null when the exception does not
    /// name one. Only a missing file or a corrupt artifact may mark a bundle; a transient lock marks nothing.
    /// </summary>
    /// <param name="exception">The exception the open raised.</param>
    /// <param name="bundlePaths">The bundle's files and folder; any that is absent makes the bundle missing.</param>
    public static ParsimonyBundleFault? Classify(Exception exception, IEnumerable<string> bundlePaths)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentNullException.ThrowIfNull(bundlePaths);
        if (bundlePaths.Any(path => !File.Exists(path) && !Directory.Exists(path)))
            return ParsimonyBundleFault.Missing;
        return exception switch
        {
            InvalidDataException => ParsimonyBundleFault.Corrupt,
            SqliteException { SqliteErrorCode: 11 or 26 } => ParsimonyBundleFault.Corrupt,
            IOException => ParsimonyBundleFault.Busy,
            UnauthorizedAccessException => ParsimonyBundleFault.Busy,
            SqliteException { SqliteErrorCode: 5 or 6 or 10 or 14 } => ParsimonyBundleFault.Busy,
            _ => null,
        };
    }
}
