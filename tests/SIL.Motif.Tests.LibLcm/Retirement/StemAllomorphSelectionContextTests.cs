using SIL.LCModel;
using SIL.LCModel.Core.Cellar;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.Core.Text;
using SIL.LCModel.DomainServices;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Projection.Retirement;
using SIL.Motif.Model.Snapshot;
using SIL.Motif.Runner.Retirement;
using SIL.Motif.Runner.Snapshotting;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Retirement;

[Collection(LcmCacheTestCollection.Name)]
public sealed class StemAllomorphSelectionContextTests : IDisposable
{
    private readonly LcmCache _cache;
    private readonly SeededProject _seed;
    private readonly ILexEntry _entry;
    private readonly IMoStemAllomorph _form;
    private readonly IMoStemName _stemName;
    private readonly IPartOfSpeech _partOfSpeech;

    public StemAllomorphSelectionContextTests(PristineProjectFixture pristine)
    {
        _cache = pristine.NewScratch();
        _seed = pristine.Seed;
        var services = _cache.ServiceLocator;
        _entry = services.GetInstance<ILexEntryRepository>().GetObject(_seed.FirstEntryId);
        _partOfSpeech = services.GetInstance<IPartOfSpeechRepository>().GetObject(_seed.PartOfSpeechId);
        _form = services.GetInstance<IMoStemAllomorphFactory>().Create();
        _stemName = services.GetInstance<IMoStemNameFactory>().Create();

        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            _entry.AlternateFormsOS.Add(_form);
            _form.MorphTypeRA = services.GetInstance<IMoMorphTypeRepository>()
                .GetObject(MoMorphTypeTags.kguidMorphRoot);
            _form.Form.set_String(_cache.DefaultVernWs, "motif-alternate");
            _partOfSpeech.StemNamesOC.Add(_stemName);
            _stemName.Name.set_String(_cache.DefaultAnalWs, "past");
            _form.StemNameRA = _stemName;
        });
    }

    public void Dispose()
    {
        if (!_cache.IsDisposed) _cache.Dispose();
    }

    [Fact]
    public void ReferenceFootprintIncludesEveryStemRouteAndEverySegmentReading()
    {
        var text = SeededProject.SeedText(_cache, _seed);
        var wordform = _cache.ServiceLocator.GetInstance<IWfiWordformRepository>().GetObject(text.AnalysedWordformId);
        var analysis = wordform.AnalysesOC.Single();
        var msa = _entry.SensesOS[0].MorphoSyntaxAnalysisRA!;
        var segment = _cache.ServiceLocator.GetInstance<ISegmentRepository>().GetObject(text.FirstSegmentId);
        var gloss = _cache.ServiceLocator.GetInstance<IWfiGlossFactory>().Create();
        var derivation = _cache.ServiceLocator.GetInstance<IMoDerivFactory>().Create();
        var stratumApp = _cache.ServiceLocator.GetInstance<IMoStratumAppFactory>().Create();
        var compound = _cache.ServiceLocator.GetInstance<IMoCompoundRuleAppFactory>().Create();
        var metadata = (IFwMetaDataCacheManaged)_cache.MetaDataCacheAccessor;
        var customFields = new int[3];
        var censusBefore = AllomorphRetargetCensusReader.ComputeDigest(_cache, [_form.Guid]);
        var alternateWs = _cache.WritingSystemFactory.GetWsFromStr(SeededProject.RightToLeftTag);

        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            _form.Form.set_String(alternateWs, "motif-alternate-ar");
            wordform.Form.set_String(alternateWs, "motif-analysed-ar");
            analysis.MorphBundlesOS[0].MorphRA = _form;
            segment.AnalysesRS.Add(wordform);
            analysis.MeaningsOC.Add(gloss);
            segment.AnalysesRS.Add(gloss);
            gloss.Form.set_String(_cache.DefaultAnalWs, "alternate reading");
            analysis.DerivationOA = derivation;
            derivation.StratumAppsOS.Add(stratumApp);
            stratumApp.CompoundRuleAppsOS.Add(compound);
            var disapproved = AddReading(_cache, wordform, msa, _form);
            _cache.LangProject.DefaultUserAgent.SetEvaluation(disapproved, Opinions.disapproves);
            var unknown = AddReading(_cache, wordform, msa, _form);
            _ = AddReading(_cache, wordform, msa,
                _cache.ServiceLocator.GetInstance<IMoFormRepository>().GetObject(_seed.SecondLexemeFormId));
            _cache.LangProject.DefaultUserAgent.SetEvaluation(analysis, Opinions.approves);
            derivation.StemFormRA = _form;
            compound.LeftFormRA = _form;
            compound.RightFormRA = _form;
            customFields[0] = AddCustomReference(metadata, "MoStemAllomorph");
            customFields[1] = AddCustomReference(metadata, "MoForm");
            customFields[2] = AddCustomReference(metadata, "CmObject");
            foreach (var field in customFields)
                _cache.DomainDataByFlid.SetObjProp(_entry.Hvo, field, _form.Hvo);
        });

        var before = SemanticSnapshot(_cache);
        var footprint = AllomorphReferenceFootprintReader.Read(_cache, [_form.Guid]);
        var retargetDigest = AllomorphRetargetCensusReader.ComputeDigest(_cache, [_form.Guid]);
        _ = StemAllomorphSelectionContextReader.Read(_cache, [_form.Guid]);
        var after = SemanticSnapshot(_cache);
        var references = footprint.References.Where(item => item.TargetForm == _form.Guid).ToArray();

        Assert.Equal(before, after);
        Assert.NotEqual(censusBefore, retargetDigest);
        Assert.Contains(references, item => item.SourceClass == "MoDeriv" && item.Field == "StemForm");
        Assert.Contains(references, item => item.SourceClass == "MoCompoundRuleApp" && item.Field == "LeftForm");
        Assert.Contains(references, item => item.SourceClass == "MoCompoundRuleApp" && item.Field == "RightForm");
        Assert.All(customFields, field => Assert.Contains(references,
            item => item.IsCustom && item.Field == metadata.GetFieldName(field)));
        Assert.Equal(3, references.Count(item => item.Kind == "bundle-morph"));
        Assert.Equal(1, footprint.Counts.ApprovedBundles);
        Assert.Equal(1, footprint.Counts.DisapprovedBundles);
        Assert.Equal(1, footprint.Counts.UnknownBundles);
        Assert.Equal(1, footprint.Counts.ApprovedAnalyses);
        Assert.Equal(1, footprint.Counts.DisapprovedAnalyses);
        Assert.Equal(1, footprint.Counts.UnknownAnalyses);
        var affected = Assert.Single(footprint.AffectedWordforms, item => item.Wordform == wordform.Guid);
        Assert.Contains(affected.Analyses, item => item.Morphs.All(morph => morph.Form != _form.Guid));
        Assert.Contains(affected.TextUses, item => item.Segment == segment.Guid && item.Kind == "wordform");
        Assert.Contains(affected.TextUses, item => item.Segment == segment.Guid && item.Kind == "analysis");
        Assert.Contains(affected.TextUses, item => item.Segment == segment.Guid && item.Kind == "gloss");
        Assert.Contains(affected.Forms, item => item.WritingSystem == SeededProject.RightToLeftTag &&
            item.Text == "motif-analysed-ar");
        Assert.Contains(affected.Analyses.SelectMany(item => item.Morphs).SelectMany(item => item.Text),
            item => item.WritingSystem == SeededProject.RightToLeftTag && item.Text == "motif-alternate-ar");
        Assert.StartsWith("sha256:", retargetDigest);
        Assert.Empty(footprint.Unavailable);
        Assert.All(customFields, field => Assert.Contains(footprint.RouteNotes,
            item => item.DeclaringClass == "LexEntry" && item.Field == metadata.GetFieldName(field) &&
                item.Reason.Contains("runtime metadata field is scanned", StringComparison.Ordinal)));
        var diagnostics = AllomorphReferenceFootprintReader.Diagnose(footprint,
            new SIL.Motif.Contract.Retirement.RetireAllomorphIntent("stem",
                SIL.Motif.Contract.Retirement.AllomorphRetirementScope.Stem, [], [], [], []));
        Assert.False(diagnostics.IsComplete);
        Assert.Contains(diagnostics.UnresolvedReferences, item => item.SourceClass == "MoDeriv" && item.Field == "StemForm");
        Assert.Contains(diagnostics.UnresolvedReferences, item => item.SourceClass == "MoCompoundRuleApp" && item.Field == "LeftForm");
        Assert.Equal(references.Length, diagnostics.Classifications.Count);
        Assert.All(diagnostics.Classifications, item => Assert.False(string.IsNullOrWhiteSpace(item.Reason)));
        Assert.Empty(diagnostics.Unavailable);
    }

    [Fact]
    public void ContextDigestBindsStemNameRegionsAndFeatureDefinitionContent()
    {
        AddStemNameRegion();
        var first = ReadContext();
        var same = ReadContext();
        Assert.Equal(first.Digest, same.Digest);
        Assert.Empty(first.Unavailable);

        var region = _stemName.RegionsOC.Last();
        var specification = region.FeatureSpecsOC.Single();
        var feature = Assert.IsAssignableFrom<IFsClosedFeature>(specification.FeatureRA);
        IFsSymFeatVal additional = null!;
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            additional = _cache.ServiceLocator.GetInstance<IFsSymFeatValFactory>().Create();
            feature.ValuesOC.Add(additional);
            additional.Name.set_String(_cache.DefaultAnalWs, "additional");
        });

        var changed = ReadContext();

        Assert.NotEqual(first.Digest, changed.Digest);
        Assert.Contains(changed.Evidence, item => item.Contains(feature.Guid.ToString("D"), StringComparison.Ordinal));
    }

    [Fact]
    public void ContextDigestBindsSameChangedAndNullStemNameAndFormGates()
    {
        var secondName = _cache.ServiceLocator.GetInstance<IMoStemNameFactory>().Create();
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            _partOfSpeech.StemNamesOC.Add(secondName);
            secondName.Name.set_String(_cache.DefaultAnalWs, "future");
        });
        var sameName = ReadContext();
        Assert.Equal(sameName.Digest, ReadContext().Digest);

        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () => _form.StemNameRA = secondName);
        var changedName = ReadContext();
        Assert.NotEqual(sameName.Digest, changedName.Digest);

        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () => _form.StemNameRA = null);
        var nullName = ReadContext();
        Assert.NotEqual(changedName.Digest, nullName.Digest);

        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () => _form.StemNameRA = _stemName);
        Assert.Equal(sameName.Digest, ReadContext().Digest);

        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () => _form.IsAbstract = true);
        var abstractForm = ReadContext();
        Assert.NotEqual(sameName.Digest, abstractForm.Digest);

        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
            _form.MorphTypeRA = _cache.ServiceLocator.GetInstance<IMoMorphTypeRepository>()
                .GetObject(MoMorphTypeTags.kguidMorphStem));
        var stemType = ReadContext();
        Assert.NotEqual(abstractForm.Digest, stemType.Digest);

        var secondEntry = _cache.ServiceLocator.GetInstance<ILexEntryRepository>().GetObject(_seed.SecondEntryId);
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () => secondEntry.AlternateFormsOS.Add(_form));
        var movedForm = ReadContext();
        Assert.NotEqual(stemType.Digest, movedForm.Digest);
    }

    [Fact]
    public void ContextDigestBindsEnvironmentMembersAndPhonemeContentBehindStableIds()
    {
        var environment = _cache.ServiceLocator.GetInstance<IPhEnvironmentFactory>().Create();
        var naturalClass = _cache.ServiceLocator.GetInstance<IPhNCSegmentsFactory>().Create();
        var context = _cache.ServiceLocator.GetInstance<IPhSimpleContextNCFactory>().Create();
        var phoneme = _cache.ServiceLocator.GetInstance<IPhPhonemeFactory>().Create();
        var code = _cache.ServiceLocator.GetInstance<IPhCodeFactory>().Create();
        var phonemeSet = _cache.ServiceLocator.GetInstance<IPhPhonemeSetFactory>().Create();
        var rule = _cache.ServiceLocator.GetInstance<IPhRegularRuleFactory>().Create();
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            _cache.LangProject.PhonologicalDataOA.EnvironmentsOS.Add(environment);
            environment.StringRepresentation = TsStringUtils.MakeString("/ a _", _cache.DefaultVernWs);
            _cache.LangProject.PhonologicalDataOA.PhonRulesOS.Add(rule);
            rule.StrucDescOS.Add(context);
            _cache.LangProject.PhonologicalDataOA.NaturalClassesOS.Add(naturalClass);
            _cache.LangProject.PhonologicalDataOA.PhonemeSetsOS.Add(phonemeSet);
            OwnedBoundaryMarkerFixture.EnsureReservedMarkers(_cache, phonemeSet);
            phonemeSet.PhonemesOC.Add(phoneme);
            naturalClass.SegmentsRC.Add(phoneme);
            phoneme.CodesOS.Add(code);
            code.Representation.set_String(_cache.DefaultVernWs, "a");
            context.FeatureStructureRA = naturalClass;
            environment.LeftContextRA = context;
            _form.PhoneEnvRC.Add(environment);
        });

        var first = ReadContext();
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
            environment.StringRepresentation = TsStringUtils.MakeString("/ e _", _cache.DefaultVernWs));
        var changedRepresentation = ReadContext();
        Assert.NotEqual(first.Digest, changedRepresentation.Digest);

        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
            code.Representation.set_String(_cache.DefaultVernWs, "e"));
        var changedPhoneme = ReadContext();
        Assert.NotEqual(changedRepresentation.Digest, changedPhoneme.Digest);

        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
            naturalClass.SegmentsRC.Remove(phoneme));
        var changedMembers = ReadContext();
        Assert.NotEqual(changedPhoneme.Digest, changedMembers.Digest);
    }

    [Fact]
    public void ContextDigestTracksFromStemNameConsumersMsaAncestryAndEntryReferences()
    {
        var secondName = _cache.ServiceLocator.GetInstance<IMoStemNameFactory>().Create();
        var consumer = _cache.ServiceLocator.GetInstance<IMoDerivAffMsaFactory>().Create();
        var reference = _cache.ServiceLocator.GetInstance<ILexEntryRefFactory>().Create();
        var outgoingReference = _cache.ServiceLocator.GetInstance<ILexEntryRefFactory>().Create();
        var variantType = _cache.ServiceLocator.GetInstance<ILexEntryTypeFactory>().Create();
        var complexType = _cache.ServiceLocator.GetInstance<ILexEntryTypeFactory>().Create();
        var otherEntry = _cache.ServiceLocator.GetInstance<ILexEntryRepository>().GetObject(_seed.SecondEntryId);
        ILexEntry outgoingTarget = null!;
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            outgoingTarget = _cache.ServiceLocator.GetInstance<ILexEntryFactory>().Create();
            _partOfSpeech.StemNamesOC.Add(secondName);
            secondName.Name.set_String(_cache.DefaultAnalWs, "future");
            _entry.MorphoSyntaxAnalysesOC.Add(consumer);
            consumer.FromStemNameRA = _stemName;
            _cache.LangProject.LexDbOA.VariantEntryTypesOA.PossibilitiesOS.Add(variantType);
            _cache.LangProject.LexDbOA.ComplexEntryTypesOA.PossibilitiesOS.Add(complexType);
            variantType.Name.set_String(_cache.DefaultAnalWs, "variant");
            complexType.Name.set_String(_cache.DefaultAnalWs, "compound");
            otherEntry.EntryRefsOS.Add(reference);
            reference.ComponentLexemesRS.Add(_entry);
            reference.VariantEntryTypesRS.Add(variantType);
            reference.ComplexEntryTypesRS.Add(complexType);
            _entry.EntryRefsOS.Add(outgoingReference);
            outgoingReference.ComponentLexemesRS.Add(outgoingTarget);
        });

        var first = ReadContext();
        Assert.Contains(first.Evidence, item => item.Contains("variant-entry-type:0", StringComparison.Ordinal));
        Assert.Contains(first.Evidence, item => item.Contains("complex-entry-type:0", StringComparison.Ordinal));
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
            variantType.Name.set_String(_cache.DefaultAnalWs, "changed variant"));
        var changedVariant = ReadContext();
        Assert.NotEqual(first.Digest, changedVariant.Digest);
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
            consumer.FromStemNameRA = secondName);
        var changedConsumer = ReadContext();
        Assert.NotEqual(changedVariant.Digest, changedConsumer.Digest);

        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            reference.ComponentLexemesRS.Clear();
            reference.ComponentLexemesRS.Add(_entry.SensesOS[0]);
        });
        var changedComponent = ReadContext();
        Assert.NotEqual(changedConsumer.Digest, changedComponent.Digest);

        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            reference.ComponentLexemesRS.Add(_entry);
        });
        var orderedComponents = ReadContext();
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            reference.ComponentLexemesRS.Clear();
            reference.ComponentLexemesRS.Add(_entry);
            reference.ComponentLexemesRS.Add(_entry.SensesOS[0]);
        });
        var reorderedComponents = ReadContext();
        Assert.NotEqual(orderedComponents.Digest, reorderedComponents.Digest);

        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
            _entry.MorphoSyntaxAnalysesOC.OfType<IMoStemMsa>().Single().PartOfSpeechRA = null);
        var changedMsa = ReadContext();
        Assert.NotEqual(reorderedComponents.Digest, changedMsa.Digest);
    }

    [Fact]
    public void ContextDigestBindsSiblingSetAndIgnoresRequestedFormOrder()
    {
        AddStemNameRegion();
        var oneSibling = ReadContext([_form.Guid]);
        var secondAlternate = _cache.ServiceLocator.GetInstance<IMoStemAllomorphFactory>().Create();
        var secondRegion = _cache.ServiceLocator.GetInstance<IFsFeatStrucFactory>().Create();
        var secondFeature = _cache.ServiceLocator.GetInstance<IFsClosedFeatureFactory>().Create();
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            _entry.AlternateFormsOS.Add(secondAlternate);
            secondAlternate.MorphTypeRA = _form.MorphTypeRA;
            secondAlternate.Form.set_String(_cache.DefaultVernWs, "motif-second");
            secondAlternate.StemNameRA = _stemName;
            _stemName.RegionsOC.Add(secondRegion);
            _cache.LangProject.MsFeatureSystemOA.FeaturesOC.Add(secondFeature);
            secondFeature.Name.set_String(_cache.DefaultAnalWs, "second-feature");
            var spec = _cache.ServiceLocator.GetInstance<IFsClosedValueFactory>().Create();
            secondRegion.FeatureSpecsOC.Add(spec);
            spec.FeatureRA = secondFeature;
        });

        var initial = ReadContext([_form.Guid, secondAlternate.Guid]);
        var requestedPermutation = ReadContext([secondAlternate.Guid, _form.Guid]);
        Assert.NotEqual(oneSibling.Digest, initial.Digest);
        Assert.Equal(initial.Digest, requestedPermutation.Digest);
        Assert.Contains(initial.Evidence, item => item.Contains("alternate-form:0", StringComparison.Ordinal));
        Assert.Contains(initial.Evidence, item => item.Contains("alternate-form:1", StringComparison.Ordinal));

    }

    [Fact]
    public void CensusDigestIgnoresReferenceCollectionPermutationButPreservesReferenceSequenceOrder()
    {
        var secondAlternate = _cache.ServiceLocator.GetInstance<IMoStemAllomorphFactory>().Create();
        var metadata = (IFwMetaDataCacheManaged)_cache.MetaDataCacheAccessor;
        var collectionField = AddCustomVectorReference(metadata, CellarPropertyType.ReferenceCollection, "Unordered");
        var sequenceField = AddCustomVectorReference(metadata, CellarPropertyType.ReferenceSequence, "Ordered");
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            _entry.AlternateFormsOS.Add(secondAlternate);
            secondAlternate.MorphTypeRA = _form.MorphTypeRA;
            secondAlternate.Form.set_String(_cache.DefaultVernWs, "motif-second");
            secondAlternate.StemNameRA = _stemName;
            _cache.DomainDataByFlid.Replace(_entry.Hvo, collectionField, 0, 0,
                [_form.Hvo, secondAlternate.Hvo], 2);
            _cache.DomainDataByFlid.Replace(_entry.Hvo, sequenceField, 0, 0,
                [_form.Hvo, secondAlternate.Hvo], 2);
        });
        var initial = AllomorphRetargetCensusReader.ComputeDigest(_cache, [_form.Guid, secondAlternate.Guid]);

        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            _cache.DomainDataByFlid.Replace(_entry.Hvo, collectionField, 0, 2,
                [secondAlternate.Hvo, _form.Hvo], 2);
            _cache.DomainDataByFlid.Replace(_entry.Hvo, sequenceField, 0, 2,
                [secondAlternate.Hvo, _form.Hvo], 2);
        });
        var reordered = AllomorphRetargetCensusReader.ComputeDigest(_cache, [_form.Guid, secondAlternate.Guid]);
        Assert.NotEqual(initial, reordered);

        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
            _cache.DomainDataByFlid.Replace(_entry.Hvo, sequenceField, 0, 2,
                [_form.Hvo, secondAlternate.Hvo], 2));
        var collectionOnlyPermutation = AllomorphRetargetCensusReader.ComputeDigest(_cache,
            [_form.Guid, secondAlternate.Guid]);
        Assert.Equal(initial, collectionOnlyPermutation);
    }

    [Fact]
    public void ContextDigestIgnoresPhoneEnvironmentCollectionPermutation()
    {
        var firstEnvironment = _cache.ServiceLocator.GetInstance<IPhEnvironmentFactory>().Create();
        var secondEnvironment = _cache.ServiceLocator.GetInstance<IPhEnvironmentFactory>().Create();
        var field = ((IFwMetaDataCacheManaged)_cache.MetaDataCacheAccessor).GetFieldIds()
            .Single(id => ((IFwMetaDataCacheManaged)_cache.MetaDataCacheAccessor).GetOwnClsName(id) == "MoStemAllomorph" &&
                ((IFwMetaDataCacheManaged)_cache.MetaDataCacheAccessor).GetFieldName(id) == "PhoneEnv");
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            _cache.LangProject.PhonologicalDataOA.EnvironmentsOS.Add(firstEnvironment);
            _cache.LangProject.PhonologicalDataOA.EnvironmentsOS.Add(secondEnvironment);
            firstEnvironment.StringRepresentation = TsStringUtils.MakeString("/ a _", _cache.DefaultVernWs);
            secondEnvironment.StringRepresentation = TsStringUtils.MakeString("/ e _", _cache.DefaultVernWs);
            _form.PhoneEnvRC.Add(firstEnvironment);
            _form.PhoneEnvRC.Add(secondEnvironment);
        });
        var initial = ReadContext();
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
            _cache.DomainDataByFlid.Replace(_form.Hvo, field, 0, 2,
                [secondEnvironment.Hvo, firstEnvironment.Hvo], 2));
        var reordered = ReadContext();
        Assert.Equal(initial.Digest, reordered.Digest);
    }

    [Fact]
    public void ContextDigestBindsStemMsaFeaturesAndInflectionClassDefaults()
    {
        var msa = _entry.MorphoSyntaxAnalysesOC.OfType<IMoStemMsa>().Single();
        var inflectionClass = _cache.ServiceLocator.GetInstance<IMoInflClassFactory>().Create();
        var classStemName = _cache.ServiceLocator.GetInstance<IMoStemNameFactory>().Create();
        var feature = _cache.ServiceLocator.GetInstance<IFsClosedFeatureFactory>().Create();
        var value = _cache.ServiceLocator.GetInstance<IFsSymFeatValFactory>().Create();
        var structure = _cache.ServiceLocator.GetInstance<IFsFeatStrucFactory>().Create();
        var specification = _cache.ServiceLocator.GetInstance<IFsClosedValueFactory>().Create();
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            _partOfSpeech.InflectionClassesOC.Add(inflectionClass);
            inflectionClass.Name.set_String(_cache.DefaultAnalWs, "class-one");
            inflectionClass.StemNamesOC.Add(classStemName);
            classStemName.Name.set_String(_cache.DefaultAnalWs, "class-stem");
            _partOfSpeech.DefaultInflectionClassRA = inflectionClass;
            msa.InflectionClassRA = inflectionClass;
            _cache.LangProject.MsFeatureSystemOA.FeaturesOC.Add(feature);
            feature.Name.set_String(_cache.DefaultAnalWs, "number");
            feature.ValuesOC.Add(value);
            value.Name.set_String(_cache.DefaultAnalWs, "singular");
            _partOfSpeech.DefaultFeaturesOA = structure;
            structure.FeatureSpecsOC.Add(specification);
            specification.FeatureRA = feature;
            specification.ValueRA = value;
        });

        var initial = ReadContext();
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
            value.Name.set_String(_cache.DefaultAnalWs, "plural"));
        var changedValue = ReadContext();
        Assert.NotEqual(initial.Digest, changedValue.Digest);

        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
            inflectionClass.Name.set_String(_cache.DefaultAnalWs, "class-two"));
        var changedClass = ReadContext();
        Assert.NotEqual(changedValue.Digest, changedClass.Digest);

        var secondStemName = _cache.ServiceLocator.GetInstance<IMoStemNameFactory>().Create();
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            inflectionClass.StemNamesOC.Add(secondStemName);
            secondStemName.Name.set_String(_cache.DefaultAnalWs, "second-class-stem");
        });
        var changedClassStemNames = ReadContext();
        Assert.NotEqual(changedClass.Digest, changedClassStemNames.Digest);
    }

    private void AddStemNameRegion()
    {
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            var feature = _cache.ServiceLocator.GetInstance<IFsClosedFeatureFactory>().Create();
            _cache.LangProject.MsFeatureSystemOA.FeaturesOC.Add(feature);
            feature.Name.set_String(_cache.DefaultAnalWs, "tense");
            var value = _cache.ServiceLocator.GetInstance<IFsSymFeatValFactory>().Create();
            feature.ValuesOC.Add(value);
            value.Name.set_String(_cache.DefaultAnalWs, "past");
            var region = _cache.ServiceLocator.GetInstance<IFsFeatStrucFactory>().Create();
            _stemName.RegionsOC.Add(region);
            var specification = _cache.ServiceLocator.GetInstance<IFsClosedValueFactory>().Create();
            region.FeatureSpecsOC.Add(specification);
            specification.FeatureRA = feature;
            specification.ValueRA = value;
        });
    }

    private StemAllomorphSelectionContext ReadContext(IEnumerable<Guid>? forms = null) =>
        StemAllomorphSelectionContextReader.Read(_cache, forms ?? [_form.Guid]);

    private static int AddCustomReference(IFwMetaDataCacheManaged metadata, string destinationClass)
    {
        var classId = metadata.GetClassIds().Single(id => metadata.GetClassName(id) == destinationClass);
        return metadata.AddCustomField("LexEntry", "StemFootprint" + destinationClass,
            CellarPropertyType.ReferenceAtomic, classId);
    }

    private static int AddCustomVectorReference(IFwMetaDataCacheManaged metadata, CellarPropertyType type, string suffix)
    {
        var classId = metadata.GetClassIds().Single(id => metadata.GetClassName(id) == "MoStemAllomorph");
        return metadata.AddCustomField("LexEntry", "StemFootprint" + suffix, type, classId);
    }

    private static IWfiAnalysis AddReading(LcmCache cache, IWfiWordform wordform,
        IMoMorphSynAnalysis msa, IMoForm form)
    {
        var analysis = cache.ServiceLocator.GetInstance<IWfiAnalysisFactory>().Create();
        wordform.AnalysesOC.Add(analysis);
        var bundle = cache.ServiceLocator.GetInstance<IWfiMorphBundleFactory>().Create();
        analysis.MorphBundlesOS.Add(bundle);
        bundle.MorphRA = form;
        bundle.MsaRA = msa;
        return analysis;
    }

    private static string SemanticSnapshot(LcmCache cache)
    {
        var snapshotters = typeof(AllomorphRetargetCensusReader).Assembly.GetTypes()
            .SelectMany(type => type.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
            .Where(method => method.Name == "Snapshot" && method.ReturnType == typeof(ObjectSnapshot))
            .Where(method => method.GetParameters() is { Length: 2 } parameters &&
                parameters[0].ParameterType == typeof(LcmCache))
            .ToArray();
        var result = new List<string>();
        foreach (var item in cache.ServiceLocator.GetInstance<ICmObjectRepository>().AllInstances())
        {
            foreach (var method in snapshotters.Where(candidate => candidate.GetParameters()[1].ParameterType.IsInstanceOfType(item)))
            {
                var snapshot = (ObjectSnapshot)method.Invoke(null, [cache, item])!;
                result.AddRange(snapshot.AlternativesFields.OrderBy(field => field.Key, StringComparer.Ordinal)
                    .SelectMany(field => field.Value.OrderBy(value => value.Key, StringComparer.Ordinal)
                        .Select(value => $"{item.Guid:D}|{field.Key}|{value.Key}|{value.Value}")));
            }
        }
        return string.Join('\n', result.Order(StringComparer.Ordinal));
    }
}
