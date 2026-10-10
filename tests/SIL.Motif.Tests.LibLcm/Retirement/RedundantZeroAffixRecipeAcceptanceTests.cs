using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.Core.Text;
using SIL.LCModel.DomainServices;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands.Parsimony;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.HumanJudgments;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Retirement;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Parsimony;
using SIL.Motif.Host.Parser;
using SIL.Motif.LiveHost.Baselines;
using SIL.Motif.Model.Snapshot;
using SIL.Motif.Projection.HumanJudgments;
using SIL.Motif.Projection.Retirement;
using SIL.Motif.Runner.Apply;
using SIL.Motif.Runner.DryRun;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.Parser;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Assess;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Retirement;

[Collection(LcmCacheParallelCollections.Group1)]
[Trait("MotifTestLevel", "System")]
public sealed partial class RedundantZeroAffixRecipeAcceptanceTests(PristineProjectFixture pristine)
{
    private const string ProductVersion = "0.1.0";
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-zero-affix-recipe-" +
        Guid.NewGuid().ToString("N"));

    [RealParserFact]
    public async Task ZeroAffixFindingDryRunAndPairedAssessmentsVerifyWholeGraphRetirement()
    {
        Directory.CreateDirectory(_root);
        var project = RetireRedundantZeroAffixComposerTests.CreateZeroAffixProject(pristine, "^0",
            optional: true, prepareParser: true);
        using var cache = project.Cache;
        var fixture = project.Fixture;
        var category = cache.ServiceLocator.GetInstance<IPartOfSpeechRepository>()
            .GetObject(pristine.Seed.PartOfSpeechId);
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var template = cache.ServiceLocator.GetInstance<IMoInflAffixTemplateFactory>().Create();
            category.AffixTemplatesOS.Add(template);
            template.Final = true;
            template.SuffixSlotsRS.Add(fixture.Slot);
        });
        var wordforms = AddApprovedRootReadings(cache, "ka", "mi");
        AddReviewedNegative(cache, "to");
        new FwDataProjectLoader().Save(cache);

        var baseline = Token(cache);
        var frozen = FrozenExpectationCapture.Capture(cache, baseline, wordforms,
            new HashSet<string>(StringComparer.Ordinal), new HashSet<string>(StringComparer.Ordinal));
        Assert.Empty(frozen.Unavailable);
        Assert.Equal(2, frozen.Cases.Count);
        Assert.Single(frozen.ReviewedNegatives);

        using var invoker = new PanGlossInvoker(PanGlossExecutable.TryLocate());
        var beforeMeasure = await ReadZeroAffixAsync(cache, baseline, invoker, Path.Combine(_root, "zero-affix-before"));
        Assert.Equal(1, beforeMeasure.Measure.Run.EligibleItems);
        Assert.Equal(1, beforeMeasure.Measure.Run.FindingItems);
        var beforeRow = Assert.Single(beforeMeasure.Rows,
            row => row.MsaGuid == fixture.Msa.Guid.ToString("D"));
        Assert.True(beforeRow.IsLoadedZeroOnly);
        Assert.Equal("optional-slot-candidate", beforeRow.Classification);
        var beforeRealization = Assert.Single(beforeRow.Realizations);
        Assert.True(beforeRealization.Loaded);
        Assert.True(beforeRealization.CompilerRecognizedZero);
        Assert.Equal("represented", beforeRealization.LoaderReason);
        Assert.All(beforeRealization.Forms.Values, form => Assert.Equal("^0", form));
        var finding = Assert.Single(beforeMeasure.Measure.Findings);
        Assert.Equal(fixture.Msa.Guid.ToString("D"), finding.AttachesTo.Identity);

        var words = new[] { "ka", "mi", "to" };
        var beforeAssessment = await AssessAsync(cache.ProjectId.Path, words, baseline, invoker,
            Path.Combine(_root, "assess-before"));
        var zeroMsaId = fixture.Msa.Guid.ToString("D");
        Assert.Contains(ParserMsaIds(beforeAssessment), id => id == zeroMsaId);
        var proposal = RetireRedundantZeroAffixComposerTests.Proposal(cache, fixture.Entry);
        var ownedIds = RetireRedundantZeroAffixComposerTests.OwnedIds(cache, fixture.Entry);
        var scratchRoot = Path.Combine(_root, "dry-run");
        var scratchCache = new ScratchCacheFactory().CreateFromFileCopy(cache.ProjectId.Path, scratchRoot);
        string afterPath;
        SIL.Motif.Model.DryRun.DryRun dryRun;
        using (var scratch = DryRunScratch.Adopt(scratchCache, "ZeroAffix whole zero-affix graph retirement"))
        {
            dryRun = ProposalDryRunner.Run(scratch, PrerequisiteExecutionPlan.Create(proposal, [], []));
            afterPath = scratch.PeekCache().ProjectId.Path;
            new FwDataProjectLoader().Save(scratch.PeekCache());
        }
        Assert.Equal(2, dryRun.ExpectedEffects.Count);

        var afterAssessment = await AssessAsync(afterPath, words, baseline, invoker,
            Path.Combine(_root, "assess-after"));
        var verification = RecipeVerification.Compare(frozen, beforeAssessment, afterAssessment,
            RecipeVerificationCriteria.Tightening);
        Assert.Equal(RecipeVerificationStatus.Pass, verification.Status);
        Assert.DoesNotContain(ParserMsaIds(afterAssessment), id => id == zeroMsaId);
        Assert.Equal(0, verification.LostApprovedReadings);
        Assert.Equal(0, verification.NewlyAcceptedNegativeCases);
        Assert.False(verification.NegativeEvidenceGap);
        Assert.All(verification.Readings, reading =>
        {
            Assert.Equal(RecipeVerificationReadingStatus.Preserved, reading.Status);
            Assert.True(reading.BeforeComplete);
            Assert.True(reading.AfterComplete);
        });
        var negative = Assert.Single(verification.Negatives);
        Assert.Equal("surface", negative.Target);
        Assert.True(negative.BeforeComplete);
        Assert.True(negative.AfterComplete);
        Assert.False(negative.BeforeAccepted);
        Assert.False(negative.AfterAccepted);
        Assert.False(negative.NewlyAccepted);
        Assert.Equal("no-analysis", Assert.Single(beforeAssessment.Assessment.Words,
            word => word.Word == "to").Outcome);
        Assert.Equal("no-analysis", Assert.Single(afterAssessment.Assessment.Words,
            word => word.Word == "to").Outcome);

        var receipt = ProposalApplier.Apply(cache, proposal, dryRun.Anchor, "zero-affix-zero-affix-recipe-acceptance");
        Assert.Equal(2, receipt.ActualEffects.Count);
        new FwDataProjectLoader().Save(cache);
        var repository = cache.ServiceLocator.GetInstance<ICmObjectRepository>();
        Assert.All(ownedIds, id => Assert.False(repository.TryGetObject(id, out _)));
        Assert.True(repository.TryGetObject(fixture.Slot.Guid, out _));
        Assert.True(fixture.Slot.Optional);
        Assert.Contains(category.AffixTemplatesOS.SelectMany(item => item.SuffixSlotsRS),
            item => item.Guid == fixture.Slot.Guid);

        var afterToken = Token(cache);
        var afterMeasure = await ReadZeroAffixAsync(cache, afterToken, invoker, Path.Combine(_root, "zero-affix-after"));
        Assert.Equal(0, afterMeasure.Measure.Run.EligibleItems);
        Assert.Equal(0, afterMeasure.Measure.Run.FindingItems);
        Assert.DoesNotContain(afterMeasure.Rows,
            row => row.MsaGuid == fixture.Msa.Guid.ToString("D"));

        var healthOutcome = await invoker.RunAsync(new PanGlossRequest.GrammarHealth(cache.ProjectId.Path,
            "Synthetic"), "zero-affix-zero-affix-grammar-health", CancellationToken.None);
        var health = Assert.IsType<PanGlossOutcome.Completed>(healthOutcome);
        using var healthReport = JsonDocument.Parse(health.Output);
        Assert.DoesNotContain(healthReport.RootElement.GetProperty("diagnostics").EnumerateArray(),
            item => item.GetProperty("level").GetString() == "error");
    }

    private static Guid[] AddApprovedRootReadings(LcmCache cache, params string[] forms)
    {
        var result = new List<Guid>();
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            foreach (var form in forms)
            {
                var entry = Assert.Single(cache.ServiceLocator.GetInstance<ILexEntryRepository>().AllInstances(),
                    item => item.LexemeFormOA?.Form.get_String(cache.DefaultVernWs)?.Text == form);
                var wordform = cache.ServiceLocator.GetInstance<IWfiWordformFactory>().Create(
                    TsStringUtils.MakeString(form, cache.DefaultVernWs));
                var analysis = cache.ServiceLocator.GetInstance<IWfiAnalysisFactory>().Create();
                wordform.AnalysesOC.Add(analysis);
                var bundle = cache.ServiceLocator.GetInstance<IWfiMorphBundleFactory>().Create();
                analysis.MorphBundlesOS.Add(bundle);
                bundle.MorphRA = entry.LexemeFormOA;
                bundle.MsaRA = entry.MorphoSyntaxAnalysesOC.Single();
                cache.LangProject.DefaultUserAgent.SetEvaluation(analysis, Opinions.approves);
                result.Add(wordform.Guid);
            }
        });
        return result.ToArray();
    }

    private static void AddReviewedNegative(LcmCache cache, string form)
    {
        var field = NotebookJudgmentFixture.InitializeReservedField(cache);
        var judgmentId = Guid.NewGuid();
        var revisionId = Guid.NewGuid();
        var judgment = new HumanJudgment(CanonicalId.FromGuid(cache.LangProject.Guid).Value,
            CanonicalId.FromGuid(judgmentId).Value, CanonicalId.FromGuid(revisionId).Value, [],
            new ReviewedNegativeJudgment(CanonicalId.FromGuid(Guid.NewGuid()).Value,
                cache.WritingSystemFactory.GetStrFromWs(cache.DefaultVernWs), form,
                "ZeroAffix absence control", new SurfaceNegativeTarget()),
            Actor: new JudgmentActor(JudgmentActorKind.Human));
        NotebookJudgmentFixture.AddRecord(cache, revisionId, field,
            TsStringUtils.MakeString(HumanJudgmentCodec.Format(judgment), cache.DefaultAnalWs));
    }

    private static async Task<(ParsimonyMeasureResult Measure, IReadOnlyList<ParsimonyNullOptionalViewRow> Rows)>
        ReadZeroAffixAsync(LcmCache cache, BaselineToken token, PanGlossInvoker invoker, string artifactRoot)
    {
        Directory.CreateDirectory(artifactRoot);
        var snapshotPath = Path.Combine(artifactRoot, "grammar-snapshot.json");
        var imported = await invoker.RunAsync(new PanGlossRequest.Import(cache.ProjectId.Path, snapshotPath),
            "zero-affix-zero-affix-import", CancellationToken.None);
        Assert.IsType<PanGlossOutcome.Completed>(imported);
        var context = JsonSerializer.Serialize(new
        {
            format = "pangloss-facts-context",
            version = 1,
            baselineToken = token,
            inputKind = "baseline",
            dryRunDigest = (string?)null,
        }, WebJson);
        var factsOutcome = await invoker.RunAsync(new PanGlossRequest.Facts(snapshotPath, context,
                Path.Combine(artifactRoot, "facts-invocation")), "zero-affix-zero-affix-facts", CancellationToken.None);
        var factsCompleted = Assert.IsType<PanGlossOutcome.Completed>(factsOutcome);
        var facts = Assert.IsType<PanGlossFactsArtifact>(factsCompleted.Facts);
        var factsPath = Path.Combine(artifactRoot, "grammar-facts.sqlite");
        File.Copy(facts.Path, factsPath);
        factsCompleted.FactsArtifactLease?.Dispose();

        var text = TextWordsProjectionBuilder.Build(cache, CancellationToken.None);
        var evidence = EvidenceProjectionBuilder.Build(cache, text,
            new ParsimonyScopeBinding(ParsimonyEvidenceScopeKind.ProjectApproved, null), CancellationToken.None);
        var evidencePath = Path.Combine(artifactRoot, "evidence.sqlite");
        var evidenceDigest = EvidenceWriter.Write(evidencePath, evidence,
            JsonSerializer.Serialize(token, WebJson), token.BundleDigest, facts.ModelFingerprint);
        var inputs = new ParsimonyReportInputs("zero-affix-zero-affix-" + Guid.NewGuid().ToString("N"), token,
            "baseline", null, facts.ModelFingerprint,
            new ParsimonyArtifactDigest(facts.SchemaVersion, StripDigest(facts.OutputSha256)), evidenceDigest,
            null, null, [], ParsimonyEvidenceScopeKind.ProjectApproved);
        using var session = new ParsimonyQuerySession(factsPath, evidencePath, inputs);
        var measure = MeasureRunner.Execute("B-affix-null-vs-optional", session, inputs.BundleId);
        var rows = ParsimonyViewsQuery.Execute(session, new ParsimonyNamedViewRequest(inputs.BundleId,
                "null-optional", new ParsimonyViewFilters()))
            .Rows.Cast<ParsimonyNullOptionalViewRow>().ToArray();
        return (measure, rows);
    }

    private static async Task<RecipeVerificationRun> AssessAsync(string projectPath, IReadOnlyList<string> words,
        BaselineToken baseline, PanGlossInvoker invoker, string artifactRoot)
    {
        var store = new StatsCacheStore(WorkspaceOwnership.Bootstrap(artifactRoot));
        var assessor = new PanGlossAssessor(store, invoker);
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
                AssessmentKind.ParseTime.ToStoredKind(), "pangloss", "0.7.0",
                batch.Words.Select(word => new AssessedWord(word.Word, word.Outcome.ToStoredOutcome(), [],
                    word.ElapsedMs, word.Signature) { Morphology = word.Morphology }).ToArray());
            return new RecipeVerificationRun(comparable, baseline, options, [], words);
        }
        finally
        {
            assessment.ArtifactLease?.Dispose();
        }
    }

    private static BaselineToken Token(LcmCache cache) => new(cache.LangProject.Guid.ToString("D"),
        BaselineSemanticDigest.Compute(cache), BaselineSemanticDigest.ProjectionVersion,
        DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"), BatchInvocationEvidence.DigestFile(cache.ProjectId.Path));

    private static IReadOnlyList<string?> ParserMsaIds(RecipeVerificationRun run) => run.Assessment.Words
        .SelectMany(word => Assert.IsType<ParseWordEvidence>(word.Morphology).Analyses)
        .SelectMany(analysis => analysis.Morphs)
        .Select(morph => morph.Msa).ToArray();

    private static string StripDigest(string digest) => digest.StartsWith("sha256:", StringComparison.Ordinal)
        ? digest[7..]
        : digest;
}
