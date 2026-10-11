using System.Security.Cryptography;
using SIL.LCModel;
using SIL.LCModel.DomainServices;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Assess;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Parsimony;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Commands.Store;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Contract.Retirement;
using SIL.Motif.Host;
using SIL.Motif.Host.Config;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Runner.Apply;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Store;
using SIL.Motif.Tests.Parser;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Host.Assess;
using SIL.Motif.Commands.Assess;
using SIL.Motif.Projection.Retirement;
using SIL.Motif.Host.Parser;
using Xunit;

namespace SIL.Motif.Tests.Retirement;

public sealed partial class AllomorphRuleRetirementAcceptanceTests
{
    [RealParserFact]
    public async Task OwnerRuleReplacementIsAppliedThroughTheStoredPathAndReadsBackFromTheSavedProject()
    {
        Directory.CreateDirectory(_root);
        var projectPath = pristine.CopyProjectFile();
        var workerRoot = Path.Combine(_root, "apply-worker");
        var parserPath = PanGlossExecutable.TryLocate();
        Assert.False(string.IsNullOrWhiteSpace(parserPath));
        var words = new[] { "ata", "ira", "ri" };
        ReplaceListedAllomorphsWithRuleIntent retirement;
        OwnerFixture fixture;
        Guid[] retiredIds;
        using (var cache = new FwDataProjectLoader().LoadScratchCache(projectPath))
        {
            fixture = BuildOwnerFixture(cache, pristine.Seed);
            AddNotebookRecordType(cache);
            retirement = ComposeIntent(cache, fixture,
            [
                Retirement(cache, fixture.FirstSuffix, fixture.FirstAlternate),
                Retirement(cache, fixture.SecondSuffix, fixture.SecondAlternate),
            ]);
            retiredIds = [fixture.FirstAlternate.Guid, fixture.SecondAlternate.Guid];
            new FwDataProjectLoader().Save(cache);
        }
        var beforeDirectory = Path.Combine(_root, "apply-before-project");
        CopyDirectory(Path.GetDirectoryName(projectPath)!, beforeDirectory);
        var beforeProject = Path.Combine(beforeDirectory, Path.GetFileName(projectPath));

        var baseline = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(projectPath), workerRoot);
        Assert.True(baseline.Succeeded, baseline.Refusal?.Message);
        var productVersion = MotifProductVersion.CurrentText;
        await using var runner = new InProcessRunnerLauncher(new JobRunnerLaunchOptions(workerRoot, parserPath));

        var reportJob = ParsimonyCommands.Enqueue(new EnqueueParsimonyReportRequest(projectPath,
            productVersion, "P-allo-alternation-family", ParsimonyEvidenceScopeKind.ProjectApproved));
        Assert.True(reportJob.Succeeded, reportJob.Refusal?.Message);
        runner.Start(projectPath);
        await runner.WhenIdleAsync().WaitAsync(TimeSpan.FromMinutes(5));
        var sourceReport = ParsimonyCommands.Wait(new WaitForParsimonyReportRequest(projectPath,
            productVersion, reportJob.Value!.JobId, TimeSpan.FromMinutes(1)));
        Assert.True(sourceReport.Succeeded, sourceReport.Refusal?.Message);
        var boundFinding = Assert.Single(sourceReport.Value!.Findings, finding =>
            finding.MeasureId == "P-allo-alternation-family" &&
            finding.AttachesTo.GroupKind == ParsimonyGroupKind.AlternationFamily);

        const string draftName = "owner a_r_V retirement apply";
        Assert.True(ProposalCommands.New(new NewDraftRequest(projectPath, productVersion, draftName,
            "Replace the listed alternation with its sound rule.")).Succeeded);
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
        Assert.True(ProposalCommands.Comment(new CommentRequest(projectPath, productVersion, draftName,
            "The retired alternates are represented by one sound rule.")).Succeeded);
        var finalized = ProposalCommands.Finalize(new FinalizeRequest(projectPath, productVersion, draftName));
        Assert.True(finalized.Succeeded, finalized.Refusal?.Message);
        var proposalId = finalized.Value!.ProposalId;

        var dryRunJob = JobCommands.EnqueueDryRun(new EnqueueDryRunRequest(projectPath, productVersion, proposalId));
        Assert.True(dryRunJob.Succeeded, dryRunJob.Refusal?.Message);
        runner.Start(projectPath);
        await runner.WhenIdleAsync().WaitAsync(TimeSpan.FromMinutes(5));
        var dryRun = JobCommands.WaitForDryRun(new WaitForDryRunRequest(projectPath, productVersion, proposalId,
            dryRunJob.Value!.JobId, TimeSpan.FromMinutes(1)));
        Assert.True(dryRun.Succeeded, dryRun.Refusal?.Message);

        var trial = JobCommands.EnqueueTrial(new EnqueueTrialRequest(projectPath, productVersion, proposalId,
            AssessmentScopeConfiguration.DefaultName, words));
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
        output.WriteLine("Trial kinds: " + string.Join(", ", trialAssessments.Value.Assessments.Select(item => item.Kind)));

        var candidateJob = ParsimonyCommands.EnqueueCandidate(new EnqueueParsimonyCandidateRequest(projectPath,
            productVersion, dryRunJob.Value.JobId, "P-allo-alternation-family",
            ParsimonyEvidenceScopeKind.ProjectApproved, [afterAssessment.AssessmentId]));
        Assert.True(candidateJob.Succeeded, candidateJob.Refusal?.Message);
        runner.Start(projectPath);
        await runner.WhenIdleAsync().WaitAsync(TimeSpan.FromMinutes(10));
        var paired = ParsimonyCommands.WaitCandidate(new WaitForParsimonyCandidateRequest(projectPath,
            productVersion, candidateJob.Value!.JobId, TimeSpan.FromMinutes(1)));
        Assert.True(paired.Succeeded, paired.Refusal?.Message);
        Assert.Contains(paired.Value!.Before.Findings, item => item.FindingId == boundFinding.FindingId);
        Assert.DoesNotContain(paired.Value.After.Findings, item => item.FindingId == boundFinding.FindingId);

        // The frozen expectations and the stored Proposal come from the saved project before Apply.
        Proposal stored;
        SIL.Motif.Model.DryRun.DryRun storedDryRun;
        using (var database = ProjectMotifDatabase.Open(projectPath))
        {
            stored = new ProposalRepository(database).GetFinalized(CanonicalId.Parse(proposalId)).Envelope;
            storedDryRun = JobCommands.ParsePublishedDryRun(new JobRepository(database).Get(dryRunJob.Value.JobId)!.DryRunJson!);
        }
        var ws = "";
        RetirementExpectationTranslation translated;
        using (var source = new ScratchCacheFactory().CreateFromFileCopy(beforeProject, Path.Combine(_root, "apply-source")))
        {
            ws = source.WritingSystemFactory.GetStrFromWs(source.DefaultVernWs);
            var sourceBaseline = Token(source);
            var frozen = FrozenExpectationCapture.Capture(source, sourceBaseline,
                [fixture.TargetWord.Guid, fixture.UnchangedWord.Guid],
                new HashSet<string>(StringComparer.Ordinal)
                {
                    FrozenExpectationCapture.CaseKey(CanonicalId.FromGuid(fixture.UnchangedWord.Guid).Value, ws, "ira"),
                },
                new HashSet<string>(StringComparer.Ordinal)
                {
                    FrozenExpectationCapture.CaseKey(CanonicalId.FromGuid(fixture.TargetWord.Guid).Value, ws, "ata"),
                });
            Assert.Empty(frozen.Unavailable);
            translated = RetirementExpectationTranslator.Translate(frozen, sourceBaseline,
                retirement.Retirements, stored, storedDryRun);
        }

        // Rollback: a fault after any operation leaves the saved file untouched and the loaded graph intact.
        var beforeHash = SHA256.HashData(File.ReadAllBytes(projectPath));
        var anchor = BoundAnchor(projectPath, proposalId);
        foreach (var failingIndex in new[] { 1, stored.Operations.Count / 2, stored.Operations.Count })
        {
            using var faulted = new FwDataProjectLoader().LoadScratchCache(projectPath);
            Assert.Throws<InvalidOperationException>(() => ProposalApplier.Apply(faulted, stored, anchor,
                string.Empty, "rollback", (index, _) =>
                {
                    if (index == failingIndex) throw new InvalidOperationException("injected apply fault");
                }));
            var repository = faulted.ServiceLocator.GetInstance<ICmObjectRepository>();
            Assert.All(retiredIds, id => Assert.True(repository.TryGetObject(id, out _)));
            Assert.DoesNotContain(faulted.LangProject.PhonologicalDataOA.PhonRulesOS,
                rule => rule.Guid == CanonicalId.Parse(retirement.Rule.Id).ToGuid());
            Assert.Equal(fixture.FirstAlternate.Guid, faulted.ServiceLocator.GetInstance<IWfiMorphBundleRepository>()
                .GetObject(fixture.TargetSuffixBundle.Guid).MorphRA!.Guid);
        }
        Assert.Equal(beforeHash, SHA256.HashData(File.ReadAllBytes(projectPath)));

        // Apply, through the same entry point as `motif apply <proposalId> --project --user`.
        var applied = ProposalCommands.Apply(new ApplyRequest(projectPath, productVersion, proposalId));
        Assert.True(applied.Succeeded, applied.Refusal?.Message);
        Assert.NotEqual(beforeHash, SHA256.HashData(File.ReadAllBytes(projectPath)));

        // Read back from the saved .fwdata in a fresh cache.
        using (var reopened = new FwDataProjectLoader().LoadScratchCache(projectPath))
        {
            var ruleGuid = CanonicalId.Parse(retirement.Rule.Id).ToGuid();
            var rule = Assert.IsAssignableFrom<IPhRegularRule>(Assert.Single(
                reopened.LangProject.PhonologicalDataOA.PhonRulesOS, item => item.Guid == ruleGuid));
            Assert.False(rule.Disabled);
            Assert.Equal(ruleGuid, reopened.LangProject.PhonologicalDataOA.PhonRulesOS.Last().Guid);
            var rhs = rule.RightHandSidesOS.Single();
            Assert.Equal(fixture.A.ToGuid(), Assert.IsAssignableFrom<IPhSimpleContextSeg>(
                rhs.LeftContextOA).FeatureStructureRA.Guid);
            var right = Assert.IsAssignableFrom<IPhSimpleContextNC>(rhs.RightContextOA);
            Assert.Equal("V", right.FeatureStructureRA.Abbreviation.get_String(reopened.DefaultAnalWs).Text);

            var repository = reopened.ServiceLocator.GetInstance<ICmObjectRepository>();
            Assert.All(retiredIds, id => Assert.False(repository.TryGetObject(id, out _)));
            Assert.DoesNotContain(reopened.ServiceLocator.GetInstance<ILexEntryRepository>().AllInstances()
                .SelectMany(entry => entry.AlternateFormsOS), form => retiredIds.Contains(form.Guid));
            var replacements = new[] { fixture.FirstSuffix.LexemeFormOA!.Guid, fixture.SecondSuffix.LexemeFormOA!.Guid };
            Assert.Equal(replacements[0], reopened.ServiceLocator.GetInstance<IWfiMorphBundleRepository>()
                .GetObject(fixture.TargetSuffixBundle.Guid).MorphRA!.Guid);
            Assert.Equal(replacements[1], reopened.ServiceLocator.GetInstance<IWfiMorphBundleRepository>()
                .GetObject(fixture.SecondTargetSuffixBundle.Guid).MorphRA!.Guid);
            Assert.All(reopened.ServiceLocator.GetInstance<IWfiMorphBundleRepository>().AllInstances(),
                bundle => Assert.DoesNotContain(bundle.MorphRA?.Guid ?? Guid.Empty, retiredIds));
        }
        var savedText = File.ReadAllText(projectPath);
        Assert.All(retiredIds, id => Assert.DoesNotContain(id.ToString("D"), savedText,
            StringComparison.OrdinalIgnoreCase));

        // Fresh parses of the frozen words, compared with the pre-Apply parses.
        using var invoker = new PanGlossInvoker(PanGlossExecutable.TryLocate());
        using var afterCache = new ScratchCacheFactory().CreateFromFileCopy(projectPath, Path.Combine(_root, "apply-after"));
        var beforeRun = await AssessAsync(beforeProject, invoker, words, Path.Combine(_root, "apply-assess-before"),
            translated.Original.Baseline);
        var afterRun = await AssessAsync(afterCache.ProjectId.Path, invoker, words,
            Path.Combine(_root, "apply-assess-after"), translated.Original.Baseline);
        var verification = RecipeVerification.Compare(translated, beforeRun.Run, afterRun.Run,
            RecipeVerificationCriteria.Tightening);
        Assert.Equal(RecipeVerificationStatus.Pass, verification.Status);
        Assert.Equal(3, verification.PreservedApprovedReadings);
        Assert.Equal(0, verification.LostApprovedReadings);
        Assert.Equal(0, verification.NewlyAcceptedNegativeCases);
        Assert.Equal(1, verification.CompletedNegativeCases);

        // The alternation finding no longer appears once the saved project is measured again.
        var afterFinding = await ReadAlternationFamilyAsync(afterCache, Token(afterCache), invoker,
            Path.Combine(_root, "apply-after-facts"));
        Assert.Empty(afterFinding.Measure.Findings);
        Assert.Empty(afterFinding.FamilyView.Rows);
        Assert.Equal(ParsimonyMeasureStatus.Computed, afterFinding.Measure.Run.Status);
    }

    private static SIL.Motif.Model.DryRun.BoundDryRunAnchor BoundAnchor(string projectPath, string proposalId)
    {
        using var database = ProjectMotifDatabase.Open(projectPath);
        var (record, _) = new ProposalRepository(database).GetFinalized(CanonicalId.Parse(proposalId));
        return Assert.IsType<SIL.Motif.Model.DryRun.BoundDryRunAnchor>(ProposalRecordMapping.ToManifest(record).Anchor);
    }

    [Fact]
    public void StemScopeIsRefusedBeforeAnyOperationIsComposed()
    {
        Directory.CreateDirectory(_root);
        using var cache = pristine.NewScratch();
        var fixture = BuildOwnerFixture(cache, pristine.Seed);
        var intent = ComposeIntent(cache, fixture, [Retirement(cache, fixture.FirstSuffix, fixture.FirstAlternate)]);
        Assert.True(AllomorphRetirementCapabilities.CanApplyFor(AllomorphRetirementScope.Affix));
        Assert.False(AllomorphRetirementCapabilities.CanApplyFor(AllomorphRetirementScope.Stem));
        var stem = intent with { Retirements = [intent.Retirements[0] with { Scope = AllomorphRetirementScope.Stem }] };
        var refusal = Assert.ThrowsAny<Exception>(() => ReplaceListedAllomorphsWithRuleComposer.Build(
            cache, stem, () => CanonicalId.Mint()));
        Assert.IsAssignableFrom<SystemException>(refusal);
        Assert.Contains("scope", refusal.Message, StringComparison.OrdinalIgnoreCase);
    }
}
