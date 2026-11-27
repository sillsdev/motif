using System.Text.Json;
using System.Text.Json.Nodes;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Model;

namespace SIL.Motif.Commands;

/// <summary>
/// Advisories on composed intents, grounded in Black's Parsing Conceptual Introduction and in grammar lanes that
/// padded a grammar with copies. They advise and never refuse (ADR 0004).
/// </summary>
public static partial class ProposalCommands
{
    /// <summary>How many intents adding the same condition make a pattern worth an advisory.</summary>
    internal const int RepeatedConditionLimit = 5;

    // Advisories read from the intents alone: repeated analyses and whole-word environments.
    private static List<ComposeAdvisory> IntentAdvisories(IReadOnlyList<ComposeStep> steps)
    {
        var advisories = new List<ComposeAdvisory>();
        var analyses = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        for (var index = 0; index < steps.Count; index++)
        {
            if (steps[index] is not { Verb: { } verb, Authored: { ValueKind: JsonValueKind.Object } intent }) continue;
            if (verb == "compose-add-morphosyntax-analysis" && intent.TryGetProperty("msa", out var msa))
            {
                var key = JsonNode.Parse(msa.GetRawText())?.ToJsonString() ?? "";
                if (!analyses.TryGetValue(key, out var at)) analyses[key] = at = [];
                at.Add(index);
            }
            if (verb == "compose-author-environment" && SpellsWholeWord(intent))
                advisories.Add(new(ComposeAdvisoryCodes.WholeWordEnvironment,
                    WholeWordEnvironmentMessage(Text(intent, "name")), [index]));
        }
        foreach (var (key, at) in analyses.Where(pair => pair.Value.Count >= RepeatedConditionLimit))
            advisories.Add(new(ComposeAdvisoryCodes.RepeatedCondition,
                $"{at.Count} compose-add-morphosyntax-analysis intents add the same analysis. A regular change of " +
                "category across many entries is usually one derivational affix, possibly zero (Black 2.1.5)" +
                (key.Contains("\"inflectionClass\"", StringComparison.Ordinal)
                    ? "; a shared inflection class belongs at the highest category that shares it (2.1.2.6.2)"
                    : "") +
                ". Keep the analyses only if each entry is a listed exception.", at));
        return advisories;
    }

    // A word boundary at both outer edges and nothing but phonemes between spells one whole word.
    internal static bool SpellsWholeWord(JsonElement intent)
    {
        if (!intent.TryGetProperty("left", out var left) || left.ValueKind != JsonValueKind.Array ||
            !intent.TryGetProperty("right", out var right) || right.ValueKind != JsonValueKind.Array) return false;
        var items = left.EnumerateArray().Concat(right.EnumerateArray()).ToArray();
        if (items.Any(item => item.ValueKind != JsonValueKind.Object)) return false;
        static bool IsWordBoundary(JsonElement item) =>
            item.TryGetProperty("boundary", out var boundary) && boundary.ValueKind == JsonValueKind.String &&
            boundary.GetString() == "word";
        var lefts = left.EnumerateArray().ToArray();
        var rights = right.EnumerateArray().ToArray();
        return lefts.Length > 0 && rights.Length > 0 && IsWordBoundary(lefts[0]) && IsWordBoundary(rights[^1]) &&
            items.Any(item => item.TryGetProperty("phoneme", out _)) &&
            items.All(item => item.TryGetProperty("phoneme", out _) || IsWordBoundary(item));
    }

    private static string Text(JsonElement intent, string property) =>
        intent.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : "";

    // The inflection class a stem analysis gets from one composed intent, or null when it sets none.
    private static CanonicalId? AssignedInflectionClass(string? verb, JsonElement input)
    {
        if (input.ValueKind != JsonValueKind.Object) return null;
        var holder = input;
        if (verb is "compose-author-lexical-entry" or "compose-add-morphosyntax-analysis")
        {
            if (!input.TryGetProperty("msa", out holder) || holder.ValueKind != JsonValueKind.Object ||
                !holder.TryGetProperty("kind", out var kind) || kind.ValueKind != JsonValueKind.String ||
                kind.GetString() != "stem") return null;
        }
        else if (verb != "compose-edit-stem-inflection-class") return null;
        return holder.TryGetProperty("inflectionClass", out var value) && value.ValueKind == JsonValueKind.String &&
            CanonicalId.TryParse(value.GetString(), out var id) ? id : null;
    }

    // Reads the scratch project after every intent, for advice that depends on objects rather than intents.
    private static List<ComposeAdvisory> ScratchAdvisories(LcmCache cache, IReadOnlyList<ComposeStep> steps,
        IReadOnlyList<(JsonElement Input, OperationEnvelope[] Added)> results)
    {
        var advisories = LiteralEnvironmentAdvisories(cache, steps, results);
        var assigned = new Dictionary<Guid, List<int>>();
        for (var index = 0; index < steps.Count && index < results.Count; index++)
            if (AssignedInflectionClass(steps[index].Verb, results[index].Input) is { } id)
            {
                if (!assigned.TryGetValue(id.ToGuid(), out var at)) assigned[id.ToGuid()] = at = [];
                at.Add(index);
            }
        if (assigned.Count == 0) return advisories;
        try
        {
            // The last intent is left off the scratch while composing, so its class is counted after applying it here.
            if (results.Count == steps.Count && assigned.Values.Any(at => at.Contains(steps.Count - 1)))
                ApplyToScratch(cache, results[^1].Added);
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            return advisories;
        }
        var stemClasses = cache.ServiceLocator.GetInstance<IMoStemMsaRepository>().AllInstances()
            .Select(msa => msa.InflectionClassRA).Where(stemClass => stemClass is not null).ToArray();
        foreach (var (guid, at) in assigned)
        {
            if (!cache.ServiceLocator.ObjectRepository.TryGetObject(guid, out var found) ||
                found is not IMoInflClass inflectionClass ||
                stemClasses.Count(stemClass => Within(stemClass, inflectionClass)) != 1) continue;
            advisories.Add(new(ComposeAdvisoryCodes.OneStemInflectionClass,
                OneStemInflectionClassMessage(inflectionClass.Name.BestAnalysisAlternative.Text, "after these intents"), at));
        }
        return advisories;
    }

    // Forms sharing a natural-class environment are Black's model (3.1), so only literal sounds are advised.
    private static List<ComposeAdvisory> LiteralEnvironmentAdvisories(LcmCache cache, IReadOnlyList<ComposeStep> steps,
        IReadOnlyList<(JsonElement Input, OperationEnvelope[] Added)> results)
    {
        var advisories = new List<ComposeAdvisory>();
        var groups = new Dictionary<(string Verb, string Environments), List<int>>();
        for (var index = 0; index < steps.Count && index < results.Count; index++)
            if (steps[index].Verb is "compose-author-alternate-form" or "compose-edit-allomorph-condition" &&
                results[index].Input is { ValueKind: JsonValueKind.Object } input &&
                input.TryGetProperty("environments", out var environments) &&
                environments.ValueKind == JsonValueKind.Array && environments.GetArrayLength() > 0)
            {
                var key = (steps[index].Verb!, environments.GetRawText());
                if (!groups.TryGetValue(key, out var at)) groups[key] = at = [];
                at.Add(index);
            }
        // Rules are only named where the project could use them: HermitCrab active, or rules already written.
        var hasRules = cache.LanguageProject.MorphologicalDataOA?.ActiveParser == "HC" ||
            cache.LanguageProject.PhonologicalDataOA?.PhonRulesOS.Count > 0;
        foreach (var ((verb, environments), at) in groups.Where(pair => pair.Value.Count >= RepeatedConditionLimit))
        {
            var literal = JsonDocument.Parse(environments).RootElement.EnumerateArray()
                .Select(item => EnvironmentText(cache, item))
                .Where(text => text is not null && !text.Contains('[', StringComparison.Ordinal)).ToArray();
            if (literal.Length == 0) continue;
            advisories.Add(new(ComposeAdvisoryCodes.RepeatedCondition,
                $"{at.Count} {verb} intents give forms the environment {string.Join(", ", literal)}, which spells " +
                "literal sounds. Write it with a natural class so it covers every sound of the pattern (Black 3.1.2, " +
                "3.1.3)" + (hasRules ? "; if every form is a regular alternation, one phonological rule does the work " +
                "(6.1.2)" : "") + ". Keep the forms if each is a listed exception.", at));
        }
        return advisories;
    }

    private static string? EnvironmentText(LcmCache cache, JsonElement item) =>
        item.ValueKind == JsonValueKind.String && CanonicalId.TryParse(item.GetString(), out var id) &&
        cache.ServiceLocator.ObjectRepository.TryGetObject(id.ToGuid(), out var found) &&
        found is IPhEnvironment environment ? environment.StringRepresentation?.Text : null;

    /// <summary>The words of the whole-word-environment advisory, shared by compose-batch and the whole-grammar sweep.</summary>
    internal static string WholeWordEnvironmentMessage(string name) =>
        $"Environment '{name}' has a word boundary on both sides and only literal sounds " +
        "between, so it fits one word: forms conditioned by it are a stem list. Condition forms by sound " +
        "with natural classes (Black, Parsing Conceptual Introduction 3.1.2, 3.1.3); if it marks a " +
        "suppletive form, keep it or list the form as a variant.";

    /// <summary>The words of the one-stem-inflection-class advisory, shared by compose-batch and the whole-grammar sweep.</summary>
    internal static string OneStemInflectionClassMessage(string name, string when) =>
        $"Inflection class '{name}' has one stem {when}. A class groups stems that take the same affix forms " +
        "(Black 2.1.2.6, 2.1.2.8); one stem is an exception, better a listed form or an exception feature (2.1.6). " +
        "Keep it if more stems will join.";

    /// <summary>How many stem analyses sit in <paramref name="inflectionClass"/> or one of its subclasses.</summary>
    internal static int StemCount(LcmCache cache, IMoInflClass inflectionClass) =>
        cache.ServiceLocator.GetInstance<IMoStemMsaRepository>().AllInstances()
            .Count(msa => msa.InflectionClassRA is { } stemClass && Within(stemClass, inflectionClass));

    private static bool Within(IMoInflClass candidate, IMoInflClass inflectionClass)
    {
        for (ICmObject? owner = candidate; owner is IMoInflClass; owner = owner.Owner)
            if (owner.Guid == inflectionClass.Guid) return true;
        return false;
    }
}
