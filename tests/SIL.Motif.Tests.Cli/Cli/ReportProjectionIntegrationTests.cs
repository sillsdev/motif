using System;
using System.IO;
using SIL.Motif.Cli.Rendering;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Host.Analysis;
using SIL.Motif.Host.Corpus;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Parser;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker.Store;
using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Projection.Usage;
using SIL.Motif.Tests.Projection;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Cli;

/// <summary>
/// Drives the typed command surfaces end to end to verify project data and that a <see cref="UsageLog"/>
/// never records any of it. A single explicit renderer assertion keeps the text and JSON projection aligned.
/// </summary>
[Collection(TestFixtures.LcmCacheTestCollection.Name)]
public sealed class ReportProjectionIntegrationTests
{
    private static string Hash(char digit) => "sha256:" + new string(digit, 64);

    private const string ProductVersion = "1.0";

    private readonly SeededProject _seed;
    private readonly string _fwDataPath;

    public ReportProjectionIntegrationTests(PristineProjectFixture pristine)
    {
        _seed = pristine.Seed;
        using var scratch = pristine.NewScratch();
        _fwDataPath = scratch.ProjectId.Path;
    }

    /// <summary>The paired project database <c>analyses --assessment</c> now reads Assessments from.</summary>
    private string AssessmentDatabasePath() => ProjectDatabaseCatalog.DatabasePathFor(
        new ProjectLocator(_fwDataPath, Path.GetFileNameWithoutExtension(_fwDataPath)));


    [Fact]
    public void OpenOutcomeReportsTheLexicalEntryCount()
    {
        var usage = new UsageLog();
        var text = ProposalCommands.Open(new OpenRequest(_fwDataPath), usage);
        var json = ProposalCommands.Open(new OpenRequest(_fwDataPath), usage);

        Assert.True(text.Succeeded);
        Assert.True(json.Succeeded);
        Assert.Equal(2, json.Value!.LexicalEntryCount);
    }

    [Fact]
    public void AnalysesReadsTheManualAggregateWithoutRecordingProjectData()
    {
        var usage = new UsageLog();

        var text = ProposalCommands.Analyses(new ManualAnalysesRequest(_fwDataPath), usage);
        var json = ProposalCommands.Analyses(new ManualAnalysesRequest(_fwDataPath), usage);

        Assert.True(text.Succeeded);
        Assert.True(json.Succeeded);
        Assert.Contains("No assessment is on record", text.Value!.AssessmentState, StringComparison.Ordinal);
        Assert.Equal(0, json.Value!.WordFormCount);
        Assert.Empty(json.Value.WordForms);
        Assert.Equal(2, usage.Entries.Count);
        Assert.All(usage.Entries, entry =>
        {
            Assert.Equal("analyses", entry.Command);
            Assert.Equal(new[] { "fwDataPath:text" }, entry.ArgumentShape);
            Assert.DoesNotContain(_fwDataPath, string.Join(" ", entry.ArgumentShape), StringComparison.Ordinal);
        });
    }

    [Fact]
    public void AnalysesLoadsNamedAssessmentFromSqliteWithoutRecordingValues()
    {
        var assessment = new StoredAssessment(
            new AssessReport(
                Array.Empty<AssessedWord>(),
                "outcome",
                "semantic",
                Hash('a'),
                "model",
                "pipeline",
                0),
            Selection.Create("corpus-one", Array.Empty<string>()));
        var assessmentId = SeededAssessment.Record(_fwDataPath, assessment, CanonicalId.Mint("assessment/").Value);
        var usage = new UsageLog();

        var text = ProposalCommands.Analyses(new AssessmentAnalysesRequest(
            _fwDataPath, ProductVersion, assessmentId, assessment.Selection.Sha256,
            assessment.Report.GrammarSourceSha256), usage);
        var json = ProposalCommands.Analyses(new AssessmentAnalysesRequest(
            _fwDataPath, ProductVersion, assessmentId, Hash('b'),
            assessment.Report.GrammarSourceSha256), usage);

        Assert.True(text.Succeeded);
        Assert.True(json.Succeeded);
        Assert.Contains("still describes the current project", text.Value!.AssessmentState, StringComparison.Ordinal);
        Assert.Contains("selection has changed", json.Value!.AssessmentState, StringComparison.Ordinal);
        Assert.Equal(0, json.Value.UnanalysedReach!.UnanalysedCount);
        Assert.Equal(0, json.Value.UnanalysedReach.ParsedCount);
        Assert.All(usage.Entries, entry => Assert.Equal(
            new[]
            {
                "fwDataPath:text",
                "assessmentId:text",
                "currentSelectionSha256:text",
                "currentGrammarSourceSha256:text",
            },
            entry.ArgumentShape));
        var usageText = string.Join(" ", usage.Entries.SelectMany(entry => entry.ArgumentShape));
        Assert.DoesNotContain(_fwDataPath, usageText, StringComparison.Ordinal);
        Assert.DoesNotContain(assessmentId, usageText, StringComparison.Ordinal);
        Assert.DoesNotContain(assessment.Selection.Sha256, usageText, StringComparison.Ordinal);
    }

    [Fact]
    public void OrderedMorphologyAggregateReturnsRetainedCasesAndFrozenExpectations()
    {
        var word = CorrectnessFixture.Word("approved", matched: true);
        var assessment = new StoredAssessment(
            new AssessReport([word], "outcome", "semantic", Hash('a'), "model", "pipeline", 0),
            Selection.Create("corpus-one", [word.Word]));
        var assessmentId = SeededAssessment.Record(_fwDataPath, assessment, CanonicalId.Mint("assessment/").Value);

        var result = ProposalCommands.Analyses(new AssessmentAnalysesRequest(
            _fwDataPath, ProductVersion, assessmentId, assessment.Selection.Sha256,
            assessment.Report.GrammarSourceSha256));

        Assert.True(result.Succeeded);
        var analysisCase = Assert.Single(result.Value!.AssessmentCases!);
        Assert.Equal("approved", analysisCase.Morphology.Word);
        Assert.Equal("covered", analysisCase.Correctness!.Status);
        Assert.Equal(word.Morphology!.Analyses[0].Morphs[0].Form,
            analysisCase.Morphology.Analyses[0].Morphs[0].Form);
        using var database = MotifDatabase.OpenOwned(AssessmentDatabasePath(),
            new ProjectLocator(_fwDataPath, Path.GetFileNameWithoutExtension(_fwDataPath)),
            MotifSchema.CurrentSchema, new Version(1, 0));
        var retained = Assert.Single(new AssessmentRepository(database).Get(assessmentId).Words!);
        Assert.Equal("covered", retained.Correctness!.Status);
        Assert.Equal(word.Morphology!.Analyses[0].Morphs[0], retained.Morphology!.Analyses[0].Morphs[0]);
    }

    [Fact]
    public void EmptyCorrectnessMeasurementReturnsAnEmptyRecordedCaseList()
    {
        var assessmentId = CanonicalId.Mint("assessment/").Value;
        var selection = Selection.Create("empty", []);
        var project = new ProjectLocator(_fwDataPath, Path.GetFileNameWithoutExtension(_fwDataPath));
        using (var database = MotifDatabase.OpenOwned(AssessmentDatabasePath(), project,
            MotifSchema.CurrentSchema, new Version(1, 0)))
        {
            new AssessmentRepository(database).Record(new NewAssessmentRecord(
                assessmentId, null, null, "pangloss", "Correctness", "{}", "sha256:scope",
                "whitespace-and-punctuation", "1", "{}", selection, null, null, Hash('a'), null, null, null, []));
        }

        var result = ProposalCommands.Analyses(new AssessmentAnalysesRequest(
            _fwDataPath, ProductVersion, assessmentId, selection.Sha256, Hash('a')));

        Assert.True(result.Succeeded);
        Assert.Empty(result.Value!.AssessmentCases!);
        Assert.Contains("still describes the current project", result.Value.AssessmentState, StringComparison.Ordinal);
    }

    [Fact]
    public void AggregatePreservesRepeatedCasesAndRecomputesIncompleteMatchesFromFrozenEvidence()
    {
        var first = CorrectnessFixture.Word("same", matched: true);
        first = first with
        {
            Morphology = first.Morphology! with { Capped = true, TimedOut = true, Unavailable = ["unresolved extra reading"] },
            Correctness = first.Correctness! with { Status = "covered", Matched = 99 },
        };
        var second = CorrectnessFixture.Word("same", matched: false);
        second = second with { Morphology = second.Morphology! with { Index = 1 } };
        var assessment = new StoredAssessment(
            new AssessReport([first, second], "outcome", "semantic", Hash('a'), "model", "pipeline", 0),
            Selection.Create("repeated", ["same", "same"]));
        var id = SeededAssessment.Record(_fwDataPath, assessment, CanonicalId.Mint("assessment/").Value);

        var result = ProposalCommands.Analyses(new AssessmentAnalysesRequest(
            _fwDataPath, ProductVersion, id, Hash('b'), Hash('c')));
        var text = ProposalCommands.Analyses(new AssessmentAnalysesRequest(
            _fwDataPath, ProductVersion, id, Hash('b'), Hash('c')));

        Assert.True(result.Succeeded);
        Assert.True(text.Succeeded);
        var cases = result.Value!.AssessmentCases!;
        var firstCorrectness = cases[0].Correctness!;
        Assert.Equal(2, cases.Count);
        Assert.Equal(0, cases[0].Morphology.Index);
        Assert.Equal(1, cases[1].Morphology.Index);
        Assert.Equal("incomplete", firstCorrectness.Status);
        Assert.Equal(1, firstCorrectness.Matched);
        Assert.Equal("unmatched", cases[1].Correctness!.Status);
        Assert.True(cases[0].Morphology.Capped);
        Assert.True(cases[0].Morphology.TimedOut);
        Assert.Contains("unresolved extra reading", firstCorrectness.Unavailable, StringComparer.Ordinal);
        Assert.Equal(first.Morphology!.Analyses[0].Morphs[0].Form,
            cases[0].Morphology.Analyses[0].Morphs[0].Form);
        Assert.Contains("selection has changed", result.Value.AssessmentState, StringComparison.Ordinal);
        Assert.Contains("grammar has changed", result.Value.AssessmentState, StringComparison.Ordinal);
        Assert.Null(result.Value.UnanalysedReach);
    }

    [Fact]
    public void TimingOnlyAggregateDoesNotInventApprovedExpectations()
    {
        var word = CorrectnessFixture.Word("timed", matched: true) with { Correctness = null };
        var assessment = new StoredAssessment(
            new AssessReport([word], "outcome", "semantic", Hash('a'), "model", "pipeline", 0),
            Selection.Create("timed", [word.Word]));
        var id = SeededAssessment.Record(_fwDataPath, assessment, CanonicalId.Mint("assessment/").Value);

        var result = ProposalCommands.Analyses(new AssessmentAnalysesRequest(
            _fwDataPath, ProductVersion, id, assessment.Selection.Sha256, Hash('a')));
        var text = ProposalCommands.Analyses(new AssessmentAnalysesRequest(
            _fwDataPath, ProductVersion, id, assessment.Selection.Sha256, Hash('a')));

        Assert.True(result.Succeeded);
        Assert.True(text.Succeeded);
        var analysisCase = Assert.Single(result.Value!.AssessmentCases!);
        Assert.Null(analysisCase.Correctness);
        Assert.Equal(word.Morphology!.ElapsedMs, analysisCase.Morphology.ElapsedMs);
    }

    [Fact]
    public void AnEmptyResultCarriesTheGrammarFindingsThatMayExplainIt()
    {
        var word = CorrectnessFixture.Word("dkat", matched: false);
        var assessment = new StoredAssessment(
            new AssessReport([word], "outcome", "semantic", Hash('a'), "model", "pipeline", 0),
            Selection.Create("findings", [word.Word]));
        var invocation = new BatchInvocationEvidence(
            "findings-run", "source.fwdata", "sha256:source", "sha256:executable", "words.txt", "sha256:words",
            "rows.tsv", "sha256:rows", "stderr.txt", "sha256:stderr", 1000, 200000, 1, false)
        {
            GrammarWarnings = string.Join('\n',
                "warning: no boundary marker representation '+' found",
                "warning: circumfix allomorph \"a3547f67\": cannot segment \"d+\"; skipped"),
        };
        var id = SeededAssessment.Record(_fwDataPath, assessment, CanonicalId.Mint("assessment/").Value, invocation);

        var result = ProposalCommands.Analyses(new AssessmentAnalysesRequest(
            _fwDataPath, ProductVersion, id, assessment.Selection.Sha256, Hash('a')));
        var text = ProposalCommands.Analyses(new AssessmentAnalysesRequest(
            _fwDataPath, ProductVersion, id, assessment.Selection.Sha256, Hash('a')));

        Assert.True(result.Succeeded);
        Assert.True(text.Succeeded);
        Assert.Equal(2, result.Value!.GrammarWarnings!.Count);
        Assert.Contains("cannot segment", result.Value.GrammarWarnings[1], StringComparison.Ordinal);
        var analysisCase = Assert.Single(result.Value.AssessmentCases!);
        Assert.Empty(analysisCase.Morphology.Analyses);
        Assert.Contains(result.Value.GrammarWarnings,
            warning => warning.Contains("circumfix allomorph", StringComparison.Ordinal));
    }

    [Fact]
    public void AQuietGrammarAddsNoFindingsBlockAndExplainsNothingAway()
    {
        var word = CorrectnessFixture.Word("quiet", matched: false);
        var assessment = new StoredAssessment(
            new AssessReport([word], "outcome", "semantic", Hash('a'), "model", "pipeline", 0),
            Selection.Create("quiet", [word.Word]));
        var id = SeededAssessment.Record(_fwDataPath, assessment, CanonicalId.Mint("assessment/").Value);

        var result = ProposalCommands.Analyses(new AssessmentAnalysesRequest(
            _fwDataPath, ProductVersion, id, assessment.Selection.Sha256, Hash('a')));
        var text = ProposalCommands.Analyses(new AssessmentAnalysesRequest(
            _fwDataPath, ProductVersion, id, assessment.Selection.Sha256, Hash('a')));

        Assert.True(result.Succeeded);
        Assert.True(text.Succeeded);
        Assert.Null(result.Value!.GrammarWarnings);
        Assert.Empty(Assert.Single(result.Value.AssessmentCases!).Morphology.Analyses);
    }

    [Theory]
    [InlineData("index")]
    [InlineData("word")]
    [InlineData("morphology")]
    [InlineData("expectations")]
    [InlineData("expected-guid")]
    public void AggregateRefusesMalformedRecordedCasesWithoutRepairingThem(string defect)
    {
        var word = CorrectnessFixture.Word("recorded", matched: true);
        word = defect switch
        {
            "index" => word with { Morphology = word.Morphology! with { Index = 1 } },
            "word" => word with { Morphology = word.Morphology! with { Word = "different" } },
            "morphology" => word with { Morphology = null },
            "expected-guid" => word with { Correctness = word.Correctness! with
                { Expectations = [new([new("malformed", word.Morphology!.Analyses[0].Morphs[0].Msa, null, [])])] } },
            _ => word with { Correctness = null },
        };
        var id = CanonicalId.Mint("assessment/").Value;
        var selection = Selection.Create("recorded", [word.Word]);
        var project = new ProjectLocator(_fwDataPath, Path.GetFileNameWithoutExtension(_fwDataPath));
        using (var database = MotifDatabase.OpenOwned(AssessmentDatabasePath(), project,
            MotifSchema.CurrentSchema, new Version(1, 0)))
        {
            new AssessmentRepository(database).Record(new NewAssessmentRecord(
                id, null, null, "pangloss", "Correctness", "{}", "sha256:scope",
                "whitespace-and-punctuation", "1", "{}", selection, null, null, Hash('a'), null, null, null, [word]));
        }

        var result = ProposalCommands.Analyses(new AssessmentAnalysesRequest(
            _fwDataPath, ProductVersion, id, selection.Sha256, Hash('a')));

        Assert.False(result.Succeeded);
        Assert.Equal("assessment.invalid-evidence", result.Refusal!.Code);
        Assert.Equal(FailureReason.Refused, result.Refusal.Reason);
    }

    [Fact]
    public void AggregateRefusesNonWordMeasurementsEvenWithCompleteLegacyMetadata()
    {
        var id = CanonicalId.Mint("assessment/").Value;
        var selection = Selection.Create("objects", []);
        var project = new ProjectLocator(_fwDataPath, Path.GetFileNameWithoutExtension(_fwDataPath));
        using (var database = MotifDatabase.OpenOwned(AssessmentDatabasePath(), project,
            MotifSchema.CurrentSchema, new Version(1, 0)))
        {
            new AssessmentRepository(database).Record(new NewAssessmentRecord(
                id, null, null, "pangloss", "ObjectTiming", "{}", "sha256:scope",
                "whitespace-and-punctuation", "1", "{}", selection, "outcome", "semantic", Hash('a'), "model", "pipeline", 0, []));
        }

        var result = ProposalCommands.Analyses(new AssessmentAnalysesRequest(
            _fwDataPath, ProductVersion, id, selection.Sha256, Hash('a')));

        Assert.False(result.Succeeded);
        Assert.Equal("assessment.aggregate-unavailable", result.Refusal!.Code);
        Assert.Equal(FailureReason.Refused, result.Refusal.Reason);
    }

    [Fact]
    public void AnalysesReturnsClearErrorWhenNamedAssessmentDoesNotExist()
    {
        // No corpus or proposal verb has touched this scratch project, so its paired database does not exist.
        var missingId = Hash('0');
        var result = ProposalCommands.Analyses(new AssessmentAnalysesRequest(
            _fwDataPath, ProductVersion, missingId, Hash('1'), Hash('2')));

        Assert.False(result.Succeeded);
        Assert.Equal("assessment.not-found", result.Refusal!.Code);
        Assert.Equal(FailureReason.NotFound, result.Refusal.Reason);
        Assert.Equal(missingId, result.Refusal.Facts["assessmentId"]);
    }

    [Theory]
    [InlineData("", "sha256:1111111111111111111111111111111111111111111111111111111111111111", "sha256:2222222222222222222222222222222222222222222222222222222222222222")]
    [InlineData("sha256:abc", "sha256:1111111111111111111111111111111111111111111111111111111111111111", "sha256:2222222222222222222222222222222222222222222222222222222222222222")]
    [InlineData("sha256:0000000000000000000000000000000000000000000000000000000000000000", "true", "sha256:2222222222222222222222222222222222222222222222222222222222222222")]
    [InlineData("sha256:0000000000000000000000000000000000000000000000000000000000000000", "sha256:1111111111111111111111111111111111111111111111111111111111111111", "SHA256:2222222222222222222222222222222222222222222222222222222222222222")]
    public void AssessmentCommandsRejectMalformedIdentifiers(
        string assessmentId,
        string currentSelectionSha256,
        string currentGrammarSha256)
    {
        var result = ProposalCommands.Analyses(new AssessmentAnalysesRequest(
            _fwDataPath, ProductVersion, assessmentId, currentSelectionSha256, currentGrammarSha256));

        Assert.False(result.Succeeded);
        Assert.Equal("assessment.invalid-id", result.Refusal!.Code);
        Assert.Equal(FailureReason.InvalidArgument, result.Refusal.Reason);
        // Each malformed field is rejected by name; an assessment id and a digest have different shapes.
        Assert.Contains("is required", result.Refusal.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AnalysesJoinsStoredAutomaticResultsToRealManuallyAnalysedWordforms()
    {
        SeedApprovedWordform("zzAssessmentParsed");
        SeedApprovedWordform("zzAssessmentEmpty");
        SeedApprovedWordform("zzAssessmentUncovered");

        var assessment = new StoredAssessment(
            new AssessReport(
                new[]
                {
                    new AssessedWord(
                        "zzAssessmentParsed",
                        "Analysed",
                        new[]
                        {
                            new ParsedAnalysis(
                                null,
                                new[] { _seed.FirstLexemeFormId.ToString() },
                                0,
                                "automatic-real-join"),
                        }),
                    new AssessedWord("zzAssessmentEmpty", "NoAnalysis", Array.Empty<ParsedAnalysis>()),
                },
                "outcome",
                "semantic",
                Hash('c'),
                "model",
                "pipeline",
                0),
            Selection.Create(
                "real-join",
                new[] { "zzAssessmentParsed", "zzAssessmentEmpty" }));
        var assessmentId = SeededAssessment.Record(_fwDataPath, assessment, CanonicalId.Mint("assessment/").Value);

        var result = ProposalCommands.Analyses(new AssessmentAnalysesRequest(
            _fwDataPath, ProductVersion, assessmentId, assessment.Selection.Sha256,
            assessment.Report.GrammarSourceSha256));

        Assert.True(result.Succeeded);
        var wordforms = result.Value!.WordForms.ToDictionary(wordform => wordform.Form, StringComparer.Ordinal);
        Assert.Equal("automatic-real-join", Assert.Single(wordforms["zzAssessmentParsed"].AutomaticAnalyses!).ContentDigest);
        Assert.Equal(1, wordforms["zzAssessmentParsed"].AutomaticAnalysisCount);
        Assert.Empty(wordforms["zzAssessmentEmpty"].AutomaticAnalyses!);
        Assert.Equal(0, wordforms["zzAssessmentEmpty"].AutomaticAnalysisCount);
        Assert.Null(wordforms["zzAssessmentUncovered"].AutomaticAnalyses);
        Assert.Null(wordforms["zzAssessmentUncovered"].AutomaticAnalysisCount);
    }

    private void SeedApprovedWordform(string form)
    {
        var loader = new SIL.Motif.Host.LcmUtils.FwDataProjectLoader();
        using var cache = loader.LoadScratchCache(_fwDataPath);
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var wordform = cache.ServiceLocator.GetInstance<IWfiWordformFactory>()
                .Create(TsStringUtils.MakeString(form, cache.DefaultVernWs));
            var analysis = cache.ServiceLocator.GetInstance<IWfiAnalysisFactory>().Create();
            wordform.AnalysesOC.Add(analysis);
            cache.LangProject.DefaultUserAgent.SetEvaluation(analysis, Opinions.approves);
        });
        loader.Save(cache);
    }

    [Fact]
    public void FullLoopTypedOutcomesCarryProjectFacts_AndUsageLogStaysDataFree()
    {
        var senseGuid = _seed.FirstSenseId;
        var wsTag = NewLangProjFixture.AnalysisTag;
        var originalGloss = SeededProject.FirstGloss;
        var canonicalId = SIL.Motif.Contract.Ids.CanonicalId.FromGuid(senseGuid);
        var newGloss = originalGloss + " (revised, report projection test)";
        const string draftName = "report-projection-demo";
        const string label = "report-projection-1";
        const string applier = "report-projection-tests";

        var usage = new UsageLog();

        var created = ProposalCommands.New(new NewDraftRequest(_fwDataPath, ProductVersion, draftName, label));
        Assert.True(created.Succeeded);
        var added = ProposalCommands.AddSetGloss(new AddSetGlossRequest(
            _fwDataPath, ProductVersion, draftName, canonicalId.Value, wsTag, newGloss));
        Assert.True(added.Succeeded);
        DraftRationale.Author(
            _fwDataPath, draftName, label, "Explain why this lexical gloss should replace the current analysis.");
        var finalize = ProposalCommands.Finalize(new FinalizeRequest(_fwDataPath, ProductVersion, draftName));
        Assert.True(finalize.Succeeded);
        var proposalId = finalize.Value!.ProposalId;

        // list
        var listText = ProposalCommands.List(new ListProposalsRequest(_fwDataPath, ProductVersion), usage);
        var listJson = ProposalCommands.List(new ListProposalsRequest(_fwDataPath, ProductVersion), usage);
        Assert.True(listText.Succeeded);
        Assert.True(listJson.Succeeded);
        Assert.Equal(proposalId, Assert.Single(listJson.Value!.Proposals).ProposalId);

        // show
        var showText = ProposalCommands.Show(new ShowProposalRequest(_fwDataPath, ProductVersion, proposalId), usage);
        var showJson = ProposalCommands.Show(new ShowProposalRequest(_fwDataPath, ProductVersion, proposalId), usage);
        Assert.True(showText.Succeeded);
        Assert.True(showJson.Succeeded);
        Assert.Contains(showJson.Value!.Operations, operation => operation.Target == canonicalId.Value);
        FigureAudit.AssertEveryTextFigureAppearsInJson(
            ProposalCommandRenderer.Render(showText, asJson: false).Output,
            ProposalCommandRenderer.Render(showJson, asJson: true).Output);

        // Dry Run is a job; the helper drains it and returns the typed result.
        var dryRunText = DryRunJobRunner.Run(_fwDataPath, ProductVersion, proposalId, usage);
        var dryRunJson = DryRunJobRunner.Run(_fwDataPath, ProductVersion, proposalId, usage);
        Assert.True(dryRunText.Succeeded);
        Assert.True(dryRunJson.Succeeded);
        var dryRunChange = Assert.Single(Assert.Single(dryRunJson.Value!.Effects).Changes);
        Assert.Equal(originalGloss, dryRunChange.Before);
        Assert.Equal(newGloss, dryRunChange.After);

        // Apply mutates once, so its effect and Receipt are checked against project state.
        var applyJson = ProposalCommands.Apply(
            new ApplyRequest(_fwDataPath, ProductVersion, proposalId, applier, Force: true), usage);
        Assert.True(applyJson.Succeeded);
        var appliedChange = Assert.Single(Assert.Single(applyJson.Value!.Effects).Changes);
        Assert.Equal(originalGloss, appliedChange.Before);
        Assert.Equal(newGloss, appliedChange.After);
        Assert.Equal(applier, applyJson.Value.AppliedLogEntry.User);
        Assert.Equal(proposalId, applyJson.Value.ProposalId);

        // log
        var logText = ProposalCommands.Log(new LogRequest(_fwDataPath), usage);
        var logJson = ProposalCommands.Log(new LogRequest(_fwDataPath), usage);
        Assert.True(logText.Succeeded);
        Assert.True(logJson.Succeeded);
        Assert.Equal(applier, Assert.Single(logJson.Value!.Entries).User);

        // usage log: recorded every real call above, but never a scrap of the real project data.
        Assert.Equal(9, usage.Entries.Count);
        foreach (var entry in usage.Entries)
        {
            AssertNever(entry.Command, originalGloss, newGloss, canonicalId.Value, proposalId, applier, _fwDataPath);
            foreach (var token in entry.ArgumentShape)
                AssertNever(token, originalGloss, newGloss, canonicalId.Value, proposalId, applier, _fwDataPath);
        }

        var summary = usage.Summarize();
        Assert.Equal(2, summary.CallCounts["list"]);
        Assert.Equal(2, summary.CallCounts["show"]);
        Assert.Equal(2, summary.CallCounts["dry-run"]);
        Assert.Equal(1, summary.CallCounts["apply"]);
        Assert.Equal(2, summary.CallCounts["log"]);
    }

    private static void AssertNever(string haystack, params string[] secrets)
    {
        foreach (var secret in secrets)
            Assert.DoesNotContain(secret, haystack, StringComparison.Ordinal);
    }

}
