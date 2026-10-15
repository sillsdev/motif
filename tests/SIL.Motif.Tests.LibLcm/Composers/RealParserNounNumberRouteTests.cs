using System.IO;
using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.DomainServices;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Parser;
using SIL.Motif.Tests.Parser;
using SIL.Motif.Tests.TestFixtures;
using Xunit;
using Xunit.Abstractions;

namespace SIL.Motif.Tests.Composers;

/// <summary>
/// Whether a gender on the stem plus a number on the class prefix is a route to singular and plural noun
/// readings. The prefix's InflFeats is a requirement, so the number it carries must be visible on the reading
/// without ever filtering a stem that lacks a number.
/// </summary>
[Collection(LcmCacheParallelCollections.Group3)]
public sealed class RealParserNounNumberRouteTests(PristineProjectFixture pristine, ITestOutputHelper output)
{
    [RealParserFact]
    public async Task PrefixNumberIsAnExposedRequirementAndStemGenderFiltersPrefixes()
    {
        using var cache = pristine.NewScratch();
        RealParserProject.PrepareForParsing(cache, "k", "i", "v", "t", "a", "b", "u", "f", "n");
        var ids = Seed(cache);
        var path = cache.ProjectId.Path;
        new FwDataProjectLoader().Save(cache);
        cache.Dispose();

        using var invoker = new PanGlossInvoker();
        var tracer = new PanGlossTracer(invoker);
        var observed = new Dictionary<string, string[]>();
        foreach (var word in new[] { "kitabu", "vitabu", "kifini", "vifini", "kibu", "vibu" })
        {
            var outcome = Assert.IsType<PanGlossTraceOutcome.Completed>(
                await tracer.TraceAsync(path, word, CancellationToken.None));
            Assert.True(outcome.Details!.SearchCompleted, word);
            observed[word] = outcome.Document!.Analyses.Select(analysis => string.Join("+",
                analysis.Morphs.Select(morph => ids.Name(morph.MsaId) + "[" + Features(ids, morph) + "]"))).ToArray();
            foreach (var reading in observed[word]) output.WriteLine($"{word}: {reading}");
            if (observed[word].Length == 0) output.WriteLine($"{word}: no reading");
        }

        Assert.Equal(["ki-sg[gender=g78;number=sg]+tabu-g78[gender=g78]"], observed["kitabu"]);
        Assert.Equal(["vi-pl[gender=g78;number=pl]+tabu-g78[gender=g78]"], observed["vitabu"]);
        Assert.Empty(observed["kifini"]);
        Assert.Empty(observed["vifini"]);
        Assert.Equal(["ki-sg[gender=g78;number=sg]+bu-none[none]"], observed["kibu"]);
        Assert.Equal(["vi-pl[gender=g78;number=pl]+bu-none[none]"], observed["vibu"]);
    }

    private static string Features(Ids ids, PanGlossTraceMorph morph)
    {
        var features = morph.Raw.GetProperty("features");
        if (!features.TryGetProperty("source", out var source) || !source.TryGetProperty("values", out var values))
            return "none";
        return string.Join(";", values.EnumerateArray().Select(value =>
            ids.Name(value.GetProperty("feature").GetString()) + "=" +
            ids.Name(value.GetProperty("value").GetProperty("value").GetString())).Order());
    }

    private sealed class Ids(Dictionary<Guid, string> names)
    {
        public string Name(string? id) =>
            names.TryGetValue(Guid.TryParse(id, out var guid) ? guid : Guid.Empty, out var name) ? name : "?";
    }

    private static Ids Seed(LcmCache cache)
    {
        var names = new Dictionary<Guid, string>();
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var services = cache.ServiceLocator;
            cache.LangProject.MsFeatureSystemOA ??= services.GetInstance<IFsFeatureSystemFactory>().Create();
            var system = cache.LangProject.MsFeatureSystemOA;
            var gender = Closed(cache, system, names, "gender", "g78", "g1");
            var number = Closed(cache, system, names, "number", "sg", "pl");
            var noun = services.GetInstance<IPartOfSpeechFactory>().Create();
            cache.LangProject.PartsOfSpeechOA.PossibilitiesOS.Add(noun);
            noun.Name.set_String(cache.DefaultAnalWs, "Noun");
            var slot = services.GetInstance<IMoInflAffixSlotFactory>().Create();
            noun.AffixSlotsOC.Add(slot);
            slot.Name.set_String(cache.DefaultAnalWs, "S-NC");
            var template = services.GetInstance<IMoInflAffixTemplateFactory>().Create();
            noun.AffixTemplatesOS.Add(template);
            template.Name.set_String(cache.DefaultAnalWs, "T-N");
            template.Final = true;
            template.PrefixSlotsRS.Add(slot);

            Add(cache, noun, slot, "ki", true, "ki-sg", names, (gender, "g78"), (number, "sg"));
            Add(cache, noun, slot, "vi", true, "vi-pl", names, (gender, "g78"), (number, "pl"));
            Add(cache, noun, null, "tabu", false, "tabu-g78", names, (gender, "g78"));
            Add(cache, noun, null, "fini", false, "fini-g1", names, (gender, "g1"));
            Add(cache, noun, null, "bu", false, "bu-none", names);
        });
        return new Ids(names);
    }

    private static IFsClosedFeature Closed(LcmCache cache, IFsFeatureSystem system, Dictionary<Guid, string> names, string name, params string[] values)
    {
        var feature = cache.ServiceLocator.GetInstance<IFsClosedFeatureFactory>().Create();
        system.FeaturesOC.Add(feature);
        feature.Name.set_String(cache.DefaultAnalWs, name);
        names[feature.Guid] = name;
        feature.Abbreviation.set_String(cache.DefaultAnalWs, name);
        foreach (var value in values)
        {
            var symbol = cache.ServiceLocator.GetInstance<IFsSymFeatValFactory>().Create();
            feature.ValuesOC.Add(symbol);
            symbol.Abbreviation.set_String(cache.DefaultAnalWs, value);
            symbol.Name.set_String(cache.DefaultAnalWs, value);
            names[symbol.Guid] = value;
        }
        return feature;
    }

    private static void Add(LcmCache cache, IPartOfSpeech category, IMoInflAffixSlot? slot, string form, bool prefix,
        string label, Dictionary<Guid, string> names, params (IFsClosedFeature Feature, string Value)[] selections)
    {
        var type = cache.ServiceLocator.GetInstance<IMoMorphTypeRepository>()
            .GetObject(prefix ? MoMorphTypeTags.kguidMorphPrefix : MoMorphTypeTags.kguidMorphStem);
        var entry = cache.ServiceLocator.GetInstance<ILexEntryFactory>().Create(type,
            TsStringUtils.MakeString(form, cache.DefaultVernWs), label,
            SandboxGenericMSA.Create(prefix ? MsaType.kInfl : MsaType.kStem, category));
        var msa = entry.MorphoSyntaxAnalysesOC.Single();
        names[msa.Guid] = label;
        if (slot is not null) ((IMoInflAffMsa)msa).SlotsRC.Add(slot);
        if (selections.Length == 0) return;
        var structure = cache.ServiceLocator.GetInstance<IFsFeatStrucFactory>().Create();
        if (msa is IMoInflAffMsa affix) affix.InflFeatsOA = structure;
        else ((IMoStemMsa)msa).MsFeaturesOA = structure;
        foreach (var (feature, value) in selections)
        {
            var specification = cache.ServiceLocator.GetInstance<IFsClosedValueFactory>().Create();
            structure.FeatureSpecsOC.Add(specification);
            specification.FeatureRA = feature;
            specification.ValueRA = feature.ValuesOC.Single(item =>
                item.Abbreviation.get_String(cache.DefaultAnalWs).Text == value);
        }
    }
}
