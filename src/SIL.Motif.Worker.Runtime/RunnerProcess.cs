using System.Diagnostics;
using System.Globalization;

namespace SIL.Motif.Worker;

/// <summary>Process start times, which tell a runner a record names from a later process that reused its pid.</summary>
public static class RunnerProcess
{
    /// <summary>How far a recorded start time may differ from the process's own and still be the same process.</summary>
    public static readonly TimeSpan StartTolerance = TimeSpan.FromSeconds(2);

    /// <summary>This process's start time as an ISO 8601 UTC text, or empty when the system will not say.</summary>
    public static string SelfStartText { get; } = Format(StartOf(Environment.ProcessId));

    /// <summary>The start time of the live process <paramref name="pid"/>, or null when it is gone or unreadable.</summary>
    public static DateTimeOffset? StartOf(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return process.HasExited ? null : new DateTimeOffset(process.StartTime.ToUniversalTime(), TimeSpan.Zero);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or
            System.ComponentModel.Win32Exception or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>Whether <paramref name="recorded"/> names <paramref name="actual"/> within <see cref="StartTolerance"/>.</summary>
    public static bool SameStart(string? recorded, DateTimeOffset actual) =>
        DateTimeOffset.TryParse(recorded, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var when) &&
        (when - actual).Duration() <= StartTolerance;

    /// <summary>Formats a start time the way records hold it.</summary>
    public static string Format(DateTimeOffset? start) =>
        start?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty;
}
