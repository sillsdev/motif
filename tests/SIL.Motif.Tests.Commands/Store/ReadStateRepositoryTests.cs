using SIL.Motif.Commands.Store;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Host.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands.Store;

public sealed class ReadStateRepositoryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-read-state-" + Guid.NewGuid().ToString("N"));

    public ReadStateRepositoryTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void UpsertListsAndDeletesOneTextOccurrence()
    {
        var project = new ProjectLocator(Path.Combine(_root, "project.fwdata"), "project");
        using var database = MotifDatabase.OpenOwned(Path.Combine(_root, "project.motif.db"), project,
            MotifSchema.CurrentSchema, new Version(1, 0));
        var repository = new ReadStateRepository(database);
        var textId = Guid.NewGuid();
        var first = new OccurrenceAnchor(textId, Guid.NewGuid(), Guid.NewGuid(), 0);
        var second = first with { Index = 1 };
        var original = new ReadOccurrenceRecord(first, "{\"evidence\":\"old\"}");

        repository.Upsert(original);
        repository.Upsert(new ReadOccurrenceRecord(second, "{\"evidence\":\"other\"}"));
        repository.Upsert(original with { FingerprintJson = "{\"evidence\":\"current\"}" });

        Assert.Equal("{\"evidence\":\"current\"}", repository.Get(first)!.FingerprintJson);
        Assert.Equal(2, repository.GetForText(textId).Count);

        repository.Delete(first);

        Assert.Null(repository.Get(first));
        Assert.Equal(second, Assert.Single(repository.GetForText(textId)).Occurrence);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); }
        catch (DirectoryNotFoundException) { }
        catch (IOException) { }
    }
}
