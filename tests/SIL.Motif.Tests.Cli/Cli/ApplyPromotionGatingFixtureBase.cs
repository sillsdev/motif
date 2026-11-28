using SIL.Motif.Tests.Parser;
using SIL.Motif.Commands.Review;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Review;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Runtime.Review;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Host.Assess;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Config;
using SIL.Motif.Host.Corpus;
using SIL.Motif.Host.Parser;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.LcmUtils;
using SIL.LCModel;
using SIL.LCModel.Infrastructure;
using SIL.LCModel.Core.Text;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Cli;

/// <summary>Shared project setup and helpers for the Apply promotion and gating tests, in the deterministic and real-parser classes.</summary>
public abstract class ApplyPromotionGatingFixtureBase
{
    protected const string ProductVersion = "1.0";
    protected const string GrammarSha = "sha256:" + "dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd";

    protected readonly SeededProject _seed;
    protected readonly string _fwDataPath;

    protected ApplyPromotionGatingFixtureBase(PristineProjectFixture pristine)
    {
        _seed = pristine.Seed;
        using var scratch = pristine.NewScratch();
        _fwDataPath = scratch.ProjectId.Path;
    }

    protected Guid CreateWordform(string form)
    {
        var loader = new FwDataProjectLoader();
        using var cache = loader.LoadCache(_fwDataPath);
        IWfiWordform created = null!;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () => created = cache.ServiceLocator
            .GetInstance<IWfiWordformFactory>().Create(TsStringUtils.MakeString(form, cache.DefaultVernWs)));
        loader.Save(cache);
        return created.Guid;
    }

    protected int SpellingStatusOf(Guid wordformId)
    {
        using var cache = new FwDataProjectLoader().LoadCache(_fwDataPath);
        return cache.ServiceLocator.GetInstance<IWfiWordformRepository>().GetObject(wordformId).SpellingStatus;
    }

    // --- helpers ---

    protected string FinalizeAndTrial(string draftName, string newGloss) => FinalizeAndTrial(draftName, draft =>
    {
        var canonicalId = CanonicalId.FromGuid(_seed.FirstSenseId);
        Assert.True(ProposalCommands.AddSetGloss(new AddSetGlossRequest(
            _fwDataPath, ProductVersion, draft, canonicalId.Value, NewLangProjFixture.AnalysisTag, newGloss)).Succeeded);
    });

    protected string FinalizeAndTrial(string draftName, Action<string> compose)
    {
        Assert.True(ProposalCommands.New(new NewDraftRequest(_fwDataPath, ProductVersion, draftName, null)).Succeeded);
        compose(draftName);
        DraftRationale.Author(_fwDataPath, draftName, "Clarify the first sense gloss", "Exercise apply's promotion and sweep.");

        var finalizeResult = ProposalCommands.Finalize(new FinalizeRequest(_fwDataPath, ProductVersion, draftName));
        Assert.True(finalizeResult.Succeeded);
        var proposalId = finalizeResult.Value!.ProposalId;

        Assert.True(DryRunJobRunner.Run(_fwDataPath, ProductVersion, proposalId).Succeeded);
        return proposalId;
    }

    protected void RecordGate(string proposalId, string scenario)
    {
        using var database = ProjectMotifDatabase.Open(_fwDataPath);
        var project = new ProjectLocator(_fwDataPath, Path.GetFileNameWithoutExtension(_fwDataPath));
        var key = ProjectWorkspaceKey.Compute(project);
        var proposal = new ProposalRepository(database).Get(CanonicalId.Parse(proposalId));
        var proposalJson = scenario == "old-revision"
            ? JsonSerializer.Serialize(new { contractVersions = new Dictionary<string, string>(), proposalId, operations = Array.Empty<object>() })
            : proposal.ProposalJson!;
        var saved = new BaselineRepository(database).GetCurrent(key)!;
        var baseline = saved.Token;
        if (scenario == "old-baseline") baseline = new BaselineToken(baseline.ProjectIdentity,
            "sha256:" + new string('f', 64), baseline.ProjectionVersion, baseline.CapturedUtc, baseline.BundleDigest);
        string[] words = ["alpha", "beta"];
        var configuration = new ProjectConfigurationReader().Read(project);
        var manifest = ReviewWordSetManifestReader.Read(Path.Combine(Path.GetDirectoryName(_fwDataPath)!, "gold.jsonl"), "regression");
        var inputs = new FrozenReviewInputs(key, proposalId, "scope", "manifest", null,
            [new ReviewSetBinding("gold", "Gold", "regression", "members", "expectations", manifest.SourceDigest, [])], [],
            words.Select(word => new ReviewPlannedCase("case-" + word, "key-" + word, word, "ws", null,
                [new ReviewMembership("gold", "case-" + word, "regression", 1, null, null, [])], [])).ToArray());
        inputs = inputs with { DeclaredGate = DeclaredTrialGatePolicy.Capture(configuration, inputs) };
        if (scenario == "undeclared-scope") inputs = inputs with { DeclaredGate = null };
        if (scenario == "omitted-required") inputs = inputs with { Sets = [], OmittedSetIds = ["gold"],
            Cases = inputs.Cases.Select(item => item with { Memberships = [] }).ToArray() };
        var reference = ReviewInputArtifacts.Store(database,
            new ReviewInputArtifact(inputs, new Dictionary<string, byte[]>())
                { Baseline = baseline, BaselineSidecarDigest = ProgressSourceIdentity.CaptureSidecars(saved.FwDataPath)!.Digest });
        var input = new TrialJobInput(proposalJson, null, ReviewInputs: reference)
        { Sample = scenario == "working-set" ? TrialSampleKind.WorkingSet : null,
          ExpectedLosses = scenario == "covered-loss" ? words : scenario == "partial-expected" ? ["alpha"] : [] };
        var jobs = new JobRepository(database);
        var job = jobs.Create("gate-" + Guid.NewGuid().ToString("N"), key, "trial",
            JsonSerializer.Serialize(input, MotifJson.CreateOptions()), JobTimestamp.FormatUtc(DateTimeOffset.UtcNow));
        if (scenario == "pending-latest") return;
        jobs.Transition(job.JobId, JobStatus.Running);
        var completion = JsonSerializer.Serialize(new { baselineToken = baseline, assessmentIds = Array.Empty<string>(),
            allWords = false, droppedGrammar = new DroppedGrammar([], 0) }, MotifJson.CreateOptions());
        jobs.Transition(job.JobId, scenario switch
        {
            "failed-latest" => JobStatus.Failed,
            "cancelled-latest" => JobStatus.Cancelled,
            _ => JobStatus.Completed,
        }, completion);
        ParseWordEvidence Before(string word) => new("fieldworks-parse-analysis/v1", 0, word, 0, false, false, false,
            [new ParseAnalysis([new ParseMorph("11111111-1111-1111-1111-111111111111",
                "33333333-3333-3333-3333-333333333333", null, null)])], []);
        var loses = scenario is "loss" or "partial-expected" or "covered-loss";
        TrialReviewEvidence.Save(database, new TrialReviewLedger(job.JobId, reference,
            words.Select(word => new TrialReviewObservation("case-" + word, word, "analysed", Before(word))).ToArray(),
            words.Select(word => new TrialReviewObservation("case-" + word, word,
                scenario == "unmeasured" ? "timed-out" : loses ? "no-analysis" : "analysed",
                scenario == "unmeasured" ? Before(word) with { TimedOut = true, Analyses = [] } :
                loses ? Before(word) with { Analyses = [] } : Before(word))).ToArray(), []));
        var reportId = new TrialReviewPublisher(database).PublishAsync(new ReviewPublishRequest(job.JobId)).GetAwaiter().GetResult();
        if (scenario == "changed-policy")
            File.AppendAllText(Path.ChangeExtension(_fwDataPath, ".motif.toml"), "\n[gain-gate]\nmode = \"hold\"\n");
        if (scenario == "changed-gold-list")
            File.AppendAllText(Path.Combine(Path.GetDirectoryName(_fwDataPath)!, "gold.jsonl"), "\n");
        {
            var report = new ReviewReportRepository(database).ReadManifest(reportId)!.Summary;
            var parser = ParserIdentity.ReadFreshKey(PanGlossExecutable.TryLocate());
            report = report with { Pins = report.Pins with { Checkpoints = report.Pins.Checkpoints
                .Select(pin => pin with { ParserDigest = pin.Side == Side.After ? scenario switch
                {
                    "parser-changed" => "sha256:" + new string('0', 64),
                    "parser-missing" => null,
                    _ => parser,
                } : parser }).ToArray() } };
            if (scenario == "legacy") report = report with { Gate = null, Losses = null, DeclaredGate = null };
            var json = JsonSerializer.Serialize(report, ReviewJson.Options);
            using var connection = database.OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE ReviewReports SET SummaryJson=$json,SummaryDigest=$hash WHERE ReportId=$id;";
            command.Parameters.AddWithValue("$json", json);
            command.Parameters.AddWithValue("$hash", Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(json))));
            command.Parameters.AddWithValue("$id", reportId.Value);
            command.ExecuteNonQuery();
        }
    }

    // The receipt a green Trial of this Proposal would write, bound to the Baseline as it stands now.
    protected string WriteReceipt(string proposalId, string name)
    {
        using var database = ProjectMotifDatabase.Open(_fwDataPath);
        var project = new ProjectLocator(_fwDataPath, Path.GetFileNameWithoutExtension(_fwDataPath));
        var record = new ProposalRepository(database).Get(CanonicalId.Parse(proposalId));
        var job = Trials.NewestFirst(Trials.JobsFor(database, project, CanonicalId.Parse(proposalId))).First();
        var baseline = new BaselineRepository(database).GetCurrent(ProjectWorkspaceKey.Compute(project))!.Token;
        var receipt = TrialReceiptFile.Build(proposalId, Trials.ReadCurrentIntentDigest(record)!, baseline.SemanticSnapshotDigest,
            job.JobId, new TrialExpectations([], []), new ReadingDeltaCounts(1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0));
        var path = Path.Combine(Path.GetDirectoryName(_fwDataPath)!, name);
        Assert.Null(TrialReceiptFile.Write(path, receipt));
        return path;
    }

    protected string ReadyProposal(string draft, string gloss)
    {
        Assert.True(BaselineCaptureCommand.Capture(new BaselineCaptureRequest(_fwDataPath)).Succeeded);
        var proposalId = FinalizeAndTrial(draft, gloss);
        RecordAssessment(proposalId, GetRecord(proposalId).IntentDigest!, "Correctness", ("alpha", true));
        return proposalId;
    }

    protected void WriteGateConfiguration()
    {
        File.WriteAllText(Path.ChangeExtension(_fwDataPath, ".motif.toml"),
            "[gate]\nword-sets = [\"gold\"]\n[[review-word-set]]\nid = \"gold\"\nname = \"Gold\"\nrole = \"regression\"\nmanifest = \"gold.jsonl\"\n");
        var rows = new[] { "alpha", "beta" }.Select(word => JsonSerializer.Serialize(new
        {
            caseId = "case-" + word, word, writingSystemId = "ws", source = new { citation = "Gold list" },
            expectationRefs = Array.Empty<string>(),
        }));
        File.WriteAllLines(Path.Combine(Path.GetDirectoryName(_fwDataPath)!, "gold.jsonl"),
            new[] { "{\"schema\":\"motif.review-word-set\",\"schemaVersion\":1}" }.Concat(rows));
        var baseline = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(_fwDataPath));
        Assert.True(baseline.Succeeded, baseline.Refusal?.Message);
    }

    protected ProposalRecord GetRecord(string proposalId)
    {
        using var database = ProjectMotifDatabase.Open(_fwDataPath);
        return new ProposalRepository(database).Get(CanonicalId.Parse(proposalId));
    }

    protected string? GetCurrentAssessmentId()
    {
        using var database = ProjectMotifDatabase.Open(_fwDataPath);
        return new AssessmentRepository(database).GetCurrent()?.AssessmentId;
    }

    /// <summary>Records and promotes an unrelated Assessment: the project's prior current one.</summary>
    protected string PromotePrevious(params (string Word, bool Analysed)[] words)
    {
        var id = RecordAssessment(proposalId: null, intentDigest: null, "Correctness", words);
        using var database = ProjectMotifDatabase.Open(_fwDataPath);
        new AssessmentRepository(database).PromoteToCurrent(id);
        return id;
    }

    protected string RecordAssessment(
        string? proposalId, string? intentDigest, string kind, params (string Word, bool Analysed)[] words) =>
        RecordAssessment(proposalId, intentDigest, kind, null, words);

    protected string RecordAssessment(
        string? proposalId, string? intentDigest, string kind, string? baselineToken,
        params (string Word, bool Analysed)[] words)
    {
        var selection = Selection.Create("test", words.Select(w => w.Word));
        var assessmentId = CanonicalId.Mint("assessment/").Value;
        var assessedWords = words
            .Select(w => CorrectnessFixture.Word(w.Word, w.Analysed))
            .ToArray();

        using (var database = ProjectMotifDatabase.Open(_fwDataPath))
        {
            var project = new ProjectLocator(_fwDataPath, Path.GetFileNameWithoutExtension(_fwDataPath));
            baselineToken ??= JsonSerializer.Serialize(new BaselineRepository(database)
                .GetCurrent(ProjectWorkspaceKey.Compute(project))!.Token, MotifJson.CreateOptions());
            new AssessmentRepository(database).Record(new NewAssessmentRecord(
                AssessmentId: assessmentId,
                ProposalId: proposalId is null ? null : CanonicalId.Parse(proposalId),
                ProposalIntentDigest: intentDigest,
                Assessor: "pangloss",
                Kind: kind,
                ScopeJson: """{"perWordLimitMs":1000,"perWordParserWorkLimit":{"steps":200000,"isUnbounded":false}}""",
                ScopeDigest: "sha256:" + new string('a', 64),
                Subject: new AssessmentSubject.Words(selection, "none", "1"),
                BaselineToken: baselineToken,
                OutcomeDigest: "sha256:" + new string('b', 64),
                SemanticDigest: "sha256:" + new string('c', 64),
                GrammarSourceSha256: GrammarSha,
                ModelFingerprint: "model",
                Pipeline: "pipeline",
                DiagnosticCount: 0,
                Material: new AssessmentRecordMaterial.Words(assessedWords, null, null, [])
            ));
        }
        if (proposalId is not null) StoredTrialJob.Record(_fwDataPath, proposalId, assessmentId);
        return assessmentId;
    }

    protected void WriteConfiguration(bool gateOnRegression, bool purgeOnApply)
    {
        var path = ProjectConfigurationReader.PathFor(
            new ProjectLocator(_fwDataPath, Path.GetFileNameWithoutExtension(_fwDataPath)));
        File.WriteAllText(path, ProjectConfigurationFile.Render(
            new ProjectConfiguration(
                new[] { AssessmentScopeConfiguration.Default() }, gateOnRegression, purgeOnApply)));
    }
}
