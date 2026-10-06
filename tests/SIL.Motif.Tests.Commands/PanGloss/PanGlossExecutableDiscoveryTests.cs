using SIL.Motif.Host.Parser;
using Xunit;

namespace SIL.Motif.Tests.Commands.PanGloss;

public sealed class PanGlossExecutableDiscoveryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-pangloss-discovery-" + Guid.NewGuid().ToString("N"));
    private readonly string _repository;
    private readonly string _application;
    private const string FileName = "pangloss-test";

    public PanGlossExecutableDiscoveryTests()
    {
        _repository = Path.Combine(_root, "motif");
        _application = Path.Combine(_repository, "bin", "Debug");
        Directory.CreateDirectory(_application);
        File.WriteAllText(Path.Combine(_repository, "pangloss-release.json"), """{ "version": "0.6.2" }""");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private string Touch(params string[] parts)
    {
        var path = Path.Combine([_root, .. parts]);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "");
        return Path.GetFullPath(path);
    }

    [Fact]
    public void AStaleSiblingCopyIsSkippedForTheParserBesideMotif()
    {
        Touch("PanGloss", "dist", "v0.3.3", FileName);
        var beside = Touch("motif", "bin", "Debug", FileName);

        Assert.Equal(beside, PanGlossExecutable.TryLocate(null, _application, FileName, _repository));
    }

    [Fact]
    public void ASiblingCopyOfThePinnedVersionWins()
    {
        Touch("PanGloss", "dist", "v0.3.3", FileName);
        var pinned = Touch("PanGloss", "dist", "v0.6.2", FileName);
        Touch("motif", "bin", "Debug", FileName);

        Assert.Equal(pinned, PanGlossExecutable.TryLocate(null, _application, FileName, _repository));
    }

    [Fact]
    public void ALiveSiblingCargoBuildStillWins()
    {
        var live = Touch("PanGloss", "rust", "target", "release", FileName);
        Touch("motif", "bin", "Debug", FileName);

        Assert.Equal(live, PanGlossExecutable.TryLocate(null, _application, FileName, _repository));
    }

    [Fact]
    public void TheConfiguredPathOverridesEverything()
    {
        Touch("PanGloss", "rust", "target", "release", FileName);
        var configured = Touch("elsewhere", FileName);

        Assert.Equal(configured, PanGlossExecutable.TryLocate(configured, _application, FileName, _repository));
    }
}
