using Xunit;

namespace SIL.Motif.Tests.TestFixtures;

/// <summary>Runs only where clearing a directory's Unix write bit stops this process deleting its entries.</summary>
public sealed class ReadOnlyDirectoryFactAttribute : FactAttribute
{
    public ReadOnlyDirectoryFactAttribute() => Skip = ReadOnlyDirectory.SkipReason;
}

/// <summary>A theory that runs only where clearing a directory's Unix write bit stops this process deleting.</summary>
public sealed class ReadOnlyDirectoryTheoryAttribute : TheoryAttribute
{
    public ReadOnlyDirectoryTheoryAttribute() => Skip = ReadOnlyDirectory.SkipReason;
}

internal static class ReadOnlyDirectory
{
    // Root bypasses directory permission checks, so the directory never becomes read-only to this process.
    internal static string? SkipReason =>
        OperatingSystem.IsWindows()
            ? "A read-only directory is modelled with Unix file modes, which Windows does not have."
            : Environment.IsPrivilegedProcess
                ? "Running as root (effective user id 0) ignores directory write permissions, so a read-only " +
                  "directory cannot be simulated."
                : null;
}
