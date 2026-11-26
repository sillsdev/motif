using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands.Review;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Commands.Review;

/// <summary>Pins the tier-1 sibling forms: the entries that use a grammar object a Proposal edits.</summary>
[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group2)]
public sealed class TrialReferrerTests(PristineProjectFixture pristine)
{
    private static OperationEnvelope Operation(string kind, CanonicalId? target = null, object? after = null, CanonicalId? entity = null) =>
        new(CanonicalId.Mint("op/"), kind, entityId: entity, target: target,
            after: after is null ? null : JsonDocument.Parse(JsonSerializer.Serialize(after)).RootElement.Clone());

    private static Proposal Proposal(params OperationEnvelope[] operations) =>
        new(new Dictionary<string, string> { ["grammar"] = "1.0" }, CanonicalId.Mint("proposal/"), null, operations);

    [Fact]
    public void AnEditSeedsItsTargetAndTheObjectsItPointsAtButARenameAndAPlainCreateDoNot()
    {
        var edited = CanonicalId.FromGuid(Guid.NewGuid());
        var renamed = CanonicalId.FromGuid(Guid.NewGuid());
        var parent = CanonicalId.FromGuid(Guid.NewGuid());
        var slot = CanonicalId.FromGuid(Guid.NewGuid());
        var other = CanonicalId.FromGuid(Guid.NewGuid());
        var created = CanonicalId.FromGuid(Guid.NewGuid());
        var proposal = Proposal(
            Operation("grammar/moInflClass/setDescription", edited, new { ws = "en", text = "x" }),
            Operation("grammar/moInflClass/setName", renamed, new { ws = "en", text = "y" }),
            Operation("grammar/moInflClass/createSubclasses", parent, new { name = "sub" }),
            Operation("grammar/moInflAffixTemplate/addRefPrefixSlots", other, new { member = slot.Value, rest = new[] { created.Value } }, null),
            Operation("grammar/phEnvironment/createEnvironments", other, new { name = "e" }),
            Operation("grammar/moStemName/createRegions", created, new { name = "r" }, created),
            Operation("lexical/lexEntry/setCitationForm", other, new { text = "ignored" }));

        var seeds = TrialReferrers.Seeds(proposal).Select(seed => seed.Id).ToHashSet();

        Assert.Contains(edited.ToGuid(), seeds);
        Assert.Contains(parent.ToGuid(), seeds);
        Assert.Contains(other.ToGuid(), seeds);
        Assert.Contains(slot.ToGuid(), seeds);
        Assert.DoesNotContain(renamed.ToGuid(), seeds);
        Assert.DoesNotContain(created.ToGuid(), seeds);
    }

    [Fact]
    public void AClassStandsForTheEntriesThatUseItOrASubclassAndASlotForTheEntriesInItsAffixes()
    {
        using var cache = pristine.NewScratch();
        var services = cache.ServiceLocator;
        Guid classId = default, subId = default, slotId = default, templateId = default, bystander = default;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var pos = services.GetInstance<IPartOfSpeechRepository>().AllInstances().First();
            var parent = services.GetInstance<IMoInflClassFactory>().Create();
            pos.InflectionClassesOC.Add(parent);
            var sub = services.GetInstance<IMoInflClassFactory>().Create();
            parent.SubclassesOC.Add(sub);
            var slot = services.GetInstance<IMoInflAffixSlotFactory>().Create();
            pos.AffixSlotsOC.Add(slot);
            var template = services.GetInstance<IMoInflAffixTemplateFactory>().Create();
            pos.AffixTemplatesOS.Add(template);
            template.SuffixSlotsRS.Add(slot);
            (classId, subId, slotId, templateId) = (parent.Guid, sub.Guid, slot.Guid, template.Guid);

            var stemType = services.GetInstance<IMoMorphTypeRepository>().GetObject(MoMorphTypeTags.kguidMorphStem);
            ILexEntry Entry(string form)
            {
                var entry = services.GetInstance<ILexEntryFactory>().Create();
                var lexeme = services.GetInstance<IMoStemAllomorphFactory>().Create();
                entry.LexemeFormOA = lexeme;
                lexeme.MorphTypeRA = stemType;
                lexeme.Form.set_String(cache.DefaultVernWs, form);
                return entry;
            }
            var inClass = Entry("kwa");
            var stem = services.GetInstance<IMoStemMsaFactory>().Create();
            inClass.MorphoSyntaxAnalysesOC.Add(stem);
            stem.InflectionClassRA = sub;
            var inSlot = Entry("-ish");
            var affix = services.GetInstance<IMoInflAffMsaFactory>().Create();
            inSlot.MorphoSyntaxAnalysesOC.Add(affix);
            affix.SlotsRC.Add(slot);
            bystander = Entry("zzz").Guid;
        });
        var seeds = new[] { classId, slotId, templateId }.Select(id => new ReferrerSeed(id, CanonicalId.FromGuid(id).Value));

        var found = TrialReferrers.Find(cache, seeds);

        Assert.Equal(["-ish", "kwa"], found.Forms.Order(StringComparer.Ordinal));
        Assert.Equal([("MoInflClass", 1), ("MoInflAffixSlot", 1), ("MoInflAffixTemplate", 1)],
            found.Referrers.Select(row => (row.Class, row.Entries)));
        Assert.DoesNotContain("zzz", found.Forms);
        Assert.NotEqual(subId, bystander);
    }

    [Fact]
    public void AnObjectNoEntryUsesOfAnotherClassOrMissingStandsForNothing()
    {
        using var cache = pristine.NewScratch();
        var seeds = new[] { pristine.Seed.PartOfSpeechId, pristine.Seed.FirstEntryId, Guid.NewGuid() }
            .Select(id => new ReferrerSeed(id, CanonicalId.FromGuid(id).Value));

        var found = TrialReferrers.Find(cache, seeds);

        Assert.Empty(found.Forms);
        Assert.Empty(found.Referrers);
    }
}
