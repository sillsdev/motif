using System.Web;
using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.PanGloss;

/// <summary>
/// Pins where a FieldWorks link lands for each kind of object the window can name, and the name of the
/// FieldWorks tool it opens, so a link can say where it goes before anyone clicks it.
/// </summary>
[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group1)]
public sealed class FieldWorksLinksTests : IDisposable
{
    private readonly LcmCache _cache;
    private readonly SeededProject _seed;

    public FieldWorksLinksTests(PristineProjectFixture pristine)
    {
        _cache = pristine.NewScratch();
        _seed = pristine.Seed;
    }

    public void Dispose()
    {
        if (!_cache.IsDisposed) _cache.Dispose();
    }

    [Theory]
    [InlineData("entry", "lexiconEdit", "Lexicon Edit", "entry")]
    [InlineData("sense", "lexiconEdit", "Lexicon Edit", "entry")]
    [InlineData("allomorph", "lexiconEdit", "Lexicon Edit", "entry")]
    [InlineData("affix process", "lexiconEdit", "Lexicon Edit", "entry")]
    [InlineData("category", "posEdit", "Category Edit", "category")]
    [InlineData("affix template", "posEdit", "Category Edit", "category")]
    [InlineData("template slot", "posEdit", "Category Edit", "category")]
    [InlineData("environment", "EnvironmentEdit", "Environments", "self")]
    [InlineData("natural class", "naturalClassedit", "Natural Classes", "self")]
    [InlineData("phoneme", "phonemeEdit", "Phonemes", "self")]
    [InlineData("phonological rule", "PhonologicalRuleEdit", "Phonological Rules", "self")]
    [InlineData("metathesis rule", "PhonologicalRuleEdit", "Phonological Rules", "self")]
    [InlineData("endocentric compound rule", "compoundRuleAdvancedEdit", "Compound Rules", "self")]
    [InlineData("exocentric compound rule", "compoundRuleAdvancedEdit", "Compound Rules", "self")]
    [InlineData("allomorph ad hoc rule", "AdhocCoprohibEdit", "Ad hoc Rules", "self")]
    [InlineData("morpheme ad hoc rule", "AdhocCoprohibEdit", "Ad hoc Rules", "self")]
    [InlineData("wordform", "Analyses", "Word Analyses", "self")]
    public void EachKindOpensInItsToolAndTheLinkNamesThatTool(
        string kind, string tool, string toolName, string landsOn)
    {
        var (found, entry, category) = Create(kind);

        var target = FieldWorksLinks.TargetFor(_cache, found);

        Assert.NotNull(target);
        Assert.Equal(tool, target.Tool);
        var expected = landsOn switch { "entry" => entry!.Guid, "category" => category!.Guid, _ => found.Guid };
        Assert.Equal(expected, target.ObjectId);
        Assert.Equal(toolName, FieldWorksLinks.ToolName(target.Tool));
        var link = FieldWorksLinks.ForTarget("Sena 3", target);
        Assert.Equal(tool, FieldWorksLinks.ToolOf(link));
        Assert.Equal(toolName, FieldWorksLinks.ToolNameOf(link));
    }

    [Fact]
    public void AStratumHasNoFieldWorksToolSoItHasNoLink()
    {
        IMoStratum stratum = null!;
        Do(() =>
        {
            stratum = _cache.ServiceLocator.GetInstance<IMoStratumFactory>().Create();
            _cache.LangProject.MorphologicalDataOA.StrataOS.Add(stratum);
        });

        Assert.Null(FieldWorksLinks.TargetFor(_cache, stratum));
    }

    [Fact]
    public void AnAllomorphsEnvironmentsEachOpenOnceInEnvironments_InfixPositionsLast()
    {
        IPhEnvironment before = null!, after = null!, position = null!;
        IMoAffixAllomorph allomorph = null!;
        Do(() =>
        {
            before = Environment("/ _ a");
            after = Environment("/ i _");
            position = Environment("/ # C _");
            var entry = Entry(MoMorphTypeTags.kguidMorphInfix, "um");
            allomorph = (IMoAffixAllomorph)entry.LexemeFormOA;
            allomorph.PhoneEnvRC.Add(before);
            allomorph.PhoneEnvRC.Add(after);
            allomorph.PositionRS.Add(position);
            allomorph.PositionRS.Add(before);
        });

        var targets = FieldWorksLinks.EnvironmentTargetsFor(allomorph);

        Assert.All(targets, target => Assert.Equal("EnvironmentEdit", target.Tool));
        Assert.Equal(
            new[] { before.Guid, after.Guid, position.Guid }.Order(),
            targets.Select(target => target.ObjectId).Order());
        Assert.Equal(position.Guid, targets[^1].ObjectId);
    }

    [Fact]
    public void AStemAllomorphWithNoEnvironmentHasNoEnvironmentTargets()
    {
        var stem = _cache.ServiceLocator.GetInstance<ILexEntryRepository>().GetObject(_seed.FirstEntryId).LexemeFormOA;

        Assert.Empty(FieldWorksLinks.EnvironmentTargetsFor(stem));
    }

    [Fact]
    public void ANaturalClassAnEnvironmentNamesOpensInNaturalClasses_AndAnUnknownOneHasNoTarget()
    {
        IPhNaturalClass consonants = null!;
        Do(() =>
        {
            consonants = _cache.ServiceLocator.GetInstance<IPhNCSegmentsFactory>().Create();
            _cache.LangProject.PhonologicalDataOA.NaturalClassesOS.Add(consonants);
            consonants.Abbreviation.set_String(_cache.DefaultAnalWs, "C");
            consonants.Name.set_String(_cache.DefaultAnalWs, "Consonants");
        });

        var target = FieldWorksLinks.NaturalClassTargetFor(_cache, "C");

        Assert.Equal(new FieldWorksLinkTarget("naturalClassedit", consonants.Guid), target);
        Assert.Null(FieldWorksLinks.NaturalClassTargetFor(_cache, "V"));
    }

    [Theory]
    [InlineData("silfw://localhost/link?database%3dp%26tool%3dlexiconEdit%26guid%3dx%26tag%3d", "lexiconEdit")]
    [InlineData("silfw://localhost/link?database=Sena%203&tool=phonemeEdit&guid=x&tag=", "phonemeEdit")]
    [InlineData("silfw://localhost/link?tool=Analyses", "Analyses")]
    [InlineData("silfw://motif.test/project/entry-1", null)]
    [InlineData("not a link", null)]
    [InlineData(null, null)]
    public void TheToolIsReadFromTheLinkItself_WhetherOrNotItsQueryIsEncodedWhole(string? link, string? tool) =>
        Assert.Equal(tool, FieldWorksLinks.ToolOf(link));

    [Theory]
    [InlineData("phonologicalFeaturesAdvancedEdit", "Phonological Features")]
    [InlineData("featuresAdvancedEdit", "Inflection Features")]
    [InlineData("variantEntryTypeEdit", "Variant Types")]
    [InlineData("ProdRestrictEdit", "Exception \"Features\"")]
    [InlineData("someFutureTool", "FieldWorks")]
    [InlineData(null, "FieldWorks")]
    public void ToolsReachedOnlyThroughTheirOwnersAndUnknownToolsAreNamed(string? tool, string name) =>
        Assert.Equal(name, FieldWorksLinks.ToolName(tool));

    private (ICmObject Found, ILexEntry? Entry, IPartOfSpeech? Category) Create(string kind)
    {
        var services = _cache.ServiceLocator;
        var entry = services.GetInstance<ILexEntryRepository>().GetObject(_seed.FirstEntryId);
        var category = services.GetInstance<IPartOfSpeechRepository>().GetObject(_seed.PartOfSpeechId);
        var phonology = _cache.LangProject.PhonologicalDataOA;
        var morphology = _cache.LangProject.MorphologicalDataOA;
        ICmObject found = null!;
        Do(() =>
        {
            switch (kind)
            {
                case "entry": found = entry; break;
                case "sense": found = entry.SensesOS[0]; break;
                case "allomorph": found = entry.LexemeFormOA; break;
                case "affix process":
                    var process = services.GetInstance<IMoAffixProcessFactory>().Create();
                    entry.AlternateFormsOS.Add(process);
                    found = process;
                    break;
                case "category": found = category; break;
                case "affix template":
                    var template = services.GetInstance<IMoInflAffixTemplateFactory>().Create();
                    category.AffixTemplatesOS.Add(template);
                    found = template;
                    break;
                case "template slot":
                    var slot = services.GetInstance<IMoInflAffixSlotFactory>().Create();
                    category.AffixSlotsOC.Add(slot);
                    found = slot;
                    break;
                case "environment": found = Environment("/ _ a"); break;
                case "natural class":
                    var naturalClass = services.GetInstance<IPhNCSegmentsFactory>().Create();
                    phonology.NaturalClassesOS.Add(naturalClass);
                    found = naturalClass;
                    break;
                case "phoneme":
                    if (phonology.PhonemeSetsOS.Count == 0)
                        phonology.PhonemeSetsOS.Add(services.GetInstance<IPhPhonemeSetFactory>().Create());
                    var phoneme = services.GetInstance<IPhPhonemeFactory>().Create();
                    phonology.PhonemeSetsOS[0].PhonemesOC.Add(phoneme);
                    found = phoneme;
                    break;
                case "phonological rule":
                    var regular = services.GetInstance<IPhRegularRuleFactory>().Create();
                    phonology.PhonRulesOS.Add(regular);
                    found = regular;
                    break;
                case "metathesis rule":
                    var metathesis = services.GetInstance<IPhMetathesisRuleFactory>().Create();
                    phonology.PhonRulesOS.Add(metathesis);
                    found = metathesis;
                    break;
                case "endocentric compound rule":
                    var endo = services.GetInstance<IMoEndoCompoundFactory>().Create();
                    morphology.CompoundRulesOS.Add(endo);
                    found = endo;
                    break;
                case "exocentric compound rule":
                    var exo = services.GetInstance<IMoExoCompoundFactory>().Create();
                    morphology.CompoundRulesOS.Add(exo);
                    found = exo;
                    break;
                case "allomorph ad hoc rule":
                    var alloRule = services.GetInstance<IMoAlloAdhocProhibFactory>().Create();
                    morphology.AdhocCoProhibitionsOC.Add(alloRule);
                    found = alloRule;
                    break;
                case "morpheme ad hoc rule":
                    var morphRule = services.GetInstance<IMoMorphAdhocProhibFactory>().Create();
                    morphology.AdhocCoProhibitionsOC.Add(morphRule);
                    found = morphRule;
                    break;
                case "wordform":
                    found = services.GetInstance<IWfiWordformFactory>()
                        .Create(TsStringUtils.MakeString("zzlinkword", _cache.DefaultVernWs));
                    break;
                default: throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
            }
        });
        return (found, entry, category);
    }

    private IPhEnvironment Environment(string representation)
    {
        var environment = _cache.ServiceLocator.GetInstance<IPhEnvironmentFactory>().Create();
        _cache.LangProject.PhonologicalDataOA.EnvironmentsOS.Add(environment);
        environment.StringRepresentation = TsStringUtils.MakeString(representation, _cache.DefaultVernWs);
        return environment;
    }

    private ILexEntry Entry(Guid morphType, string form)
    {
        var type = _cache.ServiceLocator.GetInstance<IMoMorphTypeRepository>().GetObject(morphType);
        var entry = _cache.ServiceLocator.GetInstance<ILexEntryFactory>().Create();
        var allomorph = _cache.ServiceLocator.GetInstance<IMoAffixAllomorphFactory>().Create();
        entry.LexemeFormOA = allomorph;
        allomorph.MorphTypeRA = type;
        allomorph.Form.set_String(_cache.DefaultVernWs, form);
        return entry;
    }

    private void Do(Action action) => NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, action);
}
