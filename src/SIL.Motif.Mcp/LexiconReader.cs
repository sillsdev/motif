using System.Text.Json.Nodes;
using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.Motif.Projection;
using SIL.Motif.Runner.Retirement;

namespace SIL.Motif.Mcp;

/// <summary>
/// Reads lexical entries from an already-open Baseline copy: headword, morph type, senses with their
/// grammatical category, and (in detail) every allomorph with its environments. The ids it returns are the
/// ones a composer's intent names.
/// </summary>
internal static class LexiconReader
{
    /// <summary>
    /// A page of the entries matching <paramref name="filter"/>, ordered by headword, with counts for the whole
    /// matched set: entries by morph type and morpho-syntactic analyses by category.
    /// </summary>
    /// <param name="cache">The open Baseline copy.</param>
    /// <param name="filter">The optional, combinable conditions an entry must meet.</param>
    /// <param name="limit">The most entries one page returns.</param>
    /// <param name="offset">How many matching entries to skip.</param>
    /// <param name="detailed">Whether entries carry every sense, allomorph, environment and grammatical-info record.</param>
    public static JsonObject Read(LcmCache cache, LexiconFilter filter, int limit, int offset, bool detailed)
    {
        offset = Math.Max(0, offset);
        var all = cache.ServiceLocator.GetInstance<ILexEntryRepository>().AllInstances().ToList();
        var names = new Dictionary<Guid, string?>();
        string? CategoryName(IPartOfSpeech pos)
        {
            if (!names.TryGetValue(pos.Guid, out var name)) names[pos.Guid] = name = GrammarReader.Text(cache, pos.Name);
            return name;
        }
        var typeNames = new Dictionary<Guid, string?>();
        string? MorphName(IMoForm? form)
        {
            if (form?.MorphTypeRA is not { } type) return null;
            if (!typeNames.TryGetValue(type.Guid, out var name)) typeNames[type.Guid] = name = GrammarReader.Text(cache, type.Name);
            return name;
        }
        var headwords = all.ToDictionary(entry => entry, entry => entry.HeadWord?.Text);
        var matches = all.Where(entry => filter.Matches(cache, entry, headwords[entry], CategoryName))
            .OrderBy(entry => headwords[entry], StringComparer.Ordinal).ToList();
        var page = matches.Skip(offset).Take(limit).Select(entry => detailed ? Entry(cache, entry, true) : Line(cache, entry, CategoryName, MorphName)).ToArray();
        var result = new JsonObject
        {
            ["matched"] = matches.Count,
            ["entriesInProject"] = all.Count,
            ["facets"] = new JsonObject
            {
                ["morphTypes"] = Counts(matches.Select(entry => MorphName(entry.LexemeFormOA) ?? "(none)")),
                ["categories"] = Counts(matches.SelectMany(entry => LexiconFilter.MsaCategories(entry))
                    .Select(category => CategoryName(category) ?? "(unnamed)")),
            },
            ["showing"] = $"Showing {page.Length} of {matches.Count} entries from offset {offset}.",
        };
        if (!detailed) HoistSharedGlossWs(page, result);
        result["offset"] = offset;
        result["returned"] = page.Length;
        result["entries"] = new JsonArray(page.Select(entry => (JsonNode)entry).ToArray());
        return result;
    }

    // One writing system for every gloss on the page is stated once, so a page of lines stays small.
    private static void HoistSharedGlossWs(JsonObject[] page, JsonObject result)
    {
        var senses = page.SelectMany(line => line["senses"]!.AsArray()).Select(sense => sense!.AsObject()).ToList();
        var tags = senses.Select(sense => sense["glossWs"]?.GetValue<string>()).Distinct().ToList();
        if (tags.Count != 1 || tags[0] is null) return;
        foreach (var sense in senses) sense.Remove("glossWs");
        result["glossWs"] = tags[0];
    }

    private static JsonObject Counts(IEnumerable<string> names)
    {
        var result = new JsonObject();
        foreach (var group in names.GroupBy(name => name, StringComparer.Ordinal)
                     .OrderByDescending(group => group.Count()).ThenBy(group => group.Key, StringComparer.Ordinal))
            result[group.Key] = group.Count();
        return result;
    }

    private const int LineSenseCap = 5;

    private static JsonObject Line(LcmCache cache, ILexEntry entry, Func<IPartOfSpeech, string?> categoryName, Func<IMoForm?, string?> morphName)
    {
        var senses = entry.AllSenses.ToList();
        var line = new JsonObject
        {
            ["id"] = GrammarReader.Id(entry),
            ["headword"] = entry.HeadWord?.Text,
            ["morphType"] = morphName(entry.LexemeFormOA),
            ["categories"] = new JsonArray(LexiconFilter.MsaCategories(entry).DistinctBy(category => category.Guid)
                .Select(category => (JsonNode?)categoryName(category)).ToArray()),
            ["senses"] = new JsonArray(senses.Take(LineSenseCap).Select(sense =>
            {
                var gloss = WritingSystemTextReader.BestAnalysis(cache, sense.Gloss);
                return (JsonNode)new JsonObject { ["id"] = GrammarReader.Id(sense), ["gloss"] = gloss.Text, ["glossWs"] = gloss.WritingSystem };
            }).ToArray()),
        };
        if (senses.Count > LineSenseCap) line["moreSenses"] = senses.Count - LineSenseCap;
        return line;
    }

    private static JsonObject Entry(LcmCache cache, ILexEntry entry, bool detailed)
    {
        var result = new JsonObject
        {
            ["id"] = GrammarReader.Id(entry),
            ["headword"] = entry.HeadWord?.Text,
            ["morphType"] = MorphTypeName(cache, entry.LexemeFormOA),
            ["morphTypeId"] = entry.LexemeFormOA?.MorphTypeRA is { } morphType ? GrammarReader.Id(morphType) : null,
            ["lexemeForm"] = entry.LexemeFormOA is null ? null : FormObject(cache, entry.LexemeFormOA, detailed),
            ["senses"] = new JsonArray(entry.AllSenses.Select(sense => (JsonNode)Sense(cache, sense)).ToArray()),
        };
        var alternates = entry.AlternateFormsOS.Select(form => (JsonNode)FormObject(cache, form, detailed)).ToArray();
        if (detailed) result["alternateForms"] = new JsonArray(alternates);
        else result["alternateFormCount"] = alternates.Length;
        return result;
    }

    private static JsonObject Sense(LcmCache cache, ILexSense sense) => new()
    {
        ["id"] = GrammarReader.Id(sense),
        ["gloss"] = WritingSystemTextReader.BestAnalysis(cache, sense.Gloss).Text,
        ["glossWs"] = WritingSystemTextReader.BestAnalysis(cache, sense.Gloss).WritingSystem,
        ["grammaticalInfo"] = sense.MorphoSyntaxAnalysisRA is { } msa
            ? new JsonObject { ["id"] = GrammarReader.Id(msa), ["kind"] = msa.ClassName } : null,
    };

    private static JsonObject FormObject(LcmCache cache, IMoForm form, bool detailed)
    {
        var text = WritingSystemTextReader.First(cache, form.Form);
        var result = new JsonObject
        {
            ["id"] = GrammarReader.Id(form),
            ["form"] = text.Text,
            ["ws"] = text.WritingSystem,
        };
        if (detailed)
        {
            result["formKind"] = form.ClassName;
            result["morphType"] = MorphTypeName(cache, form);
            if (form is IMoAffixAllomorph retirableAffix)
                result["semanticDigest"] = AllomorphRetirementSemanticDigest.Compute(cache, retirableAffix);
            var environments = form switch
            {
                IMoStemAllomorph stem => stem.PhoneEnvRC.Select(env => env.StringRepresentation?.Text),
                IMoAffixAllomorph affix => affix.PhoneEnvRC.Select(env => env.StringRepresentation?.Text),
                _ => [],
            };
            result["environments"] = new JsonArray(environments.Select(env => (JsonNode?)env).ToArray());
        }
        return result;
    }

    private static string? FormText(LcmCache cache, IMoForm form) => WritingSystemTextReader.First(cache, form.Form).Text;

    private static string? MorphTypeName(LcmCache cache, IMoForm? form) =>
        form?.MorphTypeRA is { } type ? GrammarReader.Text(cache, type.Name) : null;
}

/// <summary>
/// The optional, combinable conditions of a lexicon read. Text comparisons ignore case and Unicode
/// normalization form, as Motif compares forms elsewhere.
/// </summary>
/// <param name="Query">Text the headword, a gloss or an alternate form contains.</param>
/// <param name="MorphTypesOrNull">Morph type names an entry's lexeme form may have; none means any.</param>
/// <param name="Affix">True keeps affix entries, false keeps the rest.</param>
/// <param name="Category">Name or canonical id of a category some analysis of the entry names.</param>
/// <param name="Headword">Text the headword starts with.</param>
/// <param name="HeadwordContains">Text the headword contains.</param>
/// <param name="HasGloss">True keeps entries with a gloss, false keeps those without one.</param>
internal sealed record LexiconFilter(string? Query = null, IReadOnlyList<string>? MorphTypesOrNull = null, bool? Affix = null,
    string? Category = null, string? Headword = null, string? HeadwordContains = null, bool? HasGloss = null)
{
    public bool Matches(LcmCache cache, ILexEntry entry, string? entryHeadword, Func<IPartOfSpeech, string?> categoryName)
    {
        if (MorphTypesOrNull is { Count: > 0 } types)
        {
            var morphType = entry.LexemeFormOA?.MorphTypeRA is { } type ? GrammarReader.Text(cache, type.Name) : null;
            if (!types.Any(name => Same(name, morphType))) return false;
        }
        if (Affix is { } wantAffix && (entry.LexemeFormOA is IMoAffixForm) != wantAffix) return false;
        if (Category is { } category && !MsaCategories(entry).Any(pos =>
                Same(category, categoryName(pos)) || string.Equals(category, GrammarReader.Id(pos), StringComparison.Ordinal)))
            return false;
        var headword = Fold(entryHeadword);
        if (Headword is { } start && !headword.StartsWith(Fold(start), StringComparison.Ordinal)) return false;
        if (HeadwordContains is { } part && !headword.Contains(Fold(part), StringComparison.Ordinal)) return false;
        if (HasGloss is { } wantGloss &&
            entry.AllSenses.Any(sense => !string.IsNullOrEmpty(GrammarReader.Text(cache, sense.Gloss))) != wantGloss)
            return false;
        if (Query is { } text) return Folded(text, headword, cache, entry);
        return true;
    }

    private static bool Folded(string query, string headword, LcmCache cache, ILexEntry entry)
    {
        var needle = Fold(query);
        return headword.Contains(needle, StringComparison.Ordinal)
            || entry.AllSenses.Any(sense => Fold(GrammarReader.Text(cache, sense.Gloss)).Contains(needle, StringComparison.Ordinal))
            || entry.AlternateFormsOS.Any(form => Fold(WritingSystemTextReader.First(cache, form.Form).Text)
                .Contains(needle, StringComparison.Ordinal));
    }

    /// <summary>Every category any analysis of <paramref name="entry"/> names, including a derivation's source and target.</summary>
    public static IEnumerable<IPartOfSpeech> MsaCategories(ILexEntry entry) => entry.MorphoSyntaxAnalysesOC.SelectMany(msa => msa switch
    {
        IMoStemMsa stem => new[] { stem.PartOfSpeechRA },
        IMoInflAffMsa infl => new[] { infl.PartOfSpeechRA },
        IMoDerivAffMsa deriv => new[] { deriv.FromPartOfSpeechRA, deriv.ToPartOfSpeechRA },
        IMoDerivStepMsa step => new[] { step.PartOfSpeechRA },
        IMoUnclassifiedAffixMsa unclassified => new[] { unclassified.PartOfSpeechRA },
        _ => [],
    }).OfType<IPartOfSpeech>();

    private static bool Same(string left, string? right) => Fold(left) == Fold(right);

    private static string Fold(string? text) => (text ?? string.Empty).Trim().Normalize(System.Text.NormalizationForm.FormD).ToLowerInvariant();
}
