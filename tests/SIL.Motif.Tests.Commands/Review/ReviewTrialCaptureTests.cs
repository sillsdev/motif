using SIL.Motif.Commands;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Commands.Review;
using System.Collections;
using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Review;
using SIL.Motif.Host;
using SIL.Motif.Host.Config;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.Corpus;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Parser;
using SIL.Motif.Host.Store;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Runtime.Review;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands.Review;

[Collection(LcmCacheParallelCollections.Group0)]
public sealed class ReviewTrialCaptureTests(PristineProjectFixture pristine)
{
    [Fact]
    public void ChangedDraftCannotQueueCapturedInputsOrPassTheAtomicInsertCheck()
    {
        string path;
        using (var cache = pristine.NewScratch())
        {
            new FwDataProjectLoader().Save(cache);
            path = cache.ProjectId.Path;
        }
        var root = Directory.CreateTempSubdirectory("motif-review-revision-").FullName;
        try
        {
            var baseline = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(path), root);
            Assert.True(baseline.Succeeded, baseline.Refusal?.Message);
            var draft = SIL.Motif.Commands.ProposalCommands.New(new(path, "1.0", "capture-revision", null));
            Assert.True(draft.Succeeded, draft.Refusal?.Message);
            var project = new ProjectLocator(path, Path.GetFileNameWithoutExtension(path));
            using var database = MotifDatabase.OpenOwned(ProjectDatabaseCatalog.DatabasePathFor(project), project,
                MotifSchema.CurrentSchema, new Version(1, 0));
            var repository = new ProposalRepository(database);
            AddWordChange(repository, "capturealpha");
            var request = new EnqueueTrialRequest(path, "1.0", draft.Value!.ProposalId);
            var frozen = SIL.Motif.Commands.JobCommands.FreezeTrialProposal(request);
            Assert.True(frozen.Succeeded, frozen.Refusal?.Message);
            var words = SIL.Motif.Commands.JobCommands.ResolveTrialWords(frozen.Value!);
            Assert.True(words.Succeeded, words.Refusal?.Message);
            Assert.Equal("capturealpha", Assert.Single(words.Value!));
            var captured = ReviewTrialCapture.Capture(frozen.Value!, words.Value, null);
            Assert.True(captured.Succeeded, captured.Refusal?.Message);
            Assert.NotNull(captured.Value!.ReviewInputs);

            var original = repository.GetDraft("capture-revision");
            AddWordChange(repository, "capturebeta");

            var queued = SIL.Motif.Commands.JobCommands.EnqueueTrialAdmitted(captured.Value, null, null);
            Assert.False(queued.Succeeded);
            Assert.Equal(SIL.Motif.Contract.Commands.RefusalCodes.TrialReviewInputUnavailable, queued.Refusal!.Code);
            Assert.Empty(Directory.GetFiles(database.FullPath + ".review-inputs", "*.json"));
            var atomic = SIL.Motif.Commands.JobCommands.QueueTrial(new JobRepository(database), original,
                SIL.Motif.Contract.Ids.CanonicalId.Mint("job/").Value,
                SIL.Motif.Worker.Projects.ProjectWorkspaceKey.Compute(project), "{}", null);
            Assert.False(atomic.Succeeded);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void TrialExportRetainsAnExplicitWitnessAlreadyInAConfiguredSet()
    {
        const string alpha = "ÉAlpha";
        const string beta = "Beta";
        string path;
        using (var cache = pristine.NewScratch())
        {
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            {
                var factory = cache.ServiceLocator.GetInstance<IWfiWordformFactory>();
                foreach (var word in new[] { alpha, beta })
                    factory.Create(TsStringUtils.MakeString(word, cache.DefaultVernWs));
            });
            new FwDataProjectLoader().Save(cache);
            path = cache.ProjectId.Path;
        }
        var root = Directory.CreateTempSubdirectory("motif-review-witness-").FullName;
        try
        {
            var baseline = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(path), root);
            Assert.True(baseline.Succeeded, baseline.Refusal?.Message);
            var draft = ProposalCommands.New(new(path, "1.0", "capture-revision", null));
            Assert.True(draft.Succeeded, draft.Refusal?.Message);
            var project = new ProjectLocator(path, Path.GetFileNameWithoutExtension(path));
            File.WriteAllText(ProjectConfigurationReader.PathFor(project),
                "[[review-word-set]]\nid = \"gold\"\nname = \"Gold\"\nrole = \"regression\"\nselection = \"Gold words\"\n");
            using var database = MotifDatabase.OpenOwned(ProjectDatabaseCatalog.DatabasePathFor(project), project,
                MotifSchema.CurrentSchema, new Version(1, 0));
            new NamedSelectionRepository(database).SetDefault("Gold words", [],
                [alpha.Normalize(System.Text.NormalizationForm.FormD), beta],
                new SelectionParsingLimits(SIL.Motif.Contract.Assess.ParserWorkLimit.Default,
                    SelectionTimeLimitMode.Estimated, null), null);
            AddWordChange(new ProposalRepository(database), "unrelated");

            (TrialExportReceipt Receipt, JsonElement[] Rows, string InputDigest) Run(string list)
            {
                var wordsPath = Path.Combine(root, "words.txt");
                File.WriteAllText(wordsPath, list);
                var words = SIL.Motif.Commands.Assess.WordListFile.Resolve(null, wordsPath);
                Assert.True(words.Succeeded, words.Refusal?.Message);
                var request = new EnqueueTrialRequest(path, "1.0", draft.Value!.ProposalId, Words: words.Value);
                var job = TrialBaselines.EnqueueWithBaseline(request, (_, _, _, _, _, _) =>
                    SIL.Motif.Contract.Commands.CommandOutcome<AssessCommandResponse>.Refused(new(
                        SIL.Motif.Contract.Commands.RefusalCodes.AssessmentStoppedByBudget,
                        SIL.Motif.Contract.Responses.FailureReason.StoppedByBudget, "Stopped")));
                Assert.True(job.Succeeded, job.Refusal?.Message);
                var outcome = TrialExportReads.Read(new(path, job.Value!.JobId));
                Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
                var receipt = outcome.Value!;
                Assert.NotNull(receipt.ReportDigest);
                using var document = JsonDocument.Parse(File.ReadAllBytes(receipt.Path));
                var saved = new JobRepository(database).Get(job.Value.JobId)!;
                var input = TrialJobInput.Parse(saved.InputJson);
                var evidence = ReviewInputArtifacts.Read(database, input.ReviewInputs!);
                return (receipt, document.RootElement.GetProperty("words").EnumerateArray()
                    .Select(row => row.Clone()).ToArray(), evidence.Inputs.EvidenceManifestDigest);
            }

            var first = Run("  " + alpha + "\n\n" + alpha.Normalize(System.Text.NormalizationForm.FormD) + "\n");
            var second = Run(beta + "\n");
            Assert.Equal(first.Rows.Select(row => row.GetProperty("caseKey").GetString()),
                second.Rows.Select(row => row.GetProperty("caseKey").GetString()));
            Assert.NotEqual(first.InputDigest, second.InputDigest);
            Assert.NotEqual(first.Receipt.ReportDigest, second.Receipt.ReportDigest);
            foreach (var (run, explicitForm) in new[] { (first, alpha), (second, beta) })
            {
                var marked = Assert.Single(run.Rows, row => row.GetProperty("explicitInput").ValueKind != JsonValueKind.Null);
                Assert.Equal(explicitForm, marked.GetProperty("explicitInput").GetProperty("submittedForm").GetString());
                Assert.Equal(1, marked.GetProperty("explicitInput").GetProperty("ordinal").GetInt32());
                Assert.Equal("gold", Assert.Single(marked.GetProperty("memberships").EnumerateArray())
                    .GetProperty("setId").GetString());
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void DeclaredGateWordSetsCannotBeSuppressedByReviewSetNone()
    {
        string path;
        using (var cache = pristine.NewScratch())
        {
            new FwDataProjectLoader().Save(cache);
            path = cache.ProjectId.Path;
        }
        var root = Directory.CreateTempSubdirectory("motif-review-declared-gate-").FullName;
        try
        {
            File.WriteAllText(Path.ChangeExtension(path, ".motif.toml"),
                "[gate]\nword-sets = [\"gold\"]\n[[review-word-set]]\nid = \"gold\"\nname = \"Gold\"\nrole = \"regression\"\nselection = \"Gold words\"\n");
            var baseline = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(path), root);
            Assert.True(baseline.Succeeded, baseline.Refusal?.Message);
            var draft = SIL.Motif.Commands.ProposalCommands.New(new(path, "1.0", "capture-revision", null));
            Assert.True(draft.Succeeded, draft.Refusal?.Message);
            var project = new ProjectLocator(path, Path.GetFileNameWithoutExtension(path));
            using var database = MotifDatabase.OpenOwned(ProjectDatabaseCatalog.DatabasePathFor(project), project,
                MotifSchema.CurrentSchema, new Version(1, 0));
            new NamedSelectionRepository(database).SetDefault("Gold words", [], ["goldword"],
                new SIL.Motif.Contract.Requests.SelectionParsingLimits(SIL.Motif.Contract.Assess.ParserWorkLimit.Default,
                    SIL.Motif.Contract.Requests.SelectionTimeLimitMode.Estimated, null), null);
            AddWordChange(new ProposalRepository(database), "gatealpha");
            var frozen = SIL.Motif.Commands.JobCommands.FreezeTrialProposal(new EnqueueTrialRequest(path, "1.0", draft.Value!.ProposalId)
                { ReviewSetIds = [] });
            Assert.True(frozen.Succeeded, frozen.Refusal?.Message);
            var captured = ReviewTrialCapture.Capture(frozen.Value!, ["gatealpha"], null);
            Assert.True(captured.Succeeded, captured.Refusal?.Message);
            var inputs = ReviewInputArtifacts.Read(database, captured.Value!.ReviewInputs!).Inputs;
            Assert.Contains(inputs.Cases, item => item.Word == "goldword" && item.Memberships.Any(member => member.SetId == "gold"));
            Assert.DoesNotContain("gold", inputs.OmittedSetIds);
            Assert.NotNull(inputs.DeclaredGate);
            Assert.NotEmpty(inputs.DeclaredGate.ScopeDigest);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void ANoBaseSampleTrialParsesExactlyTheListedWordsAndIsMarkedAsAWorkingSet()
    {
        string path;
        using (var cache = pristine.NewScratch())
        {
            new FwDataProjectLoader().Save(cache);
            path = cache.ProjectId.Path;
        }
        var root = Directory.CreateTempSubdirectory("motif-review-working-set-").FullName;
        try
        {
            File.WriteAllText(Path.ChangeExtension(path, ".motif.toml"),
                "[gate]\nword-sets = [\"gold\"]\n[[review-word-set]]\nid = \"gold\"\nname = \"Gold\"\nrole = \"regression\"\nselection = \"Gold words\"\n");
            var baseline = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(path), root);
            Assert.True(baseline.Succeeded, baseline.Refusal?.Message);
            var draft = SIL.Motif.Commands.ProposalCommands.New(new(path, "1.0", "capture-revision", null));
            Assert.True(draft.Succeeded, draft.Refusal?.Message);
            var project = new ProjectLocator(path, Path.GetFileNameWithoutExtension(path));
            using var database = MotifDatabase.OpenOwned(ProjectDatabaseCatalog.DatabasePathFor(project), project,
                MotifSchema.CurrentSchema, new Version(1, 0));
            new NamedSelectionRepository(database).SetDefault("Gold words", [], ["goldone", "goldtwo", "goldthree"],
                new SIL.Motif.Contract.Requests.SelectionParsingLimits(SIL.Motif.Contract.Assess.ParserWorkLimit.Default,
                    SIL.Motif.Contract.Requests.SelectionTimeLimitMode.Estimated, null), null);
            AddWordChange(new ProposalRepository(database), "unrelated");
            string[] listed = ["listone", "listtwo"];

            (TrialJobInput Input, FrozenReviewInputs Inputs) Queue(bool noBaseSample)
            {
                var request = new EnqueueTrialRequest(path, "1.0", draft.Value!.ProposalId, Words: listed,
                    NoBaseSample: noBaseSample);
                var job = TrialBaselines.EnqueueWithBaseline(request, (_, _, _, _, _, _) =>
                    SIL.Motif.Contract.Commands.CommandOutcome<AssessCommandResponse>.Refused(new(
                        SIL.Motif.Contract.Commands.RefusalCodes.AssessmentStoppedByBudget,
                        SIL.Motif.Contract.Responses.FailureReason.StoppedByBudget, "Stopped")));
                Assert.True(job.Succeeded, job.Refusal?.Message);
                var input = TrialJobInput.Parse(new JobRepository(database).Get(job.Value!.JobId)!.InputJson);
                return (input, ReviewInputArtifacts.Read(database, input.ReviewInputs!).Inputs);
            }

            var working = Queue(noBaseSample: true);
            Assert.Equal(listed.Order(), working.Inputs.Cases.Select(item => item.Word).Order());
            Assert.Equal(listed.Order(), working.Input.Words!.Order());
            Assert.Null(working.Inputs.DeclaredGate);
            Assert.Empty(working.Inputs.Sets);
            Assert.Equal(TrialSampleKind.WorkingSet, working.Input.Sample);
            Assert.Equal(listed.Length, working.Input.SampleWords);
            Assert.Equal(SIL.Motif.Commands.JobCommands.WordListDigest(listed.Reverse()), working.Input.WordListDigest);

            var stratified = Queue(noBaseSample: false);
            Assert.Equal(TrialSampleKind.Stratified, stratified.Input.Sample);
            Assert.Null(stratified.Input.SampleWords);
            Assert.Contains(stratified.Inputs.Cases, item => item.Word == "goldone");
            Assert.NotNull(stratified.Inputs.DeclaredGate);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void ANoBaseSampleTrialNeedsAWordListAndRefusesOptionsThatContradictIt()
    {
        string[] words = ["listone"];
        foreach (var request in new[]
        {
            new EnqueueTrialRequest("p.fwdata", "1.0", "proposal/x", NoBaseSample: true),
            new EnqueueTrialRequest("p.fwdata", "1.0", "proposal/x", Words: words, AllWords: true, NoBaseSample: true),
            new EnqueueTrialRequest("p.fwdata", "1.0", "proposal/x", Words: words, NoBaseSample: true) { ReviewSetIds = [] },
        })
        {
            var refusal = SIL.Motif.Commands.JobCommands.InvalidTrialWords(request);
            Assert.Equal(SIL.Motif.Contract.Commands.RefusalCodes.TrialNoBaseSampleInvalid, refusal?.Code);
        }
        Assert.Null(SIL.Motif.Commands.JobCommands.InvalidTrialWords(
            new EnqueueTrialRequest("p.fwdata", "1.0", "proposal/x", Words: words, NoBaseSample: true)));
    }

    [Theory]
    [InlineData("single", false, false)]
    [InlineData("single", true, false)]
    [InlineData("ambiguous", false, false)]
    [InlineData("ambiguous", true, false)]
    [InlineData("ambiguous", false, true)]
    [InlineData("exact", false, false)]
    [InlineData("exact", true, false)]
    [InlineData("exact", false, true)]
    [InlineData("unresolved", false, false)]
    public async Task RequiredManifestWordKeepsItsNativeApprovalAndHoldsAnUnapprovedSecondReading(
        string identity, bool approveSecond, bool loseFirst)
    {
        string path;
        string writingSystem;
        string wordformId = "";
        var readings = new List<ParseAnalysis>();
        using (var cache = pristine.NewScratch())
        {
            var signatureReader = new ReadingSignatureReader(cache);
            writingSystem = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultVernWs);
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            {
                var word = cache.ServiceLocator.GetInstance<IWfiWordformFactory>()
                    .Create(TsStringUtils.MakeString("goldword", cache.DefaultVernWs));
                wordformId = SIL.Motif.Contract.Ids.CanonicalId.FromGuid(word.Guid).Value;
                var other = identity != "single" ? cache.ServiceLocator.GetInstance<IWfiWordformFactory>()
                    .Create(TsStringUtils.MakeString("goldword", cache.DefaultVernWs)) : word;
                foreach (var senseId in new[] { pristine.Seed.FirstSenseId, pristine.Seed.SecondSenseId })
                {
                    var sense = cache.ServiceLocator.GetInstance<ILexSenseRepository>().GetObject(senseId);
                    var entry = (ILexEntry)sense.Owner;
                    var analysis = cache.ServiceLocator.GetInstance<IWfiAnalysisFactory>().Create();
                    (readings.Count == 0 ? word : other).AnalysesOC.Add(analysis);
                    var bundle = cache.ServiceLocator.GetInstance<IWfiMorphBundleFactory>().Create();
                    analysis.MorphBundlesOS.Add(bundle);
                    bundle.MorphRA = entry.LexemeFormOA;
                    bundle.MsaRA = sense.MorphoSyntaxAnalysisRA;
                    if (readings.Count == 0 || approveSecond)
                        cache.LangProject.DefaultUserAgent.SetEvaluation(analysis, Opinions.approves);
                    var reading = new ParseAnalysis([new ParseMorph(bundle.MorphRA.Guid.ToString("D"),
                        bundle.MsaRA.Guid.ToString("D"), null, null)]);
                    var signature = signatureReader.Read(reading);
                    Assert.NotNull(signature);
                    readings.Add(reading with { Signature = signature });
                }
            });
            new FwDataProjectLoader().Save(cache);
            path = cache.ProjectId.Path;
        }
        var root = Directory.CreateTempSubdirectory("motif-review-manifest-approval-").FullName;
        try
        {
            File.WriteAllText(Path.ChangeExtension(path, ".motif.toml"),
                "[gate]\nword-sets = [\"gold\"]\n[[review-word-set]]\nid = \"gold\"\nname = \"Gold\"\nrole = \"regression\"\nmanifest = \"gold.jsonl\"\n");
            var member = new Dictionary<string, object>
            {
                ["caseId"] = "gold-case", ["word"] = "goldword", ["writingSystemId"] = writingSystem,
                ["source"] = new { citation = "Gold list" }, ["expectationRefs"] = Array.Empty<string>(),
            };
            if (identity is "exact" or "unresolved")
                member["wordformId"] = identity == "exact" ? wordformId : SIL.Motif.Contract.Ids.CanonicalId.Mint().Value;
            File.WriteAllLines(Path.Combine(Path.GetDirectoryName(path)!, "gold.jsonl"),
                ["{\"schema\":\"motif.review-word-set\",\"schemaVersion\":1}", JsonSerializer.Serialize(member)]);
            var baseline = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(path), root);
            Assert.True(baseline.Succeeded, baseline.Refusal?.Message);
            var draft = SIL.Motif.Commands.ProposalCommands.New(new(path, "1.0", "capture-revision", null));
            Assert.True(draft.Succeeded, draft.Refusal?.Message);
            Assert.True(SIL.Motif.Commands.ProposalCommands.AddSetGloss(new(path, "1.0", "capture-revision",
                SIL.Motif.Contract.Ids.CanonicalId.FromGuid(pristine.Seed.FirstSenseId).Value,
                NewLangProjFixture.AnalysisTag, "reviewed gloss")).Succeeded);
            DraftRationale.Author(path, "capture-revision", "Clarify the gloss", "Keep the required readings.");
            var finalized = SIL.Motif.Commands.ProposalCommands.Finalize(new(path, "1.0", "capture-revision"));
            Assert.True(finalized.Succeeded, finalized.Refusal?.Message);
            Assert.True(DryRunJobRunner.Run(path, "1.0", draft.Value!.ProposalId).Succeeded);
            var captured = ReviewTrialCapture.Capture(new EnqueueTrialRequest(path, "1.0", draft.Value!.ProposalId,
                Words: ["ordinary"]) { ReviewSetIds = [] }, ["ordinary"], null);
            if (identity == "unresolved")
            {
                Assert.False(captured.Succeeded);
                Assert.Equal(SIL.Motif.Contract.Commands.RefusalCodes.TrialReviewInputUnavailable, captured.Refusal!.Code);
                Assert.Contains("unresolved project word identity", captured.Refusal.Message);
                return;
            }
            Assert.True(captured.Succeeded, captured.Refusal?.Message);
            var project = new ProjectLocator(path, Path.GetFileNameWithoutExtension(path));
            using var database = MotifDatabase.OpenOwned(ProjectDatabaseCatalog.DatabasePathFor(project), project,
                MotifSchema.CurrentSchema, new Version(1, 0));
            var artifact = ReviewInputArtifacts.Read(database, captured.Value!.ReviewInputs!);
            var jobs = new JobRepository(database);
            var proposal = new ProposalRepository(database).Get(SIL.Motif.Contract.Ids.CanonicalId.Parse(draft.Value.ProposalId));
            var assessmentId = SIL.Motif.Contract.Ids.CanonicalId.Mint("assessment/").Value;
            var beforeId = SIL.Motif.Contract.Ids.CanonicalId.Mint("assessment/").Value;
            var parser = FakeParser.ExecutablePath;
            var invocation = new BatchInvocationEvidence("manifest-approval-after", path, "sha256:source",
                BatchInvocationEvidence.DigestFile(parser), "words", "sha256:words", "tsv", "sha256:tsv",
                "err", "sha256:err", null, ParserWorkLimit.Default, 1, false);
            var material = new AssessmentRecordMaterial.Words([CorrectnessFixture.Word("ordinary", true)], null, null, [])
                { Invocation = invocation };
            var assessment = new NewAssessmentRecord(
                AssessmentId: assessmentId, ProposalId: proposal.ProposalId, ProposalIntentDigest: proposal.IntentDigest,
                Assessor: "pangloss", Kind: "Correctness",
                ScopeJson: """{"perWordLimitMs":1000,"perWordParserWorkLimit":{"steps":200000,"isUnbounded":false}}""",
                ScopeDigest: "scope",
                Subject: new AssessmentSubject.Words(Selection.Create("test", ["ordinary"]), "none", "1"),
                BaselineToken: JsonSerializer.Serialize(artifact.Baseline, MotifJson.CreateOptions()),
                OutcomeDigest: "outcome", SemanticDigest: "semantic", GrammarSourceSha256: "grammar",
                ModelFingerprint: "model", Pipeline: "pipeline", DiagnosticCount: 0,
                Material: material);
            var assessments = new AssessmentRepository(database);
            assessments.Record(assessment);
            assessments.Record(assessment with
            {
                AssessmentId = beforeId, ProposalId = null, ProposalIntentDigest = null,
                Material = material with { Invocation = invocation with { InvocationId = "manifest-approval-before" } },
            });
            var input = new TrialJobInput(SIL.Motif.Commands.JobCommands.CanonicalProposalJson(proposal), null,
                BaselineAssessmentIds: [beforeId], ReviewInputs: captured.Value.ReviewInputs);
            var job = jobs.Create("manifest-approval", ProjectWorkspaceKey.Compute(project), "trial",
                JsonSerializer.Serialize(input, MotifJson.CreateOptions()), JobTimestamp.FormatUtc(DateTimeOffset.UtcNow));
            jobs.Transition(job.JobId, JobStatus.Running);
            jobs.Transition(job.JobId, JobStatus.Completed, JsonSerializer.Serialize(new
                { baselineToken = artifact.Baseline, assessmentIds = new[] { assessmentId }, allWords = false,
                    droppedGrammar = new DroppedGrammar([], 0) }, MotifJson.CreateOptions()));
            TrialReviewObservation Observe(ReviewPlannedCase item, bool after) => new(item.CaseKey, item.Word,
                item.Word == "goldword" ? "analysed" : "no-analysis",
                new ParseWordEvidence("fieldworks-parse-analysis/v1", 0, item.Word, 0, false, false, false,
                    item.Word != "goldword" ? [] : after ? loseFirst ? [readings[1]] : readings.ToArray() : [readings[0]], []));
            TrialReviewEvidence.Save(database, new(job.JobId, captured.Value.ReviewInputs!,
                artifact.Inputs.Cases.Select(item => Observe(item, false)).ToArray(),
                artifact.Inputs.Cases.Select(item => Observe(item, true)).ToArray(), []));
            var reportId = await new TrialReviewPublisher(database).PublishAsync(new ReviewPublishRequest(job.JobId));
            var report = new ReviewReportRepository(database).ReadManifest(reportId)!.Summary;

            var secondApproved = approveSecond && identity != "exact";
            var green = secondApproved && !loseFirst;
            var bytes = File.ReadAllBytes(path);
            var variables = Environment.GetEnvironmentVariables().Cast<DictionaryEntry>()
                .ToDictionary(entry => (string)entry.Key, entry => (string?)entry.Value, StringComparer.OrdinalIgnoreCase);
            variables[PanGlossExecutable.PathVariable] = parser;
            using var environment = AmbientEnvironment.Push(variables);
            var applied = SIL.Motif.Commands.ProposalCommands.Apply(new(path, "1.0", draft.Value.ProposalId,
                Attribution: new ApplyAttribution(ApplyModes.Autonomous, "Review confirmed")));
            if (green)
                Assert.True(applied.Succeeded, applied.Refusal?.Message);
            else
            {
                Assert.False(applied.Succeeded);
                Assert.Equal("apply.trial-gate", applied.Refusal!.Code);
                Assert.Equal(bytes, File.ReadAllBytes(path));
                Assert.Equal("proposed", new ProposalRepository(database).Get(proposal.ProposalId).Status);
            }
            Assert.Equal(loseFirst ? TrialGateState.Loss : green ? TrialGateState.Green : TrialGateState.Unmeasured,
                report.Gate!.State);
            Assert.Equal(!green && !loseFirst ? ["goldword"] : Array.Empty<string>(), report.HeldWords);
            if (loseFirst)
            {
                var loss = Assert.Single(report.Losses!.NewlyLostReadings);
                Assert.Equal("goldword", loss.Word);
                Assert.True(loss.Approved);
                Assert.NotEmpty(loss.AssertionIds);
            }
            var gold = Assert.Single(artifact.Inputs.Cases, item => item.Word == "goldword");
            Assert.Empty(Assert.Single(gold.Memberships).ExpectationIds);
            Assert.Equal(secondApproved ? 2 : 1, gold.Assertions.Count(assertion => assertion.Approved && assertion.Eligible));
            Assert.All(gold.Assertions, assertion => Assert.NotNull(assertion.Morphology!.SourceWordformGuid));
            if (identity == "ambiguous")
                Assert.Null(gold.WordformId);
            if (identity == "exact")
                Assert.Equal(wordformId, gold.WordformId);
            Assert.NotNull(artifact.BaselineSidecarDigest);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void AConfiguredGainGateIsFrozenIntoTheCapturedInputsAndSurvivesTheArtifact()
    {
        string path;
        using (var cache = pristine.NewScratch())
        {
            new FwDataProjectLoader().Save(cache);
            path = cache.ProjectId.Path;
        }
        var root = Directory.CreateTempSubdirectory("motif-review-gain-gate-").FullName;
        try
        {
            File.WriteAllText(Path.ChangeExtension(path, ".motif.toml"),
                "[gain-gate]\nmode = \"hold\"\nmin-attested-per-object = 3\n");
            var baseline = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(path), root);
            Assert.True(baseline.Succeeded, baseline.Refusal?.Message);
            var draft = SIL.Motif.Commands.ProposalCommands.New(new(path, "1.0", "capture-revision", null));
            Assert.True(draft.Succeeded, draft.Refusal?.Message);
            var project = new ProjectLocator(path, Path.GetFileNameWithoutExtension(path));
            using var database = MotifDatabase.OpenOwned(ProjectDatabaseCatalog.DatabasePathFor(project), project,
                MotifSchema.CurrentSchema, new Version(1, 0));
            AddWordChange(new ProposalRepository(database), "gatealpha");
            var frozen = SIL.Motif.Commands.JobCommands.FreezeTrialProposal(new EnqueueTrialRequest(path, "1.0", draft.Value!.ProposalId));
            Assert.True(frozen.Succeeded, frozen.Refusal?.Message);
            var words = SIL.Motif.Commands.JobCommands.ResolveTrialWords(frozen.Value!);
            Assert.True(words.Succeeded, words.Refusal?.Message);
            var captured = ReviewTrialCapture.Capture(frozen.Value!, words.Value, null);
            Assert.True(captured.Succeeded, captured.Refusal?.Message);
            var policy = new SIL.Motif.Contract.Review.GainGatePolicy(SIL.Motif.Contract.Review.GainGateMode.Hold, 3);
            var stored = ReviewInputArtifacts.Read(database, captured.Value!.ReviewInputs!);
            Assert.Equal(policy, stored.Inputs.GainGate);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task AWordsTrialCountsATextAttestedGainAsAttestedAndAnInventedOneAsGenerated()
    {
        const string invented = "motifinvented";
        string path;
        string attested;
        using (var cache = pristine.NewScratch())
        {
            SeededProject.SeedText(cache, pristine.Seed);
            attested = SeededProject.UnanalysedWordForm;
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
                cache.ServiceLocator.GetInstance<IWfiWordformFactory>()
                    .Create(TsStringUtils.MakeString(invented, cache.DefaultVernWs)));
            new FwDataProjectLoader().Save(cache);
            path = cache.ProjectId.Path;
        }
        var root = Directory.CreateTempSubdirectory("motif-review-attested-").FullName;
        try
        {
            var baseline = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(path), root);
            Assert.True(baseline.Succeeded, baseline.Refusal?.Message);
            var draft = SIL.Motif.Commands.ProposalCommands.New(new(path, "1.0", "capture-revision", null));
            Assert.True(draft.Succeeded, draft.Refusal?.Message);
            var project = new ProjectLocator(path, Path.GetFileNameWithoutExtension(path));
            using var database = MotifDatabase.OpenOwned(ProjectDatabaseCatalog.DatabasePathFor(project), project,
                MotifSchema.CurrentSchema, new Version(1, 0));
            AddWordChange(new ProposalRepository(database), "unrelated");
            var request = new EnqueueTrialRequest(path, "1.0", draft.Value!.ProposalId, Words: [attested, invented]);
            var frozen = SIL.Motif.Commands.JobCommands.FreezeTrialProposal(request);
            Assert.True(frozen.Succeeded, frozen.Refusal?.Message);
            var words = SIL.Motif.Commands.JobCommands.ResolveTrialWords(frozen.Value!);
            Assert.True(words.Succeeded, words.Refusal?.Message);
            var captured = ReviewTrialCapture.Capture(frozen.Value!, words.Value, null);
            Assert.True(captured.Succeeded, captured.Refusal?.Message);
            var reference = captured.Value!.ReviewInputs!;
            var inputs = ReviewInputArtifacts.Read(database, reference).Inputs;
            var attestedCase = Assert.Single(inputs.Cases, item => item.Word == attested);
            var inventedCase = Assert.Single(inputs.Cases, item => item.Word == invented);
            Assert.True(attestedCase.Attested);
            Assert.False(inventedCase.Attested);

            const string job = "job/attested-words";
            new JobRepository(database).RecordInterrupted(job, inputs.ProjectKey, "trial",
                JsonSerializer.Serialize(new TrialJobInput(JsonSerializer.Serialize(new { contractVersions = new Dictionary<string, string>(),
                    proposalId = draft.Value.ProposalId, operations = Array.Empty<object>() }), null, ReviewInputs: reference), MotifJson.CreateOptions()),
                "{}", "2026-01-01T00:00:00Z", JobStatus.Failed);
            const string form = "11111111-1111-1111-1111-111111111111";
            const string msa = "33333333-3333-3333-3333-333333333333";
            TrialReviewObservation Observe(string key, string word, bool parses) => new(key, word, "complete",
                new ParseWordEvidence("fieldworks-parse-analysis/v1", 0, word, 0, false, false, false,
                    parses ? [new ParseAnalysis([new ParseMorph(form, msa, null, null)])] : [], []));
            TrialReviewEvidence.Save(database, new TrialReviewLedger(job, reference,
                [Observe(attestedCase.CaseKey, attested, false), Observe(inventedCase.CaseKey, invented, false)],
                [Observe(attestedCase.CaseKey, attested, true), Observe(inventedCase.CaseKey, invented, true)], []));

            var id = await new TrialReviewPublisher(database).PublishAsync(new ReviewPublishRequest(job));
            var gains = Assert.IsType<GainSummary>(new ReviewReportRepository(database).ReadManifest(id)!.Summary.Summary.Gains);
            Assert.True(gains.AttestedGained >= 1);
            Assert.True(gains.GeneratedGained >= 1);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static void AddWordChange(ProposalRepository repository, string word)
    {
        var id = SIL.Motif.Contract.Ids.CanonicalId.FromGuid(Guid.NewGuid()).Value;
        var draft = System.Text.Json.Nodes.JsonNode.Parse(repository.GetDraft("capture-revision").ProposalJson!)!.AsObject();
        draft["contractVersions"]!["analysis"] = "1.0";
        draft["operations"]!.AsArray().Add(new System.Text.Json.Nodes.JsonObject
        {
            ["operationId"] = SIL.Motif.Contract.Ids.CanonicalId.Mint().Value,
            ["kind"] = "analysis/wfiWordform/setSpellingStatus",
            ["target"] = id,
            ["after"] = new System.Text.Json.Nodes.JsonObject { ["value"] = 2 },
            ["extensions"] = new System.Text.Json.Nodes.JsonObject
            {
                ["changeFit"] = new System.Text.Json.Nodes.JsonObject
                {
                    ["wordformId"] = id,
                    ["wordformForm"] = word,
                    ["baselineToken"] = "captured-baseline",
                },
            },
        });
        repository.SaveDraft("capture-revision", draft.ToJsonString());
    }

    [Fact]
    public async Task SharedInputsRemainUntilTheLastTrialIsDiscarded()
    {
        string path;
        using (var cache = pristine.NewScratch())
        {
            new FwDataProjectLoader().Save(cache);
            path = cache.ProjectId.Path;
        }
        var root = Directory.CreateTempSubdirectory("motif-review-retention-").FullName;
        try
        {
            var baseline = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(path), root);
            Assert.True(baseline.Succeeded, baseline.Refusal?.Message);
            var draft = SIL.Motif.Commands.ProposalCommands.New(new(path, "1.0", "retention", null));
            Assert.True(draft.Succeeded, draft.Refusal?.Message);
            var request = new EnqueueTrialRequest(path, "1.0", draft.Value!.ProposalId, Words: ["word"]);
            var first = SIL.Motif.Commands.JobCommands.EnqueueTrial(request);
            var second = SIL.Motif.Commands.JobCommands.EnqueueTrial(request);
            Assert.True(first.Succeeded, first.Refusal?.Message);
            Assert.True(second.Succeeded, second.Refusal?.Message);
            var project = new ProjectLocator(path, Path.GetFileNameWithoutExtension(path));
            using var database = MotifDatabase.OpenOwned(ProjectDatabaseCatalog.DatabasePathFor(project), project,
                MotifSchema.CurrentSchema, new Version(1, 0));
            var directory = database.FullPath + ".review-inputs";
            var inputPath = Assert.Single(Directory.GetFiles(directory, "*.json"));
            var artifact = ReviewInputArtifacts.Read(database, new(Path.GetFileNameWithoutExtension(inputPath)));
            var jobs = new JobRepository(database);
            jobs.CancelActive(first.Value!.JobId);
            jobs.DeleteArchived(first.Value.JobId);
            ReviewInputArtifacts.Sweep(database);
            Assert.Single(Directory.GetFiles(directory, "*.json"));
            jobs.CancelActive(second.Value!.JobId);
            var discarded = SIL.Motif.Commands.ProposalCommands.DiscardDraft(new(path, "1.0", "retention"));
            Assert.True(discarded.Succeeded, discarded.Refusal?.Message);
            Assert.Empty(Directory.GetFiles(directory, "*.json"));

            using var lease = ReviewInputArtifacts.Acquire(database.FullPath);
            ReviewInputArtifacts.Store(database, artifact);
            ReviewInputArtifacts.TrySweep(database);
            Assert.Single(Directory.GetFiles(directory, "*.json"));
            using var started = new ManualResetEventSlim();
            var sweep = Task.Run(() => { started.Set(); ReviewInputArtifacts.Sweep(database); });
            Assert.True(started.Wait(TimeSpan.FromSeconds(5)));
            Assert.NotSame(sweep, await Task.WhenAny(sweep, Task.Delay(100)));
            Assert.Single(Directory.GetFiles(directory, "*.json"));
            lease.Dispose();
            await sweep.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Empty(Directory.GetFiles(directory, "*.json"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void DecisionReleasesInputsAfterAnActiveTrialStops()
    {
        string path;
        using (var cache = pristine.NewScratch())
        {
            new FwDataProjectLoader().Save(cache);
            path = cache.ProjectId.Path;
        }
        var root = Directory.CreateTempSubdirectory("motif-review-decision-").FullName;
        try
        {
            var baseline = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(path), root);
            Assert.True(baseline.Succeeded, baseline.Refusal?.Message);
            var draft = SIL.Motif.Commands.ProposalCommands.New(new(path, "1.0", "decision", null));
            Assert.True(draft.Succeeded, draft.Refusal?.Message);
            var queued = SIL.Motif.Commands.JobCommands.EnqueueTrial(new(path, "1.0", draft.Value!.ProposalId, Words: ["word"]));
            Assert.True(queued.Succeeded, queued.Refusal?.Message);
            var project = new ProjectLocator(path, Path.GetFileNameWithoutExtension(path));
            using var database = MotifDatabase.OpenOwned(ProjectDatabaseCatalog.DatabasePathFor(project), project,
                MotifSchema.CurrentSchema, new Version(1, 0));
            var directory = database.FullPath + ".review-inputs";
            new ProposalRepository(database).SetStatus(SIL.Motif.Contract.Ids.CanonicalId.Parse(draft.Value.ProposalId),
                "rejected", null);
            Assert.Single(Directory.GetFiles(directory, "*.json"));
            var cancelled = SIL.Motif.Commands.JobCommands.Cancel(new(path, queued.Value!.JobId, "1.0"));
            Assert.True(cancelled.Succeeded, cancelled.Refusal?.Message);
            Assert.Empty(Directory.GetFiles(directory, "*.json"));
            using var connection = database.OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM ReviewReports WHERE TrialJobId=$job";
            command.Parameters.AddWithValue("$job", queued.Value.JobId);
            Assert.Equal(1L, (long)command.ExecuteScalar()!);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void CaptureUsesTheConfiguredManifestAndKeepsItsBytes()
    {
        string projectPath;
        using (var cache = pristine.NewScratch())
        {
            new FwDataProjectLoader().Save(cache);
            projectPath = cache.ProjectId.Path;
        }
        var root = Directory.CreateTempSubdirectory("motif-review-capture-").FullName;
        try
        {
            var baseline = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(projectPath), root);
            Assert.True(baseline.Succeeded, baseline.Refusal?.Message);
            var project = new ProjectLocator(projectPath, Path.GetFileNameWithoutExtension(projectPath));
            var manifestPath = Path.Combine(Path.GetDirectoryName(projectPath)!, "review-members.jsonl");
            var bytes = System.Text.Encoding.UTF8.GetBytes(
                "{\"schema\":\"motif.review-word-set\",\"schemaVersion\":1}\n" +
                "{\"caseId\":\"case\",\"word\":\"control\",\"writingSystemId\":\"qaa\"," +
                "\"source\":{\"citation\":\"source\"},\"expectationRefs\":[]}\n");
            File.WriteAllBytes(manifestPath, bytes);
            File.WriteAllText(ProjectConfigurationReader.PathFor(project),
                "[[review-word-set]]\nid = \"control\"\nname = \"Controls\"\nrole = \"reference\"\nmanifest = \"review-members.jsonl\"\n");

            var captured = ReviewTrialCapture.Capture(new EnqueueTrialRequest(projectPath, "1.0", "proposal", Words: ["touched"]),
                ["touched"], null);

            Assert.True(captured.Succeeded, captured.Refusal?.Message);
            Assert.Equal(new[] { "control", "touched" }, captured.Value!.Words!.Order().ToArray());
            Assert.NotNull(captured.Value.ReviewInputs);
            using var database = MotifDatabase.OpenOwned(ProjectDatabaseCatalog.DatabasePathFor(project), project,
                MotifSchema.CurrentSchema, new Version(1, 0));
            var artifact = ReviewInputArtifacts.Read(database, captured.Value.ReviewInputs!);
            Assert.Equal(bytes, artifact.Sources["control"]);
            File.WriteAllText(manifestPath, "changed");
            Assert.Equal(bytes, ReviewInputArtifacts.Read(database, captured.Value.ReviewInputs!).Sources["control"]);
            var subset = ReviewTrialCapture.Capture(new EnqueueTrialRequest(projectPath, "1.0", "proposal", Words: ["touched"])
                { ReviewSetIds = [] }, ["touched"], null);
            Assert.True(subset.Succeeded, subset.Refusal?.Message);
            Assert.Equal("touched", Assert.Single(subset.Value!.Words!));
            Assert.Equal("control", Assert.Single(ReviewInputArtifacts.Read(database, subset.Value.ReviewInputs!).Inputs.OmittedSetIds));
            File.Delete(ProjectConfigurationReader.PathFor(project));
            var noSets = ReviewTrialCapture.Capture(new EnqueueTrialRequest(projectPath, "1.0", "proposal", Words: ["touched"]),
                ["touched"], null);
            Assert.True(noSets.Succeeded, noSets.Refusal?.Message);
            var emptyCapture = ReviewInputArtifacts.Read(database, Assert.IsType<ReviewInputReference>(noSets.Value!.ReviewInputs));
            Assert.Empty(emptyCapture.Inputs.Sets);
            Assert.Equal("touched", Assert.Single(emptyCapture.Inputs.Cases).Word);
            var probes = ReviewTrialCapture.Capture(new EnqueueTrialRequest(projectPath, "1.0", "proposal", Words: ["touched"],
                NegativeWords: ["probe"]) { ReviewSetIds = [] }, ["touched"], null);
            Assert.True(probes.Succeeded, probes.Refusal?.Message);
            var probeCapture = ReviewInputArtifacts.Read(database, Assert.IsType<ReviewInputReference>(probes.Value!.ReviewInputs));
            var probe = Assert.Single(probeCapture.Inputs.Cases, item => item.Word == "probe");
            Assert.Empty(probe.Assertions);
            Assert.Equal("Inline negative probes", Assert.Single(probeCapture.Inputs.Sets).Name);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
