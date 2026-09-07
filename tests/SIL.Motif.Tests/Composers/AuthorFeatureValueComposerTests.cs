using System;
using System.Collections.Generic;
using System.Linq;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using SIL.LCModel;
using SIL.LCModel.Infrastructure;
using Xunit;

namespace SIL.Motif.Tests.Composers;

/// <summary>
/// Proves <see cref="AuthorFeatureValueComposer.Build"/>'s lowering shape against a real project --
/// the owning/col grammar counterpart to <see cref="AuthorLexemeFormComposerTests"/>'s multi-operation
/// shape, and the composer half of <see cref="FsFeatStrucFeatureSpecsOperationsTests"/> (the operation
/// family itself). The end-to-end dry-run/apply round trip lives in
/// <see cref="AuthorFeatureValueEndToEndTests"/>; this class is about the shape of what
/// <see cref="AuthorFeatureValueComposer.Build"/> returns, not about running it.
/// </summary>
[Collection(TestFixtures.LcmCacheTestCollection.Name)]
public sealed class AuthorFeatureValueComposerTests : IDisposable
{
    private readonly LcmCache _cache;

    public AuthorFeatureValueComposerTests(PristineProjectFixture pristine)
    {
        _cache = pristine.NewScratch();
    }

    public void Dispose()
    {
        if (!_cache.IsDisposed) _cache.Dispose();
    }

    [Fact]
    public void Build_ProducesCreateThenSetFeature_DependingOnTheCreate()
    {
        var featStruc = CreateBareFeatStruc();
        var feature = CreateClosedFeature();
        var intent = new AuthorFeatureValueIntent(featStruc, feature);

        var operations = AuthorFeatureValueComposer.Build(_cache, intent);

        Assert.Equal(2, operations.Count);
        var create = operations[0];
        var setFeature = operations[1];

        Assert.Equal(FsFeatStrucFeatureSpecsOperationKinds.CreateFeatureSpecs, create.Kind);
        Assert.Equal(featStruc, create.Target);
        Assert.NotNull(create.EntityId);
        Assert.Equal("{}", create.After!.Value.GetRawText());
        Assert.Empty(create.DependsOn);

        Assert.Equal(FsFeatureSpecificationFeatureOperationKinds.SetFeature, setFeature.Kind);
        Assert.Equal(create.EntityId, setFeature.Target); // targets the spec the create proposes
        Assert.Equal(feature.Value, setFeature.After!.Value.GetProperty("ref").GetString());
        var dependency = Assert.Single(setFeature.DependsOn);
        Assert.Equal(create.OperationId, dependency.OperationId);
    }

    [Fact]
    public void Build_FeatStrucDoesNotExist_ThrowsFailingClosed()
    {
        var intent = new AuthorFeatureValueIntent(CanonicalId.FromGuid(Guid.NewGuid()), CreateClosedFeature());

        Assert.ThrowsAny<Exception>(() => AuthorFeatureValueComposer.Build(_cache, intent));
    }

    [Fact]
    public void Build_FeatureDoesNotExist_ThrowsFailingClosed()
    {
        var intent = new AuthorFeatureValueIntent(CreateBareFeatStruc(), CanonicalId.FromGuid(Guid.NewGuid()));

        Assert.ThrowsAny<Exception>(() => AuthorFeatureValueComposer.Build(_cache, intent));
    }

    [Fact]
    public void Build_FeatStrucResolvesToTheWrongType_ThrowsFailingClosed()
    {
        // A feature's own id, presented where an FsFeatStruc is required.
        var intent = new AuthorFeatureValueIntent(CreateClosedFeature(), CreateClosedFeature());

        var ex = Assert.Throws<InvalidOperationException>(() => AuthorFeatureValueComposer.Build(_cache, intent));
        Assert.Contains("not a FsFeatStruc", ex.Message);
    }

    [Fact]
    public void Build_FeatureResolvesToTheWrongType_ThrowsFailingClosed()
    {
        // The FsFeatStruc's own id, presented where a feature is required.
        var featStruc = CreateBareFeatStruc();
        var intent = new AuthorFeatureValueIntent(featStruc, featStruc);

        var ex = Assert.Throws<InvalidOperationException>(() => AuthorFeatureValueComposer.Build(_cache, intent));
        Assert.Contains("not a FsFeatDefn", ex.Message);
    }

    [Fact]
    public void Build_FeatStrucAlreadyHasASpecForThatFeature_ThrowsFailingClosed_RatherThanDuplicating()
    {
        var featStruc = CreateBareFeatStruc();
        var feature = CreateClosedFeature();
        var intent = new AuthorFeatureValueIntent(featStruc, feature);

        var first = AuthorFeatureValueComposer.Build(_cache, intent);
        Apply(first);

        var ex = Assert.Throws<InvalidOperationException>(() => AuthorFeatureValueComposer.Build(_cache, intent));
        Assert.Contains("already has a feature specification", ex.Message);
    }

    [Fact]
    public void Build_FeatureNotInTheStructsDeclaredType_ThrowsFailingClosed()
    {
        var featStruc = CreateBareFeatStruc();
        var declaredFeature = CreateClosedFeature();
        var otherFeature = CreateClosedFeature();
        SetTypeWithFeatures(featStruc, declaredFeature);

        var intent = new AuthorFeatureValueIntent(featStruc, otherFeature);

        var ex = Assert.Throws<InvalidOperationException>(() => AuthorFeatureValueComposer.Build(_cache, intent));
        Assert.Contains("does not belong", ex.Message);
    }

    [Fact]
    public void Build_FeatureInTheStructsDeclaredType_Succeeds()
    {
        var featStruc = CreateBareFeatStruc();
        var feature = CreateClosedFeature();
        SetTypeWithFeatures(featStruc, feature);

        var intent = new AuthorFeatureValueIntent(featStruc, feature);

        var operations = AuthorFeatureValueComposer.Build(_cache, intent);
        Assert.Equal(2, operations.Count);
    }

    [Fact]
    public void Build_SameIntent_TwiceWithADeterministicIdSource_ProducesIdenticalOperations()
    {
        var featStruc = CreateBareFeatStruc();
        var feature = CreateClosedFeature();
        var intent = new AuthorFeatureValueIntent(featStruc, feature);
        var fixedIds = Enumerable.Range(0, 6).Select(i => CanonicalId.FromGuid(new Guid(i, 0, 0, new byte[8]))).ToArray();

        var first = AuthorFeatureValueComposer.Build(_cache, intent, DeterministicMinter(fixedIds));
        var second = AuthorFeatureValueComposer.Build(_cache, intent, DeterministicMinter(fixedIds));

        Assert.Equal(first.Count, second.Count);
        for (var i = 0; i < first.Count; i++)
        {
            Assert.Equal(first[i].Kind, second[i].Kind);
            Assert.Equal(first[i].OperationId, second[i].OperationId);
            Assert.Equal(first[i].EntityId, second[i].EntityId);
            Assert.Equal(first[i].Target, second[i].Target);
        }
    }

    private static Func<CanonicalId> DeterministicMinter(IReadOnlyList<CanonicalId> ids)
    {
        var queue = new Queue<CanonicalId>(ids);
        return () => queue.Dequeue();
    }

    private CanonicalId CreateBareFeatStruc()
    {
        var actionHandler = _cache.ServiceLocator.GetInstance<SIL.LCModel.Core.KernelInterfaces.IActionHandler>();
        var pos = _cache.ServiceLocator.GetInstance<IPartOfSpeechFactory>().Create();
        Guid featStrucGuid = default;
        UndoableUnitOfWorkHelper.Do("test setup", "test setup", actionHandler, () =>
        {
            _cache.LangProject.PartsOfSpeechOA.PossibilitiesOS.Add(pos);
            var msa = _cache.ServiceLocator.GetInstance<IMoStemMsaFactory>().Create();
            var entry = _cache.ServiceLocator.GetInstance<ILexEntryFactory>().Create();
            entry.MorphoSyntaxAnalysesOC.Add(msa);
            msa.PartOfSpeechRA = pos;
            featStrucGuid = Guid.NewGuid();
            MoStemMsaMsFeaturesCreateLowering.Apply(_cache, msa, featStrucGuid);
        });
        return CanonicalId.FromGuid(featStrucGuid);
    }

    private CanonicalId CreateClosedFeature()
    {
        var actionHandler = _cache.ServiceLocator.GetInstance<SIL.LCModel.Core.KernelInterfaces.IActionHandler>();
        Guid featureGuid = default;
        UndoableUnitOfWorkHelper.Do("test setup", "test setup", actionHandler, () =>
        {
            var feature = _cache.ServiceLocator.GetInstance<IFsClosedFeatureFactory>().Create();
            _cache.LangProject.MsFeatureSystemOA.FeaturesOC.Add(feature);
            featureGuid = feature.Guid;
        });
        return CanonicalId.FromGuid(featureGuid);
    }

    private void SetTypeWithFeatures(CanonicalId featStrucId, params CanonicalId[] features)
    {
        var actionHandler = _cache.ServiceLocator.GetInstance<SIL.LCModel.Core.KernelInterfaces.IActionHandler>();
        var repo = _cache.ServiceLocator.GetInstance<ICmObjectRepository>();
        var featStruc = (IFsFeatStruc)repo.GetObject(featStrucId.ToGuid());
        UndoableUnitOfWorkHelper.Do("test setup", "test setup", actionHandler, () =>
        {
            var type = _cache.ServiceLocator.GetInstance<IFsFeatStrucTypeFactory>().Create();
            _cache.LangProject.MsFeatureSystemOA.TypesOC.Add(type);
            foreach (var feature in features)
                type.FeaturesRS.Add((IFsFeatDefn)repo.GetObject(feature.ToGuid()));
            featStruc.TypeRA = type;
        });
    }

    private void Apply(IReadOnlyList<SIL.Motif.Contract.Model.OperationEnvelope> operations)
    {
        var actionHandler = _cache.ServiceLocator.GetInstance<SIL.LCModel.Core.KernelInterfaces.IActionHandler>();
        var repo = _cache.ServiceLocator.GetInstance<ICmObjectRepository>();
        UndoableUnitOfWorkHelper.Do("test setup", "test setup", actionHandler, () =>
        {
            var create = operations[0];
            var featStruc = (IFsFeatStruc)repo.GetObject(create.Target!.Value.ToGuid());
            var newSpec = FsFeatStrucFeatureSpecsCreateLowering.Apply(_cache, featStruc, create.EntityId!.Value.ToGuid());

            var setFeature = operations[1];
            var featureRef = CanonicalId.Parse(setFeature.After!.Value.GetProperty("ref").GetString()!);
            newSpec.FeatureRA = (IFsFeatDefn)repo.GetObject(featureRef.ToGuid());
        });
    }
}
