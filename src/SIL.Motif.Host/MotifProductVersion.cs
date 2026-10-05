using System;
using System.Reflection;
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

    /// <summary>Reads the numeric floor from a product version, defaulting malformed requests to this build.</summary>
    /// <param name="productVersion">The caller's numeric or semantic version.</param>
    /// <returns>The numeric version used by the project store's schema gate.</returns>
    public static Version CompatibilityVersion(string productVersion) =>
        Version.TryParse(productVersion, out var parsed) ? parsed :
        ProductVersionSyntax.TryParseCore(productVersion, out var core) ? core : Current;

    private static string ReadProductVersion()
    {
        var informational = typeof(MotifProductVersion).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var product = informational?.Split('+')[0];
        return ProductVersionSyntax.TryParseCore(product, out _) ? product! : Current.ToString(3);
    }
}
