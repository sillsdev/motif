using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Microsoft.Data.Sqlite;
using SIL.LCModel;
using SIL.LCModel.DomainServices;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Assess;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Analysis;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.Store;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Tests.Parser;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary>
/// Pins <see cref="AssessCommand"/> over a real, file-backed seeded project, against a fake
/// <see cref="IAssessor"/> and a <see cref="FakeInvoker"/>: a first run capturing a Baseline and
/// recording Assessments, a second run reusing that Baseline, cancellation recording nothing, an empty
/// Selection's refusal, and the reported progress stages.
/// </summary>
[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group2)]
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
        Assert.Null(response.GrammarWarnings);

        var repository = OpenRepository(seeded.FwDataPath);
        Assert.Single(repository.ListBaselineAssessments(AssessmentKind.ParseTime.ToStoredKind()));
        Assert.Single(repository.ListBaselineAssessments(AssessmentKind.ObjectTiming.ToStoredKind()));
    }

    [Fact]
    public void LimitEstimateUsesTheLatestStoredParserStatistics()
    {
        using var seeded = NewSeededScratch();
        var cachePath = Path.Combine(_managedRootsParent, "estimate.sqlite");
        // PanGloss derives word attempts from StepBudget ticks (pg-cli/src/stats_cmd.rs:330).
        WriteStatsCache(cachePath, 725_001L, ("motifa", 10, 1, 90, 2_000_000L));
        var cacheDigest = BatchInvocationEvidence.DigestFile(cachePath);
        var assessor = new FakeAssessor("fake-assessor", CollectedKinds, kind => kind switch
        {
            AssessmentKind.ParseTime => new AssessmentRaw.Batch(new SIL.Motif.Host.Parser.BatchAnalysis(
                [new(0, "motifa", 2000, SIL.Motif.Host.Parser.WordOutcome.Analysed, "complete")
                {
                    Morphology = new ParseWordEvidence(
                        SIL.Motif.Host.Parser.ParseMorphEvidence.Schema, 0, "motifa", 2000,
                        false, false, false, [], []),
                }],
                1000, seeded.FwDataPath, []) { PerWordStepLimit = StepCap.Default }),
            AssessmentKind.ObjectTiming => new AssessmentRaw.FileCache(cachePath, cacheDigest),
            _ => new AssessmentRaw.WordMeasurements([]),
        })
        {
            CaptureEvidence = (scope, candidate) => FakeAssessmentEvidence.Capture(
                _managedRootsParent, scope, candidate),
        };
        var measured = AssessCommand.Run(new AssessRequest(seeded.FwDataPath,
            new SelectionRequest(false, [], ["motifa"], false, null)), NewManagedRoot(), assessor,
            NewInvoker(), null, CancellationToken.None);
        Assert.True(measured.Succeeded, measured.Refusal?.Message);

        var rate = SelectionLimitEstimateQuery.ReadParserStepRate(seeded.FwDataPath);
        Assert.True(rate.Succeeded, rate.Refusal?.Message);
        Assert.False(rate.Value!.IsTypicalMachine);
        Assert.Equal(200m, rate.Value.MillisecondsPerStep);
        var parseRecord = measured.Value!.AssessmentIds.Select(OpenRepository(seeded.FwDataPath).Get)
            .Single(record => record.Kind == AssessmentKind.ParseTime.ToStoredKind());
        var parseWord = Assert.Single(OpenRepository(seeded.FwDataPath).Get(parseRecord.AssessmentId).Words!);
        Assert.Equal(10, parseWord.Morphology!.Attempts);
        Assert.Equal(725_001L, parseWord.ElapsedNs);

        var estimate = StepLimitEstimator.Calculate(new StepCap(4), rate.Value);
        Assert.NotNull(estimate);
        Assert.Equal(800m, estimate.EstimatedMilliseconds);
        Assert.Equal(8_000, estimate.PerWordTimeLimitMs);
    }

    [Fact]
    public void LimitEstimateUsesTheDocumentedTypicalMachineRateWithoutStoredStatistics()
    {
        using var seeded = NewSeededScratch();

        var rate = SelectionLimitEstimateQuery.ReadParserStepRate(seeded.FwDataPath);

        Assert.True(rate.Succeeded, rate.Refusal?.Message);
        Assert.True(rate.Value!.IsTypicalMachine);
        Assert.Equal(StepLimitEstimator.TypicalMachineMillisecondsPerStep,
            rate.Value.MillisecondsPerStep);
        var estimate = StepLimitEstimator.Calculate(new StepCap(1_000_000), rate.Value);
        Assert.NotNull(estimate);
        Assert.Equal(4_000, estimate.EstimatedMilliseconds);
        Assert.Equal(40_000, estimate.PerWordTimeLimitMs);
    }

    [Fact]
    public void SavingNoStepLimitStoresNoTimeLimitAndTheDefaultRunUsesNoTimeLimit()
    {
        using var seeded = NewSeededScratch();
        var saved = SelectionCommands.SetDefault(new SetDefaultSelectionRequest(
            seeded.FwDataPath, "Default", [], ["motifa"], null, StepCap.Unbounded));
        Assert.True(saved.Succeeded, saved.Refusal?.Message);
        Assert.Null(saved.Value!.Selection!.PerWordLimitMs);

        AssessmentScope? observedScope = null;
        var assessor = new FakeAssessor("fake-assessor", CollectedKinds, kind => kind switch
        {
            AssessmentKind.ParseTime => new AssessmentRaw.Batch(new SIL.Motif.Host.Parser.BatchAnalysis(
                [new(0, "motifa", 12, SIL.Motif.Host.Parser.WordOutcome.Analysed, "complete")],
                null, seeded.FwDataPath, []) { PerWordStepLimit = StepCap.Unbounded }),
            _ => new AssessmentRaw.WordMeasurements([]),
        })
        {
            CaptureEvidence = (scope, candidate) =>
            {
                observedScope = scope;
                return FakeAssessmentEvidence.Capture(_managedRootsParent, scope, candidate);
            },
        };

        var assessed = AssessCommand.Run(new AssessRequest(seeded.FwDataPath), NewManagedRoot(), assessor,
            NewInvoker(), null, CancellationToken.None);

        Assert.True(assessed.Succeeded, assessed.Refusal?.Message);
        Assert.Null(observedScope!.PerWordLimit);
        Assert.Equal(StepCap.Unbounded, observedScope.PerWordStepLimit);
    }

    [Fact]
    public void OverviewReportsSavedSelectionCountsBeforeTheFirstBaseline()
    {
        using var seeded = NewSeededScratch();
        var saved = SelectionCommands.SetDefault(new SetDefaultSelectionRequest(
            seeded.FwDataPath, "Pasted words", [], ["motifa"]));

        Assert.True(saved.Succeeded, saved.Refusal?.Message);
        var overview = OverviewCommand.Overview(new OverviewRequest(seeded.FwDataPath));

        Assert.True(overview.Succeeded, overview.Refusal?.Message);
        Assert.Null(overview.Value!.BaselineToken);
        Assert.Equal(1, overview.Value.SelectionWordCount);
        Assert.Equal(0, overview.Value.SelectionTextCount);
        Assert.Equal(1, overview.Value.SelectionAddedWordCount);
        Assert.Equal(0, overview.Value.TextOccurrenceCount);
    }

    [Fact]
    public void DefaultSelectionFeedsAssessOverviewAndStoredTiming()
    {
        using var seeded = NewSeededScratch();
        var set = SelectionCommands.SetDefault(new SetDefaultSelectionRequest(
            seeded.FwDataPath, "Default", [seeded.Seeded.TextId], [], 1200, new StepCap(4321)));

        Assert.True(set.Succeeded, set.Refusal?.Message);
        var read = SelectionCommands.ReadDefault(new ReadDefaultSelectionRequest(seeded.FwDataPath));
        Assert.Equal("Default", read.Value!.Selection!.Name);
        Assert.Equal([seeded.Seeded.TextId], read.Value.Selection.TextIds);

        var cachePath = Path.Combine(_managedRootsParent, "stats.sqlite");
        WriteStatsCache(cachePath,
            (SeededProject.AnalysedWordForm, 4, 1, 2, 2_000_000L),
            (SeededProject.UnanalysedWordForm, 3, 0, 0, 0L));
        var cacheDigest = BatchInvocationEvidence.DigestFile(cachePath);
        AssessmentScope? observedScope = null;
        var assessor = new FakeAssessor("fake-assessor", CollectedKinds, kind => kind switch
        {
            AssessmentKind.ParseTime => new AssessmentRaw.Batch(new SIL.Motif.Host.Parser.BatchAnalysis(
                [new(0, SeededProject.AnalysedWordForm, 99, SIL.Motif.Host.Parser.WordOutcome.Capped, "partial"),
                 new(1, SeededProject.UnanalysedWordForm, 15, SIL.Motif.Host.Parser.WordOutcome.NoAnalysis, "none")],
                1000, seeded.FwDataPath, []) { PerWordStepLimit = StepCap.Default }),
            AssessmentKind.ObjectTiming => new AssessmentRaw.FileCache(cachePath, cacheDigest),
            _ => new AssessmentRaw.WordMeasurements([]),
        })
        {
            CaptureEvidence = (scope, candidate) =>
            {
                observedScope = scope;
                return FakeAssessmentEvidence.Capture(_managedRootsParent, scope, candidate);
            },
        };
        var invoker = new FakeInvoker
        {
            Respond = request => throw new InvalidOperationException(
                $"Assessment must not launch a second PanGloss invocation ({request.Subcommand})."),
        };

        var assessed = AssessCommand.Run(new AssessRequest(seeded.FwDataPath), NewManagedRoot(), assessor,
            invoker, null, CancellationToken.None);

        Assert.True(assessed.Succeeded, assessed.Refusal?.Message);
        Assert.Equal(TimeSpan.FromMilliseconds(1200), observedScope!.PerWordLimit);
        Assert.Equal(4321, observedScope.PerWordStepLimit);
        Assert.Equal([SeededProject.AnalysedWordForm, SeededProject.UnanalysedWordForm], assessed.Value!.Selection.Words);
        var parseAssessment = assessed.Value.AssessmentIds
            .Select(OpenRepository(seeded.FwDataPath).Get)
            .Single(record => record.Kind == AssessmentKind.ParseTime.ToStoredKind());
        var parseWords = parseAssessment.Words!;
        Assert.Equal(parseWords.Select(word => word.ProjectStanding),
            assessed.Value.Words.Select(word => word.ProjectStanding));
        Assert.Equal(parseWords.Select(word => word.ReadingGrades),
            assessed.Value.Words.Select(word => word.ReadingGrades));
        Assert.Single(parseAssessment.ObjectTimings);
        Assert.Null(parseAssessment.ObjectTimings.Single(row =>
            row.Word == SeededProject.AnalysedWordForm).Passes);
        Assert.StartsWith("1 search completed; 1 incomplete", assessed.Value.CompletionSummary, StringComparison.Ordinal);
        Assert.Contains("2 words; 1 object timing rows", assessed.Value.SummaryMarkdown, StringComparison.Ordinal);

        var renamed = SelectionCommands.SetDefault(new SetDefaultSelectionRequest(
            seeded.FwDataPath, "Renamed default", [seeded.Seeded.TextId], []));
        Assert.True(renamed.Succeeded, renamed.Refusal?.Message);
        File.SetLastWriteTimeUtc(seeded.FwDataPath,
            assessed.Value.Baseline.SourceLastWriteUtc.AddMinutes(5).UtcDateTime);
        var fieldWorksSave = new DateTimeOffset(File.GetLastWriteTimeUtc(seeded.FwDataPath), TimeSpan.Zero);
        var overview = OverviewCommand.Overview(new OverviewRequest(seeded.FwDataPath));
        Assert.True(overview.Succeeded, overview.Refusal?.Message);
        Assert.Equal(fieldWorksSave, overview.Value!.LastFieldWorksSaveUtc);
        Assert.Equal(assessed.Value.Baseline.SourceLastWriteUtc, overview.Value.BaselineSourceLastWriteUtc);
        Assert.True(overview.Value.IsStale);
        Assert.Equal(parseAssessment.AssessmentId, overview.Value.AssessmentId);
        Assert.Equal(2, overview.Value!.SelectionWordCount);
        Assert.Equal(1, overview.Value.SelectionTextCount);
        Assert.Equal(0, overview.Value.SelectionAddedWordCount);
        Assert.Equal(2, overview.Value.TextOccurrenceCount);
        Assert.Equal(0, overview.Value.RuleCount);
        Assert.Equal(1, overview.Value.TextCoverage.UnknownWords);
        Assert.Equal(1, overview.Value.Accuracy.UnknownWords);
        Assert.Equal(0, overview.Value.Accuracy.Violations);
        Assert.Null(overview.Value.Warnings);
        Assert.Equal(99, overview.Value.Timing.Percentile95Ms);

        var timing = TimingCommand.Timing(new TimingRequest(seeded.FwDataPath,
            parseAssessment.AssessmentId, "all", "rule", "Verb template", 5));
        Assert.True(timing.Succeeded, timing.Refusal?.Message);
        var timingJson = JsonSerializer.SerializeToElement(timing.Value);
        Assert.True(timingJson.TryGetProperty("IsStale", out var isStale));
        Assert.True(isStale.GetBoolean());
        Assert.Equal(2, timing.Value!.WordCount);
        Assert.Equal("Verb template", Assert.Single(timing.Value.Aggregates).Name);
        Assert.Equal("morph_rule", Assert.Single(timing.Value.Aggregates).Kind);
        Assert.Equal(2, Assert.Single(timing.Value.Aggregates).Calls);
        Assert.Single(timing.Value.CostliestWords);
        Assert.Contains(timing.Value.Words, word => word.Word == SeededProject.AnalysedWordForm &&
            word.Completion == "Step limit");

        var stepLimited = TimingCommand.Timing(new TimingRequest(seeded.FwDataPath,
            parseAssessment.AssessmentId, "step-limit", "rule", "Verb template", 5));
        Assert.True(stepLimited.Succeeded, stepLimited.Refusal?.Message);
        Assert.Equal(1, stepLimited.Value!.WordCount);
        Assert.Equal(SeededProject.AnalysedWordForm, Assert.Single(stepLimited.Value.SlowestWords).Word);
        Assert.Equal("Step limit", Assert.Single(stepLimited.Value.Words).Completion);

        var slowest = TimingCommand.Timing(new TimingRequest(seeded.FwDataPath,
            parseAssessment.AssessmentId, "slowest", "rule", "Verb template", 1));
        Assert.True(slowest.Succeeded, slowest.Refusal?.Message);
        Assert.Equal(1, slowest.Value!.WordCount);
        Assert.Equal(SeededProject.AnalysedWordForm, Assert.Single(slowest.Value.SlowestWords).Word);

        var cell = TimingCommand.Timing(new TimingRequest(seeded.FwDataPath,
            parseAssessment.AssessmentId, "cell:approved:unknown", "rule", "Verb template", 5));
        Assert.True(cell.Succeeded, cell.Refusal?.Message);
        Assert.Equal(1, cell.Value!.WordCount);

        var namedSelection = TimingCommand.Timing(new TimingRequest(seeded.FwDataPath,
            WordSet: "Renamed default", By: "rule", Rule: "Verb template", Top: 5));
        Assert.True(namedSelection.Succeeded, namedSelection.Refusal?.Message);
        Assert.Equal(2, namedSelection.Value!.WordCount);

        var explicitWord = TimingCommand.Timing(new TimingRequest(seeded.FwDataPath,
            parseAssessment.AssessmentId, "all", "rule", "Verb template", 5,
            [SeededProject.UnanalysedWordForm]));
        Assert.True(explicitWord.Succeeded, explicitWord.Refusal?.Message);
        Assert.Equal(1, explicitWord.Value!.WordCount);
        Assert.Equal(SeededProject.UnanalysedWordForm, Assert.Single(explicitWord.Value.SlowestWords).Word);

        var invalidCell = TimingCommand.Timing(new TimingRequest(seeded.FwDataPath,
            parseAssessment.AssessmentId, "cell:not-present:misspelled", "rule", "Verb template", 5));
        Assert.False(invalidCell.Succeeded);
        Assert.Equal(FailureReason.InvalidArgument, invalidCell.Refusal!.Reason);

        using (var database = OpenDatabase(seeded.FwDataPath))
            new NamedSelectionRepository(database).SetDefault("Deleted Text", [Guid.NewGuid()], []);
        var missingText = TimingCommand.Timing(new TimingRequest(seeded.FwDataPath,
            By: "rule", Rule: "Verb template", Top: 5));
        Assert.False(missingText.Succeeded);
        Assert.Equal("selection.text-not-found", missingText.Refusal!.Code);
        var namedSelectionWithExplicitAssessment = TimingCommand.Timing(new TimingRequest(seeded.FwDataPath,
            parseAssessment.AssessmentId, "Renamed default", "rule", "Verb template", 5));
        Assert.True(namedSelectionWithExplicitAssessment.Succeeded,
            namedSelectionWithExplicitAssessment.Refusal?.Message);
        Assert.Equal(2, namedSelectionWithExplicitAssessment.Value!.WordCount);
        Assert.Empty(invoker.Requests);
    }

    [Fact]
    public void UsesReadsTheDefaultSelectionsStoredAnalysesAndTimingsByIdentity()
    {
        using var seeded = NewSeededScratch();
        Assert.Equal("uses.no-assessment", ObjectUsesQuery.Query(new ObjectUsesRequest(seeded.FwDataPath,
            Words: [SeededProject.AnalysedWordForm])).Refusal?.Code);
        var assessed = AssessForUses(seeded);
        Assert.True(assessed.Succeeded, assessed.Refusal?.Message);
        var parseAssessmentId = assessed.Value!.AssessmentIds.Select(OpenRepository(seeded.FwDataPath).Get)
            .Single(record => record.Kind == AssessmentKind.ParseTime.ToStoredKind()).AssessmentId;
        var evidence = CurrentEvidenceQuery.ReadCurrentEvidence(seeded.FwDataPath).Value!.Assessment!;
        var first = evidence.Words.Single(word => word.Word == SeededProject.AnalysedWordForm)
            .StoredAnalyses.Single().Morphs[0];
        Assert.Equal(_pristine.Seed.FirstLexemeFormId.ToString("D"), first.AllomorphId, ignoreCase: true);

        var uses = ObjectUsesQuery.Query(new ObjectUsesRequest(seeded.FwDataPath,
            ObjectUseRef.ForMorpheme(first) with { TimingKind = "morph_rule", TimingKey = "mrule#0:Verb template" },
            [SeededProject.AnalysedWordForm, SeededProject.UnanalysedWordForm, "absent"]));

        Assert.True(uses.Succeeded, uses.Refusal?.Message);
        Assert.Equal(parseAssessmentId, uses.Value!.AssessmentId);
        Assert.Equal([(SeededProject.AnalysedWordForm, "Lost")],
            uses.Value.Uses!.Words.Select(word => (word.Row.Word, word.Row.Meaning)));
        Assert.Equal([(SeededProject.AnalysedWordForm, 2, 2_000_000L)],
            uses.Value.RanIn!.Words.Select(word => (word.Row.Word, word.Calls, word.ElapsedNs)));
        Assert.Empty(uses.Value.Shared!);
        Assert.Equal(["absent"], uses.Value.UnknownWords);
        var otherEntry = ObjectUsesQuery.Query(new ObjectUsesRequest(seeded.FwDataPath,
            new ObjectUseRef { AllomorphId = _pristine.Seed.SecondLexemeFormId.ToString("D"), GrammaticalInfoId = first.GrammaticalInfoId }));
        Assert.Empty(otherEntry.Value!.Uses!.Words);
        Assert.Equal("uses.invalid-request",
            ObjectUsesQuery.Query(new ObjectUsesRequest(seeded.FwDataPath, new ObjectUseRef { Label = "kat" })).Refusal?.Code);
    }

    [Fact]
    public void UsesReadsAMorphemesFactsFromTheBaselineAndTimesAStemUnderItsEntry()
    {
        using var seeded = NewSeededScratch();
        var entryKey = _pristine.Seed.FirstEntryId.ToString("D");
        var assessed = AssessForUses(seeded, "lex_entry", entryKey);
        Assert.True(assessed.Succeeded, assessed.Refusal?.Message);
        var first = CurrentEvidenceQuery.ReadCurrentEvidence(seeded.FwDataPath).Value!.Assessment!.Words
            .Single(word => word.Word == SeededProject.AnalysedWordForm).StoredAnalyses.Single().Morphs[0];

        var uses = ObjectUsesQuery.Query(new ObjectUsesRequest(seeded.FwDataPath, ObjectUseRef.ForMorpheme(first)));

        Assert.True(uses.Succeeded, uses.Refusal?.Message);
        Assert.Equal(("lex_entry", entryKey), (uses.Value!.Ref!.TimingKind, uses.Value.Ref.TimingKey));
        Assert.Equal([(SeededProject.AnalysedWordForm, 2, 2_000_000L)],
            uses.Value.RanIn!.Words.Select(word => (word.Row.Word, word.Calls, word.ElapsedNs)));
        var facts = uses.Value.Facts!;
        Assert.Equal((SeededProject.FirstForm, "stem"), (facts.Entry!.Headword, facts.Entry.MorphType));
        Assert.Equal(("Lexicon Edit", entryKey), (facts.Entry.FieldWorks!.ToolName, facts.Entry.FieldWorks.ObjectId));
        Assert.Equal("lexiconEdit", FieldWorksLinks.ToolOf(facts.Entry.FieldWorks.Link));
        Assert.Equal([("1", SeededProject.FirstGloss)], facts.Senses.Select(sense => (sense.Number, sense.Gloss)));
        Assert.Equal(("stem", "SeededNoun"), (facts.GrammaticalInfo!.Kind, facts.GrammaticalInfo.Category!.Name));
        Assert.Equal([(SeededProject.FirstForm, true)], facts.Allomorphs.Select(allomorph => (allomorph.Form, allomorph.IsAsked)));

        var named = ObjectUsesQuery.Query(new ObjectUsesRequest(seeded.FwDataPath,
            ObjectUseRef.ForMorpheme(first) with { TimingKind = "morph_rule", TimingKey = "mrule#0:Verb template" }));
        Assert.Equal("mrule#0:Verb template", named.Value!.Ref!.TimingKey);
        Assert.Empty(named.Value.RanIn!.Words);
        var words = ObjectUsesQuery.Query(new ObjectUsesRequest(seeded.FwDataPath, Words: [SeededProject.AnalysedWordForm]));
        Assert.Null(words.Value!.Facts);
        var absent = ObjectUsesQuery.Query(new ObjectUsesRequest(seeded.FwDataPath,
            new ObjectUseRef { AllomorphId = Guid.NewGuid().ToString("D") }));
        Assert.Null(absent.Value!.Facts);
        Assert.Null(absent.Value.RanIn);
    }

    [Fact]
    public void InspectResolvesAnAuthoredLexicalEntryFromTimingToItsBaselineFacts()
    {
        using var seeded = NewSeededScratch();
        Assert.True(BaselineCaptureCommand.Capture(new BaselineCaptureRequest(seeded.FwDataPath), NewManagedRoot()).Succeeded);
        var entryKey = _pristine.Seed.FirstEntryId.ToString("D");
        var key = new TraceTimingKey("lex_entry", entryKey);

        var inspected = InspectQuery.Query(new InspectRequest(seeded.FwDataPath,
            InspectorSubject.Rule(key, identityQuality: "authored")));

        Assert.True(inspected.Succeeded, inspected.Refusal?.Message);
        var response = inspected.Value!;
        Assert.Equal(InspectorResolution.Resolved, response.Resolution);
        Assert.Equal(InspectorSectionStatus.Available, response.Facts.Status);
        Assert.Equal(entryKey, response.Facts.Value!.Entry!.Id);
        Assert.Equal(SeededProject.FirstForm, response.Facts.Value.Entry.Headword);
        Assert.Equal("Lexicon Edit", response.Facts.Value.Entry.FieldWorks!.ToolName);
        Assert.Equal(key, response.TimingKey);
    }

    [Fact]
    public void InspectReadsAMorphemesBaselineFactsBeforeAnyParseAllWords()
    {
        using var seeded = NewSeededScratch();
        Assert.True(BaselineCaptureCommand.Capture(new BaselineCaptureRequest(seeded.FwDataPath), NewManagedRoot()).Succeeded);
        var entryKey = _pristine.Seed.FirstEntryId.ToString("D");

        var inspected = InspectQuery.Query(new InspectRequest(seeded.FwDataPath,
            InspectorSubject.Morpheme(_pristine.Seed.FirstLexemeFormId.ToString("D"), null, SeededProject.FirstForm)!));

        Assert.True(inspected.Succeeded, inspected.Refusal?.Message);
        var response = inspected.Value!;
        Assert.Equal(InspectorResolution.Resolved, response.Resolution);
        Assert.Null(response.AssessmentId);
        Assert.Equal(CurrentEvidenceQuery.ReadCurrentEvidence(seeded.FwDataPath).Value!.Baseline!.Token.BundleDigest,
            response.BaselineDigest);
        Assert.Equal(InspectorSectionStatus.Available, response.Facts.Status);
        Assert.Equal(SeededProject.FirstForm, response.Facts.Value!.Entry!.Headword);
        Assert.Equal(new TraceTimingKey("lex_entry", entryKey), response.TimingKey);
        Assert.Equal(InspectorSectionStatus.Absent, response.Uses.Status);
        Assert.Equal(InspectorSectionStatus.Absent, response.RanIn.Status);
        Assert.Contains("Parse all words", response.Uses.Reason, StringComparison.Ordinal);
        Assert.Equal(InspectorSectionStatus.Absent, response.Warnings.Status);
    }

    [Fact]
    public void InspectNeverJoinsOneEntrysAllomorphToAnothersGrammaticalInfoNorFillsAnIdItCouldNotFind()
    {
        using var seeded = NewSeededScratch();
        Assert.True(AssessForUses(seeded).Succeeded);
        var first = CurrentEvidenceQuery.ReadCurrentEvidence(seeded.FwDataPath).Value!.Assessment!.Words
            .Single(word => word.Word == SeededProject.AnalysedWordForm).StoredAnalyses.Single().Morphs[0];
        InspectResponse Inspect(InspectorSubject subject)
        {
            var outcome = InspectQuery.Query(new InspectRequest(seeded.FwDataPath, subject));
            Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
            return outcome.Value!;
        }

        var mixed = Inspect(InspectorSubject.Morpheme(_pristine.Seed.SecondLexemeFormId.ToString("D"), first.GrammaticalInfoId)!);
        Assert.Equal(InspectorResolution.Contradictory, mixed.Resolution);
        Assert.Equal(InspectorSectionStatus.Absent, mixed.Facts.Status);
        Assert.Null(mixed.Facts.Value);
        Assert.Equal(InspectorSectionStatus.Available, mixed.Uses.Status);
        Assert.Empty(mixed.Uses.Value!.Words);
        Assert.Null(mixed.TimingKey);

        var missing = Inspect(InspectorSubject.Morpheme(first.AllomorphId, Guid.NewGuid().ToString("D"))!);
        Assert.Equal(InspectorResolution.NotInBaseline, missing.Resolution);
        Assert.Null(missing.Facts.Value);

        var whole = Inspect(InspectorSubject.Morpheme(first)!);
        Assert.Equal(InspectorResolution.Resolved, whole.Resolution);
        Assert.Equal([(SeededProject.AnalysedWordForm, "Lost")],
            whole.Uses.Value!.Words.Select(word => (word.Row.Word, word.Row.Meaning)));

        var rule = Inspect(InspectorSubject.Rule(new TraceTimingKey("morph_rule", "mrule#0:Verb template"), "Verb template",
            identityQuality: "structural"));
        Assert.Equal(InspectorResolution.NotAuthored, rule.Resolution);
        Assert.Equal(InspectorSectionStatus.Absent, rule.Facts.Status);
        Assert.Equal(InspectorSectionStatus.Unsupported, rule.Uses.Status);
        Assert.Equal([(SeededProject.AnalysedWordForm, 2, 2_000_000L)],
            rule.RanIn.Value!.Words.Select(word => (word.Row.Word, word.Calls, word.ElapsedNs)));

        var slot = Inspect(new InspectorSubject(InspectorSubjectKind.Slot) { ObjectId = Guid.NewGuid().ToString("D") });
        Assert.Equal(InspectorResolution.Unsupported, slot.Resolution);
        Assert.Equal(InspectorSectionStatus.Unsupported, slot.Facts.Status);
        Assert.Equal(InspectorSectionStatus.Unsupported, slot.RanIn.Status);
        Assert.Null(slot.TimingKey);
    }

    [Fact]
    public void TimingOverlaysRerunRowsAndLabelsTimeoutWithoutMorphology()
    {
        using var seeded = NewSeededScratch();
        var run = 0;
        var assessor = new FakeAssessor("fake-assessor", CollectedKinds, kind => kind switch
        {
            AssessmentKind.ParseTime => new AssessmentRaw.Batch(new SIL.Motif.Host.Parser.BatchAnalysis(
                ++run == 1
                    ? [new(0, "motifa", 99, SIL.Motif.Host.Parser.WordOutcome.Capped, "partial"),
                       new(1, "motifb", 50, SIL.Motif.Host.Parser.WordOutcome.TimedOut, "partial")]
                    : [new(0, "motifa", 10, SIL.Motif.Host.Parser.WordOutcome.Analysed, "finished")],
                1000, seeded.FwDataPath, [])),
            AssessmentKind.ObjectTiming => TimingCache(),
            _ => new AssessmentRaw.WordMeasurements([]),
        })
        {
            CaptureEvidence = (scope, candidate) => FakeAssessmentEvidence.Capture(
                _managedRootsParent, scope, candidate),
        };
        AssessmentRaw TimingCache()
        {
            var path = Path.Combine(_managedRootsParent, $"timing-overlay-{run}.sqlite");
            if (run == 1)
                WriteStatsCache(path, ("motifa", 4, 1, 2, 2_000_000), ("motifb", 3, 0, 3, 3_000_000));
            else
                WriteStatsCache(path, ("motifa", 8, 1, 5, 8_000_000));
            return new AssessmentRaw.FileCache(path, BatchInvocationEvidence.DigestFile(path));
        }
        var initial = AssessCommand.Run(new AssessRequest(seeded.FwDataPath,
            new SelectionRequest(false, [], ["motifa", "motifb"], false, null)), NewManagedRoot(),
            assessor, NewInvoker(), null, CancellationToken.None);
        Assert.True(initial.Succeeded, initial.Refusal?.Message);
        var rerun = AssessCommand.Run(new AssessRequest(seeded.FwDataPath,
            new SelectionRequest(false, [], ["motifa"], false, null)), NewManagedRoot(),
            assessor, NewInvoker(), null, CancellationToken.None);
        Assert.True(rerun.Succeeded, rerun.Refusal?.Message);
        var initialId = initial.Value!.Measurements.Single(row => row.Kind == "ParseTime").AssessmentId;
        var rerunId = rerun.Value!.Measurements.Single(row => row.Kind == "ParseTime").AssessmentId;

        var timing = TimingCommand.Timing(new TimingRequest(seeded.FwDataPath, initialId,
            By: "rule", OverrideAssessmentIds: [rerunId]));

        Assert.True(timing.Succeeded, timing.Refusal?.Message);
        Assert.Equal(2, timing.Value!.WordCount);
        Assert.Equal("Finished", timing.Value.Words.Single(word => word.Word == "motifa").Completion);
        Assert.Equal(10, timing.Value.Words.Single(word => word.Word == "motifa").ElapsedMs);
        Assert.Equal("Time limit", timing.Value.Words.Single(word => word.Word == "motifb").Completion);
        Assert.Equal(30, timing.Value.MedianMs);
        Assert.Equal(11, Assert.Single(timing.Value.Aggregates).SelfMs);
    }

    [Fact]
    public void TimingKeepsTwoObjectsWithOneLabelApartAndRefusesTheSharedLabelAsARule()
    {
        using var seeded = NewSeededScratch();
        var assessor = new FakeAssessor("fake-assessor", CollectedKinds, kind => kind switch
        {
            AssessmentKind.ParseTime => new AssessmentRaw.Batch(new SIL.Motif.Host.Parser.BatchAnalysis(
                [new(0, "motifa", 10, SIL.Motif.Host.Parser.WordOutcome.Analysed, "finished")],
                1000, seeded.FwDataPath, [])),
            AssessmentKind.ObjectTiming => TimingCache(),
            _ => new AssessmentRaw.WordMeasurements([]),
        })
        {
            CaptureEvidence = (scope, candidate) => FakeAssessmentEvidence.Capture(
                _managedRootsParent, scope, candidate),
        };
        AssessmentRaw TimingCache()
        {
            var path = Path.Combine(_managedRootsParent, "timing-shared-label.sqlite");
            WriteStatsCache(path, 8_000_000L, ("motifa", 4, 1, 2, 2_000_000));
            using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = """
                    INSERT INTO object VALUES (2, 'mrule#1:Verb template', 'morph_rule', 'Verb template', 'structural');
                    INSERT INTO fact SELECT word_id, 2, 'analysis', 3, 4000000 FROM word WHERE form = 'motifa';
                    """;
                command.ExecuteNonQuery();
            }
            return new AssessmentRaw.FileCache(path, BatchInvocationEvidence.DigestFile(path));
        }
        var assessed = AssessCommand.Run(new AssessRequest(seeded.FwDataPath,
            new SelectionRequest(false, [], ["motifa"], false, null)), NewManagedRoot(),
            assessor, NewInvoker(), null, CancellationToken.None);
        Assert.True(assessed.Succeeded, assessed.Refusal?.Message);
        var parseId = assessed.Value!.Measurements.Single(row => row.Kind == "ParseTime").AssessmentId;

        var byRule = TimingCommand.Timing(new TimingRequest(seeded.FwDataPath, parseId, By: "rule"));
        var shared = TimingCommand.Timing(new TimingRequest(seeded.FwDataPath, parseId, By: "rule",
            Rule: "Verb template"));
        var keyed = TimingCommand.Timing(new TimingRequest(seeded.FwDataPath, parseId, By: "rule",
            Rule: "mrule#1:Verb template"));

        Assert.True(byRule.Succeeded, byRule.Refusal?.Message);
        Assert.Equal(["mrule#1:Verb template", "mrule#0:Verb template"], byRule.Value!.Aggregates.Select(row => row.Key));
        Assert.Equal([0.5, 0.25], byRule.Value.Aggregates.Select(row => row.ShareOfWordTime!.Value));
        Assert.Equal(8, byRule.Value.Attribution.WordTimeMs);
        Assert.Equal(2, byRule.Value.Attribution.NotAttributedMs);
        Assert.False(shared.Succeeded);
        Assert.Equal("timing.ambiguous-rule", shared.Refusal!.Code);
        Assert.Contains("mrule#0:Verb template", shared.Refusal.Message, StringComparison.Ordinal);
        Assert.True(keyed.Succeeded, keyed.Refusal?.Message);
        Assert.Equal(4, Assert.Single(keyed.Value!.CostliestWords).SelfMs);
    }

    [Fact]
    public void MissingBatchStatisticsForASelectedWordRefusesTheAssessment()
    {
        using var seeded = NewSeededScratch();
        var cachePath = Path.Combine(_managedRootsParent, "missing-word-stats.sqlite");
        WriteStatsCache(cachePath, (SeededProject.AnalysedWordForm, 4, 1, 2, 2_000_000L));
        var assessor = new FakeAssessor("fake-assessor", CollectedKinds, kind =>
            kind == AssessmentKind.ObjectTiming
                ? new AssessmentRaw.FileCache(cachePath, BatchInvocationEvidence.DigestFile(cachePath))
                : new AssessmentRaw.Batch(new SIL.Motif.Host.Parser.BatchAnalysis(
                    [new(0, SeededProject.AnalysedWordForm, 5, SIL.Motif.Host.Parser.WordOutcome.Analysed, "sig"),
                     new(1, SeededProject.UnanalysedWordForm, 5, SIL.Motif.Host.Parser.WordOutcome.NoAnalysis, "-")],
                    1000, seeded.FwDataPath, []) { PerWordStepLimit = 200000 }))
        {
            CaptureEvidence = (scope, candidate) => FakeAssessmentEvidence.Capture(
                _managedRootsParent, scope, candidate),
        };
        var request = new SelectionRequest(false, [],
            [SeededProject.AnalysedWordForm, SeededProject.UnanalysedWordForm], false, null);
        var invoker = NewInvoker();

        var outcome = AssessCommand.Run(new AssessRequest(seeded.FwDataPath, request), NewManagedRoot(),
            assessor, invoker, null, CancellationToken.None);

        Assert.False(outcome.Succeeded);
        Assert.Equal("assess.parser-unavailable", outcome.Refusal!.Code);
        Assert.Contains(SeededProject.UnanalysedWordForm, outcome.Refusal.Message, StringComparison.Ordinal);
        Assert.Empty(invoker.Requests);
        var repository = OpenRepository(seeded.FwDataPath);
        Assert.Empty(repository.ListBaselineAssessments(AssessmentKind.ParseTime.ToStoredKind()));
        Assert.Empty(repository.ListBaselineAssessments(AssessmentKind.ObjectTiming.ToStoredKind()));
        using var database = OpenDatabase(seeded.FwDataPath);
        var project = new ProjectLocator(Path.GetFullPath(seeded.FwDataPath),
            Path.GetFileNameWithoutExtension(seeded.FwDataPath));
        Assert.Empty(new RetainedInvocationRepository(database).List(ProjectWorkspaceKey.Compute(project)));
    }

    [Fact]
    public void MissingInvocationEvidenceRefusesBeforeRecordingAssessments()
    {
        using var seeded = NewSeededScratch();

        var outcome = AssessCommand.Run(
            new AssessRequest(seeded.FwDataPath, AllWordforms), NewManagedRoot(),
            new FakeAssessor("missing-evidence", CollectedKinds), NewInvoker(),
            onProgress: null, CancellationToken.None);

        Assert.False(outcome.Succeeded);
        Assert.Equal("assess.invocation-inconsistent", outcome.Refusal!.Code);
        var repository = OpenRepository(seeded.FwDataPath);
        Assert.Empty(repository.ListBaselineAssessments(AssessmentKind.ParseTime.ToStoredKind()));
        Assert.Empty(repository.ListBaselineAssessments(AssessmentKind.ObjectTiming.ToStoredKind()));
    }

    [Fact]
    public void InvocationEvidenceProducesAQueryableRetainedAggregateWithSelectionDescriptor()
    {
        using var seeded = NewSeededScratch();
        var assessor = new FakeAssessor("fake-assessor", CollectedKinds)
        {
            CaptureEvidence = (scope, candidate) => FakeAssessmentEvidence.Capture(_managedRootsParent, scope, candidate)
        };

        var outcome = AssessCommand.Run(
            new AssessRequest(seeded.FwDataPath,
                new SelectionRequest(false, [], ["motifa"], false, null)), NewManagedRoot(), assessor,
            NewInvoker(), onProgress: null, CancellationToken.None);

        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        var response = outcome.Value!;
        Assert.NotEmpty(response.InvocationId);
        Assert.All(response.Measurements, measurement =>
            Assert.Equal(response.InvocationId, measurement.InvocationId));
        Assert.NotNull(response.SelectionDescriptor);
        Assert.Equal(["motifa"], response.SelectionDescriptor!.PastedWords);

        var project = new ProjectLocator(Path.GetFullPath(seeded.FwDataPath),
            Path.GetFileNameWithoutExtension(seeded.FwDataPath));
        using var database = MotifDatabase.OpenOwned(
            ProjectDatabaseCatalog.DatabasePathFor(project), project, MotifSchema.CurrentSchema, new Version(1, 0));
        var retained = new RetainedInvocationRepository(database).Get(response.InvocationId!);
        Assert.Equal(response.InvocationId, retained.InvocationId);
        Assert.Equal(2, retained.Members.Count);
        Assert.Equal(2, retained.Assessments.Count);
        Assert.All(retained.Assessments, assessment =>
            Assert.Equal(response.InvocationId, assessment.Invocation!.InvocationId));
        Assert.All(retained.Assessments, assessment =>
            Assert.Equal(assessment.Invocation!.SourceBytesSha256, assessment.GrammarSourceSha256));
        var listed = new RetainedInvocationRepository(database).List(retained.ProjectKey);
        Assert.All(listed.Single().Assessments, assessment => Assert.Null(assessment.Words));
        Assert.Equal(response.Baseline.Token, retained.BaselineToken);
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

        // Object self times nest without overlap, so with each word's own nanoseconds they never exceed it.
        var parseId = response.Measurements.Single(item => item.Kind == "ParseTime").AssessmentId;
        var timing = TimingCommand.Timing(new TimingRequest(cache.ProjectId.Path, parseId, By: "kind"));
        Assert.True(timing.Succeeded, timing.Refusal?.Message);
        var attribution = timing.Value!.Attribution;
        Assert.Equal(3, attribution.MeasuredWordCount);
        Assert.True(attribution.AttributedMs > 0);
        Assert.False(attribution.Overrun, $"objects overran their words by {attribution.OverrunMs} ms");
        Assert.Equal(1, timing.Value.Aggregates.Sum(row => row.ShareOfWordTime!.Value) +
            attribution.NotAttributedShare!.Value, precision: 9);
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
                 : new AssessmentRaw.WordMeasurements([]))
        {
            CaptureEvidence = (scope, candidate) => FakeAssessmentEvidence.Capture(
                _managedRootsParent, scope, candidate)
        };
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

    [Theory]
    [InlineData(null, null, null, 500000, 20_000)]
    [InlineData(4000, 150000, null, 150000, 4000)]
    [InlineData(4000, 150000, 750000, 750000, 4000)]
    public void TheRunUsesRequestLimitsBeforeProjectConfiguredLimits(
        int? requestedMs, int? selectionStepLimit, int? requestStepLimit, int expectedStepLimit, int expectedMs)
    {
        using var seeded = NewSeededScratch();
        var configured = new SIL.Motif.Host.Config.ProjectConfiguration(
            [new SIL.Motif.Host.Config.AssessmentScopeConfiguration(
                SIL.Motif.Host.Config.AssessmentScopeConfiguration.DefaultName, "all", "pangloss", [],
                TimeSpan.FromMilliseconds(2500), perWordStepLimit: 500000)],
            gateOnRegression: false, purgeOnApply: true);
        File.WriteAllText(Path.ChangeExtension(seeded.FwDataPath, ".motif.toml"),
            SIL.Motif.Host.Config.ProjectConfigurationFile.Render(configured));
        AssessmentScope? seen = null;
        var assessor = new FakeAssessor("fake-assessor", CollectedKinds, kind =>
            kind == AssessmentKind.ParseTime
                ? new AssessmentRaw.Batch(new SIL.Motif.Host.Parser.BatchAnalysis(
                    [new(0, "motifa", 12, SIL.Motif.Host.Parser.WordOutcome.Analysed, "complete-match")],
                    requestedMs ?? 2500, seeded.FwDataPath, []) { PerWordStepLimit = expectedStepLimit })
                : new AssessmentRaw.WordMeasurements([]))
        {
            CaptureEvidence = (scope, candidate) =>
            {
                seen = scope;
                return FakeAssessmentEvidence.Capture(_managedRootsParent, scope, candidate);
            }
        };

        var outcome = AssessCommand.Run(new AssessRequest(seeded.FwDataPath,
            new SelectionRequest(false, [], ["motifa"], false, null,
                PerWordStepLimit: selectionStepLimit is { } selectionSteps ? new StepCap(selectionSteps) : null),
            requestedMs,
            requestStepLimit is { } requestSteps ? new StepCap(requestSteps) : null), NewManagedRoot(),
            assessor, NewInvoker(), null, CancellationToken.None);

        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        Assert.Equal(TimeSpan.FromMilliseconds(expectedMs), seen!.PerWordLimit);
        Assert.Equal(expectedStepLimit, seen.PerWordStepLimit);
    }

    [Fact]
    public void AWordWithReadingsThatAlsoHitALimitCountsAsIncomplete()
    {
        using var seeded = NewSeededScratch();
        var assessor = new FakeAssessor("fake-assessor", CollectedKinds, kind =>
            kind == AssessmentKind.ParseTime
                ? new AssessmentRaw.Batch(new SIL.Motif.Host.Parser.BatchAnalysis(
                    [new(0, "motifa", 1010, SIL.Motif.Host.Parser.WordOutcome.Analysed, "found-then-stopped")
                    {
                        Morphology = new ParseWordEvidence(
                            SIL.Motif.Host.Parser.ParseMorphEvidence.Schema, 0, "motifa", 1010, false, true, false,
                            [new ParseAnalysis([
                                new("11111111-1111-1111-1111-111111111111",
                                    "22222222-2222-2222-2222-222222222222", null, null)])], [])
                    },
                     new(1, "motifb", 12, SIL.Motif.Host.Parser.WordOutcome.Analysed, "complete-match")],
                    1000, seeded.FwDataPath, []) { PerWordStepLimit = 200000 })
                : new AssessmentRaw.WordMeasurements([]))
        {
            CaptureEvidence = (scope, candidate) => FakeAssessmentEvidence.Capture(
                _managedRootsParent, scope, candidate)
        };
        var outcome = AssessCommand.Run(new AssessRequest(seeded.FwDataPath,
            new SelectionRequest(false, [], ["motifa", "motifb"], false, null)), NewManagedRoot(),
            assessor, NewInvoker(), null, CancellationToken.None);

        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        var response = outcome.Value!;
        // The readings it found are real, but the search stopped early, so more may exist: Statistics agrees.
        Assert.Equal("analysed", response.Words[0].Outcome);
        Assert.True(response.Words[0].IsIncomplete);
        Assert.Equal("INCOMPLETE — parsing did not finish (time limit)", response.Words[0].CompletionStatus);
        Assert.StartsWith("1 search completed; 1 incomplete", response.CompletionSummary);
    }

    [Fact]
    public void ResponseRetainsOrderedReadingsPartialEvidenceAndSharedGrammarWarnings()
    {
        using var seeded = NewSeededScratch();
        var assessor = new FakeAssessor("fake-assessor", CollectedKinds, kind =>
            kind == AssessmentKind.ParseTime
                ? new AssessmentRaw.Batch(new SIL.Motif.Host.Parser.BatchAnalysis(
                    [new(0, "motifa", 15, SIL.Motif.Host.Parser.WordOutcome.Analysed, "reading")
                    {
                        Morphology = new ParseWordEvidence(
                            SIL.Motif.Host.Parser.ParseMorphEvidence.Schema, 0, "motifa", 15, false, false, false,
                            [
                                new ParseAnalysis([
                                    new("11111111-1111-1111-1111-111111111111",
                                        "22222222-2222-2222-2222-222222222222", null, "guess-a"),
                                    new("33333333-3333-3333-3333-333333333333",
                                        "44444444-4444-4444-4444-444444444444",
                                        "55555555-5555-5555-5555-555555555555", null)]),
                                new ParseAnalysis([
                                    new("66666666-6666-6666-6666-666666666666",
                                        "77777777-7777-7777-7777-777777777777", null, null),
                                ]),
                            ], [])
                    },
                    new(1, "motifb", 700, SIL.Motif.Host.Parser.WordOutcome.Capped, "partial")
                    {
                        Morphology = new ParseWordEvidence(
                            SIL.Motif.Host.Parser.ParseMorphEvidence.Schema, 1, "motifb", 700, true, false, false,
                            [new ParseAnalysis([
                                new("88888888-8888-8888-8888-888888888888",
                                    "99999999-9999-9999-9999-999999999999", null, null),
                            ])], [])
                    },
                    new(2, "motifc", 5, SIL.Motif.Host.Parser.WordOutcome.Analysed, "unavailable")
                    {
                        Morphology = new ParseWordEvidence(
                            SIL.Motif.Host.Parser.ParseMorphEvidence.Schema, 2, "motifc", 5, false, false, false, [],
                            ["source identity unavailable"])
                    },
                    new(3, "motifd", 5, SIL.Motif.Host.Parser.WordOutcome.NoAnalysis, "-") ,
                    new(4, "motife", 0, SIL.Motif.Host.Parser.WordOutcome.Skipped, "invalid")
                    {
                        Morphology = new ParseWordEvidence(
                            SIL.Motif.Host.Parser.ParseMorphEvidence.Schema, 4, "motife", 0,
                            false, false, true, [], [])
                    }], 1000, seeded.FwDataPath, [])
                    { PerWordStepLimit = 200000 })
                : new AssessmentRaw.WordMeasurements([]))
        {
            CaptureEvidence = (scope, candidate) => FakeAssessmentEvidence.Capture(
                _managedRootsParent, scope, candidate) with
            {
                GrammarWarnings = "warning: dropped allomorph\ncapability: missing boundary marker",
            },
        };

        var outcome = AssessCommand.Run(new AssessRequest(seeded.FwDataPath,
            new SelectionRequest(false, [], ["motifa", "motifb", "motifc", "motifd", "motife"], false, null)),
            NewManagedRoot(), assessor, NewInvoker(), null, CancellationToken.None);

        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        var response = outcome.Value!;
        Assert.Equal(["warning: dropped allomorph", "capability: missing boundary marker"], response.GrammarWarnings);
        var morphology = response.Words[0].Morphology!;
        Assert.Equal(2, morphology.Analyses.Count);
        Assert.Equal(2, morphology.Analyses[0].Morphs.Count);
        Assert.Equal(
            ["11111111-1111-1111-1111-111111111111", "33333333-3333-3333-3333-333333333333",
                "66666666-6666-6666-6666-666666666666"],
            morphology.Analyses.SelectMany(analysis => analysis.Morphs)
                .Select(morph => morph.Form));
        Assert.Equal("guess-a", morphology.Analyses[0].Morphs[0].GuessedString);
        Assert.True(response.Words[1].IsIncomplete);
        Assert.Single(response.Words[1].Morphology!.Analyses);
        Assert.Equal("source identity unavailable", response.Words[2].Morphology!.Unavailable.Single());
        Assert.Equal("analysed", response.Words[2].Outcome);
        Assert.Equal("Morphology evidence unavailable.", response.Words[3].EvidenceStatus);
        Assert.Equal("Morphology evidence unavailable: invalid shape.", response.Words[4].EvidenceStatus);
    }

    [Fact]
    public void AReadingMatchingACandidateIsGradedCandidate()
    {
        using var seeded = NewSeededScratch();
        string firstMorph, firstMsa;
        IReadOnlyList<ApprovedMorphology> approvedExpectations;
        using (var cache = new FwDataProjectLoader().LoadScratchCache(seeded.FwDataPath))
        {
            var wordform = cache.ServiceLocator.GetInstance<IWfiWordformRepository>().AllInstances()
                .Single(w => w.Form.VernacularDefaultWritingSystem?.Text == SeededProject.AnalysedWordForm);
            var approved = wordform.HumanApprovedAnalyses.Single();
            firstMorph = approved.MorphBundlesOS[0].MorphRA!.Guid.ToString("D");
            firstMsa = approved.MorphBundlesOS[0].MsaRA!.Guid.ToString("D");

            // A second analysis nobody has approved or rejected: a candidate.
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            {
                var candidate = cache.ServiceLocator.GetInstance<IWfiAnalysisFactory>().Create();
                wordform.AnalysesOC.Add(candidate);
                var bundle = cache.ServiceLocator.GetInstance<IWfiMorphBundleFactory>().Create();
                candidate.MorphBundlesOS.Add(bundle);
                bundle.MorphRA = approved.MorphBundlesOS[0].MorphRA;
                bundle.MsaRA = approved.MorphBundlesOS[0].MsaRA;
            });
            new FwDataProjectLoader().Save(cache);
            approvedExpectations = ApprovedMorphologyReader.Read(cache)[SeededProject.AnalysedWordForm];
            Assert.Single(ApprovedMorphologyReader.ReadCandidates(cache)[SeededProject.AnalysedWordForm]);
        }

        var morphology = new ParseWordEvidence(
            SIL.Motif.Host.Parser.ParseMorphEvidence.Schema, 0, SeededProject.AnalysedWordForm, 5, false, false, false,
            [new ParseAnalysis([new ParseMorph(firstMorph, firstMsa, null, null)])], []);
        var correctness = SIL.Motif.Host.Parser.MorphologyCorrectness.Compare(morphology, approvedExpectations);
        var assessor = new FakeAssessor("fake-assessor", CollectedKinds, kind =>
            kind == AssessmentKind.ParseTime
                ? new AssessmentRaw.Batch(new SIL.Motif.Host.Parser.BatchAnalysis(
                    [new(0, SeededProject.AnalysedWordForm, 5, SIL.Motif.Host.Parser.WordOutcome.Analysed, "sig")
                        { Morphology = morphology, Correctness = correctness }],
                    1000, seeded.FwDataPath, []) { PerWordStepLimit = 200000 })
                : new AssessmentRaw.WordMeasurements([]))
        {
            CaptureEvidence = (scope, candidate) => FakeAssessmentEvidence.Capture(_managedRootsParent, scope, candidate),
        };

        var outcome = AssessCommand.Run(new AssessRequest(seeded.FwDataPath,
            new SelectionRequest(false, [], [SeededProject.AnalysedWordForm], false, null)),
            NewManagedRoot(), assessor, NewInvoker(), null, CancellationToken.None);

        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        var word = Assert.Single(outcome.Value!.Words);
        Assert.Equal(["candidate"], word.ReadingGrades);
        // The word still stands as approved: an approval outranks a candidate.
        Assert.Equal(ProjectStanding.Approved, word.ProjectStanding);
    }

    [Fact]
    public void ReadingsAreGradedAndMissedApprovedListsWhatTheParserDidNotProduce()
    {
        using var seeded = NewSeededScratch();
        string firstMorph, firstMsa, secondMorph, secondMsa;
        var expectedAnalysisId = string.Empty;
        IReadOnlyList<ApprovedMorphology> approvedExpectations;
        using (var cache = new FwDataProjectLoader().LoadScratchCache(seeded.FwDataPath))
        {
            var wordform = cache.ServiceLocator.GetInstance<IWfiWordformRepository>().AllInstances()
                .Single(w => w.Form.VernacularDefaultWritingSystem?.Text == SeededProject.AnalysedWordForm);
            var approved = wordform.HumanApprovedAnalyses.Single();
            expectedAnalysisId = CanonicalId.FromGuid(approved.Guid).Value;
            firstMorph = approved.MorphBundlesOS[0].MorphRA!.Guid.ToString("D");
            firstMsa = approved.MorphBundlesOS[0].MsaRA!.Guid.ToString("D");
            secondMorph = approved.MorphBundlesOS[1].MorphRA!.Guid.ToString("D");
            secondMsa = approved.MorphBundlesOS[1].MsaRA!.Guid.ToString("D");

            // A second analysis on the same wordform, explicitly disapproved rather than left with no opinion.
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            {
                var disapproved = cache.ServiceLocator.GetInstance<IWfiAnalysisFactory>().Create();
                wordform.AnalysesOC.Add(disapproved);
                var bundle = cache.ServiceLocator.GetInstance<IWfiMorphBundleFactory>().Create();
                disapproved.MorphBundlesOS.Add(bundle);
                bundle.MorphRA = approved.MorphBundlesOS[0].MorphRA;
                bundle.MsaRA = approved.MorphBundlesOS[0].MsaRA;
                cache.LangProject.DefaultUserAgent.SetEvaluation(disapproved, Opinions.disapproves);
            });
            new FwDataProjectLoader().Save(cache);
            approvedExpectations = ApprovedMorphologyReader.Read(cache)[SeededProject.AnalysedWordForm];
        }

        var morphology = new ParseWordEvidence(
            SIL.Motif.Host.Parser.ParseMorphEvidence.Schema, 0, SeededProject.AnalysedWordForm, 5, false, false, false,
            [
                new ParseAnalysis([new ParseMorph(firstMorph, firstMsa, null, null)]),
                new ParseAnalysis([new ParseMorph(
                    "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb", null, null)]),
            ], []);
        var correctness = SIL.Motif.Host.Parser.MorphologyCorrectness.Compare(morphology, approvedExpectations);
        var assessor = new FakeAssessor("fake-assessor", CollectedKinds, kind =>
            kind == AssessmentKind.ParseTime
                ? new AssessmentRaw.Batch(new SIL.Motif.Host.Parser.BatchAnalysis(
                    [new(0, SeededProject.AnalysedWordForm, 5, SIL.Motif.Host.Parser.WordOutcome.Analysed, "sig")
                        { Morphology = morphology, Correctness = correctness }],
                    1000, seeded.FwDataPath, []) { PerWordStepLimit = 200000 })
                : new AssessmentRaw.WordMeasurements([]))
        {
            CaptureEvidence = (scope, candidate) => FakeAssessmentEvidence.Capture(_managedRootsParent, scope, candidate),
        };

        var outcome = AssessCommand.Run(new AssessRequest(seeded.FwDataPath,
            new SelectionRequest(false, [], [SeededProject.AnalysedWordForm], false, null)),
            NewManagedRoot(), assessor, NewInvoker(), null, CancellationToken.None);

        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        var word = Assert.Single(outcome.Value!.Words);
        Assert.Equal(["disapproved", "no-opinion"], word.ReadingGrades);
        Assert.Equal(ProjectStanding.Approved, word.ProjectStanding);
        var missed = Assert.Single(word.MissedApproved!);
        Assert.NotNull(word.FixFirst);
        Assert.Equal(2, missed.Morphs.Count);
        Assert.Equal(SeededProject.FirstForm, missed.Morphs[0].Form);
        Assert.Equal(SeededProject.FirstGloss, missed.Morphs[0].Gloss);
        Assert.Equal(SeededProject.SecondForm, missed.Morphs[1].Form);
        Assert.Equal(SeededProject.SecondGloss, missed.Morphs[1].Gloss);
        Assert.Equal(expectedAnalysisId, missed.StoredAnalysisId);
        Assert.Equal(expectedAnalysisId, word.ExpectedAnalysis!.StoredAnalysisId);
        Assert.Equal("approved", word.ExpectedAnalysis.StoredAnalysisOpinion);
    }

    [Fact]
    public void ExpectedAnalysisUsesTheUniqueStoredCandidateWhenNoAnalysisIsApproved()
    {
        using var seeded = NewSeededScratch();
        var expectedAnalysisId = string.Empty;
        using (var cache = new FwDataProjectLoader().LoadScratchCache(seeded.FwDataPath))
        {
            var wordforms = cache.ServiceLocator.GetInstance<IWfiWordformRepository>().AllInstances();
            var wordform = wordforms.Single(item =>
                item.Form.VernacularDefaultWritingSystem?.Text == SeededProject.UnanalysedWordForm);
            var source = wordforms.Single(item =>
                item.Form.VernacularDefaultWritingSystem?.Text == SeededProject.AnalysedWordForm)
                .HumanApprovedAnalyses.Single();
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            {
                var candidate = cache.ServiceLocator.GetInstance<IWfiAnalysisFactory>().Create();
                wordform.AnalysesOC.Add(candidate);
                var bundle = cache.ServiceLocator.GetInstance<IWfiMorphBundleFactory>().Create();
                candidate.MorphBundlesOS.Add(bundle);
                bundle.MorphRA = source.MorphBundlesOS[0].MorphRA;
                bundle.MsaRA = source.MorphBundlesOS[0].MsaRA;
                expectedAnalysisId = CanonicalId.FromGuid(candidate.Guid).Value;
            });
            new FwDataProjectLoader().Save(cache);
        }

        var morphology = new ParseWordEvidence(
            SIL.Motif.Host.Parser.ParseMorphEvidence.Schema, 0, SeededProject.UnanalysedWordForm, 5,
            false, false, false, [], []);
        var assessor = new FakeAssessor("fake-assessor", CollectedKinds, kind =>
            kind == AssessmentKind.ParseTime
                ? new AssessmentRaw.Batch(new SIL.Motif.Host.Parser.BatchAnalysis(
                    [new(0, SeededProject.UnanalysedWordForm, 5, SIL.Motif.Host.Parser.WordOutcome.NoAnalysis, "sig")
                        { Morphology = morphology }],
                    1000, seeded.FwDataPath, []) { PerWordStepLimit = 200000 })
                : new AssessmentRaw.WordMeasurements([]))
        {
            CaptureEvidence = (scope, candidate) => FakeAssessmentEvidence.Capture(_managedRootsParent, scope, candidate),
        };

        var outcome = AssessCommand.Run(new AssessRequest(seeded.FwDataPath,
                new SelectionRequest(false, [], [SeededProject.UnanalysedWordForm], false, null)),
            NewManagedRoot(), assessor, NewInvoker(), null, CancellationToken.None);

        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        var expected = Assert.Single(outcome.Value!.Words).ExpectedAnalysis;
        Assert.Equal(expectedAnalysisId, expected!.StoredAnalysisId);
        Assert.Equal("candidate", expected.StoredAnalysisOpinion);
    }

    [Fact]
    public void AttemptsAndPassesComeFromBatchStatisticsForEverySelectedWord()
    {
        using var seeded = NewSeededScratch();
        Assert.True(SelectionCommands.SetDefault(new SetDefaultSelectionRequest(seeded.FwDataPath,
            "Default", [], [SeededProject.AnalysedWordForm, SeededProject.UnanalysedWordForm])).Succeeded);
        var cachePath = Path.Combine(_managedRootsParent, "attempts-passes.bin");
        WriteStatsCache(cachePath,
            (SeededProject.AnalysedWordForm, 42, 7, 4, 3_000_000L),
            (SeededProject.UnanalysedWordForm, 3, 0, 0, 0L));
        var assessor = new FakeAssessor("fake-assessor", CollectedKinds, kind =>
            kind == AssessmentKind.ParseTime
                ? new AssessmentRaw.Batch(new SIL.Motif.Host.Parser.BatchAnalysis(
                    [new(0, SeededProject.AnalysedWordForm, 5, SIL.Motif.Host.Parser.WordOutcome.Analysed, "sig"),
                     new(1, SeededProject.UnanalysedWordForm, 5, SIL.Motif.Host.Parser.WordOutcome.NoAnalysis, "-")],
                    1000, seeded.FwDataPath, []) { PerWordStepLimit = 200000 })
                : new AssessmentRaw.FileCache(cachePath, BatchInvocationEvidence.DigestFile(cachePath)))
        {
            CaptureEvidence = (scope, candidate) => FakeAssessmentEvidence.Capture(_managedRootsParent, scope, candidate),
        };
        var invoker = NewInvoker();

        var outcome = AssessCommand.Run(new AssessRequest(seeded.FwDataPath,
            new SelectionRequest(false, [], [SeededProject.AnalysedWordForm, SeededProject.UnanalysedWordForm], false, null)),
            NewManagedRoot(), assessor, invoker, null, CancellationToken.None);

        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        var analysed = outcome.Value!.Words.Single(word => word.Word == SeededProject.AnalysedWordForm);
        Assert.Equal(42, analysed.Attempts);
        Assert.Equal(7, analysed.Passes);
        var unanalysed = outcome.Value.Words.Single(word => word.Word == SeededProject.UnanalysedWordForm);
        Assert.Equal(3, unanalysed.Attempts);
        Assert.Equal(0, unanalysed.Passes);
        var reopened = CurrentEvidenceQuery.ReadCurrentEvidence(seeded.FwDataPath);
        Assert.True(reopened.Succeeded, reopened.Refusal?.Message);
        Assert.Equal(outcome.Value.Words.Select(word => (word.Word, word.Attempts, word.Passes)),
            reopened.Value!.Assessment!.Words.Select(word => (word.Word, word.Attempts, word.Passes)));
        Assert.Empty(invoker.Requests);
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
        Assert.Equal(FailureReason.Cancelled, outcome.Refusal.Reason);

        var repository = OpenRepository(seeded.FwDataPath);
        Assert.Empty(repository.ListBaselineAssessments(AssessmentKind.ParseTime.ToStoredKind()));
        Assert.Empty(repository.ListBaselineAssessments(AssessmentKind.ObjectTiming.ToStoredKind()));
    }

    [Fact]
    public void CancellingDefaultSelectionAtCompletionDoesNotRetainTheInvocation()
    {
        using var seeded = NewSeededScratch();
        var saved = SelectionCommands.SetDefault(new SetDefaultSelectionRequest(
            seeded.FwDataPath, "Default", [], [SeededProject.AnalysedWordForm], 1000, StepCap.Default));
        Assert.True(saved.Succeeded, saved.Refusal?.Message);
        using var cancellation = new CancellationTokenSource();
        var outcome = AssessCommand.Run(
            new AssessRequest(seeded.FwDataPath, null), NewManagedRoot(), NewAssessor(), NewInvoker(),
            progress =>
            {
                if (progress.Stage == AssessmentStage.Complete) cancellation.Cancel();
            }, cancellation.Token);

        Assert.False(outcome.Succeeded);
        Assert.Equal("assessment.cancelled", outcome.Refusal!.Code);
        using var database = OpenDatabase(seeded.FwDataPath);
        var project = new ProjectLocator(
            Path.GetFullPath(seeded.FwDataPath), Path.GetFileNameWithoutExtension(seeded.FwDataPath));
        Assert.Empty(new RetainedInvocationRepository(database).List(ProjectWorkspaceKey.Compute(project)));
        Assert.Empty(new AssessmentRepository(database).ListBaselineAssessments(AssessmentKind.ParseTime.ToStoredKind()));
        Assert.Empty(new AssessmentRepository(database).ListBaselineAssessments(AssessmentKind.ObjectTiming.ToStoredKind()));
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

    [Fact]
    public void ParserDiscoveryFailureIsRefusedThroughTheLazyAssessor()
    {
        using var seeded = NewSeededScratch();
        var assessor = new LazyPanGlossAssessor(() =>
            throw new SIL.Motif.Host.Parser.ParserUnavailableException("parser absent"));
        var stages = new List<AssessmentStage>();

        var outcome = AssessCommand.Run(
            new AssessRequest(seeded.FwDataPath, AllWordforms), NewManagedRoot(), assessor, NewInvoker(),
            progress => stages.Add(progress.Stage), CancellationToken.None);

        Assert.Equal("assess.parser-unavailable", outcome.Refusal!.Code);
        Assert.Contains("parser absent", outcome.Refusal.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(AssessmentStage.Parsing, stages);
        Assert.Empty(OpenRepository(seeded.FwDataPath).ListBaselineAssessments(AssessmentKind.ParseTime.ToStoredKind()));
    }

    [Fact]
    public void AnAssessmentWithNoParserRefusesBeforeReportingCaptureProgress()
    {
        using var seeded = NewSeededScratch();
        var stages = new List<AssessmentStage>();

        var outcome = AssessCommand.Assess(
            new AssessRequest(seeded.FwDataPath, AllWordforms), NewManagedRoot(),
            parserPath: null, progress => stages.Add(progress.Stage), CancellationToken.None);

        Assert.Equal("assess.parser-unavailable", outcome.Refusal!.Code);
        Assert.Empty(stages);
    }

    private static readonly IReadOnlyList<AssessmentKind> CollectedKinds =
        [AssessmentKind.ParseTime, AssessmentKind.ObjectTiming];

    private FakeAssessor NewAssessor() => new("fake-assessor", CollectedKinds)
    {
        CaptureEvidence = (scope, candidate) => FakeAssessmentEvidence.Capture(
            _managedRootsParent, scope, candidate),
    };

    // Answers every stats request with fixed rows; no test here asserts on the summary's content.
    private static FakeInvoker NewInvoker() => new()
    {
        Respond = _ => new PanGlossOutcome.Completed("fake stats", string.Empty, TimeSpan.Zero),
    };

    // Assesses the seeded Text's two words with the analysed one timed under the stats cache's one object.
    private SIL.Motif.Contract.Commands.CommandOutcome<AssessCommandResponse> AssessForUses(SeededScratch seeded,
        string objectKind = "morph_rule", string objectKey = "mrule#0:Verb template")
    {
        Assert.True(SelectionCommands.SetDefault(new SetDefaultSelectionRequest(
            seeded.FwDataPath, "Default", [seeded.Seeded.TextId], [])).Succeeded);
        var cachePath = Path.Combine(_managedRootsParent, "uses-stats.sqlite");
        WriteStatsCache(cachePath, objectKind, objectKey,
            (SeededProject.AnalysedWordForm, 4, 0, 2, 2_000_000L),
            (SeededProject.UnanalysedWordForm, 3, 0, 0, 0L));
        var cacheDigest = BatchInvocationEvidence.DigestFile(cachePath);
        var assessor = new FakeAssessor("fake-assessor", CollectedKinds, kind => kind switch
        {
            AssessmentKind.ParseTime => new AssessmentRaw.Batch(new SIL.Motif.Host.Parser.BatchAnalysis(
                [new(0, SeededProject.AnalysedWordForm, 9, SIL.Motif.Host.Parser.WordOutcome.NoAnalysis, "none"),
                 new(1, SeededProject.UnanalysedWordForm, 15, SIL.Motif.Host.Parser.WordOutcome.NoAnalysis, "none")],
                1000, seeded.FwDataPath, []) { PerWordStepLimit = StepCap.Default }),
            AssessmentKind.ObjectTiming => new AssessmentRaw.FileCache(cachePath, cacheDigest),
            _ => new AssessmentRaw.WordMeasurements([]),
        })
        {
            CaptureEvidence = (scope, candidate) => FakeAssessmentEvidence.Capture(_managedRootsParent, scope, candidate),
        };
        return AssessCommand.Run(new AssessRequest(seeded.FwDataPath), NewManagedRoot(), assessor,
            new FakeInvoker(), null, CancellationToken.None);
    }

    private static void WriteStatsCache(string path,
        params (string Word, int Attempts, int Passes, int ObjectAttempts, long SelfTimeNs)[] words) =>
        WriteStatsCache(path, 0L, words);

    private static void WriteStatsCache(string path, string objectKind, string objectKey,
        params (string Word, int Attempts, int Passes, int ObjectAttempts, long SelfTimeNs)[] words)
    {
        WriteStatsCache(path, 0L, words);
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false,
        }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE object SET kind = $kind, key = $key, " +
            "identity_quality = CASE WHEN $kind = 'lex_entry' THEN 'authored' ELSE identity_quality END";
        command.Parameters.AddWithValue("$kind", objectKind);
        command.Parameters.AddWithValue("$key", objectKey);
        command.ExecuteNonQuery();
    }

    private static void WriteStatsCache(string path, long elapsedNs,
        params (string Word, int Attempts, int Passes, int ObjectAttempts, long SelfTimeNs)[] words)
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
            INSERT INTO object VALUES (1, 'mrule#0:Verb template', 'morph_rule', 'Verb template', 'structural');
            """;
        command.ExecuteNonQuery();
        foreach (var (word, attempts, passes, objectAttempts, selfTimeNs) in words)
        {
            using var insert = connection.CreateCommand();
            insert.CommandText = """
                INSERT INTO word (form, elapsed_ns, attempts, passes, capped, timed_out, invalid_shape)
                VALUES ($form, $elapsedNs, $attempts, $passes, 0, 0, 0);
                """;
            insert.Parameters.AddWithValue("$form", word);
            insert.Parameters.AddWithValue("$elapsedNs", elapsedNs);
            insert.Parameters.AddWithValue("$attempts", attempts);
            insert.Parameters.AddWithValue("$passes", passes);
            insert.ExecuteNonQuery();
            if (objectAttempts == 0 && selfTimeNs == 0) continue;
            using var fact = connection.CreateCommand();
            fact.CommandText = """
                INSERT INTO fact (word_id, object_id, direction, attempts, self_time_ns)
                SELECT word_id, 1, 'analysis', $attempts, $self_time_ns FROM word WHERE form = $form;
                """;
            fact.Parameters.AddWithValue("$form", word);
            fact.Parameters.AddWithValue("$attempts", objectAttempts);
            fact.Parameters.AddWithValue("$self_time_ns", selfTimeNs);
            fact.ExecuteNonQuery();
        }
    }

    // SeedText's wordforms must be saved to disk for AssessCommand's own scratch load to see them.
    private SeededScratch NewSeededScratch()
    {
        var cache = _pristine.NewScratch();
        var text = SeededProject.SeedText(cache, _pristine.Seed);
        new FwDataProjectLoader().Save(cache);
        return new SeededScratch(cache, text);
    }

    private sealed class SeededScratch(LcmCache cache, SeededText seeded) : IDisposable
    {
        public string FwDataPath => cache.ProjectId.Path;
        public SeededText Seeded => seeded;

        public void Dispose() => cache.Dispose();
    }

    private static IAssessmentRepository OpenRepository(string fwDataPath)
    {
        var database = OpenDatabase(fwDataPath);
        return new AssessmentRepository(database);
    }

    private static MotifDatabase OpenDatabase(string fwDataPath)
    {
        var project = new ProjectLocator(Path.GetFullPath(fwDataPath), Path.GetFileNameWithoutExtension(fwDataPath));
        var databasePath = ProjectDatabaseCatalog.DatabasePathFor(project);
        return MotifDatabase.OpenOwned(databasePath, project, MotifSchema.CurrentSchema, new Version(1, 0));
    }

    private string NewManagedRoot()
    {
        var root = Path.Combine(_managedRootsParent, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
