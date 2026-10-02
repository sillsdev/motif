using System.IO;
using System.Text.Json;
using System.Xml.Linq;
using SIL.LCModel;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Analysis;
using SIL.Motif.Host.Corpus;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.Parser;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Store;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands;

[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group2)]
public sealed class EvidenceQueryBaselineTests : IDisposable
{
    private readonly PristineProjectFixture _pristine;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "SIL.Motif.EvidenceQueryBaselineTests",
        Guid.NewGuid().ToString("N"));

    public EvidenceQueryBaselineTests(PristineProjectFixture pristine)
    {
        _pristine = pristine;
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public void UsesAndStoredReadingsRemainReadableWhileFieldWorksHoldsTheSource()
    {
        var project = Capture();
        var selected = SelectionCommands.SetDefault(new SetDefaultSelectionRequest(
            project.Path, "Default", [project.TextId], []));
        Assert.True(selected.Succeeded, selected.Refusal?.Message);
        RecordAssessment(project, [SeededProject.AnalysedWordForm, SeededProject.UnanalysedWordForm]);
        var stamp = File.GetLastWriteTimeUtc(project.Path);
        using var held = new FileStream(project.Path + ".lock", FileMode.Create, FileAccess.ReadWrite, FileShare.None);

        var uses = ObjectUsesQuery.Query(new ObjectUsesRequest(project.Path,
            new ObjectUseRef { AllomorphId = _pristine.Seed.FirstLexemeFormId.ToString("D") }));
        var evidence = CurrentEvidenceQuery.ReadCurrentEvidence(project.Path);

        Assert.True(uses.Succeeded, uses.Refusal?.Message);
        Assert.Equal(SeededProject.AnalysedWordForm, Assert.Single(uses.Value!.Uses!.Words).Row.Word);
        Assert.True(evidence.Succeeded, evidence.Refusal?.Message);
        var reading = Assert.Single(evidence.Value!.Assessment!.Words
            .Single(word => word.Word == SeededProject.AnalysedWordForm).Readings!);
        Assert.Equal(SeededProject.FirstGloss, reading.Morphs[0].Gloss);
        Assert.Contains(Path.GetFileNameWithoutExtension(project.Path), reading.Morphs[0].FieldWorksLink!);
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(project.Path));
    }

    [Theory]
    [InlineData("structural")]
    [InlineData("unknown")]
    [InlineData("grammar-local")]
    public void OpaqueMorphemeIdsCannotReadAuthoredFactsOrUses(string quality)
    {
        var project = Capture();
        Assert.True(SelectionCommands.SetDefault(new SetDefaultSelectionRequest(
            project.Path, "Default", [], [SeededProject.AnalysedWordForm])).Succeeded);
        RecordAssessment(project, [SeededProject.AnalysedWordForm]);
        var subject = InspectorSubject.Morpheme(_pristine.Seed.FirstLexemeFormId.ToString("D"),
            project.Morphology.Analyses[0].Morphs[0].Msa)! with { IdentityQuality = quality };
        var result = InspectQuery.Query(new InspectRequest(project.Path, subject));
        Assert.True(result.Succeeded, result.Refusal?.Message);
        Assert.Equal(InspectorResolution.NotAuthored, result.Value!.Resolution);
        Assert.Null(result.Value.Facts.Value);
        Assert.Equal(InspectorSectionStatus.Absent, result.Value.Uses.Status);
        Assert.Null(result.Value.Uses.Value);
        Assert.Null(result.Value.TimingKey);
    }

    [Theory]
    [InlineData("replaced", false)]
    [InlineData("deleted", false)]
    [InlineData("wrong-type", false)]
    [InlineData("unreadable", false)]
    [InlineData("retained", true)]
    public void LiveLinksVerifySavedProjectAndTargetWhileBaselineFactsRemainReadable(string change, bool linksAvailable)
    {
        var project = Capture();
        Assert.True(SelectionCommands.SetDefault(new SetDefaultSelectionRequest(
            project.Path, "Default", [], [SeededProject.AnalysedWordForm])).Succeeded);
        RecordAssessment(project, [SeededProject.AnalysedWordForm], missedLink:
            FieldWorksLinks.ForTarget(Path.GetFileNameWithoutExtension(project.Path), new("lexiconEdit", _pristine.Seed.FirstEntryId)));
        var saved = XDocument.Load(project.Path);
        if (change == "replaced")
            saved.Root!.Elements("rt").Single(item => (string?)item.Attribute("class") == "LangProject")
                .SetAttributeValue("guid", Guid.NewGuid());
        if (change == "deleted")
            saved.Root!.Elements("rt").Where(item => (string?)item.Attribute("class") is "LexEntry" or "WfiWordform").Remove();
        if (change == "wrong-type")
            foreach (var item in saved.Root!.Elements("rt").Where(item =>
                         (string?)item.Attribute("class") is "LexEntry" or "WfiWordform"))
                item.SetAttributeValue("class", "CmPossibility");
        if (change == "unreadable") File.WriteAllText(project.Path, "<not-a-project>");
        else saved.Save(project.Path);
        File.SetLastWriteTimeUtc(project.Path, project.Baseline.SourceLastWriteUtc.UtcDateTime.AddSeconds(10));
        var subject = InspectorSubject.Morpheme(_pristine.Seed.FirstLexemeFormId.ToString("D"), null)!
            with { IdentityQuality = "authored" };
        var inspected = InspectQuery.Query(new InspectRequest(project.Path, subject));
        Assert.True(inspected.Succeeded, inspected.Refusal?.Message);
        var facts = inspected.Value!.Facts.Value!;
        Assert.NotNull(facts.Entry);
        Assert.Equal(linksAvailable, facts.Entry.FieldWorks is not null);
        var uses = ObjectUsesQuery.Query(new ObjectUsesRequest(project.Path,
            new ObjectUseRef { AllomorphId = subject.AllomorphId }));
        Assert.True(uses.Succeeded, uses.Refusal?.Message);
        Assert.Equal(linksAvailable, uses.Value!.Facts!.Entry!.FieldWorks is not null);
        var context = WordContextQuery.Query(new WordContextRequest(project.Path, SeededProject.AnalysedWordForm));
        Assert.True(context.Succeeded, context.Refusal?.Message);
        Assert.Equal(linksAvailable, context.Value!.WordAnalysesLink is not null);
        Assert.Equal(linksAvailable, context.Value.Analyses[0].Morphs[0].FieldWorksLink is not null);
        var evidence = CurrentEvidenceQuery.ReadCurrentEvidence(project.Path);
        Assert.True(evidence.Succeeded, evidence.Refusal?.Message);
        var row = Assert.Single(evidence.Value!.Assessment!.Words);
        Assert.Equal(linksAvailable, row.Readings![0].Morphs[0].FieldWorksLink is not null);
        Assert.Equal(linksAvailable, row.MissedApproved![0].Morphs[0].FieldWorksLink is not null);
        var text = TextWordsQuery.Query(new TextWordsRequest(project.Path, [project.TextId]));
        Assert.True(text.Succeeded, text.Refusal?.Message);
        var token = text.Value!.Texts.SelectMany(item => item.Lines).SelectMany(line => line.Tokens)
            .First(item => item.Analysis is not null);
        Assert.Equal(linksAvailable, token.WordLink is not null);
        Assert.Equal(linksAvailable, token.Analysis!.Morphs[0].FieldWorksLink is not null);
    }

    [Theory]
    [InlineData("deleted", false)]
    [InlineData("wrong-type", false)]
    [InlineData("replaced", false)]
    [InlineData("unreadable", false)]
    [InlineData("retained", true)]
    public void TraceCaptureVerifiesLiveDestinationsWithoutLosingCapturedLabels(string change, bool available)
    {
        var project = Capture();
        var saved = XDocument.Load(project.Path);
        if (change == "deleted")
            saved.Root!.Elements("rt").Where(item => (string?)item.Attribute("class") == "LexEntry").Remove();
        if (change == "replaced")
            saved.Root!.Elements("rt").Single(item => (string?)item.Attribute("class") == "LangProject")
                .SetAttributeValue("guid", Guid.NewGuid());
        if (change == "wrong-type")
            foreach (var item in saved.Root!.Elements("rt").Where(item =>
                         (string?)item.Attribute("class") is "LexEntry" or "WfiWordform"))
                item.SetAttributeValue("class", "CmPossibility");
        if (change == "unreadable") File.WriteAllText(project.Path, "<broken>");
        else saved.Save(project.Path);
        var root = new TraceStep("LexEntryLookup", "producer label", null, null, null, [])
        {
            SourceIdentityKind = "lexEntry", SourceIdentityId = _pristine.Seed.FirstEntryId.ToString("D"),
            SourceIdentityQuality = "authored",
            AttemptedMorphs = [new TraceMorph(null, "producer form", null, null, null, null, null, null, null, null)
            {
                EntryId = _pristine.Seed.FirstEntryId.ToString("D"), IdentityQuality = "authored",
            }],
        };
        var response = new WordTraceResponse("word", false, true, null, 1, null, 1,
            TraceReadingBuilder.Build("word", root, [], [])) { DiagnosticJson = "{}" };
        using var database = ProjectMotifDatabase.Open(project.Path);
        var locator = new ProjectLocator(project.Path, Path.GetFileNameWithoutExtension(project.Path));
        var baseline = new SIL.Motif.Worker.Baselines.BaselineRepository(database)
            .GetCurrent(ProjectWorkspaceKey.Compute(locator))!;
        var attached = TraceDiagnosticCapture.Attach(response, baseline, locator);
        var reference = attached.Reading.Refs.Single(item => item.Kind != "morph");
        Assert.Equal("producer label", reference.Label);
        Assert.NotNull(reference.CapturedFieldWorksLabel);
        Assert.Equal(available, reference.FieldWorks is not null);
        Assert.Equal(available, attached.Reading.Root.AttemptedMorphs[0].FieldWorksLink is not null);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AddedWordsKeepBaselineAnalysesOutsideTheChosenTexts(bool chooseOtherText)
    {
        var project = Capture(analysedWordOutsideText: chooseOtherText);
        var selected = SelectionCommands.SetDefault(new SetDefaultSelectionRequest(project.Path, "Default",
            chooseOtherText ? [project.TextId] : [], [SeededProject.AnalysedWordForm]));
        Assert.True(selected.Succeeded, selected.Refusal?.Message);
        RecordAssessment(project, chooseOtherText
            ? [SeededProject.AnalysedWordForm, SeededProject.UnanalysedWordForm]
            : [SeededProject.AnalysedWordForm], parsed: false);
        using var held = new FileStream(project.Path + ".lock", FileMode.Create, FileAccess.ReadWrite, FileShare.None);

        var evidence = CurrentEvidenceQuery.ReadCurrentEvidence(project.Path);
        Assert.True(evidence.Succeeded, evidence.Refusal?.Message);
        var stored = Assert.Single(evidence.Value!.Assessment!.Words
            .Single(word => word.Word == SeededProject.AnalysedWordForm).StoredAnalyses);
        Assert.Equal(ReadingGrade.Approved, stored.StoredAnalysisOpinion);
        Assert.NotNull(stored.StoredAnalysisId);
        Assert.Equal(_pristine.Seed.FirstLexemeFormId.ToString("D"), stored.Morphs[0].AllomorphId);
        Assert.Equal(stored.StoredAnalysisId, stored.Identity!.SourceAnalysisId);
        var uses = ObjectUsesQuery.Query(new ObjectUsesRequest(project.Path,
            new ObjectUseRef { AllomorphId = _pristine.Seed.FirstLexemeFormId.ToString("D") }));
        Assert.True(uses.Succeeded, uses.Refusal?.Message);
        Assert.Equal(SeededProject.AnalysedWordForm, Assert.Single(uses.Value!.Uses!.Words).Row.Word);

        var checkedNow = GrammarCheckQuery.Query(new GrammarCheckRequest(project.Path), new FakeInvoker
        {
            Respond = _ => new PanGlossOutcome.Completed(GrammarHealthReports.With(
                ("allomorph", [new("MoForm", "motifa", _pristine.Seed.FirstLexemeFormId)])),
                string.Empty, TimeSpan.Zero),
        }, CancellationToken.None);
        Assert.True(checkedNow.Succeeded, checkedNow.Refusal?.Message);
        Assert.Equal(SeededProject.AnalysedWordForm,
            Assert.Single(Assert.Single(checkedNow.Value!.Findings).YourWords!.Words).Row.Word);
        var overview = SIL.Motif.Commands.Catalog.OverviewCommand.Overview(new OverviewRequest(project.Path));
        Assert.True(overview.Succeeded, overview.Refusal?.Message);
        Assert.Equal(1, overview.Value!.Warnings!.YourWords!.Words);
    }

    [Fact]
    public void StaleSourceKeepsTheAssessmentsBaselineReadings()
    {
        var project = Capture();
        Assert.True(SelectionCommands.SetDefault(new SetDefaultSelectionRequest(
            project.Path, "Default", [], [SeededProject.AnalysedWordForm])).Succeeded);
        RecordAssessment(project, [SeededProject.AnalysedWordForm]);
        File.WriteAllText(project.Path, File.ReadAllText(project.Path)
            .Replace(SeededProject.FirstGloss, "changed live gloss", StringComparison.Ordinal));
        File.SetLastWriteTimeUtc(project.Path, project.Baseline.SourceLastWriteUtc.UtcDateTime.AddMinutes(1));
        using var held = new FileStream(project.Path + ".lock", FileMode.Create, FileAccess.ReadWrite, FileShare.None);

        var evidence = CurrentEvidenceQuery.ReadCurrentEvidence(project.Path);

        Assert.True(evidence.Succeeded, evidence.Refusal?.Message);
        Assert.Equal(EvidenceFreshness.Stale, evidence.Value!.Freshness);
        var row = Assert.Single(evidence.Value.Assessment!.Words);
        Assert.Equal(SeededProject.FirstGloss, Assert.Single(row.Readings!).Morphs[0].Gloss);
        Assert.Equal(SeededProject.FirstGloss, Assert.Single(row.StoredAnalyses).Morphs[0].Gloss);
    }

    [Fact]
    public void MissingAssessmentBaselineRefusesInsteadOfDroppingReadings()
    {
        var project = Capture();
        Assert.True(SelectionCommands.SetDefault(new SetDefaultSelectionRequest(
            project.Path, "Default", [], [SeededProject.AnalysedWordForm])).Succeeded);
        RecordAssessment(project, [SeededProject.AnalysedWordForm]);
        File.Delete(project.Baseline.FwDataPath);

        var evidence = CurrentEvidenceQuery.ReadCurrentEvidence(project.Path);

        Assert.False(evidence.Succeeded);
        Assert.Equal("current-evidence.baseline-unavailable", evidence.Refusal!.Code);
    }

    [Fact]
    public void GrammarCheckDoesNotJoinAReplacedBaselinesWords()
    {
        var project = Capture();
        Assert.True(SelectionCommands.SetDefault(new SetDefaultSelectionRequest(
            project.Path, "Default", [], [SeededProject.AnalysedWordForm])).Succeeded);
        RecordAssessment(project, [SeededProject.AnalysedWordForm], parsed: false);
        var result = GrammarCheckQuery.Query(new GrammarCheckRequest(project.Path), new FakeInvoker
        {
            Respond = _ =>
            {
                File.WriteAllText(project.Path, File.ReadAllText(project.Path)
                    .Replace(SeededProject.FirstGloss, "new Baseline gloss", StringComparison.Ordinal));
                File.SetLastWriteTimeUtc(project.Path, project.Baseline.SourceLastWriteUtc.UtcDateTime.AddMinutes(1));
                var captured = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(project.Path), _root);
                Assert.True(captured.Succeeded, captured.Refusal?.Message);
                Assert.NotEqual(project.Baseline.Token, captured.Value!.Token);
                RecordAssessment(project with { Baseline = captured.Value }, [SeededProject.AnalysedWordForm],
                    parsed: false, id: "new-baseline-assessment");
                return new PanGlossOutcome.Completed(GrammarHealthReports.With(
                    ("allomorph", [new("MoForm", "motifa", _pristine.Seed.FirstLexemeFormId)])),
                    string.Empty, TimeSpan.Zero);
            },
        }, CancellationToken.None);

        Assert.True(result.Succeeded, result.Refusal?.Message);
        Assert.Null(Assert.Single(result.Value!.Findings).YourWords);
        Assert.Null(WarningWordsQuery.Touched(result.Value.Findings));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExactWordContextDoesNotNeedMembershipInTheMeasuredSelection(bool priorAssessment)
    {
        var project = Capture(includeOtherOpinions: true);
        if (priorAssessment)
        {
            Assert.True(SelectionCommands.SetDefault(new SetDefaultSelectionRequest(project.Path,
                "Default", [], [SeededProject.UnanalysedWordForm])).Succeeded);
            RecordAssessment(project, [SeededProject.UnanalysedWordForm], parsed: false);
        }
        File.WriteAllText(project.Path, File.ReadAllText(project.Path)
            .Replace(SeededProject.FirstGloss, "changed live gloss", StringComparison.Ordinal));
        File.SetLastWriteTimeUtc(project.Path, project.Baseline.SourceLastWriteUtc.UtcDateTime.AddMinutes(1));
        using var held = new FileStream(project.Path + ".lock", FileMode.Create, FileAccess.ReadWrite, FileShare.None);

        var context = WordContextQuery.Query(new WordContextRequest(project.Path, SeededProject.AnalysedWordForm,
            project.Baseline.Token));
        var emptyWordform = WordContextQuery.Query(new WordContextRequest(project.Path, SeededProject.UnanalysedWordForm));
        var absent = WordContextQuery.Query(new WordContextRequest(project.Path, "absent-exact-form"));

        Assert.True(context.Succeeded, context.Refusal?.Message);
        Assert.True(context.Value!.HasBaseline);
        Assert.True(context.Value.IsInFieldWorks);
        Assert.True(context.Value.IsStale);
        Assert.Equal(project.Baseline.Token, context.Value.Baseline);
        Assert.Equal(project.Baseline.SourceLastWriteUtc, context.Value.SourceLastWriteUtc);
        Assert.Equal([ReadingGrade.Approved, ReadingGrade.Disapproved, ReadingGrade.Candidate],
            context.Value.Analyses.Select(analysis => analysis.StoredAnalysisOpinion));
        Assert.All(context.Value.Analyses, analysis =>
        {
            Assert.NotNull(analysis.StoredAnalysisId);
            Assert.Equal(SeededProject.FirstGloss, analysis.Morphs[0].Gloss);
        });
        Assert.Equal(context.Value.Analyses[0], context.Value.ExpectedAnalysis);
        Assert.NotNull(context.Value.WordAnalysesLink);
        Assert.True(emptyWordform.Succeeded, emptyWordform.Refusal?.Message);
        Assert.True(emptyWordform.Value!.IsInFieldWorks);
        Assert.Empty(emptyWordform.Value.Analyses);
        Assert.True(absent.Succeeded, absent.Refusal?.Message);
        Assert.False(absent.Value!.IsInFieldWorks);
        Assert.Empty(absent.Value.Analyses);
    }

    [Fact]
    public void WordContextBeforeCaptureLeavesMembershipUnknown()
    {
        var path = _pristine.CopyProjectFile();

        var context = WordContextQuery.Query(new WordContextRequest(path, SeededProject.AnalysedWordForm));

        Assert.True(context.Succeeded, context.Refusal?.Message);
        Assert.False(context.Value!.HasBaseline);
        Assert.Null(context.Value.Baseline);
        Assert.Null(context.Value.IsInFieldWorks);
        Assert.Empty(context.Value.Analyses);
        Assert.Null(context.Value.ExpectedAnalysis);
    }

    [Fact]
    public void WordContextRefusesAChangedOrMissingBaselineWithoutSubstitutingLiveAnalyses()
    {
        var project = Capture();
        var token = project.Baseline.Token;
        var otherToken = new SIL.Motif.Contract.Baselines.BaselineToken(token.ProjectIdentity,
            token.SemanticSnapshotDigest, token.ProjectionVersion, token.CapturedUtc, "sha256:" + new string('f', 64));

        var changed = WordContextQuery.Query(new WordContextRequest(project.Path,
            SeededProject.AnalysedWordForm, otherToken));

        Assert.False(changed.Succeeded);
        Assert.Equal("word-context.baseline-changed", changed.Refusal!.Code);
        File.Delete(project.Baseline.FwDataPath);
        var missing = WordContextQuery.Query(new WordContextRequest(project.Path, SeededProject.AnalysedWordForm));
        Assert.False(missing.Succeeded);
        Assert.Equal("word-context.baseline-unavailable", missing.Refusal!.Code);
    }

    [Fact]
    public async Task BaselineReadersAndNumericQueriesRemainIndependentAcrossProcesses()
    {
        var project = Capture();
        Assert.True(SelectionCommands.SetDefault(new SetDefaultSelectionRequest(
            project.Path, "Default", [], [SeededProject.AnalysedWordForm])).Succeeded);
        RecordAssessment(project, [SeededProject.AnalysedWordForm]);
        using var held = new FwDataProjectLoader().LoadScratchCache(project.Baseline.FwDataPath);
        var heldPath = held.ProjectId.Path;
        var reading = Task.Run(() => WordContextQuery.Query(new WordContextRequest(
            project.Path, SeededProject.AnalysedWordForm)));
        var numeric = SIL.Motif.Commands.Catalog.TimingCommand.Timing(new TimingRequest(project.Path));
        var overview = SIL.Motif.Commands.Catalog.OverviewCommand.Overview(new OverviewRequest(project.Path));
        var child = await CliProcess.RunAsync(Path.Combine(_root, "worker"), null, true,
            "uses", "--project", project.Path, "--allomorph", _pristine.Seed.FirstLexemeFormId.ToString("D"), "--json");
        var inspected = await CliProcess.RunAsync(Path.Combine(_root, "worker"), null, true,
            "inspect", "--project", project.Path, "--allomorph",
            _pristine.Seed.FirstLexemeFormId.ToString("D"), "--json");
        var context = await reading;
        Assert.True(numeric.Succeeded, numeric.Refusal?.Message);
        Assert.True(overview.Succeeded, overview.Refusal?.Message);
        Assert.True(context.Succeeded, context.Refusal?.Message);
        Assert.True(context.Value!.IsInFieldWorks);
        Assert.True(child.ExitCode == 0, child.Error + child.Output);
        var uses = ProjectionJson.Deserialize<ObjectUsesResponse>(child.Output)!;
        Assert.Equal(SeededProject.AnalysedWordForm, Assert.Single(uses.Uses!.Words).Row.Word);
        Assert.NotNull(uses.Facts);
        Assert.True(inspected.ExitCode == 0, inspected.Error + inspected.Output);
        var inspection = ProjectionJson.Deserialize<InspectResponse>(inspected.Output)!;
        Assert.Equal(InspectorSectionStatus.Available, inspection.Facts.Status);
        Assert.Equal(SeededProject.AnalysedWordForm, Assert.Single(inspection.Uses.Value!.Words).Row.Word);
        Assert.Equal(1, numeric.Value!.WordCount);
        Assert.Equal("assessment", Assert.Single(numeric.Value.Words).Origin!.AssessmentId);
        Assert.Single(overview.Value!.WordOrigins);
        Assert.Equal(heldPath, held.ProjectId.Path);
    }

    [Fact]
    public async Task TraceCaptureRemainsReadableAcrossProcessesWhileTheExactBaselineIsHeld()
    {
        var project = Capture();
        var parser = FakeParser.CopyRecordingInvocations(Path.Combine(_root, "trace-parser"));
        FakeParser.BehaveBesideExecutable(parser,
            FakeParser.TraceBehavior(SeededProject.AnalysedWordForm, "held-baseline", "seeded rule"));
        var original = File.ReadAllBytes(project.Baseline.FwDataPath);
        using var held = new FwDataProjectLoader().LoadScratchCache(project.Baseline.FwDataPath);
        var heldPath = held.ProjectId.Path;

        var child = await CliProcess.RunAsync(Path.Combine(_root, "worker"), parser, true,
            "trace", "--project", project.Path, "--word", SeededProject.AnalysedWordForm, "--json");

        Assert.True(child.ExitCode == 0, child.FailureDetails);
        var trace = ProjectionJson.Deserialize<WordTraceResponse>(child.Output)!;
        Assert.Equal(SeededProject.AnalysedWordForm, trace.Word);
        Assert.Equal(project.Baseline.Token, trace.HostCapture!.Baseline!.Token);
        Assert.NotEmpty(trace.HostCapture.WritingSystems);
        Assert.Contains("parse", FakeParser.Invocations(parser));
        Assert.Equal(heldPath, held.ProjectId.Path);
        Assert.Equal(original, File.ReadAllBytes(project.Baseline.FwDataPath));
    }

    [Fact]
    public void NumericQueriesDoNotOpenAnUnusedBaselineCache()
    {
        var project = Capture();
        Assert.True(SelectionCommands.SetDefault(new SetDefaultSelectionRequest(
            project.Path, "Default", [], [SeededProject.AnalysedWordForm])).Succeeded);
        RecordAssessment(project, [SeededProject.AnalysedWordForm]);
        File.WriteAllText(project.Baseline.FwDataPath, "A numeric query must not parse this file.");
        var timing = SIL.Motif.Commands.Catalog.TimingCommand.Timing(new TimingRequest(project.Path));
        var overview = SIL.Motif.Commands.Catalog.OverviewCommand.Overview(new OverviewRequest(project.Path));
        Assert.True(timing.Succeeded, timing.Refusal?.Message);
        Assert.True(overview.Succeeded, overview.Refusal?.Message);
        Assert.Equal(1, timing.Value!.WordCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EveryCapturedWritingSystemFormKeepsItsAnalysisIdentitiesAndMembership(bool addedOnly)
    {
        var project = Capture(includeOtherOpinions: true, secondaryForm: "beta");
        Assert.True(SelectionCommands.SetDefault(new SetDefaultSelectionRequest(project.Path, "Default",
            addedOnly ? [] : [project.TextId], addedOnly ? ["beta"] : [])).Succeeded);
        var forms = addedOnly ? new[] { "beta" } : new[]
            { SeededProject.AnalysedWordForm, SeededProject.UnanalysedWordForm, "beta" };
        RecordAssessment(project, forms, parsed: false);
        var evidence = CurrentEvidenceQuery.ReadCurrentEvidence(project.Path);
        Assert.True(evidence.Succeeded, evidence.Refusal?.Message);
        var secondary = evidence.Value!.Assessment!.Words.Single(word => word.Word == "beta");
        Assert.Equal(new[] { ReadingGrade.Approved, ReadingGrade.Disapproved, ReadingGrade.Candidate },
            secondary.StoredAnalyses.Select(analysis => analysis.StoredAnalysisOpinion));
        var context = WordContextQuery.Query(new WordContextRequest(project.Path, "beta"));
        Assert.True(context.Succeeded, context.Refusal?.Message);
        Assert.True(context.Value!.IsInFieldWorks);
        Assert.Equal(secondary.StoredAnalyses.Select(analysis => analysis.StoredAnalysisId),
            context.Value.Analyses.Select(analysis => analysis.StoredAnalysisId));
        Assert.All(context.Value.Analyses, analysis => Assert.Equal("es", analysis.Identity!.WritingSystem));
        Assert.NotNull(context.Value.WordAnalysesLink);
        var uses = ObjectUsesQuery.Query(new ObjectUsesRequest(project.Path,
            new ObjectUseRef { AllomorphId = _pristine.Seed.FirstLexemeFormId.ToString("D") }));
        Assert.True(uses.Succeeded, uses.Refusal?.Message);
        Assert.Contains(uses.Value!.Uses!.Words, word => word.Row.Word == "beta");
        if (!addedOnly)
        {
            var primary = evidence.Value.Assessment.Words.Single(word => word.Word == SeededProject.AnalysedWordForm);
            Assert.Equal(primary.StoredAnalyses.Select(analysis => analysis.StoredAnalysisId),
                secondary.StoredAnalyses.Select(analysis => analysis.StoredAnalysisId));
        }
    }

    [Fact]
    public void MissingWarningContextLeavesStoredAndOverviewAffectedWordsUnknown()
    {
        var project = Capture();
        Assert.True(SelectionCommands.SetDefault(new SetDefaultSelectionRequest(
            project.Path, "Default", [], [SeededProject.AnalysedWordForm])).Succeeded);
        RecordAssessment(project, [SeededProject.AnalysedWordForm], parsed: false);
        var check = GrammarCheckQuery.Query(new GrammarCheckRequest(project.Path), new FakeInvoker
        {
            Respond = _ => new PanGlossOutcome.Completed(GrammarHealthReports.With(
                ("allomorph", [new("MoForm", "motifa", _pristine.Seed.FirstLexemeFormId)])),
                string.Empty, TimeSpan.Zero),
        }, CancellationToken.None);
        Assert.True(check.Succeeded, check.Refusal?.Message);
        var finding = Assert.Single(check.Value!.Findings);
        Assert.Single(finding.YourWords!.Words);
        Assert.Equal(WarningAttributionState.ExactUses, finding.AttributionState);
        File.Delete(project.Baseline.FwDataPath);
        var stored = StoredGrammarCheckQuery.Query(new GrammarCheckRequest(project.Path));
        Assert.True(stored.Succeeded, stored.Refusal?.Message);
        var unavailable = Assert.Single(stored.Value!.Check!.Findings);
        Assert.Null(unavailable.YourWords);
        Assert.Equal(WarningAttributionState.EvidenceUnavailable, unavailable.AttributionState);
        Assert.Equal(ProjectionJson.Serialize(finding.Subject), ProjectionJson.Serialize(unavailable.Subject));
        Assert.Equal(finding.AttributionLimits, unavailable.AttributionLimits);
        var overview = SIL.Motif.Commands.Catalog.OverviewCommand.Overview(new OverviewRequest(project.Path));
        Assert.True(overview.Succeeded, overview.Refusal?.Message);
        Assert.Null(overview.Value!.Warnings!.YourWords);
    }

    private CapturedProject Capture(bool analysedWordOutsideText = false, bool includeOtherOpinions = false, string? secondaryForm = null)
    {
        string path;
        Guid textId;
        ParseWordEvidence morphology;
        using (var cache = _pristine.NewScratch())
        {
            var text = SeededProject.SeedText(cache, _pristine.Seed);
            textId = text.TextId;
            if (includeOtherOpinions)
                NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
                {
                    var wordform = cache.ServiceLocator.GetInstance<IWfiWordformRepository>().AllInstances()
                        .Single(word => word.Form.VernacularDefaultWritingSystem.Text == SeededProject.AnalysedWordForm);
                    var source = wordform.HumanApprovedAnalyses.Single();
                    foreach (var opinion in new[] { Opinions.disapproves, Opinions.noopinion })
                    {
                        var analysis = cache.ServiceLocator.GetInstance<IWfiAnalysisFactory>().Create();
                        wordform.AnalysesOC.Add(analysis);
                        analysis.CategoryRA = source.CategoryRA;
                        foreach (var original in source.MorphBundlesOS)
                        {
                            var bundle = cache.ServiceLocator.GetInstance<IWfiMorphBundleFactory>().Create();
                            analysis.MorphBundlesOS.Add(bundle);
                            bundle.MorphRA = original.MorphRA;
                            bundle.MsaRA = original.MsaRA;
                        }
                        cache.LangProject.DefaultUserAgent.SetEvaluation(analysis, opinion);
                    }
                });
            if (analysedWordOutsideText)
                NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
                    cache.ServiceLocator.GetInstance<ITextRepository>().GetObject(text.TextId)
                        .ContentsOA.ParagraphsOS.RemoveAt(0));
            if (secondaryForm is not null)
                NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
                {
                    cache.ServiceLocator.WritingSystemManager.GetOrSet("es", out var writingSystem);
                    var wordform = cache.ServiceLocator.GetInstance<IWfiWordformRepository>().AllInstances()
                        .Single(word => word.Form.VernacularDefaultWritingSystem.Text == SeededProject.AnalysedWordForm);
                    wordform.Form.set_String(writingSystem.Handle, secondaryForm);
                });
            var approved = Assert.Single(ApprovedMorphologyReader.Read(cache)[SeededProject.AnalysedWordForm]);
            morphology = new ParseWordEvidence(ParseMorphEvidence.Schema, 0, SeededProject.AnalysedWordForm, 1,
                false, false, false, [new ParseAnalysis(approved.Morphs.Select(morph =>
                    new ParseMorph(morph.Form, morph.Msa, morph.InflType, null)).ToArray())], []);
            new FwDataProjectLoader().Save(cache);
            path = cache.ProjectId.Path;
        }
        var captured = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(path), _root);
        Assert.True(captured.Succeeded, captured.Refusal?.Message);
        return new CapturedProject(path, textId, captured.Value!, morphology);
    }

    private static void RecordAssessment(CapturedProject project, IReadOnlyList<string> words, bool parsed = true,
        string id = "assessment", string? missedLink = null)
    {
        using var database = ProjectMotifDatabase.Open(project.Path);
        new AssessmentRepository(database).Record(new NewAssessmentRecord(
            id, null, null, "pangloss", AssessmentKinds.ParseTime, "{}", "sha256:scope",
            "whitespace", "1", JsonSerializer.Serialize(project.Baseline.Token, MotifJson.CreateOptions()),
            Selection.Create("Default", words), "sha256:outcome", "sha256:semantic", "sha256:grammar",
            "fingerprint", "pipeline", 0, words.Select(word => new AssessedWord(word,
                parsed ? "analysed" : "no-analysis", [], 1)
            {
                ProjectStanding = ProjectStanding.Approved,
                MissedApproved = missedLink is null ? null : [new ParserReading([
                    new ParserReadingMorph("captured form", "captured gloss", "noun", null, false, missedLink)])],
                Morphology = parsed && word == SeededProject.AnalysedWordForm ? project.Morphology : null,
            }).ToArray()));
    }

    private sealed record CapturedProject(string Path, Guid TextId, BaselineCaptureResponse Baseline,
        ParseWordEvidence Morphology);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
