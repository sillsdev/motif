using System.Text.Json;
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

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void WindowsVolumeWithoutExtendedFileIdsStillGetsAStableIdentity(bool hasLegacyId)
    {
        var legacy = new ProjectFileIdentity(0x89abcdef, 0x76543210fedcba98);
        var calls = 0;
        ProjectFileIdentity Read(string path, string? resolved) => ProjectFileIdentity.ReadWindowsIdentity(path,
            () => (87, default),
            () => { calls++; return hasLegacyId ? (0, legacy) : (87, default); },
            () => resolved);

        var original = Read(@"C:\Projects\EntryAlias.fwdata", @"\\?\C:\Projects\Lexicon.fwdata");
        var alias = Read(@"C:\PROJECTS\LEXICON.FWDATA", null);

        Assert.Equal(2, calls);
        Assert.Equal(original, alias);
        if (hasLegacyId) Assert.Equal(legacy, original);
        else Assert.NotEqual(original, Read(@"C:\Projects\Other.fwdata", null));
    }

    [Fact]
    public void PermanentProjectIoFailureDoesNotQueueAnotherJobOrRestartOnReconciliation()
    {
        var jobs = new JobRepository(_database);
        SourceIndexRefresh.Enqueue(_database);
        var current = jobs.Transition(Assert.Single(jobs.ListActive()), JobStatus.Running);
        ProgressSourceIdentity.BeforeHash.Value = () => throw new IOException("The project file cannot be read.");
        try
        {
            var outcome = SourceIndexRefresh.Run(_database);
            Assert.Equal(JobStatus.Failed, outcome.Status);
            jobs.Transition(current.JobId, outcome.Status, current.Version, outcome.Category, outcome.ResultJson);
            Assert.Empty(jobs.ListActive());
            using var detail = JsonDocument.Parse(outcome.ResultJson!);
            Assert.Equal("PermanentIo", detail.RootElement.GetProperty("reason").GetString());
            Assert.False(SourceIndexRefresh.Reconcile(_database));
            Assert.Single(jobs.ListByProjectAndKind(current.ProjectKey, current.Kind));
            jobs.DeleteArchived(current.JobId);
            Assert.False(SourceIndexRefresh.Reconcile(_database));
            Assert.Empty(jobs.ListByProjectAndKind(current.ProjectKey, current.Kind));
            SourceIndexRefresh.Enqueue(_database);
            Assert.Equal(JobStatus.Queued, Assert.Single(jobs.ListActive()).Status);
        }
        finally { ProgressSourceIdentity.BeforeHash.Value = null; }
    }

    [Fact]
    public void RestoringReadAccessToAWritingSystemFileQueuesOneRefresh()
    {
        if (OperatingSystem.IsWindows()) return;
        var store = Directory.CreateDirectory(Path.Combine(_root, "WritingSystemStore"));
        var ldml = Path.Combine(store.FullName, "ar.ldml");
        File.WriteAllText(ldml, "<ldml/>");
        var original = File.GetUnixFileMode(ldml);
        var jobs = new JobRepository(_database);
        SourceIndexRefresh.Enqueue(_database);
        var current = jobs.Transition(Assert.Single(jobs.ListActive()), JobStatus.Running);
        File.SetUnixFileMode(ldml, UnixFileMode.None);
        try
        {
            // A privileged account reads through mode bits, so the failure cannot be produced.
            if (Record.Exception(() => File.OpenRead(ldml).Dispose()) is null) return;
            var outcome = SourceIndexRefresh.Run(_database);
            Assert.Equal(JobStatus.Failed, outcome.Status);
            jobs.Transition(current.JobId, outcome.Status, current.Version, outcome.Category, outcome.ResultJson);
            using var detail = JsonDocument.Parse(outcome.ResultJson!);
            Assert.Equal("PermanentIo", detail.RootElement.GetProperty("reason").GetString());
            Assert.False(SourceIndexRefresh.Reconcile(_database));
            Assert.Empty(jobs.ListActive());
            var stamp = File.GetLastWriteTimeUtc(ldml);
            File.SetUnixFileMode(ldml, original);
            File.SetLastWriteTimeUtc(ldml, stamp);
            Assert.True(SourceIndexRefresh.Reconcile(_database));
            Assert.Equal(JobStatus.Queued, Assert.Single(jobs.ListActive()).Status);
            Assert.False(SourceIndexRefresh.Reconcile(_database));
            Assert.Single(jobs.ListActive());
        }
        finally { File.SetUnixFileMode(ldml, original); }
    }

    [Fact]
    public void ProjectBusyRequeuesTwiceAndStopsAcrossAttemptsAndReconciliation()
    {
        var jobs = new JobRepository(_database);
        SourceIndexRefresh.Enqueue(_database);
        ProgressSourceIdentity.BeforeHash.Value = () =>
            throw new ProjectStartupBusyException(_database.Project.FullFwDataPath, "Another Motif project opener");
        try
        {
            for (var attempt = 0; attempt < 3; attempt++)
            {
                var current = jobs.Transition(Assert.Single(jobs.ListActive()), JobStatus.Running);
                var outcome = SourceIndexRefresh.Run(_database);
                Assert.Equal(JobStatus.Failed, outcome.Status);
                jobs.Transition(current.JobId, outcome.Status, current.Version, outcome.Category, outcome.ResultJson);
            }
            Assert.Empty(jobs.ListActive());
            Assert.False(SourceIndexRefresh.Reconcile(_database));
            Assert.Equal(3, jobs.ListByProjectAndKind(ProjectWorkspaceKey.Compute(_database.Project),
                SourceIndexRefresh.JobKind).Count);
        }
        finally { ProgressSourceIdentity.BeforeHash.Value = null; }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SourceIndexStopsWaitingForAStalledProjectOpen(bool cancel)
    {
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var waiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var holder = Task.Run(() =>
        {
            using var gate = ProjectStartupGate.Enter(_database.Project.FullFwDataPath);
            held.SetResult();
            release.Task.WaitAsync(Wait.Scaled(TimeSpan.FromMinutes(2))).GetAwaiter().GetResult();
        });
        using var cancellation = new CancellationTokenSource();
        JobOutcome? outcome = null;
        Task<Exception?>? worker = null;
        try
        {
            await held.Task.WaitAsync(Wait.Scaled(Wait.Default));
            worker = Task.Run<Exception?>(() =>
            {
                ProjectStartupGate.WaitingForWorkerRead.Value = () => waiting.TrySetResult();
                return Record.Exception(() => { outcome = SourceIndexRefresh.Run(_database, cancellation.Token); });
            });
            await waiting.Task.WaitAsync(Wait.Scaled(Wait.Default));
            if (cancel) cancellation.Cancel();
            var within = cancel ? TimeSpan.FromSeconds(2) : TimeSpan.FromSeconds(20);
            Assert.True(await Wait.ForAsync(() => worker.IsCompleted, within),
                "Source indexing stayed blocked behind the stalled project open.");
            if (cancel)
            {
                Assert.IsAssignableFrom<OperationCanceledException>(await worker);
                Assert.Empty(new JobRepository(_database).ListActive());
            }
            else
            {
                Assert.Null(await worker);
                Assert.Equal(JobStatus.Failed, outcome!.Status);
                Assert.Contains("retryQueued", outcome.ResultJson);
                Assert.Equal(SourceIndexRefresh.JobKind, Assert.Single(new JobRepository(_database).ListActive()).Kind);
            }
        }
        finally
        {
            release.TrySetResult();
            await holder;
            if (worker is not null) await worker;
        }
    }

    [RequiresSymbolicLinkFact]
    public async Task SourceIndexAndProjectOpenShareTheGateThroughNestedSymlinks()
    {
        var real = Path.Combine(_root, "real");
        var folder = Path.Combine(real, "Lexicon");
        var parentAlias = Path.Combine(_root, "parent-alias");
        var entryAlias = Path.Combine(_root, "entry-alias");
        Directory.CreateDirectory(folder);
        Directory.CreateSymbolicLink(parentAlias, real);
        Directory.CreateSymbolicLink(entryAlias, Path.Combine(parentAlias, "Lexicon"));
        var path = Path.Combine(folder, "Lexicon.fwdata");
        File.WriteAllText(path, "identity input");

        var acquired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var waiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var holder = Task.Run(() =>
        {
            using var gate = ProjectStartupGate.Enter(path);
            acquired.SetResult();
            release.Task.WaitAsync(Wait.Scaled(Wait.Default)).GetAwaiter().GetResult();
        });
        Task? opener = null;
        try
        {
            await acquired.Task.WaitAsync(Wait.Scaled(Wait.Default));
            var sibling = Path.Combine(folder, "lexicon.fwdata");
            if (!File.Exists(sibling))
            {
                File.WriteAllText(sibling, "separate file");
                await Task.Run(() =>
                {
                    using var gate = ProjectStartupGate.Enter(sibling);
                }).WaitAsync(Wait.Scaled(Wait.Default));
            }
            opener = Task.Run(() =>
            {
                ProjectStartupGate.WaitingForWorkerRead.Value = () => waiting.TrySetResult();
                using var gate = ProjectStartupGate.Enter(Path.Combine(entryAlias, "Lexicon.fwdata"));
            });
            Assert.Same(waiting.Task, await Task.WhenAny(opener, waiting.Task).WaitAsync(Wait.Scaled(Wait.Default)));
        }
        finally
        {
            release.TrySetResult();
            await holder;
            if (opener is not null) await opener;
        }
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

    [Fact]
    public void AChangedSegmentationProfileRetokenizesEvenUnchangedPassages()
    {
        Assert.True(Build());
        var writingSystems = Path.Combine(_root, "WritingSystemStore");
        Directory.CreateDirectory(writingSystems);
        File.WriteAllText(Path.Combine(writingSystems, "qaa.ldml"), "new profile witness");
        var tokenized = new List<string>();
        SourceIndexRefresh.PassageTokenized.Value = tokenized.Add;
        Assert.True(Build());
        Assert.NotEmpty(tokenized);
        Assert.Equal(new SourceIndexRepository(_database).ReadPassages(_entry.Id).Count, tokenized.Count);
    }

    [Fact]
    public void AnIndexStampedByTheOldProfileReusePolicyRetokenizesEvenWithTheCurrentLdmlDigest()
    {
        Assert.True(Build());
        using (var connection = _database.OpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "UPDATE SourceIndexState SET TokenizerVersion='fieldworks-word-forming-1' WHERE SourceId=$id;";
            command.Parameters.AddWithValue("$id", _entry.Id);
            command.ExecuteNonQuery();
        }

        Assert.True(SourceIndexRefresh.Reconcile(_database));
        var tokenized = new List<string>();
        SourceIndexRefresh.PassageTokenized.Value = tokenized.Add;
        Assert.True(Build());
        Assert.Equal(new SourceIndexRepository(_database).ReadPassages(_entry.Id).Count, tokenized.Count);
    }

    [Fact]
    public void AnIndexBuiltWithTheOldMachineTokeniserIsQueuedForRebuildAndRetokenizesEveryChapter()
    {
        Assert.True(Build());
        var repository = new SourceIndexRepository(_database);
        var built = repository.ReadState(_entry.Id)!;
        using (var connection = _database.OpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "UPDATE SourceIndexState SET TokenizerVersion='machine-3.9.7-latin-1' WHERE SourceId=$id;";
            command.Parameters.AddWithValue("$id", built.SourceId);
            command.ExecuteNonQuery();
        }
        Assert.True(SourceIndexRefresh.Reconcile(_database));
        var tokenized = new List<string>();
        SourceIndexRefresh.PassageTokenized.Value = tokenized.Add;
        Assert.True(Build());
        Assert.Contains("MAT 1", tokenized);
        Assert.Contains("MAT 2", tokenized);
        Assert.Equal(ParatextPassages.TokenizerVersion, repository.ReadState(_entry.Id)!.TokenizerVersion);
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

    private static readonly string[] Digits = ["0", "1", "2", "3", "4", "5", "6", "7", "8", "9"];

    private bool Build() => SourceIndexRefresh.Build(_database, _entry, _source.Folder, "qaa",
        ProgressSourceIdentity.Capture(_database.Project.FullFwDataPath)!, FieldWorksTokeniser.Create(Digits));

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
    public void UnreadableSourceLeavesHealthyIndexesReadyAndReconciliationDetectsChanges()
    {
        Directory.CreateDirectory(_root);
        using var unreadable = TinyProject.Create();
        using var healthy = TinyProject.Create();
        using var environment = LocalEnvironment();
        using var database = CreateProject(
            new("WEB", "First invented source", "MAT", "Paratext", "English"),
            new("TINY", "Second invented source", "MAT", "Paratext", "English"));
        var key = ProjectWorkspaceKey.Compute(database.Project);
        ScriptureLocalStore.ForInstallation().SetSourcePath(key, "WEB", unreadable.Folder);
        ScriptureLocalStore.ForInstallation().SetSourcePath(key, "TINY", healthy.Folder);
        SourceIndexRefresh.Enqueue(database);
        Assert.Equal(JobStatus.Completed, RunQueued(database).Status);

        var deniedPath = Path.Combine(unreadable.Folder, "Settings.xml");
        SourceIndexIdentity.BeforeReadFile.Value = path =>
        {
            if (path == deniedPath) throw new UnauthorizedAccessException("Cannot read Source settings.");
        };
        SourceIndexRefresh.Enqueue(database);
        AssertPermanentIo(RunQueued(database));

        var repository = new SourceIndexRepository(database);
        Assert.Equal("stale", repository.ReadState("WEB")!.Status);
        Assert.Empty(repository.ReadPage("WEB", "MAT 1", 0, 100));
        Assert.Equal("ready", repository.ReadState("TINY")!.Status);
        Assert.Equal("MAT 1:1#1", repository.ReadPage("TINY", "MAT 1", 0, 100)[0].Place.Anchor);
        Assert.Equal("ready", repository.ReadState(FieldWorksTextsIndex.SourceId)!.Status);
        Assert.False(SourceIndexRefresh.Reconcile(database));

        var healthyBook = Path.Combine(healthy.Folder, "41MAT.SFM");
        File.AppendAllText(healthyBook, "\n");
        Assert.True(SourceIndexRefresh.Reconcile(database));
        AssertPermanentIo(RunQueued(database));
        Assert.False(SourceIndexRefresh.Reconcile(database));

        SourceIndexIdentity.BeforeReadFile.Value = null;
        Assert.True(SourceIndexRefresh.Reconcile(database));
        Assert.Equal(JobStatus.Completed, RunQueued(database).Status);
        Assert.Equal("ready", repository.ReadState("WEB")!.Status);
        Assert.False(SourceIndexRefresh.Reconcile(database));

        File.AppendAllText(healthyBook, "\n");
        Assert.True(SourceIndexRefresh.Reconcile(database));
        Assert.Equal(JobStatus.Completed, RunQueued(database).Status);
        Assert.False(SourceIndexRefresh.Reconcile(database));
    }

    [Fact]
    public void RestoringTheRemovedLiveProjectReconcilesEvenWithTheOriginalBytesAndTimestamps()
    {
        Directory.CreateDirectory(_root);
        using var environment = LocalEnvironment();
        using var database = CreateProject();
        SourceIndexRefresh.Enqueue(database);
        Assert.Equal(JobStatus.Completed, RunQueued(database).Status);

        var path = database.Project.FullFwDataPath;
        var moved = Path.Combine(_root, "saved-project.fwdata");
        File.Move(path, moved);
        try
        {
            SourceIndexRefresh.Enqueue(database);
            AssertPermanentIo(RunQueued(database));
            Assert.False(SourceIndexRefresh.Reconcile(database));
        }
        finally { File.Move(moved, path); }

        Assert.True(SourceIndexRefresh.Reconcile(database));
        Assert.Equal(JobStatus.Completed, RunQueued(database).Status);
        Assert.Equal("ready", new SourceIndexRepository(database).ReadState(FieldWorksTextsIndex.SourceId)!.Status);
        Assert.False(SourceIndexRefresh.Reconcile(database));
    }

    private static JobOutcome RunQueued(MotifDatabase database)
    {
        var jobs = new JobRepository(database);
        var current = jobs.Transition(Assert.Single(jobs.ListActive()), JobStatus.Running);
        var outcome = SourceIndexRefresh.Run(database);
        jobs.Transition(current.JobId, outcome.Status, current.Version, outcome.Category, outcome.ResultJson);
        return outcome;
    }

    private static void AssertPermanentIo(JobOutcome outcome)
    {
        Assert.Equal(JobStatus.Failed, outcome.Status);
        using var detail = JsonDocument.Parse(outcome.ResultJson!);
        Assert.Equal("PermanentIo", detail.RootElement.GetProperty("reason").GetString());
        Assert.False(detail.RootElement.GetProperty("retryQueued").GetBoolean());
    }

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
        SourceIndexIdentity.BeforeReadFile.Value = null;
        ProgressSourceIdentity.BeforeHash.Value = null;
        Directory.Delete(_root, recursive: true);
    }
}
