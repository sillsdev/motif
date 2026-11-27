using Velopack;
using Velopack.Locators;

namespace SIL.Motif.Host.Installation;

/// <summary>Connects Motif's per-user registration to Velopack startup and uninstall callbacks.</summary>
public static class MotifInstallLifecycle
{
    /// <summary>Runs Velopack hooks without allowing a downloaded update to apply during startup.</summary>
    /// <param name="args">The current process arguments passed to Velopack hook handling.</param>
    public static void Initialize(string[] args)
    {
        RunVelopackHooks(args);
        CompleteStartup();
    }

    /// <summary>Runs Velopack's startup hooks only for an installed package.</summary>
    /// <remarks>
    /// A development build has no package, and Velopack's run then appends a warning to /tmp/velopack.log on
    /// every start (issue 516), so it is not run there. A Velopack hook argument always runs it.
    /// </remarks>
    /// <param name="args">The current process arguments passed to Velopack hook handling.</param>
    public static void RunVelopackHooks(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (ShouldRunVelopack(args, IsInstalledPackage()))
            ConfigureForStartup(VelopackApp.Build(), args).Run();
    }

    /// <summary>Decides whether Velopack's startup hooks run for these arguments.</summary>
    /// <param name="args">The current process arguments.</param>
    /// <param name="installedPackage">Whether the process runs from an installed package.</param>
    public static bool ShouldRunVelopack(IReadOnlyList<string> args, bool installedPackage)
    {
        ArgumentNullException.ThrowIfNull(args);
        return installedPackage || (args.Count > 0 && args[0].StartsWith("--veloapp-", StringComparison.Ordinal));
    }

    /// <summary>Whether this process runs from an AppImage, a Velopack Windows install or a macOS app bundle.</summary>
    public static bool IsInstalledPackage()
    {
        if (OperatingSystem.IsLinux())
            return !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("APPIMAGE"));
        if (OperatingSystem.IsMacOS())
            return InstallRegistration.IsRunningFromMacAppBundle();
        if (OperatingSystem.IsWindows())
        {
            var parent = Directory.GetParent(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory))?.FullName;
            return parent is not null && File.Exists(Path.Combine(parent, "Update.exe"));
        }

        return false;
    }

    /// <summary>Configures the app's Velopack callbacks and returns its builder for the entry point to run.</summary>
    /// <param name="app">The Velopack app builder created by the executable entry point.</param>
    /// <param name="args">The current process arguments passed to Velopack hook handling.</param>
    public static VelopackApp ConfigureForStartup(VelopackApp app, string[] args)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(args);
        app.SetArgs(args)
            .SetAutoApplyOnStartup(false)
            .OnFirstRun(_ => RegisterPackagedInstall())
            .OnRestarted(_ => RegisterPackagedInstall());
        if (OperatingSystem.IsWindows())
        {
            app.OnBeforeUninstallFastCallback(_ => RunUninstallCallback());
            app.OnAfterInstallFastCallback(_ => InstallRegistration.RegisterCurrent());
        }
        return app;
    }

    /// <summary>Registers an installed package after Velopack has handled startup arguments.</summary>
    public static void CompleteStartup()
    {
        RegisterPackagedInstall();
    }

    /// <summary>Removes Motif's discovery record and shell command while retaining user data.</summary>
    public static string RemoveRegistration()
    {
        MotifAgentPackageStore.ForInstallation().Remove();
        InstallRegistration.UnregisterCurrent();
        var dataDirectory = Path.Combine(
            UserDataHome.Path, "SIL", "Motif");
        return "Motif's shell command and install record were removed. User data remains at " +
            dataDirectory + ". Remove the installed app or AppImage separately.";
    }

    private static void RegisterPackagedInstall()
    {
        MotifAgentPackageStore.ForInstallation().Refresh();
        if (OperatingSystem.IsWindows())
        {
            if (VelopackLocator.Current.CurrentlyInstalledVersion is not null)
                InstallRegistration.RegisterCurrent();
            return;
        }

        if (OperatingSystem.IsLinux() && !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("APPIMAGE")))
        {
            InstallRegistration.RegisterCurrent();
            return;
        }

        if (OperatingSystem.IsMacOS() && InstallRegistration.IsRunningFromMacAppBundle())
            InstallRegistration.RegisterCurrent();
    }

    private static void RunUninstallCallback()
    {
        WriteUninstallTrace("callback started");
        try
        {
            MotifAgentPackageStore.ForInstallation().Remove();
            var registration = OperatingSystem.IsWindows()
                ? InstallRegistration.UnregisterCurrent()
                : "The Motif assistant package was removed.";
            WriteUninstallTrace("callback completed: " + registration);
        }
        catch (Exception exception)
        {
            WriteUninstallTrace($"callback failed: {exception.GetType().Name}: {exception.Message}");
            throw;
        }
    }

    private static void WriteUninstallTrace(string message)
    {
        var tracePath = Environment.GetEnvironmentVariable("MOTIF_PACKAGE_UNINSTALL_TRACE");
        if (string.IsNullOrWhiteSpace(tracePath))
            return;
        try
        {
            File.AppendAllText(tracePath, message + Environment.NewLine);
        }
        catch (Exception exception) when (exception is
            IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
        }
    }
}
