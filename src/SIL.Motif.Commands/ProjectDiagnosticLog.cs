using System.Globalization;

namespace SIL.Motif.Commands;

/// <summary>
/// A plain-text log beside a FieldWorks project, next to its Motif store, for failures a command survived but a
/// developer must be able to find later: the result carries a one-sentence note, the full exception lands here.
/// </summary>
public static class ProjectDiagnosticLog
{
    private const string FileSuffix = ".motif.log";

    /// <summary>Returns the project-adjacent log path for a FieldWorks project.</summary>
    public static string PathFor(string fwDataPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fwDataPath);
        var full = Path.GetFullPath(fwDataPath);
        return Path.Combine(Path.GetDirectoryName(full)!, Path.GetFileNameWithoutExtension(full) + FileSuffix);
    }

    /// <summary>
    /// Appends one UTC-stamped entry, and returns <see langword="false"/> instead of throwing when the log cannot be
    /// written, because the failure being recorded already happened and must not be replaced by this one.
    /// </summary>
    public static bool Append(string fwDataPath, string summary, Exception exception)
    {
        try
        {
            var stamp = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
            File.AppendAllText(PathFor(fwDataPath), $"{stamp} {summary}{Environment.NewLine}{exception}{Environment.NewLine}");
            return true;
        }
        catch (Exception logFailure) when (logFailure is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
