using System.Text.Json.Nodes;
using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Projection;

namespace SIL.Motif.Mcp;

/// <summary>
/// Reads a project's grammar objects, the ones no catalogued command exposes, from an already-open Baseline
/// copy. Every item carries the canonical id a composer's intent names, beside the label a person would say.
/// </summary>
internal static class GrammarReader
{
    public static readonly IReadOnlyList<string> Kinds =
    [
        "categories", "phonemes", "natural_classes", "environments", "phonological_rules", "features", "phonological_features",
        "inflection_classes", "templates", "strata", "morph_types",
    ];

    /// <summary>Counts of every kind, so an agent learns what the grammar holds before it asks for any of it.</summary>
    public static JsonObject Summary(LcmCache cache)
    {
        var counts = new JsonObject();
        foreach (var kind in Kinds) counts[kind] = Items(cache, kind, detailed: false).Count();
        return new JsonObject { ["counts"] = counts, ["kinds"] = new JsonArray(Kinds.Select(k => (JsonNode)k).ToArray()) };
    }

    /// <summary>The items of <paramref name="kind"/> whose label contains <paramref name="query"/>, if one is given.</summary>
    public static JsonObject Read(LcmCache cache, string kind, string? query, bool detailed)
    {
        var all = Items(cache, kind, detailed)
            .Where(item => string.IsNullOrWhiteSpace(query) || Matches(item, query)).ToList();
        return new JsonObject { ["kind"] = kind, ["total"] = all.Count, ["items"] = new JsonArray(all.ToArray()) };
    }

    private static bool Matches(JsonObject item, string query) =>
        item["name"]?.ToString().Contains(query, StringComparison.OrdinalIgnoreCase) == true ||
        item["abbreviation"]?.ToString().Contains(query, StringComparison.OrdinalIgnoreCase) == true;

    private static IEnumerable<JsonObject> Items(LcmCache cache, string kind, bool detailed) => kind switch
    {
        "categories" => Categories(cache).Select(pos => Item(cache, pos, pos.Name, pos.Abbreviation,
            detailed ? new() { ["parent"] = (pos.Owner as ICmPossibility)?.Name.BestAnalysisAlternative.Text } : null)),
        "phonemes" => Phonemes(cache).Select(phoneme => Item(cache, phoneme, phoneme.Name, null, new()
        {
            ["representations"] = new JsonArray(phoneme.CodesOS
                .Select(code => (JsonNode?)Text(cache, code.Representation)).ToArray()),
        })),
        "natural_classes" => cache.LangProject.PhonologicalDataOA.NaturalClassesOS.Select(natural =>
            Item(cache, natural, natural.Name, natural.Abbreviation, NaturalClassFacts(natural, detailed))),
        "environments" => cache.LangProject.PhonologicalDataOA.EnvironmentsOS.Select(environment =>
            Item(cache, environment, environment.Name, null, new()
            {
                ["pattern"] = environment.StringRepresentation?.Text,
            })),
        "phonological_rules" => cache.LangProject.PhonologicalDataOA.PhonRulesOS.Select(rule =>
            Item(cache, rule, rule.Name, null, new()
            {
                ["ruleKind"] = rule.ClassName,
                ["disabled"] = (rule as IPhSegmentRule)?.Disabled,
            })),
        "features" => cache.LangProject.MsFeatureSystemOA.FeaturesOC.Select(feature =>
            Item(cache, feature, feature.Name, feature.Abbreviation, FeatureFacts(feature))),
        "phonological_features" => cache.LangProject.PhFeatureSystemOA.FeaturesOC.Select(feature =>
            Item(cache, feature, feature.Name, feature.Abbreviation, FeatureFacts(feature))),
        "inflection_classes" => Categories(cache).SelectMany(pos => pos.InflectionClassesOC).Select(inflection =>
            Item(cache, inflection, inflection.Name, inflection.Abbreviation, new()
            {
                ["category"] = (inflection.Owner as ICmPossibility)?.Name.BestAnalysisAlternative.Text,
            })),
        "templates" => Categories(cache).SelectMany(pos => pos.AffixTemplatesOS).Select(template =>
            Item(cache, template, template.Name, null, TemplateFacts(cache, template))),
        "strata" => cache.LangProject.MorphologicalDataOA.StrataOS.Select(stratum =>
            Item(cache, stratum, stratum.Name, stratum.Abbreviation, null)),
        "morph_types" => cache.LangProject.LexDbOA.MorphTypesOA.PossibilitiesOS.Select(type =>
            Item(cache, type, type.Name, type.Abbreviation, null)),
        _ => throw new ArgumentException($"Unknown grammar kind '{kind}'."),
    };

    private static JsonObject Item(LcmCache cache, ICmObject target, IMultiAccessorBase? name,
        IMultiAccessorBase? abbreviation, JsonObject? facts)
    {
        var item = new JsonObject { ["id"] = Id(target), ["name"] = Text(cache, name) };
        if (abbreviation is not null && Text(cache, abbreviation) is { Length: > 0 } abbr) item["abbreviation"] = abbr;
        if (facts is not null)
            foreach (var (key, value) in facts.ToList())
            {
                facts.Remove(key);
                item[key] = value;
            }
        return item;
    }

    private static JsonObject NaturalClassFacts(IPhNaturalClass natural, bool detailed)
    {
        var facts = new JsonObject { ["classKind"] = natural.ClassName };
        if (natural is IPhNCSegments segments)
        {
            facts["memberCount"] = segments.SegmentsRC.Count;
            if (detailed)
                facts["members"] = new JsonArray(segments.SegmentsRC
                    .Select(phoneme => (JsonNode?)phoneme.Name.BestAnalysisAlternative.Text).ToArray());
        }
        return facts;
    }

    private static JsonObject FeatureFacts(IFsFeatDefn feature)
    {
        var facts = new JsonObject { ["featureKind"] = feature.ClassName };
        if (feature is IFsClosedFeature closed)
            facts["values"] = new JsonArray(closed.ValuesOC
                .Select(value => (JsonNode?)new JsonObject
                {
                    ["id"] = Id(value),
                    ["name"] = value.Name.BestAnalysisAlternative.Text,
                }).ToArray());
        return facts;
    }

    private static JsonObject TemplateFacts(LcmCache cache, IMoInflAffixTemplate template) => new()
    {
        ["category"] = (template.Owner as ICmPossibility)?.Name.BestAnalysisAlternative.Text,
        ["final"] = template.Final,
        ["prefixSlots"] = Slots(cache, template.PrefixSlotsRS),
        ["suffixSlots"] = Slots(cache, template.SuffixSlotsRS),
    };

    private static JsonArray Slots(LcmCache cache, IEnumerable<IMoInflAffixSlot> slots) => new(slots
        .Select(slot => (JsonNode?)new JsonObject
        {
            ["id"] = Id(slot),
            ["name"] = Text(cache, slot.Name),
            ["optional"] = slot.Optional,
        }).ToArray());

    private static IEnumerable<IPartOfSpeech> Categories(LcmCache cache)
    {
        IEnumerable<IPartOfSpeech> Walk(IEnumerable<ICmPossibility> possibilities) => possibilities
            .OfType<IPartOfSpeech>().SelectMany(pos => new[] { pos }.Concat(Walk(pos.SubPossibilitiesOS)));
        return Walk(cache.LangProject.PartsOfSpeechOA.PossibilitiesOS);
    }

    private static IEnumerable<IPhPhoneme> Phonemes(LcmCache cache) =>
        cache.LangProject.PhonologicalDataOA.PhonemeSetsOS.SelectMany(set => set.PhonemesOC);

    internal static string Id(ICmObject target) => CanonicalId.FromGuid(target.Guid).Value;

    internal static string? Text(LcmCache cache, IMultiAccessorBase? accessor) =>
        accessor is null ? null : WritingSystemTextReader.BestAnalysis(cache, accessor).Text is { Length: > 0 } text
            ? text : WritingSystemTextReader.First(cache, accessor).Text;
}
