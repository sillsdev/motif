using System.Security.Cryptography;
using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.DomainServices;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Host;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.Sources;
using SIL.Motif.Host.Store;
using SIL.Motif.Scripture;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Sources;
using Xunit;

namespace SIL.Motif.Tests.Worker;

/// <summary>Pins the FieldWorks Texts provider: Passages are Texts, places are the Text words occurrence identities, and reads never build.</summary>
[Collection(LcmCacheParallelCollections.Group0)]
public sealed class FieldWorksTextsSourceIndexTests(PristineProjectFixture pristine) : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-fw-texts-index-" + Guid.NewGuid().ToString("N"));
    private IDisposable? _environment;
    private MotifDatabase? _database;

    [Fact]
    public void EachTextIsOnePassageAndEachWordIsAPlaceAtItsSegmentOccurrence()
    {
        using var cache = Saved(out var seeded);
        Assert.True(Build(cache));
        var repository = new SourceIndexRepository(_database!);
        var state = repository.ReadState(FieldWorksTextsIndex.SourceId)!;
        Assert.Equal("ready", state.Status);
        Assert.Equal(FieldWorksTextsIndex.Kind, state.Kind);
        var passage = repository.ReadPassage(FieldWorksTextsIndex.SourceId, seeded.TextId.ToString("D"))!;
        Assert.Equal(2, passage.TotalPlaces);
        var rows = repository.ReadPage(FieldWorksTextsIndex.SourceId, passage.PassageId, 0, 200);
        Assert.Equal([1, 2], rows.Select(row => row.Place.Ordinal));
        Assert.Equal($"{seeded.FirstSegmentId:D}#0", rows[0].Place.Anchor);
        Assert.Equal(SeededProject.AnalysedWordForm, rows[0].Place.Surface);
        Assert.Equal(seeded.AnalysedWordformId.ToString("D"), rows[0].Wordform.FieldWorksId);
        Assert.Equal(seeded.ApprovedAnalysisId.ToString("D"), rows[0].Place.ReadingKey);
        Assert.Equal("unmeasured", rows[0].Place.State);
        Assert.Equal($"{seeded.SecondSegmentId:D}#0", rows[1].Place.Anchor);
        Assert.Null(rows[1].Place.ReadingKey);
        Assert.Equal("no-analysis", rows[1].Place.State);
    }

    [Fact]
    public void HomographWordformsKeepSeparateIdsAndShareOneTypeIdentity()
    {
        using var cache = pristine.NewScratch();
        var seeded = SeededProject.SeedText(cache, pristine.Seed);
        var vern = cache.DefaultVernWs;
        var homograph = default(Guid);
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var duplicate = cache.ServiceLocator.GetInstance<IWfiWordformFactory>()
                .Create(TsStringUtils.MakeString(SeededProject.AnalysedWordForm, vern));
            homograph = duplicate.Guid;
            var segment = cache.ServiceLocator.GetInstance<ISegmentRepository>().GetObject(seeded.SecondSegmentId);
            segment.AnalysesRS.Add(duplicate);
        });
        new FwDataProjectLoader().Save(cache);
        Open(cache);
        Assert.True(Build(cache));
        var rows = new SourceIndexRepository(_database!)
            .ReadPage(FieldWorksTextsIndex.SourceId, seeded.TextId.ToString("D"), 0, 200)
            .Where(row => row.Place.Surface == SeededProject.AnalysedWordForm).ToArray();
        Assert.Equal(2, rows.Length);
        Assert.NotEqual(rows[0].Place.WordformKey, rows[1].Place.WordformKey);
        Assert.Equal(new[] { seeded.AnalysedWordformId.ToString("D"), homograph.ToString("D") }.Order(),
            rows.Select(row => row.Wordform.FieldWorksId!).Order());
        Assert.Single(rows.Select(row => (row.Wordform.WritingSystem, row.Wordform.Normalized)).Distinct());
    }

    [Fact]
    public void EligibleCountsAreTheProgressDenominatorOfTheSameSavedProject()
    {
        using var cache = Saved(out var seeded);
        Assert.True(Build(cache));
        var totals = ProgressProjectionBuilder.Aggregate(ProgressProjectionBuilder.Build(
            TextWordsProjectionBuilder.Build(cache, CancellationToken.None, includeCoverage: true)).Texts);
        var passage = new SourceIndexRepository(_database!).ReadPassage(FieldWorksTextsIndex.SourceId, seeded.TextId.ToString("D"))!;
        Assert.Equal(totals.Tokens.Total, passage.EligiblePlaces);
        Assert.True(passage.EligiblePlaces <= passage.TotalPlaces);
    }

    [Fact]
    public void ProjectChangeDuringTheBuildCannotPublishOverThePreviousGeneration()
    {
        using var cache = Saved(out var seeded);
        Assert.True(Build(cache));
        var repository = new SourceIndexRepository(_database!);
        var before = repository.ReadState(FieldWorksTextsIndex.SourceId);
        var rows = repository.ReadPage(FieldWorksTextsIndex.SourceId, seeded.TextId.ToString("D"), 0, 200);
        FieldWorksTextsIndex.TextRead.Value = _ =>
        {
            FieldWorksTextsIndex.TextRead.Value = null;
            File.AppendAllText(_database!.Project.FullFwDataPath, " ");
        };
        Assert.False(Build(cache));
        Assert.Equal(before, repository.ReadState(FieldWorksTextsIndex.SourceId));
        Assert.Equal(rows, repository.ReadPage(FieldWorksTextsIndex.SourceId, seeded.TextId.ToString("D"), 0, 200));
    }

    [Fact]
    public void QueuedRefreshPublishesTextsWithoutWritingTheProjectAndReconciliationFollowsProjectChanges()
    {
        using var cache = Saved(out var seeded);
        var path = _database!.Project.FullFwDataPath;
        var before = SHA256.HashData(File.ReadAllBytes(path));
        var read = new List<string>();
        FieldWorksTextsIndex.TextRead.Value = read.Add;
        SourceIndexRefresh.Enqueue(_database);
        var pending = new SourceIndexRepository(_database).ReadState(FieldWorksTextsIndex.SourceId)!;
        Assert.Equal("pending", pending.Status);
        Assert.Empty(read);
        var outcome = SourceIndexRefresh.Run(_database);
        Assert.Equal(SIL.Motif.Contract.Jobs.JobStatus.Completed, outcome.Status);
        Assert.Equal([seeded.TextId.ToString("D")], read);
        var ready = new SourceIndexRepository(_database).ReadState(FieldWorksTextsIndex.SourceId)!;
        Assert.Equal("ready", ready.Status);
        Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(path)));
        FieldWorksTextsIndex.TextRead.Value = _ => Assert.Fail("Maintenance must not read Texts.");
        using (var connection = _database.OpenConnection())
        using (var jobs = connection.CreateCommand())
        {
            jobs.CommandText = "UPDATE Jobs SET Status='completed' WHERE Kind='source-index';";
            jobs.ExecuteNonQuery();
        }
        Assert.False(SourceIndexRefresh.Reconcile(_database));
        Assert.Equal(ready, new SourceIndexRepository(_database).ReadState(FieldWorksTextsIndex.SourceId));
        File.AppendAllText(path, " ");
        Assert.True(SourceIndexRefresh.Reconcile(_database));
    }

    [Fact]
    public void UnchangedTextsAreCopiedAndEveryBuildEqualsAFullRebuild()
    {
        using var cache = pristine.NewScratch();
        var first = SeededProject.SeedText(cache, pristine.Seed);
        var second = SeededProject.SeedText(cache, pristine.Seed);
        var both = new[] { first.TextId.ToString("D"), second.TextId.ToString("D") }.Order().ToArray();
        new FwDataProjectLoader().Save(cache);
        Open(cache);
        Assert.Equal(new string[0], BuildCounting(cache));
        var baseline = Dump(_database!);
        Assert.Equal(both, baseline.Passages.Select(p => p.PassageId).Order());

        Assert.Equal(both, BuildCounting(cache).Order());
        Assert.Equal(baseline, Dump(_database!));

        var vern = cache.DefaultVernWs;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var added = cache.ServiceLocator.GetInstance<IWfiWordformFactory>()
                .Create(TsStringUtils.MakeString("motifaddedword", vern));
            cache.ServiceLocator.GetInstance<ISegmentRepository>().GetObject(second.SecondSegmentId).AnalysesRS.Add(added);
        });
        new FwDataProjectLoader().Save(cache);
        Open(cache);
        Assert.Equal([first.TextId.ToString("D")], BuildCounting(cache));
        var afterAdd = Dump(_database!);
        Assert.Equal(baseline.Passages.Single(p => p.PassageId == first.TextId.ToString("D")),
            afterAdd.Passages.Single(p => p.PassageId == first.TextId.ToString("D")));
        Assert.Equal(baseline.Passages.Single(p => p.PassageId == second.TextId.ToString("D")).TotalPlaces + 1,
            afterAdd.Passages.Single(p => p.PassageId == second.TextId.ToString("D")).TotalPlaces);
        Assert.Equal(afterAdd, FullRebuildDump(cache));

        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            cache.ServiceLocator.GetInstance<IWfiWordformRepository>().GetObject(first.AnalysedWordformId)
                .Form.set_String(vern, "motifchangedword"));
        new FwDataProjectLoader().Save(cache);
        Open(cache);
        Assert.Equal([second.TextId.ToString("D")], BuildCounting(cache));
        var afterEdit = Dump(_database!);
        Assert.Contains(afterEdit.Places, row => row.Place.Surface == "motifchangedword");
        Assert.Equal(afterEdit, FullRebuildDump(cache));
    }

    [Fact]
    public void EachTextIsStagedBeforeTheNextIsReadAndTheWholeProjectionIsNeverBuilt()
    {
        using var cache = pristine.NewScratch();
        var first = SeededProject.SeedText(cache, pristine.Seed);
        var second = SeededProject.SeedText(cache, pristine.Seed);
        new FwDataProjectLoader().Save(cache);
        Open(cache);
        var events = new List<string>();
        FieldWorksTextsIndex.TextRead.Value = id => events.Add("read " + id);
        FieldWorksTextsIndex.TextStaged.Value = id => events.Add("staged " + id);
        TextWordsProjectionBuilder.BeforeOccurrenceRead.Value = () => events.Add("projection");
        try { Assert.True(Build(cache)); }
        finally
        {
            FieldWorksTextsIndex.TextStaged.Value = null;
            TextWordsProjectionBuilder.BeforeOccurrenceRead.Value = null;
        }
        var order = new[] { first.TextId.ToString("D"), second.TextId.ToString("D") }.Order().ToArray();
        Assert.Equal([$"read {order[0]}", $"staged {order[0]}", $"read {order[1]}", $"staged {order[1]}"], events);
    }

    [Fact]
    public void ADeletedAndAnAddedTextEachLeaveTheIncrementalBuildEqualToAFullRebuild()
    {
        using var cache = pristine.NewScratch();
        var first = SeededProject.SeedText(cache, pristine.Seed);
        var second = SeededProject.SeedText(cache, pristine.Seed);
        new FwDataProjectLoader().Save(cache);
        Open(cache);
        Assert.True(Build(cache));
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            cache.ServiceLocator.GetInstance<ITextRepository>().GetObject(second.TextId).Delete());
        new FwDataProjectLoader().Save(cache);
        Open(cache);
        Assert.Equal([first.TextId.ToString("D")], BuildCounting(cache));
        var afterDelete = Dump(_database!);
        Assert.Equal([first.TextId.ToString("D")], afterDelete.Passages.Select(p => p.PassageId));
        Assert.Equal(afterDelete, FullRebuildDump(cache));

        var third = SeededProject.SeedText(cache, pristine.Seed);
        new FwDataProjectLoader().Save(cache);
        Open(cache);
        Assert.Equal([first.TextId.ToString("D")], BuildCounting(cache));
        var afterAdd = Dump(_database!);
        Assert.Contains(afterAdd.Passages, p => p.PassageId == third.TextId.ToString("D"));
        Assert.Equal(afterAdd, FullRebuildDump(cache));
    }

    [Fact]
    public void AWritingSystemChangeCopiesNothingAndEqualsAFullRebuild()
    {
        using var cache = pristine.NewScratch();
        SeededProject.SeedText(cache, pristine.Seed);
        SeededProject.SeedText(cache, pristine.Seed);
        new FwDataProjectLoader().Save(cache);
        Open(cache);
        Assert.True(Build(cache));
        var reused = new List<string>();
        FieldWorksTextsIndex.TextReused.Value = reused.Add;
        Assert.True(FieldWorksTextsIndex.Build(_database!, cache, "qaa-x-other",
            ProgressSourceIdentity.Capture(_database!.Project.FullFwDataPath)!));
        Assert.Empty(reused);
        var incremental = Dump(_database!);
        using var fresh = MotifDatabase.OpenOwned(Path.Combine(_root, "ws-full.motif.db"), _database.Project,
            MotifSchema.CurrentSchema, new Version(1, 0));
        Assert.True(FieldWorksTextsIndex.Build(fresh, cache, "qaa-x-other",
            ProgressSourceIdentity.Capture(_database.Project.FullFwDataPath)!));
        Assert.Equal(incremental, Dump(fresh));
    }

    [Fact]
    public void ASegmentationChangeRebuildsOnlyThatTextAndEqualsAFullRebuild()
    {
        using var cache = pristine.NewScratch();
        var first = SeededProject.SeedText(cache, pristine.Seed);
        var second = SeededProject.SeedText(cache, pristine.Seed);
        new FwDataProjectLoader().Save(cache);
        Open(cache);
        Assert.True(Build(cache));
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var segments = cache.ServiceLocator.GetInstance<ISegmentRepository>();
            var from = segments.GetObject(second.SecondSegmentId);
            var into = segments.GetObject(second.FirstSegmentId);
            var moved = from.AnalysesRS.ToArray();
            from.AnalysesRS.Clear();
            foreach (var item in moved) into.AnalysesRS.Add(item);
        });
        new FwDataProjectLoader().Save(cache);
        Open(cache);
        Assert.Equal([first.TextId.ToString("D")], BuildCounting(cache));
        Assert.Equal(Dump(_database!), FullRebuildDump(cache));
    }

    private List<string> BuildCounting(LcmCache cache)
    {
        var reused = new List<string>();
        FieldWorksTextsIndex.TextReused.Value = reused.Add;
        try { Assert.True(Build(cache)); }
        finally { FieldWorksTextsIndex.TextReused.Value = null; }
        return reused;
    }

    private IndexDump FullRebuildDump(LcmCache cache)
    {
        using var fresh = MotifDatabase.OpenOwned(Path.Combine(_root, "full-" + Guid.NewGuid().ToString("N") + ".motif.db"),
            _database!.Project, MotifSchema.CurrentSchema, new Version(1, 0));
        Assert.True(FieldWorksTextsIndex.Build(fresh, cache, "qaa-x-test",
            ProgressSourceIdentity.Capture(_database.Project.FullFwDataPath)!));
        return Dump(fresh);
    }

    private sealed record IndexDump(string Digest, IReadOnlyList<SourcePassage> Passages, IReadOnlyList<SourceIndexedPlace> Places)
    {
        public bool Equals(IndexDump? other) => other is not null && Digest == other.Digest
            && Passages.SequenceEqual(other.Passages) && Places.SequenceEqual(other.Places);
        public override int GetHashCode() => Digest.GetHashCode();
    }

    private static IndexDump Dump(MotifDatabase database)
    {
        var repository = new SourceIndexRepository(database);
        var passages = repository.ReadPassages(FieldWorksTextsIndex.SourceId).OrderBy(p => p.PassageId, StringComparer.Ordinal).ToArray();
        var places = new List<SourceIndexedPlace>();
        foreach (var passage in passages)
        {
            var after = 0;
            while (true)
            {
                var page = repository.ReadPage(FieldWorksTextsIndex.SourceId, passage.PassageId, after, 200);
                if (page.Count == 0) break;
                places.AddRange(page);
                after = page[^1].Place.Ordinal;
            }
        }
        return new(repository.ReadState(FieldWorksTextsIndex.SourceId)!.SourceDigest, passages, places);
    }

    private LcmCache Saved(out SeededText seeded)
    {
        var cache = pristine.NewScratch();
        seeded = SeededProject.SeedText(cache, pristine.Seed);
        new FwDataProjectLoader().Save(cache);
        Open(cache);
        return cache;
    }

    private void Open(LcmCache cache)
    {
        _database?.Dispose();
        Directory.CreateDirectory(_root);
        _environment ??= AmbientEnvironment.Push(new Dictionary<string, string?>
        {
            [ScriptureLocalStore.PathEnvironmentVariable] = Path.Combine(_root, "scripture-local.json"),
        });
        var path = cache.ProjectId.Path;
        var project = new ProjectLocator(path, Path.GetFileNameWithoutExtension(path));
        _database = MotifDatabase.OpenOwned(Path.Combine(_root, "project.motif.db"), project,
            MotifSchema.CurrentSchema, new Version(1, 0));
    }

    private bool Build(LcmCache cache) => FieldWorksTextsIndex.Build(_database!, cache, "qaa-x-test",
        ProgressSourceIdentity.Capture(_database!.Project.FullFwDataPath)!);

    public void Dispose()
    {
        FieldWorksTextsIndex.TextRead.Value = null;
        FieldWorksTextsIndex.TextReused.Value = null;
        FieldWorksTextsIndex.TextStaged.Value = null;
        _environment?.Dispose();
        _database?.Dispose();
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
