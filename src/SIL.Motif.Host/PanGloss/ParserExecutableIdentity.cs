using System.Collections.Concurrent;
using System.ComponentModel;
using System.Text.RegularExpressions;
using SIL.Motif.Host.Assess;

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
            var version = file.Exists ? ReadVersion(path) : "unavailable";
            return Path.GetFileName(path) + " " + version;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return Path.GetFileName(path) + " unavailable";
        }
    }

    /// <summary>The version the executable at <paramref name="path"/> reports for <c>--version</c>, or "unavailable".</summary>
    /// <remarks>Cached by path, modified time and length, so a rebuilt executable at the same path is read again.</remarks>
    /// <exception cref="AssessorUnavailableException">The probe was killed or exhausted memory.</exception>
    public static string ReadVersion(string path) => ReadReportedVersion(path, fresh: false);

    /// <summary>Reads the executable's reported version without using the cached observation.</summary>
    /// <exception cref="AssessorUnavailableException">The probe was killed or exhausted memory.</exception>
    public static string ReadFreshVersion(string path) => ReadReportedVersion(path, fresh: true);

    /// <summary>The version already read for the executable at <paramref name="path"/>, or null; it runs nothing.</summary>
    public static string? CachedVersion(string path)
    {
        try
        {
            var file = new FileInfo(path);
            return file.Exists && Versions.TryGetValue((file.FullName, file.LastWriteTimeUtc.Ticks, file.Length), out var known)
                ? known : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    internal static async Task<ParserVersionCheck> ReadVersionAsync(string path,
        PanGlossContainmentJob containment, CancellationToken cancellationToken, bool fresh = false)
    {
        try
        {
            var file = new FileInfo(path);
            if (!file.Exists) return new("unavailable");
            var key = (file.FullName, file.LastWriteTimeUtc.Ticks, file.Length);
            if (!fresh && Versions.TryGetValue(key, out var known)) return new(known);
            var outcome = await PanGlossProbe.RunAsync(file.FullName, "--version", containment,
                TimeSpan.FromSeconds(3), cancellationToken).ConfigureAwait(false);
            if (PanGlossProbe.FatalFailure(outcome) is { } failure) return new("unavailable", failure);
            if (outcome is not PanGlossOutcome.Completed completed) return new("unavailable");
            var match = VersionText().Match(completed.Output + " " + completed.StandardError);
            if (!match.Success) return new("unavailable");
            if (!fresh) Versions[key] = match.Value;
            return new(match.Value);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return new("unavailable");
        }
    }

    private static string ReadReportedVersion(string path, bool fresh)
    {
        try
        {
            if (!File.Exists(path)) return "unavailable";
            if (!fresh && CachedVersion(path) is { } cached) return cached;
            using var containment = PanGlossContainment.CreateJob();
            var check = ReadVersionAsync(path, containment, CancellationToken.None, fresh).GetAwaiter().GetResult();
            return check.Failure switch
            {
                PanGlossOutcome.Refused refused => throw new AssessorUnavailableException(PanGlossAssessor.AssessorName,
                    refused.Message) { ParserRefusal = refused },
                PanGlossOutcome.MemoryLimit memory => throw new AssessorUnavailableException(PanGlossAssessor.AssessorName,
                    memory.Message) { MemoryKill = memory.Kill },
                _ => check.Version,
            };
        }
        catch (Exception exception) when (exception is IOException or Win32Exception or InvalidOperationException or
            UnauthorizedAccessException or ArgumentException or PanGlossMemoryLimitSettingException)
        {
            return "unavailable";
        }
    }

    [GeneratedRegex(@"\b\d+\.\d+\.\d+(?:[-+][0-9A-Za-z.-]+)?\b", RegexOptions.CultureInvariant)]
    private static partial Regex VersionText();
}

internal sealed record ParserVersionCheck(string Version, PanGlossOutcome? Failure = null);
