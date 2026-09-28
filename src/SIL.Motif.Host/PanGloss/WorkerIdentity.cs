using System.Runtime.InteropServices;
using System.Security.Principal;

namespace SIL.Motif.Host.PanGloss;

/// <summary>Derives a stable per-user namespace for worker ownership.</summary>
public static partial class WorkerIdentity
{
    /// <summary>Returns the current Windows SID or Unix effective-user id namespace.</summary>
    public static string GetCurrentUserNamespace()
    {
        if (OperatingSystem.IsWindows())
            return WindowsIdentity.GetCurrent().User?.Value ?? "unknown-user";
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            return "uid-" + GetEffectiveUserId().ToString(System.Globalization.CultureInfo.InvariantCulture);
        return "unknown-user";
    }

    [LibraryImport("libc", EntryPoint = "geteuid")]
    private static partial uint GetEffectiveUserId();
}
