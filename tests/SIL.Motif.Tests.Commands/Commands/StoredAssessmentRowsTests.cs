using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using SIL.LCModel;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Assess;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Analysis;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Parser;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary>
/// Pins that reopening a project shows the words a run returned: <see cref="CurrentEvidenceQuery"/> returns the
/// stored Assessment in the shape <see cref="AssessCommand"/> returns it, built by the same row function.
/// </summary>
[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group2)]
public sealed class StoredAssessmentRowsTests : IDisposable
{
    private static readonly IReadOnlyList<AssessmentKind> CollectedKinds =
        [AssessmentKind.ParseTime, AssessmentKind.ObjectTiming];

    private readonly PristineProjectFixture _pristine;
    private readonly string _managedRootsParent =
        Path.Combine(Path.GetTempPath(), "SIL.Motif.StoredAssessmentRowsTests", Guid.NewGuid().ToString("N"));

    public StoredAssessmentRowsTests(PristineProjectFixture pristine)
    {
        _pristine = pristine;
        Directory.CreateDirectory(_managedRootsParent);
    }

    public void Dispose()
    {
        try { Directory.Delete(_managedRootsParent, recursive: true); }
        catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StoredRowsEqualTheRunRows(bool mixedSelection)
    {
        using var cache = _pristine.NewScratch();
        var text = SeededProject.SeedText(cache, _pristine.Seed);
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var wordform = cache.ServiceLocator.GetInstance<IWfiWordformRepository>().AllInstances()
                .Single(word => word.Form.VernacularDefaultWritingSystem.Text == SeededProject.AnalysedWordForm);
            var approvedAnalysis = wordform.HumanApprovedAnalyses.Single();
            foreach (var opinion in new[] { Opinions.disapproves, Opinions.noopinion })
            {
                var analysis = cache.ServiceLocator.GetInstance<IWfiAnalysisFactory>().Create();
                wordform.AnalysesOC.Add(analysis);
                analysis.CategoryRA = approvedAnalysis.CategoryRA;
                foreach (var source in approvedAnalysis.MorphBundlesOS)
                {
                    var bundle = cache.ServiceLocator.GetInstance<IWfiMorphBundleFactory>().Create();
                    analysis.MorphBundlesOS.Add(bundle);
                    bundle.MorphRA = source.MorphRA;
                    bundle.MsaRA = source.MsaRA;
                }
                cache.LangProject.DefaultUserAgent.SetEvaluation(analysis, opinion);
            }
            if (mixedSelection)
                cache.ServiceLocator.GetInstance<ITextRepository>().GetObject(text.TextId)
                    .ContentsOA.ParagraphsOS.RemoveAt(0);
        });
        new FwDataProjectLoader().Save(cache);
        var fwDataPath = cache.ProjectId.Path;
        var approvedReadings = ApprovedMorphologyReader.Read(cache)[SeededProject.AnalysedWordForm];
        var approved = Assert.Single(approvedReadings);
        string[] words =
            [SeededProject.AnalysedWordForm, SeededProject.UnanalysedWordForm, "motifa", "motifb", "motifc", "motifd"];
        var saved = SelectionCommands.SetDefault(new SetDefaultSelectionRequest(fwDataPath, "Default",
            mixedSelection ? [text.TextId] : [], words));
        Assert.True(saved.Succeeded, saved.Refusal?.Message);

        var unbuilt = new ParseWordEvidence(ParseMorphEvidence.Schema, 0, SeededProject.AnalysedWordForm, 5,
            false, false, false, [new ParseAnalysis([new ParseMorph(
                "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb", null, null)])], []);
        var resolvable = new ParseWordEvidence(ParseMorphEvidence.Schema, 5, "motifd", 30,
            false, false, false,
            [new ParseAnalysis(approved.Morphs.Select(morph => new ParseMorph(
                morph.Form, morph.Msa, morph.InflType, null)).ToArray())], []);
        var assessor = new FakeAssessor("fake-assessor", CollectedKinds, kind => kind == AssessmentKind.ParseTime
            ? new AssessmentRaw.Batch(new BatchAnalysis(
            [
                new(0, SeededProject.AnalysedWordForm, 5, WordOutcome.Analysed, "sig")
                    { Morphology = unbuilt, Correctness = MorphologyCorrectness.Compare(unbuilt, approvedReadings) },
                new(1, SeededProject.UnanalysedWordForm, 4, WordOutcome.NoAnalysis, "-"),
                // Stopped by the step limit while the time limit also ran out inside the search.
                new(2, "motifa", 90, WordOutcome.Capped, "partial")
                    { Morphology = new(ParseMorphEvidence.Schema, 2, "motifa", 90, false, true, false, [], []) },
                new(3, "motifb", 1000, WordOutcome.TimedOut, "-"),
                new(4, "motifc", 0, WordOutcome.Skipped, "-"),
                new(5, "motifd", 30, WordOutcome.Analysed, "sig")
                    { Morphology = resolvable },
            ], 1000, fwDataPath, []) { PerWordStepLimit = 200000 })
            : new AssessmentRaw.WordMeasurements([]))
        {
            CaptureEvidence = (scope, candidate) => FakeAssessmentEvidence.Capture(_managedRootsParent, scope, candidate),
        };

        var run = AssessCommand.Run(new AssessRequest(fwDataPath), NewManagedRoot(), assessor, NewInvoker(), null,
            CancellationToken.None);
        Assert.True(run.Succeeded, run.Refusal?.Message);
        var read = CurrentEvidenceQuery.ReadCurrentEvidence(fwDataPath);
        Assert.True(read.Succeeded, read.Refusal?.Message);

        var stored = read.Value!.Assessment;
        Assert.NotNull(stored);
        Assert.Equal(Facts(run.Value!.Words), Facts(stored.Words));
        var storedContext = stored.Words.Single(word => word.Word == SeededProject.AnalysedWordForm).StoredAnalyses;
        Assert.Equal([ReadingGrade.Approved, ReadingGrade.Disapproved, ReadingGrade.Candidate],
            storedContext.Select(analysis => analysis.StoredAnalysisOpinion));
        Assert.Equal(ObjectUsesQuery.UsesOf(run.Value.Words,
                new ObjectUseRef { AllomorphId = _pristine.Seed.FirstLexemeFormId.ToString("D") }).Words
            .Select(word => JsonSerializer.Serialize(word.Row)),
            ObjectUsesQuery.UsesOf(stored.Words,
                new ObjectUseRef { AllomorphId = _pristine.Seed.FirstLexemeFormId.ToString("D") }).Words
            .Select(word => JsonSerializer.Serialize(word.Row)));
        Assert.Equal(run.Value.CompletionSummary, stored.CompletionSummary);
        Assert.Equal(run.Value.InvocationId, stored.InvocationId);
        Assert.Equal(run.Value.Measurements.OrderBy(item => item.Kind),
            stored.Measurements.OrderBy(item => item.Kind));
        var runReading = Assert.Single(run.Value.Words.Single(word => word.Word == "motifd")
            .Readings!);
        var storedReading = Assert.Single(stored.Words.Single(word => word.Word == "motifd")
            .Readings!);
        Assert.Equal(runReading.Morphs.Select(morph => (morph.Form, morph.Gloss, morph.Category)),
            storedReading.Morphs.Select(morph => (morph.Form, morph.Gloss, morph.Category)));
        Assert.Equal(runReading.Morphs.Select(morph => (morph.AllomorphId, morph.GrammaticalInfoId)),
            storedReading.Morphs.Select(morph => (morph.AllomorphId, morph.GrammaticalInfoId)));
        Assert.All(storedReading.Morphs, morph => Assert.NotNull(morph.AllomorphId));
        var missed = Assert.Single(stored.Words, word => word.Word == SeededProject.AnalysedWordForm).MissedApproved!;
        Assert.Equal(SeededProject.FirstGloss, Assert.Single(missed).Morphs[0].Gloss);
    }

    [Fact]
    public void AReopenedTextWordKeepsItsWordAnalysesLink()
    {
        using var cache = _pristine.NewScratch();
        var text = SeededProject.SeedText(cache, _pristine.Seed);
        new FwDataProjectLoader().Save(cache);
        var fwDataPath = cache.ProjectId.Path;
        var saved = SelectionCommands.SetDefault(new SetDefaultSelectionRequest(fwDataPath, "Default", [text.TextId], []));
        Assert.True(saved.Succeeded, saved.Refusal?.Message);
        var assessor = new FakeAssessor("fake-assessor", CollectedKinds, kind => kind == AssessmentKind.ParseTime
            ? new AssessmentRaw.Batch(new BatchAnalysis(
            [
                new(0, SeededProject.AnalysedWordForm, 5, WordOutcome.NoAnalysis, "-"),
                new(1, SeededProject.UnanalysedWordForm, 4, WordOutcome.NoAnalysis, "-"),
            ], 1000, fwDataPath, []) { PerWordStepLimit = 200000 })
            : new AssessmentRaw.WordMeasurements([]))
        {
            CaptureEvidence = (scope, candidate) => FakeAssessmentEvidence.Capture(_managedRootsParent, scope, candidate),
        };

        var run = AssessCommand.Run(new AssessRequest(fwDataPath), NewManagedRoot(), assessor, NewInvoker(), null,
            CancellationToken.None);
        Assert.True(run.Succeeded, run.Refusal?.Message);
        var read = CurrentEvidenceQuery.ReadCurrentEvidence(fwDataPath);
        Assert.True(read.Succeeded, read.Refusal?.Message);

        var runLinks = run.Value!.Words.OrderBy(word => word.Word, StringComparer.Ordinal)
            .Select(word => (word.Word, word.TryWordLink)).ToArray();
        Assert.Equal(2, runLinks.Length);
        Assert.All(runLinks, link => Assert.NotNull(link.TryWordLink));
        Assert.Equal(runLinks, read.Value!.Assessment!.Words.OrderBy(word => word.Word, StringComparer.Ordinal)
            .Select(word => (word.Word, word.TryWordLink)));
    }

    [Fact]
    public void EachAssessmentKindNameIsTheStoredKind()
    {
        Assert.Equal(AssessmentKinds.ParseTime, AssessmentKind.ParseTime.ToStoredKind());
        Assert.Equal(AssessmentKinds.ObjectTiming, AssessmentKind.ObjectTiming.ToStoredKind());
        Assert.Equal(AssessmentKinds.Correctness, AssessmentKind.Correctness.ToStoredKind());
    }

    private static IReadOnlyList<string> Facts(IEnumerable<AssessmentWordResult> words) => words
        .OrderBy(word => word.Word, StringComparer.Ordinal)
        .Select(word => string.Join(" | ", word.Word, word.Outcome, word.IsIncomplete, word.CompletionStatus,
            word.ElapsedMs, word.ProjectStanding, string.Join(",", word.ReadingGrades ?? []), word.OccurrenceCount,
            word.FixFirst?.Category, word.FixFirst?.Rank, word.FixFirst?.Label, word.FixFirst?.Explanation,
            JsonSerializer.Serialize(word.MissedApproved), JsonSerializer.Serialize(word.StoredAnalyses),
            JsonSerializer.Serialize(word.ExpectedAnalysis), word.TryWordLink, word.Attempts, word.Passes))
        .ToArray();

    private static FakeInvoker NewInvoker() => new()
    {
        Respond = _ => new PanGlossOutcome.Completed("fake stats", string.Empty, TimeSpan.Zero),
    };

    private string NewManagedRoot()
    {
        var root = Path.Combine(_managedRootsParent, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
