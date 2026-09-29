using Xunit;

namespace SIL.Motif.Tests.TestFixtures;

/// <summary>Runs only where Windows file sharing prevents operations against files held open by another stream.</summary>
public sealed class WindowsFileLockFactAttribute : FactAttribute
{
    public WindowsFileLockFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
            Skip = "The assertions depend on Windows enforcing FileShare.None against another open or deletion.";
    }
}
