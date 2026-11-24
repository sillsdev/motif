using System.Text.RegularExpressions;
using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Model.DryRun;
using SIL.Motif.Runner.Apply;
using SIL.Motif.Runner.Execution;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary>
/// A part of speech's Inflectable Features are a typed operation only: add and remove apply and read back after reopen,
/// refuse a feature of another system, and have no record-file authoring.
/// </summary>
[Collection(LcmCacheParallelCollections.Group2)]
public sealed class PartOfSpeechFeaturesOperationTests(PristineProjectFixture pristine)
{
    private const string AddKind = "grammar/partOfSpeech/addRefInflectableFeats";
    private const string RemoveKind = "grammar/partOfSpeech/removeRefInflectableFeats";

    private sealed record Seeded(Guid Pos, Guid Control, Guid Held, Guid Free, Guid Phonological);

    private static void Do(LcmCache cache, Action action) => NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, action);

    private static string Members(LcmCache cache, Guid pos) => string.Join(",",
        cache.ServiceLocator.GetInstance<IPartOfSpeechRepository>().GetObject(pos).InflectableFeatsRC
            .Select(feature => feature.Guid.ToString("D")).Order(StringComparer.Ordinal));

    private static Seeded Seed(LcmCache cache)
    {
        Seeded? seeded = null;
        Do(cache, () =>
        {
            var services = cache.ServiceLocator;
            IFsClosedFeature Feature(IFsFeatureSystem system, string name)
            {
                var feature = services.GetInstance<IFsClosedFeatureFactory>().Create();
                system.FeaturesOC.Add(feature);
                feature.Name.set_String(cache.DefaultAnalWs, TsStringUtils.MakeString(name, cache.DefaultAnalWs));
                return feature;
            }

            IPartOfSpeech Pos(string name)
            {
                var pos = services.GetInstance<IPartOfSpeechFactory>().Create();
                cache.LangProject.PartsOfSpeechOA.PossibilitiesOS.Add(pos);
                pos.Name.set_String(cache.DefaultAnalWs, TsStringUtils.MakeString(name, cache.DefaultAnalWs));
                return pos;
            }

            var phonological = cache.LangProject.PhFeatureSystemOA ??= services.GetInstance<IFsFeatureSystemFactory>().Create();
            var grammatical = cache.LangProject.MsFeatureSystemOA ??= services.GetInstance<IFsFeatureSystemFactory>().Create();
            var held = Feature(grammatical, "zzheld");
            var free = Feature(grammatical, "zzfree");
            var pos = Pos("zzpos");
            pos.InflectableFeatsRC.Add(held);
            var control = Pos("zzcontrol");
            control.InflectableFeatsRC.Add(held);
            seeded = new Seeded(pos.Guid, control.Guid, held.Guid, free.Guid, Feature(phonological, "zzphon").Guid);
        });
        new FwDataProjectLoader().Save(cache);
        return seeded!;
    }

    [Fact]
    public void TheFieldHasBothKindsAndNoRecordFileAuthoring()
    {
        OperationRegistryBootstrap.Initialize();
        var kinds = OperationHandlerRegistry.RegisteredKinds
            .Where(kind => OperationKindRegistry.IsKnown(kind) && Regex.IsMatch(kind, @"^grammar/partOfSpeech/(addRef|removeRef)InflectableFeats$"))
            .Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { AddKind, RemoveKind }, kinds);
        Assert.All(kinds, kind => Assert.Empty(OperationHandlerRegistry.FieldWrites(kind)));
    }

    [Theory]
    [InlineData(AddKind)]
    [InlineData(RemoveKind)]
    public void AnAddAndARemoveApplyAndReadBackAfterReopenWithoutTouchingAControl(string kind)
    {
        var add = kind == AddKind;
        string path;
        Seeded seeded;
        using (var cache = pristine.NewScratch())
        {
            path = cache.ProjectId.Path;
            seeded = Seed(cache);
            var proposal = Single(kind, seeded.Pos, add ? seeded.Free : seeded.Held);
            var dryRun = ScratchDryRun.Of(cache, proposal);
            Assert.False(ProposalApplier.Apply(cache, proposal, dryRun.Anchor, "motif-tests").AlreadyApplied);
            new FwDataProjectLoader().Save(cache);
        }

        using var reloaded = new FwDataProjectLoader().LoadCache(path);
        var expected = add
            ? string.Join(",", new[] { seeded.Held, seeded.Free }.Select(id => id.ToString("D")).Order(StringComparer.Ordinal))
            : "";
        Assert.Equal(expected, Members(reloaded, seeded.Pos));
        Assert.Equal(seeded.Held.ToString("D"), Members(reloaded, seeded.Control));
    }

    private static Proposal Single(string kind, Guid target, Guid member) => new(
        new Dictionary<string, string> { ["grammar"] = "1.0" },
        CanonicalId.FromGuid(new Guid("11111111-2222-3333-4444-555555555555")), null,
        [new OperationEnvelope(CanonicalId.FromGuid(Guid.NewGuid()), kind, target: CanonicalId.FromGuid(target),
            after: System.Text.Json.JsonSerializer.SerializeToElement(new { member = CanonicalId.FromGuid(member).Value }))]);

    [Fact]
    public void AnAddRefusesAFeatureOfAnotherSystemAndAnObjectOfAnotherClassAndRepeatsChangeNothing()
    {
        using var cache = pristine.NewScratch();
        var seeded = Seed(cache);

        var wrongSystem = Assert.Throws<ReferenceFieldRefusal>(() => ScratchDryRun.Of(cache, Single(AddKind, seeded.Pos, seeded.Phonological)));
        Assert.Equal(RefusalCodes.ReferenceWrongList, wrongSystem.Code);
        var wrongClass = Assert.Throws<ReferenceFieldRefusal>(() => ScratchDryRun.Of(cache, Single(AddKind, seeded.Pos, seeded.Control)));
        Assert.Equal(RefusalCodes.ReferenceWrongClass, wrongClass.Code);

        var repeated = ScratchDryRun.Of(cache, Single(AddKind, seeded.Pos, seeded.Held));
        Assert.All(repeated.ExpectedEffects, effect => Assert.Equal(effect.Before.Values, effect.After.Values));
        var absent = ScratchDryRun.Of(cache, Single(RemoveKind, seeded.Pos, seeded.Free));
        Assert.All(absent.ExpectedEffects, effect => Assert.Equal(effect.Before.Values, effect.After.Values));
    }
}
