namespace SIL.Motif.Commands;

/// <summary>Thrown when Apply's save returned but the project file does not hold the change.</summary>
internal sealed class ApplySaveNotLandedException(string message) : Exception(message);

/// <summary>Reads the saved project file back, because a save that returns is not proof that it landed.</summary>
/// <remarks>
/// A write can go somewhere other than the project folder (a sandbox overlay, a temp file whose move
/// failed quietly) and LibLCM's background commit thread reports nothing. The applied-log entry is written
/// in the same unit of work as every operation, so finding its Proposal id on disk means the whole
/// change landed. A slow file system may show the file late, so the read is retried briefly before refusing.
/// </remarks>
internal static class ApplySaveCheck
{
    internal static int Attempts { get; set; } = 6;
    internal static TimeSpan Pause { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>Throws <see cref="ApplySaveNotLandedException"/> unless the file on disk names the Proposal's applied-log entry.</summary>
    internal static void RequireOnDisk(string fwDataPath, Guid proposalId)
    {
        var needle = proposalId.ToString("D");
        string? problem = null;
        for (var attempt = 1; attempt <= Attempts; attempt++)
        {
            try
            {
                problem = null;
                foreach (var line in File.ReadLines(fwDataPath))
                    if (line.Contains(needle, StringComparison.OrdinalIgnoreCase)) return;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                problem = exception.Message;
            }

            if (attempt < Attempts) Thread.Sleep(Pause);
        }

        throw new ApplySaveNotLandedException(
            $"Motif could not find the change in the project file {fwDataPath} after saving" +
            (problem is null ? "." : " (" + problem + ").") +
            " Nothing was recorded as applied. The save may still be reaching the disk, so applying again is safe: " +
            "if it landed, Motif will say it was already applied. If it keeps failing, check that the program " +
            "running Motif can write to the project folder (a restricted sandbox may hide a failed write).");
    }
}
