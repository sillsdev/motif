using Avalonia;
using Avalonia.Headless;
using SIL.Motif.Host;
using SIL.Motif.Host.Installation;
using Velopack;

namespace SIL.Motif.App;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        MotifInstallLifecycle.ConfigureForStartup(VelopackApp.Build(), args).Run();
        if (IsVelopackFastExitHook(args))
            return 0;

        // Motif's own error window reports an escaped error; Windows' dialog must not appear beside it.
        CrashDialogs.Suppress();
        MotifInstallLifecycle.CompleteStartup();
        if (args.Length > 0 && args[0] == "--smoke")
            return Smoke();
        if (args.Length > 0 && args[0] == "--cli")
            return RunCli(args[1..]);
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    private static bool IsVelopackFastExitHook(string[] args) =>
        args.Length > 0 && args[0] is
            "--veloapp-install" or "--veloapp-updated" or "--veloapp-obsolete" or "--veloapp-uninstall";

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();

    private static int Smoke()
    {
        AppBuilder.Configure<App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .SetupWithoutStarting();
        var window = new Views.MainWindow();
        window.Close();
        return 0;
    }

    private static int RunCli(string[] args)
    {
        var executable = Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "motif.exe" : "motif");
        if (!File.Exists(executable))
            return 127;
        var start = new System.Diagnostics.ProcessStartInfo(executable) { UseShellExecute = false };
        foreach (var argument in args)
            start.ArgumentList.Add(argument);
        using var process = System.Diagnostics.Process.Start(start);
        if (process is null)
            return 127;
        process.WaitForExit();
        return process.ExitCode;
    }
}
