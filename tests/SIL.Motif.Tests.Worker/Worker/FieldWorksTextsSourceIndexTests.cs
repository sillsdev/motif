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
        _environment?.Dispose();
        _database?.Dispose();
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
