using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace SIL.Motif.Host.PanGloss;

/// <summary>Reads a configured parser's file name and its own version, without exposing its local path.</summary>
public static partial class ParserExecutableIdentity
{
    private static readonly ConcurrentDictionary<(string Path, long Stamp, long Length), string> Versions = new();

    public static string Read(string path)
    {
        try
        {
            var file = new FileInfo(path);
            var version = file.Exists
                ? Versions.GetOrAdd((file.FullName, file.LastWriteTimeUtc.Ticks, file.Length), key => VersionOf(key.Path))
                : "unavailable";
            return Path.GetFileName(path) + " " + version;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return Path.GetFileName(path) + " unavailable";
        }
    }

    private static string VersionOf(string path)
    {
        try
        {
            var start = new ProcessStartInfo(path)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
            };
            PanGlossProcessEnvironment.Configure(start);
            start.ArgumentList.Add("--version");
            using var process = Process.Start(start);
            if (process is null) return "unavailable";
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(3000))
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit();
                return "unavailable";
            }
            var match = VersionText().Match(output.GetAwaiter().GetResult() + " " + error.GetAwaiter().GetResult());
            return process.ExitCode == 0 && match.Success ? match.Value : "unavailable";
        }
        catch (Exception exception) when (exception is IOException or Win32Exception or InvalidOperationException
                                              or UnauthorizedAccessException)
        {
            return "unavailable";
        }
    }

    [GeneratedRegex(@"\b\d+\.\d+\.\d+(?:[-+][0-9A-Za-z.-]+)?\b", RegexOptions.CultureInvariant)]
    private static partial Regex VersionText();
}
