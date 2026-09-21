using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands.Assess;
using SIL.Motif.Commands.Handoff;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Canonicalization;
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
/// executable: the exact five-file listing, atomicity on an existing destination, cancellation and
/// PanGloss-failure cleanup, <c>--no-assess</c>, and duplicate Text titles.
/// </summary>
[Collection(LcmCacheTestCollection.Name)]
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
        File.WriteAllText(cachePath, "statistics");
        BatchInvocationEvidence? evidence = null;
        string? baselinePath = null;
        string? importedDigest = null;
        string? importedPath = null;
        var assessor = new FakeAssessor("fake-assessor", CollectedKinds, kind =>
            kind == AssessmentKind.ObjectTiming
                ? new AssessmentRaw.FileCache(cachePath, BatchInvocationEvidence.DigestFile(cachePath))
                : new AssessmentRaw.WordMeasurements([]))
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
        var cachePath = WriteFakeCache();
        var assessor = new FakeAssessor("fake-assessor", CollectedKinds, kind => kind == AssessmentKind.ObjectTiming
            ? new AssessmentRaw.FileCache(cachePath, BatchInvocationEvidence.DigestFile(cachePath))
            : new AssessmentRaw.WordMeasurements([new AssessedWord("mirusi", "analysed", [], 42, "sig")]))
        {
            CaptureEvidence = (scope, candidate) => FakeAssessmentEvidence.Capture(_root, scope, candidate),
        };
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
        var cachePath = WriteFakeCache();
        var assessor = new FakeAssessor("fake-assessor", CollectedKinds, kind => kind == AssessmentKind.ObjectTiming
            ? new AssessmentRaw.FileCache(cachePath, BatchInvocationEvidence.DigestFile(cachePath))
            : new AssessmentRaw.WordMeasurements([new AssessedWord("mirusi", "analysed", [], 42, "sig")]))
        {
            CaptureEvidence = (scope, candidate) => FakeAssessmentEvidence.Capture(_root, scope, candidate),
        };
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

    private static string RunPythonHelper(string destination, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo(PythonExecutable.Path!)
        {
            WorkingDirectory = destination,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(Path.Combine(destination, "parse_grammar_texts_assessment.py"));
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo)!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        Assert.True(process.WaitForExit(15000), "The python helper did not exit within 15 seconds.");
        Assert.True(process.ExitCode == 0, $"python exited {process.ExitCode}: {stderr}");
        return stdout;
    }

    private string WriteFakeCache()
    {
        var cachePath = Path.Combine(_root, "fake-stats-cache-" + Guid.NewGuid().ToString("N") + ".bin");
        File.WriteAllText(cachePath, "fake per-object stats cache");
        return cachePath;
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
    public void CancellationDuringGrammarImportLeavesNoDestinationDirectory()
    {
        using var seeded = NewSeededScratch();
        var managedRoot = NewManagedRoot();
        var destination = Path.Combine(_root, "handoff-cancelled");
        var invoker = new FakeInvoker { Respond = _ => new PanGlossOutcome.Cancelled() };

        var outcome = HandoffCommand.Run(
            new HandoffRequest(seeded.FwDataPath, destination, AllWordformsAllTexts, false),
            managedRoot, NewAssessor(), invoker, onProgress: null, CancellationToken.None);

        Assert.False(outcome.Succeeded);
        Assert.Equal("handoff.cancelled", outcome.Refusal!.Code);
        Assert.Equal(FailureReason.Cancelled, outcome.Refusal.Reason);
        Assert.False(Directory.Exists(destination));
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

    private FakeAssessor NewAssessor()
    {
        var cachePath = Path.Combine(_root, "fake-stats-cache-" + Guid.NewGuid().ToString("N") + ".bin");
        File.WriteAllText(cachePath, "fake per-object stats cache");
        return new FakeAssessor("fake-assessor", CollectedKinds, kind => kind == AssessmentKind.ObjectTiming
            ? new AssessmentRaw.FileCache(cachePath, BatchInvocationEvidence.DigestFile(cachePath))
            : new AssessmentRaw.WordMeasurements([]))
        {
            CaptureEvidence = (scope, candidate) => FakeAssessmentEvidence.Capture(_root, scope, candidate),
        };
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

}
