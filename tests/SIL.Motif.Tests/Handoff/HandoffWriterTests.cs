using System.Diagnostics;
using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands.Assess;
using SIL.Motif.Commands.Handoff;
using SIL.Motif.Contract.Canonicalization;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.Store;
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
/// executable: the exact folder listing, atomicity on an existing destination, cancellation and
/// PanGloss-failure cleanup, <c>--no-assess</c>, <c>--flextext</c>, and duplicate Text titles.
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
            new HandoffRequest(seeded.FwDataPath, destination, AllWordformsAllTexts, false, true),
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
        Assert.Contains("motifa", File.ReadAllText(Path.Combine(destination, "selection.txt")), StringComparison.Ordinal);
        Assert.DoesNotContain("motifb", File.ReadAllText(Path.Combine(destination, "selection.txt")), StringComparison.Ordinal);
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
    public void RetainedInvocationFromAnotherProjectRefusesBeforeTouchingTheDestination()
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
        var changed = false;
        var invoker = new InspectingInvoker(realInvoker, request =>
        {
            if (request is PanGlossRequest.Stats && !changed)
            {
                File.WriteAllText(tamperRetained ? evidence!.SourcePath : baselinePath!, "unreadable changed project bytes");
                changed = true;
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
    public void AnEndToEndHandoffWritesTheExactListingAndEveryFileValidates()
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

        Assert.True(outcome.Succeeded);
        var response = outcome.Value!;
        Assert.Equal(destination, response.OutputDirectory);
        Assert.Equal(2, response.AssessmentIds.Count);
        Assert.True(response.Selection.Words.Count > 0);

        AssertFile(destination, "instructions.md");
        AssertFile(destination, "starter-prompt.md");
        AssertFile(destination, "grammar.json");
        AssertFile(destination, "selection.txt");
        AssertFile(destination, "statistics.md");
        AssertFile(destination, "recipes.md");
        AssertFile(destination, "read_handoff.py");
        AssertFile(destination, Path.Combine("reference", "grammar-format.md"));
        AssertFile(destination, Path.Combine("reference", "flextext-json-format.md"));
        AssertFile(destination, Path.Combine("reference", "hc-mechanics.md"));

        foreach (var group in HandoffWriter.StatisticsGroups)
            AssertFile(destination, Path.Combine("statistics", group + ".jsonl"));

        var textFiles = Directory.GetFiles(Path.Combine(destination, "texts"), "*.flextext.json");
        Assert.Single(textFiles);

        Assert.Contains("grammar.json", response.Files);
        Assert.Contains("selection.txt", response.Files);
        Assert.Contains("starter-prompt.md", response.Files);
    }

    [PythonAvailableFact]
    public void ReadHandoffPyValidatesAndSummarizesARealHandoffFolder()
    {
        using var seeded = NewSeededScratch();
        var managedRoot = NewManagedRoot();
        using var assessmentInvoker = NewInvoker();
        var assessment = RunAssessment(seeded, managedRoot, NewAssessor(), assessmentInvoker);
        using var invoker = NewInvoker();
        var destination = Path.Combine(_root, "handoff-python");

        var outcome = HandoffCommand.Run(
            AssessedRequest(seeded, destination, assessment.InvocationId),
            managedRoot, NewAssessor(), invoker, onProgress: null, CancellationToken.None);
        Assert.True(outcome.Succeeded);

        var scriptPath = Path.Combine(destination, "read_handoff.py");
        var validate = RunPython(scriptPath, "validate-handoff", destination);
        Assert.Equal(0, validate.ExitCode);
        Assert.Equal("[]", validate.StandardOutput.Trim());

        var summarize = RunPython(scriptPath, "summarize-counts", destination);
        Assert.Equal(0, summarize.ExitCode);
        Assert.Contains("\"text_count\": 1", summarize.StandardOutput);
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
            new HandoffRequest(seeded.FwDataPath, destination, AllWordformsAllTexts, false, false),
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
            new HandoffRequest(seeded.FwDataPath, destination, AllWordformsAllTexts, false, false),
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
            new HandoffRequest(seeded.FwDataPath, destination, AllWordformsAllTexts, false, false),
            managedRoot, NewAssessor(), invoker, onProgress: null, CancellationToken.None);

        Assert.False(outcome.Succeeded);
        Assert.Equal("handoff.parser-unavailable", outcome.Refusal!.Code);
        Assert.False(Directory.Exists(destination));
    }

    [Fact]
    public void NoAssessOmitsStatisticsButStillWritesGrammarTextsAndSelection()
    {
        using var seeded = NewSeededScratch();
        var managedRoot = NewManagedRoot();
        using var invoker = NewInvoker();
        var destination = Path.Combine(_root, "handoff-no-assess");

        var outcome = HandoffCommand.Run(
            new HandoffRequest(seeded.FwDataPath, destination, AllWordformsAllTexts, false, false),
            managedRoot, NewAssessor(), invoker, onProgress: null, CancellationToken.None);

        Assert.True(outcome.Succeeded);
        Assert.Empty(outcome.Value!.AssessmentIds);
        AssertFile(destination, "grammar.json");
        AssertFile(destination, "selection.txt");
        Assert.True(Directory.Exists(Path.Combine(destination, "texts")));
        Assert.False(File.Exists(Path.Combine(destination, "statistics.md")));
        Assert.False(Directory.Exists(Path.Combine(destination, "statistics")));
    }

    [Fact]
    public void FlexTextAddsMatchingXmlBesideJson()
    {
        using var seeded = NewSeededScratch();
        var managedRoot = NewManagedRoot();
        using var invoker = NewInvoker();
        var destination = Path.Combine(_root, "handoff-flextext");

        var outcome = HandoffCommand.Run(
            new HandoffRequest(seeded.FwDataPath, destination, AllWordformsAllTexts, true, false),
            managedRoot, NewAssessor(), invoker, onProgress: null, CancellationToken.None);

        Assert.True(outcome.Succeeded);
        var jsonFiles = Directory.GetFiles(Path.Combine(destination, "texts"), "*.flextext.json");
        var xmlFiles = Directory.GetFiles(Path.Combine(destination, "texts"), "*.flextext.xml");
        Assert.Single(jsonFiles);
        Assert.Single(xmlFiles);
        Assert.Equal(
            Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(jsonFiles[0])),
            Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(xmlFiles[0])));
    }

    [Fact]
    public void DuplicateTextTitlesProduceTwoDistinctFiles()
    {
        using var seeded = NewSeededScratch();
        AddPlainText(seeded.Cache, SeededProject.TextTitle);
        new FwDataProjectLoader().Save(seeded.Cache);

        var managedRoot = NewManagedRoot();
        using var invoker = NewInvoker();
        var destination = Path.Combine(_root, "handoff-duplicate-titles");

        var outcome = HandoffCommand.Run(
            new HandoffRequest(seeded.FwDataPath, destination, AllWordformsAllTexts, false, false),
            managedRoot, NewAssessor(), invoker, onProgress: null, CancellationToken.None);

        Assert.True(outcome.Succeeded);
        var textFiles = Directory.GetFiles(Path.Combine(destination, "texts"), "*.flextext.json");
        Assert.Equal(2, textFiles.Length);
        Assert.Equal(2, textFiles.Distinct().Count());
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
            new HandoffRequest(seeded.FwDataPath, destination, emptySelection, false, false),
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
            new HandoffRequest(missingProject, destination, AllWordformsAllTexts, false, false),
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
        new(seeded.FwDataPath, destination, new SelectionRequest(false, [], [], false, null),
            false, true, invocationId);

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

    private static (int ExitCode, string StandardOutput) RunPython(string scriptPath, params string[] args)
    {
        var startInfo = new ProcessStartInfo("python")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add(scriptPath);
        foreach (var arg in args) startInfo.ArgumentList.Add(arg);

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start python.");

        // Drain both streams concurrently: a large enough write on either one deadlocks a sequential read.
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        Assert.True(process.WaitForExit(60_000), "read_handoff.py did not exit within its bound.");
        var output = outputTask.GetAwaiter().GetResult();
        var error = errorTask.GetAwaiter().GetResult();

        Assert.True(process.ExitCode == 0 || process.ExitCode == 1, $"Unexpected exit {process.ExitCode}: {error}");
        return (process.ExitCode, output);
    }
}
