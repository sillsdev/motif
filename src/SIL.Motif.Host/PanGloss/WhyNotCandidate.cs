using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SIL.Motif.Host.PanGloss;

/// <summary>
/// One choice at one position of a why-not candidate: a morpheme named by its <c>grammar.xml</c> id, by FieldWorks
/// identity alone, or by both with the guids checked against the id.
/// </summary>
/// <param name="Morpheme">The <c>grammar.xml</c> id of the lexical entry or affix rule, or null when only guids name it.</param>
/// <param name="EntryGuid">The lexical entry's FieldWorks guid in textual lowercase form, or null.</param>
/// <param name="MsaGuid">The morphosyntactic analysis's FieldWorks guid in textual lowercase form, or null.</param>
/// <param name="Allomorph">The <c>grammar.xml</c> allomorph id the caller pins, or null to let PanGloss choose.</param>
public sealed record WhyNotChoice(string? Morpheme, string? EntryGuid, string? MsaGuid, string? Allomorph = null)
{
    /// <summary>The spelling Motif echoes for this choice.</summary>
    public string Spelling
    {
        get
        {
            var text = Morpheme ?? $"{EntryGuid}:{MsaGuid}";
            if (Morpheme is not null && EntryGuid is not null) text += $"={EntryGuid}:{MsaGuid}";
            return Allomorph is null ? text : text + "@" + Allomorph;
        }
    }

    /// <summary>Whether the choice names its morpheme by <c>grammar.xml</c> id.</summary>
    public bool HasId => Morpheme is not null;
}

/// <summary>The morph sequence a caller expected: positions in surface order, each offering one or more choices.</summary>
/// <param name="Positions">The positions, each with at least one choice.</param>
public sealed record WhyNotCandidate(IReadOnlyList<IReadOnlyList<WhyNotChoice>> Positions)
{
    /// <summary>The spelling Motif echoes: <c>,</c> between positions and <c>|</c> between choices.</summary>
    public string Spelling => string.Join(',', Positions.Select(position => string.Join('|', position.Select(c => c.Spelling))));
}

/// <summary>Turns candidates into the argument shape the pinned PanGloss accepts, and says when it cannot.</summary>
public static partial class WhyNotInput
{
    private static readonly Regex GuidText = GuidPattern();

    /// <summary>Whether <paramref name="text"/> is a textual GUID in the lowercase form PanGloss's schema requires.</summary>
    public static bool IsGuid(string text) => GuidText.IsMatch(text);

    /// <summary>
    /// Whether every choice carries a <c>grammar.xml</c> id, so one <c>--expect-json</c> document can carry the call.
    /// A choice named by guid alone has no such form: the input schema requires <c>morpheme</c>.
    /// </summary>
    public static bool UsesJson(IReadOnlyList<WhyNotCandidate> candidates) =>
        candidates.All(candidate => candidate.Positions.All(position => position.All(choice => choice.HasId)));

    /// <summary>The reason this set of candidates cannot be sent in one call, or null when it can.</summary>
    public static string? Problem(IReadOnlyList<WhyNotCandidate> candidates)
    {
        if (candidates.Count == 0) return "Give at least one candidate.";
        foreach (var candidate in candidates)
        {
            if (candidate.Positions.Count == 0 || candidate.Positions.Any(position => position.Count == 0))
                return "A candidate and every position in it needs at least one morph.";
            foreach (var choice in candidate.Positions.SelectMany(position => position))
            {
                if (choice.Morpheme is { Length: 0 } || choice.Allomorph is { Length: 0 })
                    return "A morph id or allomorph id is empty.";
                if (choice.Morpheme is null && (choice.EntryGuid is null || choice.MsaGuid is null))
                    return "A choice with no morph id needs both entry and MSA guids, written entry:msa.";
                if (choice.EntryGuid is { } entry && !IsGuid(entry) || choice.MsaGuid is { } msa && !IsGuid(msa))
                    return "A guid is not a lowercase 8-4-4-4-12 hexadecimal GUID.";
            }
        }
        if (UsesJson(candidates)) return null;
        foreach (var candidate in candidates)
        {
            var choices = candidate.Positions.SelectMany(position => position).ToArray();
            if (choices.Any(choice => choice.HasId))
                return "PanGloss reads one call's candidates by morph id (with optional guids) or by guid alone. " +
                    "Split the call, or give every choice a morph id.";
            if (choices.Any(choice => choice.Allomorph is not null))
                return "An allomorph can be pinned only on a choice that has a morph id.";
        }
        return null;
    }

    /// <summary>The <c>--expect-json</c> document for candidates every choice of which has a morph id.</summary>
    public static string ToJson(IReadOnlyList<WhyNotCandidate> candidates)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartArray();
            foreach (var candidate in candidates)
            {
                writer.WriteStartArray();
                foreach (var position in candidate.Positions)
                {
                    writer.WriteStartArray();
                    foreach (var choice in position)
                    {
                        writer.WriteStartObject();
                        writer.WriteString("morpheme", choice.Morpheme);
                        if (choice.Allomorph is not null) writer.WriteString("allomorph", choice.Allomorph);
                        if (choice.EntryGuid is not null)
                        {
                            writer.WriteStartObject("guid");
                            writer.WriteString("entry_guid", choice.EntryGuid);
                            writer.WriteString("msa_guid", choice.MsaGuid);
                            writer.WriteEndObject();
                        }
                        writer.WriteEndObject();
                    }
                    writer.WriteEndArray();
                }
                writer.WriteEndArray();
            }
            writer.WriteEndArray();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>The <c>--expect-guid</c> value for one candidate every choice of which is named by guid alone.</summary>
    public static string ToGuidSpec(WhyNotCandidate candidate) => string.Join(',',
        candidate.Positions.Select(position => string.Join('|', position.Select(c => $"{c.EntryGuid}:{c.MsaGuid}"))));

    [GeneratedRegex("^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$", RegexOptions.CultureInvariant)]
    private static partial Regex GuidPattern();
}
