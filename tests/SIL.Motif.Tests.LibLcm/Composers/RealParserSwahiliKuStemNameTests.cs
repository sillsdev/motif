using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.DomainServices;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Parser;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.Parser;
using SIL.Motif.Tests.TestFixtures;
using Xunit;
using Xunit.Abstractions;
using static SIL.Motif.Tests.Composers.AffixTestSupport;

namespace SIL.Motif.Tests.Composers;

/// <summary>
/// Whether stem names restrict which stem allomorph a tense prefix may combine with. An allomorph with a stem
/// name is eligible only when an applied affix's required features fit one of the name's regions.
/// </summary>
[Collection(LcmCacheParallelCollections.Group3)]
public sealed class RealParserSwahiliKuStemNameTests(PristineProjectFixture pristine, ITestOutputHelper output)
{
    private static readonly string[] Words = ["nilila", "ninanywa", "ninakula", "ninakunywa", "nila", "nikula"];

    [RealParserFact]
    public async Task StemNamesLetTenseSelectTheKuAllomorphAndRetirePlainOnes()
    {
        using var cache = pristine.NewScratch();
        RealParserProject.PrepareForParsing(cache, "n", "i", "l", "a", "k", "u", "y", "w");
        var seeded = Seed(cache);
        var path = cache.ProjectId.Path;
        new FwDataProjectLoader().Save(cache);

        using var invoker = new PanGlossInvoker();
        var tracer = new PanGlossTracer(invoker);
        var before = await Observe(tracer, path, seeded.Names);
        Assert.Equal(["ni+li+l+a"], before["nilila"]);
        Assert.Equal(["ni+na+nyw+a"], before["ninanywa"]);
        Assert.Equal(["ni+na+kul+a"], before["ninakula"]);
        Assert.Equal(["ni+na+kunyw+a"], before["ninakunywa"]);
        Assert.Equal(["ni+l+a"], before["nila"]);
        Assert.Equal(["ni+kul+a"], before["nikula"]);

        var category = CanonicalId.FromGuid(seeded.Category);
        var feature = CanonicalId.FromGuid(seeded.Feature);
        var ku = Created(ApplyOps(cache, AuthorStemNameComposer.Build(cache, new(category, "KU", Anal(cache),
            [[new(feature, CanonicalId.FromGuid(seeded.Tense))]]))), PartOfSpeechStemNamesOperationKinds.Create);
        var plain = Created(ApplyOps(cache, AuthorStemNameComposer.Build(cache, new(category, "NOTTENSE", Anal(cache),
            [[new(feature, CanonicalId.FromGuid(seeded.Imperative))]]))), PartOfSpeechStemNamesOperationKinds.Create);
        foreach (var form in seeded.Plain)
            ApplyOps(cache, EditStemAllomorphStemNameComposer.Build(cache, new(CanonicalId.FromGuid(form), null, plain)));
        foreach (var form in seeded.Ku)
            ApplyOps(cache, EditStemAllomorphStemNameComposer.Build(cache, new(CanonicalId.FromGuid(form), null, ku)));
        new FwDataProjectLoader().Save(cache);

        var after = await Observe(tracer, path, seeded.Names);
        Assert.Empty(after["nilila"]);
        Assert.Empty(after["ninanywa"]);
        Assert.Equal(["ni+na+kul+a"], after["ninakula"]);
        Assert.Equal(["ni+na+kunyw+a"], after["ninakunywa"]);
        Assert.Empty(after["nila"]);
        Assert.Empty(after["nikula"]);
    }

    private async Task<Dictionary<string, string[]>> Observe(PanGlossTracer tracer, string path,
        IReadOnlyDictionary<Guid, string> names)
    {
        var observed = new Dictionary<string, string[]>();
        foreach (var word in Words)
        {
            var outcome = Assert.IsType<PanGlossTraceOutcome.Completed>(
                await tracer.TraceAsync(path, word, CancellationToken.None));
            Assert.True(outcome.Details!.SearchCompleted, word);
            observed[word] = outcome.Document!.Analyses.Select(analysis => string.Join("+", analysis.Morphs.Select(
                morph => Guid.TryParse(morph.FormId, out var id) && names.TryGetValue(id, out var name)
                    ? name : "?" + morph.Form))).ToArray();
            foreach (var reading in observed[word]) output.WriteLine($"{word}: {reading}");
            if (observed[word].Length == 0) output.WriteLine($"{word}: no reading");
        }
        return observed;
    }

    private sealed record Seeded(Guid Category, Guid Feature, Guid Tense, Guid Imperative,
        IReadOnlyList<Guid> Plain, IReadOnlyList<Guid> Ku, IReadOnlyDictionary<Guid, string> Names);

    private static Seeded Seed(LcmCache cache)
    {
        var names = new Dictionary<Guid, string>();
        var plain = new List<Guid>();
        var ku = new List<Guid>();
        Guid category = default, feature = default, tense = default, imperative = default;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var services = cache.ServiceLocator;
            cache.LangProject.MsFeatureSystemOA ??= services.GetInstance<IFsFeatureSystemFactory>().Create();
            var system = cache.LangProject.MsFeatureSystemOA;
            var tam = services.GetInstance<IFsClosedFeatureFactory>().Create();
            system.FeaturesOC.Add(tam);
            tam.Name.set_String(cache.DefaultAnalWs, "tam");
            tam.Abbreviation.set_String(cache.DefaultAnalWs, "tam");
            var values = new Dictionary<string, IFsSymFeatVal>();
            foreach (var value in new[] { "tense", "imp" })
            {
                var symbol = services.GetInstance<IFsSymFeatValFactory>().Create();
                tam.ValuesOC.Add(symbol);
                symbol.Abbreviation.set_String(cache.DefaultAnalWs, value);
                symbol.Name.set_String(cache.DefaultAnalWs, value);
                values[value] = symbol;
            }
            feature = tam.Guid;
            tense = values["tense"].Guid;
            imperative = values["imp"].Guid;

            var verb = services.GetInstance<IPartOfSpeechFactory>().Create();
            cache.LangProject.PartsOfSpeechOA.PossibilitiesOS.Add(verb);
            verb.Name.set_String(cache.DefaultAnalWs, "Verb");
            category = verb.Guid;
            var sm = Slot(cache, verb, "S-SM", optional: false);
            var tamSlot = Slot(cache, verb, "S-TAM", optional: true);
            var fv = Slot(cache, verb, "S-FV", optional: false);
            var template = services.GetInstance<IMoInflAffixTemplateFactory>().Create();
            verb.AffixTemplatesOS.Add(template);
            template.Name.set_String(cache.DefaultAnalWs, "T-V");
            template.Final = true;
            template.PrefixSlotsRS.Add(sm);
            template.PrefixSlotsRS.Add(tamSlot);
            template.SuffixSlotsRS.Add(fv);

            Affix(cache, verb, sm, "ni", true, null, names);
            Affix(cache, verb, tamSlot, "na", true, (tam, values["tense"]), names);
            Affix(cache, verb, tamSlot, "li", true, (tam, values["tense"]), names);
            Affix(cache, verb, fv, "a", false, null, names);
            Stem(cache, verb, "l", "kul", names, plain, ku);
            Stem(cache, verb, "nyw", "kunyw", names, plain, ku);
        });
        return new Seeded(category, feature, tense, imperative, plain, ku, names);
    }

    private static IMoInflAffixSlot Slot(LcmCache cache, IPartOfSpeech category, string name, bool optional)
    {
        var slot = cache.ServiceLocator.GetInstance<IMoInflAffixSlotFactory>().Create();
        category.AffixSlotsOC.Add(slot);
        slot.Name.set_String(cache.DefaultAnalWs, name);
        slot.Optional = optional;
        return slot;
    }

    private static void Affix(LcmCache cache, IPartOfSpeech category, IMoInflAffixSlot slot, string form, bool prefix,
        (IFsClosedFeature Feature, IFsSymFeatVal Value)? selection, Dictionary<Guid, string> names)
    {
        var type = cache.ServiceLocator.GetInstance<IMoMorphTypeRepository>()
            .GetObject(prefix ? MoMorphTypeTags.kguidMorphPrefix : MoMorphTypeTags.kguidMorphSuffix);
        var entry = cache.ServiceLocator.GetInstance<ILexEntryFactory>().Create(type,
            TsStringUtils.MakeString(form, cache.DefaultVernWs), form,
            SandboxGenericMSA.Create(MsaType.kInfl, category));
        names[entry.LexemeFormOA.Guid] = form;
        var msa = (IMoInflAffMsa)entry.MorphoSyntaxAnalysesOC.Single();
        msa.SlotsRC.Add(slot);
        if (selection is not { } chosen) return;
        var structure = cache.ServiceLocator.GetInstance<IFsFeatStrucFactory>().Create();
        msa.InflFeatsOA = structure;
        var specification = cache.ServiceLocator.GetInstance<IFsClosedValueFactory>().Create();
        structure.FeatureSpecsOC.Add(specification);
        specification.FeatureRA = chosen.Feature;
        specification.ValueRA = chosen.Value;
    }

    private static void Stem(LcmCache cache, IPartOfSpeech category, string plainForm, string kuForm,
        Dictionary<Guid, string> names, List<Guid> plain, List<Guid> ku)
    {
        var type = cache.ServiceLocator.GetInstance<IMoMorphTypeRepository>().GetObject(MoMorphTypeTags.kguidMorphStem);
        var entry = cache.ServiceLocator.GetInstance<ILexEntryFactory>().Create(type,
            TsStringUtils.MakeString(plainForm, cache.DefaultVernWs), plainForm,
            SandboxGenericMSA.Create(MsaType.kStem, category));
        names[entry.LexemeFormOA.Guid] = plainForm;
        plain.Add(entry.LexemeFormOA.Guid);
        var alternate = cache.ServiceLocator.GetInstance<IMoStemAllomorphFactory>().Create();
        entry.AlternateFormsOS.Add(alternate);
        alternate.Form.set_String(cache.DefaultVernWs, TsStringUtils.MakeString(kuForm, cache.DefaultVernWs));
        alternate.MorphTypeRA = type;
        names[alternate.Guid] = kuForm;
        ku.Add(alternate.Guid);
    }
}
