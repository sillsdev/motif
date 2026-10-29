using System.Text.Json;
using System.Text.Json.Nodes;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Store;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Canonicalization;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.Corpus;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Parser;
using SIL.Motif.Mcp;
using SIL.Motif.Worker;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Mcp;

[Trait("MotifTestLevel", "Unit")]
public sealed class TrialWorkProjectionTests
{
    [Theory]
    [InlineData("matching", true)]
    [InlineData("assessor", false)]
    [InlineData("token", false)]
    [InlineData("executable", false)]
    [InlineData("missing-executable", false)]
    [InlineData("step-limit", false)]
    public void CompletedTrialReportsWorkOnlyForCompatibleEvidence(string mismatch, bool comparable)
    {
        var root = Path.Combine(Path.GetTempPath(), "motif-mcp-trial-work-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var project = Path.Combine(root, "project.fwdata");
        File.WriteAllText(project, "");
        try
        {
            var proposalId = CanonicalId.Mint("proposal/");
            using var value = JsonDocument.Parse("{\"ws\":\"en\",\"text\":\"example\"}");
            var draft = new DraftDocument
            {
                ProposalId = proposalId.Value,
                ContractVersions = new Dictionary<string, string> { ["lexical"] = "1.0" },
                Operations = [new DraftOperation
                {
                    OperationId = CanonicalId.Mint("operation/").Value,
                    Kind = "lexical/lexSense/setGloss", Target = CanonicalId.Mint("lexSense/").Value,
                    After = value.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone()),
                }],
            };
            var proposalJson = ProposalCommands.BuildProposalJson(draft);
            var digest = IntentDigest.Compute(ProposalJsonParser.Parse(proposalJson));
            var token = new BaselineToken("project-id", "sha256:" + new string('1', 64), "projection-v1",
                "2026-09-24T10:00:00Z", "sha256:" + new string('a', 64));
            var tokenJson = JsonSerializer.Serialize(token, MotifJson.CreateOptions());
            var recorded = ProjectStoreCommand.Run(project, "1.0", (database, _) =>
            {
                new ProposalRepository(database).SaveRevision(new ProposalRevisionRecord(proposalId, digest,
                    proposalJson, "proposed", null, null, null));
                var assessments = new AssessmentRepository(database);
                NewAssessmentRecord Record(string id, bool before, string kind)
                {
                    var isWork = kind == "ParseTime";
                    var different = before && isWork;
                    var invocation = new BatchInvocationEvidence("inv-" + id, "source", "source-hash",
                        mismatch == "missing-executable" ? "" : different && mismatch == "executable" ? "other" : "parser", "words", "words-hash",
                        "tsv", "tsv-hash", "stderr", "stderr-hash", null,
                        new StepCap(different && mismatch == "step-limit" ? 100 : 200), 1, false);
                    return new NewAssessmentRecord(id, before ? null : proposalId, before ? null : digest,
                        different && mismatch == "assessor" ? "other" : "pangloss", kind, "{}", "scope",
                        new AssessmentSubject.Words(Selection.Create("Default", ["cat"]), "none", "1"),
                        different && mismatch == "token" ? tokenJson + " " : tokenJson,
                        "outcome", "semantic", "grammar", "model", "pipeline", 0,
                        new AssessmentRecordMaterial.Words([new AssessedWord("cat", "analysed", [])
                            { WorkSteps = before ? 100 : 110 }], null, null, []) { Invocation = invocation });
                }
                assessments.RecordBatch([Record("before-correctness", true, "Correctness"),
                    Record("after-correctness", false, "Correctness"), Record("before-time", true, "ParseTime"),
                    Record("after-time", false, "ParseTime")]);
                var input = new TrialJobInput(proposalJson, null, ["cat"],
                    BaselineAssessmentIds: ["before-correctness", "before-time"]);
                var jobs = new JobRepository(database);
                var locator = new ProjectLocator(project, "project");
                var job = jobs.Create("trial", ProjectWorkspaceKey.Compute(locator), JobCommands.TrialKind,
                    JsonSerializer.Serialize(input, MotifJson.CreateOptions()), "2026-09-24T11:00:00Z");
                job = jobs.Transition(job.JobId, JobStatus.Running);
                jobs.Transition(job.JobId, JobStatus.Completed, JsonSerializer.Serialize(new
                    { baselineToken = token, assessmentIds = new[] { "after-correctness", "after-time" } },
                    MotifJson.CreateOptions()));
                return CommandOutcome<JsonObject>.Success(new JsonObject());
            });
            Assert.True(recorded.Succeeded, recorded.Refusal?.Message);
            var context = new ServerContext(project, "1.0", new NoRunnerLauncher(new JobRunnerLaunchOptions(root, null)),
                new ActivityLog(null), ToolProfile.Builtin, TextWriter.Null, advancedAiModeEnabled: true);
            var outcome = TrialResults.Complete(context, "trial", proposalId.Value);
            Assert.Null(outcome.Refusal);
            var work = outcome.Value!["work"];
            if (mismatch is "assessor" or "token") Assert.Null(work);
            else
            {
                Assert.NotNull(work);
                Assert.Equal(comparable ? 100L : null, work!["baselineWork"]?.GetValue<long>());
                Assert.Equal(comparable, work["workIncreaseWarning"]!.GetValue<bool>());
            }
        }
        finally { Directory.Delete(root, true); }
    }
}
