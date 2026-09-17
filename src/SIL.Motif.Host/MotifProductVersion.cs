using System;

namespace SIL.Motif.Host;

/// <summary>
/// The running build's version, as every front end reports it to a project store.
/// </summary>
/// <remarks>
/// Read from the assembly rather than written down, so this follows the single version the build declares
/// and cannot drift from the artifact a user installed. The store compares it against the minimum worker
/// version its schema requires, so a build whose assembly carries no version at all reports 0.0 and is
/// refused rather than admitted as though it were current: a missing version is a broken build, and the
/// refusal says so at the point of use.
/// </remarks>
public static class MotifProductVersion
{
    /// <summary>The running build's version, three-part; 0.0 when the assembly declares none.</summary>
    public static Version Current { get; } =
        typeof(MotifProductVersion).Assembly.GetName().Version is { } version
            ? new Version(version.Major, version.Minor, version.Build)
            : new Version(0, 0);

    /// <summary>The same version in the three-part text form a command request carries.</summary>
    public static string CurrentText { get; } = Current.ToString(3);
}
