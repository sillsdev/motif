using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using SIL.LCModel;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Assess;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;
using SIL.Motif.Host.Analysis;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Parser;
using SIL.Motif.Tests.Parser;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Commands;

[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group2)]
public sealed class MiniRowReadingTests : IDisposable
{
    private readonly PristineProjectFixture _pristine;
    private readonly string _managedRootsParent = Path.Combine(Path.GetTempPath(), nameof(MiniRowReadingTests),
        Guid.NewGuid().ToString("N"));

    public MiniRowReadingTests(PristineProjectFixture pristine)
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
    public void WarningRowsShowTheResolvedParserReadingAndMatchTheMatrixRow()
    {
        using var seeded = NewSeededScratch();
        var evidence = AssessOneDifferentReading(seeded);
        var finding = Finding(evidence.AllomorphId);
        var check = new GrammarCheckResponse([finding], HasBaseline: true);

        var outcome = ProjectStoreCommand.Run(seeded.FwDataPath, MotifProductVersion.CurrentText,
            (database, project) => CommandOutcome<GrammarCheckResponse>.Success(
                WarningWordsQuery.WithYourWords(database, project, check, evidence.Snapshot.Baseline!.Token)));

        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        var row = Assert.Single(Assert.Single(outcome.Value!.Findings).YourWords!.Words).Row;
        AssertRowsMatch(evidence.MatrixRow, row);
        Assert.Equal(WordRowReadingAvailability.Included, row.PanGlossReadingAvailability);
        Assert.Equal(1, row.PanGlossReadingCount);
        Assert.Equal([SeededProject.FirstForm, SeededProject.FirstForm], row.PanGlossMorphemes.Select(morph => morph.Form));
        Assert.Equal([2], row.DifferingPositions);
    }

    [Fact]
    public void InspectorRowsShowTheResolvedParserReadingAndMatchTheMatrixRow()
    {
        using var seeded = NewSeededScratch();
        var evidence = AssessOneDifferentReading(seeded);

        var outcome = InspectQuery.Query(new InspectRequest(seeded.FwDataPath,
            InspectorSubject.Morpheme(evidence.AllomorphId, evidence.GrammaticalInfoId,
                identityQuality: "authored")!));

        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        var row = Assert.Single(outcome.Value!.Uses.Value!.Words).Row;
        AssertRowsMatch(evidence.MatrixRow, row);
        Assert.Equal(WordRowReadingAvailability.Included, row.PanGlossReadingAvailability);
        Assert.Equal(1, row.PanGlossReadingCount);
        Assert.Equal([SeededProject.FirstForm, SeededProject.FirstForm], row.PanGlossMorphemes.Select(morph => morph.Form));
        Assert.Equal([2], row.DifferingPositions);
    }

    private (CurrentEvidenceSnapshot Snapshot, WordRow MatrixRow, string AllomorphId, string GrammaticalInfoId)
        AssessOneDifferentReading(SeededScratch seeded)
    {
        Assert.True(SelectionCommands.SetDefault(new SetDefaultSelectionRequest(
            seeded.FwDataPath, "Default", [seeded.Seeded.TextId], [])).Succeeded);

        string allomorphId;
        string grammaticalInfoId;
        IReadOnlyList<ApprovedMorphology> approved;
        using (var cache = new FwDataProjectLoader().LoadScratchCache(seeded.FwDataPath))
        {
            var wordform = cache.ServiceLocator.GetInstance<IWfiWordformRepository>().AllInstances()
                .Single(word => word.Form.VernacularDefaultWritingSystem?.Text == SeededProject.AnalysedWordForm);
            var bundle = wordform.HumanApprovedAnalyses.Single().MorphBundlesOS[0];
            allomorphId = bundle.MorphRA!.Guid.ToString("D");
            grammaticalInfoId = bundle.MsaRA!.Guid.ToString("D");
            approved = ApprovedMorphologyReader.Read(cache)[SeededProject.AnalysedWordForm];
        }

        var morphology = new ParseWordEvidence(ParseMorphEvidence.Schema, 0, SeededProject.AnalysedWordForm, 5,
            false, false, false,
            [new ParseAnalysis([
                new ParseMorph(allomorphId, grammaticalInfoId, null, null),
                new ParseMorph(allomorphId, grammaticalInfoId, null, null),
            ])], []);
        var correctness = MorphologyCorrectness.Compare(morphology, approved);
        var assessor = new FakeAssessor("mini-row-reading",
            [AssessmentKind.ParseTime, AssessmentKind.ObjectTiming], kind =>
            kind == AssessmentKind.ParseTime
                ? new AssessmentRaw.Batch(new BatchAnalysis(
                [
                    new(0, SeededProject.AnalysedWordForm, 5, WordOutcome.Analysed, "different")
                    {
                        Morphology = morphology,
                        Correctness = correctness,
                    },
                    new(1, SeededProject.UnanalysedWordForm, 4, WordOutcome.NoAnalysis, "none"),
                ], 1000, seeded.FwDataPath, []) { PerWordStepLimit = 200000 })
                : new AssessmentRaw.WordMeasurements([]))
        {
            CaptureEvidence = (scope, candidate) => FakeAssessmentEvidence.Capture(
                _managedRootsParent, scope, candidate),
        };
        var invoker = new FakeInvoker
        {
            Respond = _ => new PanGlossOutcome.Completed("fake stats", string.Empty, TimeSpan.Zero),
        };
        var assessed = AssessCommand.Run(new AssessRequest(seeded.FwDataPath),
            Path.Combine(_managedRootsParent, "managed"), assessor, invoker, null, CancellationToken.None);
        Assert.True(assessed.Succeeded, assessed.Refusal?.Message);

        var current = CurrentEvidenceQuery.ReadCurrentEvidence(seeded.FwDataPath);
        Assert.True(current.Succeeded, current.Refusal?.Message);
        var snapshot = current.Value!;
        var word = Assert.Single(snapshot.Assessment!.Words, item => item.Word == SeededProject.AnalysedWordForm);
        var row = WordRowProjection.Of(word);
        Assert.Equal(WordRowOutcome.Different, row.Outcome);
        Assert.Equal(1, row.PanGlossReadingCount);
        Assert.Equal(2, row.PanGlossMorphemes.Count);
        return (snapshot, row, allomorphId, grammaticalInfoId);
    }

    private static GrammarWarning Finding(string allomorphId) => new(GrammarDiagnosticLevel.Warning, "test", [
        new GrammarWarningPart(SeededProject.FirstForm, GrammarWarningPartRole.Object, allomorphId, "MoForm")
        {
            SubjectGuid = allomorphId,
            Reach = new WarningReach(WarningWordsPath.Uses) { AllomorphIds = [allomorphId] },
        },
    ], [], "test warning") { Code = "test.warning" };

    private static void AssertRowsMatch(WordRow expected, WordRow actual)
    {
        Assert.Equal(expected.Word, actual.Word);
        Assert.Equal(expected.Outcome, actual.Outcome);
        Assert.Equal(expected.Meaning, actual.Meaning);
        Assert.Equal(expected.MeaningCode, actual.MeaningCode);
        Assert.Equal(expected.MeaningDetail, actual.MeaningDetail);
        Assert.Equal(ProjectionJson.Serialize(expected.Comparison), ProjectionJson.Serialize(actual.Comparison));
        Assert.Equal(expected.Tone, actual.Tone);
        Assert.Equal(expected.PanGlossReadingAvailability, actual.PanGlossReadingAvailability);
        Assert.Equal(expected.Gloss, actual.Gloss);
        Assert.Equal(expected.Opinion, actual.Opinion);
        Assert.Equal(expected.FieldWorksAnalysisId, actual.FieldWorksAnalysisId);
        Assert.Equal(expected.FieldWorksMorphemes.Select(MorphFacts), actual.FieldWorksMorphemes.Select(MorphFacts));
        Assert.Equal(expected.PanGlossMorphemes.Select(MorphFacts), actual.PanGlossMorphemes.Select(MorphFacts));
        Assert.Equal(expected.DifferingPositions, actual.DifferingPositions);
        Assert.Equal(expected.PanGlossReadingCount, actual.PanGlossReadingCount);
        Assert.Equal(expected.Places, actual.Places);
        Assert.Equal(expected.ElapsedMs, actual.ElapsedMs);
        Assert.Equal(expected.Origin, actual.Origin);
        Assert.Equal(expected.WordAnalysesLink, actual.WordAnalysesLink);
    }

    private static (string Form, string Gloss, string Category, string? AllomorphId, string? GrammaticalInfoId)
        MorphFacts(ParserReadingMorph morph) =>
        (morph.Form, morph.Gloss, morph.Category, morph.AllomorphId, morph.GrammaticalInfoId);

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
}
