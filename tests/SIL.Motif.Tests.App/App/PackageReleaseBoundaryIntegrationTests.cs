using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App;

[Trait("MotifTestLevel", "Integration")]
public sealed class PackageReleaseBoundaryIntegrationTests
{
    [WindowsHostFact]
    public void PackageReleaseRejectsUnixTargetsOnAWindowsHostBeforeResolvingTheParser()
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(BuildOutput.ProductDirectory, "..", ".."));
        var start = new System.Diagnostics.ProcessStartInfo("pwsh");
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-File");
        start.ArgumentList.Add(Path.Combine(repositoryRoot, "tools", "package-release.ps1"));
        start.ArgumentList.Add("-ProductVersion");
        start.ArgumentList.Add("0.1.0");
        start.ArgumentList.Add("-ParserArtifact");
        start.ArgumentList.Add(Path.Combine(repositoryRoot, ".tmp", "missing-pangloss"));
        start.ArgumentList.Add("-RuntimeIdentifier");
        start.ArgumentList.Add("linux-x64");

        var result = ToolProcess.Run(start);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Unix package targets must be built on Linux or macOS", result.Error, StringComparison.Ordinal);
    }
}

/// <summary>A fact about how release packaging behaves when the host running it is Windows.</summary>
internal sealed class WindowsHostFactAttribute : FactAttribute
{
    public WindowsHostFactAttribute()
    {
        if (!OperatingSystem.IsWindows()) Skip = "This checks packaging on a Windows host.";
    }
}
