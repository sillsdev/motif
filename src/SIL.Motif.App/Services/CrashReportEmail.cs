namespace SIL.Motif.App.Services;

/// <summary>
/// Builds the <c>mailto:</c> link Email maintainer opens: the address, a short subject, and a short body that
/// names the error and asks for the saved report.
/// </summary>
/// <remarks>
/// RFC 6068 intends a <c>mailto:</c> body for short text, and mail programs cut long links off at lengths of
/// their own, so the stack trace never goes in the link. The person attaches the saved report instead.
/// </remarks>
public static class CrashReportEmail
{
    /// <summary>How many characters of the error's message the body carries before it is cut short.</summary>
    public const int MessageLimit = 200;

    /// <summary>The link that opens a message to <paramref name="address"/> about <paramref name="report"/>.</summary>
    public static Uri MailtoFor(CrashReport report, string address)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentException.ThrowIfNullOrWhiteSpace(address);
        var subject = $"Motif {report.MotifVersion} error report";
        var body =
            $"Motif {report.MotifVersion} closed after an error: {report.Exception.GetType().Name}: " +
            $"{OneShortLine(report.Message)}\r\n\r\n" +
            "Please attach the report you saved from Motif's error window.\r\n\r\n" +
            "What I was doing when it happened:\r\n";
        return new Uri($"mailto:{address}?subject={Uri.EscapeDataString(subject)}&body={Uri.EscapeDataString(body)}");
    }

    private static string OneShortLine(string message)
    {
        var line = string.Join(' ', message.Split((char[])['\r', '\n'], StringSplitOptions.RemoveEmptyEntries));
        return line.Length <= MessageLimit ? line : line[..MessageLimit] + "…";
    }
}
