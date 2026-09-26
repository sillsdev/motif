using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using SIL.Motif.Host;

namespace SIL.Motif.App.Services;

/// <summary>An error that escaped the App's UI thread, with what a maintainer needs to reproduce it.</summary>
/// <param name="Exception">The error, whose own account is the report's details.</param>
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

    /// <summary>The error's own message, which the window shows under its plain summary.</summary>
    public string Message => Exception.Message;

    /// <summary>
    /// <see cref="System.Exception.ToString"/>: the type, message and stack trace of the error and of every
    /// exception inside it.
    /// </summary>
    public string Details => Exception.ToString();

    /// <summary>The name the save dialog offers: a text file named for when the error happened.</summary>
    public string SuggestedFileName =>
        "motif-error-" + OccurredAt.UtcDateTime.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".txt";

    /// <summary>The whole report, as Save report writes it and Copy details copies it.</summary>
    public string ToText()
    {
        var text = new StringBuilder();
        Line(text, "Motif error report");
        Line(text, "Motif version: " + MotifVersion);
        Line(text, "Time (UTC): " + OccurredAt.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
        Line(text, "Operating system: " + OperatingSystem);
        Line(text, ".NET: " + Runtime);
        Line(text, "");
        Line(text, Details);
        return text.ToString();
    }

    // CRLF throughout, so the saved file reads the same in Notepad as it does attached to an email.
    private static void Line(StringBuilder text, string line) => text.Append(line).Append("\r\n");
}
