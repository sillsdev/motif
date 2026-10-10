using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.Core.Text;
using SIL.LCModel.DomainServices;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Parsimony;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.HumanJudgments;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Retirement;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Parser;
using SIL.Motif.Host.Parsimony;
using SIL.Motif.LiveHost.Baselines;
using SIL.Motif.Model.DryRun;
using SIL.Motif.Projection.HumanJudgments;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.DryRun;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.Composers;
using SIL.Motif.Tests.Parser;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Assess;
using SIL.Motif.Worker.Store;
using Xunit;
using Xunit.Abstractions;

namespace SIL.Motif.Tests.Parsimony;

[Collection(LcmCacheParallelCollections.Group3)]
[Trait("MotifTestLevel", "System")]
public sealed partial class ConditionReviewRecipeAcceptanceTests(
    PristineProjectFixture pristine, ITestOutputHelper output) : IDisposable
{
    private const string ProductVersion = "0.1.0";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-condition-review-" +
        Guid.NewGuid().ToString("N"));

    [RealParserFact]
    public async Task UnconditionedAllomorphEditConditionRemovesTheWrongReadingAndPreservesElsewhereAndHeldOutReadings()
    {
        using var cache = pristine.NewScratch();
        var fixture = MakeUnconditionedAllomorph(cache, broadFirst: true, includeSeparateFallback: true);
        var result = await RunProposalAsync(cache, fixture.Wordforms, fixture.Words, "unconditioned-allomorph-edit-condition",
            [Case(cache, fixture.PkaWordform, "pka")], [Case(cache, fixture.TkaWordform, "tka")],
            (path, draft) => ComposeCondition(path, draft, fixture.Broad, [], [fixture.AfterT]));

        Assert.NotNull(result.Proposal);
        Assert.Single(result.Proposal!.Operations, operation => operation.Target?.Value == fixture.Broad);
        AssertRecipePassed(result);
        Assert.Equal(0, result.Verification.LostApprovedReadings);
        Assert.Equal(0, result.Verification.NewlyAcceptedNegativeCases);
        Assert.False(result.Verification.NegativeEvidenceGap);
        AssertAllFrozenApprovedReadingsProduced(result);
        AssertReviewedNegativesRejected(result.Verification);
        var disapproved = Assert.Single(result.Verification.Negatives,
            item => item.Target == "disapproved-reading");
        Assert.True(disapproved.IdentityAvailable);
        Assert.True(disapproved.BeforeComplete);
        Assert.True(disapproved.BeforeAccepted);
        Assert.True(disapproved.AfterComplete);
        Assert.False(disapproved.AfterAccepted);
        AssertParserContains(result.After, "pka", fixture.PrefixP, fixture.Conditioned);
        AssertParserContains(result.After, "tka", fixture.PrefixT, fixture.Broad);
        AssertParserContains(result.After, "aka", fixture.PrefixA, fixture.SeparateFallback);
        using var after = new FwDataProjectLoader().LoadScratchCache(result.AfterProjectPath);
        var broad = Resolve<IMoStemAllomorph>(after, fixture.Broad);
        Assert.Equal(new[] { fixture.AfterT }, broad.PhoneEnvRC.Select(item => CanonicalId.FromGuid(item.Guid).Value));
        output.WriteLine("UnconditionedAllomorph condition edit preserved exact Approved readings, the elsewhere reading, and the held-out positive.");
        output.WriteLine("The reviewed broad reading was rejected after the condition edit.");
    }

    [RealParserFact]
    public async Task UnconditionedAllomorphOrderMakesTheBroadVariantTheFinalFallback()
    {
        using var cache = pristine.NewScratch();
        var fixture = MakeUnconditionedAllomorph(cache, broadFirst: true, includeSeparateFallback: false);
        var result = await RunProposalAsync(cache, fixture.Wordforms, fixture.Words, "unconditioned-allomorph-order-fallback",
            [Case(cache, fixture.PkaWordform, "pka")], [Case(cache, fixture.TkaWordform, "tka")],
            (path, draft) => ComposeOrder(path, draft, fixture.Entry,
                [fixture.Broad, fixture.Conditioned], [fixture.Conditioned, fixture.Broad]));

        AssertRecipePassed(result);
        Assert.Equal(0, result.Verification.LostApprovedReadings);
        Assert.Equal(0, result.Verification.NewlyAcceptedNegativeCases);
        Assert.False(result.Verification.NegativeEvidenceGap);
        AssertAllFrozenApprovedReadingsProduced(result);
        AssertReviewedNegativesRejected(result.Verification);
        AssertParserContains(result.After, "pka", fixture.PrefixP, fixture.Conditioned);
        AssertParserContains(result.After, "tka", fixture.PrefixT, fixture.Broad);
        AssertParserContains(result.After, "aka", fixture.PrefixA, fixture.Broad);
        using var after = new FwDataProjectLoader().LoadScratchCache(result.AfterProjectPath);
        var entry = Resolve<ILexEntry>(after, fixture.Entry);
        Assert.Equal(new[] { fixture.Conditioned, fixture.Broad },
            entry.AlternateFormsOS.Select(item => CanonicalId.FromGuid(item.Guid).Value));
        output.WriteLine("UnconditionedAllomorph OrderAllomorphs put the broad variant after the conditioned variant; the Approved elsewhere reading remained parsed.");
        output.WriteLine("The reviewed broad reading was rejected in the context selected by the conditioned variant.");
    }

    [RealParserFact]
    public async Task UnconditionedAllomorphFinalFallbackCanBeKeptWithAReasonWithoutCreatingAProposal()
    {
        using var cache = pristine.NewScratch();
        var fixture = MakeUnconditionedAllomorph(cache, broadFirst: false, includeSeparateFallback: false);
        Assert.Equal(fixture.Broad, CanonicalId.FromGuid(
            cache.ServiceLocator.GetInstance<ILexEntryRepository>().GetObject(
                CanonicalId.Parse(fixture.Entry).ToGuid()).AlternateFormsOS.Last().Guid).Value);
        var reason = "The variant is the confirmed final fallback for forms outside the conditioned contexts.";
        RecordKeep(cache, "R-allo-unconditioned", fixture.Broad, reason);
        new FwDataProjectLoader().Save(cache);
        var snapshot = HumanJudgmentReader.Read(cache);
        var disposition = Assert.Single(snapshot.Judgments).Judgment;
        Assert.Equal(reason, disposition.Reason);
        Assert.Equal(ParsimonyDispositionKind.Keep,
            Assert.IsType<DispositionJudgment>(disposition.Body).Disposition);
        Assert.Empty(snapshot.Unavailable);
        var result = await RunNoChangeAsync(cache, fixture.Wordforms, fixture.Words, "unconditioned-allomorph-keep",
            [Case(cache, fixture.PkaWordform, "pka")], [Case(cache, fixture.TkaWordform, "tka")]);
        AssertRecipePassed(result);
        Assert.Equal(0, result.Verification.LostApprovedReadings);
        Assert.Equal(0, result.Verification.NewlyAcceptedNegativeCases);
        Assert.Null(result.Proposal);
        Assert.Null(result.DryRun);
        AssertAllFrozenApprovedReadingsProduced(result);
        AssertReviewedNegativesRejected(result.Verification);
        AssertParserContains(result.After, "tka", fixture.PrefixT, fixture.Broad);
        AssertParserContains(result.After, "aka", fixture.PrefixA, fixture.Broad);
        output.WriteLine("The final fallback was kept with a Notebook reason; no grammar Proposal or Dry Run was created.");
    }

    [RealParserFact]
    public async Task BroadEnvironmentNarrowEnvironmentProposalKeepsSharedUsersAndRejectsTheReviewedWrongContext()
    {
        using var cache = pristine.NewScratch();
        var fixture = MakeBroadEnvironment(cache, includeWrongContextNegative: true);
        var result = await RunProposalAsync(cache, fixture.Wordforms, fixture.Words, "broad-environment-narrow-environment",
            [Case(cache, fixture.PkaWordform, "pka")],
            [Case(cache, fixture.BkaWordform, "bka"), Case(cache, fixture.OtherWordform, "tku")],
            (path, draft) => ComposeNarrowEnvironment(path, draft, fixture));

        AssertRecipePassed(result);
        Assert.Equal(0, result.Verification.LostApprovedReadings);
        Assert.Equal(0, result.Verification.NewlyAcceptedNegativeCases);
        Assert.False(result.Verification.NegativeEvidenceGap);
        AssertAllFrozenApprovedReadingsProduced(result);
        AssertReviewedNegativesRejected(result.Verification);
        AssertParserContains(result.After, "pka", fixture.PrefixP, fixture.TargetAllomorph);
        AssertParserContains(result.After, "bka", fixture.PrefixB, fixture.TargetAllomorph);
        AssertParserContains(result.After, "tku", fixture.PrefixT, fixture.SharedAllomorph);
        AssertParserDoesNotContain(result.After, "tka", fixture.PrefixT, fixture.TargetAllomorph);

        Assert.NotNull(result.Proposal);
        var createdEnvironment = Assert.Single(result.Proposal!.Operations, operation =>
            operation.EntityId?.Value == result.AuthoredEnvironmentId);
        Assert.Contains(result.Proposal.Operations, operation => operation.Target?.Value == fixture.TargetAllomorph &&
            operation.DependsOn.Any(dependency => dependency.OperationId == createdEnvironment.OperationId));
        using var after = new FwDataProjectLoader().LoadScratchCache(result.AfterProjectPath);
        var target = Resolve<IMoStemAllomorph>(after, fixture.TargetAllomorph);
        var shared = Resolve<IMoStemAllomorph>(after, fixture.SharedAllomorph);
        Assert.Equal(new[] { result.AuthoredEnvironmentId },
            target.PhoneEnvRC.Select(item => CanonicalId.FromGuid(item.Guid).Value));
        Assert.Equal(new[] { fixture.BroadEnvironment },
            shared.PhoneEnvRC.Select(item => CanonicalId.FromGuid(item.Guid).Value));
        Assert.Contains(Resolve<IPhEnvironment>(after, fixture.BroadEnvironment),
            shared.PhoneEnvRC);
        output.WriteLine("AuthorEnvironment and EditAllomorphCondition used a declared dependency and changed only the intended allomorph user.");
        output.WriteLine("Approved p/b contexts and the independent shared user remained parsed; reviewed t-context was rejected.");
    }

    [RealParserFact]
    public async Task BroadEnvironmentProductiveHeldOutExtensionCanBeKeptWithoutGrammarChange()
    {
        using var cache = pristine.NewScratch();
        var fixture = MakeBroadEnvironment(cache, includeWrongContextNegative: false);
        var reason = "A held-out Approved t-context confirms this extension remains productive.";
        RecordKeep(cache, "R-env-broad", fixture.TargetAllomorph, reason);
        var disposition = Assert.Single(HumanJudgmentReader.Read(cache).Judgments).Judgment;
        Assert.Equal(reason, disposition.Reason);
        Assert.Equal(ParsimonyDispositionKind.Keep,
            Assert.IsType<DispositionJudgment>(disposition.Body).Disposition);

        var result = await RunNoChangeAsync(cache, fixture.Wordforms, fixture.Words, "broad-environment-keep",
            [Case(cache, fixture.PkaWordform, "pka")], [Case(cache, fixture.TkaWordform, "tka")]);
        AssertRecipePassed(result);
        Assert.Equal(0, result.Verification.LostApprovedReadings);
        Assert.Equal(0, result.Verification.NewlyAcceptedNegativeCases);
        Assert.Null(result.Proposal);
        Assert.Null(result.DryRun);
        AssertAllFrozenApprovedReadingsProduced(result);
        AssertParserContains(result.After, "tka", fixture.PrefixT, fixture.TargetAllomorph);
        output.WriteLine("The held-out Approved t-context confirmed a productive extension; the grammar was unchanged and no Proposal was created.");
    }

    private async Task<Acceptance> RunProposalAsync(LcmCache cache, IReadOnlyList<Guid> wordforms,
        IReadOnlyList<string> words, string draftName, IReadOnlyCollection<string> selection,
        IReadOnlyCollection<string> heldOut,
        Func<string, string, CommandOutcome<ComposedOperationsResponse>> compose)
    {
        Directory.CreateDirectory(_root);
        var projectPath = cache.ProjectId.Path;
        NotebookJudgmentFixture.InitializeReservedField(cache);
        new FwDataProjectLoader().Save(cache);
        var frozen = Capture(cache, wordforms, selection.ToHashSet(StringComparer.Ordinal),
            heldOut.ToHashSet(StringComparer.Ordinal));
        cache.Dispose();

        using var invoker = new PanGlossInvoker(PanGlossExecutable.TryLocate());
        var before = await AssessAsync(projectPath, invoker, words, Path.Combine(_root, draftName + "-before"),
            frozen.Baseline);
        Assert.True(ProposalCommands.New(new NewDraftRequest(projectPath, ProductVersion, draftName,
            "Verify a parsimony condition repair with paired parser Assessments.")).Succeeded);
        var composed = compose(projectPath, draftName);
        Assert.True(composed.Succeeded, composed.Refusal?.Message);
        Assert.True(ProposalCommands.Label(new LabelRequest(projectPath, ProductVersion, draftName,
            "Parsimony condition repair acceptance")).Succeeded);
        Assert.True(ProposalCommands.Comment(new CommentRequest(projectPath, ProductVersion, draftName,
            "Exact Approved readings, held-out positives, and reviewed negatives are checked against paired Assessments.")).Succeeded);
        var finalized = ProposalCommands.Finalize(new FinalizeRequest(projectPath, ProductVersion, draftName));
        Assert.True(finalized.Succeeded, finalized.Refusal?.Message);
        var loaded = ProjectStoreCommand.Run<Proposal>(projectPath, ProductVersion, (database, _) =>
            CommandOutcome<Proposal>.Success(new ProposalRepository(database)
                .GetFinalized(CanonicalId.Parse(finalized.Value!.ProposalId)).Envelope));
        Assert.True(loaded.Succeeded, loaded.Refusal?.Message);
        var proposal = loaded.Value!;

        var dryRunCache = new ScratchCacheFactory().CreateFromFileCopy(projectPath,
            Path.Combine(_root, draftName + "-dry-run"));
        string afterPath;
        DryRun dryRun;
        using (var scratch = DryRunScratch.Adopt(dryRunCache, "Condition review Proposal Dry Run"))
        {
            dryRun = ProposalDryRunner.Run(scratch, proposal);
            afterPath = scratch.PeekCache().ProjectId.Path;
            new FwDataProjectLoader().Save(scratch.PeekCache());
        }

        var after = await AssessAsync(afterPath, invoker, words, Path.Combine(_root, draftName + "-after"),
            frozen.Baseline);
        var verification = RecipeVerification.Compare(frozen, before, after, RecipeVerificationCriteria.Tightening);
        var authoredEnvironment = proposal.Operations.Select(operation => operation.EntityId?.Value)
            .FirstOrDefault(id => id is not null);
        return new Acceptance(proposal, dryRun, verification, frozen, before, after, afterPath,
            authoredEnvironment);
    }

    private static CommandOutcome<ComposedOperationsResponse> ComposeCondition(string path, string draft,
        string target, IReadOnlyList<string> expected, IReadOnlyList<string> environments) =>
        ProposalCommands.ComposeEditAllomorphCondition(new ComposeEditAllomorphConditionRequest(path,
            ProductVersion, draft, JsonSerializer.Serialize(new
            {
                target,
                field = "phoneEnv",
                expectedEnvironments = expected,
                environments,
            })));

    private static CommandOutcome<ComposedOperationsResponse> ComposeOrder(string path, string draft,
        string target, IReadOnlyList<string> expected, IReadOnlyList<string> alternates) =>
        ProposalCommands.ComposeOrderAllomorphs(new ComposeOrderAllomorphsRequest(path, ProductVersion, draft,
            JsonSerializer.Serialize(new { target, expectedAlternates = expected, alternates })));

    private static CommandOutcome<ComposedOperationsResponse> ComposeNarrowEnvironment(string path, string draft,
        BroadEnvironmentFixture fixture)
    {
        var authored = ProposalCommands.ComposeAuthorEnvironment(new ComposeAuthorEnvironmentRequest(path,
            ProductVersion, draft, JsonSerializer.Serialize(new
            {
                name = "Labial left context",
                left = new[] { new { naturalClass = fixture.LabialClass } },
                right = Array.Empty<object>(),
            })));
        if (!authored.Succeeded) return authored;
        var environmentId = Assert.Single(authored.Value!.Operations, operation => operation.EntityId is not null)
            .EntityId!;
        return ComposeCondition(path, draft, fixture.TargetAllomorph, [fixture.BroadEnvironment], [environmentId]);
    }

    private async Task<Acceptance> RunNoChangeAsync(LcmCache cache, IReadOnlyList<Guid> wordforms,
        IReadOnlyList<string> words, string name, IReadOnlyCollection<string> selection,
        IReadOnlyCollection<string> heldOut)
    {
        Directory.CreateDirectory(_root);
        new FwDataProjectLoader().Save(cache);
        var frozen = Capture(cache, wordforms, selection.ToHashSet(StringComparer.Ordinal),
            heldOut.ToHashSet(StringComparer.Ordinal));
        using var invoker = new PanGlossInvoker(PanGlossExecutable.TryLocate());
        var before = await AssessAsync(cache.ProjectId.Path, invoker, words, Path.Combine(_root, name + "-before"),
            frozen.Baseline);
        var after = await AssessAsync(cache.ProjectId.Path, invoker, words, Path.Combine(_root, name + "-after"),
            frozen.Baseline);
        var verification = RecipeVerification.Compare(frozen, before, after, RecipeVerificationCriteria.Tightening);
        return new Acceptance(null, null, verification, frozen, before, after, cache.ProjectId.Path, null);
    }

    private static FrozenExpectationSet Capture(LcmCache cache, IReadOnlyList<Guid> wordforms,
        IReadOnlySet<string> selection, IReadOnlySet<string> heldOut)
    {
        var frozen = FrozenExpectationCapture.Capture(cache, Token(cache), wordforms, selection, heldOut);
        Assert.Empty(frozen.Unavailable);
        Assert.Contains(frozen.Cases.SelectMany(item => item.Readings), item => item.Opinion == "approved");
        return frozen;
    }

    private static async Task<RecipeVerificationRun> AssessAsync(string projectPath, PanGlossInvoker invoker,
        IReadOnlyList<string> words, string artifactRoot, BaselineToken baseline)
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
                AssessmentKind.ParseTime.ToStoredKind(), "pangloss", "0.7.0", ToAssessedWords(batch.Words));
            return new RecipeVerificationRun(comparable, baseline, options, [], words);
        }
        finally
        {
            assessment.ArtifactLease?.Dispose();
        }
    }

    private static IReadOnlyList<AssessedWord> ToAssessedWords(IReadOnlyList<WordAnalysis> words) =>
        words.Select(word => new AssessedWord(word.Word, word.Outcome.ToStoredOutcome(), [], word.ElapsedMs,
            word.Signature) { Morphology = word.Morphology }).ToArray();

    private UnconditionedAllomorphFixture MakeUnconditionedAllomorph(LcmCache cache, bool broadFirst, bool includeSeparateFallback)
    {
        RealParserProject.PrepareForParsing(cache, "a", "k", "p", "t");
        var p = Phoneme(cache, "p");
        var t = Phoneme(cache, "t");
        var afterP = CreateEnvironment(cache, "After p", [new(Phoneme: p)], []);
        var afterT = CreateEnvironment(cache, "After t", [new(Phoneme: t)], []);
        ILexEntry entry = null!;
        ILexEntry prefixP = null!;
        ILexEntry prefixT = null!;
        ILexEntry prefixA = null!;
        IMoStemAllomorph broad = null!;
        IMoStemAllomorph conditioned = null!;
        IMoStemAllomorph separateFallback = null!;
        Guid pkaWordform = Guid.Empty;
        Guid tkaWordform = Guid.Empty;
        Guid akaWordform = Guid.Empty;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var services = cache.ServiceLocator;
            var morphTypes = services.GetInstance<IMoMorphTypeRepository>();
            var entries = services.GetInstance<ILexEntryFactory>();
            entry = entries.Create(morphTypes.GetObject(MoMorphTypeTags.kguidMorphStem),
                TsStringUtils.MakeString("ka", cache.DefaultVernWs), "root",
                new SandboxGenericMSA { MsaType = MsaType.kStem });
            prefixP = MakeEntry(cache, MoMorphTypeTags.kguidMorphPrefix, "p", "p-prefix");
            prefixT = MakeEntry(cache, MoMorphTypeTags.kguidMorphPrefix, "t", "t-prefix");
            prefixA = MakeEntry(cache, MoMorphTypeTags.kguidMorphPrefix, "a", "a-prefix");
            if (broadFirst)
            {
                broad = CreateAlternate(cache, entry, "ka");
                conditioned = CreateAlternate(cache, entry, "ka");
            }
            else
            {
                conditioned = CreateAlternate(cache, entry, "ka");
                broad = CreateAlternate(cache, entry, "ka");
            }
            if (includeSeparateFallback) separateFallback = CreateAlternate(cache, entry, "ka");
            var msa = entry.MorphoSyntaxAnalysesOC.Single();
            pkaWordform = AddReading(cache, "pka", "approves",
                new(prefixP.LexemeFormOA!, prefixP.MorphoSyntaxAnalysesOC.Single()), new(conditioned, msa)).Wordform;
            AddReadingToWordform(cache, "pka", "disapproves", pkaWordform,
                new(prefixP.LexemeFormOA!, prefixP.MorphoSyntaxAnalysesOC.Single()), new(broad, msa));
            tkaWordform = AddReading(cache, "tka", "approves",
                new(prefixT.LexemeFormOA!, prefixT.MorphoSyntaxAnalysesOC.Single()), new(broad, msa)).Wordform;
            var elsewhere = includeSeparateFallback ? separateFallback : broad;
            akaWordform = AddReading(cache, "aka", "approves",
                new(prefixA.LexemeFormOA!, prefixA.MorphoSyntaxAnalysesOC.Single()), new(elsewhere, msa)).Wordform;
        });
        var entryId = Id(entry);
        var broadId = Id(broad);
        var conditionedId = Id(conditioned);
        var fallbackId = Id(includeSeparateFallback ? separateFallback : broad);
        var conditionOperations = EditAllomorphConditionComposer.Build(cache,
            new EditAllomorphConditionIntent(CanonicalId.Parse(conditionedId), AllomorphConditionField.PhoneEnv,
                [], [afterP]));
        SoundSystemComposerTests.Execute(cache, conditionOperations);
        new FwDataProjectLoader().Save(cache);
        return new UnconditionedAllomorphFixture(entryId, broadId, conditionedId, fallbackId, afterT.Value, Id(prefixP.LexemeFormOA!),
            Id(prefixT.LexemeFormOA!), Id(prefixA.LexemeFormOA!), pkaWordform, tkaWordform, akaWordform,
            [pkaWordform, tkaWordform, akaWordform], ["aka", "pka", "tka"]);
    }

    private BroadEnvironmentFixture MakeBroadEnvironment(LcmCache cache, bool includeWrongContextNegative)
    {
        RealParserProject.PrepareForParsing(cache, "a", "b", "k", "m", "p", "t", "u");
        var p = Phoneme(cache, "p");
        var b = Phoneme(cache, "b");
        var t = Phoneme(cache, "t");
        var wideClass = ComposeSound(cache, () => AuthorNaturalClassComposer.Build(cache,
            new("Consonants", "C", [p, b, t])));
        var labialClass = ComposeSound(cache, () => AuthorNaturalClassComposer.Build(cache,
            new("Labials", "Lab", [p, b])));
        var broadEnvironment = CreateEnvironment(cache, "Consonant left context",
            [new(NaturalClass: wideClass)], []);
        ILexEntry entry = null!;
        ILexEntry sharedEntry = null!;
        ILexEntry prefixP = null!;
        ILexEntry prefixB = null!;
        ILexEntry prefixT = null!;
        IMoStemAllomorph target = null!;
        IMoStemAllomorph shared = null!;
        Guid pkaWordform = Guid.Empty;
        Guid bkaWordform = Guid.Empty;
        Guid tkaWordform = Guid.Empty;
        Guid otherWordform = Guid.Empty;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var broad = Resolve<IPhEnvironment>(cache, broadEnvironment);
            entry = MakeStem(cache, "ka", "target");
            sharedEntry = MakeStem(cache, "ku", "shared");
            prefixP = MakeEntry(cache, MoMorphTypeTags.kguidMorphPrefix, "p", "p-prefix");
            prefixB = MakeEntry(cache, MoMorphTypeTags.kguidMorphPrefix, "b", "b-prefix");
            prefixT = MakeEntry(cache, MoMorphTypeTags.kguidMorphPrefix, "t", "t-prefix");
            target = (IMoStemAllomorph)entry.LexemeFormOA!;
            shared = (IMoStemAllomorph)sharedEntry.LexemeFormOA!;
            target.PhoneEnvRC.Add(broad);
            shared.PhoneEnvRC.Add(broad);
            pkaWordform = AddReading(cache, "pka", "approves",
                new(prefixP.LexemeFormOA!, prefixP.MorphoSyntaxAnalysesOC.Single()), new(target, entry.MorphoSyntaxAnalysesOC.Single())).Wordform;
            bkaWordform = AddReading(cache, "bka", "approves",
                new(prefixB.LexemeFormOA!, prefixB.MorphoSyntaxAnalysesOC.Single()), new(target, entry.MorphoSyntaxAnalysesOC.Single())).Wordform;
            if (includeWrongContextNegative)
            {
                tkaWordform = AddReading(cache, "tka", "disapproves",
                    new(prefixT.LexemeFormOA!, prefixT.MorphoSyntaxAnalysesOC.Single()),
                    new(target, entry.MorphoSyntaxAnalysesOC.Single())).Wordform;
            }
            else
            {
                tkaWordform = AddReading(cache, "tka", "approves",
                    new(prefixT.LexemeFormOA!, prefixT.MorphoSyntaxAnalysesOC.Single()),
                    new(target, entry.MorphoSyntaxAnalysesOC.Single())).Wordform;
            }
            otherWordform = AddReading(cache, "tku", "approves",
                new(prefixT.LexemeFormOA!, prefixT.MorphoSyntaxAnalysesOC.Single()),
                new(shared, sharedEntry.MorphoSyntaxAnalysesOC.Single())).Wordform;
        });
        new FwDataProjectLoader().Save(cache);
        return new BroadEnvironmentFixture(Id(entry), Id(target), Id(sharedEntry), Id(shared), broadEnvironment.Value,
            wideClass.Value, labialClass.Value, Id(prefixP.LexemeFormOA!), Id(prefixB.LexemeFormOA!), Id(prefixT.LexemeFormOA!),
            pkaWordform, bkaWordform, tkaWordform, otherWordform,
            [pkaWordform, bkaWordform, tkaWordform, otherWordform], ["bka", "pka", "tka", "tku"]);
    }

    private BroadEnvironmentDispositionFixture MakeBroadEnvironmentDispositionFixture(LcmCache cache)
    {
        RealParserProject.PrepareForParsing(cache, "a", "b", "m", "p", "t");
        var p = Phoneme(cache, "p");
        var b = Phoneme(cache, "b");
        var t = Phoneme(cache, "t");
        var consonants = ComposeSound(cache, () => AuthorNaturalClassComposer.Build(cache,
            new("Consonants", "C", [p, b, t])));
        var broadEnvironment = CreateEnvironment(cache, "Consonant left context",
            [new(NaturalClass: consonants)], []);
        ILexEntry suffixEntry = null!;
        IMoAffixAllomorph target = null!;
        Guid firstWordform = Guid.Empty;
        Guid secondWordform = Guid.Empty;
        Guid thirdWordform = Guid.Empty;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            suffixEntry = MakeEntry(cache, MoMorphTypeTags.kguidMorphSuffix, "a", "conditioned suffix");
            target = (IMoAffixAllomorph)suffixEntry.LexemeFormOA!;
            target.PhoneEnvRC.Add(Resolve<IPhEnvironment>(cache, broadEnvironment.Value));
            var suffix = new Morph(target, suffixEntry.MorphoSyntaxAnalysesOC.Single());
            var first = MakeStem(cache, "ap", "p-final stem");
            var second = MakeStem(cache, "ab", "b-final stem");
            var third = MakeStem(cache, "mp", "second p-final stem");
            firstWordform = AddReading(cache, "apa", "approves",
                new(first.LexemeFormOA!, first.MorphoSyntaxAnalysesOC.Single()), suffix).Wordform;
            secondWordform = AddReading(cache, "aba", "approves",
                new(second.LexemeFormOA!, second.MorphoSyntaxAnalysesOC.Single()), suffix).Wordform;
            thirdWordform = AddReading(cache, "mpa", "approves",
                new(third.LexemeFormOA!, third.MorphoSyntaxAnalysesOC.Single()), suffix).Wordform;
            _ = MakeStem(cache, "at", "held-out t-final stem");
        });
        new FwDataProjectLoader().Save(cache);
        return new BroadEnvironmentDispositionFixture(Id(target), [firstWordform, secondWordform, thirdWordform], ["ata"]);
    }

    private static CanonicalId CreateEnvironment(LcmCache cache, string name,
        IReadOnlyList<EnvironmentContext> left, IReadOnlyList<EnvironmentContext> right) =>
        ComposeSound(cache, () => AuthorEnvironmentComposer.Build(cache, new(name, left, right)));

    private static CanonicalId ComposeSound(LcmCache cache, Func<IReadOnlyList<OperationEnvelope>> build) =>
        SoundSystemComposerTests.Compose(cache, [], build);

    private static CanonicalId Phoneme(LcmCache cache, string name)
    {
        var phoneme = cache.LangProject.PhonologicalDataOA.PhonemeSetsOS.SelectMany(set => set.PhonemesOC)
            .Single(item => item.Name.get_String(cache.DefaultVernWs)?.Text == name);
        foreach (var code in phoneme.CodesOS)
        {
            var representation = code.Representation.get_String(cache.DefaultVernWs)?.Text;
            try
            {
                SoundSystemAuthoring.RequireLiteral(representation ?? string.Empty);
            }
            catch (InvalidOperationException exception)
            {
                throw new InvalidOperationException(
                    $"Phoneme {name} has invalid representation {JsonSerializer.Serialize(representation)}.",
                    exception);
            }
        }
        return CanonicalId.FromGuid(phoneme.Guid);
    }

    private static ILexEntry MakeEntry(LcmCache cache, Guid morphType, string form, string gloss) =>
        cache.ServiceLocator.GetInstance<ILexEntryFactory>().Create(
            cache.ServiceLocator.GetInstance<IMoMorphTypeRepository>().GetObject(morphType),
            TsStringUtils.MakeString(form, cache.DefaultVernWs), gloss,
            new SandboxGenericMSA { MsaType = morphType == MoMorphTypeTags.kguidMorphStem
                ? MsaType.kStem : MsaType.kUnclassified });

    private static ILexEntry MakeStem(LcmCache cache, string form, string gloss) => MakeEntry(cache,
        MoMorphTypeTags.kguidMorphStem, form, gloss);

    private static IMoStemAllomorph CreateAlternate(LcmCache cache, ILexEntry entry, string form)
    {
        var alternate = cache.ServiceLocator.GetInstance<IMoStemAllomorphFactory>().Create();
        entry.AlternateFormsOS.Add(alternate);
        alternate.MorphTypeRA = entry.LexemeFormOA!.MorphTypeRA;
        alternate.Form.set_String(cache.DefaultVernWs, form);
        return alternate;
    }

    private static (Guid Wordform, Guid Analysis) AddReading(LcmCache cache, string surface, string opinion,
        params Morph[] morphs) => AddReadingCore(cache, surface, opinion, null, morphs);

    private static (Guid Wordform, Guid Analysis) AddReadingToWordform(LcmCache cache, string surface,
        string opinion, Guid existingWordform, params Morph[] morphs) =>
        AddReadingCore(cache, surface, opinion, existingWordform, morphs);

    private static (Guid Wordform, Guid Analysis) AddReadingCore(LcmCache cache, string surface, string opinion,
        Guid? existingWordform, IReadOnlyList<Morph> morphs)
    {
        var services = cache.ServiceLocator;
        var wordform = existingWordform is { } existing
            ? services.GetInstance<IWfiWordformRepository>().GetObject(existing)
            : services.GetInstance<IWfiWordformFactory>().Create(TsStringUtils.MakeString(surface, cache.DefaultVernWs));
        var analysis = services.GetInstance<IWfiAnalysisFactory>().Create();
        wordform.AnalysesOC.Add(analysis);
        foreach (var morph in morphs)
        {
            var bundle = services.GetInstance<IWfiMorphBundleFactory>().Create();
            analysis.MorphBundlesOS.Add(bundle);
            bundle.MorphRA = morph.Form;
            bundle.MsaRA = morph.Msa;
        }
        cache.LangProject.DefaultUserAgent.SetEvaluation(analysis,
            opinion == "approves" ? Opinions.approves : Opinions.disapproves);
        return (wordform.Guid, analysis.Guid);
    }

    private void RecordKeep(LcmCache cache, string measureId, string objectId, string reason)
    {
        var field = NotebookJudgmentFixture.InitializeReservedField(cache);
        var project = CanonicalId.FromGuid(cache.LangProject.Guid).Value;
        var id = CanonicalId.FromGuid(Guid.NewGuid()).Value;
        var judgment = new HumanJudgment(project, id, id, [],
            new DispositionJudgment(
                new ObjectJudgmentSubject(new JudgmentObject("MoStemAllomorph", objectId)),
                measureId, ParsimonyDispositionKind.Keep, "sha256:" + new string('a', 64),
                measureId + "/v1", "The reviewed allomorph", measureId),
            reason, new JudgmentActor(JudgmentActorKind.Human));
        NotebookJudgmentFixture.AddRecord(cache, CanonicalId.Parse(id).ToGuid(), field,
            TsStringUtils.MakeString(HumanJudgmentCodec.Format(judgment), cache.DefaultAnalWs));
    }

    private static string Case(LcmCache cache, Guid wordform, string surface) => FrozenExpectationCapture.CaseKey(
        CanonicalId.FromGuid(wordform).Value,
        cache.WritingSystemFactory.GetStrFromWs(cache.DefaultVernWs), surface);

    private static BaselineToken Token(LcmCache cache) => new(cache.LangProject.Guid.ToString("D"),
        BaselineSemanticDigest.Compute(cache), BaselineSemanticDigest.ProjectionVersion,
        DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"), BatchInvocationEvidence.DigestFile(cache.ProjectId.Path));

    private static string Id(ICmObject item) => CanonicalId.FromGuid(item.Guid).Value;

    private static T Resolve<T>(LcmCache cache, string id) where T : class =>
        Assert.IsAssignableFrom<T>(cache.ServiceLocator.GetInstance<ICmObjectRepository>()
            .GetObject(CanonicalId.Parse(id).ToGuid()));

    private static T Resolve<T>(LcmCache cache, CanonicalId id) where T : class =>
        Resolve<T>(cache, id.Value);

    private static void AssertAllFrozenApprovedReadingsProduced(Acceptance result) =>
        AssertAllFrozenApprovedReadings(result.Frozen, result.Verification);

    private static void AssertAllFrozenApprovedReadings(FrozenExpectationSet frozen,
        RecipeVerificationResult verification)
    {
        var approved = frozen.Cases.SelectMany(item => item.Readings.Where(reading => reading.Opinion == "approved"))
            .ToArray();
        Assert.NotEmpty(approved);
        foreach (var reading in approved)
        {
            var result = Assert.Single(verification.Readings, item => item.ReadingId == reading.ReadingId);
            Assert.True(result.AfterComplete);
            Assert.True(result.AfterProduced);
        }
        Assert.All(frozen.Cases.Where(item => item.HeldOut)
            .SelectMany(item => item.Readings.Where(reading => reading.Opinion == "approved")), reading =>
        {
            var result = Assert.Single(verification.Readings, item => item.ReadingId == reading.ReadingId);
            Assert.True(result.AfterProduced);
        });
    }

    private static void AssertReviewedNegativesRejected(RecipeVerificationResult verification)
    {
        Assert.NotEmpty(verification.Negatives);
        Assert.All(verification.Negatives, negative =>
        {
            Assert.True(negative.BeforeComplete);
            Assert.True(negative.AfterComplete);
            Assert.False(negative.AfterAccepted);
            Assert.False(negative.NewlyAccepted);
        });
    }

    private static void AssertRecipePassed(Acceptance result)
    {
        var evidence = new
        {
            result.Verification,
            Before = result.Before.Assessment.Words.Select(word => new
            {
                word.Word, word.Outcome, word.IsIncomplete, word.Morphology,
            }),
            After = result.After.Assessment.Words.Select(word => new
            {
                word.Word, word.Outcome, word.IsIncomplete, word.Morphology,
            }),
        };
        Assert.True(result.Verification.Status == RecipeVerificationStatus.Pass,
            JsonSerializer.Serialize(evidence));
    }

    private static void AssertParserContains(RecipeVerificationRun run, string surface, params string[] forms)
    {
        var word = Assert.Single(run.Assessment.Words, item => item.Word == surface);
        var parses = word.Morphology?.Analyses ?? [];
        var expected = forms.Select(form => CanonicalId.Parse(form).ToGuid().ToString("D")).ToArray();
        Assert.True(parses.Any(parse => parse.Morphs.Select(morph => morph.Form)
            .SequenceEqual(expected, StringComparer.Ordinal)),
            $"Expected {surface} to contain [{string.Join(", ", expected)}]; actual parses: " +
            JsonSerializer.Serialize(parses));
    }

    private static void AssertParserDoesNotContain(RecipeVerificationRun run, string surface,
        params string[] forms)
    {
        var word = Assert.Single(run.Assessment.Words, item => item.Word == surface);
        var parses = word.Morphology?.Analyses ?? [];
        var expected = forms.Select(form => CanonicalId.Parse(form).ToGuid().ToString("D")).ToArray();
        Assert.DoesNotContain(parses, parse => parse.Morphs.Select(morph => morph.Form)
            .SequenceEqual(expected, StringComparer.Ordinal));
    }

    private sealed record Morph(IMoForm Form, IMoMorphSynAnalysis Msa);

    private sealed record UnconditionedAllomorphFixture(string Entry, string Broad, string Conditioned, string SeparateFallback,
        string AfterT, string PrefixP, string PrefixT, string PrefixA,
        Guid PkaWordform, Guid TkaWordform, Guid AkaWordform,
        IReadOnlyList<Guid> Wordforms, IReadOnlyList<string> Words);

    private sealed record BroadEnvironmentFixture(string Entry, string TargetAllomorph, string SharedEntry,
        string SharedAllomorph, string BroadEnvironment, string WideClass, string LabialClass,
        string PrefixP, string PrefixB, string PrefixT,
        Guid PkaWordform, Guid BkaWordform, Guid TkaWordform, Guid OtherWordform,
        IReadOnlyList<Guid> Wordforms, IReadOnlyList<string> Words);

    private sealed record BroadEnvironmentDispositionFixture(string TargetAllomorph, IReadOnlyList<Guid> Wordforms,
        IReadOnlyList<string> Words);

    private sealed record Acceptance(Proposal? Proposal, DryRun? DryRun,
        RecipeVerificationResult Verification, FrozenExpectationSet Frozen,
        RecipeVerificationRun Before, RecipeVerificationRun After, string AfterProjectPath,
        string? AuthoredEnvironmentId);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
