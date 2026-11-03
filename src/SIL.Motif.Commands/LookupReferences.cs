using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.Motif.Contract.Ids;

namespace SIL.Motif.Commands;

/// <summary>
/// Turns a <c>lookup:</c> reference in an intent into the id of the object it names in the project the Draft is
/// composed against. An id is a GUID, and an object a Proposal creates gets a fresh one each time the Proposal is
/// composed, so a plan that names the objects an earlier Proposal created needs a stable way to name them: their
/// form and gloss, which survive copying a project and applying the same plan to the copy and to the original.
/// </summary>
/// <remarks>
/// <para>
/// The forms are <c>lookup:entry:FORM</c>, <c>lookup:sense:FORM|GLOSS</c>, <c>lookup:msa:FORM|GLOSS</c> (the
/// grammatical info the sense uses), <c>lookup:phoneme:GRAPHEME</c> and <c>lookup:category:NAME</c>. <c>FORM</c> is
/// the lexeme form in the first vernacular writing system, written with or without the affix hyphen (<c>ika-</c> finds
/// the prefix, <c>ika</c> finds any entry of that form); <c>GLOSS</c> is a gloss in the first analysis writing system and
/// separates homographs. A name that matches nothing, or more than one object, is refused with the candidates' ids.
/// </para>
/// <para>
/// It runs on the project as the Draft's earlier steps leave it, so a name finds an object the same batch created. An
/// ordinary id is never touched, and neither is any value under a field that holds written text (<c>form</c>,
/// <c>gloss</c>, <c>text</c>, <c>name</c> and the like); any other string that starts with <c>lookup:</c> is read
/// as a reference.
/// </para>
/// </remarks>
internal static class LookupReferences
{
    public const string Prefix = "lookup:";

    // Written-text fields keep a value that begins with the prefix.
    private static readonly HashSet<string> TextFields = new(StringComparer.Ordinal)
    {
        "form", "gloss", "glosses", "text", "name", "names", "abbreviation", "citation", "description", "note",
        "notes", "label", "rationale", "spelling", "representation", "comment", "title", "definition", "pattern",
    };

    /// <summary>Replaces each <c>lookup:</c> string in <paramref name="authored"/> with an id, or returns it unchanged.</summary>
    public static JsonElement Resolve(LcmCache cache, JsonElement authored)
    {
        if (!authored.GetRawText().Contains("\"" + Prefix, StringComparison.Ordinal)) return authored;
        var node = JsonNode.Parse(authored.GetRawText());
        var changed = false;
        node = Walk(node);
        if (!changed) return authored;
        using var document = JsonDocument.Parse(node!.ToJsonString());
        return document.RootElement.Clone();

        JsonNode? Walk(JsonNode? current, bool textual = false)
        {
            switch (current)
            {
                case JsonObject obj:
                    foreach (var key in obj.Select(pair => pair.Key).ToArray())
                        if (Walk(obj[key], textual || TextFields.Contains(key)) is { } replaced && !ReferenceEquals(replaced, obj[key]))
                            obj[key] = replaced;
                    return obj;
                case JsonArray array:
                    for (var i = 0; i < array.Count; i++)
                        if (Walk(array[i], textual) is { } replaced && !ReferenceEquals(replaced, array[i])) array[i] = replaced;
                    return array;
                case JsonValue value when !textual && value.TryGetValue<string>(out var text) &&
                    text.StartsWith(Prefix, StringComparison.Ordinal):
                    changed = true;
                    return JsonValue.Create(ResolveOne(cache, text));
                default:
                    return current;
            }
        }
    }

    internal static string ResolveOne(LcmCache cache, string reference)
    {
        var body = reference[Prefix.Length..];
        var colon = body.IndexOf(':');
        if (colon <= 0)
            throw new InvalidOperationException(
                $"'{reference}' is not a lookup reference; write lookup:<kind>:<name> with kind entry, sense, msa, phoneme or category.");
        var kind = body[..colon];
        var name = body[(colon + 1)..];
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException($"'{reference}' names nothing after the kind.");
        var (form, gloss) = SplitGloss(name);
        return kind switch
        {
            "entry" => One(reference, Entries(cache, form, gloss).Select(entry => (ICmObject)entry), Describe),
            "sense" => One(reference, SensesOf(cache, form, gloss).Select(sense => (ICmObject)sense), Describe),
            "msa" => One(reference, SensesOf(cache, form, gloss).Select(sense => sense.MorphoSyntaxAnalysisRA)
                .Where(msa => msa is not null).DistinctBy(msa => msa!.Guid).Select(msa => (ICmObject)msa!), Describe),
            "phoneme" => One(reference, Phonemes(cache, name).Select(phoneme => (ICmObject)phoneme), Describe),
            "category" => One(reference, Categories(cache, name).Select(category => (ICmObject)category), Describe),
            _ => throw new InvalidOperationException(
                $"'{reference}': lookup kind '{kind}' is not one of entry, sense, msa, phoneme or category."),
        };
    }

    private static (string Form, string? Gloss) SplitGloss(string name)
    {
        var bar = name.IndexOf('|');
        return bar < 0 ? (name, null) : (name[..bar], name[(bar + 1)..]);
    }

    private static IEnumerable<ILexEntry> Entries(LcmCache cache, string form, string? gloss)
    {
        var ws = cache.DefaultVernWs;
        var wanted = Nfd(form);
        var all = cache.ServiceLocator.GetInstance<ILexEntryRepository>().AllInstances()
            .Select(entry => (Entry: entry, Bare: entry.LexemeFormOA?.Form.get_String(ws)?.Text))
            .Where(item => !string.IsNullOrEmpty(item.Bare)).ToArray();
        // A form written with the affix hyphen names the affix; a bare form names every entry of that spelling.
        var marked = all.Where(item => Nfd(Marked(item.Entry, item.Bare!)) == wanted).Select(item => item.Entry).ToArray();
        var matches = marked.Length > 0 ? marked : all.Where(item => Nfd(item.Bare!) == wanted).Select(item => item.Entry).ToArray();
        return gloss is null ? matches : matches.Where(entry => entry.SensesOS.Any(sense => HasGloss(sense, gloss)));
    }

    private static string Marked(ILexEntry entry, string bare) =>
        entry.LexemeFormOA?.MorphTypeRA is { } type ? (type.Prefix ?? "") + bare + (type.Postfix ?? "") : bare;

    private static IEnumerable<ILexSense> SensesOf(LcmCache cache, string form, string? gloss) =>
        Entries(cache, form, gloss).SelectMany(entry => entry.SensesOS.Where(sense => gloss is null || HasGloss(sense, gloss)));

    private static bool HasGloss(ILexSense sense, string gloss) =>
        Nfd(sense.Gloss.BestAnalysisAlternative?.Text ?? "") == Nfd(gloss);

    private static IEnumerable<IPhPhoneme> Phonemes(LcmCache cache, string grapheme)
    {
        var ws = cache.DefaultVernWs;
        var wanted = Nfd(grapheme);
        return cache.LangProject.PhonologicalDataOA.PhonemeSetsOS.SelectMany(set => set.PhonemesOC)
            .Where(phoneme => phoneme.CodesOS.Any(code => Nfd(code.Representation.get_String(ws)?.Text ?? "") == wanted));
    }

    private static IEnumerable<IPartOfSpeech> Categories(LcmCache cache, string name)
    {
        var wanted = Nfd(name);
        return Flatten(cache.LangProject.PartsOfSpeechOA.PossibilitiesOS).Where(category =>
            string.Equals(Nfd(category.Name.BestAnalysisAlternative?.Text ?? ""), wanted, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Nfd(category.Abbreviation.BestAnalysisAlternative?.Text ?? ""), wanted, StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<IPartOfSpeech> Flatten(IEnumerable<ICmPossibility> items)
    {
        foreach (var item in items)
        {
            if (item is IPartOfSpeech category) yield return category;
            foreach (var child in Flatten(item.SubPossibilitiesOS)) yield return child;
        }
    }

    private static string One(string reference, IEnumerable<ICmObject> found, Func<ICmObject, string> describe)
    {
        var distinct = found.DistinctBy(item => item.Guid).ToArray();
        if (distinct.Length == 1) return CanonicalId.FromGuid(distinct[0].Guid).Value;
        var detail = distinct.Length == 0 ? "matches nothing in the project"
            : $"matches {distinct.Length} objects ({string.Join("; ", distinct.Take(8).Select(describe))}); add |gloss to pick one";
        throw new InvalidOperationException($"Lookup '{reference}' {detail}.");
    }

    private static string Describe(ICmObject item) => item switch
    {
        ILexEntry entry => $"entry {CanonicalId.FromGuid(entry.Guid).Value} {entry.HeadWord?.Text}, glosses {Glosses(entry)}",
        ILexSense sense => $"sense {CanonicalId.FromGuid(sense.Guid).Value} '{sense.Gloss.BestAnalysisAlternative?.Text}'",
        _ => $"{item.ClassName} {CanonicalId.FromGuid(item.Guid).Value}",
    };

    private static string Glosses(ILexEntry entry) =>
        string.Join("/", entry.SensesOS.Select(sense => sense.Gloss.BestAnalysisAlternative?.Text));

    private static string Nfd(string text) => text.Normalize(NormalizationForm.FormD);
}
