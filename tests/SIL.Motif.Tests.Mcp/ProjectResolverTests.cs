using SIL.Motif.Host.Store;
using SIL.Motif.Mcp;
using Xunit;

namespace SIL.Motif.Tests.Mcp;

public sealed class ProjectResolverTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-project-selectors-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void ExistingFilesDoNotAuthorizeAnUnknownProject()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "unknown.fwdata");
        File.WriteAllText(path, "");
        Assert.Throws<ProfileException>(() => ProjectResolver.Resolve(path, _root));
        using var machine = MachineDatabase.Open(_root);
        Assert.Empty(new KnownProjectRegistry(machine).List());
    }

    [Fact]
    public void AnInvalidPathIsASelectorRefusal()
    {
        Register("known");
        Assert.Throws<ProfileException>(() => ProjectResolver.Resolve("invalid\0path", _root));
    }

    [Fact]
    public void AmbiguousNamesRequireARecordedPathAndPathsSelectEachProjectIndependently()
    {
        var paths = new[] { Register("first"), Register("second") };
        Assert.Throws<ProfileException>(() => ProjectResolver.Resolve("shared", _root));
        Assert.All(paths, path => Assert.Equal(path, ProjectResolver.Resolve(path, _root)));
        Assert.Equal(paths[0], ProjectResolver.Resolve("first", _root));
        Assert.Equal(paths[1], ProjectResolver.Resolve("second", _root));
    }

    private string Register(string folder)
    {
        var directory = Path.Combine(_root, folder);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "shared.fwdata");
        File.WriteAllText(path, "");
        using var machine = MachineDatabase.Open(_root);
        new KnownProjectRegistry(machine).Record(folder, path, DateTimeOffset.UtcNow);
        return path;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
