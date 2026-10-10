using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Parsimony;

[Collection(LcmCacheParallelCollections.Group1)]
public sealed class FindingCaptionTests(PristineProjectFixture pristine)
{
    [Fact]
    public void EveryAuthoredObjectKindReadsInALinguistsWords()
    {
        using var cache = pristine.NewScratch();
        var services = cache.ServiceLocator;
        var entry = services.GetInstance<ILexEntryRepository>().GetObject(pristine.Seed.FirstEntryId);
        IMoInflAffixSlot named = null!, unnamedSlot = null!;
        IMoInflAffixTemplate template = null!;
        IPhEnvironment environment = null!;
        IPhNCSegments byName = null!, byAbbreviation = null!, unnamedClass = null!;
        IMoAlloAdhocProhib prohibition = null!, emptyProhibition = null!;
        IPhRegularRule rule = null!;
        IMoAffixAllomorph first = null!, second = null!;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var category = services.GetInstance<IPartOfSpeechFactory>().Create();
            cache.LangProject.PartsOfSpeechOA.PossibilitiesOS.Add(category);
            named = Slot(cache, category, "Subject");
            unnamedSlot = Slot(cache, category, null);
            template = services.GetInstance<IMoInflAffixTemplateFactory>().Create();
            category.AffixTemplatesOS.Add(template);
            template.Name.set_String(cache.DefaultAnalWs, "Verb template");
            environment = services.GetInstance<IPhEnvironmentFactory>().Create();
            cache.LangProject.PhonologicalDataOA.EnvironmentsOS.Add(environment);
            environment.StringRepresentation = TsStringUtils.MakeString("/ _ a", cache.DefaultVernWs);
            byName = NaturalClass(cache, "Vowels", "V");
            byAbbreviation = NaturalClass(cache, null, "C");
            unnamedClass = NaturalClass(cache, null, null);
            first = Alternate(cache, entry, "ra");
            second = Alternate(cache, entry, "ta");
            prohibition = services.GetInstance<IMoAlloAdhocProhibFactory>().Create();
            emptyProhibition = services.GetInstance<IMoAlloAdhocProhibFactory>().Create();
            cache.LangProject.MorphologicalDataOA.AdhocCoProhibitionsOC.Add(prohibition);
            cache.LangProject.MorphologicalDataOA.AdhocCoProhibitionsOC.Add(emptyProhibition);
            prohibition.FirstAllomorphRA = first;
            prohibition.RestOfAllosRS.Add(second);
            rule = services.GetInstance<IPhRegularRuleFactory>().Create();
            cache.LangProject.PhonologicalDataOA.PhonRulesOS.Add(rule);
            rule.Name.set_String(cache.DefaultAnalWs, "r becomes t");
        });
        var msa = entry.MorphoSyntaxAnalysesOC.First();

        Assert.Equal(entry.HeadWord.Text, ParsimonyFindingDispositionComposer.Caption(cache, msa));
        Assert.Equal("Subject", ParsimonyFindingDispositionComposer.Caption(cache, named));
        Assert.Equal("Verb template", ParsimonyFindingDispositionComposer.Caption(cache, template));
        Assert.Equal("/ _ a", ParsimonyFindingDispositionComposer.Caption(cache, environment));
        Assert.Equal("Vowels", ParsimonyFindingDispositionComposer.Caption(cache, byName));
        Assert.Equal("C", ParsimonyFindingDispositionComposer.Caption(cache, byAbbreviation));
        Assert.Equal("ra, ta", ParsimonyFindingDispositionComposer.Caption(cache, prohibition));
        Assert.Equal("r becomes t", ParsimonyFindingDispositionComposer.Caption(cache, rule));
        Assert.Equal("an unnamed affix slot", ParsimonyFindingDispositionComposer.Caption(cache, unnamedSlot));
        Assert.Equal("an unnamed natural class", ParsimonyFindingDispositionComposer.Caption(cache, unnamedClass));
        Assert.Equal("an ad hoc prohibition with no named members",
            ParsimonyFindingDispositionComposer.Caption(cache, emptyProhibition));
    }

    private static IMoInflAffixSlot Slot(LcmCache cache, IPartOfSpeech category, string? name)
    {
        var slot = cache.ServiceLocator.GetInstance<IMoInflAffixSlotFactory>().Create();
        category.AffixSlotsOC.Add(slot);
        if (name is not null) slot.Name.set_String(cache.DefaultAnalWs, name);
        return slot;
    }

    private static IPhNCSegments NaturalClass(LcmCache cache, string? name, string? abbreviation)
    {
        var value = cache.ServiceLocator.GetInstance<IPhNCSegmentsFactory>().Create();
        cache.LangProject.PhonologicalDataOA.NaturalClassesOS.Add(value);
        if (name is not null) value.Name.set_String(cache.DefaultAnalWs, name);
        if (abbreviation is not null) value.Abbreviation.set_String(cache.DefaultAnalWs, abbreviation);
        return value;
    }

    private static IMoAffixAllomorph Alternate(LcmCache cache, ILexEntry entry, string form)
    {
        var value = cache.ServiceLocator.GetInstance<IMoAffixAllomorphFactory>().Create();
        entry.AlternateFormsOS.Add(value);
        value.Form.set_String(cache.DefaultVernWs, form);
        return value;
    }
}
