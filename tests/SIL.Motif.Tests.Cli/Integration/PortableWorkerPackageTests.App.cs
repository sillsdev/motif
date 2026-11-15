using Xunit;

namespace SIL.Motif.Tests.Integration;

public sealed partial class PortableWorkerPackageTests
{
    private static void AssertAppHost(string package, string suffix)
    {
        var appHost = Path.Combine(package, "SIL.Motif.App" + suffix);
        Assert.True(File.Exists(appHost), "The portable package is missing the App apphost.");
        AssertSelfContained(package, "SIL.Motif.App.runtimeconfig.json");
    }
}
