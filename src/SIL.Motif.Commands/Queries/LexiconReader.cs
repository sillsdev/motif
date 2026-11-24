using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Projection;
using SIL.Motif.Projection.Grammar;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Runner.Retirement;

namespace SIL.Motif.Commands.Queries;

/// <summary>
/// Reads lexical entries from an already-open Baseline copy: headword, morph type, senses with their
/// grammatical category, and (in detail) every allomorph with its environments. The ids it returns are the
/// ones a composer's intent names.
/// </summary>
public static class LexiconReader
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
    /// <param name="withForms">Whether concise entries also carry the lexeme form, alternate forms and inflection classes.</param>
    public static JsonObject Read(LcmCache cache, LexiconFilter filter, int limit, int offset, bool detailed, bool withForms = false)
    {
        offset = Math.Max(0, offset);
        var all = cache.ServiceLocator.GetInstance<ILexEntryRepository>().AllInstances().ToList();
        var names = new Dictionary<Guid, string?>();
        string? CategoryName(IPartOfSpeech pos)
        {
            if (!names.TryGetValue(pos.Guid, out var name)) names[pos.Guid] = name = Text(cache, pos.Name);
            return name;
        }
        var typeNames = new Dictionary<Guid, string?>();
        string? MorphName(IMoForm? form)
        {
            if (form?.MorphTypeRA is not { } type) return null;
            if (!typeNames.TryGetValue(type.Guid, out var name)) typeNames[type.Guid] = name = Text(cache, type.Name);
            return name;
        }
        var headwords = all.ToDictionary(entry => entry, entry => entry.HeadWord?.Text);
        var matches = all.Where(entry => filter.Matches(cache, entry, headwords[entry], CategoryName))
            .OrderBy(entry => headwords[entry], StringComparer.Ordinal).ToList();
        var page = matches.Skip(offset).Take(limit).Select(entry => detailed ? Entry(cache, entry, true) : Line(cache, entry, CategoryName, MorphName, withForms)).ToArray();
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
        if (filter.MorphTypesOrNull is not { Count: > 0 } &&
            GrammarReadProjectionBuilder.ExactNote(filter.Exact, matches.Select(entry => (headwords[entry], Id(entry)))) is { } note)
            result["exactNote"] = note;
        if (!detailed) HoistSharedGlossWs(page, result);
        result["offset"] = offset;
        result["returned"] = page.Length;
        if (offset + page.Length < matches.Count) result["nextOffset"] = offset + page.Length;
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

    private static JsonObject Line(LcmCache cache, ILexEntry entry, Func<IPartOfSpeech, string?> categoryName, Func<IMoForm?, string?> morphName, bool withForms)
    {
        var senses = entry.AllSenses.ToList();
        var line = new JsonObject
        {
            ["id"] = Id(entry),
            ["headword"] = entry.HeadWord?.Text,
            ["morphType"] = morphName(entry.LexemeFormOA),
            ["categories"] = new JsonArray(LexiconFilter.MsaCategories(entry).DistinctBy(category => category.Guid)
                .Select(category => (JsonNode?)categoryName(category)).ToArray()),
            ["senses"] = new JsonArray(senses.Take(LineSenseCap).Select(sense =>
            {
                var gloss = WritingSystemTextReader.BestAnalysis(cache, sense.Gloss);
                return (JsonNode)new JsonObject { ["id"] = Id(sense), ["gloss"] = gloss.Text, ["glossWs"] = gloss.WritingSystem };
            }).ToArray()),
        };
        if (senses.Count > LineSenseCap) line["moreSenses"] = senses.Count - LineSenseCap;
        if (withForms)
        {
            line["lexemeForm"] = entry.LexemeFormOA is { } lexeme ? FormText(cache, lexeme) : null;
            line["alternateForms"] = new JsonArray(entry.AlternateFormsOS.Select(form => (JsonNode?)FormText(cache, form)).ToArray());
            line["inflectionClasses"] = new JsonArray(LexiconFilter.InflectionClasses(entry).Distinct()
                .Select(inflectionClass => (JsonNode?)new JsonObject { ["id"] = Id(inflectionClass), ["name"] = Text(cache, inflectionClass.Name) }).ToArray());
        }
        return line;
    }

    private static JsonObject Entry(LcmCache cache, ILexEntry entry, bool detailed)
    {
        var result = new JsonObject
        {
            ["id"] = Id(entry),
            ["headword"] = entry.HeadWord?.Text,
            ["note"] = Alternatives(cache, entry.Comment),
            ["morphType"] = MorphTypeName(cache, entry.LexemeFormOA),
            ["morphTypeId"] = entry.LexemeFormOA?.MorphTypeRA is { } morphType ? Id(morphType) : null,
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
        ["id"] = Id(sense),
        ["gloss"] = WritingSystemTextReader.BestAnalysis(cache, sense.Gloss).Text,
        ["glossWs"] = WritingSystemTextReader.BestAnalysis(cache, sense.Gloss).WritingSystem,
        ["glossAlternatives"] = Alternatives(cache, sense.Gloss),
        ["definition"] = Alternatives(cache, sense.Definition),
        ["notes"] = new JsonObject
        {
            ["anthropology-note"] = Alternatives(cache, sense.AnthroNote),
            ["discourse-note"] = Alternatives(cache, sense.DiscourseNote),
            ["general-note"] = Alternatives(cache, sense.GeneralNote),
            ["grammar-note"] = Alternatives(cache, sense.GrammarNote),
            ["phonology-note"] = Alternatives(cache, sense.PhonologyNote),
            ["semantics-note"] = Alternatives(cache, sense.SemanticsNote),
            ["sociolinguistics-note"] = Alternatives(cache, sense.SocioLinguisticsNote),
        },
        ["grammaticalInfo"] = sense.MorphoSyntaxAnalysisRA is { } msa ? GrammaticalInfo(msa) : null,
    };

    /// <summary>An analysis's id and kind, plus <c>maxApps</c> when set.</summary>
    private static JsonObject GrammaticalInfo(IMoMorphSynAnalysis msa)
    {
        var info = new JsonObject { ["id"] = Id(msa), ["kind"] = msa.ClassName };
        if (AffixMaxApps.ReadFor(msa) is var maxApps && maxApps > AffixMaxApps.Default) info["maxApps"] = maxApps;
        return info;
    }

    private static JsonObject Alternatives(LcmCache cache, IMultiAccessorBase accessor)
    {
        var result = new JsonObject();
        foreach (var ws in accessor.AvailableWritingSystemIds.Order())
            if (accessor.get_String(ws)?.Text is { Length: > 0 } text)
                result[cache.WritingSystemFactory.GetStrFromWs(ws)] = text;
        return result;
    }

    private static JsonObject FormObject(LcmCache cache, IMoForm form, bool detailed)
    {
        var text = WritingSystemTextReader.First(cache, form.Form);
        var result = new JsonObject
        {
            ["id"] = Id(form),
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

    internal static string Id(ICmObject target) => CanonicalId.FromGuid(target.Guid).Value;

    internal static string? Text(LcmCache cache, IMultiAccessorBase? accessor) =>
        accessor is null ? null : WritingSystemTextReader.BestAnalysis(cache, accessor).Text is { Length: > 0 } text
            ? text : WritingSystemTextReader.First(cache, accessor).Text;

    internal static string? FormText(LcmCache cache, IMoForm form) => WritingSystemTextReader.First(cache, form.Form).Text;

    private static string? MorphTypeName(LcmCache cache, IMoForm? form) =>
        form?.MorphTypeRA is { } type ? Text(cache, type.Name) : null;
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
/// <param name="Exact">Text the headword or a form equals; a bare form also equals its marked affix headword.</param>
/// <param name="FormRegex">A regular expression some form (headword, lexeme form or alternate form) matches, searched in NFC text.</param>
/// <param name="Suffix">Text some form or the headword ends with, ignoring case and Unicode normalization form.</param>
/// <param name="InflectionClass">Name, abbreviation, canonical id or guid of an inflection class an analysis of the entry (or an ancestor class) names.</param>
public sealed record LexiconFilter(string? Query = null, IReadOnlyList<string>? MorphTypesOrNull = null, bool? Affix = null,
    string? Category = null, string? Headword = null, string? HeadwordContains = null, bool? HasGloss = null,
    string? Exact = null, string? FormRegex = null, string? Suffix = null, string? InflectionClass = null)
{
    /// <summary>The longest one regular-expression match may take, so a pathological pattern is refused rather than hanging a lane.</summary>
    public static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);

    private readonly Regex? _formRegex = FormRegex is null ? null : new Regex(FormRegex, RegexOptions.CultureInvariant, RegexTimeout);

    /// <summary>The reason <paramref name="pattern"/> is not a usable regular expression, or null when it is.</summary>
    public static string? RegexProblem(string pattern)
    {
        try { _ = new Regex(pattern, RegexOptions.CultureInvariant, RegexTimeout); return null; }
        catch (ArgumentException exception) { return exception.Message; }
    }

    public bool Matches(LcmCache cache, ILexEntry entry, string? entryHeadword, Func<IPartOfSpeech, string?> categoryName)
    {
        if (MorphTypesOrNull is { Count: > 0 } types)
        {
            var morphType = entry.LexemeFormOA?.MorphTypeRA is { } type ? LexiconReader.Text(cache, type.Name) : null;
            if (!types.Any(name => Same(name, morphType))) return false;
        }
        if (Affix is { } wantAffix && (entry.LexemeFormOA is IMoAffixForm) != wantAffix) return false;
        if (Category is { } category && !MsaCategories(entry).Any(pos =>
                Same(category, categoryName(pos)) || string.Equals(category, LexiconReader.Id(pos), StringComparison.Ordinal)))
            return false;
        var headword = Fold(entryHeadword);
        if (Headword is { } start && !headword.StartsWith(Fold(start), StringComparison.Ordinal)) return false;
        if (HeadwordContains is { } part && !headword.Contains(Fold(part), StringComparison.Ordinal)) return false;
        if (HasGloss is { } wantGloss &&
            entry.AllSenses.Any(sense => !string.IsNullOrEmpty(LexiconReader.Text(cache, sense.Gloss))) != wantGloss)
            return false;
        if (Exact is { } exact && !FormMatch.Same(exact, entryHeadword) &&
            !FormMatch.Any(exact, entry.LexemeFormOA is { } lexeme
                ? entry.AlternateFormsOS.Prepend(lexeme).Select(form => WritingSystemTextReader.First(cache, form.Form).Text)
                : entry.AlternateFormsOS.Select(form => WritingSystemTextReader.First(cache, form.Form).Text)))
            return false;
        if (_formRegex is not null || Suffix is not null)
        {
            var forms = AllForms(cache, entry, entryHeadword).ToList();
            if (_formRegex is not null && !forms.Any(form => _formRegex.IsMatch(form.Normalize(System.Text.NormalizationForm.FormC)))) return false;
            if (Suffix is { } suffix && !forms.Any(form => Fold(form).EndsWith(Fold(suffix), StringComparison.Ordinal))) return false;
        }
        if (InflectionClass is { } wanted && !InflectionClasses(entry).Any(inflectionClass => ClassMatches(cache, wanted, inflectionClass)))
            return false;
        if (Query is { } text) return Folded(text, headword, cache, entry);
        return true;
    }

    private static IEnumerable<string> AllForms(LcmCache cache, ILexEntry entry, string? headword)
    {
        if (!string.IsNullOrEmpty(headword)) yield return headword;
        if (entry.LexemeFormOA is { } lexeme && LexiconReader.FormText(cache, lexeme) is { Length: > 0 } lexemeText) yield return lexemeText;
        foreach (var form in entry.AlternateFormsOS)
            if (LexiconReader.FormText(cache, form) is { Length: > 0 } text) yield return text;
    }

    /// <summary>Every inflection class an analysis of <paramref name="entry"/> names.</summary>
    public static IEnumerable<IMoInflClass> InflectionClasses(ILexEntry entry) => entry.MorphoSyntaxAnalysesOC.SelectMany(msa => msa switch
    {
        IMoStemMsa stem => new[] { stem.InflectionClassRA },
        IMoDerivAffMsa deriv => new[] { deriv.FromInflectionClassRA, deriv.ToInflectionClassRA },
        _ => [],
    }).OfType<IMoInflClass>();

    // A class matches by its own name, abbreviation or id, or an ancestor's, so a parent finds its subclasses.
    private static bool ClassMatches(LcmCache cache, string wanted, IMoInflClass inflectionClass)
    {
        for (ICmObject? current = inflectionClass; current is IMoInflClass candidate; current = candidate.Owner)
        {
            if (Same(wanted, LexiconReader.Text(cache, candidate.Name)) || Same(wanted, LexiconReader.Text(cache, candidate.Abbreviation)) ||
                string.Equals(wanted, LexiconReader.Id(candidate), StringComparison.Ordinal) ||
                Guid.TryParse(wanted, out var guid) && guid == candidate.Guid)
                return true;
        }
        return false;
    }

    private static bool Folded(string query, string headword, LcmCache cache, ILexEntry entry)
    {
        var needle = Fold(query);
        return headword.Contains(needle, StringComparison.Ordinal)
            || entry.AllSenses.Any(sense => Fold(LexiconReader.Text(cache, sense.Gloss)).Contains(needle, StringComparison.Ordinal))
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
