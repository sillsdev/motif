using SIL.Motif.Contract.Projects;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker.Jobs;
using Xunit;

namespace SIL.Motif.Tests.Store;

public sealed class JobWordListsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-job-words-" + Guid.NewGuid().ToString("N"));

    public JobWordListsTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void AStoredListReadsBackAndTheSameListIsStoredOnce()
    {
        using var database = OpenDatabase();
        string[] words = ["kitabu", "vitabu", "ng'ombe", "mtotó"];

        var first = JobWordLists.Store(database, words);
        var second = JobWordLists.Store(database, words);

        Assert.Equal(first, second);
        Assert.Equal(4, first.Count);
        Assert.Equal(words, JobWordLists.Read(database, first));
        Assert.Single(Directory.GetFiles(database.FullPath + ".job-inputs"));
    }

    [Fact]
    public void AnAlteredOrMissingListIsRefused()
    {
        using var database = OpenDatabase();
        var list = JobWordLists.Store(database, ["kitabu"]);
        var path = Assert.Single(Directory.GetFiles(database.FullPath + ".job-inputs"));

        File.WriteAllText(path, """["vitabu"]""");
        Assert.Contains("altered", Assert.Throws<InvalidOperationException>(() => JobWordLists.Read(database, list)).Message);
        File.Delete(path);
        Assert.Contains("no longer stored",
            Assert.Throws<InvalidOperationException>(() => JobWordLists.Read(database, list)).Message);
    }

    [Fact]
    public void OnlyOldListsNoRemainingJobNamesAreRemoved()
    {
        using var database = OpenDatabase();
        var kept = JobWordLists.Store(database, ["kept"]);
        var orphan = JobWordLists.Store(database, ["orphan"]);
        var recent = JobWordLists.Store(database, ["recent"]);
        var directory = database.FullPath + ".job-inputs";
        var old = DateTime.UtcNow - JobWordLists.UnreferencedGrace - TimeSpan.FromMinutes(1);
        foreach (var list in new[] { kept, orphan })
            File.SetLastWriteTimeUtc(Path.Combine(directory, list.Sha256 + ".words.json"), old);

        var removed = JobWordLists.RemoveUnreferenced(database,
            [System.Text.Json.JsonSerializer.Serialize(new { wordList = kept })], DateTime.UtcNow);

        Assert.Equal(1, removed);
        Assert.Equal(["kept"], JobWordLists.Read(database, kept));
        Assert.Equal(["recent"], JobWordLists.Read(database, recent));
        Assert.Throws<InvalidOperationException>(() => JobWordLists.Read(database, orphan));
    }

    private MotifDatabase OpenDatabase()
    {
        var project = new ProjectLocator(Path.Combine(_root, "words.fwdata"), "words");
        return MotifDatabase.OpenOwned(Path.Combine(_root, "words.motif.db"), project,
            MotifSchema.CurrentSchema, new Version(1, 0));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
