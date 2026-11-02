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
using SIL.Motif.Contract.Responses;
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
    [InlineData("parser-work-limit", false)]
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
                        new ParserWorkLimit(different && mismatch == "parser-work-limit" ? 100 : 200), 1, false);
                    return new NewAssessmentRecord(id, before ? null : proposalId, before ? null : digest,
                        different && mismatch == "assessor" ? "other" : "pangloss", kind, "{}", "scope",
                        new AssessmentSubject.Words(Selection.Create("Default", ["cat"]), "none", "1"),
                        different && mismatch == "token" ? tokenJson + " " : tokenJson,
                        "outcome", "semantic", "grammar", "model", "pipeline", 0,
                        new AssessmentRecordMaterial.Words([new AssessedWord("cat", "analysed", [])
                            { ParserWork = before ? 100 : 110 }], null, null, []) { Invocation = invocation });
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

    [Fact]
    public void ABaselineHoldingBothSpellingsOfAWordHasNoDifferenceButStillReportsDroppedGrammar()
    {
        const string composed = "caf\u00E9";
        var decomposed = composed.Normalize(System.Text.NormalizationForm.FormD);
        var root = Path.Combine(Path.GetTempPath(), "motif-mcp-trial-both-" + Guid.NewGuid().ToString("N"));
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
                NewAssessmentRecord Record(string id, bool before, params string[] words) =>
                    new(id, before ? null : proposalId, before ? null : digest, "pangloss", "Correctness", "{}", "scope",
                        new AssessmentSubject.Words(Selection.Create("Default", words), "none", "1"), tokenJson,
                        "outcome", "semantic", "grammar", "model", "pipeline", 0,
                        new AssessmentRecordMaterial.Words(
                            words.Select(word => new AssessedWord(word, "analysed", [])).ToArray(), null, null, []));
                new AssessmentRepository(database).RecordBatch([Record("before", true, composed, decomposed),
                    Record("after", false, decomposed)]);
                var input = new TrialJobInput(proposalJson, null, [decomposed], BaselineAssessmentIds: ["before"]);
                var jobs = new JobRepository(database);
                var job = jobs.Create("trial", ProjectWorkspaceKey.Compute(new ProjectLocator(project, "project")),
                    JobCommands.TrialKind, JsonSerializer.Serialize(input, MotifJson.CreateOptions()),
                    "2026-09-24T11:00:00Z");
                job = jobs.Transition(job.JobId, JobStatus.Running);
                jobs.Transition(job.JobId, JobStatus.Completed, JsonSerializer.Serialize(new
                    { baselineToken = token, assessmentIds = new[] { "after" }, droppedGrammar = new DroppedGrammar(
                        [new("grammar.environment.invalid", [], "dropped")], 0) }, MotifJson.CreateOptions()));
                return CommandOutcome<JsonObject>.Success(new JsonObject());
            });
            Assert.True(recorded.Succeeded, recorded.Refusal?.Message);
            var context = new ServerContext(project, "1.0", new NoRunnerLauncher(new JobRunnerLaunchOptions(root, null)),
                new ActivityLog(null), ToolProfile.Builtin, TextWriter.Null, advancedAiModeEnabled: true);

            var outcome = TrialResults.Complete(context, "trial", proposalId.Value);

            Assert.Null(outcome.Refusal);
            var reported = outcome.Value!.AsObject();
            Assert.Null(reported["difference"]);
            Assert.Equal("comparison-refused", (string?)reported["differenceUnavailable"]!["code"]);
            Assert.Contains("Run the Trial again", (string?)reported["differenceUnavailable"]!["reason"]);
            var dropped = reported["summary"]!["droppedGrammar"]!;
            Assert.Equal("checked", (string?)dropped["state"]);
            Assert.False((bool)dropped["clean"]!);
            Assert.Equal("grammar.environment.invalid", (string?)dropped["introduced"]![0]!["code"]);
        }
        finally { Directory.Delete(root, true); }
    }
}
