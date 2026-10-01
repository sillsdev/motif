using System;
using System.Linq;
using SIL.LCModel;
using SIL.LCModel.DomainServices;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Projection;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Projection;

/// <summary>
/// Pins what the inspector reads from FieldWorks for an object: an affix's sense, slot, template and required
/// features, its allomorphs' environments exactly as FieldWorks stores them, a stem's timing key, and a rule's
/// kind and home tool. The grammar is a SYNTHETIC EXAMPLE modelled loosely on Swahili.
/// </summary>
[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group0)]
public sealed class ObjectFactsReaderTests : IDisposable
{
    private const string ProjectName = "Sample";

    private readonly LcmCache _cache;
    private readonly Grammar _grammar;

    public ObjectFactsReaderTests(PristineProjectFixture pristine)
    {
        _cache = pristine.NewScratch();
        _grammar = Grammar.Build(_cache);
        new FwDataProjectLoader().Save(_cache);
    }

    public void Dispose()
    {
        if (!_cache.IsDisposed) _cache.Dispose();
    }

    [Fact]
    public void AnAffixReadsItsSenseSlotTemplateAndRequiredFeatures()
    {
        var facts = Read(new ObjectUseRef
        {
            AllomorphId = _grammar.Ja.LexemeFormOA.Guid.ToString("D"),
            GrammaticalInfoId = _grammar.JaMsa.Guid.ToString("D"),
        })!;

        Assert.Equal(("ja-", "prefix"), (facts.Entry!.Headword, facts.Entry.MorphType));
        Assert.Equal(("lexiconEdit", "Lexicon Edit"), (facts.Entry.FieldWorks!.Tool, facts.Entry.FieldWorks.ToolName));
        var sense = Assert.Single(facts.Senses);
        Assert.Equal(("1", "NEG.PERF", "not yet"), (sense.Number, sense.Gloss, sense.Definition));
        var info = facts.GrammaticalInfo!;
        Assert.Equal(("inflectionalAffix", "Verb"), (info.Kind, info.Category!.Name));
        Assert.Equal("Category Edit", info.Category.FieldWorks!.ToolName);
        var slot = Assert.Single(info.Slots);
        Assert.Equal(("TAM", false), (slot.Name, slot.Optional));
        Assert.Equal(["Verb template"], slot.Templates.Select(template => template.Name));
        Assert.Equal("Category Edit", slot.FieldWorks!.ToolName);
        Assert.Null(info.AddedFeatures);
        Assert.Null(info.RequiredFeatures);
        var ja = facts.Allomorphs.Single(allomorph => allomorph.IsAsked);
        Assert.Equal("ja-", ja.Form);
        var required = ja.RequiredFeatures!;
        Assert.Equal(_grammar.JaRequired.LongName, required.Notation);
        var value = Assert.Single(required.Values);
        Assert.Equal(("Polarity", "negative", "neg"), (value.Feature, value.Value, value.ValueAbbreviation));
        Assert.Equal("Inflection Features", value.FieldWorks!.ToolName);
        Assert.Equal(new TraceTimingKey("morph_rule", _grammar.JaMsa.Guid.ToString("D")), facts.TimingKey);
        Assert.Null(facts.Rule);
    }

    [Fact]
    public void AnAllomorphsEnvironmentReadsAsFieldWorksStoresIt_LexemeFormFirst()
    {
        var facts = Read(new ObjectUseRef { GrammaticalInfoId = _grammar.JaMsa.Guid.ToString("D") })!;

        Assert.Equal([("ja-", "/ _ [C]"), ("j-", "/ _ [V]")], facts.Allomorphs.Select(allomorph =>
            (allomorph.Form, Assert.Single(allomorph.Environments).Notation)));
        Assert.Equal(_grammar.BeforeConsonant.StringRepresentation.Text, facts.Allomorphs[0].Environments[0].Notation);
        Assert.All(facts.Allomorphs, allomorph => Assert.False(allomorph.IsAsked));
        Assert.All(facts.Allomorphs.SelectMany(allomorph => allomorph.Environments),
            environment => Assert.Equal("Environments", environment.FieldWorks!.ToolName));
        Assert.Null(facts.Allomorphs[1].RequiredFeatures);
    }

    [Fact]
    public void AnAffixGivesTheFeaturesItAdds_AndAStemIsTimedUnderItsEntry()
    {
        var wa = Read(new ObjectUseRef { GrammaticalInfoId = _grammar.WaMsa.Guid.ToString("D") })!;
        Assert.Equal(["SM"], wa.GrammaticalInfo!.Slots.Select(slot => slot.Name));
        Assert.Equal([("Number", "plural")],
            wa.GrammaticalInfo.AddedFeatures!.Values.Select(value => (value.Feature, value.Value)));

        var kat = Read(new ObjectUseRef { AllomorphId = _grammar.Kata.LexemeFormOA.Guid.ToString("D") })!;

        Assert.Equal(("kata", "root"), (kat.Entry!.Headword, kat.Entry.MorphType));
        Assert.Equal(("stem", "Verb"), (kat.GrammaticalInfo!.Kind, kat.GrammaticalInfo.Category!.Name));
        Assert.Empty(kat.GrammaticalInfo.Slots);
        Assert.Equal(["cut"], kat.Senses.Select(sense => sense.Gloss));
        var allomorph = Assert.Single(kat.Allomorphs);
        Assert.Equal(("kat", true), (allomorph.Form, allomorph.IsAsked));
        Assert.Empty(allomorph.Environments);
        Assert.Equal(new TraceTimingKey("lex_entry", _grammar.Kata.Guid.ToString("D")), kat.TimingKey);
    }

    [Fact]
    public void ARuleReadsItsKindAndHomeTool_AndAnAffixRuleItsMorpheme()
    {
        var harmony = Read(ObjectUseRef.ForTimingKey(new TraceTimingKey("phon_rule",
            _grammar.VowelHarmony.Guid.ToString("D").ToUpperInvariant())))!;

        Assert.Equal(("phonologicalRule", "Vowel harmony"), (harmony.Rule!.Kind, harmony.Rule.Name));
        Assert.Equal(("PhonologicalRuleEdit", "Phonological Rules"),
            (harmony.Rule.FieldWorks!.Tool, harmony.Rule.FieldWorks.ToolName));
        Assert.Null(harmony.Entry);
        Assert.Empty(harmony.Allomorphs);
        Assert.Equal(new TraceTimingKey("phon_rule", _grammar.VowelHarmony.Guid.ToString("D")), harmony.TimingKey);

        var ja = Read(ObjectUseRef.ForTimingKey(new TraceTimingKey("morph_rule", _grammar.JaMsa.Guid.ToString("D"))))!;
        Assert.Equal(("affixRule", "ja- ‘NEG.PERF’", "Lexicon Edit"), (ja.Rule!.Kind, ja.Rule.Name, ja.Rule.FieldWorks!.ToolName));
        Assert.Equal("ja-", ja.Entry!.Headword);
        Assert.Equal(["TAM"], ja.GrammaticalInfo!.Slots.Select(slot => slot.Name));
    }

    [Fact]
    public void AnAffixAllomorphAloneHasNoTimingKeyWhenItsEntryHasTwoGrammaticalInfos()
    {
        IMoInflAffMsa second = null!;
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
            second = _cache.ServiceLocator.GetInstance<IMoInflAffMsaFactory>().Create(
                _grammar.Ja, SandboxGenericMSA.Create(MsaType.kInfl, _grammar.Verb)));

        var facts = Read(new ObjectUseRef { AllomorphId = _grammar.Ja.LexemeFormOA.Guid.ToString("D") })!;

        Assert.Null(facts.GrammaticalInfo);
        Assert.Null(facts.TimingKey);
        Assert.Equal("ja-", facts.Entry!.Headword);
        Assert.NotEqual(second.Guid, _grammar.JaMsa.Guid);
    }

    [Fact]
    public void NothingTheBaselineHolds_OrNoRefAtAll_ReadsAsNoFacts()
    {
        Assert.Null(Read(new ObjectUseRef { AllomorphId = Guid.NewGuid().ToString("D") }));
        Assert.Null(Read(new ObjectUseRef { TimingKind = "morph_rule", TimingKey = "mrule#0:Verb template" }));
        Assert.Null(Read(new ObjectUseRef { Label = "kat" }));
    }

    [Fact]
    public void ReadingLeavesTheProjectUnaltered()
    {
        var undo = _cache.ServiceLocator.GetInstance<IUndoStackManager>();
        Assert.False(undo.HasUnsavedChanges);

        Read(new ObjectUseRef { AllomorphId = _grammar.Ja.LexemeFormOA.Guid.ToString("D") });
        Read(new ObjectUseRef { GrammaticalInfoId = _grammar.WaMsa.Guid.ToString("D") });
        Read(ObjectUseRef.ForTimingKey(new TraceTimingKey("phon_rule", _grammar.VowelHarmony.Guid.ToString("D"))));

        Assert.False(undo.HasUnsavedChanges);
    }

    private ObjectFacts? Read(ObjectUseRef reference) => ObjectFactsReader.Read(_cache, reference, found =>
        FieldWorksLinks.TargetFor(_cache, found) is { } target
            ? new TraceFieldWorksTarget(target.Tool, FieldWorksLinks.ToolName(target.Tool),
                target.ObjectId.ToString("D"), FieldWorksLinks.ForTarget(ProjectName, target)!)
            : null);

    private sealed record Grammar(
        IPartOfSpeech Verb, ILexEntry Ja, IMoInflAffMsa JaMsa, IFsFeatStruc JaRequired, IPhEnvironment BeforeConsonant,
        IMoInflAffMsa WaMsa, ILexEntry Kata, IPhRegularRule VowelHarmony)
    {
        // Verb template: SM (wa- 3PL adds Number plural), TAM (ja- NEG.PERF needs Polarity negative), stem.
        public static Grammar Build(LcmCache cache)
        {
            Grammar built = null!;
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            {
                var services = cache.ServiceLocator;
                var analysis = cache.DefaultAnalWs;
                var phonology = cache.LangProject.PhonologicalDataOA;

                var consonants = services.GetInstance<IPhNCSegmentsFactory>().Create();
                phonology.NaturalClassesOS.Add(consonants);
                consonants.Name.set_String(analysis, "Consonants");
                consonants.Abbreviation.set_String(analysis, "C");
                var vowels = services.GetInstance<IPhNCSegmentsFactory>().Create();
                phonology.NaturalClassesOS.Add(vowels);
                vowels.Name.set_String(analysis, "Vowels");
                vowels.Abbreviation.set_String(analysis, "V");
                var beforeConsonant = Environment(cache, "/ _ [C]");
                var beforeVowel = Environment(cache, "/ _ [V]");

                var polarity = Feature(cache, "Polarity", "pol", ("negative", "neg"), ("positive", "pos"));
                var number = Feature(cache, "Number", "num", ("plural", "pl"));

                var verb = services.GetInstance<IPartOfSpeechFactory>().Create();
                cache.LangProject.PartsOfSpeechOA.PossibilitiesOS.Add(verb);
                verb.Name.set_String(analysis, "Verb");
                verb.Abbreviation.set_String(analysis, "V");
                var subject = Slot(cache, verb, "SM");
                var tam = Slot(cache, verb, "TAM");
                var template = services.GetInstance<IMoInflAffixTemplateFactory>().Create();
                verb.AffixTemplatesOS.Add(template);
                template.Name.set_String(analysis, "Verb template");
                template.PrefixSlotsRS.Add(subject);
                template.PrefixSlotsRS.Add(tam);

                var ja = Entry(cache, MoMorphTypeTags.kguidMorphPrefix, "ja", beforeConsonant);
                Allomorph(cache, ja, MoMorphTypeTags.kguidMorphPrefix, "j", beforeVowel);
                var jaRequired = services.GetInstance<IFsFeatStrucFactory>().Create();
                ((IMoAffixAllomorph)ja.LexemeFormOA).MsEnvFeaturesOA = jaRequired;
                Value(cache, jaRequired, polarity, "negative");
                var jaMsa = Affix(cache, ja, verb, tam, "NEG.PERF", "not yet");

                var wa = Entry(cache, MoMorphTypeTags.kguidMorphPrefix, "wa", null);
                var waMsa = Affix(cache, wa, verb, subject, "3PL", null);
                waMsa.InflFeatsOA = services.GetInstance<IFsFeatStrucFactory>().Create();
                Value(cache, waMsa.InflFeatsOA, number, "plural");

                var kata = services.GetInstance<ILexEntryFactory>().Create();
                var kat = services.GetInstance<IMoStemAllomorphFactory>().Create();
                kata.LexemeFormOA = kat;
                kat.MorphTypeRA = services.GetInstance<IMoMorphTypeRepository>().GetObject(MoMorphTypeTags.kguidMorphRoot);
                kat.Form.set_String(cache.DefaultVernWs, "kat");
                kata.CitationForm.set_String(cache.DefaultVernWs, "kata");
                var stemMsa = services.GetInstance<IMoStemMsaFactory>().Create();
                kata.MorphoSyntaxAnalysesOC.Add(stemMsa);
                stemMsa.PartOfSpeechRA = verb;
                var cut = services.GetInstance<ILexSenseFactory>().Create();
                kata.SensesOS.Add(cut);
                cut.Gloss.set_String(analysis, "cut");
                cut.MorphoSyntaxAnalysisRA = stemMsa;

                var harmony = services.GetInstance<IPhRegularRuleFactory>().Create();
                phonology.PhonRulesOS.Add(harmony);
                harmony.Name.set_String(analysis, "Vowel harmony");

                built = new Grammar(verb, ja, jaMsa, jaRequired, beforeConsonant, waMsa, kata, harmony);
            });
            return built;
        }

        private static IPhEnvironment Environment(LcmCache cache, string representation)
        {
            var environment = cache.ServiceLocator.GetInstance<IPhEnvironmentFactory>().Create();
            cache.LangProject.PhonologicalDataOA.EnvironmentsOS.Add(environment);
            environment.StringRepresentation = SIL.LCModel.Core.Text.TsStringUtils.MakeString(
                representation, cache.DefaultVernWs);
            return environment;
        }

        private static IFsClosedFeature Feature(LcmCache cache, string name, string abbreviation,
            params (string Name, string Abbreviation)[] values)
        {
            var feature = cache.ServiceLocator.GetInstance<IFsClosedFeatureFactory>().Create();
            cache.LangProject.MsFeatureSystemOA.FeaturesOC.Add(feature);
            feature.Name.set_String(cache.DefaultAnalWs, name);
            feature.Abbreviation.set_String(cache.DefaultAnalWs, abbreviation);
            foreach (var (valueName, valueAbbreviation) in values)
            {
                var value = cache.ServiceLocator.GetInstance<IFsSymFeatValFactory>().Create();
                feature.ValuesOC.Add(value);
                value.Name.set_String(cache.DefaultAnalWs, valueName);
                value.Abbreviation.set_String(cache.DefaultAnalWs, valueAbbreviation);
            }
            return feature;
        }

        private static void Value(LcmCache cache, IFsFeatStruc structure, IFsClosedFeature feature, string value)
        {
            var specification = cache.ServiceLocator.GetInstance<IFsClosedValueFactory>().Create();
            structure.FeatureSpecsOC.Add(specification);
            specification.FeatureRA = feature;
            specification.ValueRA = feature.ValuesOC.Single(candidate => candidate.Name.BestAnalysisAlternative.Text == value);
        }

        private static IMoInflAffixSlot Slot(LcmCache cache, IPartOfSpeech category, string name)
        {
            var slot = cache.ServiceLocator.GetInstance<IMoInflAffixSlotFactory>().Create();
            category.AffixSlotsOC.Add(slot);
            slot.Name.set_String(cache.DefaultAnalWs, name);
            return slot;
        }

        private static ILexEntry Entry(LcmCache cache, Guid morphType, string form, IPhEnvironment? environment)
        {
            var entry = cache.ServiceLocator.GetInstance<ILexEntryFactory>().Create();
            Allomorph(cache, entry, morphType, form, environment, lexemeForm: true);
            return entry;
        }

        // LibLCM sets an allomorph's morph type only once the allomorph has an owner.
        private static void Allomorph(LcmCache cache, ILexEntry entry, Guid morphType, string form,
            IPhEnvironment? environment, bool lexemeForm = false)
        {
            var allomorph = cache.ServiceLocator.GetInstance<IMoAffixAllomorphFactory>().Create();
            if (lexemeForm) entry.LexemeFormOA = allomorph;
            else entry.AlternateFormsOS.Add(allomorph);
            allomorph.MorphTypeRA = cache.ServiceLocator.GetInstance<IMoMorphTypeRepository>().GetObject(morphType);
            allomorph.Form.set_String(cache.DefaultVernWs, form);
            if (environment is not null) allomorph.PhoneEnvRC.Add(environment);
        }

        private static IMoInflAffMsa Affix(LcmCache cache, ILexEntry entry, IPartOfSpeech category,
            IMoInflAffixSlot slot, string gloss, string? definition)
        {
            var msa = cache.ServiceLocator.GetInstance<IMoInflAffMsaFactory>().Create(
                entry, SandboxGenericMSA.Create(MsaType.kInfl, category));
            msa.SlotsRC.Add(slot);
            var sense = cache.ServiceLocator.GetInstance<ILexSenseFactory>().Create();
            entry.SensesOS.Add(sense);
            sense.Gloss.set_String(cache.DefaultAnalWs, gloss);
            if (definition is not null)
                sense.Definition.set_String(cache.DefaultAnalWs,
                    SIL.LCModel.Core.Text.TsStringUtils.MakeString(definition, cache.DefaultAnalWs));
            sense.MorphoSyntaxAnalysisRA = msa;
            return msa;
        }
    }
}
