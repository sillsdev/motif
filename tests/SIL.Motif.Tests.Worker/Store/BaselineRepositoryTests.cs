using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker.Baselines;
using Xunit;

namespace SIL.Motif.Tests.Store;

public sealed class BaselineRepositoryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "SIL.Motif.BaselineRepositoryTests",
        Guid.NewGuid().ToString("N"));

    public BaselineRepositoryTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void Record_RoundTripsThroughTheOwnedMotifDatabase()
    {
        var project = Project("roundtrip");
        var path = Path.Combine(_root, "roundtrip.motif.db");
        var sourceLastWriteUtc = DateTimeOffset.Parse("2026-08-23T11:30:00Z");
        using (var database = MotifDatabase.OpenOwned(
                   path, project, MotifSchema.CurrentSchema, new Version(1, 0)))
        {
            var repository = new BaselineRepository(database);
            repository.Record("workspace", Publication("a", "sha256:" + new string('a', 64)),
                DateTimeOffset.Parse("2026-08-23T12:00:00Z"), sourceLastWriteUtc);
        }

        using var reopened = MotifDatabase.OpenOwned(
            path, project, MotifSchema.CurrentSchema, new Version(1, 0));
        var record = new BaselineRepository(reopened).GetCurrent("workspace");

        Assert.NotNull(record);
        Assert.Equal("workspace", record.ProjectKey);
        Assert.Equal("sha256:" + new string('a', 64), record.Token.BundleDigest);
        Assert.Equal(Path.Combine(_root, "a", "project.fwdata"), record.FwDataPath);
        Assert.Equal(DateTimeOffset.Parse("2026-08-23T12:00:00Z"), record.PublishedUtc);
        Assert.Equal(sourceLastWriteUtc, record.SourceLastWriteUtc);
        Assert.Equal(TimeSpan.Zero, record.SourceLastWriteUtc.Offset);
        // The source last-write and capture-digest times measure different events, so pin they diverge.
        Assert.NotEqual(DateTimeOffset.Parse(record.Token.CapturedUtc), record.SourceLastWriteUtc);
        Assert.NotEqual(record.PublishedUtc, record.SourceLastWriteUtc);
    }

    [Fact]
    public void Record_ReplacesCurrentRowButSameDigestIsIdempotent()
    {
        using var database = MotifDatabase.OpenOwned(Path.Combine(_root, "replace.motif.db"), Project("replace"),
            MotifSchema.CurrentSchema, new Version(1, 0));
        var repository = new BaselineRepository(database);
        var first = Publication("first", "sha256:" + new string('a', 64));
        var second = Publication("second", "sha256:" + new string('b', 64));
        repository.Record("workspace", first, DateTimeOffset.Parse("2026-08-23T12:00:00Z"),
            DateTimeOffset.Parse("2026-08-23T11:00:00Z"));
        repository.Record("workspace", first, DateTimeOffset.Parse("2026-08-23T13:00:00Z"),
            DateTimeOffset.Parse("2026-08-23T11:00:00Z"));

        Assert.Equal(DateTimeOffset.Parse("2026-08-23T12:00:00Z"),
            repository.GetCurrent("workspace")!.PublishedUtc);

        repository.Record("workspace", second, DateTimeOffset.Parse("2026-08-23T14:00:00Z"),
            DateTimeOffset.Parse("2026-08-23T13:30:00Z"));

        var replaced = repository.GetCurrent("workspace")!;
        Assert.Equal(second.Token, replaced.Token);
        Assert.Equal(DateTimeOffset.Parse("2026-08-23T13:30:00Z"), replaced.SourceLastWriteUtc);
    }

    [Fact]
    public void Record_RejectsANonUtcSourceLastWriteOffset()
    {
        using var database = MotifDatabase.OpenOwned(Path.Combine(_root, "offset.motif.db"), Project("offset"),
            MotifSchema.CurrentSchema, new Version(1, 0));
        var repository = new BaselineRepository(database);

        Assert.Throws<ArgumentException>(() => repository.Record("workspace",
            Publication("offset", "sha256:" + new string('a', 64)),
            DateTimeOffset.Parse("2026-08-23T12:00:00Z"),
            new DateTimeOffset(2026, 8, 23, 11, 0, 0, TimeSpan.FromHours(1))));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); }
        catch (DirectoryNotFoundException) { }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private ProjectLocator Project(string name) => new(Path.Combine(_root, name + ".fwdata"), name);

    private BaselinePublication Publication(string folder, string digest)
    {
        var root = Path.Combine(_root, folder);
        return new BaselinePublication(root, Path.Combine(root, "project.fwdata"),
            new BaselineToken("project-id", "sha256:" + new string('1', 64), "projection-v1",
                "2026-08-23T00:00:00Z", digest));
    }
}
