using SIL.Motif.Commands.Queries;
using SIL.Motif.Host.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary>
/// Pins <see cref="KnownProjectsQuery"/>: Known projects come back most-recently-seen first, and a
/// project whose <c>.fwdata</c> file has gone missing is both omitted from the list and forgotten from
/// the machine store, rather than merely skipped.
/// </summary>
public sealed class KnownProjectsQueryTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "SIL.Motif.KnownProjectsQueryTests", Guid.NewGuid().ToString("N"));

    public KnownProjectsQueryTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public void ListsKnownProjectsMostRecentlySeenFirst()
    {
        var older = NewProjectFile("older.fwdata");
        var newer = NewProjectFile("newer.fwdata");
        using (var machine = MachineDatabase.Open(_root))
        {
            var registry = new KnownProjectRegistry(machine);
            registry.Record("older", older, DateTimeOffset.UtcNow.AddDays(-1));
            registry.Record("newer", newer, DateTimeOffset.UtcNow);
        }

        var projects = KnownProjectsQuery.List(_root);

        Assert.Equal([newer, older], projects.Select(p => p.FullFwDataPath));
    }

    [Fact]
    public void ListsAProjectOnceAtItsLatestSightingWhateverWorkspaceKeysItWasRecordedUnder()
    {
        var project = NewProjectFile("repeated.fwdata");
        var other = NewProjectFile("other.fwdata");
        using (var machine = MachineDatabase.Open(_root))
        {
            var registry = new KnownProjectRegistry(machine);
            registry.Record("first", project, DateTimeOffset.UtcNow.AddDays(-3));
            registry.Record("other", other, DateTimeOffset.UtcNow.AddDays(-2));
            registry.Record("second", project, DateTimeOffset.UtcNow.AddDays(-1));
            registry.Record("third", project, DateTimeOffset.UtcNow.AddDays(-4));
        }

        var projects = KnownProjectsQuery.List(_root);

        Assert.Equal([project, other], projects.Select(p => p.FullFwDataPath));
    }

    [Fact]
    public void OmitsAndForgetsAProjectWhoseFileHasGoneMissing()
    {
        var missing = Path.Combine(_root, "deleted.fwdata");
        var present = NewProjectFile("present.fwdata");
        using (var machine = MachineDatabase.Open(_root))
        {
            var registry = new KnownProjectRegistry(machine);
            registry.Record("missing", missing, DateTimeOffset.UtcNow.AddDays(-1));
            registry.Record("present", present, DateTimeOffset.UtcNow);
        }

        var projects = KnownProjectsQuery.List(_root);

        Assert.Equal(present, Assert.Single(projects).FullFwDataPath);

        using var reopened = MachineDatabase.Open(_root);
        Assert.Equal(present, Assert.Single(new KnownProjectRegistry(reopened).List()).FullFwDataPath);
    }

    private string NewProjectFile(string name)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, string.Empty);
        return path;
    }
}
