using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.DomainServices;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Projection.Grammar;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary>An affix template reads with its ordered slots and the entries whose analyses fill each slot.</summary>
[Collection(LcmCacheParallelCollections.Group0)]
public sealed class GrammarReadAffixTemplateTests(PristineProjectFixture pristine) : IDisposable
{
    private readonly LcmCache _cache = pristine.NewScratch();

    public void Dispose() => _cache.Dispose();

    private static string Id(ICmObject target) => CanonicalId.FromGuid(target.Guid).Value;

    private (IMoInflAffixTemplate Template, IMoInflAffixSlot Subject, IMoInflAffixSlot Tense, IMoInflAffixSlot Empty,
        ILexEntry Prefix, ILexEntry Suffix, ILexEntry Stem, IMoInflClass Class) Seed()
    {
        IMoInflAffixTemplate template = null!;
        IMoInflAffixSlot subject = null!, tense = null!, empty = null!;
        ILexEntry prefix = null!, suffix = null!, stem = null!;
        IMoInflClass inflectionClass = null!;
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            var services = _cache.ServiceLocator;
            var verb = services.GetInstance<IPartOfSpeechFactory>().Create();
            _cache.LangProject.PartsOfSpeechOA.PossibilitiesOS.Add(verb);
            verb.Name.set_String(_cache.DefaultAnalWs, "Verb");
            IMoInflAffixSlot Slot(string name, bool optional)
            {
                var slot = services.GetInstance<IMoInflAffixSlotFactory>().Create();
                verb.AffixSlotsOC.Add(slot);
                slot.Name.set_String(_cache.DefaultAnalWs, name);
                slot.Optional = optional;
                return slot;
            }
            subject = Slot("Subject", false);
            tense = Slot("Tense", true);
            empty = Slot("Object", true);
            template = services.GetInstance<IMoInflAffixTemplateFactory>().Create();
            verb.AffixTemplatesOS.Add(template);
            template.Name.set_String(_cache.DefaultAnalWs, "VerbFinite");
            template.Final = true;
            template.PrefixSlotsRS.Add(subject);
            template.PrefixSlotsRS.Add(empty);
            template.SuffixSlotsRS.Add(tense);
            inflectionClass = services.GetInstance<IMoInflClassFactory>().Create();
            verb.InflectionClassesOC.Add(inflectionClass);
            inflectionClass.Name.set_String(_cache.DefaultAnalWs, "Class1");
            var types = services.GetInstance<IMoMorphTypeRepository>();
            prefix = services.GetInstance<ILexEntryFactory>().Create(types.GetObject(MoMorphTypeTags.kguidMorphPrefix),
                TsStringUtils.MakeString("ni", _cache.DefaultVernWs), "I", SandboxGenericMSA.Create(MsaType.kInfl, verb));
            ((IMoInflAffMsa)prefix.MorphoSyntaxAnalysesOC.Single()).SlotsRC.Add(subject);
            suffix = services.GetInstance<ILexEntryFactory>().Create(types.GetObject(MoMorphTypeTags.kguidMorphSuffix),
                TsStringUtils.MakeString("a", _cache.DefaultVernWs), "PRS", SandboxGenericMSA.Create(MsaType.kInfl, verb));
            ((IMoInflAffMsa)suffix.MorphoSyntaxAnalysesOC.Single()).SlotsRC.Add(tense);
            stem = services.GetInstance<ILexEntryFactory>().Create(types.GetObject(MoMorphTypeTags.kguidMorphStem),
                TsStringUtils.MakeString("pend", _cache.DefaultVernWs), "love", SandboxGenericMSA.Create(MsaType.kStem, verb));
            ((IMoStemMsa)stem.MorphoSyntaxAnalysesOC.Single()).InflectionClassRA = inflectionClass;
        });
        return (template, subject, tense, empty, prefix, suffix, stem, inflectionClass);
    }

    [Fact]
    public void ATemplateReadsWithItsSlotsInOrderAndTheAffixesThatFillEachSlot()
    {
        var seed = Seed();

        var response = GrammarReadProjectionBuilder.Build(_cache,
            new GrammarReadRequest("p", GrammarReadKind.affix_template));

        var item = Assert.IsType<GrammarAffixTemplateRead>(Assert.Single(response.Items, i => i.Id == Id(seed.Template)));
        Assert.Equal("VerbFinite", item.Name);
        Assert.Equal("Verb", item.PartOfSpeech!.Name);
        Assert.True(item.Final);
        Assert.Equal(["Subject", "Object"], item.PrefixSlots.Select(slot => slot.Name));
        Assert.Equal([false, true], item.PrefixSlots.Select(slot => slot.Optional));
        var subjectAffix = Assert.Single(item.PrefixSlots[0].Affixes);
        Assert.Equal(Id(seed.Prefix), subjectAffix.EntryId);
        Assert.Equal("ni-", subjectAffix.Headword);
        Assert.Equal("prefix", subjectAffix.MorphType);
        Assert.Equal(Id(seed.Prefix.MorphoSyntaxAnalysesOC.Single()), subjectAffix.MsaId);
        Assert.Equal("Verb", subjectAffix.PartOfSpeech!.Name);
        Assert.Empty(item.PrefixSlots[1].Affixes);
        var tenseSlot = Assert.Single(item.SuffixSlots);
        Assert.Equal(Id(seed.Tense), tenseSlot.Id);
        Assert.Equal(Id(seed.Suffix), Assert.Single(tenseSlot.Affixes).EntryId);
    }

    [Fact]
    public void AnEntryReadsWithEveryAnalysisItsPartOfSpeechInflectionClassAndSlots()
    {
        var seed = Seed();

        var response = GrammarReadProjectionBuilder.Build(_cache,
            new GrammarReadRequest("p", GrammarReadKind.lexical_entry, Id: Id(seed.Prefix)));
        var prefix = Assert.IsType<GrammarLexicalEntryRead>(Assert.Single(response.Items));
        var affixMsa = Assert.Single(prefix.Msas!);
        Assert.Equal("MoInflAffMsa", affixMsa.Kind);
        Assert.Equal("Verb", affixMsa.PartOfSpeech!.Name);
        Assert.Equal([Id(seed.Subject)], affixMsa.Slots!.Select(slot => slot.Id));

        var stem = Assert.IsType<GrammarLexicalEntryRead>(Assert.Single(GrammarReadProjectionBuilder.Build(_cache,
            new GrammarReadRequest("p", GrammarReadKind.lexical_entry, Id: Id(seed.Stem))).Items));
        var stemMsa = Assert.Single(stem.Msas!);
        Assert.Equal(Id(seed.Class), stemMsa.InflectionClass!.Id);
        Assert.Null(stemMsa.Slots);
    }

    [Fact]
    public void ATemplateReadsByIdAndByNameQuery()
    {
        var seed = Seed();

        Assert.Single(GrammarReadProjectionBuilder.Build(_cache,
            new GrammarReadRequest("p", GrammarReadKind.affix_template, Id: Id(seed.Template))).Items);
        Assert.Single(GrammarReadProjectionBuilder.Build(_cache,
            new GrammarReadRequest("p", GrammarReadKind.affix_template, Query: "finite")).Items);
        Assert.Empty(GrammarReadProjectionBuilder.Build(_cache,
            new GrammarReadRequest("p", GrammarReadKind.affix_template, Query: "nothing")).Items);
    }

    [Fact]
    public void AnalysesAreReadOnlyWhenOneEntryIsReadById()
    {
        var seed = Seed();

        var bulk = GrammarReadProjectionBuilder.Build(_cache, new GrammarReadRequest("p", GrammarReadKind.lexical_entry));
        Assert.All(bulk.Items.Cast<GrammarLexicalEntryRead>(), entry => Assert.Null(entry.Msas));
        var json = System.Text.Json.JsonSerializer.Serialize(bulk.Items.Single(i => i.Id == Id(seed.Prefix)));
        Assert.DoesNotContain("\"msas\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"slots\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void ATemplatePageFillsOnlyItsOwnTemplatesAndNamesTheNextOffset()
    {
        var seed = Seed();
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            var second = _cache.ServiceLocator.GetInstance<IMoInflAffixTemplateFactory>().Create();
            ((IPartOfSpeech)seed.Template.Owner).AffixTemplatesOS.Add(second);
            second.Name.set_String(_cache.DefaultAnalWs, "ZZSecond");
            second.SuffixSlotsRS.Add(seed.Tense);
        });

        var first = GrammarReadProjectionBuilder.Build(_cache, new GrammarReadRequest("p", GrammarReadKind.affix_template, Limit: 1));
        Assert.Equal(2, first.Total);
        Assert.Equal(1, first.NextOffset);
        Assert.Equal("VerbFinite", Assert.IsType<GrammarAffixTemplateRead>(Assert.Single(first.Items)).Name);
        var next = GrammarReadProjectionBuilder.Build(_cache,
            new GrammarReadRequest("p", GrammarReadKind.affix_template, Offset: 1, Limit: 1));
        Assert.Null(next.NextOffset);
        var shared = Assert.IsType<GrammarAffixTemplateRead>(Assert.Single(next.Items));
        Assert.Equal("ZZSecond", shared.Name);
        Assert.Equal(Id(seed.Suffix), Assert.Single(Assert.Single(shared.SuffixSlots).Affixes).EntryId);
    }

    [Fact]
    public void AnAffixWithNoPartOfSpeechAndAVariantWithNoInflectionalAnalysisAreHandled()
    {
        var seed = Seed();
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
            ((IMoInflAffMsa)seed.Prefix.MorphoSyntaxAnalysesOC.Single()).PartOfSpeechRA = null);

        var template = Assert.IsType<GrammarAffixTemplateRead>(Assert.Single(GrammarReadProjectionBuilder.Build(_cache,
            new GrammarReadRequest("p", GrammarReadKind.affix_template, Id: Id(seed.Template))).Items));

        Assert.Null(Assert.Single(template.PrefixSlots[0].Affixes).PartOfSpeech);
        Assert.DoesNotContain(template.PrefixSlots.Concat(template.SuffixSlots).SelectMany(slot => slot.Affixes),
            affix => affix.EntryId == Id(seed.Stem));
    }

    [Fact]
    public void AnAffixAnalysisInTwoSlotsListsUnderBoth()
    {
        var seed = Seed();
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
            ((IMoInflAffMsa)seed.Prefix.MorphoSyntaxAnalysesOC.Single()).SlotsRC.Add(seed.Empty));

        var template = Assert.IsType<GrammarAffixTemplateRead>(Assert.Single(GrammarReadProjectionBuilder.Build(_cache,
            new GrammarReadRequest("p", GrammarReadKind.affix_template, Id: Id(seed.Template))).Items));

        Assert.Equal(Id(seed.Prefix), Assert.Single(template.PrefixSlots[1].Affixes).EntryId);
        Assert.Equal(Id(seed.Prefix), Assert.Single(template.PrefixSlots[0].Affixes).EntryId);
    }

    [Fact]
    public void AMorphTypeFilterIsRefusedForTemplates()
    {
        var outcome = SIL.Motif.Commands.Queries.GrammarReadQuery.Query(
            new GrammarReadRequest("p", GrammarReadKind.affix_template, MorphType: "prefix"));

        Assert.False(outcome.Succeeded);
        Assert.Equal("grammar-read.invalid-request", outcome.Refusal!.Code);
    }
}
