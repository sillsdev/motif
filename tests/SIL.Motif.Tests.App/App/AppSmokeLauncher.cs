using System.Diagnostics;
using SIL.Motif.Tests.TestFixtures;

namespace SIL.Motif.Tests.App;

internal static class AppSmokeLauncher
{
    public static Process Start()
    {
        var app = Path.Combine(BuildOutput.ProductDirectory,
            OperatingSystem.IsWindows() ? "SIL.Motif.App.exe" : "SIL.Motif.App");
        var start = new ProcessStartInfo(app, "--smoke")
        {
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.Environment.Remove("ICU_DATA");
        return Process.Start(start)!;
    }
}
