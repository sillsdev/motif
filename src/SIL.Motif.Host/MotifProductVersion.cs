using System;
using System.Reflection;
using System.Text.RegularExpressions;
using SIL.Motif.Contract;

namespace SIL.Motif.Host;

/// <summary>The running build's product version and numeric store compatibility floor.</summary>
public static class MotifProductVersion
{
    /// <summary>The numeric assembly version used to check the store's minimum worker version.</summary>
    public static Version Current { get; } =
        typeof(MotifProductVersion).Assembly.GetName().Version is { } version
            ? new Version(version.Major, version.Minor, version.Build)
            : new Version(0, 0, 0);

    /// <summary>The semantic version reported to users, including its prerelease suffix.</summary>
    public static string CurrentText { get; } = ReadProductVersion();

    /// <summary>The source revision embedded in this build, when the SDK supplied a full Git SHA.</summary>
    public static string? BuildSourceSha { get; } = ReadBuildSourceSha();

    /// <summary>The version and source revision shown by the command line.</summary>
    public static string DisplayText { get; } = FormatDisplayText(CurrentText, BuildSourceSha);

    /// <summary>Reads the numeric floor from a product version, defaulting malformed requests to this build.</summary>
    /// <param name="productVersion">The caller's numeric or semantic version.</param>
    /// <returns>The numeric version used by the project store's schema gate.</returns>
    public static Version CompatibilityVersion(string productVersion) =>
        Version.TryParse(productVersion, out var parsed) ? parsed :
        ProductVersionSyntax.TryParseCore(productVersion, out var core) ? core : Current;

    internal static string FormatDisplayText(string productVersion, string? sourceSha) =>
        sourceSha is not null && Regex.IsMatch(sourceSha, @"\A(?:[0-9a-fA-F]{40}|[0-9a-fA-F]{64})\z")
            ? $"{productVersion} (source SHA {sourceSha})"
            : productVersion;

    private static string ReadProductVersion()
    {
        var informational = typeof(MotifProductVersion).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var product = informational?.Split('+')[0];
        return ProductVersionSyntax.TryParseCore(product, out _) ? product! : Current.ToString(3);
    }

    private static string? ReadBuildSourceSha()
    {
        var informational = typeof(MotifProductVersion).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var separator = informational?.LastIndexOf('+') ?? -1;
        if (separator < 0) return null;
        var sourceSha = informational![(separator + 1)..];
        return Regex.IsMatch(sourceSha, @"\A(?:[0-9a-fA-F]{40}|[0-9a-fA-F]{64})\z")
            ? sourceSha
            : null;
    }
}
