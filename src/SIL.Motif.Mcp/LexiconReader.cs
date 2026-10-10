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
    /// <summary>A page of the entries matching <paramref name="query"/>, ordered by headword.</summary>
    /// <param name="cache">The open Baseline copy.</param>
    /// <param name="query">Text the headword, a gloss or an allomorph form contains; blank matches every entry.</param>
    /// <param name="morphType">A morph type name such as <c>stem</c> or <c>suffix</c>; blank matches every type.</param>
    /// <param name="limit">The most entries one page returns.</param>
    /// <param name="offset">How many matching entries to skip.</param>
    /// <param name="detailed">Whether to include every allomorph, environment and grammatical-info record.</param>
    public static JsonObject Read(LcmCache cache, string? query, string? morphType, int limit, int offset, bool detailed)
    {
        var matches = cache.ServiceLocator.GetInstance<ILexEntryRepository>().AllInstances()
            .Where(entry => MatchesMorphType(cache, entry, morphType) && MatchesQuery(cache, entry, query))
            .OrderBy(entry => entry.HeadWord?.Text, StringComparer.Ordinal).ToList();
        var page = matches.Skip(offset).Take(limit).Select(entry => Entry(cache, entry, detailed)).ToArray();
        return new JsonObject
        {
            ["total"] = matches.Count,
            ["offset"] = offset,
            ["returned"] = page.Length,
            ["entries"] = new JsonArray(page.Select(entry => (JsonNode)entry).ToArray()),
        };
    }

    private static bool MatchesMorphType(LcmCache cache, ILexEntry entry, string? morphType) =>
        string.IsNullOrWhiteSpace(morphType) ||
        string.Equals(MorphTypeName(cache, entry.LexemeFormOA), morphType.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool MatchesQuery(LcmCache cache, ILexEntry entry, string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return true;
        bool Has(string? text) => text?.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase) == true;
        return Has(entry.HeadWord?.Text)
            || entry.AllSenses.Any(sense => Has(GrammarReader.Text(cache, sense.Gloss)))
            || entry.AlternateFormsOS.Any(form => Has(FormText(cache, form)));
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
