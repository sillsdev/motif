using System.Globalization;

namespace SIL.Motif.Host.PanGloss;

/// <summary>A <c>MOTIF_PARSER_MEMORY_LIMIT</c> value that is neither a byte count, a binary-suffixed count nor none.</summary>
public sealed class PanGlossMemoryLimitSettingException(string message) : Exception(message);

/// <summary>
/// The memory ceiling for one parser run: the point at which Motif stops PanGloss so the rest of the machine,
/// which other programs and agents share, keeps its memory (ADR 0048).
/// </summary>
/// <remarks>
/// The setting is the environment variable named by <see cref="Variable"/>. Unset, the ceiling is
/// <see cref="DefaultFraction"/> of the memory the runtime reports as available to this process, which is
/// physical memory unless a container or cgroup limits the process, in which case it is that limit.
/// The value is a byte count, a count with a binary suffix (<c>KiB</c>, <c>MiB</c>, <c>GiB</c>, <c>TiB</c>), or
/// <c>none</c>, which removes the ceiling entirely.
/// </remarks>
public static class PanGlossMemoryLimit
{
    /// <summary>The environment variable that sets the ceiling.</summary>
    public const string Variable = "MOTIF_PARSER_MEMORY_LIMIT";

    /// <summary>The share of physical memory used as the ceiling when the variable is unset.</summary>
    public const double DefaultFraction = 0.75;

    private const ulong BytesPerGibibyte = 1024UL * 1024 * 1024;

    private static readonly (string Suffix, ulong Scale)[] Suffixes =
    [
        ("KiB", 1UL << 10), ("MiB", 1UL << 20), ("GiB", 1UL << 30), ("TiB", 1UL << 40),
    ];

    /// <summary>The ceiling the environment asks for; null means none. Throws when the value is invalid.</summary>
    public static ulong? FromEnvironment() =>
        Resolve(Environment.GetEnvironmentVariable(Variable), PhysicalMemoryBytes());

    /// <summary>The memory the runtime reports as available to this process; total physical memory when unconfined.</summary>
    public static ulong PhysicalMemoryBytes() =>
        (ulong)Math.Max(1, GC.GetGCMemoryInfo().TotalAvailableMemoryBytes);

    /// <summary>Turns a setting into a ceiling in bytes, or null for none; an unset setting takes the default.</summary>
    public static ulong? Resolve(string? setting, ulong physicalMemoryBytes)
    {
        if (string.IsNullOrWhiteSpace(setting)) return (ulong)(physicalMemoryBytes * DefaultFraction);
        var text = setting.Trim();
        if (string.Equals(text, "none", StringComparison.OrdinalIgnoreCase)) return null;
        var digits = text;
        var scale = 1UL;
        foreach (var (suffix, suffixScale) in Suffixes)
        {
            if (!text.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) continue;
            digits = text[..^suffix.Length];
            scale = suffixScale;
            break;
        }
        if (ulong.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var count) && count > 0 &&
            count <= ulong.MaxValue / scale)
            return count * scale;
        throw new PanGlossMemoryLimitSettingException(
            $"{Variable} is '{setting}', which is not a memory limit. Use a number of bytes such as 12884901888, " +
            "a number with a binary suffix such as 12GiB or 512MiB (KiB, MiB, GiB and TiB), or none to remove the " +
            "limit. Unset, the limit is 75% of the memory available to Motif.");
    }

    /// <summary>A byte count in gibibytes, for a message a person reads.</summary>
    public static string Format(ulong bytes) =>
        (bytes / (double)BytesPerGibibyte).ToString("0.##", CultureInfo.InvariantCulture) + " GiB";
}

/// <summary>A parser run Motif stopped for memory: the ceiling applied and the peak measured, when known.</summary>
/// <param name="LimitBytes">The ceiling the run was held to.</param>
/// <param name="PeakBytes">The largest memory the run was seen to hold, or null when the platform cannot say.</param>
/// <param name="Inferred">
/// True when the stop is inferred from a failed run's peak rather than reported by the platform, so the message says
/// the run was likely stopped by the limit.
/// </param>
public sealed record PanGlossMemoryKill(ulong LimitBytes, ulong? PeakBytes, bool Inferred = false)
{
    // An allocation past the limit fails rather than succeeding, so the peak settles just under it.
    private const double JobPeakShare = 0.95;

    /// <summary>
    /// A heuristic for Windows: a job-object run that failed with its peak within 5% of the limit is read as likely
    /// stopped for memory. The job's own limit-violation record (JOBOBJECT_LIMIT_VIOLATION_INFORMATION) would say so
    /// exactly and is the intended replacement.
    /// </summary>
    public static PanGlossMemoryKill? FromJobPeak(ulong limitBytes, ulong peakBytes, int exitCode) =>
        exitCode != 0 && peakBytes >= limitBytes * JobPeakShare
            ? new PanGlossMemoryKill(limitBytes, peakBytes, Inferred: true) : null;

    /// <summary>One plain statement of what happened and what to do about it, for a refusal's message.</summary>
    public string Describe() =>
        (Inferred
            ? "The parser failed after using nearly all the memory Motif allows, so it was likely stopped by the memory " +
              $"limit. The limit was {PanGlossMemoryLimit.Format(LimitBytes)}"
            : $"The parser used more memory than Motif allows and was stopped. The limit was {PanGlossMemoryLimit.Format(LimitBytes)}") +
        (PeakBytes is { } peak ? $" and the parser had reached {PanGlossMemoryLimit.Format(peak)}. " : ". ") +
        "Run the parser with fewer threads or a per-word step limit. The limit protects the rest of this computer; " +
        $"to raise it or remove it, set {PanGlossMemoryLimit.Variable} to a number of bytes or to none.";
}
