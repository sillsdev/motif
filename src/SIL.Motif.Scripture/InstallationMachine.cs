using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace SIL.Motif.Scripture;

/// <summary>
/// What tells this computer and user apart from another for <see cref="ScriptureLocalStore.InstallationId"/>: the host
/// name, the operating-system user and the system's own machine id. The machine id is the Windows
/// <c>HKLM\SOFTWARE\Microsoft\Cryptography\MachineGuid</c>, the macOS <c>IOPlatformUUID</c>, or the Linux
/// <c>/etc/machine-id</c> (or <c>/var/lib/dbus/machine-id</c>). It is set when the operating system is installed, so two
/// computers with the same host and user name still differ.
/// </summary>
/// <param name="HostName">The computer's name.</param>
/// <param name="UserName">The operating-system user.</param>
/// <param name="SystemId">The system's machine id; null when none can be read.</param>
/// <param name="SystemIdSource">Where <paramref name="SystemId"/> was read from, or why there is none.</param>
public sealed record InstallationMachine(string HostName, string UserName, string? SystemId, string SystemIdSource)
{
    private static readonly AsyncLocal<InstallationMachine?> Injected = new();

    /// <summary>
    /// Whether no machine id could be read, so the fingerprint is the host and user names only and two computers with
    /// the same names cannot be told apart.
    /// </summary>
    public bool IsWeak => string.IsNullOrEmpty(SystemId);

    /// <summary>The first 32 hex digits of a SHA-256 of the host name, user and machine id.</summary>
    public string Fingerprint =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(HostName + "\n" + UserName + "\n" + (SystemId ?? ""))))[..32];

    /// <summary>The computer this flow runs on, or the one <see cref="Use"/> put in place.</summary>
    public static InstallationMachine Current => Injected.Value ?? Read();

    /// <summary>Makes <paramref name="machine"/> this flow's computer until the result is disposed; tests stand in for another computer with it.</summary>
    public static IDisposable Use(InstallationMachine machine)
    {
        ArgumentNullException.ThrowIfNull(machine);
        var previous = Injected.Value;
        Injected.Value = machine;
        return new Restore(previous);
    }

    /// <summary>Reads this computer's names and machine id.</summary>
    public static InstallationMachine Read()
    {
        var (id, source) = OperatingSystem.IsWindows() ? WindowsMachineGuid()
            : OperatingSystem.IsMacOS() ? MacPlatformUuid()
            : LinuxMachineId();
        return new InstallationMachine(Environment.MachineName, Environment.UserName, id, source);
    }

    private const string NoMachineId = "none readable: host and user names only";

    [SupportedOSPlatform("windows")]
    private static (string?, string) WindowsMachineGuid()
    {
        const string key = @"SOFTWARE\Microsoft\Cryptography";
        try
        {
            // The 64-bit view, so a 32-bit process reads the same value.
            using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var cryptography = root.OpenSubKey(key);
            if (cryptography?.GetValue("MachineGuid") is string guid && !string.IsNullOrWhiteSpace(guid))
                return (guid.Trim(), @"HKLM\" + key + @"\MachineGuid");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
        return (null, NoMachineId);
    }

    private static (string?, string) MacPlatformUuid()
    {
        const string ioreg = "/usr/sbin/ioreg";
        try
        {
            if (!File.Exists(ioreg)) return (null, NoMachineId);
            var start = new ProcessStartInfo(ioreg)
            {
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true,
            };
            foreach (var argument in new[] { "-rd1", "-c", "IOPlatformExpertDevice" }) start.ArgumentList.Add(argument);
            start.Environment.Remove("ICU_DATA");
            using var process = Process.Start(start);
            if (process is null) return (null, NoMachineId);
            var output = process.StandardOutput.ReadToEndAsync();
            _ = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(TimeSpan.FromSeconds(10)))
            {
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                return (null, NoMachineId);
            }
            var match = Regex.Match(output.GetAwaiter().GetResult(), "\"IOPlatformUUID\"\\s*=\\s*\"([^\"]+)\"");
            return process.ExitCode == 0 && match.Success ? (match.Groups[1].Value, "IOPlatformUUID") : (null, NoMachineId);
        }
        catch (Exception exception) when (exception is IOException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return (null, NoMachineId);
        }
    }

    private static (string?, string) LinuxMachineId()
    {
        foreach (var file in new[] { "/etc/machine-id", "/var/lib/dbus/machine-id" })
        {
            try
            {
                if (File.Exists(file) && File.ReadAllText(file).Trim() is { Length: > 0 } id) return (id, file);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
        return (null, NoMachineId);
    }

    private sealed class Restore(InstallationMachine? previous) : IDisposable
    {
        public void Dispose() => Injected.Value = previous;
    }
}
