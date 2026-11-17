using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Runner.Operations;

namespace SIL.Motif.Runner.Composers;

/// <summary>
/// One entry to tag. <c>At</c> labels it in refusals, such as "line 7" of an entry list file.
/// </summary>
public sealed record AttributedEntry(CanonicalId Entry, string? At = null);

/// <summary>The tag, the entries it goes on, and the analysis writing system it is written in; omitted uses the default.</summary>
public sealed record SetEntryAttributionIntent(
    AttributionTag Tag, IReadOnlyList<AttributedEntry> Entries, string? AnalysisWritingSystem = null);

/// <summary>Reads a closed intent <c>{ "tag", "analysisWs"?, "entries": [ { "entry", "at"? } ] }</c>.</summary>
public static class SetEntryAttributionIntentParser
{
    private const string ConstructName = "SetEntryAttribution";

    /// <summary>Requires the tag and at least one entry, and collects every bad entry into one refusal.</summary>
    public static SetEntryAttributionIntent Parse(JsonElement authored)
    {
        SoundSystemIntentParsing.Object(authored, ConstructName, "tag", "analysisWs", "entries");
        var tag = AttributionTag.Parse(SoundSystemIntentParsing.Text(authored, "tag"), ConstructName);
        var analysisWs = authored.TryGetProperty("analysisWs", out _) ? SoundSystemIntentParsing.Text(authored, "analysisWs") : null;
        if (analysisWs is not null && string.IsNullOrWhiteSpace(analysisWs))
            throw Refuse("'analysisWs' must name a writing system.");
        var entries = new List<AttributedEntry>();
        var problems = new List<string>();
        var seen = new Dictionary<CanonicalId, string>();
        foreach (var (item, number) in SoundSystemIntentParsing.Array(authored, "entries").Select((item, index) => (item, index + 1)))
        {
            string? at = null;
            try
            {
                SoundSystemIntentParsing.Object(item, ConstructName + $" entry {number}", "entry", "at");
                at = item.TryGetProperty("at", out _) ? SoundSystemIntentParsing.Text(item, "at") : null;
                var entry = SoundSystemIntentParsing.Id(item, "entry");
                var label = Label(number, at);
                if (seen.TryGetValue(entry, out var first)) throw Refuse($"{label} repeats {first}.");
                seen[entry] = label;
                entries.Add(new AttributedEntry(entry, at));
            }
            catch (ContractParseException failure)
            {
                problems.Add(Label(number, at) + ": " + Strip(failure.Message));
            }
        }
        if (problems.Count > 0) throw Refuse(Summarize(problems));
        if (entries.Count == 0) throw Refuse("'entries' needs at least one entry.");
        return new SetEntryAttributionIntent(tag, entries, analysisWs);
    }

    /// <summary>
    /// Reads an entry list, one entry per line, into an intent. A line is an entry id or a FieldWorks GUID; blank lines
    /// and lines starting with <c>#</c> are skipped. Every unreadable line is reported once, with its line number.
    /// </summary>
    public static JsonElement FromList(string list, string tag, string? analysisWs = null)
    {
        var entries = new List<object>();
        var problems = new List<string>();
        var number = 0;
        foreach (var raw in list.Split('\n'))
        {
            number++;
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#') continue;
            if (Guid.TryParseExact(line, "D", out var guid)) line = CanonicalId.FromGuid(guid).Value;
            else if (!CanonicalId.TryParse(line, out _))
            {
                problems.Add($"line {number}: '{line}' is neither an entry id nor a GUID.");
                continue;
            }
            entries.Add(new { entry = line, at = $"line {number}" });
        }
        if (problems.Count > 0) throw Refuse(Summarize(problems));
        return JsonSerializer.SerializeToElement(analysisWs is null ? new { tag, entries } : (object)new { tag, analysisWs, entries });
    }

    internal static string Label(int number, string? at) => $"entry {number}" + (at is null ? string.Empty : $" ({at})");

    internal static string Summarize(IReadOnlyList<string> problems, int shown = 20) =>
        $"{problems.Count} problem(s):\n  " + string.Join("\n  ", problems.Take(shown)) +
        (problems.Count > shown ? $"\n  ... and {problems.Count - shown} more." : string.Empty);

    private static string Strip(string message) => message.StartsWith($"'{ConstructName}': ", StringComparison.Ordinal)
        ? message[($"'{ConstructName}': ").Length..] : message;

    private static ContractParseException Refuse(string message) => new($"'{ConstructName}': {message}");
}

/// <summary>
/// Stages one <c>lexical/lexEntry/appendBibliography</c> per entry that does not yet carry the tag, writing the entry's
/// existing Bibliography text first and the tag after it. An entry that already carries the tag stages nothing.
/// </summary>
public static class SetEntryAttributionComposer
{
    private const string ConstructName = "SetEntryAttribution";
    private const string Separator = "; ";
    private const string Rationale =
        "Record the source, revision and licence of this entry in its Bibliography, so the Attribution stays with the " +
        "data when the project is exported. Text the Bibliography already holds is kept.";

    /// <summary>Resolves every entry first, so one refusal names all the unknown entries and licence clashes together.</summary>
    public static IReadOnlyList<OperationEnvelope> Build(LcmCache cache, SetEntryAttributionIntent intent, Func<CanonicalId>? mintId = null)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(intent);
        var mint = mintId ?? (() => CanonicalId.Mint());
        var ws = AttributionTag.AnalysisWritingSystem(cache, intent.AnalysisWritingSystem, ConstructName);
        var repository = cache.ServiceLocator.GetInstance<ICmObjectRepository>();
        var operations = new List<OperationEnvelope>();
        var problems = new List<string>();
        for (var number = 1; number <= intent.Entries.Count; number++)
        {
            var item = intent.Entries[number - 1];
            var label = SetEntryAttributionIntentParser.Label(number, item.At);
            if (!repository.TryGetObject(item.Entry.ToGuid(), out var found) || found is not ILexEntry entry)
            {
                problems.Add($"{label}: '{item.Entry.Value}' is not a lexical entry of this project.");
                continue;
            }
            try
            {
                if (intent.Tag.IsInAnyAnalysisAlternative(cache, entry.Bibliography, string.Empty)) continue;
                operations.Add(new OperationEnvelope(mint(), AttributionOperationKinds.AppendToBibliography,
                    target: item.Entry, after: JsonSerializer.SerializeToElement(new { ws, text = intent.Tag.Text, separator = Separator }),
                    rationale: Rationale));
            }
            catch (InvalidOperationException failure)
            {
                problems.Add($"{label}: {failure.Message}");
            }
        }
        if (problems.Count > 0)
            throw new ContractParseException($"'{ConstructName}': " + SetEntryAttributionIntentParser.Summarize(problems));
        return operations;
    }
}
