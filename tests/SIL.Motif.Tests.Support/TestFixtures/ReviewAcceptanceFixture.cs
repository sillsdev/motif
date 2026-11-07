using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.Parser;
using SIL.Motif.Commands;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Review;
using SIL.Motif.Host;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Parsimony;
using SIL.Motif.Worker.Runtime.Review;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Tests.TestFixtures;

public sealed class ReviewAcceptanceFixture : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "motif-acceptance-" + Guid.NewGuid().ToString("N"));
    public string Project { get; }
    public string Proposal { get; } = CanonicalId.Mint("proposal/").Value;
    public string Report { get; }
    public string ProjectKey { get; private set; } = "";

    public ReviewAcceptanceFixture(int extraFailures = 0)
    {
        Directory.CreateDirectory(Root);
        string Form1;
        string Form2;
        string Msa;
        using (var cache = NewLangProjFixture.CreateCache(Root))
        {
            var seed = SeededProject.Seed(cache);
            var forms = cache.ServiceLocator.GetInstance<IMoFormRepository>();
            var firstForm = forms.GetObject(seed.FirstLexemeFormId);
            var secondForm = forms.GetObject(seed.SecondLexemeFormId);
            var msa = cache.ServiceLocator.GetInstance<IMoStemMsaRepository>().AllInstances().First();
            Form1 = firstForm.Guid.ToString("D");
            Form2 = secondForm.Guid.ToString("D");
            Msa = msa.Guid.ToString("D");
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            {
                var wordform = cache.ServiceLocator.GetInstance<IWfiWordformFactory>()
                    .Create(TsStringUtils.MakeString("approved", cache.DefaultVernWs));
                foreach (var form in new[] { firstForm, secondForm })
                {
                    var analysis = cache.ServiceLocator.GetInstance<IWfiAnalysisFactory>().Create();
                    wordform.AnalysesOC.Add(analysis);
                    var bundle = cache.ServiceLocator.GetInstance<IWfiMorphBundleFactory>().Create();
                    analysis.MorphBundlesOS.Add(bundle);
                    bundle.MorphRA = form;
                    bundle.MsaRA = msa;
                    cache.LangProject.DefaultUserAgent.SetEvaluation(analysis, Opinions.approves);
                }
            });
            new FwDataProjectLoader().Save(cache);
        }
        Project = NewLangProjFixture.FwDataPath(Root);
        var outcome = ProjectStoreCommand.Run<string>(Project, MotifProductVersion.CurrentText, (database, locator) =>
        {
            ProjectKey = ProjectWorkspaceKey.Compute(locator);
            using (var connection = database.OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "INSERT INTO Proposals (ProposalId,CurrentIntentDigest,Status) VALUES ($id,'intent','proposed');";
                command.Parameters.AddWithValue("$id", Proposal);
                command.ExecuteNonQuery();
            }
            var sets = new[] { new ReviewSetBinding("references", "References", "reference", "members", "expectations", "source", []),
                new ReviewSetBinding("negatives", "Negatives", "negative", "members2", "expectations2", "source2", []) };
            ReviewAssertion Approved(string form) => new(form, form, new ApprovedMorphology([new ApprovedMorph(form, Msa, null, [])]),
                ReferenceAuthority.Human, "actor", "revision", true, false, true, []);
            var reference = Approved(Form1) with { Id = "reference", Approved = false, Authority = ReferenceAuthority.SourceValidated };
            var negative = new ReviewAssertion("negative", "", null, ReferenceAuthority.Human, "actor", "revision", false, true, true, []);
            var cases = new[] {
                new ReviewPlannedCase("approved", "approved-key", "approved", "ws", null, [], [Approved(Form1), Approved(Form2)]),
                new ReviewPlannedCase("reference", "reference-key", "reference", "ws", null,
                    [new ReviewMembership("references", "reference", "reference", 1, null, null, ["reference"])], [reference]),
                new ReviewPlannedCase("negative", "negative-key", "negative", "ws", null,
                    [new ReviewMembership("negatives", "negative", "negative", 1, null, null, ["negative"])], [negative]),
                new ReviewPlannedCase("incomplete", "incomplete-key", "incomplete", "ws", null, [], []),
                new ReviewPlannedCase("unchanged", "unchanged-key", "unchanged", "ws", null, [], []),
                new ReviewPlannedCase("homograph/a", "homograph-key-a", "homograph", "ws-a", null, [], []),
                new ReviewPlannedCase("homograph/b", "homograph-key-b", "homograph", "ws-b", null, [], []) }.ToList();
            for (var i = 0; i < extraFailures; i++)
                cases.Add(new ReviewPlannedCase("extra/" + i, "extra-key/" + i, "extra" + i, "ws", null, [], []));
            var inputs = new FrozenReviewInputs(ProjectKey, Proposal, "scope", "manifest", null, sets, [], cases);
            var digest = "sha256:" + new string('a', 64);
            var baseline = new BaselineToken(ProjectKey, digest, "v1", "2026-01-01T00:00:00Z", digest);
            var inputRef = ReviewInputArtifacts.Store(database, new ReviewInputArtifact(inputs, new Dictionary<string, byte[]>()) { Baseline = baseline });
            var proposalJson = JsonSerializer.Serialize(new { contractVersions = new Dictionary<string, string>(), proposalId = Proposal, operations = Array.Empty<object>() });
            var input = new TrialJobInput(proposalJson, null, ReviewInputs: inputRef);
            const string job = "job/acceptance";
            new JobRepository(database).RecordInterrupted(job, ProjectKey, "trial", JsonSerializer.Serialize(input, MotifJson.CreateOptions()),
                "{}", "2026-01-01T00:00:00Z", JobStatus.Failed);
            TrialReviewObservation Observe(string word, string[] forms, bool capped = false) => new(word, word, capped ? "capped" : "complete",
                new ParseWordEvidence("fieldworks-parse-analysis/v1", 0, word, 0, capped, false, false,
                    forms.Select(form => new ParseAnalysis([new ParseMorph(form, Msa, null, null)])).ToArray(), []));
            var before = new[] { Observe("approved", [Form1, Form2]), Observe("reference", [Form1]), Observe("negative", []),
                Observe("incomplete", [Form1]), Observe("unchanged", [Form1]),
                Observe("homograph", [Form1]) with { CaseKey = "homograph/a" }, Observe("homograph", [Form1]) with { CaseKey = "homograph/b" } }.ToList();
            var after = new[] { Observe("approved", [Form1]), Observe("reference", []), Observe("negative", [Form1]),
                Observe("incomplete", [], true), Observe("unchanged", [Form1]),
                Observe("homograph", [Form1]) with { CaseKey = "homograph/a" }, Observe("homograph", [Form1]) with { CaseKey = "homograph/b" } }.ToList();
            for (var i = 0; i < extraFailures; i++)
            {
                before.Add(Observe("extra" + i, [Form1]) with { CaseKey = "extra/" + i });
                after.Add(Observe("extra" + i, []) with { CaseKey = "extra/" + i });
            }
            var subject = new GrammarWarningPart("subject", GrammarWarningPartRole.Object) { SubjectGuid = Form1 };
            var warning = new GrammarWarning(GrammarDiagnosticLevel.Warning, "label", [subject], [], "warning")
                { Code = "new-warning", Origin = GrammarFindingOrigin.Check };
            TrialReviewEvidence.Save(database, new TrialReviewLedger(job, inputRef, before, after, []) { BeforeWarnings = [], AfterWarnings = [warning] });
            var publisher = new TrialReviewPublisher(database);
            var firstId = publisher.PublishAsync(new ReviewPublishRequest(job)).GetAwaiter().GetResult();
            var first = new ReviewReportRepository(database).ReadManifest(firstId)!.Summary;
            var jobs = new JobRepository(database);
            var candidateId = ParsimonyAutoRun.Enqueue(database, jobs.Get(job)!, baseline, false)!;
            var candidateInput = ParsimonyCandidateJobInput.Parse(jobs.Get(candidateId)!.InputJson);
            var artifactDigest = new string('4', 64);
            ParsimonyReportResponse MakeReport(string side, string measure) => new("report/" + side + measure,
                new ParsimonyReportInputs("bundle/" + side, baseline, side, side == "candidate" ? "identity" : null,
                    "model", new ParsimonyArtifactDigest(1, artifactDigest), new ParsimonyArtifactDigest(1, artifactDigest), null, null, []),
                side == "candidate" ? [new ParsimonyFinding("finding/one", measure, ParsimonyAxis.Parsimony,
                    ParsimonyTier.Static, new ParsimonyFindingAttachment(ParsimonyAttachmentKind.Project, "project"), null,
                    new ParsimonyMeasureNumber(1, 1, "items"),
                    new ParsimonyMeasureThreshold(ParsimonyThresholdOperator.RankedOnly, null, "threshold/v1"),
                    "sha256:" + artifactDigest, [], "guide:measure", [], ParsimonyVerification.NotRun)] : [], "text")
            {
                AssessmentIds = [], Notes = [],
                MeasureRuns = [new ParsimonyMeasureRun(measure, ParsimonyMeasureStatus.Computed, 10, 1, "items", null, null, null)],
            };
            var pairs = candidateInput.RunMeasureIds.Select(measure => new ParsimonyCandidateMeasureResult(measure,
                MakeReport("baseline", measure), MakeReport("candidate", measure), [])).ToArray();
            var binding = new ParsimonyCandidateBinding("identity", baseline, job, Proposal, first.Pins.IntentDigest,
                "sha256:c", [], "sha256:e", "sha256:a", "sha256:s", "sha256:t", "sha256:j");
            var parsimony = new ParsimonyCandidateEvidenceResponse(1, pairs[0].MeasureId, binding,
                pairs[0].Before, pairs[0].After, [], new ParsimonyApprovedAnalysisPreservation("preserved", 0, 0, []), pairs.Skip(1).ToArray());
            using (var connection = database.OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "UPDATE Jobs SET Status='completed', ArchivedUtc=UpdatedUtc, ResultJson=$result WHERE JobId=$id;";
                command.Parameters.AddWithValue("$result", JsonSerializer.Serialize(parsimony, MotifJson.CreateOptions()));
                command.Parameters.AddWithValue("$id", candidateId);
                command.ExecuteNonQuery();
            }
            return CommandOutcome<string>.Success(publisher.PublishAsync(new ReviewPublishRequest(job)).GetAwaiter().GetResult().Value);
        });
        if (!outcome.Succeeded) throw new InvalidOperationException(outcome.Refusal?.Message);
        Report = outcome.Value!;
    }

    public string CreateWorkOnlyReport(long afterWork)
    {
        var result = ProjectStoreCommand.Run<string>(Project, MotifProductVersion.CurrentText, (database, locator) =>
        {
            var key = ProjectWorkspaceKey.Compute(locator);
            var digest = "sha256:" + new string('a', 64);
            var baseline = new BaselineToken(key, digest, "v1", "2026-01-01T00:00:00Z", digest);
            var inputs = new FrozenReviewInputs(key, Proposal, "work-scope", "work-manifest", "parser-hash", [], [],
                [new ReviewPlannedCase("work", "work-key", "work", "ws", null, [], [])]);
            var reference = ReviewInputArtifacts.Store(database, new ReviewInputArtifact(inputs, new Dictionary<string, byte[]>()) { Baseline = baseline });
            var proposalJson = JsonSerializer.Serialize(new { contractVersions = new Dictionary<string, string>(), proposalId = Proposal, operations = Array.Empty<object>() });
            var input = new TrialJobInput(proposalJson, null, ReviewInputs: reference,
                ReviewBaselines: [new ReviewBaselinePartition("ws", ["work"], ["assessment/work-before"])], ReviewCounterSemantics: 1);
            var selection = SIL.Motif.Host.Corpus.Selection.Create("work", ["work"]);
            var invocation = new SIL.Motif.Host.PanGloss.BatchInvocationEvidence("parser", "source", "source-hash", "parser-hash", "words", "words-hash",
                "tsv", "tsv-hash", "stderr", "stderr-hash", null, new SIL.Motif.Contract.Assess.ParserWorkLimit(200), 1, true);
            var repository = new AssessmentRepository(database);
            var evidence = new ParseWordEvidence("fieldworks-parse-analysis/v1", 0, "work", 0, false, false, false, [], []);
            void Assessment(string id, long work) => repository.Record(new NewAssessmentRecord(id, null, null, "pangloss", "ParseTime", "{}", "scope",
                new AssessmentSubject.Words(selection, "none", "1"), JsonSerializer.Serialize(baseline, MotifJson.CreateOptions()), null, null,
                "grammar", null, null, null, new AssessmentRecordMaterial.Words([new AssessedWord("work", "analysed", []) { ParserWork = work, Morphology = evidence }], null, null, [], invocation)));
            Assessment("assessment/work-before", 100);
            Assessment("assessment/work-after", afterWork);
            var resultJson = JsonSerializer.Serialize(new { reviewCounterSemantics = 1, reviewAfter = new[] {
                new ReviewAfterPartition("ws", ["work"], ["assessment/work-after"]) } }, MotifJson.CreateOptions());
            var job = "job/work-" + afterWork;
            new JobRepository(database).RecordInterrupted(job, key, "trial", JsonSerializer.Serialize(input, MotifJson.CreateOptions()),
                resultJson, "2026-01-01T00:00:00Z", JobStatus.Failed);
            var observation = new TrialReviewObservation("work", "work", "complete", evidence);
            TrialReviewEvidence.Save(database, new TrialReviewLedger(job, reference, [observation], [observation], []) { BeforeWarnings = [], AfterWarnings = [] });
            return CommandOutcome<string>.Success(new TrialReviewPublisher(database).PublishAsync(new ReviewPublishRequest(job)).GetAwaiter().GetResult().Value);
        });
        if (!result.Succeeded) throw new InvalidOperationException(result.Refusal?.Message);
        return result.Value!;
    }

    public string ProposalState()
    {
        var result = ProjectStoreCommand.Run<string>(Project, MotifProductVersion.CurrentText, (database, _) =>
        {
            using var connection = database.OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT Status || ':' || CurrentIntentDigest FROM Proposals WHERE ProposalId=$id;";
            command.Parameters.AddWithValue("$id", Proposal);
            return CommandOutcome<string>.Success((string)command.ExecuteScalar()!);
        });
        return result.Value!;
    }

    public void Dispose() { if (Directory.Exists(Root)) Directory.Delete(Root, true); }
}
