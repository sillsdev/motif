using SIL.LCModel;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Retirement;
using SIL.Motif.Projection.Retirement;
using SIL.Motif.Runner.Apply;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Runner.Retirement;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Retirement;

[Collection(LcmCacheParallelCollections.Group1)]
public sealed class StemAlternateRetirementTests : IDisposable
{
    private readonly LcmCache _cache;
    private readonly ILexEntry _entry;
    private readonly IMoStemAllomorph _retired;
    private readonly IMoStemAllomorph _survivor;

    public StemAlternateRetirementTests(PristineProjectFixture pristine)
    {
        _cache = pristine.NewScratch();
        _entry = _cache.ServiceLocator.GetInstance<ILexEntryRepository>().GetObject(pristine.Seed.FirstEntryId);
        IMoStemAllomorph retired = null!, survivor = null!;
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            var stem = _cache.ServiceLocator.GetInstance<IMoMorphTypeRepository>().GetObject(MoMorphTypeTags.kguidMorphStem);
            retired = AddAlternate(stem, "kala");
            survivor = AddAlternate(stem, "kala");
        });
        _retired = retired;
        _survivor = survivor;
    }

    public void Dispose()
    {
        if (!_cache.IsDisposed) _cache.Dispose();
    }

    [Fact]
    public void ADuplicateStemAlternateIsRetiredAndTheSurvivorAndLexemeFormStay()
    {
        var lexeme = _entry.LexemeFormOA;
        var operations = RetireAllomorphComposer.Build(_cache, Document(_retired, _survivor), NewId);
        var deletion = Assert.Single(operations);
        Assert.Equal(AlternateFormRetirementOperationKinds.DeleteAlternateForm, deletion.Kind);
        var proposal = new Proposal(new Dictionary<string, string> { ["lexical"] = "1.0" }, CanonicalId.Mint(), null, operations);
        var dryRun = ScratchDryRun.Of(_cache, proposal);

        Assert.Contains(dryRun.ExpectedEffects, effect => effect.Field == "lexical/lexEntry/alternateForms");
        Assert.Contains(_retired, _entry.AlternateFormsOS);

        ProposalApplier.Apply(_cache, proposal, dryRun.Anchor, "retirement-tests");

        Assert.DoesNotContain(_retired.Guid, _entry.AlternateFormsOS.Select(item => item.Guid));
        Assert.Equal(_survivor.Guid, Assert.Single(_entry.AlternateFormsOS).Guid);
        Assert.Equal(lexeme.Guid, _entry.LexemeFormOA.Guid);
        Assert.False(_cache.ServiceLocator.ObjectRepository.TryGetObject(_retired.Guid, out _));
    }

    [Fact]
    public void ALexemeFormMayBeTheSurvivor()
    {
        IMoStemAllomorph lexeme = null!;
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            lexeme = _cache.ServiceLocator.GetInstance<IMoStemAllomorphFactory>().Create();
            _entry.LexemeFormOA = lexeme;
            lexeme.MorphTypeRA = _retired.MorphTypeRA;
            lexeme.Form.set_String(_cache.DefaultVernWs, "kala");
        });

        var operations = RetireAllomorphComposer.Build(_cache, Document(_retired, lexeme, "lexeme"), NewId);

        Assert.Single(operations);
    }

    [Fact]
    public void AStemAlternateThatIsNotAnExactDuplicateIsRefused()
    {
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
            _survivor.Form.set_String(_cache.DefaultVernWs, "kalu"));

        var refusal = Assert.Throws<InvalidOperationException>(() =>
            RetireAllomorphComposer.Build(_cache, Document(_retired, _survivor), NewId));

        Assert.Contains("exact surviving duplicate", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ARetiredStemWithAnotherStemNameIsNotADuplicate()
    {
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            var name = _cache.ServiceLocator.GetInstance<IMoStemNameFactory>().Create();
            var pos = _cache.ServiceLocator.GetInstance<IPartOfSpeechFactory>().Create();
            _cache.LangProject.PartsOfSpeechOA ??= _cache.ServiceLocator.GetInstance<ICmPossibilityListFactory>().Create();
            _cache.LangProject.PartsOfSpeechOA.PossibilitiesOS.Add(pos);
            pos.StemNamesOC.Add(name);
            _survivor.StemNameRA = name;
        });

        var refusal = Assert.Throws<InvalidOperationException>(() =>
            RetireAllomorphComposer.Build(_cache, Document(_retired, _survivor), NewId));

        Assert.Contains("exact surviving duplicate", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AStemAlternateThatAnAnalysisRefersToIsRefusedAndNothingIsStaged()
    {
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            var wordform = _cache.ServiceLocator.GetInstance<IWfiWordformFactory>()
                .Create(SIL.LCModel.Core.Text.TsStringUtils.MakeString("kala", _cache.DefaultVernWs));
            var analysis = _cache.ServiceLocator.GetInstance<IWfiAnalysisFactory>().Create();
            wordform.AnalysesOC.Add(analysis);
            var bundle = _cache.ServiceLocator.GetInstance<IWfiMorphBundleFactory>().Create();
            analysis.MorphBundlesOS.Add(bundle);
            bundle.MorphRA = _retired;
        });

        var refusal = Assert.Throws<InvalidOperationException>(() =>
            RetireAllomorphComposer.Build(_cache, Document(_retired, _survivor), NewId));

        Assert.Contains("only an unreferenced stem alternate", refusal.Message, StringComparison.Ordinal);
        Assert.Contains(_retired, _entry.AlternateFormsOS);
    }

    [Fact]
    public void AReferenceAddedAfterComposingMakesApplyRefuseAndKeepTheStem()
    {
        var operations = RetireAllomorphComposer.Build(_cache, Document(_retired, _survivor), NewId);
        var proposal = new Proposal(new Dictionary<string, string> { ["lexical"] = "1.0" }, CanonicalId.Mint(), null, operations);
        var dryRun = ScratchDryRun.Of(_cache, proposal);
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            var wordform = _cache.ServiceLocator.GetInstance<IWfiWordformFactory>()
                .Create(SIL.LCModel.Core.Text.TsStringUtils.MakeString("kala", _cache.DefaultVernWs));
            var analysis = _cache.ServiceLocator.GetInstance<IWfiAnalysisFactory>().Create();
            wordform.AnalysesOC.Add(analysis);
            var bundle = _cache.ServiceLocator.GetInstance<IWfiMorphBundleFactory>().Create();
            analysis.MorphBundlesOS.Add(bundle);
            bundle.MorphRA = _retired;
        });

        var refusal = Assert.ThrowsAny<Exception>(() => ProposalApplier.Apply(_cache, proposal, dryRun.Anchor, "retirement-tests"));

        Assert.Contains("still has an incoming reference", refusal.ToString(), StringComparison.Ordinal);

        Assert.Contains(_retired, _entry.AlternateFormsOS);
        Assert.True(_cache.ServiceLocator.ObjectRepository.TryGetObject(_retired.Guid, out _));
    }

    [Fact]
    public void TheLastStemFormOfAnEntryCannotBeRetiredBecauseASurvivorIsRequired()
    {
        var alone = new RetireAllomorphIntentDocument(Intent(_retired), []);

        Assert.Throws<FormatException>(() => RetireAllomorphCodec.Parse(RetireAllomorphCodec.ToJson(alone)));
        var onlyItself = new RetireAllomorphIntentDocument(Intent(_retired), [Identity(_retired, "alternate")]);
        Assert.Throws<InvalidOperationException>(() => RetireAllomorphComposer.Build(_cache, onlyItself, NewId));
        Assert.Contains(_retired, _entry.AlternateFormsOS);
    }

    [Fact]
    public void ARuleReplacementOfAStemIsStillRefused()
    {
        var witness = new RuleReplacementWitness(CanonicalId.Mint(), CanonicalId.Mint(), CanonicalId.Mint());

        var refusal = Assert.Throws<InvalidOperationException>(() =>
            RetireAllomorphComposer.Build(_cache, Document(_retired, _survivor), NewId, witness));

        Assert.Equal(AllomorphRetirementCapabilities.StemRefusal, refusal.Message);
    }

    private IMoStemAllomorph AddAlternate(IMoMorphType type, string form)
    {
        var allomorph = _cache.ServiceLocator.GetInstance<IMoStemAllomorphFactory>().Create();
        _entry.AlternateFormsOS.Add(allomorph);
        allomorph.MorphTypeRA = type;
        allomorph.Form.set_String(_cache.DefaultVernWs, form);
        return allomorph;
    }

    private RetireAllomorphIntentDocument Document(IMoStemAllomorph retired, IMoStemAllomorph survivor,
        string location = "alternate") => new(Intent(retired), [Identity(survivor, location)]);

    private RetireAllomorphIntent Intent(IMoStemAllomorph retired) => new(
        CanonicalId.FromGuid(_entry.Guid).Value, AllomorphRetirementScope.Stem, [Identity(retired, "alternate")], [], [], []);

    private AllomorphIdentity Identity(IMoStemAllomorph form, string location) => new(
        CanonicalId.FromGuid(form.Guid).Value, CanonicalId.FromGuid(_entry.Guid).Value, form.ClassName, location, "stem",
        AllomorphRetirementSemanticDigest.Compute(_cache, form),
        form.StemNameRA is { } name ? CanonicalId.FromGuid(name.Guid).Value : null);

    private static CanonicalId NewId() => CanonicalId.Mint();
}
