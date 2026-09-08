using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using SIL.LCModel;
using SIL.Motif.Commands.Assess;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.Parser;
using SIL.Motif.Host.Store;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.PanGloss;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary>
/// Pins <see cref="AssessCommand"/> over a real, file-backed seeded project, against a fake
/// <see cref="IAssessor"/> and <see cref="IPanGlossStatsQuery"/>: a first run capturing a Baseline and
/// recording Assessments, a second run reusing that Baseline, cancellation recording nothing, an empty
/// Selection's refusal, and the reported progress stages.
/// </summary>
[SupportedOSPlatform("windows")]
[Collection(LcmCacheTestCollection.Name)]
public sealed class AssessCommandTests : IDisposable
{
    private static readonly SelectionRequest AllWordforms = new(true, [], [], false, null);

    private readonly PristineProjectFixture _pristine;
    private readonly string _managedRootsParent =
        Path.Combine(Path.GetTempPath(), "SIL.Motif.AssessCommandTests", Guid.NewGuid().ToString("N"));

    public AssessCommandTests(PristineProjectFixture pristine)
    {
        _pristine = pristine;
        Directory.CreateDirectory(_managedRootsParent);
    }

    public void Dispose()
    {
        try { Directory.Delete(_managedRootsParent, recursive: true); }
        catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public void FirstRunCapturesABaselineAndRecordsAnAssessmentPerCollectedKind()
    {
        using var seeded = NewSeededScratch();
        using var queue = NewQueue();

        var outcome = AssessCommand.Run(
            new AssessRequest(seeded.FwDataPath, AllWordforms), NewManagedRoot(), NewAssessor, NewStatsQuery,
            queue, onProgress: null, CancellationToken.None);

        Assert.True(outcome.Succeeded);
        var response = outcome.Value!;
        Assert.False(response.Baseline.ReusedExistingBytes);
        Assert.Equal(2, response.AssessmentIds.Count);

        var repository = OpenRepository(seeded.FwDataPath);
        Assert.Single(repository.ListBaselineAssessments(AssessmentKind.ParseTime.ToStoredKind()));
        Assert.Single(repository.ListBaselineAssessments(AssessmentKind.ObjectTiming.ToStoredKind()));
    }

    [Fact]
    public void TheAdmittedJobIsThreadedToTheAssessorAsAGovernor()
    {
        using var seeded = NewSeededScratch();
        using var queue = NewQueue();
        var assessor = new FakeAssessor("fake-assessor", CollectedKinds);

        var outcome = AssessCommand.Run(
            new AssessRequest(seeded.FwDataPath, AllWordforms), NewManagedRoot(), () => assessor, NewStatsQuery,
            queue, onProgress: null, CancellationToken.None);

        Assert.True(outcome.Succeeded);
        Assert.IsType<WindowsCpuJobGovernor>(assessor.LastGovernor);
    }

    [Fact]
    public void TheStatisticsQueryIsAlsoThreadedAnAdmittedJobAsAGovernor()
    {
        using var seeded = NewSeededScratch();
        using var queue = NewQueue();
        var cachePath = Path.Combine(_managedRootsParent, "fake-stats-cache-" + Guid.NewGuid().ToString("N") + ".bin");
        File.WriteAllText(cachePath, "fake per-object stats cache");
        var assessor = new FakeAssessor("fake-assessor", CollectedKinds, kind => kind == AssessmentKind.ObjectTiming
            ? new AssessmentRaw.FileCache(cachePath, "sha256:" + new string('0', 64))
            : new AssessmentRaw.WordMeasurements([]));
        var statsQuery = new FakeStatsQuery();

        var outcome = AssessCommand.Run(
            new AssessRequest(seeded.FwDataPath, AllWordforms), NewManagedRoot(), () => assessor,
            () => statsQuery, queue, onProgress: null, CancellationToken.None);

        Assert.True(outcome.Succeeded);
        Assert.IsType<WindowsCpuJobGovernor>(statsQuery.LastGovernor);
    }

    [Fact]
    public void SecondRunReusesTheExistingBaselineRatherThanRecapturing()
    {
        using var seeded = NewSeededScratch();
        var managedRoot = NewManagedRoot();

        using (var firstQueue = NewQueue())
        {
            var first = AssessCommand.Run(
                new AssessRequest(seeded.FwDataPath, AllWordforms), managedRoot, NewAssessor, NewStatsQuery,
                firstQueue, onProgress: null, CancellationToken.None);
            Assert.True(first.Succeeded);
            Assert.False(first.Value!.Baseline.ReusedExistingBytes);
        }

        using var secondQueue = NewQueue();
        var second = AssessCommand.Run(
            new AssessRequest(seeded.FwDataPath, AllWordforms), managedRoot, NewAssessor, NewStatsQuery,
            secondQueue, onProgress: null, CancellationToken.None);

        Assert.True(second.Succeeded);
        Assert.True(second.Value!.Baseline.ReusedExistingBytes);
    }

    [Fact]
    public void CancellationWhileTheAssessorIsRunningRecordsNoAssessments()
    {
        using var seeded = NewSeededScratch();
        using var queue = NewQueue();
        var cancellingAssessor = new FakeAssessor(
            "cancelling", CollectedKinds, _ => throw new OperationCanceledException());

        var outcome = AssessCommand.Run(
            new AssessRequest(seeded.FwDataPath, AllWordforms), NewManagedRoot(), () => cancellingAssessor, NewStatsQuery,
            queue, onProgress: null, CancellationToken.None);

        Assert.False(outcome.Succeeded);
        Assert.Equal("assessment.cancelled", outcome.Refusal!.Code);

        var repository = OpenRepository(seeded.FwDataPath);
        Assert.Empty(repository.ListBaselineAssessments(AssessmentKind.ParseTime.ToStoredKind()));
        Assert.Empty(repository.ListBaselineAssessments(AssessmentKind.ObjectTiming.ToStoredKind()));
    }

    [Fact]
    public void AnEmptySelectionIsRefusedAndRecordsNoAssessments()
    {
        var fwDataPath = _pristine.CopyProjectFile();
        using var queue = NewQueue();
        var noSources = new SelectionRequest(false, [], [], false, null);

        var outcome = AssessCommand.Run(
            new AssessRequest(fwDataPath, noSources), NewManagedRoot(), NewAssessor, NewStatsQuery,
            queue, onProgress: null, CancellationToken.None);

        Assert.False(outcome.Succeeded);
        Assert.Equal("selection.empty", outcome.Refusal!.Code);

        var repository = OpenRepository(fwDataPath);
        Assert.Empty(repository.ListBaselineAssessments(AssessmentKind.ParseTime.ToStoredKind()));
        Assert.Empty(repository.ListBaselineAssessments(AssessmentKind.ObjectTiming.ToStoredKind()));
    }

    [Fact]
    public void ProgressReportsOnlyTheCommandOwnedStagesWithNoPerWordTick()
    {
        using var seeded = NewSeededScratch();
        using var queue = NewQueue();
        var stages = new List<AssessmentProgress>();

        var outcome = AssessCommand.Run(
            new AssessRequest(seeded.FwDataPath, AllWordforms), NewManagedRoot(), NewAssessor, NewStatsQuery,
            queue, stages.Add, CancellationToken.None);

        Assert.True(outcome.Succeeded);
        Assert.Equal(
            new[]
            {
                AssessmentStage.Capturing,
                AssessmentStage.SelectingWords,
                AssessmentStage.Parsing,
                AssessmentStage.ReadingStatistics,
                AssessmentStage.Complete,
            },
            stages.Select(stage => stage.Stage));
        // One Parsing step names the whole Selection up front; PanGloss exposes no per-word tick to report.
        var parsing = Assert.Single(stages, stage => stage.Stage == AssessmentStage.Parsing);
        Assert.Equal(2, parsing.Total);
    }

    [Fact]
    public void AnUnavailableParserIsRefusedRatherThanEscapingAsAnException()
    {
        using var seeded = NewSeededScratch();
        using var queue = NewQueue();

        var outcome = AssessCommand.Run(
            new AssessRequest(seeded.FwDataPath, AllWordforms), NewManagedRoot(),
            () => throw new ParserUnavailableException("no pangloss here"), NewStatsQuery,
            queue, onProgress: null, CancellationToken.None);

        Assert.False(outcome.Succeeded);
        Assert.Equal("assess.parser-unavailable", outcome.Refusal!.Code);
        Assert.Equal(FailureReason.Refused, outcome.Refusal.Reason);
    }

    // A mistyped path must report the missing project, not the missing parser the eager build would hit first.
    [Fact]
    public void AMissingProjectIsRefusedBeforeTheParserIsEvenBuilt()
    {
        using var queue = NewQueue();

        var outcome = AssessCommand.Run(
            new AssessRequest(Path.Combine(_managedRootsParent, "absent.fwdata"), AllWordforms), NewManagedRoot(),
            () => throw new ParserUnavailableException("no pangloss here"),
            () => throw new ParserUnavailableException("no pangloss here"),
            queue, onProgress: null, CancellationToken.None);

        Assert.False(outcome.Succeeded);
        Assert.Equal("project.not-found", outcome.Refusal!.Code);
    }

    private static readonly IReadOnlyList<AssessmentKind> CollectedKinds =
        [AssessmentKind.ParseTime, AssessmentKind.ObjectTiming];

    private static FakeAssessor NewAssessor() => new("fake-assessor", CollectedKinds);

    private static FakeStatsQuery NewStatsQuery() => new();

    private static MachinePanGlossQueue NewQueue() =>
        new(new[]
        {
            "Local\\MotifAssessCommandTests-" + Guid.NewGuid().ToString("N") + "-0",
            "Local\\MotifAssessCommandTests-" + Guid.NewGuid().ToString("N") + "-1",
        });

    // SeedText's wordforms must be saved to disk for AssessCommand's own scratch load to see them.
    private SeededScratch NewSeededScratch()
    {
        var cache = _pristine.NewScratch();
        SeededProject.SeedText(cache, _pristine.Seed);
        new FwDataProjectLoader().Save(cache);
        return new SeededScratch(cache);
    }

    private sealed class SeededScratch(LcmCache cache) : IDisposable
    {
        public string FwDataPath => cache.ProjectId.Path;

        public void Dispose() => cache.Dispose();
    }

    private static IAssessmentRepository OpenRepository(string fwDataPath)
    {
        var project = new ProjectLocator(Path.GetFullPath(fwDataPath), Path.GetFileNameWithoutExtension(fwDataPath));
        var databasePath = ProjectDatabaseCatalog.DatabasePathFor(project);
        var database = MotifDatabase.OpenOwned(databasePath, project, MotifSchema.CurrentSchema, new Version(1, 0));
        return new AssessmentRepository(database);
    }

    private string NewManagedRoot()
    {
        var root = Path.Combine(_managedRootsParent, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    // A pure stand-in for PanGloss's `stats` command; no test here asserts on its output's content.
    private sealed class FakeStatsQuery : IPanGlossStatsQuery
    {
        public IParserProcessGovernor? LastGovernor { get; private set; }

        public Task<PanGlossStatsOutput> QueryAsync(string grammarPath, string cachePath,
            IReadOnlyList<string> forwardedArguments, CancellationToken cancellationToken,
            IParserProcessGovernor? governor = null)
        {
            LastGovernor = governor;
            return Task.FromResult(new PanGlossStatsOutput("fake stats", string.Empty));
        }
    }
}
