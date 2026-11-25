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

    // Advisories read from the intents alone: repeated analyses or environments, and whole-word environments.
    private static List<ComposeAdvisory> IntentAdvisories(IReadOnlyList<ComposeStep> steps)
    {
        var advisories = new List<ComposeAdvisory>();
        var conditions = new Dictionary<(string Verb, string Condition), List<int>>();
        for (var index = 0; index < steps.Count; index++)
        {
            if (steps[index] is not { Verb: { } verb, Authored: { ValueKind: JsonValueKind.Object } intent }) continue;
            if (RepeatedCondition(verb, intent) is { } condition)
            {
                if (!conditions.TryGetValue((verb, condition), out var at)) conditions[(verb, condition)] = at = [];
                at.Add(index);
            }
            if (verb == "compose-author-environment" && SpellsWholeWord(intent))
                advisories.Add(new(ComposeAdvisoryCodes.WholeWordEnvironment,
                    $"Environment '{Text(intent, "name")}' has a word boundary on both sides and only literal sounds " +
                    "between, so it fits one word: forms conditioned by it are a stem list. Condition forms by sound " +
                    "with natural classes (Black, Parsing Conceptual Introduction 3.1.2, 3.1.3), write one " +
                    "phonological rule (6.1.2), or list a true exception as a variant.", [index]));
        }
        foreach (var ((verb, _), at) in conditions.Where(pair => pair.Value.Count >= RepeatedConditionLimit))
            advisories.Add(new(ComposeAdvisoryCodes.RepeatedCondition, verb == "compose-add-morphosyntax-analysis"
                ? $"{at.Count} {verb} intents add the same analysis. One analysis on many entries usually belongs in " +
                    "an inflection class at the highest category that shares it, one analysis on a parent class, or a " +
                    "derivational affix (Black 2.1.2.6, 2.1.3); keep the copies only if each entry is a listed exception."
                : $"{at.Count} {verb} intents give forms the same environments. A regular alternation across many " +
                    "morphemes is one phonological rule (Black 3.1, 6.1.2) or one environment with a natural class " +
                    "(3.1.2); keep the copies only if each form is a listed exception.", at));
        return advisories;
    }

    // The analysis or environment list an intent adds, as normalized JSON; null when it adds none to compare.
    private static string? RepeatedCondition(string verb, JsonElement intent)
    {
        var property = verb switch
        {
            "compose-add-morphosyntax-analysis" => "msa",
            "compose-author-alternate-form" or "compose-edit-allomorph-condition" => "environments",
            _ => null,
        };
        if (property is null || !intent.TryGetProperty(property, out var value)) return null;
        if (value.ValueKind == JsonValueKind.Array && value.GetArrayLength() == 0) return null;
        return JsonNode.Parse(value.GetRawText())?.ToJsonString();
    }

    // A word boundary at both outer edges and nothing but phonemes between spells one whole word.
    private static bool SpellsWholeWord(JsonElement intent)
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

    // Reads the scratch project after every intent: a class the intents assign that holds one stem is an exception.
    private static List<ComposeAdvisory> OneStemClassAdvisories(LcmCache cache, IReadOnlyList<ComposeStep> steps,
        IReadOnlyList<(JsonElement Input, OperationEnvelope[] Added)> results)
    {
        var advisories = new List<ComposeAdvisory>();
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
                $"Inflection class '{inflectionClass.Name.BestAnalysisAlternative.Text}' has one stem after these " +
                "intents. A class groups stems that take the same affix forms (Black 2.1.2.6, 2.1.2.8); one stem is an " +
                "exception, better a listed form or an exception feature (2.1.6). Keep it if more stems will join.", at));
        }
        return advisories;
    }

    private static bool Within(IMoInflClass candidate, IMoInflClass inflectionClass)
    {
        for (ICmObject? owner = candidate; owner is IMoInflClass; owner = owner.Owner)
            if (owner.Guid == inflectionClass.Guid) return true;
        return false;
    }
}
