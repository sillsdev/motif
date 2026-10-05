using System.Diagnostics;
using System.Reflection;
using System.Xml.Linq;
using SIL.Motif.Host;
using SIL.Motif.Tests.TestFixtures;
using Velopack;
using Xunit;

namespace SIL.Motif.Tests.Cli;

public sealed class ProductVersionTests
{
    [Fact]
    public void RuntimeReportsTheDeclaredSemanticVersionIncludingTheSuffix()
    {
        var source = XDocument.Load(Path.Combine(FindRepoRoot(), "Directory.Build.props"))
            .Descendants("VersionPrefix").Single().Value;
        var informational = typeof(MotifProductVersion).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
        Assert.Equal(source, informational.Split('+')[0]);
        Assert.Equal(source, MotifProductVersion.CurrentText);
        Assert.Equal(MotifProductVersion.Current, MotifProductVersion.CompatibilityVersion(source));
    }

    [Fact]
    public void CliVersionMatchesTheRuntimeSemanticVersion()
    {
        var start = new ProcessStartInfo(BuildOutput.Cli);
        start.ArgumentList.Add("--version");
        var result = ToolProcess.Run(start);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(MotifProductVersion.CurrentText, result.Output.Trim());
        Assert.Equal(string.Empty, result.Error);
    }

    [Theory]
    [InlineData("99.2.3-beta001")]
    [InlineData("99.2.3-rc.1+build.001")]
    public void PrereleaseCompatibilityUsesItsOwnNumericCore(string version) =>
        Assert.Equal(new Version(99, 2, 3), MotifProductVersion.CompatibilityVersion(version));

    [Fact]
    public void VelopackOrdersBetaUpdatesBeforeTheStableRelease()
    {
        var first = SemanticVersion.Parse("0.2.0-beta001");
        var next = SemanticVersion.Parse("0.2.0-beta002");
        var stable = SemanticVersion.Parse("0.2.0");
        Assert.Equal("0.2.0-beta001", first.ToString());
        Assert.Equal("0.2.0-beta002", next.ToString());
        Assert.True(first.IsPrerelease);
        Assert.True(next > first);
        Assert.True(stable > next);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Motif.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Could not find Motif.sln.");
    }

}
