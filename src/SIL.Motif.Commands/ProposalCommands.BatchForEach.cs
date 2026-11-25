using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using SIL.LCModel;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;

namespace SIL.Motif.Commands;

public static partial class ProposalCommands
{
    // One expanded intent: its forEach origin, its target and why it was refused, if it was.
    private sealed record ExpandedEntry(int? From, string? Target, string? Field = null, string? Code = null,
        string? Message = null);

    private static readonly Regex KnownPlaceholder = new(@"\{target(?:\.[A-Za-z0-9_]+)?\}", RegexOptions.CultureInvariant);
    private static readonly Regex AnyPlaceholder = new(@"\{[A-Za-z][A-Za-z0-9_.]*\}", RegexOptions.CultureInvariant);

    // Rewrites each forEach entry as one entry per target; a refused one becomes a placeholder row.
    private static string ExpandForEach(string json, string? baseDirectory, LcmCache cache,
        out List<ExpandedEntry> expanded)
    {
        var rows = new List<ExpandedEntry>();
        expanded = rows;
        JsonNode? parsed;
        try
        {
            parsed = JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            return json;
        }
        if (parsed is not JsonObject root || root["intents"] is not JsonArray intents) return json;
        var output = new JsonArray();
        var from = -1;
        foreach (var node in intents)
        {
            from++;
            if (node is not JsonObject entry)
            {
                output.Add(node?.DeepClone());
                rows.Add(new(null, null));
                continue;
            }
            var verb = entry["verb"] is JsonValue verbValue && verbValue.TryGetValue<string>(out var verbText)
                ? verbText : "<unknown>";
            var hasForEach = entry.ContainsKey("forEach");
            var placeholderText = ScanStrings(entry["intent"]).Concat(ScanStrings(entry["citation"])).ToArray();
            void Refuse(string field, string code, string message)
            {
                output.Add(new JsonObject { ["verb"] = verb, ["intent"] = null });
                rows.Add(new(from, null, field, code, message));
            }
            if (!hasForEach)
            {
                if (placeholderText.FirstOrDefault(text => KnownPlaceholder.IsMatch(text)) is { } stray)
                    Refuse("intent", RefusalCodes.DraftForEachPlaceholderWithoutForEach,
                        $"Intent {from} ({verb}): '{KnownPlaceholder.Match(stray).Value}' is a forEach placeholder but this " +
                        "entry has no 'forEach'; add \"forEach\": {\"targets\": [...]} or write the value out.");
                else
                {
                    output.Add(entry.DeepClone());
                    rows.Add(new(null, null));
                }
                continue;
            }
            if (entry["forEach"] is not JsonObject forEach ||
                forEach.Count != 1 || (!forEach.ContainsKey("targets") && !forEach.ContainsKey("targetsFile")))
            {
                Refuse("forEach", RefusalCodes.DraftInvalid,
                    $"Intent {from} ({verb}): 'forEach' is an object with exactly one of 'targets' (an array) or 'targetsFile' (a path).");
                continue;
            }
            if (entry.Any(property => property.Key is not ("verb" or "intent" or "citation" or "forEach" or "as")) ||
                entry["intent"] is not JsonObject intent || entry["verb"] is not JsonValue)
            {
                Refuse("entry", RefusalCodes.DraftInvalid,
                    $"Intent {from} ({verb}): each entry has 'verb' and 'intent', with optional 'citation' and 'forEach'.");
                continue;
            }
            if (entry.ContainsKey("as"))
            {
                Refuse("as", RefusalCodes.DraftInvalid,
                    $"Intent {from} ({verb}): a forEach entry cannot carry 'as': one label would name every target's object.");
                continue;
            }
            if (ContainsForEach(intent) || ContainsForEach(entry["citation"]))
            {
                Refuse("forEach", RefusalCodes.DraftForEachNested,
                    $"Intent {from} ({verb}): forEach does not nest; expand the inner list into 'targets'.");
                continue;
            }
            var unknown = placeholderText.SelectMany(text => AnyPlaceholder.Matches(text).Select(match => match.Value))
                .FirstOrDefault(token => !(token is "{target}" or "{target.form}" or "{target.id}"));
            if (unknown is not null)
            {
                Refuse("intent", RefusalCodes.DraftForEachUnknownPlaceholder,
                    $"Intent {from} ({verb}): unknown placeholder '{unknown}'; the placeholders are {{target}}, " +
                    "{target.form} and {target.id}.");
                continue;
            }
            List<string> targets;
            try
            {
                targets = ReadForEachTargets(forEach, baseDirectory, from, verb);
            }
            catch (InvalidOperationException exception)
            {
                Refuse(forEach.ContainsKey("targetsFile") ? "forEach.targetsFile" : "forEach.targets",
                    RefusalCodes.DraftInvalid, exception.Message);
                continue;
            }
            if (targets.Count == 0)
            {
                Refuse("forEach", RefusalCodes.DraftForEachEmpty,
                    $"Intent {from} ({verb}): forEach has no targets, so it would compose nothing.");
                continue;
            }
            var needsObject = placeholderText.Any(text => text.Contains("{target.form}", StringComparison.Ordinal) ||
                text.Contains("{target.id}", StringComparison.Ordinal));
            var position = rows.Count;
            foreach (var target in targets)
            {
                var values = new Dictionary<string, string>(StringComparer.Ordinal) { ["{target}"] = target };
                if (needsObject)
                {
                    var (id, form, error) = ResolveForEachTarget(cache, target);
                    if (placeholderText.Any(text => text.Contains("{target.id}", StringComparison.Ordinal)) &&
                        id is null)
                    {
                        Unresolved(error ?? "it names no object");
                        continue;
                    }
                    if (placeholderText.Any(text => text.Contains("{target.form}", StringComparison.Ordinal)) &&
                        form is null)
                    {
                        Unresolved(error ?? "the object it names has no form");
                        continue;
                    }
                    values["{target.id}"] = id ?? "";
                    values["{target.form}"] = form ?? "";
                }
                var copy = new JsonObject
                {
                    ["verb"] = verb,
                    ["intent"] = Substitute(intent, values),
                };
                if (entry["citation"] is { } citation) copy["citation"] = Substitute(citation, values);
                output.Add(copy);
                rows.Add(new(from, target));
                position++;

                void Unresolved(string reason)
                {
                    output.Add(new JsonObject { ["verb"] = verb, ["intent"] = null });
                    rows.Add(new(from, target, "forEach", RefusalCodes.DraftForEachTargetUnresolved,
                        $"Intent {position} ({verb}, target '{target}' of forEach intent {from}): target does not resolve: {reason}."));
                    position++;
                }
            }
        }
        if (rows.All(item => item.From is null && item.Code is null)) return json;
        return new JsonObject { ["intents"] = output }.ToJsonString();
    }

    private static IEnumerable<string> ScanStrings(JsonNode? node)
    {
        switch (node)
        {
            case JsonValue value when value.TryGetValue<string>(out var text):
                yield return text;
                break;
            case JsonObject obj:
                foreach (var item in obj.SelectMany(property => ScanStrings(property.Value))) yield return item;
                break;
            case JsonArray array:
                foreach (var item in array.SelectMany(ScanStrings)) yield return item;
                break;
        }
    }

    private static bool ContainsForEach(JsonNode? node) => node switch
    {
        JsonObject obj => obj.Any(property => property.Key == "forEach" || ContainsForEach(property.Value)),
        JsonArray array => array.Any(ContainsForEach),
        _ => false,
    };

    private static JsonNode Substitute(JsonNode node, IReadOnlyDictionary<string, string> values) => node switch
    {
        JsonObject obj => new JsonObject(obj.Select(property => new KeyValuePair<string, JsonNode?>(
            property.Key, property.Value is null ? null : Substitute(property.Value, values)))),
        JsonArray array => new JsonArray(array.Select(item => item is null ? null : Substitute(item, values)).ToArray()),
        JsonValue value when value.TryGetValue<string>(out var text) => JsonValue.Create(
            values.Aggregate(text, (current, pair) => current.Replace(pair.Key, pair.Value, StringComparison.Ordinal)))!,
        _ => node.DeepClone(),
    };

    private static List<string> ReadForEachTargets(JsonObject forEach, string? baseDirectory, int from, string verb)
    {
        if (forEach["targets"] is { } list)
        {
            if (list is not JsonArray array || array.Any(item => item is not JsonValue value || !value.TryGetValue<string>(out _)))
                throw new InvalidOperationException(
                    $"Intent {from} ({verb}): forEach 'targets' is an array of strings, each an id or a form.");
            return array.Select(item => item!.GetValue<string>()).ToList();
        }
        if (forEach["targetsFile"] is not JsonValue pathValue || !pathValue.TryGetValue<string>(out var relative) ||
            string.IsNullOrWhiteSpace(relative))
            throw new InvalidOperationException($"Intent {from} ({verb}): forEach 'targetsFile' is a path string.");
        var path = Path.GetFullPath(relative, baseDirectory ?? Directory.GetCurrentDirectory());
        if (!File.Exists(path))
            throw new InvalidOperationException(
                $"Intent {from} ({verb}): forEach targetsFile '{relative}' does not exist (looked for {path}); a relative path " +
                "is read from the folder of the batch file.");
        var targets = new List<string>();
        foreach (var line in File.ReadAllLines(path))
        {
            var text = line.Trim();
            if (text.Length == 0) continue;
            if (text[0] == '"')
            {
                try
                {
                    text = JsonSerializer.Deserialize<string>(text) ?? "";
                }
                catch (JsonException)
                {
                    throw new InvalidOperationException(
                        $"Intent {from} ({verb}): forEach targetsFile line '{text}' starts with a quote but is not a JSON string.");
                }
            }
            targets.Add(text);
        }
        return targets;
    }

    // An id names the object it finds; a form names the one entry whose lexeme form is that text.
    private static (string? Id, string? Form, string? Error) ResolveForEachTarget(LcmCache cache, string target)
    {
        if (CanonicalId.TryParse(target, out var id))
        {
            if (!cache.ServiceLocator.ObjectRepository.TryGetObject(id.ToGuid(), out var found))
                return (null, null, $"'{target}' is not an object in the project");
            var form = found switch
            {
                ILexEntry entry => entry.LexemeFormOA?.Form.VernacularDefaultWritingSystem?.Text,
                IMoForm morph => morph.Form.VernacularDefaultWritingSystem?.Text,
                _ => null,
            };
            return (id.Value, string.IsNullOrEmpty(form) ? null : form, form is null ? $"'{target}' has no form" : null);
        }
        var wanted = target.Normalize(System.Text.NormalizationForm.FormD);
        var matches = cache.ServiceLocator.GetInstance<ILexEntryRepository>().AllInstances()
            .Where(entry => entry.LexemeFormOA is { } lexeme && Enumerable.Range(0, lexeme.Form.StringCount).Any(slot =>
                lexeme.Form.GetStringFromIndex(slot, out _)?.Text?.Normalize(System.Text.NormalizationForm.FormD) == wanted))
            .ToArray();
        return matches.Length switch
        {
            1 => (CanonicalId.FromGuid(matches[0].Guid).Value, target, null),
            0 => (null, target, $"no entry has the lexeme form '{target}'"),
            _ => (null, target, $"{matches.Length} entries have the lexeme form '{target}'; name one by id"),
        };
    }
}
