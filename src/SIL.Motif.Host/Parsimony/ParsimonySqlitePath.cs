namespace SIL.Motif.Host.Parsimony;

/// <summary>
/// Spells a Parsimony artifact path the way SQLite needs it to open the file at any depth.
/// </summary>
/// <remarks>
/// SQLite's Windows file layer refuses a path longer than the legacy 260-character limit even when the
/// operating system allows long paths, and a worker root below a long profile name reaches that limit.
/// On Windows both spellings use the extended-length <c>\\?\</c> form, which SQLite accepts at any length;
/// a URI carries it percent-encoded, because a URI authority cannot hold it.
/// Pinned by `ArtifactBeyondTheLegacyPathLimitOpensForWritingAndAttachesReadOnly`.
/// </remarks>
internal static class ParsimonySqlitePath
{
    private const string ExtendedPrefix = @"\\?\";

    /// <summary>Returns the SQLite data source that opens <paramref name="path"/> at any length.</summary>
    /// <param name="path">A relative or absolute file path.</param>
    /// <returns>The full path, in extended-length form on Windows.</returns>
    public static string DataSource(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        if (!OperatingSystem.IsWindows() || fullPath.StartsWith(ExtendedPrefix, StringComparison.Ordinal))
            return fullPath;
        return fullPath.StartsWith(@"\\", StringComparison.Ordinal)
            ? ExtendedPrefix + @"UNC\" + fullPath[2..]
            : ExtendedPrefix + fullPath;
    }

    /// <summary>Returns a read-only SQLite URI, for <c>ATTACH</c>, that opens <paramref name="path"/> at any length.</summary>
    /// <param name="path">A relative or absolute file path.</param>
    /// <returns>A <c>file:</c> URI with <c>mode=ro</c>.</returns>
    public static string ReadOnlyUri(string path)
    {
        if (!OperatingSystem.IsWindows())
            return new Uri(Path.GetFullPath(path)).AbsoluteUri + "?mode=ro";
        var escaped = DataSource(path).Replace("%", "%25", StringComparison.Ordinal)
            .Replace("?", "%3F", StringComparison.Ordinal).Replace("#", "%23", StringComparison.Ordinal);
        return "file:" + escaped + "?mode=ro";
    }
}
