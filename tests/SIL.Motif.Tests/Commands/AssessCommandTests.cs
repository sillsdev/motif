using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading;
using SIL.LCModel;
using SIL.Motif.Commands.Assess;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.Store;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Tests.Parser;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary>
/// Pins <see cref="AssessCommand"/> over a real, file-backed seeded project, against a fake
/// <see cref="IAssessor"/> and a <see cref="FakeInvoker"/>: a first run capturing a Baseline and
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
        var invoker = NewInvoker();

        var outcome = AssessCommand.Run(
            new AssessRequest(seeded.FwDataPath, AllWordforms), NewManagedRoot(), NewAssessor(), invoker,
            onProgress: null, CancellationToken.None);

        Assert.True(outcome.Succeeded);
        var response = outcome.Value!;
        Assert.False(response.Baseline.ReusedExistingBytes);
        Assert.Equal(2, response.AssessmentIds.Count);

        var repository = OpenRepository(seeded.FwDataPath);
        Assert.Single(repository.ListBaselineAssessments(AssessmentKind.ParseTime.ToStoredKind()));
        Assert.Single(repository.ListBaselineAssessments(AssessmentKind.ObjectTiming.ToStoredKind()));
    }

    [RealParserFact]
    public void SupportedAssessmentRecordsRealTimingAndStatisticsWithOneInvocation()
    {
        using var cache = _pristine.NewScratch();
        RealParserProject.PrepareForParsing(cache, "m", "o", "t", "i", "f", "a", "b");
        var selection = new SelectionRequest(false, [], ["motifa", "motifb", "mofita"], false, null);

        var outcome = AssessCommand.Assess(new AssessRequest(cache.ProjectId.Path, selection), NewManagedRoot());

        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        var response = outcome.Value!;
        Assert.Equal(3, response.Words.Count);
        Assert.All(response.Words, word => Assert.False(word.IsIncomplete));
        Assert.Equal(2, response.Words.Count(word => word.Outcome == "analysed"));
        Assert.Equal(1, response.Words.Count(word => word.Outcome == "no-analysis"));
        Assert.StartsWith("3 searches completed; 0 incomplete", response.SummaryMarkdown);
        Assert.Contains("0/0 approved readings matched", response.CorrectnessStatus);
        Assert.Equal(3, response.Measurements.Count);
        Assert.Single(response.Measurements.Select(item => item.InvocationId).Distinct());
        var repository = OpenRepository(cache.ProjectId.Path);
        foreach (var measurement in response.Measurements)
        {
            var record = repository.Get(measurement.AssessmentId);
            Assert.Equal(measurement.InvocationId, record.Invocation!.InvocationId);
            Assert.Null(record.SemanticDigest);
        }
    }

    [Fact]
    public void PartialFindingsRemainIncompleteInTheResponseAndStoredWord()
    {
        using var seeded = NewSeededScratch();
        var assessor = new FakeAssessor("fake-assessor", CollectedKinds, kind =>
            kind == AssessmentKind.ParseTime
                ? new AssessmentRaw.Batch(new SIL.Motif.Host.Parser.BatchAnalysis(
                    [new(0, "motifa", 700, SIL.Motif.Host.Parser.WordOutcome.Capped, "partial-match"),
                     new(1, "motifb", 12, SIL.Motif.Host.Parser.WordOutcome.Analysed, "complete-match")],
                    1000, seeded.FwDataPath, []) { PerWordStepLimit = 200000 })
                : new AssessmentRaw.WordMeasurements([]));
        var outcome = AssessCommand.Run(new AssessRequest(seeded.FwDataPath,
            new SelectionRequest(false, [], ["motifa", "motifb"], false, null)), NewManagedRoot(),
            assessor, NewInvoker(), null, CancellationToken.None);

        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        var response = outcome.Value!;
        Assert.StartsWith("1 search completed; 1 incomplete", response.CompletionSummary);
        Assert.True(response.Words[0].IsIncomplete);
        Assert.StartsWith("INCOMPLETE", response.Words[0].CompletionStatus);
        Assert.Equal("partial-match", response.Words[0].RawSignature);
        Assert.False(response.Words[1].IsIncomplete);
        var timing = response.Measurements.Single(item => item.Kind == "ParseTime");
        var stored = OpenRepository(seeded.FwDataPath).Get(timing.AssessmentId).Words!;
        Assert.Equal("capped", stored[0].Outcome);
        Assert.Equal("partial-match", stored[0].RawSignature);
        Assert.Empty(stored[0].Analyses);
    }

    [Fact]
    public void SecondRunReusesTheExistingBaselineRatherThanRecapturing()
    {
        using var seeded = NewSeededScratch();
        var managedRoot = NewManagedRoot();
        var invoker = NewInvoker();

        var first = AssessCommand.Run(
            new AssessRequest(seeded.FwDataPath, AllWordforms), managedRoot, NewAssessor(), invoker,
            onProgress: null, CancellationToken.None);
        Assert.True(first.Succeeded);
        Assert.False(first.Value!.Baseline.ReusedExistingBytes);

        var second = AssessCommand.Run(
            new AssessRequest(seeded.FwDataPath, AllWordforms), managedRoot, NewAssessor(), invoker,
            onProgress: null, CancellationToken.None);

        Assert.True(second.Succeeded);
        Assert.True(second.Value!.Baseline.ReusedExistingBytes);
    }

    [Fact]
    public void CancellationWhileTheAssessorIsRunningRecordsNoAssessments()
    {
        using var seeded = NewSeededScratch();
        var cancellingAssessor = new FakeAssessor(
            "cancelling", CollectedKinds, _ => throw new OperationCanceledException());

        var outcome = AssessCommand.Run(
            new AssessRequest(seeded.FwDataPath, AllWordforms), NewManagedRoot(), cancellingAssessor, NewInvoker(),
            onProgress: null, CancellationToken.None);

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
        var noSources = new SelectionRequest(false, [], [], false, null);

        var outcome = AssessCommand.Run(
            new AssessRequest(fwDataPath, noSources), NewManagedRoot(), NewAssessor(), NewInvoker(),
            onProgress: null, CancellationToken.None);

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
        var stages = new List<AssessmentProgress>();

        var outcome = AssessCommand.Run(
            new AssessRequest(seeded.FwDataPath, AllWordforms), NewManagedRoot(), NewAssessor(), NewInvoker(),
            stages.Add, CancellationToken.None);

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

    // The parser existing is not the parser working: a subcommand it lacks must refuse, not kill the app.
    [Fact]
    public void AParserThatFailsOnceRunningIsRefusedRatherThanEscapingAsAnException()
    {
        using var seeded = NewSeededScratch();
        var failingAssessor = new FakeAssessor(
            "unavailable-at-run", CollectedKinds,
            _ => throw new AssessorUnavailableException("unavailable-at-run", "pangloss assess exited 1"));

        var outcome = AssessCommand.Run(
            new AssessRequest(seeded.FwDataPath, AllWordforms), NewManagedRoot(),
            failingAssessor, NewInvoker(), onProgress: null, CancellationToken.None);

        Assert.False(outcome.Succeeded);
        Assert.Equal("assess.parser-unavailable", outcome.Refusal!.Code);
        Assert.Contains("pangloss assess exited 1", outcome.Refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnAssessorThatCouldNotRunItsParser_IsRefusedAsParserUnavailable_AndRecordsNothing()
    {
        using var seeded = NewSeededScratch();
        var unavailableAssessor = new FakeAssessor("fake-assessor", CollectedKinds,
            _ => throw new AssessorUnavailableException("fake-assessor", "no pangloss here"));

        var outcome = AssessCommand.Run(
            new AssessRequest(seeded.FwDataPath, AllWordforms), NewManagedRoot(), unavailableAssessor, NewInvoker(),
            onProgress: null, CancellationToken.None);

        Assert.False(outcome.Succeeded);
        Assert.Equal("assess.parser-unavailable", outcome.Refusal!.Code);
        Assert.Contains("no pangloss here", outcome.Refusal.Message, StringComparison.Ordinal);

        var repository = OpenRepository(seeded.FwDataPath);
        Assert.Empty(repository.ListBaselineAssessments(AssessmentKind.ParseTime.ToStoredKind()));
        Assert.Empty(repository.ListBaselineAssessments(AssessmentKind.ObjectTiming.ToStoredKind()));
    }

    // A mistyped path must report the missing project, not any collaborator this run would otherwise use.
    [Fact]
    public void AMissingProjectIsRefusedBeforeTheParserIsEvenBuilt()
    {
        var mustNotRun = new LazyPanGlossAssessor(
            () => throw new InvalidOperationException("A refused request must never build the Assessor."));
        var unreachableInvoker = new FakeInvoker
        {
            Respond = _ => throw new InvalidOperationException("A refused request must never reach the invoker."),
        };

        var outcome = AssessCommand.Run(
            new AssessRequest(Path.Combine(_managedRootsParent, "absent.fwdata"), AllWordforms), NewManagedRoot(),
            mustNotRun, unreachableInvoker, onProgress: null, CancellationToken.None);

        Assert.False(outcome.Succeeded);
        Assert.Equal("project.not-found", outcome.Refusal!.Code);
    }

    [Theory]
    [InlineData("completed", null)]
    [InlineData("unavailable", "assess.parser-unavailable")]
    [InlineData("refused", "assess.parser-unavailable")]
    [InlineData("timed-out", "assess.parser-unavailable")]
    [InlineData("cancelled", "assessment.cancelled")]
    public void StatisticsSummaryMapsTheInvocationOutcome(string result, string? refusalCode)
    {
        using var seeded = NewSeededScratch();
        var cachePath = Path.Combine(_managedRootsParent, "statistics.bin");
        File.WriteAllText(cachePath, "statistics artifact");
        var assessor = new FakeAssessor("fake-assessor", CollectedKinds, kind =>
            kind == AssessmentKind.ObjectTiming
                ? new AssessmentRaw.FileCache(cachePath, BatchInvocationEvidence.DigestFile(cachePath))
                : new AssessmentRaw.WordMeasurements([]))
        {
            CaptureEvidence = (scope, candidate) => FakeAssessmentEvidence.Capture(_managedRootsParent, scope, candidate),
        };
        var invoker = new FakeInvoker
        {
            Respond = _ => result switch
            {
                "completed" => new PanGlossOutcome.Completed("statistics rows", string.Empty, TimeSpan.Zero),
                "unavailable" => new PanGlossOutcome.Unavailable("parser absent"),
                "refused" => new PanGlossOutcome.Refused(2, "cache refused", string.Empty, "cache refused"),
                "timed-out" => new PanGlossOutcome.TimedOut(TimeSpan.FromMinutes(10), "query timed out"),
                "cancelled" => new PanGlossOutcome.Cancelled(),
                _ => throw new ArgumentOutOfRangeException(nameof(result)),
            },
        };

        var outcome = AssessCommand.Run(
            new AssessRequest(seeded.FwDataPath, AllWordforms), NewManagedRoot(), assessor, invoker,
            onProgress: null, CancellationToken.None);

        var request = Assert.IsType<PanGlossRequest.Stats>(Assert.Single(invoker.Requests).Request);
        Assert.NotEqual(cachePath, request.CachePath);
        Assert.False(File.Exists(request.CachePath));
        Assert.Empty(request.ForwardedArguments);
        Assert.Equal(refusalCode is null, outcome.Succeeded);
        if (refusalCode is not null) Assert.Equal(refusalCode, outcome.Refusal!.Code);
        else Assert.Contains("statistics rows", outcome.Value!.SummaryMarkdown, StringComparison.Ordinal);
        var repository = OpenRepository(seeded.FwDataPath);
        foreach (var kind in CollectedKinds)
        {
            var records = repository.ListBaselineAssessments(kind.ToStoredKind());
            if (refusalCode is null) Assert.Single(records);
            else Assert.Empty(records);
        }
    }

    [Fact]
    public void ParserDiscoveryFailureIsRefusedThroughTheLazyAssessor()
    {
        using var seeded = NewSeededScratch();
        var assessor = new LazyPanGlossAssessor(() =>
            throw new SIL.Motif.Host.Parser.ParserUnavailableException("parser absent"));

        var outcome = AssessCommand.Run(
            new AssessRequest(seeded.FwDataPath, AllWordforms), NewManagedRoot(), assessor, NewInvoker(),
            onProgress: null, CancellationToken.None);

        Assert.Equal("assess.parser-unavailable", outcome.Refusal!.Code);
        Assert.Contains("parser absent", outcome.Refusal.Message, StringComparison.Ordinal);
        Assert.Empty(OpenRepository(seeded.FwDataPath).ListBaselineAssessments(AssessmentKind.ParseTime.ToStoredKind()));
    }

    private static readonly IReadOnlyList<AssessmentKind> CollectedKinds =
        [AssessmentKind.ParseTime, AssessmentKind.ObjectTiming];

    private static FakeAssessor NewAssessor() => new("fake-assessor", CollectedKinds);

    // Answers every stats request with fixed rows; no test here asserts on the summary's content.
    private static FakeInvoker NewInvoker() => new()
    {
        Respond = _ => new PanGlossOutcome.Completed("fake stats", string.Empty, TimeSpan.Zero),
    };

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
}
