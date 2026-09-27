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
        ArgumentNullException.ThrowIfNull(args);
        var app = VelopackApp.Build()
            .SetArgs(args)
            .SetAutoApplyOnStartup(false)
            .OnFirstRun(_ => RegisterPackagedInstall())
            .OnRestarted(_ => RegisterPackagedInstall());
        if (OperatingSystem.IsWindows())
        {
            app.OnAfterInstallFastCallback(_ => InstallRegistration.RegisterCurrent())
                .OnBeforeUninstallFastCallback(_ => InstallRegistration.UnregisterCurrent());
        }
        app.Run();
        RegisterPackagedInstall();
    }

    /// <summary>Removes Motif's discovery record and shell command while retaining user data.</summary>
    public static string RemoveRegistration()
    {
        InstallRegistration.UnregisterCurrent();
        var dataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SIL", "Motif");
        return "Motif's shell command and install record were removed. User data remains at " +
            dataDirectory + ". Remove the installed app or AppImage separately.";
    }

    private static void RegisterPackagedInstall()
    {
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
}
