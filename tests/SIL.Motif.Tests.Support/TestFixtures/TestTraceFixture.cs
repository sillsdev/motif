namespace SIL.Motif.Tests.TestFixtures;

/// <summary>Adapts v3 captures for tree-content tests; the captures do not supply v4-only counters.</summary>
public static class TestTraceFixture
{
    /// <summary>Replaces the v3 profile identifier with v4 without adding v4-only fields.</summary>
    public static string Read(string path) => File.ReadAllText(path).Replace(
        "pangloss.trace-details.v3", "pangloss.trace-details.v4", StringComparison.Ordinal);
}
