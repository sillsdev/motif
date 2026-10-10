using System.Text.Json;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Contract.Retirement;
using SIL.Motif.Model.Snapshot;
using SIL.Motif.Projection.Retirement;
using SIL.Motif.Runner.Apply;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Runner.Retirement;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Tests.TestFixtures;
using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.Infrastructure;
using Xunit;
using ContractIntentDigest = SIL.Motif.Contract.Canonicalization.IntentDigest;

namespace SIL.Motif.Tests.Retirement;

[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group1)]
public sealed class AllomorphRetargetOperationsTests : IDisposable
{
    private readonly LcmCache _cache;
    private readonly SeededProject _seed;
    private readonly IMoAffixAllomorph _retired;
    private readonly IMoAffixAllomorph _replacement;
    private readonly IWfiWordform _wordform;
    private readonly IWfiAnalysis _analysis;
    private readonly IWfiMorphBundle _bundle;
    private readonly ISegment _textUse;

    public AllomorphRetargetOperationsTests(PristineProjectFixture pristine)
    {
        _cache = pristine.NewScratch();
        _seed = pristine.Seed;
        var entry = _cache.ServiceLocator.GetInstance<ILexEntryRepository>().GetObject(_seed.FirstEntryId);
        var msa = entry.MorphoSyntaxAnalysesOC.Single();
        var prefix = _cache.ServiceLocator.GetInstance<IMoMorphTypeRepository>()
            .GetObject(MoMorphTypeTags.kguidMorphPrefix);
        IMoAffixAllomorph retired = null!;
        IMoAffixAllomorph replacement = null!;
        IWfiWordform wordform = null!;
        IWfiAnalysis analysis = null!;
        IWfiMorphBundle bundle = null!;
        ISegment textUse = null!;

        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            retired = _cache.ServiceLocator.GetInstance<IMoAffixAllomorphFactory>().Create();
            replacement = _cache.ServiceLocator.GetInstance<IMoAffixAllomorphFactory>().Create();
            entry.AlternateFormsOS.Add(retired);
            entry.AlternateFormsOS.Add(replacement);
            retired.MorphTypeRA = prefix;
            replacement.MorphTypeRA = prefix;
            retired.Form.set_String(_cache.DefaultVernWs, "ta");
            replacement.Form.set_String(_cache.DefaultVernWs, "ra");

            wordform = _cache.ServiceLocator.GetInstance<IWfiWordformFactory>()
                .Create(SIL.LCModel.Core.Text.TsStringUtils.MakeString("word-surface", _cache.DefaultVernWs));
            analysis = _cache.ServiceLocator.GetInstance<IWfiAnalysisFactory>().Create();
            wordform.AnalysesOC.Add(analysis);
            bundle = _cache.ServiceLocator.GetInstance<IWfiMorphBundleFactory>().Create();
            analysis.MorphBundlesOS.Add(bundle);
            bundle.MorphRA = retired;
            bundle.MsaRA = msa;
            bundle.SenseRA = entry.SensesOS.FirstOrDefault();
            var otherBundle = _cache.ServiceLocator.GetInstance<IWfiMorphBundleFactory>().Create();
            analysis.MorphBundlesOS.Add(otherBundle);
            otherBundle.MorphRA = entry.LexemeFormOA;
            otherBundle.MsaRA = msa;
            _cache.LangProject.DefaultUserAgent.SetEvaluation(analysis, Opinions.approves);
            _cache.LangProject.DefaultParserAgent.SetEvaluation(analysis, Opinions.approves);

            var text = _cache.ServiceLocator.GetInstance<ITextFactory>().Create();
            text.Name.set_String(_cache.DefaultAnalWs, "retarget occurrence");
            text.ContentsOA = _cache.ServiceLocator.GetInstance<IStTextFactory>().Create();
            var paragraph = _cache.ServiceLocator.GetInstance<IStTxtParaFactory>().Create();
            text.ContentsOA.ParagraphsOS.Add(paragraph);
            textUse = _cache.ServiceLocator.GetInstance<ISegmentFactory>().Create();
            paragraph.SegmentsOS.Add(textUse);
            textUse.AnalysesRS.Add(NewWordform(_cache, "before-text-use"));
            textUse.AnalysesRS.Add(wordform);
            textUse.AnalysesRS.Add(NewWordform(_cache, "after-text-use"));
        });

        _retired = retired;
        _replacement = replacement;
        _wordform = wordform;
        _analysis = analysis;
        _bundle = bundle;
        _textUse = textUse;
    }

    public void Dispose()
    {
        if (!_cache.IsDisposed) _cache.Dispose();
    }

    [Fact]
    public void RetargetBundleMorph_DryRunShowsNativeFormCopyAndApplyPreservesTheApprovedAnalysis()
    {
        var proposal = Proposal();
        var beforeOpinion = _analysis.GetAgentOpinion(_cache.LangProject.DefaultUserAgent);
        var beforeSurface = _wordform.Form.VernacularDefaultWritingSystem.Text;
        var beforeEvaluations = _analysis.EvaluationsRC.Select(item => item.Guid).Order().ToArray();
        var beforeBundleOrder = _analysis.MorphBundlesOS.Select(item => item.Guid).ToArray();
        var beforeMsa = _bundle.MsaRA?.Guid;
        var beforeInflType = _bundle.InflTypeRA?.Guid;
        var beforeSense = _bundle.SenseRA?.Guid;
        var beforeTextUse = _textUse.AnalysesRS.Select(item => item.Guid).ToArray();
        var dryRun = ScratchDryRun.Of(_cache, proposal);

        Assert.Equal(_retired.Guid, _bundle.MorphRA!.Guid);
        Assert.Equal("ta", _bundle.Form.get_String(_cache.DefaultVernWs)!.Text);
        Assert.Equal(2, dryRun.ExpectedEffects.Count);
        var morph = Assert.Single(dryRun.ExpectedEffects, item => item.Field == SnapshotFields.WfiMorphBundleMorph);
        Assert.Equal(CanonicalId.FromGuid(_retired.Guid).Value, morph.Before["ref"]);
        Assert.Equal(CanonicalId.FromGuid(_replacement.Guid).Value, morph.After["ref"]);
        var text = Assert.Single(dryRun.ExpectedEffects, item => item.Field == SnapshotFields.WfiMorphBundleForm);
        Assert.Equal("ta", text.Before[_cache.WritingSystemFactory.GetStrFromWs(_cache.DefaultVernWs)]);
        Assert.Equal("ra", text.After[_cache.WritingSystemFactory.GetStrFromWs(_cache.DefaultVernWs)]);

        var receipt = ProposalApplier.Apply(_cache, proposal, dryRun.Anchor, "retirement-tests");

        Assert.Equal(2, receipt.ActualEffects.Count);
        Assert.Equal(_replacement.Guid, _bundle.MorphRA!.Guid);
        Assert.Equal("ra", _bundle.Form.get_String(_cache.DefaultVernWs)!.Text);
        Assert.Equal(_analysis.GetAgentOpinion(_cache.LangProject.DefaultUserAgent), beforeOpinion);
        Assert.Equal(Opinions.approves, _analysis.GetAgentOpinion(_cache.LangProject.DefaultParserAgent));
        Assert.Equal(beforeEvaluations, _analysis.EvaluationsRC.Select(item => item.Guid).Order().ToArray());
        Assert.Equal(beforeBundleOrder, _analysis.MorphBundlesOS.Select(item => item.Guid).ToArray());
        Assert.Equal(beforeMsa, _bundle.MsaRA?.Guid);
        Assert.Equal(beforeInflType, _bundle.InflTypeRA?.Guid);
        Assert.Equal(beforeSense, _bundle.SenseRA?.Guid);
        Assert.Equal(_wordform.Form.VernacularDefaultWritingSystem.Text, beforeSurface);
        Assert.Equal(beforeTextUse, _textUse.AnalysesRS.Select(item => item.Guid).ToArray());
        Assert.Equal(_seed.FirstEntryId, _replacement.Owner!.Guid);
    }

    [Fact]
    public void RetainedFormRetargetAllowsOwnedDependentsButRetirementRefusesThem()
    {
        IFsFeatStruc environmentFeatures = null!;
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            environmentFeatures = _cache.ServiceLocator.GetInstance<IFsFeatStrucFactory>().Create();
            _retired.MsEnvFeaturesOA = environmentFeatures;
        });

        var footprint = AllomorphReferenceFootprintReader.Read(_cache, [_retired.Guid]);
        var dependent = Assert.Single(footprint.OwnedDependents);
        Assert.Equal(environmentFeatures.Guid, dependent.DependentObject);
        Assert.Equal("MoAffixAllomorph", dependent.DeclaringClass);
        Assert.Equal("MsEnvFeatures", dependent.Field);

        var retargets = AllomorphRetargetComposer.Build(_cache, Intent(_replacement),
            () => CanonicalId.FromGuid(Guid.NewGuid()));
        Assert.Single(retargets);
        Assert.DoesNotContain(retargets,
            operation => operation.Kind == AlternateFormRetirementOperationKinds.DeleteAlternateForm);

        var refusal = Assert.Throws<InvalidOperationException>(() => RetireAllomorphComposer.Build(_cache,
            RetireIntent(_replacement), () => CanonicalId.FromGuid(Guid.NewGuid())));

        Assert.Contains("A retired form owns objects that deletion would cascade.", refusal.Message,
            StringComparison.Ordinal);
        Assert.Equal(_retired.Guid, _bundle.MorphRA!.Guid);
        Assert.Equal(_retired.Guid, environmentFeatures.Owner!.Guid);
    }

    [Fact]
    public void PinnedNativeMorphSetterCopiesReplacementAlternativesAndClearsMissingOnes()
    {
        _cache.ServiceLocator.WritingSystemManager.GetOrSet(NewLangProjFixture.SecondVernacularTag, out var secondVernacular);
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            _retired.Form.set_String(_cache.DefaultAnalWs, "source-analysis-form");
            _bundle.Form.set_String(_cache.DefaultAnalWs, "source-analysis-form");
            _replacement.Form.set_String(secondVernacular.Handle, "ra-secondary");
            _bundle.MorphRA = _replacement;
        });

        Assert.Equal("ra", _bundle.Form.get_String(_cache.DefaultVernWs)?.Text);
        Assert.Equal("ra-secondary", _bundle.Form.get_String(secondVernacular.Handle)?.Text);
        Assert.True(string.IsNullOrEmpty(_bundle.Form.get_String(_cache.DefaultAnalWs)?.Text));
    }

    [Fact]
    public void BundleRetargetRefusesCustomizedRichAndMissingWritingSystemText()
    {
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
            _bundle.Form.set_String(_cache.DefaultVernWs, "customized"));
        var customized = Assert.Throws<InvalidOperationException>(() => ScratchDryRun.Of(_cache, Proposal()));
        Assert.Contains("customized text", customized.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(_retired.Guid, _bundle.MorphRA!.Guid);

        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            var rich = SIL.LCModel.Core.Text.TsStringUtils.MakeString("ta", _cache.DefaultVernWs).GetBldr();
            rich.SetIntPropValues(0, 2, (int)FwTextPropType.ktptBold, (int)FwTextPropVar.ktpvEnum, 1);
            _bundle.Form.set_String(_cache.DefaultVernWs, rich.GetString());
        });
        var styled = Assert.Throws<InvalidOperationException>(() => ScratchDryRun.Of(_cache, Proposal()));
        Assert.Contains("non-writing-system text properties", styled.Message, StringComparison.OrdinalIgnoreCase);

        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            _bundle.Form.set_String(_cache.DefaultVernWs, "ta");
            _retired.Form.set_String(_cache.DefaultAnalWs, "source-only");
            _bundle.Form.set_String(_cache.DefaultAnalWs, "source-only");
        });
        var missing = Assert.Throws<InvalidOperationException>(() => ScratchDryRun.Of(_cache, Proposal()));
        Assert.Contains("lacks a populated writing-system alternative", missing.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(_retired.Guid, _bundle.MorphRA!.Guid);
    }

    [Fact]
    public void DisabledAdHocReferencesRewriteAllSupportedFieldsInOneOrderedOperation()
    {
        var entry = _cache.ServiceLocator.GetInstance<ILexEntryRepository>().GetObject(_seed.FirstEntryId);
        IMoAffixAllomorph untouched = null!;
        IMoAlloAdhocProhib firstRule = null!;
        IMoAlloAdhocProhib sequenceRule = null!;
        IMoAdhocProhibGr group = null!;
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            untouched = AddAlternate(entry, "mi");
            firstRule = _cache.ServiceLocator.GetInstance<IMoAlloAdhocProhibFactory>().Create();
            sequenceRule = _cache.ServiceLocator.GetInstance<IMoAlloAdhocProhibFactory>().Create();
            group = _cache.ServiceLocator.GetInstance<IMoAdhocProhibGrFactory>().Create();
            _cache.LangProject.MorphologicalDataOA.AdhocCoProhibitionsOC.Add(group);
            group.MembersOC.Add(firstRule);
            _cache.LangProject.MorphologicalDataOA.AdhocCoProhibitionsOC.Add(sequenceRule);
            firstRule.Disabled = true;
            sequenceRule.Disabled = true;
            firstRule.FirstAllomorphRA = _retired;
            sequenceRule.RestOfAllosRS.Add(untouched);
            sequenceRule.RestOfAllosRS.Add(_retired);
            sequenceRule.AllomorphsRS.Add(untouched);
            sequenceRule.AllomorphsRS.Add(_retired);
        });
        var proposal = Proposal();

        Assert.Equal(3, proposal.Operations.Count);
        var dryRun = ScratchDryRun.Of(_cache, proposal);
        Assert.Equal(_retired.Guid, firstRule.FirstAllomorphRA!.Guid);
        Assert.Equal(new[] { untouched.Guid, _retired.Guid }, sequenceRule.RestOfAllosRS.Select(item => item.Guid));
        Assert.Equal(new[] { untouched.Guid, _retired.Guid }, sequenceRule.AllomorphsRS.Select(item => item.Guid));
        Assert.Contains(dryRun.ExpectedEffects, item => item.Field == SnapshotFields.MoAlloAdhocProhibRestOfAllos &&
            item.Before["0000000001"] == CanonicalId.FromGuid(_retired.Guid).Value &&
            item.After["0000000001"] == CanonicalId.FromGuid(_replacement.Guid).Value);

        ProposalApplier.Apply(_cache, proposal, dryRun.Anchor, "retirement-tests");

        Assert.Equal(_replacement.Guid, firstRule.FirstAllomorphRA!.Guid);
        Assert.Equal(new[] { untouched.Guid, _replacement.Guid }, sequenceRule.RestOfAllosRS.Select(item => item.Guid));
        Assert.Equal(new[] { untouched.Guid, _replacement.Guid }, sequenceRule.AllomorphsRS.Select(item => item.Guid));
        Assert.True(firstRule.Disabled);
        Assert.True(sequenceRule.Disabled);
    }

    [Fact]
    public void ActiveAdHocRetargetRequiresAnExactUnconditionedSameDenotation()
    {
        IMoAlloAdhocProhib rule = null!;
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            _replacement.Form.set_String(_cache.DefaultVernWs, "ta");
            rule = _cache.ServiceLocator.GetInstance<IMoAlloAdhocProhibFactory>().Create();
            _cache.LangProject.MorphologicalDataOA.AdhocCoProhibitionsOC.Add(rule);
            rule.FirstAllomorphRA = _retired;
        });

        _ = ScratchDryRun.Of(_cache, Proposal());

        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
            _replacement.Form.set_String(_cache.DefaultVernWs, "ra"));
        var broader = Assert.Throws<InvalidOperationException>(() => ScratchDryRun.Of(_cache, Proposal()));
        Assert.Contains("meaning is not provably unchanged", broader.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(_retired.Guid, rule.FirstAllomorphRA!.Guid);
    }

    [Fact]
    public void RetargetingRefusesSelfTargetsAndCollapsedSequenceDestinations()
    {
        var selfTarget = Assert.Throws<InvalidOperationException>(() => AllomorphRetargetComposer.Build(
            _cache, Intent(_retired), () => CanonicalId.Mint()));
        Assert.Contains("cannot be a retarget destination", selfTarget.Message, StringComparison.OrdinalIgnoreCase);

        var entry = _cache.ServiceLocator.GetInstance<ILexEntryRepository>().GetObject(_seed.FirstEntryId);
        IMoAlloAdhocProhib rule = null!;
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            rule = _cache.ServiceLocator.GetInstance<IMoAlloAdhocProhibFactory>().Create();
            _cache.LangProject.MorphologicalDataOA.AdhocCoProhibitionsOC.Add(rule);
            rule.Disabled = true;
            rule.RestOfAllosRS.Add(_replacement);
            rule.RestOfAllosRS.Add(_retired);
        });
        var collapse = Assert.Throws<ContractParseException>(() =>
            ScratchDryRun.Of(_cache, Proposal(_replacement)));
        Assert.Contains("cannot repeat an id", collapse.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(new[] { _replacement.Guid, _retired.Guid }, rule.RestOfAllosRS.Select(item => item.Guid));
    }

    [Fact]
    public void RetargetingRefusesPatternAndNonPrefixForms()
    {
        var originalType = _retired.MorphTypeRA;
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () => _retired.IsAbstract = true);
        var pattern = Assert.Throws<InvalidOperationException>(() => Proposal());
        Assert.Contains("ordinary prefix and suffix", pattern.Message, StringComparison.OrdinalIgnoreCase);

        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            _retired.IsAbstract = false;
            _retired.MorphTypeRA = _cache.ServiceLocator.GetInstance<IMoMorphTypeRepository>()
                .GetObject(MoMorphTypeTags.kguidMorphCircumfix);
        });
        var unsupported = Assert.Throws<InvalidOperationException>(() => Proposal());
        Assert.Contains("ordinary prefix and suffix", unsupported.Message, StringComparison.OrdinalIgnoreCase);
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () => _retired.MorphTypeRA = originalType);
        Assert.Equal(_retired.Guid, _bundle.MorphRA!.Guid);
    }

    [Fact]
    public void DisabledFirstAllomorphCannotCollapseIntoRestOfAllos()
    {
        IMoAlloAdhocProhib rule = null!;
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            rule = _cache.ServiceLocator.GetInstance<IMoAlloAdhocProhibFactory>().Create();
            _cache.LangProject.MorphologicalDataOA.AdhocCoProhibitionsOC.Add(rule);
            rule.Disabled = true;
            rule.FirstAllomorphRA = _retired;
            rule.RestOfAllosRS.Add(_replacement);
        });

        var exception = Assert.Throws<InvalidOperationException>(() => ScratchDryRun.Of(_cache, Proposal()));

        Assert.Contains("FirstAllomorph overlap RestOfAllos", exception.Message, StringComparison.Ordinal);
        Assert.Equal(_retired.Guid, rule.FirstAllomorphRA!.Guid);
        Assert.Equal(_replacement.Guid, rule.RestOfAllosRS.Single().Guid);
    }

    [Fact]
    public void FailedWholeProposalRollsBackBundleFormAndAdHocRetargets()
    {
        var entry = _cache.ServiceLocator.GetInstance<ILexEntryRepository>().GetObject(_seed.FirstEntryId);
        IMoAffixAllomorph untouched = null!;
        IMoAlloAdhocProhib rule = null!;
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            untouched = AddAlternate(entry, "mi");
            rule = _cache.ServiceLocator.GetInstance<IMoAlloAdhocProhibFactory>().Create();
            _cache.LangProject.MorphologicalDataOA.AdhocCoProhibitionsOC.Add(rule);
            rule.Disabled = true;
            rule.RestOfAllosRS.Add(untouched);
            rule.RestOfAllosRS.Add(_retired);
        });
        var proposal = Proposal();
        var dryRun = ScratchDryRun.Of(_cache, proposal);
        var beforeForm = _bundle.Form.get_String(_cache.DefaultVernWs)?.Text;

        Assert.Throws<InvalidOperationException>(() => ProposalApplier.Apply(_cache, proposal, dryRun.Anchor,
            "retirement-tests", string.Empty, afterOperation: (index, _) =>
            {
                if (index == 2) throw new InvalidOperationException("injected after the ad hoc reference write");
            }));

        Assert.Equal(_retired.Guid, _bundle.MorphRA!.Guid);
        Assert.Equal(beforeForm, _bundle.Form.get_String(_cache.DefaultVernWs)?.Text);
        Assert.Equal(new[] { untouched.Guid, _retired.Guid }, rule.RestOfAllosRS.Select(item => item.Guid));
    }

    [Fact]
    public void AppliedRetargetSurvivesProjectSaveAndOpen()
    {
        var proposal = Proposal();
        var dryRun = ScratchDryRun.Of(_cache, proposal);
        ProposalApplier.Apply(_cache, proposal, dryRun.Anchor, "retirement-tests");
        new FwDataProjectLoader().Save(_cache);
        var root = Path.Combine(Path.GetTempPath(), "motif-retarget-save-open-" + Guid.NewGuid().ToString("N"));

        try
        {
            using var reopened = new ScratchCacheFactory().CreateFromFileCopy(_cache.ProjectId.Path, root);
            var bundle = reopened.ServiceLocator.GetInstance<IWfiMorphBundleRepository>().GetObject(_bundle.Guid);
            var analysis = reopened.ServiceLocator.GetInstance<IWfiAnalysisRepository>().GetObject(_analysis.Guid);
            Assert.Equal(_replacement.Guid, bundle.MorphRA!.Guid);
            Assert.Equal("ra", bundle.Form.get_String(reopened.DefaultVernWs)?.Text);
            Assert.Equal(Opinions.approves, analysis.GetAgentOpinion(reopened.LangProject.DefaultUserAgent));
            Assert.Equal(Opinions.approves, analysis.GetAgentOpinion(reopened.LangProject.DefaultParserAgent));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    [Fact]
    public void RetirementDeclaresRetargetsBeforeOwnedAlternateDeletionAndReadsBackAbsence()
    {
        var entry = _cache.ServiceLocator.GetInstance<ILexEntryRepository>().GetObject(_seed.FirstEntryId);
        IMoAffixAllomorph trailing = null!;
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            trailing = AddAlternate(entry, "mi");
            _retired.Form.set_String(_cache.DefaultVernWs, "ra");
            _bundle.Form.set_String(_cache.DefaultVernWs, "ra");
        });
        var beforeOrder = entry.AlternateFormsOS.Select(item => item.Guid).ToArray();
        var intent = RetireIntent(_replacement);
        var operations = RetireAllomorphComposer.Build(_cache, intent,
            () => CanonicalId.FromGuid(Guid.NewGuid()));
        var deletion = Assert.Single(operations,
            operation => operation.Kind == "lexical/lexEntry/deleteAlternateForm");
        var retargets = operations.Where(operation => operation.OperationId != deletion.OperationId).ToArray();

        Assert.NotEmpty(retargets);
        Assert.All(retargets, operation => Assert.Contains(operation.OperationId, deletion.DependsOn
            .Select(dependency => dependency.OperationId)));
        var proposal = new Proposal(new Dictionary<string, string>
            { ["analysis"] = "1.0", ["lexical"] = "1.0" }, CanonicalId.Mint(), null, operations);
        var dryRun = ScratchDryRun.Of(_cache, proposal);

        Assert.Contains(dryRun.ExpectedEffects, effect => effect.Field == "lexical/lexEntry/alternateForms");
        Assert.Contains(_retired, _cache.ServiceLocator.GetInstance<ILexEntryRepository>()
            .GetObject(_seed.FirstEntryId).AlternateFormsOS);
        ProposalApplier.Apply(_cache, proposal, dryRun.Anchor, "retirement-tests");

        entry = _cache.ServiceLocator.GetInstance<ILexEntryRepository>().GetObject(_seed.FirstEntryId);
        Assert.DoesNotContain(_retired, entry.AlternateFormsOS);
        Assert.Equal(beforeOrder.Where(id => id != _retired.Guid), entry.AlternateFormsOS.Select(item => item.Guid));
        Assert.Equal(new[] { _replacement.Guid, trailing.Guid }, entry.AlternateFormsOS.Select(item => item.Guid));
        Assert.False(_cache.ServiceLocator.GetInstance<ICmObjectRepository>()
            .TryGetObject(_retired.Guid, out _));
        Assert.Equal("word-surface", _wordform.Form.VernacularDefaultWritingSystem.Text);
        Assert.Equal(Opinions.approves, _analysis.GetAgentOpinion(_cache.LangProject.DefaultUserAgent));
    }

    [Fact]
    public void AlternateFormDeletionAcceptsRemovingTheLastAlternate()
    {
        var retiredId = CanonicalId.FromGuid(_retired.Guid);
        var survivorId = CanonicalId.FromGuid(_replacement.Guid);
        var payload = JsonSerializer.SerializeToElement(new
        {
            forms = new[] { retiredId.Value },
            duplicateSurvivors = new[] { survivorId.Value },
            formWitnesses = new[]
            {
                new { form = retiredId.Value, semanticDigest = AllomorphRetirementSemanticDigest.Compute(_cache, _retired) },
                new { form = survivorId.Value, semanticDigest = AllomorphRetirementSemanticDigest.Compute(_cache, _replacement) }
            },
            before = new[] { retiredId.Value },
            after = Array.Empty<string>()
        });

        var parsed = AlternateFormDeletePayload.Parse(payload);

        Assert.Equal(new[] { retiredId }, parsed.Before);
        Assert.Empty(parsed.After);
    }

    [Fact]
    public void AlternateFormDeletePayloadCarriesAnExplicitRuleReplacementWitness()
    {
        var retiredId = CanonicalId.FromGuid(_retired.Guid);
        var survivorId = CanonicalId.FromGuid(_replacement.Guid);
        var ruleId = CanonicalId.Mint();
        var inputId = CanonicalId.Mint();
        var outputId = CanonicalId.Mint();
        var payload = JsonSerializer.SerializeToElement(new
        {
            forms = new[] { retiredId.Value },
            duplicateSurvivors = new[] { survivorId.Value },
            formWitnesses = new[]
            {
                new { form = retiredId.Value, semanticDigest = AllomorphRetirementSemanticDigest.Compute(_cache, _retired) },
                new { form = survivorId.Value, semanticDigest = AllomorphRetirementSemanticDigest.Compute(_cache, _replacement) }
            },
            before = new[] { retiredId.Value },
            after = Array.Empty<string>(),
            ruleReplacement = new { rule = ruleId.Value, inputPhoneme = inputId.Value, outputPhoneme = outputId.Value }
        });

        var parsed = AlternateFormDeletePayload.Parse(payload);

        Assert.Equal(new RuleReplacementWitness(ruleId, inputId, outputId), parsed.RuleReplacement);
    }

    [Fact]
    public void RuleBackedRetirementRequiresTheNamedActiveRuleAndRetargetsInTheSameProposal()
    {
        var phonemeR = AddPhoneme("r");
        var phonemeT = AddPhoneme("t");
        var phonemeA = AddPhoneme("a");
        var classOperations = AuthorNaturalClassComposer.Build(_cache,
            new("low vowel", "LV", [phonemeA]));
        Apply(classOperations);
        var naturalClass = classOperations.Single(item => item.Kind == PhPhonDataNaturalClassesOperationKinds.Create)
            .EntityId!.Value;
        var ruleOperations = AuthorPhonologicalRuleComposer.Build(_cache,
            new("r becomes t before a", PhonologicalRuleDirection.Simultaneous,
                [new(Phoneme: phonemeR)], [new(Phoneme: phonemeT)],
                [new(NaturalClass: naturalClass)], [new(NaturalClass: naturalClass)]));
        var ruleId = ruleOperations.Single(item => item.Kind == PhPhonDataPhonRulesOperationKinds.Create)
            .EntityId!.Value;
        var enableId = ruleOperations.Single(item => item.Kind == PhSegmentRuleDisabledOperationKinds.SetDisabled)
            .OperationId;
        var retargets = AllomorphRetargetComposer.Build(_cache, Intent(_replacement),
            () => CanonicalId.FromGuid(Guid.NewGuid())).Select(item => DependsOn(item, enableId)).ToArray();

        var missingRuleDeletion = DeleteOperation(CanonicalId.Mint(), phonemeR, phonemeT, retargets);
        var missingRuleProposal = Proposal([.. ruleOperations, .. retargets, missingRuleDeletion]);
        var refused = Assert.Throws<InvalidOperationException>(() => ScratchDryRun.Of(_cache, missingRuleProposal));
        Assert.Contains("active exact rule replacement", refused.Message, StringComparison.Ordinal);

        var deletion = DeleteOperation(ruleId, phonemeR, phonemeT, retargets);
        var proposal = Proposal([.. ruleOperations, .. retargets, deletion]);
        var dryRun = ScratchDryRun.Of(_cache, proposal);

        Assert.Equal(1, dryRun.ExpectedEffects.Count(item => item.Field == SnapshotFields.WfiMorphBundleMorph));
        Assert.Contains(dryRun.ExpectedEffects, item => item.Field == SnapshotFields.LexEntryAlternateForms);
        ProposalApplier.Apply(_cache, proposal, dryRun.Anchor, "rule-backed-retirement-tests");

        Assert.DoesNotContain(_retired, _cache.ServiceLocator.GetInstance<ILexEntryRepository>()
            .GetObject(_seed.FirstEntryId).AlternateFormsOS);
        Assert.Equal(_replacement.Guid, _bundle.MorphRA!.Guid);
        Assert.Equal(Opinions.approves, _analysis.GetAgentOpinion(_cache.LangProject.DefaultUserAgent));
        Assert.Equal("word-surface", _wordform.Form.VernacularDefaultWritingSystem.Text);
    }

    [Fact]
    public void RuleReplacementRefusesMatchingActivePositionRestrictions()
    {
        var input = AddPhoneme("r");
        var output = AddPhoneme("t");
        IPhEnvironment environment = null!;
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            environment = _cache.ServiceLocator.GetInstance<IPhEnvironmentFactory>().Create();
            _cache.LangProject.PhonologicalDataOA.EnvironmentsOS.Add(environment);
            environment.StringRepresentation = SIL.LCModel.Core.Text.TsStringUtils.MakeString(
                "a _", _cache.DefaultVernWs);
            _retired.PositionRS.Add(environment);
            _replacement.PositionRS.Add(environment);
        });

        Assert.False(RuleBackedAllomorphFormProof.Matches(_cache, _retired, _replacement, input, output));
    }

    [Fact]
    public void RuleComposerRefusesAnEnvironmentWithOneEmptySide()
    {
        var input = AddPhoneme("r");
        var output = AddPhoneme("t");
        var vowel = AddPhoneme("a");
        var classOperations = AuthorNaturalClassComposer.Build(_cache,
            new("vowels", "V", [vowel]));
        Apply(classOperations);
        var naturalClass = classOperations.Single(item => item.Kind == PhPhonDataNaturalClassesOperationKinds.Create)
            .EntityId!.Value;

        var refused = Assert.Throws<InvalidOperationException>(() => AuthorPhonologicalRuleComposer.Build(_cache,
            new("r after a vowel", PhonologicalRuleDirection.Simultaneous,
                [new(Phoneme: input)], [new(Phoneme: output)], [new(NaturalClass: naturalClass)], [])));

        Assert.Contains("right context must contain between 1 and 8", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReplacementComposerBuildsOneDependencyOrderedProposalWithAPlannedClass()
    {
        var phonemeR = AddPhoneme("r");
        var phonemeT = AddPhoneme("t");
        var phonemeA = AddPhoneme("a");
        var classId = CanonicalId.Mint();
        var ruleId = CanonicalId.Mint();
        var abstractIds = Enumerable.Range(0, 12).Select(_ => CanonicalId.Mint()).ToArray();
        var retirement = Intent(_replacement);
        var ruleOperationIds = abstractIds.Skip(2).Take(7).ToArray();
        var bindings = new List<RetirementOperationBinding>
        {
            new(abstractIds[0].Value, "class-create", classId.Value, null, []),
            new(abstractIds[1].Value, "class-members", classId.Value, null, [abstractIds[0].Value]),
            new(ruleOperationIds[0].Value, "rule-create", ruleId.Value, null, [abstractIds[1].Value])
        };
        foreach (var (slot, index) in new[] { "rule-input", "rule-output", "rule-left", "rule-right", "rule-placement", "rule-enabled" }
                     .Select((slot, index) => (slot, index)))
            bindings.Add(new(ruleOperationIds[index + 1].Value, slot, ruleId.Value, null,
                [abstractIds[1].Value, ruleOperationIds[0].Value]));
        var bundleOperation = CanonicalId.Mint();
        bindings.Add(new(bundleOperation.Value, "bundle-morph", CanonicalId.FromGuid(_bundle.Guid).Value, null,
            ruleOperationIds.Select(id => id.Value).ToArray()));
        var deletionOperation = CanonicalId.Mint();
        bindings.Add(new(deletionOperation.Value, "alternate-delete", retirement.Entry,
            CanonicalId.FromGuid(_retired.Guid).Value,
            [bundleOperation.Value, .. ruleOperationIds.Select(id => id.Value)]));
        var intent = new ReplaceListedAllomorphsWithRuleIntent(
            new("create", classId.Value, "low vowel", "LV", [phonemeA.Value], null),
            new(ruleId.Value, "r becomes t between vowels", [phonemeR.Value], [phonemeT.Value],
                [new("natural-class", classId.Value)], [new("natural-class", classId.Value)],
                new("last", null), true),
            [retirement], bindings);

        var operations = ReplaceListedAllomorphsWithRuleComposer.Build(_cache, intent,
            () => CanonicalId.FromGuid(Guid.NewGuid()));
        var deletion = Assert.Single(operations, item => item.Kind == AlternateFormRetirementOperationKinds.DeleteAlternateForm);
        var retarget = Assert.Single(operations, item => item.Kind == AllomorphRetargetOperationKinds.SetBundleMorph);
        var ruleCreate = Assert.Single(operations, item => item.Kind == PhPhonDataPhonRulesOperationKinds.Create);
        var ruleEnabled = Assert.Single(operations, item => item.Kind == PhSegmentRuleDisabledOperationKinds.SetDisabled);
        Assert.Contains(ruleEnabled.OperationId, retarget.DependsOn.Select(item => item.OperationId));
        Assert.Contains(retarget.OperationId, deletion.DependsOn.Select(item => item.OperationId));
        Assert.Contains(ruleCreate.OperationId, ruleEnabled.DependsOn.Select(item => item.OperationId));

        var proposal = Proposal(operations.Reverse().ToArray());
        var dryRun = ScratchDryRun.Of(_cache, proposal);
        Assert.Contains(dryRun.ExpectedEffects, item => item.Field == SnapshotFields.PhPhonDataNaturalClasses);
        Assert.Contains(dryRun.ExpectedEffects, item => item.Field == SnapshotFields.PhPhonDataPhonRules);
        Assert.Contains(dryRun.ExpectedEffects, item => item.Field == SnapshotFields.LexEntryAlternateForms);
        ProposalApplier.Apply(_cache, proposal, dryRun.Anchor, "rule-backed-retirement-tests");

        Assert.DoesNotContain(_retired, _cache.ServiceLocator.GetInstance<ILexEntryRepository>()
            .GetObject(_seed.FirstEntryId).AlternateFormsOS);
        Assert.Equal(_replacement.Guid, _bundle.MorphRA!.Guid);
    }

    [Fact]
    public void RetirementFaultAfterEachOperationRestoresRetargetAndOwnedForm()
    {
        var entry = _cache.ServiceLocator.GetInstance<ILexEntryRepository>().GetObject(_seed.FirstEntryId);
        var beforeOrder = entry.AlternateFormsOS.Select(item => item.Guid).ToArray();
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            _retired.Form.set_String(_cache.DefaultVernWs, "ra");
            _bundle.Form.set_String(_cache.DefaultVernWs, "ra");
        });
        var operations = RetireAllomorphComposer.Build(_cache, RetireIntent(_replacement),
            () => CanonicalId.FromGuid(Guid.NewGuid()));
        var proposal = new Proposal(new Dictionary<string, string>
            { ["analysis"] = "1.0", ["lexical"] = "1.0" }, CanonicalId.Mint(), null, operations);
        var dryRun = ScratchDryRun.Of(_cache, proposal);

        Assert.NotEmpty(operations);
        foreach (var failingIndex in Enumerable.Range(1, operations.Count))
        {
            Assert.Throws<InvalidOperationException>(() => ProposalApplier.Apply(_cache, proposal, dryRun.Anchor,
                "retirement-tests", string.Empty, afterOperation: (index, _) =>
                {
                    if (index == failingIndex) throw new InvalidOperationException("injected retirement fault");
                }));

            Assert.Equal(beforeOrder, entry.AlternateFormsOS.Select(item => item.Guid));
            Assert.True(_cache.ServiceLocator.GetInstance<ICmObjectRepository>()
                .TryGetObject(_retired.Guid, out _));
            Assert.Equal(_retired.Guid, _bundle.MorphRA!.Guid);
            Assert.Equal("ra", _bundle.Form.get_String(_cache.DefaultVernWs)?.Text);
            Assert.Equal(Opinions.approves, _analysis.GetAgentOpinion(_cache.LangProject.DefaultUserAgent));
        }
    }

    [Fact]
    public void RetirementRefusesFormMeaningDriftAfterComposition()
    {
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            _retired.Form.set_String(_cache.DefaultVernWs, "ra");
            _bundle.Form.set_String(_cache.DefaultVernWs, "ra");
        });
        var operations = RetireAllomorphComposer.Build(_cache, RetireIntent(_replacement),
            () => CanonicalId.FromGuid(Guid.NewGuid()));
        var proposal = new Proposal(new Dictionary<string, string>
            { ["analysis"] = "1.0", ["lexical"] = "1.0" }, CanonicalId.Mint(), null, operations);
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            _retired.Form.set_String(_cache.DefaultVernWs, "changed");
            _replacement.Form.set_String(_cache.DefaultVernWs, "changed");
            _bundle.Form.set_String(_cache.DefaultVernWs, "changed");
        });

        var exception = Assert.Throws<InvalidOperationException>(() => ScratchDryRun.Of(_cache, proposal));

        Assert.Contains("measured semantic meaning changed", exception.Message, StringComparison.Ordinal);
        Assert.Contains(_retired, _cache.ServiceLocator.GetInstance<ILexEntryRepository>()
            .GetObject(_seed.FirstEntryId).AlternateFormsOS);
        Assert.Equal(_retired.Guid, _bundle.MorphRA!.Guid);
    }

    [Fact]
    public void MissingReferenceDestinationRefusesWithTheCompleteCountDiagnostic()
    {
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            _retired.Form.set_String(_cache.DefaultVernWs, "ra");
            _bundle.Form.set_String(_cache.DefaultVernWs, "ra");
        });
        var complete = RetireIntent(_replacement);
        var incomplete = complete with
        {
            Retirement = complete.Retirement with { RoleReplacements = [] }
        };

        var exception = Assert.Throws<InvalidOperationException>(() => RetireAllomorphComposer.Build(_cache,
            incomplete, () => CanonicalId.FromGuid(Guid.NewGuid())));

        Assert.Contains("1 unresolved reference rows", exception.Message, StringComparison.Ordinal);
        Assert.Contains("1 Approved analyses", exception.Message, StringComparison.Ordinal);
        Assert.Contains("0 ad hoc rules", exception.Message, StringComparison.Ordinal);
        Assert.Equal(_retired.Guid, _bundle.MorphRA!.Guid);
    }

    [Fact]
    public void RetirementRefusesAnUnclassifiedCustomIncomingReference()
    {
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
            _replacement.Form.set_String(_cache.DefaultVernWs,
                _retired.Form.get_String(_cache.DefaultVernWs)!.Text));
        var metadata = (IFwMetaDataCacheManaged)_cache.MetaDataCacheAccessor;
        var formClass = metadata.GetClassIds().Single(id => metadata.GetClassName(id) == "MoForm");
        var field = metadata.AddCustomField("LexEntry", "UnclassifiedRetirementReference",
            SIL.LCModel.Core.Cellar.CellarPropertyType.ReferenceAtomic, formClass);
        var entry = _cache.ServiceLocator.GetInstance<ILexEntryRepository>().GetObject(_seed.FirstEntryId);
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
            _cache.DomainDataByFlid.SetObjProp(entry.Hvo, field, _retired.Hvo));

        var refusal = Assert.Throws<InvalidOperationException>(() => RetireAllomorphComposer.Build(_cache,
            RetireIntent(_replacement), () => CanonicalId.FromGuid(Guid.NewGuid())));

        Assert.Contains("unresolved reference rows", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("LexEntry.UnclassifiedRetirementReference", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("No supported retirement operation accounts for", refusal.Message, StringComparison.Ordinal);
        Assert.Equal(_retired.Guid, _bundle.MorphRA!.Guid);
        Assert.True(_cache.ServiceLocator.ObjectRepository.TryGetObject(_retired.Guid, out _));
    }

    [Fact]
    public void AReferenceAddedAfterCompositionBlocksTheDryRunBeforeAnyRetarget()
    {
        var proposal = Proposal();
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            var otherWordform = _cache.ServiceLocator.GetInstance<IWfiWordformFactory>()
                .Create(SIL.LCModel.Core.Text.TsStringUtils.MakeString("later-use", _cache.DefaultVernWs));
            var otherAnalysis = _cache.ServiceLocator.GetInstance<IWfiAnalysisFactory>().Create();
            otherWordform.AnalysesOC.Add(otherAnalysis);
            var otherBundle = _cache.ServiceLocator.GetInstance<IWfiMorphBundleFactory>().Create();
            otherAnalysis.MorphBundlesOS.Add(otherBundle);
            otherBundle.MorphRA = _retired;
            otherBundle.MsaRA = _analysis.MorphBundlesOS[0].MsaRA;
        });

        var exception = Assert.Throws<InvalidOperationException>(() => ScratchDryRun.Of(_cache, proposal));

        Assert.Contains("reference footprint changed", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(_retired.Guid, _bundle.MorphRA!.Guid);
    }

    [Fact]
    public void ApplyRefusesReplacementMsaOpinionAndTextUseDriftAfterDryRun()
    {
        var originalMsa = _bundle.MsaRA;
        var replacementProposal = Proposal();
        var replacementDryRun = ScratchDryRun.Of(_cache, replacementProposal);
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
            _replacement.Form.set_String(_cache.DefaultVernWs, "changed-after-dry-run"));
        var replacementDrift = Assert.Throws<ApplyPreconditionException>(() =>
            ProposalApplier.Apply(_cache, replacementProposal, replacementDryRun.Anchor, "retirement-tests"));
        Assert.Contains("Footprint drift detected", replacementDrift.Message, StringComparison.Ordinal);
        Assert.Equal(_retired.Guid, _bundle.MorphRA!.Guid);

        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
            _replacement.Form.set_String(_cache.DefaultVernWs, "ra"));
        var opinionProposal = Proposal();
        var opinionDryRun = ScratchDryRun.Of(_cache, opinionProposal);
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
            _cache.LangProject.DefaultUserAgent.SetEvaluation(_analysis, Opinions.disapproves));
        var opinionDrift = Assert.Throws<ApplyPreconditionException>(() =>
            ProposalApplier.Apply(_cache, opinionProposal, opinionDryRun.Anchor, "retirement-tests"));
        Assert.Contains("Footprint drift detected", opinionDrift.Message, StringComparison.Ordinal);

        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
            _cache.LangProject.DefaultUserAgent.SetEvaluation(_analysis, Opinions.approves));
        var msaProposal = Proposal();
        var msaDryRun = ScratchDryRun.Of(_cache, msaProposal);
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () => _bundle.MsaRA = null);
        var msaDrift = Assert.Throws<ApplyPreconditionException>(() =>
            ProposalApplier.Apply(_cache, msaProposal, msaDryRun.Anchor, "retirement-tests"));
        Assert.Contains("Footprint drift detected", msaDrift.Message, StringComparison.Ordinal);
        Assert.Equal(_retired.Guid, _bundle.MorphRA!.Guid);

        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () => _bundle.MsaRA = originalMsa);
        var textUseProposal = Proposal();
        var textUseDryRun = ScratchDryRun.Of(_cache, textUseProposal);
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () => _textUse.AnalysesRS[1] = _textUse.AnalysesRS[0]);
        var textUseDrift = Assert.Throws<ApplyPreconditionException>(() =>
            ProposalApplier.Apply(_cache, textUseProposal, textUseDryRun.Anchor, "retirement-tests"));
        Assert.Contains("Footprint drift detected", textUseDrift.Message, StringComparison.Ordinal);
        Assert.Equal(_retired.Guid, _bundle.MorphRA!.Guid);
    }

    private Proposal Proposal(IMoAffixAllomorph? destination = null)
    {
        new FwDataProjectLoader().Save(_cache);
        var intent = Intent(destination ?? _replacement);
        var operations = AllomorphRetargetComposer.Build(_cache, intent,
            () => CanonicalId.FromGuid(Guid.NewGuid()));
        return new Proposal(new Dictionary<string, string> { ["analysis"] = "1.0" },
            CanonicalId.Mint(), null, operations);
    }

    private Proposal Proposal(IReadOnlyList<OperationEnvelope> operations) =>
        new(new Dictionary<string, string> { ["analysis"] = "1.0", ["lexical"] = "1.0", ["phonology"] = "1.0" },
            CanonicalId.Mint(), null, operations);

    private void Apply(IReadOnlyList<OperationEnvelope> operations)
    {
        var proposal = Proposal(operations);
        var dryRun = ScratchDryRun.Of(_cache, proposal);
        ProposalApplier.Apply(_cache, proposal, dryRun.Anchor, "rule-backed-retirement-tests");
    }

    private CanonicalId AddPhoneme(string representation)
    {
        var operations = AuthorPhonemeComposer.Build(_cache, new(representation, [representation]));
        Apply(operations);
        return operations.Single(item => item.Kind == PhPhonemeSetPhonemesOperationKinds.Create)
            .EntityId!.Value;
    }

    private OperationEnvelope DeleteOperation(CanonicalId rule, CanonicalId input, CanonicalId output,
        IReadOnlyList<OperationEnvelope> retargets)
    {
        var entry = _cache.ServiceLocator.GetInstance<ILexEntryRepository>().GetObject(_seed.FirstEntryId);
        var retiredId = CanonicalId.FromGuid(_retired.Guid);
        var survivorId = CanonicalId.FromGuid(_replacement.Guid);
        var before = entry.AlternateFormsOS.Select(item => CanonicalId.FromGuid(item.Guid)).ToArray();
        return new OperationEnvelope(CanonicalId.FromGuid(Guid.NewGuid()),
            AlternateFormRetirementOperationKinds.DeleteAlternateForm,
            target: CanonicalId.FromGuid(entry.Guid),
            after: JsonSerializer.SerializeToElement(new
            {
                forms = new[] { retiredId.Value },
                duplicateSurvivors = new[] { survivorId.Value },
                formWitnesses = new[]
                {
                    new { form = retiredId.Value, semanticDigest = AllomorphRetirementSemanticDigest.Compute(_cache, _retired) },
                    new { form = survivorId.Value, semanticDigest = AllomorphRetirementSemanticDigest.Compute(_cache, _replacement) }
                },
                before = before.Select(item => item.Value),
                after = before.Where(item => item != retiredId).Select(item => item.Value),
                ruleReplacement = new { rule = rule.Value, inputPhoneme = input.Value, outputPhoneme = output.Value }
            }),
            dependsOn: retargets.Select(item => new OperationDependency(item.OperationId)).ToArray());
    }

    private static OperationEnvelope DependsOn(OperationEnvelope operation, CanonicalId prerequisite) =>
        new(operation.OperationId, operation.Kind, operation.EntityId, operation.Target, operation.After,
            operation.Placement, operation.DependsOn.Append(new OperationDependency(prerequisite)).ToArray(),
            operation.StorageIdOverride, operation.Rationale, operation.Confidence, operation.Provenance,
            operation.Extensions);

    private RetireAllomorphIntent Intent(IMoAffixAllomorph destination)
    {
        var entryId = CanonicalId.FromGuid(_seed.FirstEntryId);
        var retiredId = CanonicalId.FromGuid(_retired.Guid);
        var footprint = AllomorphReferenceFootprintReader.Read(_cache, [_retired.Guid]);
        var bundles = footprint.References.Where(item => item.Kind == "bundle-morph").ToArray();
        var adHoc = footprint.References.Where(item => item.Kind.StartsWith("adhoc-", StringComparison.Ordinal)).ToArray();
        var roles = bundles.Select(item => (item.Msa!.Value, item.InflType)).Distinct()
            .Select(role => new AllomorphRoleReplacement(retiredId.Value,
                CanonicalId.FromGuid(role.Value).Value,
                role.InflType is { } infl ? CanonicalId.FromGuid(infl).Value : null, "whole",
                Identity(destination, entryId, "alternate"))).ToArray();
        var intent = new RetireAllomorphIntent(
            entryId.Value, AllomorphRetirementScope.Affix,
            [Identity(_retired, entryId, "alternate")],
            roles,
            bundles.Select(item => new RetirementBundle(CanonicalId.FromGuid(item.Bundle!.Value).Value,
                CanonicalId.FromGuid(item.Analysis!.Value).Value, CanonicalId.FromGuid(item.Wordform!.Value).Value,
                retiredId.Value, CanonicalId.FromGuid(item.Msa!.Value).Value,
                item.InflType is { } infl ? CanonicalId.FromGuid(infl).Value : null, "whole")).ToArray(),
            adHoc.Select(item => new AdhocOccurrenceReplacement(CanonicalId.FromGuid(item.Rule!.Value).Value,
                item.Field, item.Ordinal, retiredId.Value, Identity(destination, entryId, "alternate"))).ToArray());
        return intent;
    }

    private RetireAllomorphIntentDocument RetireIntent(IMoAffixAllomorph destination)
    {
        var entryId = CanonicalId.FromGuid(_seed.FirstEntryId);
        return new(Intent(destination), [Identity(destination, entryId, "alternate")]);
    }

    [Theory]
    [InlineData(AllomorphRetirementScope.Stem)]
    [InlineData((AllomorphRetirementScope)99)]
    public void RetainedFormComposerRefusesUnsupportedScopeBeforeProducingOperations(AllomorphRetirementScope scope)
    {
        var called = false;
        var intent = Intent(_replacement) with { Scope = scope };
        Assert.Throws<InvalidOperationException>(() => AllomorphRetargetComposer.Build(_cache, intent, () =>
        {
            called = true;
            return CanonicalId.FromGuid(Guid.NewGuid());
        }));
        Assert.False(called);
        Assert.Equal(_retired.Guid, _bundle.MorphRA!.Guid);
    }

    private AllomorphIdentity Identity(IMoAffixAllomorph form, CanonicalId entry, string location) =>
        new(CanonicalId.FromGuid(form.Guid).Value, entry.Value, form.ClassName, location, "prefix",
            AllomorphRetirementSemanticDigest.Compute(_cache, form), null);

    private IMoAffixAllomorph AddAlternate(ILexEntry entry, string form) =>
        AddAlternate(_cache, entry, form);

    private static IMoAffixAllomorph AddAlternate(LcmCache cache, ILexEntry entry, string form)
    {
        var value = cache.ServiceLocator.GetInstance<IMoAffixAllomorphFactory>().Create();
        entry.AlternateFormsOS.Add(value);
        value.MorphTypeRA = cache.ServiceLocator.GetInstance<IMoMorphTypeRepository>()
            .GetObject(MoMorphTypeTags.kguidMorphPrefix);
        value.Form.set_String(cache.DefaultVernWs, form);
        return value;
    }

    private static IWfiWordform NewWordform(LcmCache cache, string text) =>
        cache.ServiceLocator.GetInstance<IWfiWordformFactory>().Create(
            SIL.LCModel.Core.Text.TsStringUtils.MakeString(text, cache.DefaultVernWs));
}
