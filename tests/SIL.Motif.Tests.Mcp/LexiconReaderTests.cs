using System.Text.Json.Nodes;
using SIL.LCModel;
using SIL.LCModel.DomainServices;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Mcp;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Mcp;

/// <summary>The lexicon read filters by morph type, affix-ness, category, headword and gloss, and counts facets.</summary>
[Collection(LcmCacheParallelCollections.Group3)]
public sealed class LexiconReaderTests(PristineProjectFixture pristine) : IDisposable
{
    private readonly LcmCache _cache = pristine.NewScratch();
    private readonly Dictionary<string, Guid> _categories = new();

    public void Dispose() => _cache.Dispose();

    private JsonObject Read(LexiconFilter filter, int limit = 25, int offset = 0, bool detailed = false)
    {
        Fill();
        return LexiconReader.Read(_cache, filter, limit, offset, detailed);
    }

    private bool _filled;

    private void Fill()
    {
        if (_filled) return;
        _filled = true;
        var services = _cache.ServiceLocator;
        var vern = _cache.DefaultVernWs;
        var anal = _cache.DefaultAnalWs;
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            IPartOfSpeech Pos(string name)
            {
                var pos = services.GetInstance<IPartOfSpeechFactory>().Create();
                _cache.LangProject.PartsOfSpeechOA.PossibilitiesOS.Add(pos);
                pos.Name.set_String(anal, name);
                _categories[name] = pos.Guid;
                return pos;
            }
            var noun = Pos("Noun");
            var verb = Pos("Verb");
            IMoMorphType Type(Guid guid) => services.GetInstance<IMoMorphTypeRepository>().GetObject(guid);

            void Stem(string form, Guid type, IPartOfSpeech pos, string? gloss)
            {
                var entry = services.GetInstance<ILexEntryFactory>().Create();
                var lexeme = services.GetInstance<IMoStemAllomorphFactory>().Create();
                entry.LexemeFormOA = lexeme;
                lexeme.MorphTypeRA = Type(type);
                lexeme.Form.set_String(vern, form);
                var msa = services.GetInstance<IMoStemMsaFactory>().Create();
                entry.MorphoSyntaxAnalysesOC.Add(msa);
                msa.PartOfSpeechRA = pos;
                Sense(entry, msa, gloss);
            }

            void Affix(string form, Guid type, IPartOfSpeech pos, string? gloss)
            {
                var entry = services.GetInstance<ILexEntryFactory>().Create();
                var lexeme = services.GetInstance<IMoAffixAllomorphFactory>().Create();
                entry.LexemeFormOA = lexeme;
                lexeme.MorphTypeRA = Type(type);
                lexeme.Form.set_String(vern, form);
                var msa = services.GetInstance<IMoInflAffMsaFactory>().Create();
                entry.MorphoSyntaxAnalysesOC.Add(msa);
                msa.PartOfSpeechRA = pos;
                Sense(entry, msa, gloss);
            }

            void Sense(ILexEntry entry, IMoMorphSynAnalysis msa, string? gloss)
            {
                var sense = services.GetInstance<ILexSenseFactory>().Create();
                entry.SensesOS.Add(sense);
                sense.MorphoSyntaxAnalysisRA = msa;
                if (gloss is not null) sense.Gloss.set_String(anal, gloss);
            }

            Stem("balay", MoMorphTypeTags.kguidMorphStem, noun, "house");
            Stem("mása", MoMorphTypeTags.kguidMorphStem, noun, "table");
            Stem("kaon", MoMorphTypeTags.kguidMorphRoot, verb, "eat");
            Affix("mag", MoMorphTypeTags.kguidMorphPrefix, verb, "actor");
            Affix("an", MoMorphTypeTags.kguidMorphSuffix, noun, "locative");
            Affix("um", MoMorphTypeTags.kguidMorphInfix, verb, null);
        });
    }

    private static string[] Headwords(JsonObject result) => result["entries"]!.AsArray()
        .Select(entry => entry!["headword"]!.GetValue<string>().Trim('-')).Order(StringComparer.Ordinal).ToArray();

    private static Dictionary<string, int> Facet(JsonObject result, string name) => result["facets"]![name]!.AsObject()
        .ToDictionary(pair => pair.Key, pair => pair.Value!.GetValue<int>());

    [Fact]
    public void UnfilteredReadCountsEntriesByMorphTypeAndMsasByCategoryBeforeAnyEntry()
    {
        var result = Read(new LexiconFilter());

        Assert.Equal(["matched", "entriesInProject", "facets"], result.Select(pair => pair.Key).Take(3));
        Assert.Equal(8, result["matched"]!.GetValue<int>());
        Assert.Equal(8, result["entriesInProject"]!.GetValue<int>());
        Assert.Equal(new Dictionary<string, int>
            { ["stem"] = 4, ["root"] = 1, ["prefix"] = 1, ["suffix"] = 1, ["infix"] = 1 }, Facet(result, "morphTypes"));
        Assert.Equal(new Dictionary<string, int> { ["SeededNoun"] = 2, ["Noun"] = 3, ["Verb"] = 3 }, Facet(result, "categories"));
    }

    [Fact]
    public void MorphTypeAcceptsSeveralNamesIgnoringCase()
    {
        var result = Read(new LexiconFilter(MorphTypesOrNull: ["Suffix", "infix"]));

        Assert.Equal(["an", "um"], Headwords(result));
        Assert.Equal(new Dictionary<string, int> { ["suffix"] = 1, ["infix"] = 1 }, Facet(result, "morphTypes"));
    }

    [Fact]
    public void AffixSelectsAnyAffixMorphTypeAndFalseSelectsTheRest()
    {
        Assert.Equal(["an", "mag", "um"], Headwords(Read(new LexiconFilter(Affix: true))));
        Assert.Equal(5, Read(new LexiconFilter(Affix: false))["matched"]!.GetValue<int>());
    }

    [Fact]
    public void CategoryMatchesExactNameOrIdOfAnyMsa()
    {
        Assert.Equal(["an", "balay", "mása".Normalize(System.Text.NormalizationForm.FormD)],
            Headwords(Read(new LexiconFilter(Category: "noun"))));
        var byId = Read(new LexiconFilter(Category: CanonicalIdOf("Verb")));
        Assert.Equal(["kaon", "mag", "um"], Headwords(byId));
        Assert.Equal(0, Read(new LexiconFilter(Category: "Nou"))["matched"]!.GetValue<int>());
    }

    [Fact]
    public void HeadwordMatchesByPrefixOrSubstringAndIgnoresNormalizationForm()
    {
        Assert.Equal(["balay"], Headwords(Read(new LexiconFilter(Headword: "ba"))));
        Assert.Equal(["balay"], Headwords(Read(new LexiconFilter(HeadwordContains: "ala"))));
        Assert.Single(Headwords(Read(new LexiconFilter(Headword: "má"))));
        Assert.Single(Headwords(Read(new LexiconFilter(HeadwordContains: "ás"))));
        Assert.Empty(Headwords(Read(new LexiconFilter(Headword: "ala"))));
    }

    [Fact]
    public void HasGlossSplitsEntriesWithAndWithoutAGloss()
    {
        Assert.Equal(["um"], Headwords(Read(new LexiconFilter(HasGloss: false))));
        Assert.Equal(7, Read(new LexiconFilter(HasGloss: true))["matched"]!.GetValue<int>());
    }

    [Fact]
    public void FiltersCombineAndFacetsDescribeOnlyTheFilteredSet()
    {
        var result = Read(new LexiconFilter(Affix: true, Category: "Verb"));

        Assert.Equal(["mag", "um"], Headwords(result));
        Assert.Equal(2, result["matched"]!.GetValue<int>());
        Assert.Equal(8, result["entriesInProject"]!.GetValue<int>());
        Assert.Equal(new Dictionary<string, int> { ["prefix"] = 1, ["infix"] = 1 }, Facet(result, "morphTypes"));
        Assert.Equal(new Dictionary<string, int> { ["Verb"] = 2 }, Facet(result, "categories"));
    }

    [Fact]
    public void ConciseEntriesAreOneSmallLineObjectAndDetailedKeepsTheFullEntry()
    {
        var page = Read(new LexiconFilter(Headword: "balay"));
        var concise = page["entries"]![0]!.AsObject();
        Assert.Equal("en", page["glossWs"]!.GetValue<string>());
        var detailed = Read(new LexiconFilter(Headword: "balay"), detailed: true)["entries"]![0]!.AsObject();

        Assert.Equal(["id", "headword", "morphType", "categories", "senses"],
            concise.Select(pair => pair.Key));
        Assert.Equal("house", concise["senses"]![0]!["gloss"]!.GetValue<string>());
        Assert.NotNull(concise["senses"]![0]!["id"]);
        Assert.Null(concise["senses"]![0]!["glossWs"]);
        Assert.Equal("Noun", concise["categories"]![0]!.GetValue<string>());
        Assert.True(concise.ToJsonString().Length < 400, concise.ToJsonString());
        Assert.NotNull(detailed["senses"]);
        Assert.NotNull(detailed["lexemeForm"]);
        Assert.NotNull(detailed["alternateForms"]);
    }

    [Fact]
    public void ConciseLineKeepsAtMostFiveSensesAndCountsTheRest()
    {
        Fill();
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            var entry = _cache.ServiceLocator.GetInstance<ILexEntryRepository>().AllInstances()
                .Single(candidate => candidate.HeadWord?.Text == "balay");
            for (var i = 0; i < 6; i++)
            {
                var sense = _cache.ServiceLocator.GetInstance<ILexSenseFactory>().Create();
                entry.SensesOS.Add(sense);
                sense.Gloss.set_String(_cache.DefaultAnalWs, "extra" + i);
            }
        });

        var line = Read(new LexiconFilter(Headword: "balay"))["entries"]![0]!.AsObject();

        Assert.Equal(5, line["senses"]!.AsArray().Count);
        Assert.Equal(2, line["moreSenses"]!.GetValue<int>());
    }

    [Fact]
    public void PagingSaysShowingXOfNAndSkipsWhatItWasToldTo()
    {
        var result = Read(new LexiconFilter(), limit: 3, offset: 6);

        Assert.Equal(2, result["returned"]!.GetValue<int>());
        Assert.Contains("showing 2 of 8", result["showing"]!.GetValue<string>(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AnOffsetPastTheEndIsAnEmptyPageThatStillCountsAndANegativeOneStartsAtZero()
    {
        var past = Read(new LexiconFilter(), offset: 100);

        Assert.Equal(8, past["matched"]!.GetValue<int>());
        Assert.Equal(0, past["returned"]!.GetValue<int>());
        Assert.Empty(past["entries"]!.AsArray());
        Assert.Contains("Showing 0 of 8 entries from offset 100", past["showing"]!.GetValue<string>());
        var negative = Read(new LexiconFilter(), limit: 2, offset: -5);
        Assert.Equal(0, negative["offset"]!.GetValue<int>());
        Assert.Equal(2, negative["returned"]!.GetValue<int>());
    }

    private string CanonicalIdOf(string name)
    {
        Fill();
        return SIL.Motif.Mcp.GrammarReader.Id(
            _cache.ServiceLocator.GetInstance<IPartOfSpeechRepository>().GetObject(_categories[name]));
    }
}
