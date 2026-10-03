using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using SIL.Motif.Commands.Diagnostics;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;
using SIL.Motif.Host.Parser;

namespace SIL.Motif.App.Services;

/// <summary>A previewable problem report with a fixed public surface and optional local details.</summary>
public sealed record ProblemReport
{
    private static readonly Regex SafeVersion = new(@"\A[0-9A-Za-z][0-9A-Za-z.+-]{0,39}\z", RegexOptions.CultureInvariant);
    private static readonly Regex SafeCode = new(@"\A[a-z][a-z0-9.-]{0,79}\z", RegexOptions.CultureInvariant);
    private static readonly Regex SafeIdentifier = new(@"\A[A-Za-z_][A-Za-z0-9_+<>.`-]{0,79}\z", RegexOptions.CultureInvariant);
    private static readonly HashSet<string> SafeExceptionNames = new(StringComparer.Ordinal)
    {
        nameof(ArgumentException), nameof(InvalidDataException), nameof(InvalidOperationException),
        nameof(IOException), nameof(NotSupportedException), nameof(UnauthorizedAccessException),
        "MotifStoreVersionException", "SqliteException",
    };
    private readonly string? _localDetails;

    private ProblemReport(string motifVersion, string panGlossVersion, string operatingSystem, string operation,
        string refusalCode, string exitStatus, string sanitizedStack, IReadOnlyDictionary<string, string> safeFacts,
        string? localDetails)
    {
        MotifVersion = motifVersion;
        PanGlossVersion = panGlossVersion;
        OperatingSystem = operatingSystem;
        Operation = operation;
        RefusalCode = refusalCode;
        ExitStatus = exitStatus;
        SanitizedStack = sanitizedStack;
        SafeFacts = safeFacts;
        _localDetails = localDetails;
    }

    /// <summary>The Motif product version included in the report.</summary>
    public string MotifVersion { get; }

    /// <summary>The bundled or pinned PanGloss version, or an explicit unknown value.</summary>
    public string PanGlossVersion { get; }

    /// <summary>The operating system description for the process that made the report.</summary>
    public string OperatingSystem { get; }

    /// <summary>The fixed window operation associated with the failure.</summary>
    public string Operation { get; }

    /// <summary>The stable refusal code, or an unavailable value for an error without a refusal.</summary>
    public string RefusalCode { get; }

    /// <summary>The command's equivalent CLI exit status, the App's crash exit status, or an unavailable value.</summary>
    public string ExitStatus { get; }

    /// <summary>Method names from product and framework stack frames, without messages or file locations.</summary>
    public string SanitizedStack { get; }

    /// <summary>Fixed-name facts whose values passed the report's safety checks.</summary>
    public IReadOnlyDictionary<string, string> SafeFacts { get; }

    /// <summary>Builds a report from a refused window operation.</summary>
    /// <param name="refusal">The refusal shown in the window.</param>
    public static ProblemReport FromRefusal(WindowRefusal refusal)
    {
        ArgumentNullException.ThrowIfNull(refusal);
        return Create(refusal.Operation, refusal.Code, ExitStatusFor(refusal.Reason),
            refusal.FailureException is { } failure ? SanitizedStackFor(failure) : "Unavailable",
            refusal.Facts, refusal.Details);
    }

    /// <summary>Builds a report for a parse that has stopped making progress while it is still running.</summary>
    public static ProblemReport ForStalledParse() =>
        Create("measure words", "unavailable", "Unavailable (parse still running)", "Unavailable",
            EmptyFacts, null);

    /// <summary>Builds a report for a machine store that prevented the Known-project list from loading.</summary>
    /// <param name="exception">The local error, retained only for the optional details section.</param>
    public static ProblemReport ForMachineStoreFailure(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var facts = new Dictionary<string, string>
        {
            ["exceptionType"] = SafeExceptionName(exception.GetType()),
        };
        return Create("read Known projects", "machine-store.inconsistent",
            "Unavailable (in-process window action)", SanitizedStackFor(exception), facts, exception.Message);
    }

    /// <summary>Builds a report from an error that escaped the App's UI thread.</summary>
    /// <param name="report">The local crash record and its private details.</param>
    public static ProblemReport FromCrash(CrashReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return new ProblemReport(SafeVersionText(report.MotifVersion), ReportPanGlossVersion.Read(),
            SafeOperatingSystem(report.OperatingSystem), "window action", "unhandled-ui-error",
            "1", SanitizedStackFor(report.Exception),
            new ReadOnlyDictionary<string, string>(new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["exceptionType"] = SafeExceptionName(report.Exception.GetType()),
            }), report.Details);
    }

    /// <summary>Renders only allowlisted report data unless the person explicitly includes local details.</summary>
    public string ToText(bool includeLocalDetails = false)
    {
        var text = new StringBuilder();
        AppendLine(text, "Motif problem report");
        AppendLine(text, "Motif version: " + MotifVersion);
        AppendLine(text, "PanGloss version: " + PanGlossVersion);
        AppendLine(text, "Operating system: " + OperatingSystem);
        AppendLine(text, "Operation: " + Operation);
        AppendLine(text, "Refusal code: " + RefusalCode);
        AppendLine(text, "Exit status: " + ExitStatus);
        foreach (var fact in SafeFacts)
            AppendLine(text, "Failure fact " + fact.Key + ": " + fact.Value);
        AppendLine(text, "Sanitized stack:");
        foreach (var line in SanitizedStack.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            AppendLine(text, "  " + line.TrimEnd('\r'));

        if (includeLocalDetails && !string.IsNullOrWhiteSpace(_localDetails))
        {
            AppendLine(text, "");
            AppendLine(text, "Local details (may contain project or language data):");
            foreach (var line in _localDetails.Split('\n')) AppendLine(text, "  " + line.TrimEnd('\r'));
        }
        return text.ToString();
    }

    /// <summary>Creates a GitHub issue form with the reviewed report in its body.</summary>
    /// <param name="reviewedText">The exact text shown in the report preview.</param>
    public Uri IssueUri(string reviewedText)
    {
        ArgumentNullException.ThrowIfNull(reviewedText);
        var title = "Problem report for Motif " + MotifVersion;
        return new Uri(AppLinks.NewIssue + "?title=" + Uri.EscapeDataString(title) +
            "&body=" + Uri.EscapeDataString(reviewedText));
    }

    private static ProblemReport Create(string operation, string code, string exitStatus, string stack,
        IReadOnlyDictionary<string, string> facts, string? localDetails) =>
        new(MotifProductVersion.CurrentText, ReportPanGlossVersion.Read(), SafeOperatingSystem(RuntimeInformation.OSDescription),
            operation, SafeCode.IsMatch(code) ? code : "unavailable", exitStatus, stack,
            SafeFactsFrom(facts), localDetails);

    private static string ExitStatusFor(FailureReason? reason) => reason is { } value
        ? "CLI equivalent " + FailureEnvelope.ExitCodeFor(value).ToString(CultureInfo.InvariantCulture)
        : "Unavailable (in-process window action)";

    private static IReadOnlyDictionary<string, string> SafeFactsFrom(IReadOnlyDictionary<string, string> facts)
    {
        var safe = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in facts)
        {
            switch (key)
            {
                case "parserNotFound" when value == "true":
                    safe["parserNotFound"] = "true";
                    break;
                case "exitCode" when int.TryParse(value, NumberStyles.AllowLeadingSign,
                    CultureInfo.InvariantCulture, out var code) && code is >= -255 and <= 255:
                    safe["PanGloss exit status"] = code.ToString(CultureInfo.InvariantCulture);
                    break;
                case "capMinutes" when decimal.TryParse(value, NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture, out var minutes) && minutes is >= 0 and <= 100000:
                    safe["timeLimitMinutes"] = minutes.ToString(CultureInfo.InvariantCulture);
                    break;
                case "exceptionHResult" when Regex.IsMatch(value, @"\A0x[0-9A-Fa-f]{8}\z", RegexOptions.CultureInvariant):
                    safe["exceptionHResult"] = value.ToUpperInvariant();
                    break;
                case "exceptionType":
                    var name = value.Split('.').LastOrDefault() ?? string.Empty;
                    if (SafeExceptionNames.Contains(name)) safe["exceptionType"] = name;
                    break;
                case "baselinePublicationPhase" when SafeIdentifier.IsMatch(value):
                    safe["baselinePublicationPhase"] = value;
                    break;
            }
        }
        return new ReadOnlyDictionary<string, string>(safe);
    }

    private static string SanitizedStackFor(Exception exception)
    {
        var lines = new List<string>();
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            lines.Add(SafeExceptionName(current.GetType()) + ":");
            try
            {
                foreach (var frame in new StackTrace(current, false).GetFrames() ?? [])
                {
                    var method = frame.GetMethod();
                    var declaring = method?.DeclaringType;
                    var ns = declaring?.Namespace;
                    if (method is null || declaring is null || !ReportStackNamespacePolicy.IsKnown(ns)) continue;
                    var typeName = SafeIdentifier.IsMatch(declaring.Name) ? declaring.Name : "type";
                    var methodName = SafeIdentifier.IsMatch(method.Name) ? method.Name : "method";
                    lines.Add(typeName + "." + methodName);
                    if (lines.Count >= 41) break;
                }
            }
            catch (Exception)
            {
                lines.Add("frames unavailable");
            }
        }
        return lines.Count == 0 ? "Unavailable" : string.Join(Environment.NewLine, lines.Take(41));
    }

    private static string SafeExceptionName(Type type)
    {
        var name = type.Name;
        return SafeExceptionNames.Contains(name) ? name : "Exception";
    }

    private static string SafeVersionText(string value) => SafeVersion.IsMatch(value) ? value : "unknown";

    private static string SafeOperatingSystem(string value) =>
        string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static void AppendLine(StringBuilder text, string line) => text.Append(line).Append("\r\n");

    private static readonly IReadOnlyDictionary<string, string> EmptyFacts =
        new ReadOnlyDictionary<string, string>(new Dictionary<string, string>());

    private static class ReportPanGlossVersion
    {
        /// <summary>Reads a bundled version or the checkout's declared release pin.</summary>
        public static string Read()
        {
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(PanGlossExecutable.PathVariable)))
                return "unknown (custom executable)";
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null;
                 directory = directory.Parent)
            {
                var packaged = ReadPackageManifest(Path.Combine(directory.FullName, "release-manifest.json"));
                if (packaged is not null) return packaged;
                var pinPath = Path.Combine(directory.FullName, "pangloss-release.json");
                if (File.Exists(pinPath))
                    return ReadVersion(pinPath) is { } pinned ? pinned + " (release pin)" : "unknown";
            }
            return "unknown";
        }

        private static string? ReadPackageManifest(string path)
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(path));
                if (!document.RootElement.TryGetProperty("dependencies", out var dependencies) ||
                    dependencies.ValueKind != JsonValueKind.Array) return null;
                foreach (var dependency in dependencies.EnumerateArray())
                    if (dependency.TryGetProperty("name", out var name) && name.GetString() == "PanGloss" &&
                        dependency.TryGetProperty("version", out var version) && version.GetString() is { } text &&
                        SafeVersion.IsMatch(text)) return text;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
            {
            }
            return null;
        }

        private static string? ReadVersion(string path)
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(path));
                return document.RootElement.TryGetProperty("version", out var version) &&
                       version.GetString() is { } text && SafeVersion.IsMatch(text) ? text : null;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
            {
                return null;
            }
        }
    }
}
