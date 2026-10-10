using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.Core.Text;
using SIL.LCModel.DomainServices;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Contract.Retirement;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Projection.Retirement;
using SIL.Motif.Host.Assess;
using SIL.Motif.Runner.Apply;
using SIL.Motif.Runner.DryRun;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Parser;
using SIL.Motif.Tests.Parser;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Assess;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Retirement;

[Collection(LcmCacheParallelCollections.Group1)]
public sealed class RetireRedundantZeroAffixComposerTests(PristineProjectFixture pristine)
{
    [Fact]
    public void RetiresWholeUnreferencedZeroAffixGraphAndPreservesItsOptionalSlot()
    {
        var project = CreateZeroAffixProject(pristine, "^0", optional: true);
        using var cache = project.Cache;
        var fixture = project.Fixture;
        var ownedIds = OwnedIds(cache, fixture.Entry);
        var document = new RetireRedundantZeroAffixIntentDocument(
            new RetireRedundantZeroAffixIntent(CanonicalId.FromGuid(fixture.Entry.Guid).Value));

        var operations = RetireRedundantZeroAffixComposer.Build(cache, document,
            () => CanonicalId.FromGuid(Guid.NewGuid()));

        var slotRemoval = Assert.Single(operations, item => item.Kind == MoInflAffMsaSlotsOperationKinds.RemoveRefSlots);
        var deletion = Assert.Single(operations, item => item.Kind == RetireRedundantZeroAffixOperationKinds.DeleteGraph);
        Assert.Equal(CanonicalId.FromGuid(fixture.Msa.Guid), slotRemoval.Target);
        Assert.Equal(CanonicalId.FromGuid(fixture.Entry.Guid), deletion.Target);
        Assert.Contains(deletion.DependsOn, item => item.OperationId == slotRemoval.OperationId);

        var proposal = new Proposal(new Dictionary<string, string>
        {
            ["analysis"] = "1.0", ["lexical"] = "1.0", ["phonology"] = "1.0",
        }, CanonicalId.Mint(), null, operations.Reverse().ToArray());
        var dryRun = DryRunPatchedProject(cache, proposal);
        Assert.Equal(2, dryRun.ExpectedEffects.Count);
        var receipt = ProposalApplier.Apply(cache, proposal, dryRun.Anchor, "zero-affix-retirement-tests");

        Assert.Equal(2, receipt.ActualEffects.Count);
        Assert.True(fixture.Slot.Optional);
        var repository = cache.ServiceLocator.GetInstance<ICmObjectRepository>();
        Assert.False(repository.TryGetObject(fixture.Msa.Guid, out _));
        Assert.All(ownedIds, id => Assert.False(repository.TryGetObject(id, out _)));
        Assert.True(repository.TryGetObject(pristine.Seed.SecondEntryId, out _));
    }

    [Theory]
    [InlineData("meaningful", true)]
    [InlineData("^0", false)]
    public void RefusesMeaningfulRealizationOrRequiredSlot(string form, bool optional)
    {
        var project = CreateZeroAffixProject(pristine, form, optional);
        using var cache = project.Cache;
        var fixture = project.Fixture;
        var document = new RetireRedundantZeroAffixIntentDocument(
            new RetireRedundantZeroAffixIntent(CanonicalId.FromGuid(fixture.Entry.Guid).Value));

        var error = Assert.Throws<InvalidOperationException>(() => RetireRedundantZeroAffixComposer.Build(
            cache, document, () => CanonicalId.FromGuid(Guid.NewGuid())));

        Assert.NotEmpty(error.Message);
        Assert.True(cache.ServiceLocator.GetInstance<ICmObjectRepository>().TryGetObject(fixture.Entry.Guid, out _));
    }

    [Fact]
    public void RefusesExternalApprovedBundleReferenceToTheEntryGraph()
    {
        var project = CreateZeroAffixProject(pristine, "^0", optional: true);
        using var cache = project.Cache;
        var fixture = project.Fixture;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var wordform = cache.ServiceLocator.GetInstance<IWfiWordformFactory>().Create(
                TsStringUtils.MakeString("approved use", cache.DefaultVernWs));
            var analysis = cache.ServiceLocator.GetInstance<IWfiAnalysisFactory>().Create();
            wordform.AnalysesOC.Add(analysis);
            var bundle = cache.ServiceLocator.GetInstance<IWfiMorphBundleFactory>().Create();
            analysis.MorphBundlesOS.Add(bundle);
            bundle.MorphRA = fixture.Entry.LexemeFormOA;
            bundle.MsaRA = fixture.Msa;
            cache.LangProject.DefaultUserAgent.SetEvaluation(analysis, Opinions.approves);
        });
        var document = Intent(fixture.Entry);

        var error = Assert.Throws<InvalidOperationException>(() => RetireRedundantZeroAffixComposer.Build(
            cache, document, () => CanonicalId.FromGuid(Guid.NewGuid())));

        Assert.Contains("incoming", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RefusesAZeroAffixEntryWithAMeaningfulSenseGloss()
    {
        var project = CreateZeroAffixProject(pristine, "^0", optional: true, senseGloss: "meaning");
        using var cache = project.Cache;
        var document = Intent(project.Fixture.Entry);

        var error = Assert.Throws<InvalidOperationException>(() => RetireRedundantZeroAffixComposer.Build(
            cache, document, () => CanonicalId.FromGuid(Guid.NewGuid())));

        Assert.Contains("sense", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RefusesAnAffixWithAFeatureContribution()
    {
        var project = CreateZeroAffixProject(pristine, "^0", optional: true);
        using var cache = project.Cache;
        var services = cache.ServiceLocator;
        var feature = services.GetInstance<IFsClosedFeatureFactory>().Create();
        var value = services.GetInstance<IFsSymFeatValFactory>().Create();
        var structure = services.GetInstance<IFsFeatStrucFactory>().Create();
        var specification = services.GetInstance<IFsClosedValueFactory>().Create();
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            cache.LangProject.MsFeatureSystemOA.FeaturesOC.Add(feature);
            feature.ValuesOC.Add(value);
            project.Fixture.Msa.InflFeatsOA = structure;
            structure.FeatureSpecsOC.Add(specification);
            specification.FeatureRA = feature;
            specification.ValueRA = value;
        });

        var error = Assert.Throws<InvalidOperationException>(() => RetireRedundantZeroAffixComposer.Build(
            cache, Intent(project.Fixture.Entry), () => CanonicalId.FromGuid(Guid.NewGuid())));

        Assert.Contains("features", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RefusesAnAffixWithAnInflectionClassContribution()
    {
        var project = CreateZeroAffixProject(pristine, "^0", optional: true);
        using var cache = project.Cache;
        var category = cache.ServiceLocator.GetInstance<IPartOfSpeechRepository>()
            .GetObject(pristine.Seed.PartOfSpeechId);
        var inflectionClass = cache.ServiceLocator.GetInstance<IMoInflClassFactory>().Create();
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            category.InflectionClassesOC.Add(inflectionClass);
            ((IMoAffixAllomorph)project.Fixture.Entry.LexemeFormOA!).InflectionClassesRC.Add(inflectionClass);
        });

        var error = Assert.Throws<InvalidOperationException>(() => RetireRedundantZeroAffixComposer.Build(
            cache, Intent(project.Fixture.Entry), () => CanonicalId.FromGuid(Guid.NewGuid())));

        Assert.Contains("classes", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RefusesAnAffixWithAnExceptionFeatureContribution()
    {
        var project = CreateZeroAffixProject(pristine, "^0", optional: true);
        using var cache = project.Cache;
        var restriction = cache.ServiceLocator.GetInstance<ICmPossibilityFactory>().Create();
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            cache.LangProject.MorphologicalDataOA.ProdRestrictOA.PossibilitiesOS.Add(restriction);
            project.Fixture.Msa.FromProdRestrictRC.Add(restriction);
        });

        var error = Assert.Throws<InvalidOperationException>(() => RetireRedundantZeroAffixComposer.Build(
            cache, Intent(project.Fixture.Entry), () => CanonicalId.FromGuid(Guid.NewGuid())));

        Assert.Contains("exception", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RetiresAZeroAffixFromATemplateAndPreservesItsOptionalSlot()
    {
        var project = CreateZeroAffixProject(pristine, "^0", optional: true);
        using var cache = project.Cache;
        var category = cache.ServiceLocator.GetInstance<IPartOfSpeechRepository>()
            .GetObject(pristine.Seed.PartOfSpeechId);
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var template = cache.ServiceLocator.GetInstance<IMoInflAffixTemplateFactory>().Create();
            category.AffixTemplatesOS.Add(template);
            template.Final = true;
            template.SuffixSlotsRS.Add(project.Fixture.Slot);
        });

        var ownedIds = OwnedIds(cache, project.Fixture.Entry);
        var proposal = Proposal(cache, project.Fixture.Entry);
        var dryRun = DryRunPatchedProject(cache, proposal);
        var receipt = ProposalApplier.Apply(cache, proposal, dryRun.Anchor,
            "zero-affix-retirement-tests");

        Assert.Equal(2, receipt.ActualEffects.Count);
        Assert.True(project.Fixture.Slot.Optional);
        Assert.Contains(category.AffixTemplatesOS.SelectMany(item => item.SuffixSlotsRS),
            item => item.Guid == project.Fixture.Slot.Guid);
        var repository = cache.ServiceLocator.GetInstance<ICmObjectRepository>();
        Assert.All(ownedIds, id => Assert.False(repository.TryGetObject(id, out _)));
    }

    [Fact]
    public void RefusesAnAdHocProhibitionThatReferencesTheEntryGraph()
    {
        var project = CreateZeroAffixProject(pristine, "^0", optional: true);
        using var cache = project.Cache;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var prohibition = cache.ServiceLocator.GetInstance<IMoAlloAdhocProhibFactory>().Create();
            cache.LangProject.MorphologicalDataOA.AdhocCoProhibitionsOC.Add(prohibition);
            prohibition.FirstAllomorphRA = project.Fixture.Entry.LexemeFormOA;
        });

        var error = Assert.Throws<InvalidOperationException>(() => RetireRedundantZeroAffixComposer.Build(
            cache, Intent(project.Fixture.Entry), () => CanonicalId.FromGuid(Guid.NewGuid())));

        Assert.Contains("MoAlloAdhocProhib", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ApplyPreflightRejectsAChangedGraphWithoutDeletingIt()
    {
        var project = CreateZeroAffixProject(pristine, "^0", optional: true);
        using var cache = project.Cache;
        var fixture = project.Fixture;
        var proposal = Proposal(cache, fixture.Entry);
        var dryRun = DryRunPatchedProject(cache, proposal);
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor,
            () => fixture.Entry.LexemeFormOA!.Form.set_String(cache.DefaultVernWs, "meaningful"));

        Assert.ThrowsAny<Exception>(() => ProposalApplier.Apply(cache, proposal, dryRun.Anchor,
            "zero-affix-retirement-tests"));

        Assert.Equal("meaningful", fixture.Entry.LexemeFormOA!.Form.get_String(cache.DefaultVernWs)!.Text);
        Assert.True(cache.ServiceLocator.GetInstance<ICmObjectRepository>().TryGetObject(fixture.Entry.Guid, out _));
        Assert.Single(fixture.Msa.SlotsRC);
    }

    [Fact]
    public void FailureAfterOptionalSlotRemovalRollsBackTheWholeGraphDelete()
    {
        var project = CreateZeroAffixProject(pristine, "^0", optional: true);
        using var cache = project.Cache;
        var fixture = project.Fixture;
        var proposal = Proposal(cache, fixture.Entry);
        var dryRun = DryRunPatchedProject(cache, proposal);
        var removal = Assert.Single(proposal.Operations,
            item => item.Kind == MoInflAffMsaSlotsOperationKinds.RemoveRefSlots);

        var error = Assert.Throws<InvalidOperationException>(() => ProposalApplier.Apply(cache, proposal,
            dryRun.Anchor, "zero-affix-retirement-tests", "Inject failure after slot removal.", afterOperation: (_, operation) =>
            {
                if (operation.OperationId == removal.OperationId)
                    throw new InvalidOperationException("injected zero-affix rollback");
            }));

        Assert.Equal("injected zero-affix rollback", error.Message);
        Assert.True(cache.ServiceLocator.GetInstance<ICmObjectRepository>().TryGetObject(fixture.Entry.Guid, out _));
        Assert.Equal(fixture.Slot.Guid, Assert.Single(fixture.Msa.SlotsRC).Guid);
    }

    [Fact]
    public void RefusesAStemEntryAndDoesNotInferAnAffixIdentity()
    {
        using var cache = pristine.NewScratch();
        var document = new RetireRedundantZeroAffixIntentDocument(
            new RetireRedundantZeroAffixIntent(CanonicalId.FromGuid(pristine.Seed.FirstEntryId).Value));

        var error = Assert.Throws<InvalidOperationException>(() => RetireRedundantZeroAffixComposer.Build(
            cache, document, () => CanonicalId.FromGuid(Guid.NewGuid())));

        Assert.Contains("prefix or suffix", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RefusesAnEmptyFormThatThePinnedParserDoesNotLoadAsZero()
    {
        var project = CreateZeroAffixProject(pristine, "", optional: true);
        using var cache = project.Cache;

        var error = Assert.Throws<InvalidOperationException>(() => RetireRedundantZeroAffixComposer.Build(
            cache, Intent(project.Fixture.Entry), () => CanonicalId.FromGuid(Guid.NewGuid())));

        Assert.Contains("loads as zero", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RefusesAnEntryWithAnAlternateFormOutsideTheRetirementScope()
    {
        var project = CreateZeroAffixProject(pristine, "^0", optional: true);
        using var cache = project.Cache;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var alternate = cache.ServiceLocator.GetInstance<IMoAffixAllomorphFactory>().Create();
            project.Fixture.Entry.AlternateFormsOS.Add(alternate);
        });

        var error = Assert.Throws<InvalidOperationException>(() => RetireRedundantZeroAffixComposer.Build(
            cache, Intent(project.Fixture.Entry), () => CanonicalId.FromGuid(Guid.NewGuid())));

        Assert.Contains("no alternates", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static AffixFixture CreateAffix(LcmCache cache, SeededProject seed, string form, bool optional,
        string senseGloss = "")
    {
        var services = cache.ServiceLocator;
        var category = services.GetInstance<IPartOfSpeechRepository>().GetObject(seed.PartOfSpeechId);
        var suffixType = services.GetInstance<IMoMorphTypeRepository>().GetObject(MoMorphTypeTags.kguidMorphSuffix);
        ILexEntry entry = null!;
        IMoInflAffMsa msa = null!;
        IMoInflAffixSlot slot = null!;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            slot = services.GetInstance<IMoInflAffixSlotFactory>().Create();
            category.AffixSlotsOC.Add(slot);
            slot.Optional = optional;
            entry = services.GetInstance<ILexEntryFactory>().Create(suffixType,
                TsStringUtils.MakeString(form, cache.DefaultVernWs), senseGloss,
                SandboxGenericMSA.Create(MsaType.kInfl, category));
            msa = Assert.IsAssignableFrom<IMoInflAffMsa>(entry.MorphoSyntaxAnalysesOC.Single());
            msa.SlotsRC.Add(slot);
        });
        return new AffixFixture(entry, msa, slot);
    }

    internal static ZeroAffixProject CreateZeroAffixProject(PristineProjectFixture pristine, string form,
        bool optional, string senseGloss = "", bool prepareParser = false)
    {
        string path;
        Guid entryId;
        Guid msaId;
        Guid slotId;
        using (var authored = pristine.NewScratch())
        {
            var fixture = CreateAffix(authored, pristine.Seed, form, optional, senseGloss);
            if (prepareParser)
            {
                var category = authored.ServiceLocator.GetInstance<IPartOfSpeechRepository>()
                    .GetObject(pristine.Seed.PartOfSpeechId);
                NonUndoableUnitOfWorkHelper.Do(authored.ActionHandlerAccessor, () =>
                {
                    CreateStem(authored, category, "ka", "root");
                    CreateStem(authored, category, "mi", "control");
                });
                RealParserProject.PrepareForParsing(authored, "k", "a", "m", "i", "o", "t", "f", "b");
            }
            path = authored.ProjectId.Path;
            entryId = fixture.Entry.Guid;
            msaId = fixture.Msa.Guid;
            slotId = fixture.Slot.Guid;
            new FwDataProjectLoader().Save(authored);
        }
        var cache = new FwDataProjectLoader().LoadScratchCache(path);
        var repository = cache.ServiceLocator.GetInstance<ICmObjectRepository>();
        var entry = Assert.IsAssignableFrom<ILexEntry>(repository.GetObject(entryId));
        var msa = Assert.IsAssignableFrom<IMoInflAffMsa>(repository.GetObject(msaId));
        var slot = Assert.IsAssignableFrom<IMoInflAffixSlot>(repository.GetObject(slotId));
        return new ZeroAffixProject(cache, new AffixFixture(entry, msa, slot));
    }

    private static void CreateStem(LcmCache cache, IPartOfSpeech category, string form, string gloss)
    {
        var stemType = cache.ServiceLocator.GetInstance<IMoMorphTypeRepository>()
            .GetObject(MoMorphTypeTags.kguidMorphStem);
        _ = cache.ServiceLocator.GetInstance<ILexEntryFactory>().Create(stemType,
            TsStringUtils.MakeString(form, cache.DefaultVernWs), gloss,
            SandboxGenericMSA.Create(MsaType.kStem, category));
    }

    internal static async Task<IReadOnlyList<AssessedWord>> AssessReadingSetAsync(string projectPath,
        PanGlossInvoker invoker, IReadOnlyList<string> words, string artifactRoot)
    {
        var store = new StatsCacheStore(WorkspaceOwnership.Bootstrap(artifactRoot));
        var assessor = new PanGlossAssessor(store, invoker);
        var produced = await assessor.ProduceAsync(new AssessmentScope(words, [AssessmentKind.ParseTime],
                TimeSpan.FromMilliseconds(1000), new StepCap(200_000)), Path.GetDirectoryName(projectPath)!,
            CancellationToken.None);
        var assessment = Assert.Single(produced);
        try
        {
            Assert.IsType<BatchInvocationEvidence>(assessment.Invocation);
            var batch = Assert.IsType<AssessmentRaw.Batch>(assessment.Raw).Analysis;
            return batch.Words.Select(word => new AssessedWord(word.Word, word.Outcome.ToStoredOutcome(), [],
                word.ElapsedMs, word.Signature) { Morphology = word.Morphology }).ToArray();
        }
        finally
        {
            assessment.ArtifactLease?.Dispose();
        }
    }

    internal static string CompletedReadingSet(AssessedWord word)
    {
        Assert.False(word.IsIncomplete, $"The search for '{word.Word}' did not complete.");
        Assert.True(word.Outcome is "analysed" or "no-analysis");
        var evidence = Assert.IsType<ParseWordEvidence>(word.Morphology);
        Assert.False(evidence.InvalidShape);
        Assert.Empty(evidence.Unavailable);
        var readings = evidence.Analyses.Select(analysis => JsonSerializer.Serialize(analysis.Morphs.Select(morph =>
                new { morph.Form, morph.Msa, morph.InflType, morph.GuessedString })))
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (word.Outcome == "no-analysis") Assert.Empty(readings);
        else Assert.NotEmpty(readings);
        return JsonSerializer.Serialize(new { word.Word, word.Outcome, readings });
    }

    internal static SIL.Motif.Model.DryRun.DryRun DryRunPatchedProject(LcmCache projectCache,
        Proposal proposal)
    {
        var scratchRoot = Path.Combine(Path.GetTempPath(), "Motif.ZeroAffixDryRun", Guid.NewGuid().ToString("N"));
        using var scratch = DryRunScratch.Adopt(
            new ScratchCacheFactory().CreateFromFileCopy(projectCache.ProjectId.Path, scratchRoot),
            "test copy of the saved zero-affix project",
            onDisposed: () =>
            {
                try { Directory.Delete(scratchRoot, recursive: true); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            });
        return ProposalDryRunner.Run(scratch, PrerequisiteExecutionPlan.Create(proposal, [], []));
    }

    private static RetireRedundantZeroAffixIntentDocument Intent(ILexEntry entry) =>
        new(new RetireRedundantZeroAffixIntent(CanonicalId.FromGuid(entry.Guid).Value));

    internal static Proposal Proposal(LcmCache cache, ILexEntry entry)
    {
        var operations = RetireRedundantZeroAffixComposer.Build(cache, Intent(entry),
            () => CanonicalId.FromGuid(Guid.NewGuid()));
        return new Proposal(new Dictionary<string, string>
        {
            ["analysis"] = "1.0", ["lexical"] = "1.0", ["phonology"] = "1.0",
        }, CanonicalId.Mint(), null, operations.Reverse().ToArray());
    }

    internal static Guid[] OwnedIds(LcmCache cache, ILexEntry entry) =>
        cache.ServiceLocator.GetInstance<ICmObjectRepository>().AllInstances()
            .Where(item => item.Guid == entry.Guid || HasOwner(item, entry.Guid))
            .Select(item => item.Guid).Order().ToArray();

    private static bool HasOwner(ICmObject item, Guid owner)
    {
        for (var current = item.Owner; current is not null; current = current.Owner)
            if (current.Guid == owner) return true;
        return false;
    }

    internal sealed record AffixFixture(ILexEntry Entry, IMoInflAffMsa Msa, IMoInflAffixSlot Slot);
    internal sealed record ZeroAffixProject(LcmCache Cache, AffixFixture Fixture);
}

[Collection(LcmCacheParallelCollections.Group1)]
public sealed class RetireRedundantZeroAffixParserAcceptanceTests(PristineProjectFixture pristine)
{
    [RealParserFact]
    public async Task PinnedParserKeepsExactCompletedReadingSetsAndHealthyGrammarAfterApply()
    {
        var project = RetireRedundantZeroAffixComposerTests.CreateZeroAffixProject(pristine, "^0", optional: true,
            prepareParser: true);
        using var cache = project.Cache;
        var fixture = project.Fixture;
        var words = new[] { "ka", "mi" };
        var ownedIds = RetireRedundantZeroAffixComposerTests.OwnedIds(cache, fixture.Entry);
        var path = cache.ProjectId.Path;
        var assessmentRoot = Path.Combine(Path.GetTempPath(), "Motif.ZeroAffixParser",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(assessmentRoot);
        using var invoker = new PanGlossInvoker(PanGlossExecutable.TryLocate());

        var before = await RetireRedundantZeroAffixComposerTests.AssessReadingSetAsync(path, invoker, words,
            Path.Combine(assessmentRoot, "before"));
        Assert.Contains(before, word => word.Word == "ka" && word.Outcome == "analysed");
        var msaId = fixture.Msa.Guid.ToString("D");
        var parserMsas = before.SelectMany(word => Assert.IsType<ParseWordEvidence>(word.Morphology).Analyses)
            .SelectMany(analysis => analysis.Morphs).Select(morph => morph.Msa).ToArray();
        Assert.DoesNotContain(parserMsas, id => id == msaId);
        var beforeReadings = before.Select(RetireRedundantZeroAffixComposerTests.CompletedReadingSet).ToArray();

        var beforeHealthOutcome = await invoker.RunAsync(new PanGlossRequest.GrammarHealth(path, "Synthetic"),
            "zero-affix-grammar-health-before", CancellationToken.None);
        var beforeHealth = Assert.IsType<PanGlossOutcome.Completed>(beforeHealthOutcome);
        using (var beforeReport = JsonDocument.Parse(beforeHealth.Output))
            Assert.DoesNotContain(beforeReport.RootElement.GetProperty("diagnostics").EnumerateArray().ToArray(),
                item => item.GetProperty("level").GetString() == "error");

        var proposal = RetireRedundantZeroAffixComposerTests.Proposal(cache, fixture.Entry);
        var dryRun = RetireRedundantZeroAffixComposerTests.DryRunPatchedProject(cache, proposal);
        ProposalApplier.Apply(cache, proposal, dryRun.Anchor, "pinned-zero-affix-retirement");

        var repository = cache.ServiceLocator.GetInstance<ICmObjectRepository>();
        Assert.All(ownedIds, id => Assert.False(repository.TryGetObject(id, out _)));
        Assert.True(repository.TryGetObject(fixture.Slot.Guid, out _));
        Assert.True(fixture.Slot.Optional);
        new FwDataProjectLoader().Save(cache);

        var after = await RetireRedundantZeroAffixComposerTests.AssessReadingSetAsync(path, invoker, words,
            Path.Combine(assessmentRoot, "after"));
        Assert.Equal(before.Select(word => word.Word), after.Select(word => word.Word));
        Assert.Equal(beforeReadings, after.Select(RetireRedundantZeroAffixComposerTests.CompletedReadingSet));

        var healthOutcome = await invoker.RunAsync(new PanGlossRequest.GrammarHealth(path, "Synthetic"),
            "zero-affix-grammar-health", CancellationToken.None);
        var health = Assert.IsType<PanGlossOutcome.Completed>(healthOutcome);
        using var report = JsonDocument.Parse(health.Output);
        Assert.DoesNotContain(report.RootElement.GetProperty("diagnostics").EnumerateArray().ToArray(),
            item => item.GetProperty("level").GetString() == "error");
        Directory.Delete(assessmentRoot, recursive: true);
    }
}
