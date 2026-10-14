using SIL.LCModel;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Contract.Canonicalization;
using SIL.Motif.Contract.Ids;
using SIL.Motif.LiveHost.Baselines;
using SIL.Motif.Model.Snapshot;
using SIL.Motif.Runner.Snapshotting;
using SIL.Motif.Tests.TestFixtures;
using Xunit;
using static SIL.Motif.Tests.Composers.AffixTestSupport;
using static SIL.Motif.Tests.Composers.InflFeatsTestSupport;

namespace SIL.Motif.Tests.Composers;

[Collection(LcmCacheParallelCollections.Group3)]
public sealed class InflFeatsSnapshotTests : IDisposable
{
    private readonly LcmCache _cache;
    private readonly CanonicalId _category;

    public InflFeatsSnapshotTests(PristineProjectFixture pristine)
    {
        _cache = pristine.NewScratch();
        _category = CanonicalId.FromGuid(pristine.Seed.PartOfSpeechId);
    }

    public void Dispose() => _cache.Dispose();

    [Fact]
    public void EmptyStructureAddsTheExactOwningLinkToMechanicalDiff()
    {
        var (_, msa, _) = AddInflectionalPrefix(_cache, "ki", _category);
        var before = InflectionSnapshots.Read(_cache);
        var create = Structure(msa);
        ApplyOps(_cache, [create]);
        var effects = SoundSystemSnapshots.Compare(before, InflectionSnapshots.Read(_cache));
        var link = Assert.Single(effects, effect => effect.Field == SnapshotFields.MoInflAffMsaInflFeats);
        Assert.Equal(msa, link.CanonicalId);
        Assert.Empty(link.Before);
        Assert.Equal(create.EntityId!.Value.Value, link.After["ref"]);
    }

    [Fact]
    public void NativeSpecificationFeatureAndValueDeltasKeepDistinctIdentitiesDespiteIdenticalLabels()
    {
        var (_, msa, _) = AddInflectionalPrefix(_cache, "ki", _category);
        var (feature, values) = AddFeature(_cache, "nc", "seven", "eight");
        var create = Structure(msa);
        ApplyOps(_cache, [create]);
        var before = InflectionSnapshots.Read(_cache);
        var selection = Selection(create.EntityId!.Value, feature, values[0]);
        ApplyOps(_cache, selection);
        var selected = InflectionSnapshots.Read(_cache);
        var effects = SoundSystemSnapshots.Compare(before, selected);
        Assert.Contains(effects, effect => effect.Field == SnapshotFields.FsFeatStrucFeatureSpecs &&
            effect.After.Values.Contains(selection[0].EntityId!.Value.Value));
        Assert.Contains(effects, effect => effect.CanonicalId == selection[0].EntityId &&
            effect.Field == SnapshotFields.FsFeatureSpecificationFeature && effect.After["ref"] == feature.Value);
        Assert.Contains(effects, effect => effect.CanonicalId == selection[0].EntityId &&
            effect.Field == SnapshotFields.FsClosedValueValue && effect.After["ref"] == values[0].Value);
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            Get<IFsSymFeatVal>(_cache, values[1]).Name.set_String(_cache.DefaultAnalWs,
                Get<IFsSymFeatVal>(_cache, values[0]).Name.get_String(_cache.DefaultAnalWs).Text);
        });
        before = InflectionSnapshots.Read(_cache);
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
            Get<IFsClosedValue>(_cache, selection[0].EntityId!.Value).ValueRA = Get<IFsSymFeatVal>(_cache, values[1]));
        var changed = SoundSystemSnapshots.Compare(before, InflectionSnapshots.Read(_cache));
        var delta = Assert.Single(changed, effect => effect.Field == SnapshotFields.FsClosedValueValue);
        Assert.Equal(values[0].Value, delta.Before["ref"]);
        Assert.Equal(values[1].Value, delta.After["ref"]);
    }

    [Fact]
    public void AddingTheLinkSnapshotChangesProjectionDigestForAnOtherwiseUnchangedPopulatedProject()
    {
        var (_, msa, _) = AddInflectionalPrefix(_cache, "ki", _category);
        ApplyOps(_cache, [Structure(msa)]);
        var nativeIds = _cache.ServiceLocator.GetInstance<ICmObjectRepository>().AllInstances()
            .Select(item => item.Guid).Order().ToArray();
        var projected = new List<ObjectSnapshot>();
        var liveDigest = BaselineSemanticDigest.Compute(_cache, System.Threading.CancellationToken.None, projected.Add);
        var msaSnapshot = Assert.Single(projected, snapshot => snapshot.CanonicalId == msa);
        Assert.Equal(Get<IMoInflAffMsa>(_cache, msa).InflFeatsOA!.Guid,
            CanonicalId.Parse(msaSnapshot.AlternativesFields[SnapshotFields.MoInflAffMsaInflFeats]["ref"]).ToGuid());
        var withoutLink = projected.Select(snapshot => new ObjectSnapshot(snapshot.CanonicalId,
            snapshot.AlternativesFields.Where(field => field.Key != SnapshotFields.MoInflAffMsaInflFeats)
                .ToDictionary(field => field.Key, field => field.Value)));
        var priorShape = IntentDigest.Sha256Of(CanonicalJson.CanonicalizeToUtf8(ObjectSnapshotJsonWriter.WriteJson(withoutLink)));
        var currentShape = IntentDigest.Sha256Of(CanonicalJson.CanonicalizeToUtf8(ObjectSnapshotJsonWriter.WriteJson(projected)));
        Assert.NotEqual(priorShape, currentShape);
        Assert.Equal(liveDigest, BaselineSemanticDigest.Compute(_cache));
        Assert.Equal(nativeIds, _cache.ServiceLocator.GetInstance<ICmObjectRepository>().AllInstances()
            .Select(item => item.Guid).Order().ToArray());
    }

}
