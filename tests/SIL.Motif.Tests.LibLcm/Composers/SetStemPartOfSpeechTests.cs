using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Tests.TestFixtures;
using Xunit;
using static SIL.Motif.Tests.Composers.AffixTestSupport;

namespace SIL.Motif.Tests.Composers;

/// <summary>Moving a stem to a sub-category keeps the class and features the sub-category inherits.</summary>
[Collection(LcmCacheParallelCollections.Group3)]
public sealed class SetStemPartOfSpeechTests : IDisposable
{
    private readonly LcmCache _cache;
    public SetStemPartOfSpeechTests(PristineProjectFixture pristine) => _cache = pristine.NewScratch();
    public void Dispose() { if (!_cache.IsDisposed) _cache.Dispose(); }

    private IPartOfSpeech Sub(IPartOfSpeech parent, string name)
    {
        IPartOfSpeech child = null!;
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            child = _cache.ServiceLocator.GetInstance<IPartOfSpeechFactory>().Create();
            parent.SubPossibilitiesOS.Add(child);
            child.Name.set_String(_cache.DefaultAnalWs, name);
        });
        return child;
    }

    private IFsClosedValue Select(IMoStemMsa msa, IPartOfSpeech? bearer)
    {
        IFsClosedValue spec = null!;
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            var services = _cache.ServiceLocator;
            var feature = services.GetInstance<IFsClosedFeatureFactory>().Create();
            _cache.LangProject.MsFeatureSystemOA.FeaturesOC.Add(feature);
            feature.Name.set_String(_cache.DefaultAnalWs, "gen");
            var symbol = services.GetInstance<IFsSymFeatValFactory>().Create();
            feature.ValuesOC.Add(symbol);
            symbol.Name.set_String(_cache.DefaultAnalWs, "g9/10");
            bearer?.InflectableFeatsRC.Add(feature);
            var structure = services.GetInstance<IFsFeatStrucFactory>().Create();
            msa.MsFeaturesOA = structure;
            spec = services.GetInstance<IFsClosedValueFactory>().Create();
            structure.FeatureSpecsOC.Add(spec);
            spec.FeatureRA = feature;
            spec.ValueRA = symbol;
        });
        return spec;
    }

    [Fact]
    public void AMoveToASubCategoryKeepsTheInheritedFeaturesAndClass()
    {
        var noun = AddCategory(_cache, "Noun");
        var nounPos = Get<IPartOfSpeech>(_cache, noun);
        var kin = Sub(nounPos, "Kin");
        var cls = AddInflectionClass(_cache, noun, "G1/2");
        var (_, msaId, _) = AddStem(_cache, "baba", noun, cls);
        var msa = Get<IMoStemMsa>(_cache, msaId);
        var spec = Select(msa, nounPos);

        ApplyOps(_cache, SetStemPartOfSpeechComposer.Build(_cache, new(msaId, noun, Id(kin))));

        Assert.Equal(kin.Guid, msa.PartOfSpeechRA!.Guid);
        Assert.Equal(cls.ToGuid(), msa.InflectionClassRA!.Guid);
        Assert.NotNull(msa.MsFeaturesOA);
        Assert.Equal(spec.Guid, Assert.Single(msa.MsFeaturesOA!.FeatureSpecsOC).Guid);
    }

    [Fact]
    public void AFeatureNoCategoryBearsIsDroppedByLibLcmAndTheDraftSaysSo()
    {
        var noun = AddCategory(_cache, "Noun");
        var kin = Sub(Get<IPartOfSpeech>(_cache, noun), "Kin");
        var (_, msaId, _) = AddStem(_cache, "baba", noun);
        var msa = Get<IMoStemMsa>(_cache, msaId);
        Select(msa, null);

        var operation = Assert.Single(SetStemPartOfSpeechComposer.Build(_cache, new(msaId, noun, Id(kin))));
        Assert.Contains("Warning: the move drops feature gen", operation.Rationale!, StringComparison.Ordinal);
        ApplyOps(_cache, [operation]);

        Assert.Equal(kin.Guid, msa.PartOfSpeechRA!.Guid);
        Assert.Null(msa.MsFeaturesOA);
    }

    [Fact]
    public void AMoveOutOfTheClassCategoryWarnsAndLibLcmClearsTheClass()
    {
        var noun = AddCategory(_cache, "Noun");
        var verb = AddCategory(_cache, "Verb");
        var cls = AddInflectionClass(_cache, noun, "G1/2");
        var (_, msaId, _) = AddStem(_cache, "baba", noun, cls);
        var msa = Get<IMoStemMsa>(_cache, msaId);

        var operation = Assert.Single(SetStemPartOfSpeechComposer.Build(_cache, new(msaId, noun, verb)));
        Assert.Contains("Warning: the move drops inflection class", operation.Rationale!, StringComparison.Ordinal);
        ApplyOps(_cache, [operation]);

        Assert.Equal(verb.ToGuid(), msa.PartOfSpeechRA!.Guid);
        Assert.Null(msa.InflectionClassRA);
    }

    [Fact]
    public void AMoveToASubCategoryWarnsOfNothingWhenEverythingIsInherited()
    {
        var noun = AddCategory(_cache, "Noun");
        var nounPos = Get<IPartOfSpeech>(_cache, noun);
        var kin = Sub(nounPos, "Kin");
        var cls = AddInflectionClass(_cache, noun, "G1/2");
        var (_, msaId, _) = AddStem(_cache, "baba", noun, cls);
        Select(Get<IMoStemMsa>(_cache, msaId), nounPos);

        var operation = Assert.Single(SetStemPartOfSpeechComposer.Build(_cache, new(msaId, noun, Id(kin))));
        Assert.Equal(SetStemPartOfSpeechComposer.OperationRationale, operation.Rationale);
    }

    [Fact]
    public void AMoveDropsANestedClassBecauseLibLcmHoldsOnlyTopLevelClassesAndSaysSo()
    {
        var noun = AddCategory(_cache, "Noun");
        var kin = Sub(Get<IPartOfSpeech>(_cache, noun), "Kin");
        var parent = AddInflectionClass(_cache, noun, "G1/2");
        IMoInflClass nested = null!;
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            nested = _cache.ServiceLocator.GetInstance<IMoInflClassFactory>().Create();
            Get<IMoInflClass>(_cache, parent).SubclassesOC.Add(nested);
            nested.Name.set_String(_cache.DefaultAnalWs, "G1/2 m-V");
        });
        var (_, msaId, _) = AddStem(_cache, "baba", noun);
        var msa = Get<IMoStemMsa>(_cache, msaId);
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () => msa.InflectionClassRA = nested);

        var operation = Assert.Single(SetStemPartOfSpeechComposer.Build(_cache, new(msaId, noun, Id(kin))));
        Assert.Contains("Warning: the move drops inflection class", operation.Rationale!, StringComparison.Ordinal);
        Assert.Contains("moving the stem back does not restore them", operation.Rationale!, StringComparison.Ordinal);
        ApplyOps(_cache, [operation]);

        Assert.Null(msa.InflectionClassRA);
    }
}
