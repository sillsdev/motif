using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using SIL.Motif.Cli.Rendering;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Cli;

/// <summary>The CLI side of <c>motif lexicon find</c>: reads the flags, runs the shared query and renders it.</summary>
internal static class LexiconFindCli
{
    private static readonly string[] Names =
    [
        "project", "query", "exact", "morph-type", "affix", "category", "headword", "headword-contains", "has-gloss",
        "form-regex", "suffix", "class", "offset", "limit", "page", "all", "id-format", "at", "json",
    ];

    /// <summary>The result of the invocation, or null when its words and flags are not a well-formed <c>lexicon find</c>.</summary>
    internal static CommandResult? Run(IReadOnlyList<string> positionals, IReadOnlyDictionary<string, string> flags, bool asJson)
    {
        if (positionals is not ["find"] || !flags.TryGetValue("project", out var project) ||
            flags.Keys.Any(name => !Names.Contains(name) && name != "batch")) return null;
        if (flags.TryGetValue("batch", out var batch))
            return flags.Keys.All(name => name is "project" or "batch" or "at" or "json") && batch != "true"
                ? RunBatch(project, batch, flags.GetValueOrDefault("at"), asJson) : null;
        var (request, refusal) = Build(project, flags);
        if (request is null && refusal is null) return null;
        if (refusal is not null) return CommandTextRenderer.Render(CommandOutcome<JsonObject>.Refused(refusal), asJson);
        var outcome = LexiconFindCommand.Find(request!);
        if (asJson || !outcome.Succeeded) return CommandTextRenderer.Render(outcome, asJson);
        return new CommandResult(0, Text(outcome.Value!, flags, positionals, request!.Limit));
    }

    // One JSON object per input line, one JSON line out, all answered from a single open of the Baseline.
    private static CommandResult RunBatch(string project, string source, string? at, bool asJson)
    {
        string[] lines;
        try { lines = (source == "-" ? Console.In.ReadToEnd() : File.ReadAllText(source)).Split('\n'); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Refuse($"--batch could not read '{source}': {exception.Message}", asJson);
        }
        var inputs = new List<JsonObject>();
        var requests = new List<LexiconFindRequest>();
        foreach (var raw in lines.Select(line => line.Trim()).Where(line => line.Length > 0))
        {
            JsonObject? input;
            try { input = JsonNode.Parse(raw) as JsonObject; }
            catch (System.Text.Json.JsonException) { input = null; }
            if (input is null) return Refuse($"--batch line {inputs.Count + 1} is not a JSON object.", asJson);
            var pairs = input.ToDictionary(pair => pair.Key, pair => pair.Value switch
            {
                JsonValue value when value.TryGetValue<string>(out var text) => text,
                JsonValue value => value.ToJsonString(),
                _ => "",
            }, StringComparer.Ordinal);
            var (request, refusal) = Build(project, pairs);
            if (request is null) return Refuse(refusal?.Message ?? $"--batch line {inputs.Count + 1} names an unknown or malformed filter.", asJson);
            inputs.Add(input);
            requests.Add(request with { At = at });
        }
        var outcome = LexiconFindCommand.FindMany(project, at, requests);
        if (!outcome.Succeeded) return CommandTextRenderer.Render(outcome, asJson);
        var text = new StringBuilder();
        for (var index = 0; index < requests.Count; index++)
        {
            var answer = outcome.Value![index];
            var line = new JsonObject { ["query"] = inputs[index] };
            if (answer.Succeeded) line["result"] = answer.Value;
            else line["refusal"] = new JsonObject { ["code"] = answer.Refusal!.Code, ["message"] = answer.Refusal.Message };
            text.Append(line.ToJsonString()).Append('\n');
        }
        return new CommandResult(0, text.ToString());
    }

    // The request the flags name, a refusal saying why not, or neither when the words are not a lexicon find at all.
    private static (LexiconFindRequest? Request, Refusal? Refusal) Build(string project, IReadOnlyDictionary<string, string> flags)
    {
        if (flags.Keys.Any(name => name is not "project" and not "json" && !Names.Contains(name)) ||
            flags.Any(pair => pair.Value == "true" && pair.Key is not ("affix" or "has-gloss" or "all" or "json")) ||
            !TryBool(flags, "affix", out var affix) || !TryBool(flags, "has-gloss", out var hasGloss))
            return (null, null);
        var idFormat = LexiconIdFormat.Canonical;
        if (flags.TryGetValue("id-format", out var format))
        {
            if (format == "guid") idFormat = LexiconIdFormat.Guid;
            else if (format != "canonical")
                return (null, new Refusal(RefusalCodes.LexiconFindInvalidRequest, FailureReason.InvalidArgument, "--id-format must be guid or canonical."));
        }
        var (window, pagingRefusal) = PagingFlags.Read(flags, ResultPaging.DefaultLimit);
        if (pagingRefusal is not null) return (null, pagingRefusal);
        var morphTypes = flags.TryGetValue("morph-type", out var types)
            ? types.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries) : null;
        return (new LexiconFindRequest(project, flags.GetValueOrDefault("query"),
            flags.GetValueOrDefault("exact"), morphTypes, affix, flags.GetValueOrDefault("category"),
            flags.GetValueOrDefault("headword"), flags.GetValueOrDefault("headword-contains"), hasGloss,
            flags.GetValueOrDefault("form-regex"), flags.GetValueOrDefault("suffix"), flags.GetValueOrDefault("class"),
            window.All ? 0 : window.Offset, window.All ? LexiconFindCommand.MaximumPageSize : window.Limit, idFormat,
            flags.GetValueOrDefault("at")), null);
    }

    private static CommandResult Refuse(string message, bool asJson) => CommandTextRenderer.Render(
        CommandOutcome<JsonObject>.Refused(new Refusal(RefusalCodes.LexiconFindInvalidRequest, FailureReason.InvalidArgument, message)), asJson);

    private static bool TryBool(IReadOnlyDictionary<string, string> flags, string name, out bool? value)
    {
        value = null;
        if (!flags.TryGetValue(name, out var text)) return true;
        if (text is "true" or "false") { value = text == "true"; return true; }
        return false;
    }

    // One tab-separated line per entry: id, headword, morph type, forms, classes, categories, gloss.
    private static string Text(JsonObject page, IReadOnlyDictionary<string, string> flags, IReadOnlyList<string> positionals,
        int limit)
    {
        var text = new StringBuilder();
        foreach (var node in page["entries"]!.AsArray())
        {
            var entry = node!.AsObject();
            var forms = new[] { entry["lexemeForm"]?.GetValue<string>() }
                .Concat(entry["alternateForms"]!.AsArray().Select(form => form?.GetValue<string>())).Where(form => !string.IsNullOrEmpty(form));
            var classes = entry["inflectionClasses"]!.AsArray().Select(item => item!["name"]?.GetValue<string>());
            var categories = entry["categories"]!.AsArray().Select(item => item?.GetValue<string>());
            var gloss = entry["senses"]!.AsArray().Select(sense => sense!["gloss"]?.GetValue<string>()).FirstOrDefault(item => !string.IsNullOrEmpty(item));
            text.Append(string.Join('\t', entry["id"]!.GetValue<string>(), entry["headword"]?.GetValue<string>(),
                entry["morphType"]?.GetValue<string>(), string.Join('|', forms), string.Join('|', classes),
                string.Join('|', categories), gloss)).Append('\n');
        }
        text.Append(CultureInfo.InvariantCulture, $"# {page["showing"]!.GetValue<string>()}\n");
        if (page["nextOffset"] is { } next)
            text.Append("# Next: ").Append(PagingFlags.NextCommand("lexicon", positionals, flags, next.GetValue<int>(), limit)).Append('\n');
        return text.ToString();
    }
}
