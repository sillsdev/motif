using SIL.Motif.Host.Store;

namespace SIL.Motif.Commands;

/// <summary>Arguments for an explicit project store upgrade.</summary>
public sealed record ProjectStoreUpgradeRequest(string? Project, string? Source, string? Out);

/// <summary>The report produced by an explicit project store upgrade.</summary>
public sealed record ProjectStoreUpgradeResponse(MotifStoreUpgrade.Report Report);

/// <summary>Runs an explicit project store upgrade from an immutable source.</summary>
public static class ProjectStoreUpgrade
{
    /// <summary>Copies an old project store to a new current-schema store.</summary>
    public static MotifStoreUpgrade.Report Copy(string source, string output) => MotifStoreUpgrade.Copy(source, output);
}
