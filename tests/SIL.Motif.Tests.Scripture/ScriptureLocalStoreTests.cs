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
    public void TheInstallationIdIsMadeOnceBesideTheFileAndAnUnreadableOneIsReplaced()
    {
        var first = Store().InstallationId();
        var path = Path.Combine(_root, "Motif", "installation-id");

        Assert.NotEqual(Guid.Empty, first);
        Assert.Equal(first, Store().InstallationId());
        Assert.Equal(first, Guid.Parse(File.ReadAllLines(path)[0]));
        Assert.NotEqual(first, new ScriptureLocalStore(Path.Combine(_root, "Other", "scripture-local.json")).InstallationId());
        Assert.Equal([first], Enumerable.Range(0, 8).AsParallel().Select(_ => Store().InstallationId()).Distinct());

        File.WriteAllText(path, "not an id");
        var replaced = Store().InstallationId();
        Assert.NotEqual(first, replaced);
        Assert.Equal(replaced, Store().InstallationId());
    }

    [Theory]
    [InlineData("")]
    [InlineData("machine\t00000000000000000000000000000000\n")]
    public void AnInstallationIdFileCopiedFromAnotherComputerIsSetAsideAndANewIdIsMade(string fingerprint)
    {
        var path = Path.Combine(_root, "Motif", "installation-id");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var copied = Guid.NewGuid();
        File.WriteAllText(path, copied.ToString("D") + "\n" + fingerprint);

        var made = Store().InstallationId();

        Assert.NotEqual(copied, made);
        Assert.Equal(made, Store().InstallationId());
        var aside = Assert.Single(Directory.GetFiles(Path.GetDirectoryName(path)!, "installation-id.*.old"));
        Assert.Equal(copied, Guid.Parse(File.ReadAllLines(aside)[0]));
    }

    [Fact]
    public void AnIntactInstallationIdFileCopiedToAComputerWithTheSameNamesButAnotherMachineIdIsSetAside()
    {
        var path = Path.Combine(_root, "Motif", "installation-id");
        var ours = new InstallationMachine("SAME-HOST", "same-user", "machine-ours", "test");
        Guid original;
        using (InstallationMachine.Use(ours)) original = Store().InstallationId();
        var copied = File.ReadAllText(path);

        using (InstallationMachine.Use(ours with { SystemId = "machine-theirs" }))
        {
            var theirs = Store().InstallationId();

            Assert.NotEqual(original, theirs);
            Assert.Equal(theirs, Store().InstallationId());
        }
        var aside = Assert.Single(Directory.GetFiles(Path.GetDirectoryName(path)!, "installation-id.*.old"));
        Assert.Equal(copied, File.ReadAllText(aside));
        Assert.Equal("machine-id\ttest", File.ReadAllLines(path)[2]);
    }

    [Fact]
    public void WithoutAReadableMachineIdTheFileSaysItsFingerprintIsWeakAndSameNamedComputersShareIt()
    {
        var path = Path.Combine(_root, "Motif", "installation-id");
        var weak = new InstallationMachine("SAME-HOST", "same-user", null, "none readable: host and user names only");
        Assert.True(weak.IsWeak);
        Assert.False((weak with { SystemId = "x" }).IsWeak);

        Guid first;
        using (InstallationMachine.Use(weak)) first = Store().InstallationId();
        using (InstallationMachine.Use(weak with { SystemIdSource = "another computer" })) Assert.Equal(first, Store().InstallationId());

        Assert.Equal("weak\tnone readable: host and user names only", File.ReadAllLines(path)[2]);
        Assert.NotEqual(weak.Fingerprint, (weak with { SystemId = "x" }).Fingerprint);
    }

    [Fact]
    public void ThisComputersMachineIdIsReadWhereTheSystemHasOne()
    {
        var machine = InstallationMachine.Read();

        Assert.Equal(Environment.MachineName, machine.HostName);
        Assert.Equal(Environment.UserName, machine.UserName);
        Assert.False(string.IsNullOrWhiteSpace(machine.SystemIdSource));
        // Windows always has MachineGuid and macOS IOPlatformUUID; a container may have an empty /etc/machine-id.
        if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ||
            (File.Exists("/etc/machine-id") && File.ReadAllText("/etc/machine-id").Trim().Length > 0))
            Assert.False(machine.IsWeak, machine.SystemIdSource);
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
