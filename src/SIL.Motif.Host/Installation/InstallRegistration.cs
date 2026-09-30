using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace SIL.Motif.Host.Installation;

internal static class InstallRegistration
{
    private const string RegistryKeyPath = "Software\\SIL\\Motif";
    private const string UserEnvironmentKeyPath = "Environment";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    internal static void RegisterCurrent()
    {
        if (OperatingSystem.IsWindows())
        {
            RegisterWindows();
            return;
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(home))
            throw new InvalidOperationException("The current user's home directory could not be resolved.");
        var shimPath = Path.Combine(home, ".local", "bin", "motif");
        var appImagePath = OperatingSystem.IsLinux()
            ? Environment.GetEnvironmentVariable("APPIMAGE")
            : null;
        var installDirectory = appImagePath is not null
            ? Path.GetFullPath(appImagePath)
            : OperatingSystem.IsMacOS() ? MacAppBundlePath() : Path.GetFullPath(AppContext.BaseDirectory);
        var cliPath = appImagePath is not null
            ? shimPath
            : OperatingSystem.IsMacOS()
                ? Path.Combine(installDirectory, "Contents", "MacOS", "motif")
                : Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "motif.exe" : "motif");
        var configDirectory = OperatingSystem.IsMacOS()
            ? Path.Combine(home, "Library", "Application Support", "SIL", "Motif")
            : Path.Combine(
                Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } xdg
                    ? Path.GetFullPath(xdg)
                    : Path.Combine(home, ".config"),
                "SIL", "Motif");

        RegisterUnix(installDirectory, cliPath, Path.Combine(configDirectory, "install.json"), shimPath, appImagePath);
    }

    internal static string UnregisterCurrent()
    {
        if (OperatingSystem.IsWindows())
            return UnregisterWindows();

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var shimPath = Path.Combine(home, ".local", "bin", "motif");
        var configDirectory = OperatingSystem.IsMacOS()
            ? Path.Combine(home, "Library", "Application Support", "SIL", "Motif")
            : Path.Combine(
                Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } xdg
                    ? Path.GetFullPath(xdg)
                    : Path.Combine(home, ".config"),
                "SIL", "Motif");
        return UnregisterUnix(Path.Combine(configDirectory, "install.json"), shimPath)
            ? "Unix registration removed."
            : "Unix registration was absent.";
    }

    internal static string AddUserPathEntry(string? currentPath, string installDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installDirectory);
        var entry = Path.GetFullPath(installDirectory);
        var entries = SplitPath(currentPath);
        if (entries.Any(candidate => PathsEqual(candidate, entry)))
            return currentPath ?? string.Empty;
        return currentPath is null or "" ? entry : currentPath + Path.PathSeparator + entry;
    }

    internal static string RemoveUserPathEntry(string? currentPath, string installDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installDirectory);
        var entry = Path.GetFullPath(installDirectory);
        return string.Join(Path.PathSeparator, SplitPath(currentPath)
            .Where(candidate => !PathsEqual(candidate, entry)));
    }

    internal static bool KeepPathOwnership(
        string? previousInstallDirectory, string installDirectory, bool previousPathEntryAdded) =>
        previousPathEntryAdded && previousInstallDirectory is not null &&
        PathsEqual(previousInstallDirectory, installDirectory);

    internal static void RegisterUnix(
        string installDirectory,
        string cliPath,
        string configPath,
        string shimPath,
        string? appImagePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(cliPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(configPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(shimPath);

        var fullInstallDirectory = Path.GetFullPath(installDirectory);
        var fullCliPath = Path.GetFullPath(cliPath);
        var fullConfigPath = Path.GetFullPath(configPath);
        var fullShimPath = Path.GetFullPath(shimPath);
        var shim = BuildUnixShim(fullCliPath, appImagePath is null ? null : Path.GetFullPath(appImagePath));
        var shimHash = HashText(shim);

        if (File.Exists(fullShimPath))
        {
            var previous = ReadRecord(fullConfigPath);
            if (previous is null || !PathsEqual(previous.ShimPath, fullShimPath) ||
                !string.Equals(previous.ShimSha256, HashFile(fullShimPath), StringComparison.Ordinal))
            {
                throw new IOException("The motif CLI shim already exists and is not owned by this installation.");
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(fullShimPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(fullConfigPath)!);
        File.WriteAllText(fullShimPath, shim, new UTF8Encoding(false));
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(fullShimPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);

        var record = new InstallRecord(1, fullInstallDirectory, fullCliPath, fullShimPath, shimHash);
        File.WriteAllText(fullConfigPath, JsonSerializer.Serialize(record, JsonOptions), new UTF8Encoding(false));
    }

    internal static bool UnregisterUnix(string configPath, string shimPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(shimPath);
        var fullConfigPath = Path.GetFullPath(configPath);
        var fullShimPath = Path.GetFullPath(shimPath);
        var record = ReadRecord(fullConfigPath);
        if (record is null)
            return false;
        if (record.SchemaVersion != 1)
            throw new InvalidDataException("The Motif install record has an unsupported shape.");

        if (PathsEqual(record.ShimPath, fullShimPath) && File.Exists(fullShimPath) &&
            string.Equals(record.ShimSha256, HashFile(fullShimPath), StringComparison.Ordinal))
        {
            File.Delete(fullShimPath);
        }
        File.Delete(fullConfigPath);
        return true;
    }

    internal static string BuildUnixShim(string cliPath, string? appImagePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cliPath);
        if (appImagePath is null)
            return "#!/bin/sh\nexec " + ShellQuote(Path.GetFullPath(cliPath)) + " \"$@\"\n";

        var image = ShellQuote(Path.GetFullPath(appImagePath));
        return "#!/bin/sh\nif [ ! -f " + image + " ]; then\n" +
            "  printf '%s\\n' 'Motif is not installed; run motif uninstall to remove its shell command.' >&2\n" +
            "  exit 127\nfi\nexec " + image + " --appimage-extract-and-run --cli \"$@\"\n";
    }

    private static string[] SplitPath(string? path) =>
        (path ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static bool PathsEqual(string left, string right) =>
        string.Equals(Path.GetFullPath(Environment.ExpandEnvironmentVariables(left)),
            Path.GetFullPath(Environment.ExpandEnvironmentVariables(right)),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static string ShellQuote(string value) => "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";

    private static string HashText(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static string HashFile(string path) =>
        Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));

    private static InstallRecord? ReadRecord(string path)
    {
        if (!File.Exists(path))
            return null;
        return JsonSerializer.Deserialize<InstallRecord>(File.ReadAllText(path));
    }

    [SupportedOSPlatform("windows")]
    private static void RegisterWindows()
    {
        var installDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(AppContext.BaseDirectory));
        var cliPath = Path.Combine(installDirectory, "motif.exe");
        using var motifKey = Registry.CurrentUser.CreateSubKey(RegistryKeyPath, writable: true)
            ?? throw new InvalidOperationException("Could not create the Motif discovery registry key.");
        var previousInstallDirectory = motifKey.GetValue("InstallDir") as string;
        var keepPathOwnership = KeepPathOwnership(
            previousInstallDirectory,
            installDirectory,
            string.Equals(motifKey.GetValue("PathEntryAdded") as string, "true", StringComparison.OrdinalIgnoreCase));
        var previousPathWasMissing = string.Equals(
            motifKey.GetValue("PathWasMissing") as string, "true", StringComparison.OrdinalIgnoreCase);
        var previousPathKind = motifKey.GetValue("PathKind") as string;
        motifKey.SetValue("InstallDir", installDirectory, RegistryValueKind.String);
        motifKey.SetValue("CliPath", cliPath, RegistryValueKind.String);

        using var environmentKey = Registry.CurrentUser.CreateSubKey(UserEnvironmentKeyPath, writable: true)
            ?? throw new InvalidOperationException("Could not open the current user's environment registry key.");
        var originalPath = environmentKey.GetValue("Path", null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
        var updatedPath = AddUserPathEntry(originalPath, installDirectory);
        var pathEntryAdded = keepPathOwnership ||
            !string.Equals(originalPath ?? string.Empty, updatedPath, StringComparison.Ordinal);
        var pathWasMissing = keepPathOwnership ? previousPathWasMissing : originalPath is null;
        var pathKind = keepPathOwnership && Enum.TryParse<RegistryValueKind>(previousPathKind, out var savedPathKind)
            ? savedPathKind
            : environmentKey.GetValueNames().Contains("Path", StringComparer.OrdinalIgnoreCase)
            ? environmentKey.GetValueKind("Path")
            : RegistryValueKind.String;
        motifKey.SetValue("PathEntryAdded", pathEntryAdded ? "true" : "false", RegistryValueKind.String);
        motifKey.SetValue("PathWasMissing", pathWasMissing ? "true" : "false", RegistryValueKind.String);
        motifKey.SetValue("PathKind", pathKind.ToString(), RegistryValueKind.String);
        if (pathEntryAdded)
        {
            environmentKey.SetValue("Path", updatedPath, pathKind);
            BroadcastEnvironmentChange();
        }
    }

    [SupportedOSPlatform("windows")]
    private static string UnregisterWindows()
    {
        string initialState;
        using (var motifKey = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, writable: true))
        {
            if (motifKey is null)
                return "Windows discovery key was absent.";

            var values = motifKey.GetValueNames()
                .Select(name => $"{name}={motifKey.GetValue(name)}");
            var subKeys = motifKey.GetSubKeyNames();
            initialState = $"values=[{string.Join(", ", values)}]; subkeys=[{string.Join(", ", subKeys)}]";

            var installDirectory = motifKey.GetValue("InstallDir") as string;
            if (installDirectory is not null &&
                string.Equals(motifKey.GetValue("PathEntryAdded") as string, "true", StringComparison.OrdinalIgnoreCase))
            {
                using var environmentKey = Registry.CurrentUser.OpenSubKey(UserEnvironmentKeyPath, writable: true);
                if (environmentKey is not null)
                {
                    var currentPath = environmentKey.GetValue(
                        "Path", null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
                    var updatedPath = RemoveUserPathEntry(currentPath, installDirectory);
                    if (!string.Equals(currentPath ?? string.Empty, updatedPath, StringComparison.Ordinal))
                    {
                        var pathWasMissing = string.Equals(
                            motifKey.GetValue("PathWasMissing") as string, "true", StringComparison.OrdinalIgnoreCase);
                        if (pathWasMissing && updatedPath.Length == 0)
                            environmentKey.DeleteValue("Path", throwOnMissingValue: false);
                        else if (Enum.TryParse<RegistryValueKind>(motifKey.GetValue("PathKind") as string, out var pathKind))
                            environmentKey.SetValue("Path", updatedPath, pathKind);
                        else
                            environmentKey.SetValue("Path", updatedPath, RegistryValueKind.String);
                        BroadcastEnvironmentChange();
                    }
                }
            }
        }

        Registry.CurrentUser.DeleteSubKeyTree(RegistryKeyPath, throwOnMissingSubKey: false);
        return "Windows discovery key tree removed; before removal " + initialState + ".";
    }

    private static string MacAppBundlePath()
    {
        var directory = new DirectoryInfo(Path.TrimEndingDirectorySeparator(Path.GetFullPath(AppContext.BaseDirectory)));
        for (var i = 0; i < 8 && directory is not null; i++, directory = directory.Parent)
        {
            if (directory.Name.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
                return directory.FullName;
        }
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(AppContext.BaseDirectory));
    }

    internal static bool IsRunningFromMacAppBundle() =>
        !PathsEqual(MacAppBundlePath(), Path.TrimEndingDirectorySeparator(Path.GetFullPath(AppContext.BaseDirectory)));

    private static void BroadcastEnvironmentChange()
    {
        SendMessageTimeout(new IntPtr(0xffff), 0x001A, UIntPtr.Zero, "Environment", 0x0002, 2000, out _);
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = false)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr window, uint message, UIntPtr wParam, string lParam, uint flags, uint timeout, out UIntPtr result);

    private sealed record InstallRecord(
        int SchemaVersion,
        string InstallDirectory,
        string CliPath,
        string ShimPath,
        string ShimSha256);
}
