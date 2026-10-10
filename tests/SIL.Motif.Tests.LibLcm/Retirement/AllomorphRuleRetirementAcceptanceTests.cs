using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.Core.Text;
using SIL.LCModel.DomainServices;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Assess;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Parsimony;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Commands.Store;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.HumanJudgments;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Retirement;
using SIL.Motif.Host;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.Config;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Parsimony;
using SIL.Motif.Host.Parser;
using SIL.Motif.LiveHost.Baselines;
using SIL.Motif.LiveHost.HumanJudgments;
using SIL.Motif.Model.Snapshot;
using SIL.Motif.Projection.Retirement;
using SIL.Motif.Runner.Apply;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Runner.Retirement;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Tests.Parser;
using SIL.Motif.Worker.Assess;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Store;
using Xunit;
using Xunit.Abstractions;

namespace SIL.Motif.Tests.Retirement;

[Collection(LcmCacheParallelCollections.Group2)]
[Trait("MotifTestLevel", "System")]
public sealed partial class AllomorphRuleRetirementAcceptanceTests(
    PristineProjectFixture pristine, ITestOutputHelper output)
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);
    private readonly string _root = Path.Combine(Path.GetTempPath(), "allomorph-rule-retirement-acceptance-" +
        Guid.NewGuid().ToString("N"));

    [RealParserFact]
    public void PrefixRetirementUsesWordEdgeAndKeepsLiteralHashAsAStoredMarker()
    {
        Directory.CreateDirectory(_root);
        using (var wordCache = pristine.NewScratch())
            AssertPrefixRetirementParserOutcome(wordCache, pristine.Seed, wordEdge: true);
        using (var literalCache = pristine.NewScratch())
            AssertPrefixRetirementParserOutcome(literalCache, pristine.Seed, wordEdge: false);
    }

    [RealParserFact]
    public void PrefixRetirementParsesDirectFwDataWhenPanGlossSupportsOwnedWordBoundaries()
    {
        Directory.CreateDirectory(_root);
        using var cache = pristine.NewScratch();
        AssertPrefixRetirementParserOutcome(cache, pristine.Seed, wordEdge: true,
            directWordBoundaryAcceptance: true);
    }

    [RealParserFact]
    public async Task OwnerRaTaProposalPassesPairedVerificationAndRebuildsItsFinding()
    {
        Directory.CreateDirectory(_root);
        using var cache = pristine.NewScratch();
        var fixture = BuildOwnerFixture(cache, pristine.Seed);
        new FwDataProjectLoader().Save(cache);
        var sourceBaseline = Token(cache);
        var affected = new[] { fixture.TargetWord.Guid, fixture.UnchangedWord.Guid };
        var writingSystem = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultVernWs);
        var selection = new HashSet<string>(StringComparer.Ordinal)
        {
            FrozenExpectationCapture.CaseKey(CanonicalId.FromGuid(fixture.UnchangedWord.Guid).Value,
                writingSystem, "ira"),
        };
        var heldOut = new HashSet<string>(StringComparer.Ordinal)
        {
            FrozenExpectationCapture.CaseKey(CanonicalId.FromGuid(fixture.TargetWord.Guid).Value,
                writingSystem, "ata"),
        };
        var frozen = FrozenExpectationCapture.Capture(cache, sourceBaseline, affected, selection, heldOut);
        Assert.Empty(frozen.Unavailable);
        var targetExpectation = Assert.Single(frozen.Cases, item => item.Surface == "ata");
        var unchangedExpectation = Assert.Single(frozen.Cases, item => item.Surface == "ira");
        Assert.Equal(2, targetExpectation.Readings.Count(item => item.Opinion == "approved"));
        Assert.Equal(1, unchangedExpectation.Readings.Count(item => item.Opinion == "approved"));
        Assert.Contains(frozen.Cases, item => item.Surface == "ata" && item.HeldOut &&
            item.Readings.Any(reading => reading.Opinion == "approved"));
        Assert.Contains(frozen.Cases, item => item.Surface == "ira" && item.InSelection &&
            item.Readings.Any(reading => reading.Opinion == "approved"));
        Assert.Contains(frozen.ReviewedNegatives, item => item.Surface == "ri" && item.Target == "surface");

        using var invoker = new PanGlossInvoker(PanGlossExecutable.TryLocate());
        var beforeFinding = await ReadAlternationFamilyAsync(cache, sourceBaseline, invoker, Path.Combine(_root, "before-facts"));
        Assert.Equal(ParsimonyMeasureStatus.Computed, beforeFinding.Measure.Run.Status);
        var family = Assert.Single(beforeFinding.Measure.Findings);
        Assert.Equal(ParsimonyAttachmentKind.Group, family.AttachesTo.Kind);
        Assert.Equal(ParsimonyGroupKind.AlternationFamily, family.AttachesTo.GroupKind);
        Assert.Equal(2L, family.Number.Numerator);
        Assert.Equal(2L, family.Number.Denominator);

        var firstRetirement = Retirement(cache, fixture.FirstSuffix, fixture.FirstAlternate);
        var secondRetirement = Retirement(cache, fixture.SecondSuffix, fixture.SecondAlternate);
        var intent = ComposeIntent(cache, fixture, [firstRetirement, secondRetirement]);
        var operations = ReplaceListedAllomorphsWithRuleComposer.Build(cache, intent,
            () => CanonicalId.FromGuid(Guid.NewGuid()));
        var proposal = new Proposal(new Dictionary<string, string>
        {
            ["analysis"] = "1.0", ["lexical"] = "1.0", ["phonology"] = "1.0",
        }, CanonicalId.Mint(), null, operations.Reverse().ToArray());
        var dryRun = ScratchDryRun.Of(cache, proposal);
        var translated = RetirementExpectationTranslator.Translate(frozen, sourceBaseline,
            [firstRetirement, secondRetirement], proposal, dryRun);
        Assert.Equal(2, translated.Mappings.Count);
        var firstMapping = Assert.Single(translated.Mappings,
            item => item.RetiredForm == CanonicalId.FromGuid(fixture.FirstAlternate.Guid).Value);
        Assert.Equal(CanonicalId.FromGuid(fixture.FirstSuffix.LexemeFormOA!.Guid).Value,
            firstMapping.ReplacementForm);
        Assert.Equal(2, translated.BundleTextEffects.Count);
        Assert.All(translated.BundleTextEffects, effect =>
        {
            Assert.Equal("ta", effect.Before[writingSystem]);
            Assert.Equal("ra", effect.After[writingSystem]);
        });

        var beforeRun = await AssessAsync(cache.ProjectId.Path, invoker, ["ata", "ira", "ri"],
            Path.Combine(_root, "assess-before"), sourceBaseline);
        var afterRun = await ApplyAndAssessAsync(cache, proposal, dryRun, invoker, ["ata", "ira", "ri"],
            Path.Combine(_root, "assess-after"), fixture, translated);
        var verification = RecipeVerification.Compare(translated, beforeRun.Run, afterRun,
            RecipeVerificationCriteria.Tightening);
        output.WriteLine($"Verification={verification.Status}; incomplete={verification.IncompleteCases}; " +
            $"unavailable={string.Join(" | ", verification.Unavailable)}; exclusions={string.Join(" | ", verification.Exclusions)}; " +
            $"negativeGap={verification.NegativeEvidenceGap}; negatives={verification.CompletedNegativeCases}/{verification.AcceptedNegativeCases}/{verification.NewlyAcceptedNegativeCases}");
        Assert.Equal(RecipeVerificationStatus.Pass, verification.Status);
        Assert.Equal(3, verification.PreservedApprovedReadings);
        Assert.Equal(0, verification.LostApprovedReadings);
        Assert.Equal(0, verification.NewlyAcceptedNegativeCases);
        Assert.Equal(1, verification.CompletedNegativeCases);
        Assert.All(verification.Readings,
            item => Assert.Equal(RecipeVerificationReadingStatus.Preserved, item.Status));
        Assert.DoesNotContain(verification.Negatives, item => item.NewlyAccepted || item.AfterAccepted);

        new FwDataProjectLoader().Save(cache);
        Assert.Equal("ata", fixture.TargetWord.Form.get_String(cache.DefaultVernWs)?.Text);
        Assert.Equal("ira", fixture.UnchangedWord.Form.get_String(cache.DefaultVernWs)?.Text);
        Assert.Equal(fixture.FirstSuffix.LexemeFormOA!.Guid, fixture.TargetSuffixBundle.MorphRA!.Guid);
        Assert.Equal("ra", fixture.TargetSuffixBundle.Form.get_String(cache.DefaultVernWs)?.Text);
        Assert.Equal(fixture.SecondSuffix.LexemeFormOA!.Guid, fixture.SecondTargetSuffixBundle.MorphRA!.Guid);
        Assert.Equal("ra", fixture.SecondTargetSuffixBundle.Form.get_String(cache.DefaultVernWs)?.Text);
        Assert.Equal(Opinions.approves, fixture.TargetAnalysis.GetAgentOpinion(cache.LangProject.DefaultUserAgent));
        Assert.Equal(fixture.TargetEvaluationIds,
            fixture.TargetAnalysis.EvaluationsRC.Select(item => item.Guid).Order().ToArray());
        Assert.Equal(Opinions.approves,
            fixture.SecondTargetAnalysis.GetAgentOpinion(cache.LangProject.DefaultUserAgent));
        Assert.Equal(fixture.SecondTargetEvaluationIds,
            fixture.SecondTargetAnalysis.EvaluationsRC.Select(item => item.Guid).Order().ToArray());
        Assert.Equal(fixture.TargetBundleOrder,
            fixture.TargetAnalysis.MorphBundlesOS.Select(item => item.Guid).ToArray());
        Assert.Equal(fixture.SecondTargetBundleOrder,
            fixture.SecondTargetAnalysis.MorphBundlesOS.Select(item => item.Guid).ToArray());
        Assert.Equal(2, dryRun.ExpectedEffects.Count(item => item.Field == SnapshotFields.WfiMorphBundleMorph));
        Assert.Equal(2, dryRun.ExpectedEffects.Count(item => item.Field == SnapshotFields.WfiMorphBundleForm));
        Assert.Equal(2, dryRun.ExpectedEffects.Count(item => item.Field == SnapshotFields.LexEntryAlternateForms));
        _ = Assert.Single(firstRetirement.Bundles);
        _ = Assert.Single(secondRetirement.Bundles);
        var repository = cache.ServiceLocator.GetInstance<ICmObjectRepository>();
        Assert.False(repository.TryGetObject(fixture.FirstAlternate.Guid, out _));
        Assert.False(repository.TryGetObject(fixture.SecondAlternate.Guid, out _));
        var survivingBundles = cache.ServiceLocator.GetInstance<IWfiAnalysisRepository>().AllInstances()
            .SelectMany(analysis => analysis.MorphBundlesOS);
        Assert.DoesNotContain(survivingBundles, bundle => bundle.MorphRA?.Guid == fixture.FirstAlternate.Guid ||
            bundle.MorphRA?.Guid == fixture.SecondAlternate.Guid);

        var saveRoot = Path.Combine(_root, "rebuilt-scratch");
        using var rebuilt = new ScratchCacheFactory().CreateFromFileCopy(cache.ProjectId.Path, saveRoot);
        var rebuiltBaseline = Token(rebuilt);
        var afterFinding = await ReadAlternationFamilyAsync(rebuilt, rebuiltBaseline, invoker,
            Path.Combine(_root, "after-facts"));
        Assert.Empty(afterFinding.Measure.Findings);
        Assert.Empty(afterFinding.FamilyView.Rows);
        Assert.Equal(ParsimonyMeasureStatus.Computed, afterFinding.Measure.Run.Status);
        Assert.DoesNotContain("outside the exact comparison", afterFinding.Measure.Run.Detail ?? string.Empty,
            StringComparison.Ordinal);
        Assert.Contains(rebuilt.LangProject.PhonologicalDataOA.PhonRulesOS,
            item => item.Guid == CanonicalId.Parse(intent.Rule.Id).ToGuid() &&
                !Assert.IsAssignableFrom<IPhRegularRule>(item).Disabled);
        Assert.DoesNotContain(rebuilt.ServiceLocator.GetInstance<ILexEntryRepository>().AllInstances()
            .SelectMany(entry => entry.AlternateFormsOS), form =>
            form.Guid == fixture.FirstAlternate.Guid || form.Guid == fixture.SecondAlternate.Guid);

        var retainedDirectory = Path.Combine(_root, "trace-project");
        CopyDirectory(Path.GetDirectoryName(rebuilt.ProjectId.Path)!, retainedDirectory);
        var retainedProject = Path.Combine(retainedDirectory, Path.GetFileName(rebuilt.ProjectId.Path));
        var parser = PanGlossExecutable.TryLocate()!;
        var importPath = Path.Combine(retainedDirectory, "after-import.json");
        var import = Run(parser, "import", retainedProject, importPath);
        Assert.Equal(0, import.ExitCode);
        var tracePath = Path.Combine(retainedDirectory, "ata.trace.json");
        var trace = Run(parser, "parse", retainedProject, "ata", "--trace=" + tracePath,
            "--trace-format", "json", "--trace-details");
        Assert.Equal(0, trace.ExitCode);
        var traceJson = File.ReadAllText(tracePath);
        Assert.Contains("\"type\":\"PhonologicalRuleSynthesis\"", traceJson, StringComparison.Ordinal);
        Assert.Contains("\"outputShape\":\"ata\"", traceJson, StringComparison.Ordinal);
        Assert.Contains(CanonicalId.Parse(intent.Rule.Id).ToGuid().ToString("D"), traceJson,
            StringComparison.OrdinalIgnoreCase);

        var controlPath = Path.Combine(retainedDirectory, "rule-disabled-control.json");
        var snapshot = JsonNode.Parse(File.ReadAllText(importPath))!;
        var rules = snapshot["phonology"]!["rules"]!.AsArray();
        var matchedRule = rules.Single(item => item!["guid"]!.GetValue<string>() ==
            CanonicalId.Parse(intent.Rule.Id).ToGuid().ToString("D"));
        rules.Remove(matchedRule);
        File.WriteAllText(controlPath, snapshot.ToJsonString());
        var controlTracePath = Path.Combine(retainedDirectory, "ata-without-rule.trace.json");
        var control = Run(parser, "parse", controlPath, "ata", "--trace=" + controlTracePath,
            "--trace-format", "json", "--trace-details");
        Assert.Equal(0, control.ExitCode);
        using (var controlResult = JsonDocument.Parse(File.ReadAllText(controlTracePath)))
            Assert.Empty(controlResult.RootElement.GetProperty("result").GetProperty("analyses").EnumerateArray());

        var heldOutGrammar = Path.Combine(_root, "heldout.xml");
        File.WriteAllText(heldOutGrammar, HeldOutGrammar());
        var generated = Run(parser, "generate", heldOutGrammar, "HELDOUT");
        Assert.Equal(0, generated.ExitCode);
        Assert.Contains("ikata", generated.Output, StringComparison.Ordinal);
        var heldOutTracePath = Path.Combine(_root, "heldout.trace.json");
        var heldOutParse = Run(parser, "parse", heldOutGrammar, "ikata", "--trace=" + heldOutTracePath,
            "--trace-format", "json", "--trace-details");
        Assert.Equal(0, heldOutParse.ExitCode);
        using (var heldOutResult = JsonDocument.Parse(File.ReadAllText(heldOutTracePath)))
            Assert.NotEmpty(heldOutResult.RootElement.GetProperty("result").GetProperty("analyses").EnumerateArray());

        Assert.False(AllomorphRetirementCapabilities.CanApply);
        output.WriteLine($"Parser={parser}; version=0.7.0");
        output.WriteLine($"Target={fixture.TargetWord.Form.get_String(cache.DefaultVernWs)?.Text}; unchanged={fixture.UnchangedWord.Form.get_String(cache.DefaultVernWs)?.Text}");
        output.WriteLine($"FrozenCases={frozen.Cases.Count}; Approved={frozen.Cases.SelectMany(item => item.Readings).Count(item => item.Opinion == "approved")}; reviewedNegatives={frozen.ReviewedNegatives.Count}");
        output.WriteLine($"RecipeVerification={verification.Status}; approved preserved={verification.PreservedApprovedReadings}/3; " +
            $"completed reviewed negatives={verification.CompletedNegativeCases}; newly accepted={verification.NewlyAcceptedNegativeCases}; " +
            "before=ata,ira,ri; after=ata,ira,ri; ri remains unaccepted.");
        output.WriteLine("BundleMorph retargets=2; bundle Form copies=2; alternate deletions=2; bundle order, Opinions, evaluation ids and wordform surfaces preserved.");
        output.WriteLine($"Mandatory cases: ata wordform={fixture.TargetWord.Guid:D} readings={fixture.TargetAnalysis.Guid:D},{fixture.SecondTargetAnalysis.Guid:D}; " +
            $"ira wordform={fixture.UnchangedWord.Guid:D} reading={CanonicalId.FromGuid(fixture.UnchangedWord.AnalysesOC.Single().Guid).Value}; " +
            $"ri reviewed-negative case={Assert.Single(frozen.ReviewedNegatives).CaseId} ({writingSystem}).");
        output.WriteLine("AlternationFamily before=r~t family across 2 suffixes; rebuilt scratch=0 findings/0 family rows, " +
            "Run=Computed because the retired family is absent and no loaded rule can affect another family.");
        output.WriteLine("Causal witness=trace PhonologicalRuleSynthesis ata; controlled rule removal=ata not analysed.");
        output.WriteLine("Held-out XML route=generate ikata; parse ikata=analysed.");
        output.WriteLine($"Trace scratch={retainedProject}");
    }

    [RealParserFact]
    public async Task OwnerRetirementJobsProduceACompleteStoredReview()
    {
        Directory.CreateDirectory(_root);
        var projectPath = pristine.CopyProjectFile();
        var workerRoot = Path.Combine(_root, "stored-review-worker");
        var parserPath = PanGlossExecutable.TryLocate();
        Assert.False(string.IsNullOrWhiteSpace(parserPath));
        var words = new[] { "ata", "ira", "ri" };
        ReplaceListedAllomorphsWithRuleIntent retirement;
        using (var cache = new FwDataProjectLoader().LoadScratchCache(projectPath))
        {
            var fixture = BuildOwnerFixture(cache, pristine.Seed);
            AddNotebookRecordType(cache);
            retirement = ComposeIntent(cache, fixture,
            [
                Retirement(cache, fixture.FirstSuffix, fixture.FirstAlternate),
                Retirement(cache, fixture.SecondSuffix, fixture.SecondAlternate),
            ]);
            new FwDataProjectLoader().Save(cache);
        }

        var baseline = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(projectPath), workerRoot);
        Assert.True(baseline.Succeeded, baseline.Refusal?.Message);
        var productVersion = MotifProductVersion.CurrentText;
        await using var runner = new InProcessRunnerLauncher(
            new JobRunnerLaunchOptions(workerRoot, parserPath));

        var baselineJob = ParsimonyCommands.Enqueue(new EnqueueParsimonyReportRequest(projectPath,
            productVersion, "P-allo-alternation-family", ParsimonyEvidenceScopeKind.ProjectApproved));
        Assert.True(baselineJob.Succeeded, baselineJob.Refusal?.Message);
        runner.Start(projectPath);
        await runner.WhenIdleAsync().WaitAsync(TimeSpan.FromMinutes(5));
        var sourceReport = ParsimonyCommands.Wait(new WaitForParsimonyReportRequest(projectPath,
            productVersion, baselineJob.Value!.JobId, TimeSpan.FromMinutes(1)));
        Assert.True(sourceReport.Succeeded, sourceReport.Refusal?.Message);
        output.WriteLine($"Source Report findings ({sourceReport.Value!.Findings.Count}): " +
            string.Join(" | ", sourceReport.Value.Findings.Select(finding =>
                $"{finding.MeasureId}:{finding.AttachesTo.GroupKind}:{finding.GroupKey}")));
        var boundFinding = Assert.Single(sourceReport.Value!.Findings, finding =>
            finding.MeasureId == "P-allo-alternation-family" &&
            finding.AttachesTo.GroupKind == ParsimonyGroupKind.AlternationFamily);

        const string draftName = "owner a_r_V retirement review";
        var draft = ProposalCommands.New(new NewDraftRequest(projectPath, productVersion,
            draftName, "Replace the listed alternation with its sound rule."));
        Assert.True(draft.Succeeded, draft.Refusal?.Message);
        var recordType = Assert.Single(ParsimonyCommands.ListNotebookRecordTypes(
            new ListNotebookRecordTypesRequest(projectPath, productVersion)).Value!.RecordTypes,
            item => item.Name == "Motif human judgment");
        var binding = ParsimonyCommands.RecordDisposition(new RecordParsimonyDispositionFromFindingRequest(
            projectPath, productVersion, sourceReport.Value.ReportId, boundFinding.FindingId,
            "fix", recordType.Id, draftName, "Stage the rule that replaces this alternation."));
        Assert.True(binding.Succeeded, binding.Refusal?.Message);
        var composition = ProposalCommands.ComposeRetireAllomorph(new ComposeRetireAllomorphRequest(
            projectPath, productVersion, draftName, AllomorphRetirementCodec.ToJson(retirement)));
        Assert.True(composition.Succeeded, composition.Refusal?.Message);
        Assert.NotEmpty(composition.Value!.Operations);
        var comment = ProposalCommands.Comment(new CommentRequest(projectPath, productVersion, draftName,
            "The retired alternates are represented by one sound rule."));
        Assert.True(comment.Succeeded, comment.Refusal?.Message);
        var finalized = ProposalCommands.Finalize(new FinalizeRequest(projectPath, productVersion, draftName));
        Assert.True(finalized.Succeeded, finalized.Refusal?.Message);
        var proposalId = finalized.Value!.ProposalId;

        var scope = new ProjectConfigurationReader().Read(ProjectStoreCommand.Locate(projectPath)).Scopes
            .Single(item => item.Name == AssessmentScopeConfiguration.DefaultName);
        var baselineAssessment = AssessCommand.Assess(new AssessRequest(projectPath,
                new SelectionRequest(false, [], words, false, null,
                    PerWordStepLimit: scope.PerWordStepLimit),
                PerWordLimitMs: (int)scope.PerWordLimit.TotalMilliseconds,
                PerWordStepLimit: scope.PerWordStepLimit),
            workerRoot, parserPath, onProgress: null, cancellationToken: CancellationToken.None);
        Assert.True(baselineAssessment.Succeeded, baselineAssessment.Refusal?.Message);
        var beforeAssessment = Assert.Single(baselineAssessment.Value!.Measurements,
            item => item.Kind == AssessmentKinds.ParseTime);

        var dryRunJob = JobCommands.EnqueueDryRun(new EnqueueDryRunRequest(projectPath,
            productVersion, proposalId));
        Assert.True(dryRunJob.Succeeded, dryRunJob.Refusal?.Message);
        runner.Start(projectPath);
        await runner.WhenIdleAsync().WaitAsync(TimeSpan.FromMinutes(5));
        var dryRun = JobCommands.WaitForDryRun(new WaitForDryRunRequest(projectPath,
            productVersion, proposalId, dryRunJob.Value!.JobId, TimeSpan.FromMinutes(1)));
        Assert.True(dryRun.Succeeded, dryRun.Refusal?.Message);

        var trial = JobCommands.EnqueueTrial(new EnqueueTrialRequest(projectPath, productVersion,
            proposalId, AssessmentScopeConfiguration.DefaultName, words));
        Assert.True(trial.Succeeded, trial.Refusal?.Message);
        runner.Start(projectPath);
        await runner.WhenIdleAsync().WaitAsync(TimeSpan.FromMinutes(5));
        var trialStatus = JobCommands.WaitForJob(new WaitForJobRequest(projectPath, trial.Value!.JobId,
            productVersion, TimeSpan.FromMinutes(1)));
        Assert.True(trialStatus.Succeeded, trialStatus.Refusal?.Message);
        Assert.Equal(JobStatus.Completed, trialStatus.Value!.Status);
        var trialAssessments = JobCommands.Assessments(new JobAssessmentsRequest(projectPath,
            trial.Value.JobId, productVersion));
        Assert.True(trialAssessments.Succeeded, trialAssessments.Refusal?.Message);
        var afterAssessment = Assert.Single(trialAssessments.Value!.Assessments,
            item => item.Kind == AssessmentKinds.ParseTime);
        Assert.NotEqual(beforeAssessment.AssessmentId, afterAssessment.AssessmentId);

        var reopened = ProposalCommands.Reopen(new ReopenRequest(projectPath, productVersion,
            draftName, proposalId));
        Assert.True(reopened.Succeeded, reopened.Refusal?.Message);

        using (var database = ProjectMotifDatabase.Open(projectPath))
        {
            var stored = new AssessmentRepository(database).Get(afterAssessment.AssessmentId);
            var dryRunRecord = new JobRepository(database).Get(dryRunJob.Value.JobId)!;
            var frozen = DryRunJobInput.Parse(dryRunRecord.InputJson);
            var source = DryRunJobCompletion.Parse(dryRunRecord.ResultJson!).SourceBaseline;
            var storedToken = JsonSerializer.Deserialize<BaselineToken>(stored.BaselineToken, MotifJson.CreateOptions());
            output.WriteLine($"Candidate Trial Assessment: proposal={stored.ProposalId?.Value}; " +
                $"intent={stored.ProposalIntentDigest}; expectedProposal={frozen.Proposal.ProposalId}; " +
                $"expectedIntent={frozen.Proposal.IntentDigest}; tokenMatches={storedToken == source.Token}; " +
                $"assessor={stored.Assessor}; kind={stored.Kind}; grammar={stored.GrammarSourceSha256}; " +
                $"invocationSource={stored.Invocation?.SourceBytesSha256}; " +
                $"grammarMatches={stored.GrammarSourceSha256 == stored.Invocation?.SourceBytesSha256}; " +
                $"parser={stored.Invocation?.ExecutableBytesSha256}; " +
                $"expectedParser=sha256:{Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(parserPath!))).ToLowerInvariant()}; " +
                $"threads={stored.Invocation?.Threads}; invoker={stored.Invocation is not null}");
        }

        var candidateJob = ParsimonyCommands.EnqueueCandidate(new EnqueueParsimonyCandidateRequest(
            projectPath, productVersion, dryRunJob.Value.JobId, "P-allo-alternation-family",
            ParsimonyEvidenceScopeKind.ProjectApproved, [afterAssessment.AssessmentId]));
        Assert.True(candidateJob.Succeeded, candidateJob.Refusal?.Message);
        runner.Start(projectPath);
        await runner.WhenIdleAsync().WaitAsync(TimeSpan.FromMinutes(10));
        var paired = ParsimonyCommands.WaitCandidate(new WaitForParsimonyCandidateRequest(projectPath,
            productVersion, candidateJob.Value!.JobId, TimeSpan.FromMinutes(1)));
        Assert.True(paired.Succeeded, paired.Refusal?.Message);

        var query = ParsimonyCommands.ReadRetirementReview(new ReadRetirementReviewRequest(
            projectPath, productVersion, proposalId));
        output.WriteLine($"Retirement query: {query.Value?.State ?? query.Refusal?.Code}; " +
            $"pending={string.Join(" | ", query.Value?.Unavailable ?? [])}");
        Assert.True(query.Succeeded, query.Refusal?.Message);
        var result = query.Value!;
        Assert.True(result.Applicable);
        Assert.Equal("review-ready", result.State);
        Assert.Empty(result.Unavailable);
        var review = Assert.IsType<RetirementProposalReviewProjection>(result.Review);
        Assert.Empty(review.Unavailable);
        Assert.Empty(review.UnlinkedEffects);
        Assert.Empty(review.UnlinkedOperationIds);
        Assert.NotEmpty(review.Finding.BindingOperationIds);
        Assert.NotEmpty(review.Finding.BindingEffects);

        var evidence = paired.Value!;
        Assert.NotEqual(evidence.Before.ReportId, evidence.After.ReportId);
        Assert.Equal("baseline", evidence.Before.Inputs.InputKind);
        Assert.Equal("candidate", evidence.After.Inputs.InputKind);
        Assert.Equal(evidence.Candidate.BaselineToken, evidence.Before.Inputs.BaselineToken);
        Assert.Equal(evidence.Candidate.BaselineToken, evidence.After.Inputs.BaselineToken);
        var beforeFinding = Assert.Single(evidence.Before.Findings,
            item => item.FindingId == boundFinding.FindingId);
        Assert.Equal(boundFinding.EvidenceDigest, beforeFinding.EvidenceDigest);
        Assert.Equal(CanonicalDigest(beforeFinding.EvidenceDigest), review.Finding.BeforeEvidenceDigest);
        var afterFinding = evidence.After.Findings.SingleOrDefault(
            item => item.FindingId == boundFinding.FindingId);
        Assert.Equal(CanonicalDigest(afterFinding?.EvidenceDigest ?? evidence.After.Inputs.Evidence.Sha256),
            review.Finding.AfterEvidenceDigest);
        Assert.True(review.Finding.Resolved);
        Assert.Equal(0, review.Finding.AfterNumerator);
        Assert.Equal(0, review.Finding.AfterDenominator);

        var translation = Assert.IsType<RetirementExpectationTranslation>(
            evidence.After.Inputs.RetirementExpectationTranslation);
        Assert.Equal(translation.MappingDigest,
            evidence.Before.Inputs.RetirementExpectationTranslation!.MappingDigest);
        Assert.NotEmpty(translation.Mappings);
        Assert.NotEmpty(translation.Original.Cases);
        Assert.StartsWith("sha256:", review.Statistics.DetailManifestDigest);
        Assert.Equal(review.DryRun.FootprintDigest, translation.DryRunFootprintDigest);
        Assert.Equal(review.DryRun.EffectDigest, translation.DryRunEffectDigest);
        Assert.Contains(review.AffectedReadings, item => item.Word == "ata");
        Assert.All(review.AffectedReadings, item => Assert.Equal("ata", item.Word));
        Assert.All(review.AffectedReadings,
            item => Assert.NotEqual("unavailable", item.VerificationStatus));
    }

    [RealParserFact]
    public async Task SeededF2FixturePinsNasalGoldAndItsMorphologyRuleAndFeaturelessControls()
    {
        Directory.CreateDirectory(_root);
        using var cache = pristine.NewScratch();
        var fixture = BuildNasalFixture(cache, pristine.Seed);
        new FwDataProjectLoader().Save(cache);
        var baseline = Token(cache);
        var sourceProject = File.ReadAllBytes(cache.ProjectId.Path);
        using var invoker = new PanGlossInvoker(PanGlossExecutable.TryLocate());

        var measured = await ReadAlternationFamilyAsync(cache, baseline, invoker, Path.Combine(_root, "nasal-facts"));
        Assert.Equal(sourceProject, File.ReadAllBytes(cache.ProjectId.Path));
        Assert.Equal(ParsimonyMeasureStatus.Inconclusive, measured.Measure.Run.Status);
        var finding = Assert.Single(measured.Measure.Findings);
        var family = Assert.IsType<ParsimonyAlternationFamilyViewRow>(Assert.Single(measured.FamilyView.Rows));
        var gold = new[]
        {
            new NasalGold(fixture.FirstRoot.Guid, fixture.FirstRoot.LexemeFormOA!.Guid,
                fixture.FirstSuffix.Guid, fixture.FirstAlternate.Guid, fixture.FirstWordform.Guid,
                fixture.FirstAnalysis.Guid, "an", "am", "aam"),
            new NasalGold(fixture.SecondRoot.Guid, fixture.SecondRoot.LexemeFormOA!.Guid,
                fixture.SecondSuffix.Guid, fixture.SecondAlternate.Guid, fixture.SecondWordform.Guid,
                fixture.SecondAnalysis.Guid, "in", "im", "iim"),
        };
        Assert.Equal(2L, finding.Number.Numerator);
        Assert.Equal(5L, finding.Number.Denominator);
        Assert.Equal(Opinions.approves, fixture.FirstAnalysis.GetAgentOpinion(cache.LangProject.DefaultUserAgent));
        Assert.Equal(Opinions.approves, fixture.SecondAnalysis.GetAgentOpinion(cache.LangProject.DefaultUserAgent));
        Assert.Equal(gold.Select(item => item.Entry.ToString("D")).Order(StringComparer.Ordinal),
            family.Members.Select(item => item.EntryGuid).Order(StringComparer.Ordinal));
        Assert.Collection(family.Members, _ => { }, _ => { });
        Assert.Equal(family.Members.Count, finding.Number.Numerator);
        Assert.Equal(gold.SelectMany(item => new[] { item.Underlying, item.Listed }).Order(StringComparer.Ordinal),
            family.Members.SelectMany(item => item.Forms).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));
        Assert.Equal(new[] { fixture.NasalN.ToGuid().ToString("D"), fixture.NasalM.ToGuid().ToString("D") }
                .Order(StringComparer.Ordinal),
            family.InputPhonemeGuids.Concat(family.OutputPhonemeGuids).Order(StringComparer.Ordinal));
        var changed = Assert.Single(family.ChangedFeatures);
        Assert.Equal(fixture.Features.FirstPositive.Feature.ToGuid().ToString("D"), changed.FeatureGuid);
        Assert.Equal(new[] { fixture.Features.FirstPositive.Value.ToGuid().ToString("D"),
                fixture.Features.FirstNegative.Value.ToGuid().ToString("D") }
                .Order(StringComparer.Ordinal),
            new[] { changed.InputValueGuid, changed.OutputValueGuid }.Order(StringComparer.Ordinal));
        Assert.Equal(new[]
            {
                fixture.Features.SecondPositive.Feature.ToGuid().ToString("D"),
                fixture.Features.ThirdPositive!.Feature.ToGuid().ToString("D"),
            }.Order(StringComparer.Ordinal),
            family.SharedFeatures.Select(item => item.FeatureGuid).Order(StringComparer.Ordinal));
        Assert.Contains("missing-feature-weighted-alignment-pair=1", measured.Measure.Run.Detail ?? string.Empty,
            StringComparison.Ordinal);
        Assert.Contains("multi-segment-change-pair", measured.Measure.Run.Detail ?? string.Empty,
            StringComparison.Ordinal);

        foreach (var item in gold)
        {
            var tracePath = Path.Combine(_root, item.Surface + ".trace.json");
            var parsed = Run(PanGlossExecutable.TryLocate()!, "parse", cache.ProjectId.Path, item.Surface,
                "--trace=" + tracePath, "--trace-format", "json", "--trace-details");
            Assert.Equal(0, parsed.ExitCode);
            using var trace = JsonDocument.Parse(File.ReadAllText(tracePath));
            var analyses = trace.RootElement.GetProperty("result").GetProperty("analyses").EnumerateArray().ToArray();
            Assert.Contains(analyses, analysis =>
            {
                var morphs = analysis.GetProperty("morphs").EnumerateArray().ToArray();
                return morphs.Length == 2 &&
                    morphs[0].GetProperty("identity").GetProperty("entryId").GetString() == item.RootEntry.ToString("D") &&
                    morphs[0].GetProperty("identity").GetProperty("formId").GetString() == item.RootForm.ToString("D") &&
                    morphs[1].GetProperty("identity").GetProperty("entryId").GetString() == item.Entry.ToString("D") &&
                    morphs[1].GetProperty("identity").GetProperty("formId").GetString() == item.Alternate.ToString("D");
            });
        }

        var health = Run(PanGlossExecutable.TryLocate()!, "grammar-health", cache.ProjectId.Path);
        Assert.True(health.ExitCode == 0, $"grammar-health exit={health.ExitCode}\n{health.Output}\n{health.Error}");
        var statedRuleId = AddUnconditionedRewriteRule(cache, fixture.NasalN, fixture.NasalM);
        new FwDataProjectLoader().Save(cache);
        var statedHealth = Run(PanGlossExecutable.TryLocate()!, "grammar-health", cache.ProjectId.Path);
        Assert.True(statedHealth.ExitCode == 0,
            $"grammar-health exit={statedHealth.ExitCode}\n{statedHealth.Output}\n{statedHealth.Error}");
        var statedTracePath = Path.Combine(_root, "aam-with-stated-rule.trace.json");
        var statedParse = Run(PanGlossExecutable.TryLocate()!, "parse", cache.ProjectId.Path, "aam",
            "--trace=" + statedTracePath, "--trace-format", "json", "--trace-details");
        Assert.Equal(0, statedParse.ExitCode);
        var statedTrace = File.ReadAllText(statedTracePath);
        Assert.Contains("\"type\":\"PhonologicalRuleSynthesis\"", statedTrace, StringComparison.Ordinal);
        Assert.Contains("\"outputShape\":\"aam\"", statedTrace, StringComparison.Ordinal);
        Assert.Contains(statedRuleId.ToGuid().ToString("D"), statedTrace, StringComparison.OrdinalIgnoreCase);
        var stated = await ReadAlternationFamilyAsync(cache, Token(cache), invoker, Path.Combine(_root, "stated-rule-facts"));
        Assert.Empty(stated.Measure.Findings);
        Assert.Empty(stated.FamilyView.Rows);
        Assert.Equal(ParsimonyMeasureStatus.Inconclusive, stated.Measure.Run.Status);

        output.WriteLine("Independent gold: suffix 1 an~am, root a, held surface aam; suffix 2 in~im, root i, held surface iim.");
        output.WriteLine($"AlternationFamily nasal family: 2 morphemes, input/output={fixture.NasalN.Value}/{fixture.NasalM.Value}, " +
            $"changed feature={changed.FeatureGuid}:{changed.InputValueGuid}~{changed.OutputValueGuid}, " +
            $"shared features={string.Join(",", family.SharedFeatures.Select(item => item.FeatureGuid))}.");
        output.WriteLine("Controls: suppletive multi-segment pair and unmapped-feature n/x→y shape abstain; one different-POS pair is not pooled.");
        output.WriteLine($"Already-stated active unconditioned n→m rule {statedRuleId.Value}: trace synthesizes aam and the AlternationFamily family is absent after reimport.");
        output.WriteLine(string.Join(Environment.NewLine, gold.Select(item =>
            $"F2 Approved case {item.Surface}: wordform={item.Wordform:D}, reading={item.Reading:D}; " +
            $"root={item.RootEntry:D}/{item.RootForm:D}; suffix={item.Entry:D}/{item.Alternate:D}; " +
            "PanGloss retained the exact two-morph gold.")));
    }

    private async Task<RecipeVerificationRun> ApplyAndAssessAsync(LcmCache cache, Proposal proposal,
        SIL.Motif.Model.DryRun.DryRun dryRun, PanGlossInvoker invoker, IReadOnlyList<string> words,
        string artifactRoot, OwnerFixture fixture, RetirementExpectationTranslation translated)
    {
        ProposalApplier.Apply(cache, proposal, dryRun.Anchor, "allomorph-rule-acceptance-system-test");
        Assert.Equal(Opinions.approves, fixture.TargetAnalysis.GetAgentOpinion(cache.LangProject.DefaultUserAgent));
        Assert.Equal("ata", fixture.TargetWord.Form.get_String(cache.DefaultVernWs)?.Text);
        Assert.Equal("ra", fixture.TargetSuffixBundle.Form.get_String(cache.DefaultVernWs)?.Text);
        Assert.Contains(translated.Translated.Cases.Single(item => item.Surface == "ata").Readings,
            reading => reading.Opinion == "approved" && reading.Morphs[1].Form ==
                CanonicalId.FromGuid(fixture.FirstSuffix.LexemeFormOA!.Guid).Value);
        new FwDataProjectLoader().Save(cache);
        using var afterCache = new ScratchCacheFactory().CreateFromFileCopy(cache.ProjectId.Path,
            Path.Combine(_root, "assessment-after-scratch"));
        return (await AssessAsync(afterCache.ProjectId.Path, invoker, words, artifactRoot, translated.Original.Baseline)).Run;
    }

    private async Task<(RecipeVerificationRun Run, IReadOnlyList<AssessedWord> Words)> AssessAsync(
        string projectPath, PanGlossInvoker invoker, IReadOnlyList<string> words, string artifactRoot,
        BaselineToken baseline)
    {
        var paths = new StatsCacheStore(WorkspaceOwnership.Bootstrap(artifactRoot));
        var assessor = new PanGlossAssessor(paths, invoker);
        var produced = await assessor.ProduceAsync(new AssessmentScope(words, [AssessmentKind.ParseTime],
            TimeSpan.FromMilliseconds(1000), new StepCap(200_000)), Path.GetDirectoryName(projectPath)!,
            CancellationToken.None);
        var assessment = Assert.Single(produced);
        try
        {
            var invocation = Assert.IsType<BatchInvocationEvidence>(assessment.Invocation);
            var batch = Assert.IsType<AssessmentRaw.Batch>(assessment.Raw).Analysis;
            var options = new RecipeVerificationOptions(invocation.ExecutableBytesSha256,
                invocation.PerWordTimeoutMs, invocation.PerWordStepLimit, invocation.Threads,
                invocation.CollectStatistics);
            var comparable = new ComparableAssessment(CanonicalId.Mint().Value, PanGlossAssessor.AssessorName,
                AssessmentKind.ParseTime.ToStoredKind(), "pangloss", "0.7.0", ToAssessedWords(batch.Words));
            var run = new RecipeVerificationRun(comparable, baseline, options, [], words);
            return (run, ToAssessedWords(batch.Words));
        }
        finally
        {
            assessment.ArtifactLease?.Dispose();
        }
    }

    private static IReadOnlyList<AssessedWord> ToAssessedWords(IReadOnlyList<WordAnalysis> words) =>
        words.Select(word => new AssessedWord(word.Word, word.Outcome.ToStoredOutcome(), [], word.ElapsedMs,
            word.Signature) { Morphology = word.Morphology }).ToArray();

    private async Task<AlternationFamilyRead> ReadAlternationFamilyAsync(LcmCache cache, BaselineToken token,
        PanGlossInvoker invoker, string artifactRoot)
    {
        Directory.CreateDirectory(artifactRoot);
        var parser = PanGlossExecutable.TryLocate()!;
        var snapshotPath = Path.Combine(artifactRoot, "grammar-snapshot.json");
        var imported = await invoker.RunAsync(new PanGlossRequest.Import(cache.ProjectId.Path, snapshotPath),
            "allomorph-rule-alternation-family-import", CancellationToken.None);
        Assert.IsType<PanGlossOutcome.Completed>(imported);
        var context = JsonSerializer.Serialize(new
        {
            format = "pangloss-facts-context",
            version = 1,
            baselineToken = token,
            inputKind = "baseline",
            dryRunDigest = (string?)null,
        }, WebJson);
        var factOutcome = await invoker.RunAsync(new PanGlossRequest.Facts(snapshotPath, context,
            Path.Combine(artifactRoot, "facts-invocation")), "allomorph-rule-alternation-family-facts", CancellationToken.None);
        var completed = Assert.IsType<PanGlossOutcome.Completed>(factOutcome);
        var facts = Assert.IsType<PanGlossFactsArtifact>(completed.Facts);
        var factsPath = Path.Combine(artifactRoot, "grammar-facts.sqlite");
        File.Copy(facts.Path, factsPath);
        completed.FactsArtifactLease?.Dispose();

        var textProjection = TextWordsProjectionBuilder.Build(cache, CancellationToken.None);
        var evidence = EvidenceProjectionBuilder.Build(cache, textProjection,
            new ParsimonyScopeBinding(ParsimonyEvidenceScopeKind.ProjectApproved, null), CancellationToken.None);
        var evidencePath = Path.Combine(artifactRoot, "evidence.sqlite");
        var tokenJson = JsonSerializer.Serialize(token, WebJson);
        var evidenceDigest = EvidenceWriter.Write(evidencePath, evidence, tokenJson, token.BundleDigest,
            facts.ModelFingerprint);
        var inputs = new ParsimonyReportInputs("allomorph-rule-rebuilt-" + Guid.NewGuid().ToString("N"), token,
            "baseline", null, facts.ModelFingerprint,
            new ParsimonyArtifactDigest(facts.SchemaVersion, StripDigest(facts.OutputSha256)), evidenceDigest,
            null, null, [], ParsimonyEvidenceScopeKind.ProjectApproved);
        using var session = new ParsimonyQuerySession(factsPath, evidencePath, inputs);
        var measure = MeasureRunner.Execute("P-allo-alternation-family", session, inputs.BundleId);
        var familyView = ParsimonyViewsQuery.Execute(session, new ParsimonyNamedViewRequest(inputs.BundleId,
            "alternation-families", new ParsimonyViewFilters()));
        return new AlternationFamilyRead(measure, familyView);
    }

    private static OwnerFixture BuildOwnerFixture(LcmCache cache, SeededProject seed)
    {
        var features = AddFeatures(cache);
        var r = AddPhoneme(cache, "r", [features.FirstPositive, features.SecondPositive]);
        var t = AddPhoneme(cache, "t", [features.FirstNegative, features.SecondPositive]);
        var a = AddPhoneme(cache, "a", [features.FirstPositive, features.SecondNegative]);
        var i = AddPhoneme(cache, "i", [features.FirstNegative, features.SecondNegative]);
        var ws = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultVernWs);
        var stemType = CanonicalId.FromGuid(MoMorphTypeTags.kguidMorphStem);
        Apply(cache, AuthorLexemeFormComposer.Build(cache,
            new(CanonicalId.FromGuid(seed.FirstEntryId), stemType, ws, "a")));
        Apply(cache, AuthorLexemeFormComposer.Build(cache,
            new(CanonicalId.FromGuid(seed.SecondEntryId), stemType, ws, "i")));

        var pos = cache.ServiceLocator.GetInstance<IPartOfSpeechRepository>().GetObject(seed.PartOfSpeechId);
        var first = AddSuffix(cache, "ra", "first ra suffix", pos);
        var second = AddSuffix(cache, "ra", "second ra suffix", pos);
        var firstTarget = AddAnalysis(cache, "ata", (cache.ServiceLocator.GetInstance<ILexEntryRepository>()
            .GetObject(seed.FirstEntryId).LexemeFormOA!, cache.ServiceLocator.GetInstance<ILexEntryRepository>()
            .GetObject(seed.FirstEntryId).MorphoSyntaxAnalysesOC.Single(), cache.ServiceLocator.GetInstance<ILexEntryRepository>()
            .GetObject(seed.FirstEntryId).SensesOS.Single()), (first.Alternate, first.Entry.MorphoSyntaxAnalysesOC.Single(), first.Entry.SensesOS.Single()));
        var secondTarget = AddAnalysis(cache, "ata", (cache.ServiceLocator.GetInstance<ILexEntryRepository>()
            .GetObject(seed.FirstEntryId).LexemeFormOA!, cache.ServiceLocator.GetInstance<ILexEntryRepository>()
            .GetObject(seed.FirstEntryId).MorphoSyntaxAnalysesOC.Single(), cache.ServiceLocator.GetInstance<ILexEntryRepository>()
            .GetObject(seed.FirstEntryId).SensesOS.Single()), (second.Alternate, second.Entry.MorphoSyntaxAnalysesOC.Single(), second.Entry.SensesOS.Single()), firstTarget.Wordform);
        var unchanged = AddAnalysis(cache, "ira", (cache.ServiceLocator.GetInstance<ILexEntryRepository>()
            .GetObject(seed.SecondEntryId).LexemeFormOA!, cache.ServiceLocator.GetInstance<ILexEntryRepository>()
            .GetObject(seed.SecondEntryId).MorphoSyntaxAnalysesOC.Single(), cache.ServiceLocator.GetInstance<ILexEntryRepository>()
            .GetObject(seed.SecondEntryId).SensesOS.Single()), (first.Entry.LexemeFormOA!, first.Entry.MorphoSyntaxAnalysesOC.Single(), first.Entry.SensesOS.Single()));

        var field = NotebookJudgmentFixture.InitializeReservedField(cache);
        var projectId = CanonicalId.FromGuid(cache.LangProject.Guid).Value;
        var negativeRevision = CanonicalId.FromGuid(Guid.NewGuid()).Value;
        var negative = new HumanJudgment(projectId, CanonicalId.FromGuid(Guid.NewGuid()).Value,
            negativeRevision, [], new ReviewedNegativeJudgment(CanonicalId.FromGuid(Guid.NewGuid()).Value,
                ws, "ri", "owner acceptance negative", new SurfaceNegativeTarget()),
            Actor: new JudgmentActor(JudgmentActorKind.Human));
        NotebookJudgmentFixture.AddRecord(cache, CanonicalId.Parse(negativeRevision).ToGuid(), field,
            TsStringUtils.MakeString(HumanJudgmentCodec.Format(negative), cache.DefaultAnalWs));
        return new(r, t, a, i, first.Entry, first.Alternate, second.Entry, second.Alternate,
            firstTarget.Wordform, firstTarget.Analysis, firstTarget.SuffixBundle, secondTarget.Analysis,
            secondTarget.SuffixBundle, unchanged.Wordform, firstTarget.EvaluationIds,
            firstTarget.Analysis.MorphBundlesOS.Select(item => item.Guid).ToArray(),
            secondTarget.EvaluationIds, secondTarget.Analysis.MorphBundlesOS.Select(item => item.Guid).ToArray());
    }

    private static void AddNotebookRecordType(LcmCache cache)
    {
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var notebook = cache.LangProject.ResearchNotebookOA;
            var recordTypes = notebook.RecTypesOA ?? cache.ServiceLocator
                .GetInstance<ICmPossibilityListFactory>().Create();
            notebook.RecTypesOA = recordTypes;
            var recordType = cache.ServiceLocator.GetInstance<ICmPossibilityFactory>().Create();
            recordTypes.PossibilitiesOS.Add(recordType);
            recordType.Name.set_String(cache.DefaultAnalWs,
                TsStringUtils.MakeString("Motif human judgment", cache.DefaultAnalWs));
        });
    }

    private static NasalFixture BuildNasalFixture(LcmCache cache, SeededProject seed)
    {
        var features = AddFeatures(cache, includeThird: true);
        var thirdPositive = features.ThirdPositive!;
        var thirdNegative = features.ThirdNegative!;
        var n = AddPhoneme(cache, "n", [features.FirstPositive, features.SecondPositive, thirdPositive]);
        var m = AddPhoneme(cache, "m", [features.FirstNegative, features.SecondPositive, thirdPositive]);
        var a = AddPhoneme(cache, "a", [features.FirstPositive, features.SecondNegative, thirdPositive]);
        var i = AddPhoneme(cache, "i", [features.FirstNegative, features.SecondNegative, thirdPositive]);
        var x = AddPhoneme(cache, "x", []);
        var y = AddPhoneme(cache, "y", [features.FirstPositive, features.SecondPositive, thirdNegative]);
        var ws = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultVernWs);
        var stemType = CanonicalId.FromGuid(MoMorphTypeTags.kguidMorphStem);
        Apply(cache, AuthorLexemeFormComposer.Build(cache,
            new(CanonicalId.FromGuid(seed.FirstEntryId), stemType, ws, "a")));
        Apply(cache, AuthorLexemeFormComposer.Build(cache,
            new(CanonicalId.FromGuid(seed.SecondEntryId), stemType, ws, "i")));
        var rootA = cache.ServiceLocator.GetInstance<ILexEntryRepository>().GetObject(seed.FirstEntryId);
        var rootI = cache.ServiceLocator.GetInstance<ILexEntryRepository>().GetObject(seed.SecondEntryId);
        var pos = cache.ServiceLocator.GetInstance<IPartOfSpeechRepository>().GetObject(seed.PartOfSpeechId);
        var otherPos = AddPartOfSpeech(cache, "Nasal control category");
        var first = AddSuffix(cache, "an", "first nasal morpheme", pos, "am");
        var second = AddSuffix(cache, "in", "second nasal morpheme", pos, "im");
        _ = AddSuffix(cache, "an", "separate MSA gate control", otherPos, "am");
        _ = AddSuffix(cache, "an", "suppletive control", pos, "imi");
        _ = AddSuffix(cache, "ax", "featureless control", pos, "ayy");
        var firstReading = AddAnalysis(cache, "aam",
            (rootA.LexemeFormOA!, rootA.MorphoSyntaxAnalysesOC.Single(), rootA.SensesOS.Single()),
            (first.Alternate, first.Entry.MorphoSyntaxAnalysesOC.Single(), first.Entry.SensesOS.Single()));
        var secondReading = AddAnalysis(cache, "iim",
            (rootI.LexemeFormOA!, rootI.MorphoSyntaxAnalysesOC.Single(), rootI.SensesOS.Single()),
            (second.Alternate, second.Entry.MorphoSyntaxAnalysesOC.Single(), second.Entry.SensesOS.Single()));
        return new(n, m, features, rootA, rootI, first.Entry, first.Alternate, second.Entry, second.Alternate,
            firstReading.Wordform, secondReading.Wordform, firstReading.Analysis, secondReading.Analysis);
    }

    private static IPartOfSpeech AddPartOfSpeech(LcmCache cache, string name)
    {
        IPartOfSpeech pos = null!;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            pos = cache.ServiceLocator.GetInstance<IPartOfSpeechFactory>().Create();
            cache.LangProject.PartsOfSpeechOA.PossibilitiesOS.Add(pos);
            pos.Name.set_String(cache.DefaultAnalWs, name);
        });
        return pos;
    }

    private static CanonicalId AddUnconditionedRewriteRule(LcmCache cache, CanonicalId input, CanonicalId output)
    {
        IPhRegularRule rule = null!;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            rule = cache.ServiceLocator.GetInstance<IPhRegularRuleFactory>().Create();
            cache.LangProject.PhonologicalDataOA.PhonRulesOS.Add(rule);
            rule.Name.set_String(cache.DefaultAnalWs, "already stated nasal alternation");
            rule.Direction = 2;
            rule.Disabled = false;
            var inputContext = cache.ServiceLocator.GetInstance<IPhSimpleContextSegFactory>().Create();
            rule.StrucDescOS.Add(inputContext);
            inputContext.FeatureStructureRA = Assert.IsAssignableFrom<IPhPhoneme>(
                cache.ServiceLocator.GetInstance<ICmObjectRepository>().GetObject(input.ToGuid()));
            var rhs = Assert.Single(rule.RightHandSidesOS);
            var outputContext = cache.ServiceLocator.GetInstance<IPhSimpleContextSegFactory>().Create();
            rhs.StrucChangeOS.Add(outputContext);
            outputContext.FeatureStructureRA = Assert.IsAssignableFrom<IPhPhoneme>(
                cache.ServiceLocator.GetInstance<ICmObjectRepository>().GetObject(output.ToGuid()));
        });
        return CanonicalId.FromGuid(rule.Guid);
    }

    private static (ILexEntry Entry, IMoAffixAllomorph Alternate) AddSuffix(LcmCache cache,
        string form, string gloss, IPartOfSpeech pos, string alternateForm = "ta")
    {
        ILexEntry entry = null!;
        IMoAffixAllomorph alternate = null!;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var type = cache.ServiceLocator.GetInstance<IMoMorphTypeRepository>()
                .GetObject(MoMorphTypeTags.kguidMorphSuffix);
            entry = cache.ServiceLocator.GetInstance<ILexEntryFactory>().Create(type,
                TsStringUtils.MakeString(form, cache.DefaultVernWs), gloss,
                new SandboxGenericMSA { MsaType = MsaType.kDeriv });
            var msa = Assert.IsAssignableFrom<IMoDerivAffMsa>(entry.MorphoSyntaxAnalysesOC.Single());
            msa.FromPartOfSpeechRA = pos;
            msa.ToPartOfSpeechRA = pos;
            alternate = cache.ServiceLocator.GetInstance<IMoAffixAllomorphFactory>().Create();
            entry.AlternateFormsOS.Add(alternate);
            alternate.MorphTypeRA = type;
            alternate.Form.set_String(cache.DefaultVernWs, alternateForm);
        });
        return (entry, alternate);
    }

    private static (IWfiWordform Wordform, IWfiAnalysis Analysis, IWfiMorphBundle SuffixBundle,
        Guid[] EvaluationIds) AddAnalysis(LcmCache cache, string surface,
        (IMoForm Form, IMoMorphSynAnalysis Msa, ILexSense Sense) root,
        (IMoForm Form, IMoMorphSynAnalysis Msa, ILexSense Sense) suffix, IWfiWordform? existingWordform = null)
    {
        IWfiWordform wordform = null!;
        IWfiAnalysis analysis = null!;
        IWfiMorphBundle suffixBundle = null!;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            wordform = existingWordform ?? cache.ServiceLocator.GetInstance<IWfiWordformFactory>()
                .Create(TsStringUtils.MakeString(surface, cache.DefaultVernWs));
            analysis = cache.ServiceLocator.GetInstance<IWfiAnalysisFactory>().Create();
            wordform.AnalysesOC.Add(analysis);
            foreach (var (form, msa, sense) in new[] { root, suffix })
            {
                var bundle = cache.ServiceLocator.GetInstance<IWfiMorphBundleFactory>().Create();
                analysis.MorphBundlesOS.Add(bundle);
                bundle.MorphRA = form;
                bundle.MsaRA = msa;
                bundle.SenseRA = sense;
                if (form is IMoAffixAllomorph) suffixBundle = bundle;
            }
            cache.LangProject.DefaultUserAgent.SetEvaluation(analysis, Opinions.approves);
            cache.LangProject.DefaultParserAgent.SetEvaluation(analysis, Opinions.approves);
        });
        return (wordform, analysis, suffixBundle, analysis.EvaluationsRC.Select(item => item.Guid).Order().ToArray());
    }

    private RetireAllomorphIntent Retirement(LcmCache cache, ILexEntry entry,
        IMoAffixAllomorph retired)
    {
        var replacement = Assert.IsAssignableFrom<IMoAffixAllomorph>(entry.LexemeFormOA);
        var entryId = CanonicalId.FromGuid(entry.Guid);
        var position = retired.MorphTypeRA!.Guid == MoMorphTypeTags.kguidMorphPrefix ? "prefix" : "suffix";
        var retiredIdentity = Identity(cache, retired, entryId, "alternate", position);
        var replacementIdentity = Identity(cache, replacement, entryId, "lexeme", position);
        var footprint = AllomorphReferenceFootprintReader.Read(cache, [retired.Guid]);
        output.WriteLine($"Census {retired.Guid:D}: references={footprint.References.Count}; " +
            $"unavailable={string.Join(" | ", footprint.Unavailable)}");
        var references = footprint.References.Where(item => item.TargetForm == retired.Guid).ToArray();
        output.WriteLine("Census routes: " + string.Join(" | ", references.Select(item =>
            $"{item.DeclaringClass}.{item.Field} ({item.Kind})")));
        var bundles = references.Where(item => item.Kind == "bundle-morph").ToArray();
        Assert.Single(bundles);
        Assert.DoesNotContain(references, item => item.Kind is "self-reference" or "derived-allomorph-view");
        Assert.Single(references, item => item.Kind == "entry-alternate-form");
        Assert.Equal(2, references.Length);
        var msa = entry.MorphoSyntaxAnalysesOC.Single();
        var role = new AllomorphRoleReplacement(CanonicalId.FromGuid(retired.Guid).Value,
            CanonicalId.FromGuid(msa.Guid).Value, null, "whole", replacementIdentity);
        var intent = new RetireAllomorphIntent(entryId.Value, AllomorphRetirementScope.Affix,
            [retiredIdentity], [role],
            bundles.Select(item => new RetirementBundle(CanonicalId.FromGuid(item.Bundle!.Value).Value,
                CanonicalId.FromGuid(item.Analysis!.Value).Value, CanonicalId.FromGuid(item.Wordform!.Value).Value,
                CanonicalId.FromGuid(retired.Guid).Value, CanonicalId.FromGuid(item.Msa!.Value).Value,
                item.InflType is { } infl ? CanonicalId.FromGuid(infl).Value : null, "whole")).ToArray(), []);
        var diagnostics = AllomorphReferenceFootprintReader.Diagnose(footprint, intent);
        Assert.Equal(references.Length, diagnostics.Classifications.Count);
        Assert.All(diagnostics.Classifications, item => Assert.False(string.IsNullOrWhiteSpace(item.Reason)));
        Assert.DoesNotContain(diagnostics.Classifications, item => item.Disposition == "blocking");
        return intent;
    }

    private static AllomorphIdentity Identity(LcmCache cache, IMoAffixAllomorph form,
        CanonicalId entry, string location, string position) => new(CanonicalId.FromGuid(form.Guid).Value, entry.Value,
        form.ClassName, location, position, AllomorphRetirementSemanticDigest.Compute(cache, form), null);

    private static ReplaceListedAllomorphsWithRuleIntent ComposeIntent(LcmCache cache, OwnerFixture fixture,
        IReadOnlyList<RetireAllomorphIntent> retirements)
    {
        var classId = CanonicalId.Mint();
        var ruleId = CanonicalId.Mint();
        var ids = Enumerable.Range(0, 13).Select(_ => CanonicalId.Mint()).ToArray();
        var bindings = new List<RetirementOperationBinding>
        {
            new(ids[0].Value, "class-create", classId.Value, null, []),
            new(ids[1].Value, "class-members", classId.Value, null, [ids[0].Value]),
            new(ids[2].Value, "rule-create", ruleId.Value, null, [ids[1].Value]),
        };
        var ruleOperations = ids.Skip(2).Take(7).ToArray();
        foreach (var (slot, index) in new[] { "rule-input", "rule-output", "rule-left", "rule-right", "rule-placement", "rule-enabled" }
                     .Select((slot, index) => (slot, index)))
            bindings.Add(new(ruleOperations[index + 1].Value, slot, ruleId.Value, null,
                [ids[1].Value, ids[2].Value]));
        var firstRetarget = CanonicalId.Mint();
        bindings.Add(new(firstRetarget.Value, "bundle-morph",
            CanonicalId.FromGuid(fixture.TargetSuffixBundle.Guid).Value, null,
            ruleOperations.Select(item => item.Value).ToArray()));
        var secondRetarget = CanonicalId.Mint();
        bindings.Add(new(secondRetarget.Value, "bundle-morph",
            CanonicalId.FromGuid(fixture.SecondTargetSuffixBundle.Guid).Value, null,
            ruleOperations.Select(item => item.Value).ToArray()));
        var firstDelete = CanonicalId.Mint();
        bindings.Add(new(firstDelete.Value, "alternate-delete", CanonicalId.FromGuid(fixture.FirstSuffix.Guid).Value,
            CanonicalId.FromGuid(fixture.FirstAlternate.Guid).Value,
            [firstRetarget.Value, secondRetarget.Value, .. ruleOperations.Select(item => item.Value)]));
        var secondDelete = CanonicalId.Mint();
        bindings.Add(new(secondDelete.Value, "alternate-delete", CanonicalId.FromGuid(fixture.SecondSuffix.Guid).Value,
            CanonicalId.FromGuid(fixture.SecondAlternate.Guid).Value,
            [firstRetarget.Value, secondRetarget.Value, .. ruleOperations.Select(item => item.Value)]));
        return new(new("create", classId.Value, "vowels", "V", [fixture.A.Value], null),
            new(ruleId.Value, "r becomes t between a segments", [fixture.R.Value],
                [fixture.T.Value],
                [new("segment", fixture.A.Value)],
                [new("natural-class", classId.Value)], new("last", null), true),
            retirements, bindings);
    }

    private void AssertPrefixRetirementParserOutcome(LcmCache cache, SeededProject seed, bool wordEdge,
        bool directWordBoundaryAcceptance = false)
    {
        var features = AddFeatures(cache);
        var r = AddPhoneme(cache, "r", [features.FirstPositive, features.SecondPositive]);
        var t = AddPhoneme(cache, "t", [features.FirstNegative, features.SecondPositive]);
        var a = AddPhoneme(cache, "a", [features.FirstPositive, features.SecondNegative]);
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            cache.LangProject.MorphologicalDataOA.ParserParameters =
                "<ParserParameters><HC><NoDefaultCompounding>true</NoDefaultCompounding><Strata /></HC></ParserParameters>");

        var classId = CanonicalId.Mint();
        var ruleId = CanonicalId.Mint();
        var boundaryId = wordEdge
            ? CanonicalId.FromGuid(LangProjectTags.kguidPhRuleWordBdry)
            : AddBoundaryMarker(cache, Guid.NewGuid(), "#");
        var root = cache.ServiceLocator.GetInstance<ILexEntryRepository>().GetObject(seed.FirstEntryId);
        var ws = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultVernWs);
        Apply(cache, AuthorLexemeFormComposer.Build(cache,
            new(CanonicalId.FromGuid(root.Guid), CanonicalId.FromGuid(MoMorphTypeTags.kguidMorphStem), ws, "a")));

        ILexEntry prefix = null!;
        IMoAffixAllomorph alternate = null!;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var prefixType = cache.ServiceLocator.GetInstance<IMoMorphTypeRepository>()
                .GetObject(MoMorphTypeTags.kguidMorphPrefix);
            prefix = cache.ServiceLocator.GetInstance<ILexEntryFactory>().Create(prefixType,
                TsStringUtils.MakeString("r", cache.DefaultVernWs), "r prefix",
                new SandboxGenericMSA { MsaType = MsaType.kDeriv });
            var msa = Assert.IsAssignableFrom<IMoDerivAffMsa>(prefix.MorphoSyntaxAnalysesOC.Single());
            var pos = cache.ServiceLocator.GetInstance<IPartOfSpeechRepository>().GetObject(seed.PartOfSpeechId);
            msa.FromPartOfSpeechRA = pos;
            msa.ToPartOfSpeechRA = pos;
            alternate = cache.ServiceLocator.GetInstance<IMoAffixAllomorphFactory>().Create();
            prefix.AlternateFormsOS.Add(alternate);
            alternate.MorphTypeRA = prefixType;
            alternate.Form.set_String(cache.DefaultVernWs, "t");
        });
        _ = AddAnalysis(cache, "ta",
            (alternate, prefix.MorphoSyntaxAnalysesOC.Single(), prefix.SensesOS.Single()),
            (root.LexemeFormOA!, root.MorphoSyntaxAnalysesOC.Single(), root.SensesOS.Single()));

        var retirement = Retirement(cache, prefix, alternate);
        var intent = PrefixRetirementIntent(classId, ruleId, r, t, a, boundaryId, retirement);
        var operations = ReplaceListedAllomorphsWithRuleComposer.Build(cache, intent,
            () => CanonicalId.FromGuid(Guid.NewGuid()));
        var proposal = new Proposal(new Dictionary<string, string>
            { ["analysis"] = "1.0", ["lexical"] = "1.0", ["phonology"] = "1.0" },
            CanonicalId.Mint(), null, operations.Reverse().ToArray());
        var dryRun = ScratchDryRun.Of(cache, proposal);
        ProposalApplier.Apply(cache, proposal, dryRun.Anchor, "r5-word-boundary-prefix-tests");
        Assert.False(Assert.IsAssignableFrom<IPhRegularRule>(cache.ServiceLocator.ObjectRepository
            .GetObject(ruleId.ToGuid())).Disabled);
        new FwDataProjectLoader().Save(cache);

        var caseName = wordEdge ? "word-edge" : "literal-hash";
        var retainedDirectory = Path.Combine(_root, caseName);
        CopyDirectory(Path.GetDirectoryName(cache.ProjectId.Path)!, retainedDirectory);
        var retainedProject = Path.Combine(retainedDirectory, Path.GetFileName(cache.ProjectId.Path));
        var parser = PanGlossExecutable.TryLocate()!;
        var importPath = Path.Combine(retainedDirectory, "import.json");
        var import = Run(parser, "import", retainedProject, importPath);
        using var imported = JsonDocument.Parse(File.ReadAllText(importPath));
        var rule = imported.RootElement.GetProperty("phonology").GetProperty("rules").EnumerateArray()
            .Single(item => item.GetProperty("guid").GetString() == ruleId.ToGuid().ToString("D"));
        var context = rule.GetProperty("rightHandSides")[0].GetProperty("leftContext");
        var boundaryMarkers = imported.RootElement.GetProperty("phonology").GetProperty("boundaryMarkers")
            .EnumerateArray().Select(item => item.GetProperty("guid").GetString()).ToArray();
        Assert.DoesNotContain(LangProjectTags.kguidPhRuleWordBdry.ToString("D"), boundaryMarkers);
        var parseInput = importPath;
        if (wordEdge)
        {
            Assert.Equal("wordBoundary", context.GetProperty("kind").GetString());
            Assert.False(context.TryGetProperty("marker", out _));
            Assert.DoesNotContain("does not resolve", import.Output + import.Error, StringComparison.Ordinal);
            if (directWordBoundaryAcceptance)
            {
                var directTracePath = Path.Combine(retainedDirectory, "ta-direct.trace.json");
                var directParse = Run(parser, "parse", retainedProject, "ta", "--trace=" + directTracePath,
                    "--trace-format", "json", "--trace-details");
                var directTrace = File.Exists(directTracePath) ? File.ReadAllText(directTracePath) : "trace file missing";
                var directDiagnostic = $"case={caseName}; parser={parser}; parse ta exit={directParse.ExitCode}\n" +
                    $"{directParse.Output}\n{directParse.Error}\n{directTrace}";
                Assert.True(directParse.ExitCode == 0, directDiagnostic);
                Assert.True(directTrace.Contains("\"type\":\"PhonologicalRuleSynthesis\"", StringComparison.Ordinal),
                    directDiagnostic);
                Assert.True(directTrace.Contains("\"outputShape\":\"ta\"", StringComparison.Ordinal), directDiagnostic);
                Assert.True(directTrace.Contains(ruleId.ToGuid().ToString("D"), StringComparison.OrdinalIgnoreCase),
                    directDiagnostic);
                output.WriteLine("Direct FWData: PhonologicalRuleSynthesis produces ta through the retired prefix and root.");
                output.WriteLine(directDiagnostic);
                return;
            }
        }
        else
        {
            Assert.Equal("boundary", context.GetProperty("kind").GetString());
            Assert.Equal(boundaryId.ToGuid().ToString("D"), context.GetProperty("marker").GetString());
            Assert.Contains(boundaryId.ToGuid().ToString("D"), boundaryMarkers);
        }
        var tracePath = Path.Combine(retainedDirectory, "ta.trace.json");
        var parse = Run(parser, "parse", parseInput, "ta", "--trace=" + tracePath,
            "--trace-format", "json", "--trace-details");
        var trace = File.Exists(tracePath) ? File.ReadAllText(tracePath) : "trace file missing";
        var diagnostic = $"case={caseName}; parser={parser}; import exit={import.ExitCode}\n{import.Output}\n{import.Error}\n" +
            $"parse ta exit={parse.ExitCode}\n{parse.Output}\n{parse.Error}\n{trace}";
        Assert.True(import.ExitCode == 0, diagnostic);
        Assert.True(parse.ExitCode == 0, diagnostic);
        Assert.Equal(wordEdge, boundaryId.ToGuid() == LangProjectTags.kguidPhRuleWordBdry);

        using var parsed = JsonDocument.Parse(trace);
        var analyses = parsed.RootElement.GetProperty("result").GetProperty("analyses");
        if (wordEdge)
        {
            var morphs = analyses.EnumerateArray().SelectMany(analysis =>
                analysis.GetProperty("morphs").EnumerateArray()).ToArray();
            Assert.Contains(morphs, morph => morph.GetProperty("identity").GetProperty("entryId").GetString() ==
                prefix.Guid.ToString("D"));
            Assert.Contains(morphs, morph => morph.GetProperty("identity").GetProperty("formId").GetString() ==
                prefix.LexemeFormOA!.Guid.ToString("D"));
            Assert.Contains("\"type\":\"PhonologicalRuleSynthesis\"", trace, StringComparison.Ordinal);
            Assert.Contains("\"outputShape\":\"ta\"", trace, StringComparison.Ordinal);
            Assert.Contains(ruleId.ToGuid().ToString("D"), trace, StringComparison.OrdinalIgnoreCase);

            var control = JsonNode.Parse(File.ReadAllText(parseInput))!;
            var controlRules = control["phonology"]!["rules"]!.AsArray();
            controlRules.RemoveAt(controlRules.IndexOf(controlRules.Single(item =>
                item!["guid"]!.GetValue<string>() == ruleId.ToGuid().ToString("D"))));
            var controlPath = Path.Combine(retainedDirectory, "rule-removed.json");
            File.WriteAllText(controlPath, control.ToJsonString());
            var controlTracePath = Path.Combine(retainedDirectory, "rule-removed.trace.json");
            var controlParse = Run(parser, "parse", controlPath, "ta", "--trace=" + controlTracePath,
                "--trace-format", "json", "--trace-details");
            Assert.True(controlParse.ExitCode == 0, diagnostic + $"\ncontrol stderr={controlParse.Error}");
            using var controlResult = JsonDocument.Parse(File.ReadAllText(controlTracePath));
            Assert.Empty(controlResult.RootElement.GetProperty("result").GetProperty("analyses").EnumerateArray());
            output.WriteLine("R5 word-edge snapshot: PhonologicalRuleSynthesis produces ta; rule removal leaves no analysis.");
            output.WriteLine("FWData import maps the reserved GUID to the wordBoundary node, outside the marker table.");
        }
        else
        {
            Assert.Empty(analyses.EnumerateArray());
            output.WriteLine("R5 literal-hash snapshot keeps the stored marker literal and ta has no analysis.");
        }
        output.WriteLine($"Parser={parser}; version=0.8.2; scratch={retainedProject}; rule={ruleId.Value}; context={boundaryId.Value}.");
    }

    private static ReplaceListedAllomorphsWithRuleIntent PrefixRetirementIntent(CanonicalId classId,
        CanonicalId ruleId, CanonicalId input, CanonicalId output, CanonicalId vowel, CanonicalId boundary,
        RetireAllomorphIntent retirement)
    {
        var ids = Enumerable.Range(0, 11).Select(_ => CanonicalId.Mint()).ToArray();
        var classCreate = ids[0];
        var classMembers = ids[1];
        var ruleCreate = ids[2];
        var ruleOperations = ids.Skip(2).Take(7).ToArray();
        var bindings = new List<RetirementOperationBinding>
        {
            new(classCreate.Value, "class-create", classId.Value, null, []),
            new(classMembers.Value, "class-members", classId.Value, null, [classCreate.Value]),
            new(ruleCreate.Value, "rule-create", ruleId.Value, null, [classMembers.Value]),
        };
        foreach (var (slot, index) in new[]
                 { "rule-input", "rule-output", "rule-left", "rule-right", "rule-placement", "rule-enabled" }
                 .Select((slot, index) => (slot, index)))
            bindings.Add(new(ruleOperations[index + 1].Value, slot, ruleId.Value, null,
                [classMembers.Value, ruleCreate.Value]));
        var retarget = ids[9];
        bindings.Add(new(retarget.Value, "bundle-morph",
            retirement.Bundles.Single().Bundle, null,
            ruleOperations.Select(item => item.Value).ToArray()));
        bindings.Add(new(ids[10].Value, "alternate-delete", retirement.Entry,
            retirement.RetiredForms.Single().Id,
            [retarget.Value, .. ruleOperations.Select(item => item.Value)]));
        return new(new("create", classId.Value, "vowels", "V", [vowel.Value], null),
            new(ruleId.Value, "r becomes t at word edge before a vowel", [input.Value], [output.Value],
                [new("boundary", boundary.Value)], [new("natural-class", classId.Value)], new("first", null), true),
            [retirement], bindings);
    }

    private static FeaturePairs AddFeatures(LcmCache cache, bool includeThird = false)
    {
        IFsClosedFeature first = null!;
        IFsClosedFeature second = null!;
        IFsClosedFeature? third = null;
        IFsSymFeatVal firstPositive = null!;
        IFsSymFeatVal firstNegative = null!;
        IFsSymFeatVal secondPositive = null!;
        IFsSymFeatVal secondNegative = null!;
        IFsSymFeatVal? thirdPositive = null;
        IFsSymFeatVal? thirdNegative = null;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            first = cache.ServiceLocator.GetInstance<IFsClosedFeatureFactory>().Create();
            second = cache.ServiceLocator.GetInstance<IFsClosedFeatureFactory>().Create();
            cache.LangProject.PhFeatureSystemOA.FeaturesOC.Add(first);
            cache.LangProject.PhFeatureSystemOA.FeaturesOC.Add(second);
            if (includeThird)
            {
                third = cache.ServiceLocator.GetInstance<IFsClosedFeatureFactory>().Create();
                cache.LangProject.PhFeatureSystemOA.FeaturesOC.Add(third);
            }
            first.Name.set_String(cache.DefaultAnalWs, "First segment feature");
            second.Name.set_String(cache.DefaultAnalWs, "Second segment feature");
            firstPositive = cache.ServiceLocator.GetInstance<IFsSymFeatValFactory>().Create();
            firstNegative = cache.ServiceLocator.GetInstance<IFsSymFeatValFactory>().Create();
            secondPositive = cache.ServiceLocator.GetInstance<IFsSymFeatValFactory>().Create();
            secondNegative = cache.ServiceLocator.GetInstance<IFsSymFeatValFactory>().Create();
            first.ValuesOC.Add(firstPositive);
            first.ValuesOC.Add(firstNegative);
            second.ValuesOC.Add(secondPositive);
            second.ValuesOC.Add(secondNegative);
            firstPositive.Name.set_String(cache.DefaultAnalWs, "positive");
            firstNegative.Name.set_String(cache.DefaultAnalWs, "negative");
            secondPositive.Name.set_String(cache.DefaultAnalWs, "positive");
            secondNegative.Name.set_String(cache.DefaultAnalWs, "negative");
            if (third is not null)
            {
                third.Name.set_String(cache.DefaultAnalWs, "Third segment feature");
                thirdPositive = cache.ServiceLocator.GetInstance<IFsSymFeatValFactory>().Create();
                thirdNegative = cache.ServiceLocator.GetInstance<IFsSymFeatValFactory>().Create();
                third.ValuesOC.Add(thirdPositive);
                third.ValuesOC.Add(thirdNegative);
                thirdPositive.Name.set_String(cache.DefaultAnalWs, "positive");
                thirdNegative.Name.set_String(cache.DefaultAnalWs, "negative");
            }
        });
        return new(new(CanonicalId.FromGuid(first.Guid), CanonicalId.FromGuid(firstPositive.Guid)),
            new(CanonicalId.FromGuid(first.Guid), CanonicalId.FromGuid(firstNegative.Guid)),
            new(CanonicalId.FromGuid(second.Guid), CanonicalId.FromGuid(secondPositive.Guid)),
            new(CanonicalId.FromGuid(second.Guid), CanonicalId.FromGuid(secondNegative.Guid)),
            thirdPositive is null ? null : new(CanonicalId.FromGuid(third!.Guid), CanonicalId.FromGuid(thirdPositive.Guid)),
            thirdNegative is null ? null : new(CanonicalId.FromGuid(third!.Guid), CanonicalId.FromGuid(thirdNegative.Guid)));
    }

    private static CanonicalId AddPhoneme(LcmCache cache, string symbol,
        IReadOnlyList<PhonologicalFeatureValue> features)
    {
        var operations = AuthorPhonemeComposer.Build(cache, new(symbol, [symbol], features));
        Apply(cache, operations);
        return operations.Single(item => item.Kind == PhPhonemeSetPhonemesOperationKinds.Create)
            .EntityId!.Value;
    }

    private static CanonicalId AddBoundaryMarker(LcmCache cache, Guid guid, string codeText)
    {
        IPhBdryMarker marker = null!;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var set = cache.LangProject.PhonologicalDataOA.PhonemeSetsOS.Single();
            marker = cache.ServiceLocator.GetInstance<IPhBdryMarkerFactory>().Create(guid);
            set.BoundaryMarkersOC.Add(marker);
            marker.Name.set_String(cache.DefaultVernWs, "literal hash marker");
            var code = cache.ServiceLocator.GetInstance<IPhCodeFactory>().Create();
            marker.CodesOS.Add(code);
            code.Representation.set_String(cache.DefaultVernWs, codeText);
        });
        return CanonicalId.FromGuid(marker.Guid);
    }

    private static BaselineToken Token(LcmCache cache)
    {
        new FwDataProjectLoader().Save(cache);
        return new BaselineToken(cache.LangProject.Guid.ToString("D"), BaselineSemanticDigest.Compute(cache),
            BaselineSemanticDigest.ProjectionVersion, DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"),
            BatchInvocationEvidence.DigestFile(cache.ProjectId.Path));
    }

    private static string HeldOutGrammar() => """
        <HermitCrabInput><Language><Name>RetirementHeldOut</Name>
          <PartsOfSpeech><PartOfSpeech id="pos"><Name>Noun</Name></PartOfSpeech></PartsOfSpeech>
          <PhonologicalFeatureSystem><SymbolicFeature id="f"><Name>f</Name><Symbols><Symbol id="fp">positive</Symbol><Symbol id="fn">negative</Symbol></Symbols></SymbolicFeature>
            <SymbolicFeature id="g"><Name>g</Name><Symbols><Symbol id="gp">positive</Symbol><Symbol id="gn">negative</Symbol></Symbols></SymbolicFeature>
            <SymbolicFeature id="h"><Name>h</Name><Symbols><Symbol id="hp">positive</Symbol><Symbol id="hn">negative</Symbol></Symbols></SymbolicFeature></PhonologicalFeatureSystem>
          <CharacterDefinitionTable id="table"><Name>Main</Name><SegmentDefinitions>
            <SegmentDefinition id="ca"><Representations><Representation>a</Representation></Representations><FeatureValue feature="f" symbolValues="fp"/><FeatureValue feature="g" symbolValues="gp"/><FeatureValue feature="h" symbolValues="hp"/></SegmentDefinition>
            <SegmentDefinition id="ci"><Representations><Representation>i</Representation></Representations><FeatureValue feature="f" symbolValues="fp"/><FeatureValue feature="g" symbolValues="gp"/><FeatureValue feature="h" symbolValues="hn"/></SegmentDefinition>
            <SegmentDefinition id="ck"><Representations><Representation>k</Representation></Representations><FeatureValue feature="f" symbolValues="fp"/><FeatureValue feature="g" symbolValues="gn"/><FeatureValue feature="h" symbolValues="hp"/></SegmentDefinition>
            <SegmentDefinition id="cr"><Representations><Representation>r</Representation></Representations><FeatureValue feature="f" symbolValues="fp"/><FeatureValue feature="g" symbolValues="gn"/><FeatureValue feature="h" symbolValues="hn"/></SegmentDefinition>
            <SegmentDefinition id="ct"><Representations><Representation>t</Representation></Representations><FeatureValue feature="f" symbolValues="fn"/><FeatureValue feature="g" symbolValues="gp"/><FeatureValue feature="h" symbolValues="hp"/></SegmentDefinition>
          </SegmentDefinitions></CharacterDefinitionTable>
          <NaturalClasses><SegmentNaturalClass id="nca"><Name>A</Name><Segment segment="ca"/></SegmentNaturalClass><SegmentNaturalClass id="ncr"><Name>R</Name><Segment segment="cr"/></SegmentNaturalClass><SegmentNaturalClass id="nct"><Name>T</Name><Segment segment="ct"/></SegmentNaturalClass></NaturalClasses>
          <PhonologicalRuleDefinitions><PhonologicalRule id="pr"><Name>r becomes t between a segments</Name><PhoneticInput><PhoneticSequence><SimpleContext naturalClass="ncr"/></PhoneticSequence></PhoneticInput>
            <PhonologicalSubrules><PhonologicalSubrule><PhoneticOutput><PhoneticSequence><SimpleContext naturalClass="nct"/></PhoneticSequence></PhoneticOutput><Environment>
              <LeftEnvironment><PhoneticTemplate><PhoneticSequence><SimpleContext naturalClass="nca"/></PhoneticSequence></PhoneticTemplate></LeftEnvironment>
              <RightEnvironment><PhoneticTemplate><PhoneticSequence><SimpleContext naturalClass="nca"/></PhoneticSequence></PhoneticTemplate></RightEnvironment>
            </Environment></PhonologicalSubrule></PhonologicalSubrules></PhonologicalRule></PhonologicalRuleDefinitions>
          <Strata><Stratum characterDefinitionTable="table" morphologicalRuleOrder="unordered" phonologicalRules="pr"><Name>Main</Name><LexicalEntries>
            <LexicalEntry id="holdout" partOfSpeech="pos"><Allomorphs><Allomorph id="holdout-form"><PhoneticShape>ikara</PhoneticShape></Allomorph></Allomorphs><MorphemeId>HELDOUT</MorphemeId><Gloss>heldout</Gloss></LexicalEntry>
          </LexicalEntries></Stratum></Strata>
        </Language></HermitCrabInput>
        """;

    private static string StripDigest(string digest) => digest.StartsWith("sha256:", StringComparison.Ordinal)
        ? digest["sha256:".Length..] : digest;

    private static string CanonicalDigest(string digest) => digest.StartsWith("sha256:", StringComparison.Ordinal)
        ? digest : "sha256:" + digest;

    private static void Apply(LcmCache cache, IReadOnlyList<OperationEnvelope> operations)
    {
        var proposal = new Proposal(new Dictionary<string, string>
            { ["phonology"] = "1.0", ["lexical"] = "1.0" }, CanonicalId.Mint(), null, operations);
        var dryRun = ScratchDryRun.Of(cache, proposal);
        ProposalApplier.Apply(cache, proposal, dryRun.Anchor, "allomorph-rule-fixture-builder");
    }

    private static ToolProcessResult Run(string executable, params string[] arguments)
    {
        var start = new ProcessStartInfo(executable);
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        return ToolProcess.Run(start);
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)), true);
        foreach (var directory in Directory.GetDirectories(source))
            CopyDirectory(directory, Path.Combine(target, Path.GetFileName(directory)));
    }

    private sealed record OwnerFixture(CanonicalId R, CanonicalId T, CanonicalId A, CanonicalId I,
        ILexEntry FirstSuffix, IMoAffixAllomorph FirstAlternate, ILexEntry SecondSuffix,
        IMoAffixAllomorph SecondAlternate, IWfiWordform TargetWord, IWfiAnalysis TargetAnalysis,
        IWfiMorphBundle TargetSuffixBundle, IWfiAnalysis SecondTargetAnalysis,
        IWfiMorphBundle SecondTargetSuffixBundle, IWfiWordform UnchangedWord, Guid[] TargetEvaluationIds,
        Guid[] TargetBundleOrder, Guid[] SecondTargetEvaluationIds, Guid[] SecondTargetBundleOrder);

    private sealed record NasalFixture(CanonicalId NasalN, CanonicalId NasalM, FeaturePairs Features,
        ILexEntry FirstRoot, ILexEntry SecondRoot, ILexEntry FirstSuffix, IMoAffixAllomorph FirstAlternate,
        ILexEntry SecondSuffix, IMoAffixAllomorph SecondAlternate, IWfiWordform FirstWordform,
        IWfiWordform SecondWordform, IWfiAnalysis FirstAnalysis, IWfiAnalysis SecondAnalysis);

    private sealed record NasalGold(Guid RootEntry, Guid RootForm, Guid Entry, Guid Alternate,
        Guid Wordform, Guid Reading, string Underlying, string Listed, string Surface);

    private sealed record AlternationFamilyRead(ParsimonyMeasureResult Measure, ParsimonyNamedViewResponse FamilyView);

    private sealed record FeaturePairs(PhonologicalFeatureValue FirstPositive,
        PhonologicalFeatureValue FirstNegative, PhonologicalFeatureValue SecondPositive,
        PhonologicalFeatureValue SecondNegative, PhonologicalFeatureValue? ThirdPositive,
        PhonologicalFeatureValue? ThirdNegative);
}
