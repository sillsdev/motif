using SIL.Motif.Scripture;
using Xunit;

namespace SIL.Motif.Tests.Scripture;

public sealed class ScriptureLocalStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "SIL.Motif.Tests.ScriptureLocal", Guid.NewGuid().ToString("N"));
    private ScriptureLocalStore Store() => new(Path.Combine(_root, "Motif", "scripture-local.json"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void NothingRecordedReadsAsEmptyAndCreatesNoFile()
    {
        var store = Store();

        var state = store.Read("P1");

        Assert.Empty(state.Current);
        Assert.Empty(state.SourcePaths);
        Assert.False(File.Exists(store.FilePath));
    }

    [Fact]
    public void CurrentAndPathRoundTripThroughANewStoreOnTheSameFile()
    {
        Store().SetCurrent("P1", "WEB", new ChapterRef("mat", 5));
        Store().SetSourcePath("P1", "WEB", "/data/WEB");

        var state = Store().Read("P1");

        Assert.Equal(new ChapterRef("MAT", 5), state.Current["WEB"]);
        Assert.Equal("/data/WEB", state.SourcePaths["WEB"]);
    }

    [Fact]
    public void CurrentIsOneChapterPerSource()
    {
        var store = Store();
        store.SetCurrent("P1", "WEB", new ChapterRef("MAT", 5));
        store.SetCurrent("P1", "WEB", new ChapterRef("MAT", 6));
        store.SetCurrent("P1", "CEB", new ChapterRef("JHN", 1));

        var state = store.Read("P1");

        Assert.Equal(new ChapterRef("MAT", 6), state.Current["WEB"]);
        Assert.Equal(new ChapterRef("JHN", 1), state.Current["CEB"]);
    }

    [Fact]
    public void ClearingForgetsTheEntryAndAnEmptyProjectLeavesNoRecord()
    {
        var store = Store();
        store.SetCurrent("P1", "WEB", new ChapterRef("MAT", 5));
        store.SetSourcePath("P1", "WEB", "/data/WEB");
        store.ClearCurrent("P1", "WEB");
        store.ClearSourcePath("P1", "WEB");

        Assert.Empty(store.Read("P1").Current);
        Assert.DoesNotContain("P1", File.ReadAllText(store.FilePath));
    }

    [Fact]
    public void TwoProjectsDoNotCollide()
    {
        var store = Store();
        store.SetCurrent("P1", "WEB", new ChapterRef("MAT", 5));
        store.SetCurrent("P2", "WEB", new ChapterRef("LUK", 2));

        Assert.Equal(new ChapterRef("MAT", 5), store.Read("P1").Current["WEB"]);
        Assert.Equal(new ChapterRef("LUK", 2), store.Read("P2").Current["WEB"]);
    }

    [Fact]
    public void ChapterTheSourceDoesNotHoldIsRefusedAndNothingIsWritten()
    {
        var source = new ParatextSource("T", "", SIL.Scripture.ScrVers.English,
            new Dictionary<string, IReadOnlyList<int>> { ["MAT"] = [1, 2] });
        var store = Store();

        store.SetCurrent("P1", "WEB", new ChapterRef("MAT", 2), source);
        Assert.Throws<ArgumentException>(() => store.SetCurrent("P1", "WEB", new ChapterRef("MAT", 3), source));
        Assert.Throws<ArgumentException>(() => store.SetCurrent("P1", "WEB", new ChapterRef("LUK", 1), source));

        Assert.Equal(new ChapterRef("MAT", 2), store.Read("P1").Current["WEB"]);
    }

    [Fact]
    public void PluginPathOverridesTheStoredOne()
    {
        var store = Store();
        Assert.Null(store.ResolveSourcePath("P1", "WEB"));
        store.SetSourcePath("P1", "WEB", "/stored");

        Assert.Equal("/stored", store.ResolveSourcePath("P1", "WEB"));
        Assert.Equal("/plugin", store.ResolveSourcePath("P1", "WEB", "/plugin"));
        Assert.Equal("/stored", store.ResolveSourcePath("P1", "WEB", "  "));
    }

    [Fact]
    public void WriteLeavesNoTemporaryFileAndAnUnreadableFileReadsAsEmpty()
    {
        var store = Store();
        store.SetCurrent("P1", "WEB", new ChapterRef("MAT", 5));
        Assert.Equal([store.FilePath], Directory.GetFiles(Path.GetDirectoryName(store.FilePath)!));

        File.WriteAllText(store.FilePath, "{ not json");
        Assert.Empty(store.Read("P1").Current);
        store.SetCurrent("P1", "WEB", new ChapterRef("MAT", 7));
        Assert.Equal(new ChapterRef("MAT", 7), store.Read("P1").Current["WEB"]);
    }

    [Fact]
    public void InstallationFileIsInTheUserDataFolderNotAProject()
    {
        Assert.EndsWith(Path.Combine("SIL", "Motif", "scripture-local.json"), ScriptureLocalStore.DefaultPath);
    }

    [Fact]
    public void TestProcessUsesItsOwnFileNotTheDevelopersReal()
    {
        Assert.NotEqual(ScriptureLocalStore.DefaultPath, ScriptureLocalStore.ForInstallation().FilePath);
    }

    [Fact]
    public void FileWithNullRecordsReadsAsEmptyAndCanBeWritten()
    {
        var store = Store();
        Directory.CreateDirectory(Path.GetDirectoryName(store.FilePath)!);
        File.WriteAllText(store.FilePath, """{"projects":{"P1":null,"P2":{"current":null,"sourcePaths":null}}}""");

        Assert.Empty(store.Read("P1").Current);
        Assert.Empty(store.Read("P2").SourcePaths);
        store.SetCurrent("P1", "WEB", new ChapterRef("MAT", 1));
        store.SetSourcePath("P2", "WEB", "/x");
        Assert.Equal(new ChapterRef("MAT", 1), store.Read("P1").Current["WEB"]);
        Assert.Equal("/x", store.Read("P2").SourcePaths["WEB"]);
    }

    [Fact]
    public void CorruptFileIsKeptAsBadBeforeTheNextWrite()
    {
        var store = Store();
        store.SetCurrent("P1", "WEB", new ChapterRef("MAT", 5));
        File.WriteAllText(store.FilePath + ".bad", "older");
        File.WriteAllText(store.FilePath, "{ damaged");

        store.SetCurrent("P2", "WEB", new ChapterRef("LUK", 1));

        Assert.Equal("{ damaged", File.ReadAllText(store.FilePath + ".bad"));
        Assert.Equal(new ChapterRef("LUK", 1), store.Read("P2").Current["WEB"]);
    }

    [Fact]
    public void SourceIdsMatchIgnoringCaseAcrossWritesAndReads()
    {
        var store = Store();
        store.SetCurrent("P1", "web", new ChapterRef("MAT", 5));
        store.SetSourcePath("P1", "web", "/a");
        store.SetCurrent("P1", "WEB", new ChapterRef("MAT", 6));

        var state = Store().Read("P1");

        Assert.Single(state.Current);
        Assert.Equal(new ChapterRef("MAT", 6), state.Current["WEB"]);
        Assert.Equal("/a", state.SourcePaths["Web"]);
        Assert.Equal("/a", store.ResolveSourcePath("P1", "WEB"));
        store.ClearCurrent("P1", "wEb");
        Assert.Empty(store.Read("P1").Current);
    }
}
