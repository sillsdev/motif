using Avalonia;
using SIL.Motif.Host;

namespace SIL.Motif.App;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // Motif's own error window reports an escaped error; Windows' dialog must not appear beside it.
        CrashDialogs.Suppress();
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
