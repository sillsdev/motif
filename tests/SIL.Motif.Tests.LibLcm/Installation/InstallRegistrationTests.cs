using SIL.Motif.Host.Installation;
using Xunit;

namespace SIL.Motif.Tests.LibLcm.Installation;

public sealed class InstallRegistrationTests
{
    [Fact]
    public void AddingAnExistingInstallDirectoryDoesNotDuplicateTheUserPathEntry()
    {
        var current = OperatingSystem.IsWindows()
            ? @"C:\tools;C:\Apps\Motif"
            : "/usr/bin:/home/user/.local/opt/motif";
        var installDirectory = OperatingSystem.IsWindows() ? @"c:\apps\motif" : "/home/user/.local/opt/motif";

        var updated = InstallRegistration.AddUserPathEntry(current, installDirectory);

        Assert.Equal(current, updated);
    }

    [Fact]
    public void RemovingAnInstallDirectoryPreservesUnrelatedUserPathEntries()
    {
        var current = OperatingSystem.IsWindows()
            ? @"C:\tools;C:\Apps\Motif;D:\helpers"
            : "/usr/bin:/home/user/.local/opt/motif:/opt/helpers";
        var installDirectory = OperatingSystem.IsWindows() ? @"c:\apps\motif" : "/home/user/.local/opt/motif";
        var expected = OperatingSystem.IsWindows() ? @"C:\tools;D:\helpers" : "/usr/bin:/opt/helpers";

        var updated = InstallRegistration.RemoveUserPathEntry(current, installDirectory);

        Assert.Equal(expected, updated);
    }

    [Fact]
    public void RepeatedRegistrationKeepsOwnershipOfThePathEntryItAdded()
    {
        var installDirectory = Path.Combine(Path.GetTempPath(), "motif-install-root");

        Assert.True(InstallRegistration.KeepPathOwnership(installDirectory, installDirectory, true));
        Assert.False(InstallRegistration.KeepPathOwnership(installDirectory, installDirectory, false));
        Assert.False(InstallRegistration.KeepPathOwnership(
            installDirectory, installDirectory + "-moved", true));
    }

    [Fact]
    public void UnixRegistrationIsIdempotentAndUninstallKeepsUserData()
    {
        var temporary = Path.Combine(Path.GetTempPath(), "motif-install-" + Guid.NewGuid().ToString("N"));
        var installDirectory = Path.Combine(temporary, "Motif.app");
        var cliPath = Path.Combine(installDirectory, "Contents", "MacOS", "motif");
        var configPath = Path.Combine(temporary, "config", "install.json");
        var shimPath = Path.Combine(temporary, "bin", "motif");
        var userDataPath = Path.Combine(temporary, "data", "project.db");
        Directory.CreateDirectory(Path.GetDirectoryName(cliPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(userDataPath)!);
        File.WriteAllText(cliPath, "cli");
        File.WriteAllText(userDataPath, "user data");

        try
        {
            InstallRegistration.RegisterUnix(installDirectory, cliPath, configPath, shimPath, appImagePath: null);
            var firstShim = File.ReadAllText(shimPath);
            InstallRegistration.RegisterUnix(installDirectory, cliPath, configPath, shimPath, appImagePath: null);

            Assert.Equal(firstShim, File.ReadAllText(shimPath));
            Assert.True(InstallRegistration.UnregisterUnix(configPath, shimPath));
            Assert.False(File.Exists(shimPath));
            Assert.False(File.Exists(configPath));
            Assert.Equal("user data", File.ReadAllText(userDataPath));
        }
        finally
        {
            if (Directory.Exists(temporary))
                Directory.Delete(temporary, recursive: true);
        }
    }

    [Fact]
    public void UnixUninstallKeepsAUserModifiedShim()
    {
        var temporary = Path.Combine(Path.GetTempPath(), "motif-install-" + Guid.NewGuid().ToString("N"));
        var installDirectory = Path.Combine(temporary, "Motif.app");
        var cliPath = Path.Combine(installDirectory, "Contents", "MacOS", "motif");
        var configPath = Path.Combine(temporary, "config", "install.json");
        var shimPath = Path.Combine(temporary, "bin", "motif");
        Directory.CreateDirectory(Path.GetDirectoryName(cliPath)!);
        File.WriteAllText(cliPath, "cli");

        try
        {
            InstallRegistration.RegisterUnix(installDirectory, cliPath, configPath, shimPath, appImagePath: null);
            File.WriteAllText(shimPath, "user change");

            Assert.True(InstallRegistration.UnregisterUnix(configPath, shimPath));
            Assert.Equal("user change", File.ReadAllText(shimPath));
            Assert.False(File.Exists(configPath));
        }
        finally
        {
            if (Directory.Exists(temporary))
                Directory.Delete(temporary, recursive: true);
        }
    }

    [Fact]
    public void AppImageShimUsesExtractAndRunForRunnersWithoutFuse()
    {
        var shim = InstallRegistration.BuildUnixShim("/home/user/.local/bin/motif", "/tmp/motif.AppImage");

        Assert.Contains("--appimage-extract-and-run --cli", shim, StringComparison.Ordinal);
    }

    [Fact]
    public void AnAppImageDesktopEntryNamesTheWindowClassAndQuotesItsPath()
    {
        var entry = LinuxDesktopEntry.Build("/home/user/Apps/Motif \"beta\" $1.AppImage");

        Assert.StartsWith("[Desktop Entry]\n", entry, StringComparison.Ordinal);
        Assert.Contains("\nExec=\"/home/user/Apps/Motif \\\"beta\\\" \\$1.AppImage\" %f\n", entry, StringComparison.Ordinal);
        Assert.Contains("\nIcon=" + LinuxDesktopEntry.Name + "\n", entry, StringComparison.Ordinal);
        Assert.Contains("\nStartupWMClass=" + LinuxDesktopEntry.WindowClass + "\n", entry, StringComparison.Ordinal);
    }

    [Fact]
    public void AnAppImageDesktopEntryAndIconAreInstalledAndRemovedTogether()
    {
        var root = Path.Combine(Path.GetTempPath(), "motif-desktop-" + Guid.NewGuid().ToString("N"));
        try
        {
            var icon = Path.Combine(root, "payload", LinuxDesktopEntry.IconFileName);
            Directory.CreateDirectory(Path.GetDirectoryName(icon)!);
            File.WriteAllBytes(icon, [1, 2, 3]);
            var dataHome = Path.Combine(root, "share");

            LinuxDesktopEntry.Install(dataHome, Path.Combine(root, "Motif.AppImage"), icon);
            LinuxDesktopEntry.Install(dataHome, Path.Combine(root, "Motif.AppImage"), icon);

            Assert.True(File.Exists(Path.Combine(dataHome, "applications", LinuxDesktopEntry.Name + ".desktop")));
            Assert.Equal([1, 2, 3], File.ReadAllBytes(
                Path.Combine(dataHome, "icons", "hicolor", "256x256", "apps", LinuxDesktopEntry.Name + ".png")));
            Assert.True(LinuxDesktopEntry.Remove(dataHome));
            Assert.False(LinuxDesktopEntry.Remove(dataHome));
            Assert.Empty(Directory.EnumerateFiles(dataHome, "*", SearchOption.AllDirectories));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
