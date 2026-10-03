using System.Runtime.InteropServices;
using SIL.Motif.Host;

namespace SIL.Motif.App.Services;

/// <summary>An error that escaped the App's UI thread, with its local details kept inside the window.</summary>
/// <param name="Exception">The error shown locally under Details.</param>
/// <param name="OccurredAt">When it reached the error window.</param>
/// <param name="MotifVersion">The running build's version.</param>
/// <param name="OperatingSystem">The operating system's own description of itself.</param>
/// <param name="Runtime">The .NET runtime's own description of itself.</param>
public sealed record CrashReport(
    Exception Exception, DateTimeOffset OccurredAt, string MotifVersion, string OperatingSystem, string Runtime)
{
    /// <summary>A report of <paramref name="exception"/> from this process, timed by <paramref name="clock"/>.</summary>
    public static CrashReport For(Exception exception, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentNullException.ThrowIfNull(clock);
        return new CrashReport(exception, clock.GetUtcNow(), MotifProductVersion.CurrentText,
            RuntimeInformation.OSDescription, RuntimeInformation.FrameworkDescription);
    }

    /// <summary>The error message shown locally in the error window.</summary>
    public string Message => Exception.Message;

    /// <summary>The full local exception details shown only inside the error window.</summary>
    public string Details => Exception.ToString();

    /// <summary>The allowlisted report shown for review before it can be copied or shared.</summary>
    public ProblemReport ProblemReport => global::SIL.Motif.App.Services.ProblemReport.FromCrash(this);

    /// <summary>Renders only the allowlisted report fields; rich local details require a separate explicit choice.</summary>
    public string ToText() => ProblemReport.ToText();
}
