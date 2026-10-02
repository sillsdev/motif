using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands.Assess;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Handoff;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Canonicalization;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.Corpus;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.Store;
using SIL.Motif.Host.Parser;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Tests.App.Walkthrough;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Handoff;

/// <summary>
/// Drives <see cref="HandoffCommand"/> over a real, file-backed seeded project against a fake
/// <see cref="IAssessor"/> and the real <see cref="PanGlossInvoker"/> pointed at the FakePanGloss
/// executable: the file listing, atomicity on an existing destination, cancellation and
/// PanGloss-failure cleanup, <c>--no-assess</c>, and duplicate Text titles.
/// </summary>
[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group1)]
public sealed class HandoffWriterTests : IDisposable
{
    private static readonly SelectionRequest AllWordformsAllTexts = new(true, [], [], false, null);

    private static readonly IReadOnlyList<AssessmentKind> CollectedKinds =
        [AssessmentKind.ParseTime, AssessmentKind.ObjectTiming];

    private readonly PristineProjectFixture _pristine;
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "SIL.Motif.HandoffWriterTests", Guid.NewGuid().ToString("N"));

    public HandoffWriterTests(PristineProjectFixture pristine)
    {
        _pristine = pristine;
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public void AssessedHandoffRequiresARetainedInvocation()
    {
        using var seeded = NewSeededScratch();
        using var invoker = NewInvoker();
        var destination = Path.Combine(_root, "handoff-cancelled");
        var cancellingAssessor = new FakeAssessor(
            "cancelling", CollectedKinds, _ => throw new OperationCanceledException());

        var outcome = HandoffCommand.Run(
            new HandoffRequest(seeded.FwDataPath, destination, AllWordformsAllTexts, true),
            NewManagedRoot(), cancellingAssessor, invoker, onProgress: null, CancellationToken.None);

        Assert.False(outcome.Succeeded);
        Assert.Equal("handoff.invocation-required", outcome.Refusal!.Code);
        Assert.Equal(FailureReason.InvalidArgument, outcome.Refusal.Reason);
        Assert.False(Directory.Exists(destination));
    }

    [Fact]
    public void SelectedWordsAreAssessedAfreshWithoutARetainedInvocation()
    {
        using var seeded = NewSeededScratch();
        using var invoker = NewInvoker();
        var destination = Path.Combine(_root, "handoff-selected-words");
        var selectedWords = new SelectionRequest(false, [], ["motifa"], false, null);

        var outcome = HandoffCommand.Run(
            new HandoffRequest(seeded.FwDataPath, destination, selectedWords, true),
            NewManagedRoot(), NewAssessor(), invoker, onProgress: null, CancellationToken.None);

        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        Assert.Equal(["motifa"], outcome.Value!.Selection.Words);
        Assert.NotEmpty(outcome.Value.AssessmentIds);
        Assert.True(File.Exists(Path.Combine(destination, "grammar.json")));
    }

    [Fact]
    public void SingleWordAssessedHandoffWritesItsTraceAndSummaryBesideBatchEvidence()
    {
        using var seeded = NewSeededScratch();
        using var invoker = NewInvoker();
        var destination = Path.Combine(_root, "handoff-one-word-trace");
        var selectedWord = "motifa";

        var outcome = HandoffCommand.Run(
            new HandoffRequest(seeded.FwDataPath, destination,
                new SelectionRequest(false, [], [selectedWord], false, null), true),
            NewManagedRoot(), NewAssessor(), invoker, onProgress: null, CancellationToken.None);

        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        var tracePath = Path.Combine(destination, "traces", selectedWord + ".trace.json");
        Assert.True(File.Exists(tracePath));
        Assert.Contains("traces/" + selectedWord + ".trace.json", outcome.Value!.Files);

        using var assessment = JsonDocument.Parse(File.ReadAllText(Path.Combine(destination, "assessment.json")));
        var record = Assert.Single(assessment.RootElement.EnumerateArray());
        Assert.Equal(selectedWord, record.GetProperty("word").GetString());
        var trace = record.GetProperty("trace");
        Assert.Equal("traces/" + selectedWord + ".trace.json", trace.GetProperty("file").GetString());
        var summary = trace.GetProperty("summary");
        Assert.Equal("unknown", summary.GetProperty("completion").GetString());
        Assert.Equal(42, summary.GetProperty("parserSteps").GetInt32());
        using var rawTrace = JsonDocument.Parse(File.ReadAllText(tracePath));
        Assert.Equal(selectedWord, rawTrace.RootElement.GetProperty("word").GetString());
    }

    [Fact]
    public void SingleWordHandoffNeverWritesZeroForAnUnmeasuredHostElapsedTime()
    {
        using var seeded = NewSeededScratch();
        using var inner = NewInvoker();
        var invoker = new DelayingTraceInvoker(inner, TimeSpan.FromMilliseconds(75));
        var destination = Path.Combine(_root, "handoff-trace-elapsed");

        var outcome = HandoffCommand.Run(
            new HandoffRequest(seeded.FwDataPath, destination,
                new SelectionRequest(false, [], ["motifa"], false, null), true),
            NewManagedRoot(), NewAssessor(), invoker, onProgress: null, CancellationToken.None);

        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        Assert.Equal(1, invoker.TraceCalls);
        using var trace = JsonDocument.Parse(File.ReadAllText(Path.Combine(destination, "traces", "motifa.trace.json")));
        var hostCapture = trace.RootElement.GetProperty("hostCapture");
        var elapsed = hostCapture.GetProperty("wallElapsedMs");
        Assert.True(elapsed.ValueKind == JsonValueKind.Null || elapsed.GetInt64() > 0,
            "Host elapsed time must be measured or unavailable, never the unmeasured default zero.");
    }

    [Fact]
    public void SelectedTraceHandoffKeepsItsDiagnosticAndCapturedBaselineAfterRefresh()
    {
        const string selectedWord = "motifa";
        using var seeded = NewSeededScratch();
        var managedRoot = NewManagedRoot();
        var firstCapture = BaselineCaptureCommand.Capture(
            new BaselineCaptureRequest(seeded.FwDataPath), managedRoot);
        Assert.True(firstCapture.Succeeded, firstCapture.Refusal?.Message);

        using var traceInvoker = NewInvoker();
        var traced = WordTraceQuery.Query(new WordTraceRequest(seeded.FwDataPath, selectedWord),
            new PanGlossTracer(traceInvoker), CancellationToken.None);
        Assert.True(traced.Succeeded, traced.Refusal?.Message);
        var selectedDocument = JsonNode.Parse(traced.Value!.DiagnosticJson)!.AsObject();
        selectedDocument["producerExtension"] = new JsonObject { ["preserve"] = true };
        var search = selectedDocument["search"]!.AsObject();
        search["completed"] = false;
        search["capped"] = true;
        var selectedTrace = WordTraceQuery.LoadDiagnostic(selectedDocument.ToJsonString());
        Assert.True(selectedTrace.Succeeded, selectedTrace.Refusal?.Message);
        Assert.False(selectedTrace.Value!.Complete);

        AddPlainText(seeded.Cache, "after first capture");
        new FwDataProjectLoader().Save(seeded.Cache);
        var secondCapture = BaselineCaptureCommand.Capture(
            new BaselineCaptureRequest(seeded.FwDataPath), managedRoot);
        Assert.True(secondCapture.Succeeded, secondCapture.Refusal?.Message);
        Assert.NotEqual(firstCapture.Value!.Token.BundleDigest, secondCapture.Value!.Token.BundleDigest);

        string? importedDigest = null;
        var traceCalls = 0;
        using var inner = NewInvoker();
        var invoker = new InspectingInvoker(inner, request =>
        {
            if (request is PanGlossRequest.Trace) traceCalls++;
            if (request is PanGlossRequest.Import import)
                importedDigest = BatchInvocationEvidence.DigestFile(import.FwDataPath);
        });
        var destination = Path.Combine(_root, "handoff-selected-trace-after-refresh");
        var request = new HandoffRequest(seeded.FwDataPath, destination,
            new SelectionRequest(false, [], [selectedWord], false, null), false)
        {
            SelectedTrace = selectedTrace.Value,
        };

        var outcome = HandoffCommand.Run(request, managedRoot, NewAssessor(), invoker,
            onProgress: null, CancellationToken.None);

        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        Assert.Equal(firstCapture.Value.Token, outcome.Value!.Baseline.Token);
        Assert.Equal(BatchInvocationEvidence.DigestFile(firstCapture.Value.FwDataPath), importedDigest);
        Assert.NotEqual(BatchInvocationEvidence.DigestFile(secondCapture.Value!.FwDataPath), importedDigest);
        Assert.Equal(0, traceCalls);
        var tracePath = Path.Combine(destination, HandoffWriter.TraceRelativePath(selectedWord)
            .Replace('/', Path.DirectorySeparatorChar));
        Assert.Equal(selectedTrace.Value.DiagnosticJson, File.ReadAllText(tracePath));
        using var exported = JsonDocument.Parse(File.ReadAllText(tracePath));
        Assert.True(exported.RootElement.GetProperty("producerExtension").GetProperty("preserve").GetBoolean());
        Assert.False(exported.RootElement.GetProperty("search").GetProperty("completed").GetBoolean());
        Assert.Equal(selectedTrace.Value.HostCapture!.WallElapsedMs,
            exported.RootElement.GetProperty("hostCapture").GetProperty("wallElapsedMs").GetInt64());
        Assert.False(File.Exists(Path.Combine(destination, "assessment.json")));
    }

    [Fact]
    public void WarningScopedHandoffCarriesTheRawParserMessageAndMatchKind()
    {
        using var seeded = NewSeededScratch();
        using var invoker = NewInvoker();
        var destination = Path.Combine(_root, "handoff-warning-scope");
        var selection = new SelectionRequest(false, [], ["motifa"], false, null);
        var scope = new WarningHandoffScope(
            "PanGloss warning text.\nA second parser line.", "parser.warning", WarningDisplayState.SpellingCandidates,
            HasUnfollowedConnections: true);

        var outcome = HandoffCommand.Run(
            new HandoffRequest(seeded.FwDataPath, destination, selection, true, WarningScope: scope),
            NewManagedRoot(), NewAssessor(), invoker, onProgress: null, CancellationToken.None);

        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        var markdown = File.ReadAllText(Path.Combine(destination, "handoff.md"));
        Assert.Contains("## PanGloss warning", markdown, StringComparison.Ordinal);
        Assert.Contains("Code: `parser.warning`.", markdown, StringComparison.Ordinal);
        Assert.Contains("Spelling matches only; use is not confirmed", markdown, StringComparison.Ordinal);
        Assert.Contains("Some named connections could not be followed", markdown, StringComparison.Ordinal);
        Assert.Contains("> PanGloss warning text.\n> A second parser line.", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("What to do in FieldWorks", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void EmptyWarningScopeCanBeHandedOffWithoutAnAssessmentAndKeepsItsUnfollowedLimit()
    {
        using var seeded = NewSeededScratch();
        using var invoker = NewInvoker();
        var destination = Path.Combine(_root, "handoff-warning-empty");
        var scope = new WarningHandoffScope(
            "PanGloss named a template whose word route is unavailable.", "grammar.template.unfollowed",
            WarningDisplayState.NoFollowedRouteMatch, HasUnfollowedConnections: true);

        var outcome = HandoffCommand.Run(
            new HandoffRequest(seeded.FwDataPath, destination, new SelectionRequest(false, [], [], false, null),
                Assess: false, WarningScope: scope),
            NewManagedRoot(), NewAssessor(), invoker, onProgress: null, CancellationToken.None);

        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        Assert.Empty(outcome.Value!.AssessmentIds);
        Assert.Empty(outcome.Value.Selection.Words);
        var markdown = File.ReadAllText(Path.Combine(destination, "handoff.md"));
        Assert.Contains("No words matched through routes Motif could follow", markdown, StringComparison.Ordinal);
        Assert.Contains("other named connections remain unchecked", markdown, StringComparison.Ordinal);
        Assert.Contains("no word conclusion is made about those routes", markdown, StringComparison.Ordinal);
        Assert.Contains("> PanGloss named a template whose word route is unavailable.", markdown,
            StringComparison.Ordinal);
        Assert.DoesNotContain("No words in this Selection use the named item", markdown, StringComparison.Ordinal);
    }

    // Complete must arrive once, at the end: the nested Assessment reports its own part way through.
    [Fact]
    public void ProgressReachesCompleteOnlyOnceTheFolderIsActuallyWritten()
    {
        using var seeded = NewSeededScratch();
        var managedRoot = NewManagedRoot();
        using var assessmentInvoker = NewInvoker();
        var assessment = RunAssessment(seeded, managedRoot, NewAssessor(), assessmentInvoker);
        using var invoker = NewInvoker();
        var destination = Path.Combine(_root, "handoff-progress");
        var reported = new List<AssessmentProgress>();

        var outcome = HandoffCommand.Run(
            AssessedRequest(seeded, destination, assessment.InvocationId),
            managedRoot, NewAssessor(), invoker, reported.Add, CancellationToken.None);

        Assert.True(outcome.Succeeded);
        Assert.Contains(AssessmentStage.ImportingGrammar, reported.Select(step => step.Stage));
        var complete = Assert.Single(reported, step => step.Stage == AssessmentStage.Complete);
        Assert.Same(reported[^1], complete);
        Assert.Equal("Handoff complete.", complete.Message);
    }

    [Fact]
    public void HandoffExportsTheSelectedRetainedAssessmentWithoutCreatingAnotherOne()
    {
        using var seeded = NewSeededScratch();
        var managedRoot = NewManagedRoot();
        var selectionA = new SelectionRequest(false, [], ["motifa"], false, null);
        var selectionB = new SelectionRequest(false, [], ["motifb"], false, null);
        using var assessorInvokerA = NewInvoker();
        var assessmentA = RunAssessment(seeded, managedRoot, NewAssessor(), assessorInvokerA, selectionA);
        using var assessorInvokerB = NewInvoker();
        _ = RunAssessment(seeded, managedRoot, NewAssessor(), assessorInvokerB, selectionB);

        var before = WalkthroughStoreAssertions.ListInvocations(seeded.FwDataPath);
        var retainedA = Assert.Single(before, invocation => invocation.InvocationId == assessmentA.InvocationId);
        AddPlainText(seeded.Cache, "changed after assessment");
        new FwDataProjectLoader().Save(seeded.Cache);

        string? importedDigest = null;
        using var realInvoker = NewInvoker();
        var invoker = new InspectingInvoker(realInvoker, request =>
        {
            if (request is PanGlossRequest.Import import)
                importedDigest = BatchInvocationEvidence.DigestFile(import.FwDataPath);
        });
        var destination = Path.Combine(_root, "handoff-retained-a");
        var outcome = HandoffCommand.Run(
            AssessedRequest(seeded, destination, assessmentA.InvocationId),
            managedRoot, NewAssessor(), invoker, null, CancellationToken.None);

        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        Assert.Equal(assessmentA.InvocationId, outcome.Value!.InvocationId);
        Assert.Equal(assessmentA.AssessmentIds.OrderBy(id => id), outcome.Value.AssessmentIds.OrderBy(id => id));
        Assert.Equal(assessmentA.Baseline.Token, outcome.Value.Baseline.Token);
        Assert.Equal(retainedA.Assessments[0].Invocation!.SourceBytesSha256, importedDigest);
        Assert.Contains("motifa", outcome.Value.Selection.Words);
        Assert.DoesNotContain("motifb", outcome.Value.Selection.Words);
        Assert.Equal(before.Count, WalkthroughStoreAssertions.ListInvocations(seeded.FwDataPath).Count);
    }

    [Fact]
    public void UnknownRetainedInvocationRefusesBeforeTouchingTheDestination()
    {
        using var seeded = NewSeededScratch();
        var destination = Path.Combine(_root, "handoff-unknown-invocation");

        var outcome = HandoffCommand.Run(
            AssessedRequest(seeded, destination, "missing-invocation"),
            NewManagedRoot(), NewAssessor(), new FakeInvoker(), null, CancellationToken.None);

        Assert.False(outcome.Succeeded);
        Assert.Equal("handoff.invocation-not-found", outcome.Refusal!.Code);
        Assert.Equal(FailureReason.NotFound, outcome.Refusal.Reason);
        Assert.Contains("missing-invocation", outcome.Refusal.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(destination));
    }

    [Fact]
    public void AnInvocationFromAnotherProjectDatabaseIsNotFound()
    {
        using var selectedProject = NewSeededScratch();
        using var otherProject = NewSeededScratch();
        var managedRoot = NewManagedRoot();
        using var assessmentInvoker = NewInvoker();
        var otherAssessment = RunAssessment(otherProject, managedRoot, NewAssessor(), assessmentInvoker);
        var destination = Path.Combine(_root, "handoff-mismatched-invocation");

        var outcome = HandoffCommand.Run(
            AssessedRequest(selectedProject, destination, otherAssessment.InvocationId),
            managedRoot, NewAssessor(), new FakeInvoker(), null, CancellationToken.None);

        Assert.False(outcome.Succeeded);
        Assert.Equal("handoff.invocation-not-found", outcome.Refusal!.Code);
        Assert.Equal(FailureReason.NotFound, outcome.Refusal.Reason);
        Assert.False(Directory.Exists(destination));
    }

    [Fact]
    public void AForeignRetainedInvocationInTheSelectedDatabaseIsRefusedAsAMismatch()
    {
        using var selectedProject = NewSeededScratch();
        var locator = new ProjectLocator(
            Path.GetFullPath(selectedProject.FwDataPath),
            Path.GetFileNameWithoutExtension(selectedProject.FwDataPath));
        using (var database = MotifDatabase.OpenOwned(
            ProjectDatabaseCatalog.DatabasePathFor(locator), locator,
            MotifSchema.CurrentSchema, new Version(1, 0)))
        {
            new RetainedInvocationRepository(database).Record(
                ForeignRetained("foreign-invocation"),
                [ForeignAssessment("foreign-assessment", "foreign-invocation")]);
        }

        var destination = Path.Combine(_root, "handoff-mismatched-invocation");
        var outcome = HandoffCommand.Run(
            AssessedRequest(selectedProject, destination, "foreign-invocation"),
            NewManagedRoot(), NewAssessor(), new FakeInvoker(), null, CancellationToken.None);

        Assert.False(outcome.Succeeded);
        Assert.Equal("handoff.invocation-mismatch", outcome.Refusal!.Code);
        Assert.Equal(FailureReason.Refused, outcome.Refusal.Reason);
        Assert.False(Directory.Exists(destination));
    }

    [Fact]
    public void MissingRetainedTextRefusesBeforeWritingTheDestination()
    {
        using var seeded = NewSeededScratch();
        using var assessmentInvoker = NewInvoker();
        var selection = new SelectionRequest(true, [seeded.TextId], [], false, null);
        var assessment = RunAssessment(seeded, NewManagedRoot(), NewAssessor(), assessmentInvoker, selection);
        var retained = Assert.Single(WalkthroughStoreAssertions.ListInvocations(seeded.FwDataPath));
        var descriptor = retained.Selection with { TextIds = [Guid.NewGuid()] };
        descriptor = descriptor with { DescriptorSha256 = SelectionDescriptorDigest.Compute(descriptor) };
        var locator = new ProjectLocator(Path.GetFullPath(seeded.FwDataPath),
            Path.GetFileNameWithoutExtension(seeded.FwDataPath));
        using (var database = MotifDatabase.OpenOwned(
            ProjectDatabaseCatalog.DatabasePathFor(locator), locator,
            MotifSchema.CurrentSchema, new Version(1, 0)))
        using (var connection = database.OpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "UPDATE RetainedInvocations SET SelectionDescriptorJson = $json, " +
                "SelectionDescriptorSha256 = $digest WHERE InvocationId = $invocation;";
            command.Parameters.AddWithValue("$json", System.Text.Json.JsonSerializer.Serialize(descriptor));
            command.Parameters.AddWithValue("$digest", descriptor.DescriptorSha256);
            command.Parameters.AddWithValue("$invocation", assessment.InvocationId);
            command.ExecuteNonQuery();
        }

        var destination = Path.Combine(_root, "handoff-missing-retained-text");
        var outcome = HandoffCommand.Run(
            AssessedRequest(seeded, destination, assessment.InvocationId), NewManagedRoot(),
            NewAssessor(), new FakeInvoker(), null, CancellationToken.None);

        Assert.False(outcome.Succeeded);
        Assert.Equal("handoff.text-not-found", outcome.Refusal!.Code);
        Assert.Contains(descriptor.TextIds[0].ToString("D"), outcome.Refusal.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(destination));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AssessedHandoffImportsItsRetainedSourceAndRefusesTamperedEvidence(bool tamperRetained)
    {
        using var seeded = NewSeededScratch();
        using var realInvoker = NewInvoker();
        var destination = Path.Combine(_root, "handoff-source");
        var cachePath = Path.Combine(_root, "retained-statistics.sqlite");
        BatchInvocationEvidence? evidence = null;
        string? baselinePath = null;
        string? importedDigest = null;
        string? importedPath = null;
        var assessor = new FakeAssessor("fake-assessor", CollectedKinds,
            rawForScope: (scope, candidate, kind) =>
            {
                if (kind == AssessmentKind.ObjectTiming)
                {
                    WriteStatsCache(cachePath, scope.Words);
                    return new AssessmentRaw.FileCache(cachePath, BatchInvocationEvidence.DigestFile(cachePath));
                }
                if (kind == AssessmentKind.ParseTime)
                {
                    var projectPath = Directory.GetFiles(candidate, "*.fwdata", SearchOption.AllDirectories).Single();
                    var words = scope.Words.Select((word, index) => new WordAnalysis(
                        index, word, 1, WordOutcome.NoAnalysis, "none")).ToArray();
                    return new AssessmentRaw.Batch(new BatchAnalysis(words,
                        (int)scope.PerWordLimit!.Value.TotalMilliseconds, projectPath, [])
                    { PerWordStepLimit = scope.PerWordStepLimit });
                }
                return new AssessmentRaw.WordMeasurements([]);
            })
        {
            CaptureEvidence = (scope, candidate) =>
            {
                baselinePath = Directory.GetFiles(candidate, "*.fwdata", SearchOption.AllDirectories).Single();
                return evidence = FakeAssessmentEvidence.Capture(_root, scope, candidate);
            }
        };
        var managedRoot = NewManagedRoot();
        using var assessmentInvoker = NewInvoker();
        var assessment = RunAssessment(seeded, managedRoot, assessor, assessmentInvoker);

        // Evidence is verified up front; the baseline is read only once, early, so a later tamper cannot matter.
        if (tamperRetained) File.WriteAllText(evidence!.SourcePath, "unreadable changed project bytes");
        var baselineTampered = false;
        var invoker = new InspectingInvoker(realInvoker, request =>
        {
            if (!tamperRetained && !baselineTampered && request is PanGlossRequest.Import)
            {
                File.WriteAllText(baselinePath!, "unreadable changed project bytes");
                baselineTampered = true;
            }
            if (request is PanGlossRequest.Import import)
            {
                importedPath = import.FwDataPath;
                importedDigest = BatchInvocationEvidence.DigestFile(import.FwDataPath);
            }
        });

        var outcome = HandoffCommand.Run(
            AssessedRequest(seeded, destination, assessment.InvocationId),
            managedRoot, NewAssessor(), invoker, null, CancellationToken.None);

        Assert.Equal(!tamperRetained, outcome.Succeeded);
        if (tamperRetained)
        {
            Assert.Equal("handoff.source-unavailable", outcome.Refusal!.Code);
            Assert.Null(importedPath);
            Assert.False(Directory.Exists(destination));
        }
        else
        {
            Assert.Equal(evidence!.SourceBytesSha256, importedDigest);
            Assert.NotEqual(baselinePath, importedPath);
            Assert.NotEqual(evidence.SourcePath, importedPath);
            Assert.False(File.Exists(importedPath));
            Assert.True(File.Exists(evidence.SourcePath));
        }
    }

    private sealed class InspectingInvoker(IPanGlossInvoker inner, Action<PanGlossRequest> inspect) : IPanGlossInvoker
    {
        public Task<PanGlossOutcome> RunAsync(PanGlossRequest request, string label,
            CancellationToken cancellationToken, TimeSpan? wallClockCap = null)
        {
            inspect(request);
            return inner.RunAsync(request, label, cancellationToken, wallClockCap);
        }
    }

    private sealed class DelayingTraceInvoker(IPanGlossInvoker inner, TimeSpan delay) : IPanGlossInvoker
    {
        public int TraceCalls { get; private set; }

        public async Task<PanGlossOutcome> RunAsync(PanGlossRequest request, string label,
            CancellationToken cancellationToken, TimeSpan? wallClockCap = null)
        {
            if (request is PanGlossRequest.Trace)
            {
                TraceCalls++;
                await Task.Delay(delay, cancellationToken);
            }
            return await inner.RunAsync(request, label, cancellationToken, wallClockCap);
        }
    }

    [Fact]
    public void AnEndToEndHandoffWritesExactlyFiveFilesAndEveryJsonFileValidates()
    {
        using var seeded = NewSeededScratch();
        var managedRoot = NewManagedRoot();
        using var assessmentInvoker = NewInvoker();
        var assessment = RunAssessment(seeded, managedRoot, NewAssessor(), assessmentInvoker);
        using var invoker = NewInvoker();
        var destination = Path.Combine(_root, "handoff-full");

        var outcome = HandoffCommand.Run(
            AssessedRequest(seeded, destination, assessment.InvocationId),
            managedRoot, NewAssessor(), invoker, onProgress: null, CancellationToken.None);

        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        var response = outcome.Value!;
        Assert.Equal(destination, response.OutputDirectory);
        Assert.Equal(2, response.AssessmentIds.Count);
        Assert.True(response.Selection.Words.Count > 0);

        // No sixth file and no subfolder: exactly the five files ADR 0045 names.
        Assert.Equal(
            new[]
            {
                "assessment.json", "grammar.json", "handoff.md",
                "parse_grammar_texts_assessment.py", "texts.json",
            },
            Directory.GetFiles(destination).Select(Path.GetFileName).Order(StringComparer.Ordinal));

        AssertFile(destination, "grammar.json");
        AssertFile(destination, "texts.json");
        AssertFile(destination, "assessment.json");
        AssertFile(destination, "parse_grammar_texts_assessment.py");
        AssertFile(destination, "handoff.md");

        using (JsonDocument.Parse(File.ReadAllText(Path.Combine(destination, "grammar.json")))) { }
        using (JsonDocument.Parse(File.ReadAllText(Path.Combine(destination, "texts.json")))) { }
        using (JsonDocument.Parse(File.ReadAllText(Path.Combine(destination, "assessment.json")))) { }

        Assert.Contains("grammar.json", response.Files);
        Assert.Contains("texts.json", response.Files);
        Assert.Contains("assessment.json", response.Files);
        Assert.Contains("parse_grammar_texts_assessment.py", response.Files);
        Assert.Contains("handoff.md", response.Files);
        Assert.False(string.IsNullOrWhiteSpace(response.PastedHeader));
        Assert.False(string.IsNullOrWhiteSpace(response.HandoffMarkdown));
    }

    // Pins one-record-per-line: a grep for a word's surface form finds that word's whole record on one line.
    [Fact]
    public void GreppingEitherJsonFileForAWordReturnsThatWordsWholeRecordOnOneLine()
    {
        using var seeded = NewSeededScratch();
        var managedRoot = NewManagedRoot();
        var selection = new SelectionRequest(false, [], ["mirusi"], false, null);
        var assessor = NewAssessor("mirusi");
        using var assessmentInvoker = NewInvoker();
        var assessment = RunAssessment(seeded, managedRoot, assessor, assessmentInvoker, selection);
        using var invoker = NewInvoker();
        var destination = Path.Combine(_root, "handoff-grep");

        var outcome = HandoffCommand.Run(
            AssessedRequest(seeded, destination, assessment.InvocationId),
            managedRoot, NewAssessor(), invoker, onProgress: null, CancellationToken.None);
        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);

        var assessmentLines = File.ReadAllLines(Path.Combine(destination, "assessment.json"));
        var matches = assessmentLines.Where(line => line.Contains("\"mirusi\"", StringComparison.Ordinal)).ToList();
        var match = Assert.Single(matches);
        Assert.Contains("\"outcome\"", match, StringComparison.Ordinal);
        using (JsonDocument.Parse(match.TrimEnd(','))) { } // one record per line, compact within it
    }

    // Round-trips the helper against a Handoff the writer actually produced, not a typed-by-hand fixture.
    [RequiresPythonFact]
    public void ThePythonHelperReadsBackTheWordAndTextRecordsTheWriterActuallyWrote()
    {
        using var seeded = NewSeededScratch();
        var managedRoot = NewManagedRoot();
        var selection = new SelectionRequest(false, [], ["mirusi"], false, null);
        var assessor = NewAssessor("mirusi");
        using var assessmentInvoker = NewInvoker();
        var assessment = RunAssessment(seeded, managedRoot, assessor, assessmentInvoker, selection);
        using var invoker = NewInvoker();
        var destination = Path.Combine(_root, "handoff-python-roundtrip");

        var outcome = HandoffCommand.Run(
            AssessedRequest(seeded, destination, assessment.InvocationId),
            managedRoot, NewAssessor(), invoker, onProgress: null, CancellationToken.None);
        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);

        using var textsDocument = JsonDocument.Parse(File.ReadAllText(Path.Combine(destination, "texts.json")));
        var expectedKey = textsDocument.RootElement.EnumerateArray().First().GetProperty("key").GetString()!;

        using var wordJson = JsonDocument.Parse(RunPythonHelper(destination, "word", "mirusi"));
        var wordRecord = Assert.Single(wordJson.RootElement.EnumerateArray());
        Assert.Equal("mirusi", wordRecord.GetProperty("word").GetString());
        Assert.Equal("analysed", wordRecord.GetProperty("outcome").GetString());
        Assert.Equal(42, wordRecord.GetProperty("elapsedMs").GetInt32());

        using var textJson = JsonDocument.Parse(RunPythonHelper(destination, "text", expectedKey));
        Assert.Equal(expectedKey, textJson.RootElement.GetProperty("key").GetString());
    }

    [RequiresPythonFact]
    public void TracePythonExampleRunsForAnOrdinaryWord() => TracePythonExampleRuns("motifa", flattenUpload: true);

    [RequiresPythonFact]
    public void TracePythonExampleRunsForPunctuation() => TracePythonExampleRuns("motifa's", flattenUpload: false);

    private void TracePythonExampleRuns(string selectedWord, bool flattenUpload)
    {
        using var seeded = NewSeededScratch();
        using var invoker = NewInvoker();
        var destination = Path.Combine(_root, "handoff-python-trace-" + Guid.NewGuid().ToString("N"));
        var outcome = HandoffCommand.Run(
            new HandoffRequest(seeded.FwDataPath, destination,
                new SelectionRequest(false, [], [selectedWord], false, null), true),
            NewManagedRoot(), NewAssessor(selectedWord), invoker, onProgress: null, CancellationToken.None);

        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        var traceFileName = Path.GetFileName(HandoffWriter.TraceRelativePath(selectedWord));
        if (flattenUpload)
        {
            var nestedTracePath = Path.Combine(destination, "traces", traceFileName);
            var flatTracePath = Path.Combine(destination, traceFileName);
            File.Move(nestedTracePath, flatTracePath);
            Directory.Delete(Path.Combine(destination, "traces"));
        }

        var traceHeading = "## " + traceFileName;
        var sectionStart = outcome.Value!.HandoffMarkdown.IndexOf(traceHeading, StringComparison.Ordinal);
        Assert.True(sectionStart >= 0, "The trace file needs a matching Help section.");
        var commandStart = outcome.Value.HandoffMarkdown.IndexOf("python -c ", sectionStart, StringComparison.Ordinal);
        Assert.True(commandStart >= 0, "The trace section needs a runnable Python example.");
        var commandEnd = outcome.Value.HandoffMarkdown.IndexOf('\n', commandStart);
        var command = outcome.Value.HandoffMarkdown[commandStart..commandEnd]
            .Replace("python", PythonExecutable.Path!, StringComparison.Ordinal);

        var result = RunShellCommand(destination, command);
        using var trace = JsonDocument.Parse(result);
        Assert.Equal(selectedWord, trace.RootElement.GetProperty("word").GetString());
    }

    private static string RunPythonHelper(string destination, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo(PythonExecutable.Path!) { WorkingDirectory = destination };
        startInfo.ArgumentList.Add(Path.Combine(destination, "parse_grammar_texts_assessment.py"));
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);

        var result = ToolProcess.Run(startInfo);
        Assert.True(result.ExitCode == 0, $"python exited {result.ExitCode}: {result.Error}");
        return result.Output;
    }

    private static string RunShellCommand(string workingDirectory, string command)
    {
        var startInfo = new ProcessStartInfo(OperatingSystem.IsWindows() ? "pwsh" : "/bin/sh")
        {
            WorkingDirectory = workingDirectory,
        };
        if (OperatingSystem.IsWindows())
        {
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-Command");
        }
        else
        {
            startInfo.ArgumentList.Add("-c");
        }
        startInfo.ArgumentList.Add(command);
        var result = ToolProcess.Run(startInfo);
        Assert.True(result.ExitCode == 0, $"The emitted shell command exited {result.ExitCode}: {result.Error}");
        return result.Output;
    }

    [Fact]
    public void AnExistingNonEmptyDestinationRefusesWithoutTouchingIt()
    {
        using var seeded = NewSeededScratch();
        var managedRoot = NewManagedRoot();
        using var invoker = NewInvoker();
        var destination = Path.Combine(_root, "handoff-existing");
        Directory.CreateDirectory(destination);
        File.WriteAllText(Path.Combine(destination, "keep.txt"), "do not touch");

        var outcome = HandoffCommand.Run(
            new HandoffRequest(seeded.FwDataPath, destination, AllWordformsAllTexts, false),
            managedRoot, NewAssessor(), invoker, onProgress: null, CancellationToken.None);

        Assert.False(outcome.Succeeded);
        Assert.Equal("handoff.destination-exists", outcome.Refusal!.Code);
        Assert.True(File.Exists(Path.Combine(destination, "keep.txt")));
    }

    [Fact]
    public async Task InFlightImportCancellationCleansStagingAndAllowsRetry()
    {
        using var seeded = NewSeededScratch();
        var managedRoot = NewManagedRoot();
        var destination = Path.Combine(_root, "handoff-cancelled");
        Directory.CreateDirectory(destination);
        var invoker = new HeldCancellationInvoker();
        using var stopping = new CancellationTokenSource();

        var running = Task.Run(() => HandoffCommand.Run(
            new HandoffRequest(seeded.FwDataPath, destination, AllWordformsAllTexts, false),
            managedRoot, NewAssessor(), invoker, onProgress: null, stopping.Token));
        string incoming;
        CancellationToken importToken;
        CommandOutcome<HandoffCommandResponse> outcome;
        try
        {
            importToken = await invoker.ImportStarted.WaitAsync(TimeSpan.FromMinutes(2));
            Assert.True(importToken.CanBeCanceled);
            Assert.False(importToken.IsCancellationRequested);

            incoming = Assert.Single(Directory.GetDirectories(_root, ".incoming-*"));
            AssertFile(incoming, "texts.json");
            AssertFile(incoming, "parse_grammar_texts_assessment.py");
            Assert.Empty(Directory.EnumerateFileSystemEntries(destination));

            stopping.Cancel();
            outcome = await running.WaitAsync(TimeSpan.FromMinutes(2));
        }
        finally
        {
            stopping.Cancel();
            await running.WaitAsync(TimeSpan.FromMinutes(2));
        }

        Assert.False(outcome.Succeeded);
        Assert.Equal("handoff.cancelled", outcome.Refusal!.Code);
        Assert.Equal(FailureReason.Cancelled, outcome.Refusal.Reason);
        Assert.True(importToken.IsCancellationRequested);
        Assert.False(Directory.Exists(incoming));
        Assert.True(Directory.Exists(destination));
        Assert.Empty(Directory.EnumerateFileSystemEntries(destination));

        using var retryInvoker = NewInvoker();
        var retry = HandoffCommand.Run(
            new HandoffRequest(seeded.FwDataPath, destination, AllWordformsAllTexts, false),
            managedRoot, NewAssessor(), retryInvoker, onProgress: null, CancellationToken.None);

        Assert.True(retry.Succeeded, retry.Refusal?.Message);
        AssertFile(destination, "grammar.json");
        Assert.Empty(Directory.GetDirectories(_root, ".incoming-*"));
    }

    [Fact]
    public void APanGlossGrammarImportFailureLeavesNoDestinationDirectory()
    {
        using var seeded = NewSeededScratch();
        var managedRoot = NewManagedRoot();
        var destination = Path.Combine(_root, "handoff-parser-failure");
        var invoker = new FakeInvoker { Respond = _ => new PanGlossOutcome.Unavailable("boom") };

        var outcome = HandoffCommand.Run(
            new HandoffRequest(seeded.FwDataPath, destination, AllWordformsAllTexts, false),
            managedRoot, NewAssessor(), invoker, onProgress: null, CancellationToken.None);

        Assert.False(outcome.Succeeded);
        Assert.Equal("handoff.parser-unavailable", outcome.Refusal!.Code);
        Assert.False(Directory.Exists(destination));
    }

    // ADR 0045 decision 2: a Handoff with no Assessment is valid, not a degraded one.
    [Fact]
    public void NoAssessOmitsAssessmentJsonButStillWritesGrammarAndTexts()
    {
        using var seeded = NewSeededScratch();
        var managedRoot = NewManagedRoot();
        using var invoker = NewInvoker();
        var destination = Path.Combine(_root, "handoff-no-assess");

        var outcome = HandoffCommand.Run(
            new HandoffRequest(seeded.FwDataPath, destination, AllWordformsAllTexts, false),
            managedRoot, NewAssessor(), invoker, onProgress: null, CancellationToken.None);

        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        Assert.Empty(outcome.Value!.AssessmentIds);
        AssertFile(destination, "grammar.json");
        AssertFile(destination, "texts.json");
        AssertFile(destination, "parse_grammar_texts_assessment.py");
        AssertFile(destination, "handoff.md");
        Assert.False(File.Exists(Path.Combine(destination, "assessment.json")));
        Assert.Contains(
            "No Assessment was run", File.ReadAllText(Path.Combine(destination, "handoff.md")),
            StringComparison.Ordinal);
    }

    [Fact]
    public void DuplicateTextTitlesProduceTwoDistinctRecords()
    {
        using var seeded = NewSeededScratch();
        AddPlainText(seeded.Cache, SeededProject.TextTitle);
        new FwDataProjectLoader().Save(seeded.Cache);

        var managedRoot = NewManagedRoot();
        using var invoker = NewInvoker();
        var destination = Path.Combine(_root, "handoff-duplicate-titles");

        var outcome = HandoffCommand.Run(
            new HandoffRequest(seeded.FwDataPath, destination, AllWordformsAllTexts, false),
            managedRoot, NewAssessor(), invoker, onProgress: null, CancellationToken.None);

        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(destination, "texts.json")));
        var keys = document.RootElement.EnumerateArray()
            .Select(record => record.GetProperty("key").GetString()).ToList();
        Assert.Equal(2, keys.Count);
        Assert.Equal(2, keys.Distinct().Count());
    }

    [Fact]
    public void AnEmptySelectionRefusesWithoutTouchingTheDestination()
    {
        using var seeded = NewSeededScratch();
        var managedRoot = NewManagedRoot();
        using var invoker = NewInvoker();
        var destination = Path.Combine(_root, "handoff-empty-selection");
        var emptySelection = new SelectionRequest(false, [], [], false, null);

        var outcome = HandoffCommand.Run(
            new HandoffRequest(seeded.FwDataPath, destination, emptySelection, false),
            managedRoot, NewAssessor(), invoker, onProgress: null, CancellationToken.None);

        Assert.False(outcome.Succeeded);
        Assert.Equal("selection.empty", outcome.Refusal!.Code);
        Assert.False(Directory.Exists(destination));
    }

    // A mistyped path must report the missing project, not any collaborator this run would otherwise use.
    [Fact]
    public void AMissingProjectIsRefusedBeforeTheParserIsEvenBuilt()
    {
        var managedRoot = NewManagedRoot();
        var missingProject = Path.Combine(_root, "absent.fwdata");
        var destination = Path.Combine(_root, "handoff-missing-project");
        var mustNotRun = new FakeAssessor("fake-assessor", CollectedKinds,
            _ => throw new InvalidOperationException("A refused request must never reach the Assessor."));
        var unreachableInvoker = new FakeInvoker
        {
            Respond = _ => throw new InvalidOperationException("A refused request must never reach the invoker."),
        };

        var outcome = HandoffCommand.Run(
            new HandoffRequest(missingProject, destination, AllWordformsAllTexts, false),
            managedRoot, mustNotRun, unreachableInvoker, onProgress: null, CancellationToken.None);

        Assert.False(outcome.Succeeded);
        Assert.Equal("project.not-found", outcome.Refusal!.Code);
    }

    private static void AssertFile(string root, string relativePath) =>
        Assert.True(File.Exists(Path.Combine(root, relativePath)), $"Missing '{relativePath}'.");

    private FakeAssessor NewAssessor(string? analyzedWord = null)
    {
        return new FakeAssessor("fake-assessor", CollectedKinds, rawForScope: (scope, candidate, kind) =>
        {
            if (kind == AssessmentKind.ObjectTiming)
            {
                var cachePath = Path.Combine(_root, "fake-stats-cache-" + Guid.NewGuid().ToString("N") + ".sqlite");
                WriteStatsCache(cachePath, scope.Words);
                return new AssessmentRaw.FileCache(cachePath, BatchInvocationEvidence.DigestFile(cachePath));
            }
            if (kind == AssessmentKind.ParseTime)
            {
                var projectPath = Directory.GetFiles(candidate, "*.fwdata", SearchOption.AllDirectories).Single();
                var words = scope.Words.Select((word, index) =>
                {
                    var analyzed = StringComparer.Ordinal.Equals(word, analyzedWord);
                    return new WordAnalysis(index, word, analyzed ? 42 : 1,
                        analyzed ? WordOutcome.Analysed : WordOutcome.NoAnalysis, analyzed ? "sig" : "none");
                }).ToArray();
                return new AssessmentRaw.Batch(new BatchAnalysis(words,
                    (int)scope.PerWordLimit!.Value.TotalMilliseconds, projectPath, [])
                { PerWordStepLimit = scope.PerWordStepLimit });
            }
            return new AssessmentRaw.WordMeasurements([]);
        })
        {
            CaptureEvidence = (scope, candidate) => FakeAssessmentEvidence.Capture(_root, scope, candidate),
        };
    }

    private static void WriteStatsCache(string path, IReadOnlyList<string> words)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE word (
                word_id INTEGER PRIMARY KEY, form TEXT NOT NULL, elapsed_ns INTEGER NOT NULL,
                attempts INTEGER NOT NULL, passes INTEGER NOT NULL, capped INTEGER NOT NULL,
                timed_out INTEGER NOT NULL, invalid_shape INTEGER NOT NULL);
            CREATE TABLE object (object_id INTEGER PRIMARY KEY, key TEXT NOT NULL, kind TEXT NOT NULL,
                label TEXT NOT NULL, identity_quality TEXT NOT NULL);
            CREATE TABLE fact (word_id INTEGER NOT NULL, object_id INTEGER NOT NULL,
                direction TEXT NOT NULL, attempts INTEGER NOT NULL, self_time_ns INTEGER NOT NULL);
            """;
        command.ExecuteNonQuery();
        foreach (var word in words)
        {
            using var insert = connection.CreateCommand();
            insert.CommandText = """
                INSERT INTO word (form, elapsed_ns, attempts, passes, capped, timed_out, invalid_shape)
                VALUES ($form, 0, 1, 0, 0, 0, 0);
                """;
            insert.Parameters.AddWithValue("$form", word);
            insert.ExecuteNonQuery();
        }
    }

    // The real module against the fake executable: the Handoff's grammar and statistics files come from it.
    private static PanGlossInvoker NewInvoker() => new(FakeParser.ExecutablePath, new MachinePanGlossQueue(new[]
    {
        "Local\\MotifHandoffWriterTests-" + Guid.NewGuid().ToString("N") + "-0",
        "Local\\MotifHandoffWriterTests-" + Guid.NewGuid().ToString("N") + "-1",
    }));

    private static AssessCommandResponse RunAssessment(
        SeededScratch seeded, string managedRoot, IAssessor assessor, IPanGlossInvoker invoker,
        SelectionRequest? selection = null)
    {
        var outcome = AssessCommand.Run(
            new AssessRequest(seeded.FwDataPath, selection ?? AllWordformsAllTexts), managedRoot,
            assessor, invoker, onProgress: null, CancellationToken.None);
        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        return outcome.Value!;
    }

    private static HandoffRequest AssessedRequest(
        SeededScratch seeded, string destination, string invocationId) =>
        new(seeded.FwDataPath, destination, new SelectionRequest(false, [], [], false, null), true, invocationId);

    private static RetainedInvocationRecord ForeignRetained(string invocationId) => new(
        invocationId, "foreign-project", ForeignBaseline(), "C:/managed/foreign",
        "C:/managed/foreign/project.fwdata", ForeignUtc("2020-01-01T00:00:00Z"),
        ForeignUtc("2020-01-01T00:01:00Z"), ForeignUtc("2020-01-01T00:02:00Z"),
        ForeignSelection(), "pangloss", "{\"words\":\"all\"}", "sha256:scope", invocationId,
        [new RetainedInvocationMember("ParseTime", "foreign-assessment")]);

    private static NewAssessmentRecord ForeignAssessment(string id, string invocationId) =>
        new(id, null, null, "pangloss", "ParseTime", "{\"words\":\"all\"}", "sha256:scope",
            "none", "1", JsonSerializer.Serialize(ForeignBaseline()),
            Selection.Create("project", ["word"]), "sha256:outcome", "sha256:semantic",
            "sha256:source", "fingerprint", "pipeline", 0,
            [new AssessedWord("word", "complete", [])])
        {
            Invocation = new BatchInvocationEvidence(
                invocationId, "source.fwdata", "sha256:source", "sha256:executable", "words.txt",
                "sha256:words", "rows.tsv", "sha256:rows", "stderr.txt", "sha256:stderr",
                1000, 200000, 1, true)
        };

    private static SelectionDescriptor ForeignSelection()
    {
        var selection = new SelectionDescriptor(
            [], ["word"], false, false, null, null, ["word"],
            Selection.Create("project", ["word"]).Sha256, [new("pasted-words", 1)]);
        return selection with { DescriptorSha256 = SelectionDescriptorDigest.Compute(selection) };
    }

    private static BaselineToken ForeignBaseline() => new(
        "project", "sha256:" + new string('a', 64), "projection-1", "2020-01-01T00:00:00Z",
        "sha256:" + new string('b', 64));

    private static DateTimeOffset ForeignUtc(string value) => DateTimeOffset.ParseExact(
        value, "yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);

    // SeedText's wordforms must be saved to disk for HandoffCommand's own scratch loads to see them.
    private SeededScratch NewSeededScratch()
    {
        var cache = _pristine.NewScratch();
        var text = SeededProject.SeedText(cache, _pristine.Seed);
        new FwDataProjectLoader().Save(cache);
        return new SeededScratch(cache, text.TextId);
    }

    private string NewManagedRoot()
    {
        var root = Path.Combine(_root, "managed-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    // A second Text sharing an existing title, minimal enough to exercise only the naming collision.
    private static void AddPlainText(LcmCache cache, string title)
    {
        var services = cache.ServiceLocator;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var text = services.GetInstance<ITextFactory>().Create();
            text.Name.set_String(cache.DefaultAnalWs, title);
            var contents = services.GetInstance<IStTextFactory>().Create();
            text.ContentsOA = contents;
            var paragraph = services.GetInstance<IStTxtParaFactory>().Create();
            contents.ParagraphsOS.Add(paragraph);
            paragraph.Contents = TsStringUtils.MakeString("filler", cache.DefaultVernWs);
        });
    }

    private sealed class SeededScratch(LcmCache cache, Guid textId) : IDisposable
    {
        public LcmCache Cache => cache;
        public string FwDataPath => cache.ProjectId.Path;
        public Guid TextId => textId;
        public void Dispose() => cache.Dispose();
    }

    private sealed class HeldCancellationInvoker : IPanGlossInvoker
    {
        private readonly TaskCompletionSource<CancellationToken> _importStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<CancellationToken> ImportStarted => _importStarted.Task;

        public async Task<PanGlossOutcome> RunAsync(
            PanGlossRequest request, string label, CancellationToken cancellationToken,
            TimeSpan? wallClockCap = null)
        {
            if (request is not PanGlossRequest.Import)
                throw new InvalidOperationException("The Handoff should request a grammar import.");

            _importStarted.TrySetResult(cancellationToken);
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return new PanGlossOutcome.Completed(string.Empty, string.Empty, TimeSpan.Zero);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return new PanGlossOutcome.Cancelled();
            }
        }
    }

}
