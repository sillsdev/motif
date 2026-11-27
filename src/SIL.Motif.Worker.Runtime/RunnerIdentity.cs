using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using SIL.Motif.Host;

namespace SIL.Motif.Worker;

/// <summary>
/// Names the build of the runner that owns a root, so a launch from another build can say so instead of
/// leaving that runner to claim its jobs silently.
/// </summary>
/// <remarks>
/// The runner is a per-user singleton: a launch that finds one alive starts nothing, so an older build left
/// running keeps claiming a newer build's jobs. The owner writes this file once it holds ownership; a
/// launching process reads it and warns when the pid is alive and the build differs. Pinned by
/// `ALiveRunnerFromAnotherBuildIsNamedAndWarnedAboutWhenAnotherLaunchFindsIt`.
/// </remarks>
public static class RunnerIdentity
{
    /// <summary>This build: its product version, source revision and assembly identity.</summary>
    public static string BuildId { get; } =
        MotifProductVersion.DisplayText + " [" + typeof(MotifProductVersion).Assembly.ManifestModule.ModuleVersionId + "]";

    /// <summary>Where the owner of <paramref name="root"/> records its identity.</summary>
    public static string PathFor(string root) => Path.Combine(root, "runner-identity");

    /// <summary>Records this process as the owner of <paramref name="root"/>; best-effort.</summary>
    public static void Publish(string root)
    {
        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(PathFor(root), Environment.ProcessId.ToString(CultureInfo.InvariantCulture) + "\n" + BuildId + "\n" +
                RunnerProcess.SelfStartText);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>The live runner <paramref name="root"/> records, or null when none is recorded or its process has exited.</summary>
    public static (int Pid, string Build, string StartedUtc)? ReadLive(string root, Func<int, bool>? isAlive = null)
    {
        string[] lines;
        try
        {
            var path = PathFor(root);
            if (!File.Exists(path)) return null;
            lines = File.ReadAllLines(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
        if (lines.Length < 2 || !int.TryParse(lines[0], NumberStyles.None, CultureInfo.InvariantCulture, out var pid) ||
            !(isAlive ?? IsAlive)(pid))
            return null;
        return (pid, lines[1].Trim(), lines.Length > 2 ? lines[2].Trim() : string.Empty);
    }

    /// <summary>Removes the record of <paramref name="root"/> when it names <paramref name="pid"/>; best-effort.</summary>
    public static void Remove(string root, int pid)
    {
        try
        {
            var path = PathFor(root);
            if (!File.Exists(path)) return;
            var lines = File.ReadAllLines(path);
            if (lines.Length > 0 && lines[0].Trim() == pid.ToString(CultureInfo.InvariantCulture)) File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>The warning for a live runner of another build holding <paramref name="root"/>, or null.</summary>
    public static string? StaleWarning(string root)
    {
        string[] lines;
        try
        {
            var path = PathFor(root);
            if (!File.Exists(path)) return null;
            lines = File.ReadAllLines(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
        if (lines.Length < 2 || !int.TryParse(lines[0], NumberStyles.None, CultureInfo.InvariantCulture, out var pid))
            return null;
        var build = lines[1].Trim();
        if (string.Equals(build, BuildId, StringComparison.Ordinal) || !IsAlive(pid)) return null;
        return $"warning: a SIL.Motif.Worker from a different build (pid {pid}, build {build}) is running on " +
            $"this root and will claim this build's jobs ({BuildId}); stop it with `motif worker stop` (or kill {pid}) so a matching runner " +
            "starts, or its jobs may fail.";
    }

    private static bool IsAlive(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }
}
