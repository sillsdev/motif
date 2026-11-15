using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host;
using SIL.Motif.Host.Sources;
using SIL.Motif.Host.Store;
using SIL.Motif.Scripture;
using SIL.Motif.Tests.Scripture;
using SIL.Motif.Tests.Scripture.Fixtures;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Sources;
using Xunit;

namespace SIL.Motif.Tests.Worker;

public sealed class SourceIndexRefreshTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-source-refresh-" + Guid.NewGuid().ToString("N"));
    private readonly TinyProject _source = TinyProject.Create();
    private readonly MotifDatabase _database;
    private readonly IDisposable _environment;
    private readonly SourceListEntry _entry = new("TINY", "Tiny", SourceKind.Paratext, "GEN-MAT", "English", null, null);

    public SourceIndexRefreshTests()
    {
        Directory.CreateDirectory(_root);
        var project = new ProjectLocator(Path.Combine(_root, "project.fwdata"), "source-refresh");
        File.WriteAllText(project.FullFwDataPath, "identity input");
        _database = MotifDatabase.OpenOwned(Path.Combine(_root, "project.motif.db"), project,
            MotifSchema.CurrentSchema, new Version(1, 0));
        _environment = AmbientEnvironment.Push(new Dictionary<string, string?>
        {
            [ScriptureLocalStore.PathEnvironmentVariable] = Path.Combine(_root, "scripture-local.json"),
        });
        ScriptureLocalStore.ForInstallation().SetSourcePath(ProjectWorkspaceKey.Compute(project), _entry.Id, _source.Folder);
    }

    [Fact]
    public void MachineVersesBecomeStableOrderedAnchorsWithoutCopyingNotes()
    {
        var path = Path.Combine(_source.Folder, "41MAT.SFM");
        File.WriteAllText(path, File.ReadAllText(path).Replace("The lamp MAT 1", "The \\f + \\ft hidden note\\f* lamp MAT 1"));
        Assert.True(Build());
        var repository = new SourceIndexRepository(_database);
        var first = repository.ReadPage(_entry.Id, "MAT 1", 0, 200);
        Assert.Equal("MAT 1:1#1", first[0].Place.Anchor);
        Assert.Equal("The", first[0].Place.Surface);
        Assert.Equal("MAT 1:1#2", first[1].Place.Anchor);
        Assert.Equal("lamp", first[1].Place.Surface);
        Assert.Contains(first, row => row.Place.Anchor == "MAT 1:2#1");
        Assert.DoesNotContain(first, row => row.Place.Surface is "hidden" or "note");
        Assert.Equal(Enumerable.Range(1, first.Count), first.Select(row => row.Place.Ordinal));
        Assert.All(first, row => Assert.Equal("unmeasured", row.Place.State));
        Assert.Equal(first.Count, repository.ReadPassage(_entry.Id, "MAT 1")!.TotalPlaces);
        Assert.True(Build());
        Assert.Equal(first, repository.ReadPage(_entry.Id, "MAT 1", 0, 200));
    }

    [Fact]
    public void EditingOneChapterRetokenizesOnlyThatChapterAndKeepsOtherHashesAndAnchors()
    {
        Assert.True(Build());
        var repository = new SourceIndexRepository(_database);
        var unchanged = repository.ReadPassage(_entry.Id, "MAT 2");
        var unchangedRows = repository.ReadPage(_entry.Id, "MAT 2", 0, 200);
        var changed = repository.ReadPassage(_entry.Id, "MAT 1");
        var tokenized = new List<string>();
        var path = Path.Combine(_source.Folder, "41MAT.SFM");
        File.WriteAllText(path, File.ReadAllText(path).Replace("lamp MAT 1", "lantern MAT 1"));
        SourceIndexRefresh.PassageTokenized.Value = tokenized.Add;
        Assert.True(Build());
        Assert.Equal(new[] { "MAT 1" }, tokenized);
        Assert.NotEqual(changed!.ContentHash, repository.ReadPassage(_entry.Id, "MAT 1")!.ContentHash);
        Assert.Equal(unchanged, repository.ReadPassage(_entry.Id, "MAT 2"));
        Assert.Equal(unchangedRows, repository.ReadPage(_entry.Id, "MAT 2", 0, 200));
    }

    [Theory]
    [InlineData("project")]
    [InlineData("writing-system")]
    [InlineData("source")]
    [InlineData("path-binding")]
    public void ChangedInputsDuringBuildCannotPublishOverThePreviousGeneration(string input)
    {
        Assert.True(Build());
        var repository = new SourceIndexRepository(_database);
        var before = repository.ReadState(_entry.Id);
        var rows = repository.ReadPage(_entry.Id, "MAT 1", 0, 200);
        var path = Path.Combine(_source.Folder, "41MAT.SFM");
        File.AppendAllText(path, "\n\\c 4\n\\p\n\\v 1 Another invented passage.\n");
        SourceIndexRefresh.PassageTokenized.Value = _ =>
        {
            SourceIndexRefresh.PassageTokenized.Value = null;
            switch (input)
            {
                case "project": File.AppendAllText(_database.Project.FullFwDataPath, " changed"); break;
                case "writing-system":
                    var store = Path.Combine(_root, "WritingSystemStore");
                    Directory.CreateDirectory(store);
                    File.WriteAllText(Path.Combine(store, "qaa.ldml"), "changed identity input");
                    break;
                case "source": File.AppendAllText(path, "\n"); break;
                case "path-binding":
                    ScriptureLocalStore.ForInstallation().ClearSourcePath(ProjectWorkspaceKey.Compute(_database.Project), _entry.Id);
                    break;
            }
        };
        Assert.False(Build());
        Assert.Equal(before, repository.ReadState(_entry.Id));
        Assert.Equal(rows, repository.ReadPage(_entry.Id, "MAT 1", 0, 200));
        Assert.Null(repository.ReadPassage(_entry.Id, "MAT 4"));
    }

    [Fact]
    public void QueueingInvalidatesGenerationAndPersistsARefreshJobWithoutTokenizing()
    {
        Assert.True(Build());
        var repository = new SourceIndexRepository(_database);
        var before = repository.ReadState(_entry.Id)!;
        SourceIndexRefresh.PassageTokenized.Value = _ => Assert.Fail("Enqueue must not tokenize a Source.");
        SourceIndexRefresh.Enqueue(_database);
        var pending = repository.ReadState(_entry.Id)!;
        Assert.Equal("pending", pending.Status);
        Assert.NotEqual(before.Generation, pending.Generation);
        using var reopened = MotifDatabase.OpenOwned(_database.FullPath, _database.Project,
            MotifSchema.CurrentSchema, new Version(1, 0));
        var job = Assert.Single(new JobRepository(reopened).ListActive(ProjectWorkspaceKey.Compute(_database.Project)));
        Assert.Equal(SourceIndexRefresh.JobKind, job.Kind);
    }

    [Fact]
    public void ReconciliationLeavesUnchangedSourcesAndTheirGenerationAlone()
    {
        Assert.True(Build());
        var repository = new SourceIndexRepository(_database);
        var before = repository.ReadState(_entry.Id);
        SourceIndexRefresh.PassageTokenized.Value = _ => Assert.Fail("Maintenance must not tokenize a Source.");
        Assert.False(SourceIndexRefresh.Reconcile(_database));
        Assert.Equal(before, repository.ReadState(_entry.Id));
        Assert.Empty(new JobRepository(_database).ListActive());
    }

    [Fact]
    public void EnqueueRegistersANewSourceAsPendingWithoutTokenizing()
    {
        SourceIndexRefresh.PassageTokenized.Value = _ => Assert.Fail("Enqueue must not tokenize a Source.");
        SourceIndexRefresh.Enqueue(_database, [_entry]);
        var state = new SourceIndexRepository(_database).ReadState(_entry.Id);
        Assert.Equal("pending", state!.Status);
        Assert.Equal("Paratext", state.Kind);
        Assert.Empty(new SourceIndexRepository(_database).ReadPage(_entry.Id, "MAT 1", 0, 100));
        Assert.Single(new JobRepository(_database).ListActive());
    }

    [Fact]
    public void PendingGenerationWithoutAnActiveJobIsRequeuedExactlyOnce()
    {
        Assert.True(Build());
        new SourceIndexRepository(_database).MarkPending(_entry.Id);
        Assert.Empty(new JobRepository(_database).ListActive());
        SourceIndexRefresh.PassageTokenized.Value = _ => Assert.Fail("Maintenance must not tokenize a Source.");

        Assert.True(SourceIndexRefresh.Reconcile(_database));
        Assert.False(SourceIndexRefresh.Reconcile(_database));

        var job = Assert.Single(new JobRepository(_database).ListByProjectAndKind(
            ProjectWorkspaceKey.Compute(_database.Project), SourceIndexRefresh.JobKind));
        Assert.Equal(JobStatus.Queued, job.Status);
        Assert.Equal("pending", new SourceIndexRepository(_database).ReadState(_entry.Id)!.Status);
    }

    [Theory]
    [InlineData("source")]
    [InlineData("project")]
    [InlineData("writing-system")]
    [InlineData("path-binding")]
    public void ReconciliationQueuesChangedInputsOnceWithoutTokenizing(string input)
    {
        Assert.True(Build());
        switch (input)
        {
            case "source": File.AppendAllText(Path.Combine(_source.Folder, "41MAT.SFM"), "\n"); break;
            case "project": File.AppendAllText(_database.Project.FullFwDataPath, " changed"); break;
            case "writing-system":
                var store = Path.Combine(_root, "WritingSystemStore");
                Directory.CreateDirectory(store);
                File.WriteAllText(Path.Combine(store, "qaa.ldml"), "changed identity input");
                break;
            case "path-binding":
                ScriptureLocalStore.ForInstallation().ClearSourcePath(ProjectWorkspaceKey.Compute(_database.Project), _entry.Id);
                break;
        }
        SourceIndexRefresh.PassageTokenized.Value = _ => Assert.Fail("Maintenance must not tokenize a Source.");
        Assert.True(SourceIndexRefresh.Reconcile(_database));
        var pending = new SourceIndexRepository(_database).ReadState(_entry.Id)!;
        Assert.Equal("pending", pending.Status);
        Assert.False(SourceIndexRefresh.Reconcile(_database));
        Assert.Equal(pending, new SourceIndexRepository(_database).ReadState(_entry.Id));
        using var reopened = MotifDatabase.OpenOwned(_database.FullPath, _database.Project,
            MotifSchema.CurrentSchema, new Version(1, 0));
        var job = Assert.Single(new JobRepository(reopened).ListByProjectAndKind(
            ProjectWorkspaceKey.Compute(_database.Project), SourceIndexRefresh.JobKind));
        Assert.Equal(JobStatus.Queued, job.Status);
    }

    [Theory]
    [InlineData(100)]
    [InlineData(200)]
    public void LargePassagePagesMaterializeOnlyTheirRequestedRows(int limit)
    {
        var repository = new SourceIndexRepository(_database);
        var state = new SourceIndexState(_entry.Id, "Paratext", "large", "digest", "{}", "ready", "tokenizer", "anchor");
        const int count = 20000;
        Assert.True(repository.Publish(state, [new("GEN 1", "GEN 1", "hash", count, count)],
            [new("word", "qaa", "lamp", "lamp")],
            Enumerable.Range(1, count).Select(i => new SourceWordPlace("GEN 1", i, "GEN 1:1#" + i, "GEN 1:1", "lamp", "word"))));
        var observed = new List<int>();
        SourceIndexRepository.RowsRead.Value = observed.Add;
        var rows = repository.ReadPage(_entry.Id, "GEN 1", 15000, limit);
        Assert.Equal(limit, rows.Count);
        Assert.Equal(15001, rows[0].Place.Ordinal);
        Assert.Equal(15000 + limit, rows[^1].Place.Ordinal);
        Assert.Equal(new[] { limit }, observed);
    }

    private bool Build() => SourceIndexRefresh.Build(_database, _entry, _source.Folder, "qaa",
        ProgressSourceIdentity.Capture(_database.Project.FullFwDataPath)!);

    public void Dispose()
    {
        SourceIndexRefresh.PassageTokenized.Value = null;
        SourceIndexRepository.RowsRead.Value = null;
        _environment.Dispose();
        _database.Dispose();
        _source.Dispose();
        Directory.Delete(_root, recursive: true);
    }
}

[Collection(LcmCacheParallelCollections.Group3)]
public sealed class SourceIndexRunTests(PristineProjectFixture pristine) : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-source-run-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void CorruptFirstSourceDoesNotPreventHealthySecondSourceFromPublishing()
    {
        Directory.CreateDirectory(_root);
        using var tiny = TinyProject.Create();
        var broken = Path.Combine(_root, "broken");
        Directory.CreateDirectory(broken);
        File.WriteAllText(Path.Combine(broken, "Settings.xml"), "<ScriptureText><broken>");
        using var environment = LocalEnvironment();
        using var database = CreateProject(
            new("WEB", "Broken invented source", "GEN", "Paratext", "English"),
            new("TINY", "Healthy invented source", "MAT", "Paratext", "English"));
        var key = ProjectWorkspaceKey.Compute(database.Project);
        ScriptureLocalStore.ForInstallation().SetSourcePath(key, "WEB", broken);
        ScriptureLocalStore.ForInstallation().SetSourcePath(key, "TINY", tiny.Folder);

        var outcome = SourceIndexRefresh.Run(database);

        Assert.Equal(JobStatus.Completed, outcome.Status);
        var repository = new SourceIndexRepository(database);
        Assert.Equal("stale", repository.ReadState("WEB")!.Status);
        Assert.Empty(repository.ReadPage("WEB", "GEN 1", 0, 100));
        Assert.Equal("ready", repository.ReadState("TINY")!.Status);
        Assert.Equal("MAT 1:1#1", repository.ReadPage("TINY", "MAT 1", 0, 100)[0].Place.Anchor);
    }

    [Fact]
    public void ASourceChangedDuringTokenizationFailsAndQueuesAFreshRunThatCanPublish()
    {
        Directory.CreateDirectory(_root);
        using var tiny = TinyProject.Create();
        using var environment = LocalEnvironment();
        using var database = CreateProject(new SeededSourceList.Item("TINY", "Invented source", "MAT", "Paratext", "English"));
        var key = ProjectWorkspaceKey.Compute(database.Project);
        ScriptureLocalStore.ForInstallation().SetSourcePath(key, "TINY", tiny.Folder);
        SourceIndexRefresh.PassageTokenized.Value = _ =>
        {
            SourceIndexRefresh.PassageTokenized.Value = null;
            File.AppendAllText(Path.Combine(tiny.Folder, "41MAT.SFM"), "\n");
        };

        var raced = SourceIndexRefresh.Run(database);

        Assert.Equal(JobStatus.Failed, raced.Status);
        Assert.Equal(JobFailureCategory.Infrastructure, raced.Category);
        var job = Assert.Single(new JobRepository(database).ListByProjectAndKind(key, SourceIndexRefresh.JobKind));
        Assert.Equal(JobStatus.Queued, job.Status);
        Assert.Null(new SourceIndexRepository(database).ReadState("TINY"));

        var stable = SourceIndexRefresh.Run(database);

        Assert.Equal(JobStatus.Completed, stable.Status);
        var repository = new SourceIndexRepository(database);
        Assert.Equal("ready", repository.ReadState("TINY")!.Status);
        Assert.NotEmpty(repository.ReadPage("TINY", "MAT 1", 0, 100));
        Assert.Single(new JobRepository(database).ListByProjectAndKind(key, SourceIndexRefresh.JobKind));
    }

    [Fact]
    public void InitialProjectCaptureRaceQueuesRetryBeforeAnySourceStateExists()
    {
        Directory.CreateDirectory(_root);
        using var tiny = TinyProject.Create();
        using var environment = LocalEnvironment();
        using var database = CreateProject(new SeededSourceList.Item("TINY", "Invented source", "MAT", "Paratext", "English"));
        var key = ProjectWorkspaceKey.Compute(database.Project);
        ScriptureLocalStore.ForInstallation().SetSourcePath(key, "TINY", tiny.Folder);
        ProgressSourceIdentity.BeforeHash.Value = () =>
        {
            ProgressSourceIdentity.BeforeHash.Value = null;
            File.AppendAllText(database.Project.FullFwDataPath, "\n");
        };

        var raced = SourceIndexRefresh.Run(database);

        Assert.Equal(JobStatus.Failed, raced.Status);
        Assert.Equal(JobFailureCategory.Infrastructure, raced.Category);
        Assert.Null(new SourceIndexRepository(database).ReadState("TINY"));
        var job = Assert.Single(new JobRepository(database).ListByProjectAndKind(key, SourceIndexRefresh.JobKind));
        Assert.Equal(JobStatus.Queued, job.Status);

        var stable = SourceIndexRefresh.Run(database);

        Assert.Equal(JobStatus.Completed, stable.Status);
        Assert.Equal("ready", new SourceIndexRepository(database).ReadState("TINY")!.Status);
        Assert.Single(new JobRepository(database).ListByProjectAndKind(key, SourceIndexRefresh.JobKind));
    }

    [Fact]
    public void AnObsoleteRemovedSourceSnapshotCannotReplaceANewerPublication()
    {
        Directory.CreateDirectory(_root);
        using var tiny = TinyProject.Create();
        using var environment = LocalEnvironment();
        using var database = CreateProject(
            new("TINY", "Invented source", "MAT", "Paratext", "English"),
            new("OLD", "Removed source", "MAT", "Paratext", "English", "Removed"));
        var key = ProjectWorkspaceKey.Compute(database.Project);
        ScriptureLocalStore.ForInstallation().SetSourcePath(key, "TINY", tiny.Folder);
        SourceIndexRefresh.PassageTokenized.Value = _ =>
        {
            SourceIndexRefresh.PassageTokenized.Value = null;
            File.AppendAllText(database.Project.FullFwDataPath, "\n");
            new SourceIndexRepository(database).Publish(new("OLD", "Paratext", "newer", "newer-content",
                "newer-identity", "ready", ParatextPassages.TokenizerVersion, ParatextPassages.AnchorVersion), [], [], []);
        };

        Assert.Equal(JobStatus.Failed, SourceIndexRefresh.Run(database).Status);

        var state = new SourceIndexRepository(database).ReadState("OLD")!;
        Assert.Equal("newer-content", state.SourceDigest);
        Assert.Equal("newer-identity", state.IdentityJson);
        Assert.Single(new JobRepository(database).ListActive());
    }

    private IDisposable LocalEnvironment() => AmbientEnvironment.Push(new Dictionary<string, string?>
    {
        [ScriptureLocalStore.PathEnvironmentVariable] = Path.Combine(_root, "scripture-local.json"),
    });

    private MotifDatabase CreateProject(params SeededSourceList.Item[] sources)
    {
        ProjectLocator project;
        using (var cache = pristine.NewScratch())
        {
            SeededSourceList.AddList(cache, sources);
            new FwDataProjectLoader().Save(cache);
            project = new ProjectLocator(cache.ProjectId.Path, cache.LangProject.Guid.ToString());
        }
        return MotifDatabase.OpenOwned(Path.Combine(_root, "project.motif.db"), project,
            MotifSchema.CurrentSchema, new Version(1, 0));
    }

    public void Dispose()
    {
        SourceIndexRefresh.PassageTokenized.Value = null;
        ProgressSourceIdentity.BeforeHash.Value = null;
        Directory.Delete(_root, recursive: true);
    }
}
