using System.Diagnostics;
using System.Globalization;

namespace SIL.Motif.Host.Assistants;

/// <summary>Provides a process start identity that is stable when read by different Motif processes.</summary>
public static class TunnelProcessIdentity
{
    /// <summary>Returns Linux kernel start ticks or the UTC process start time on other platforms.</summary>
    public static long Start(Process process)
    {
        if (!OperatingSystem.IsLinux()) return process.StartTime.ToUniversalTime().Ticks;
        var stat = File.ReadAllText($"/proc/{process.Id}/stat");
        var end = stat.LastIndexOf(')');
        if (end < 0) throw new InvalidOperationException();
        var fields = stat[(end + 2)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return long.Parse(fields[19], CultureInfo.InvariantCulture);
    }
}
