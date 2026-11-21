using System.Globalization;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Host.PanGloss;

namespace SIL.Motif.Commands.Queries;

/// <summary>
/// Reads one expected morph sequence as a person writes it: <c>,</c> between positions, <c>|</c> between choices at a
/// position, <c>@</c> to pin an allomorph, <c>id=entry:msa</c> to name a morph by id and check its FieldWorks identity,
/// and <c>entry:msa</c> alone to name an entry by FieldWorks identity. A guid is a textual GUID or a Motif canonical id.
/// </summary>
public static class WhyNotCandidateSpec
{
    /// <summary>The most positions one candidate may have.</summary>
    public const int MaxPositions = 32;

    /// <summary>The most choices one position may offer.</summary>
    public const int MaxChoices = 16;

    /// <summary>Reads a candidate, or says in one sentence what is wrong with it.</summary>
    public static bool TryParse(string spec, out WhyNotCandidate? candidate, out string error)
    {
        candidate = null;
        if (string.IsNullOrWhiteSpace(spec))
        {
            error = "A candidate is empty.";
            return false;
        }
        var positions = new List<IReadOnlyList<WhyNotChoice>>();
        var texts = spec.Trim().Split(',');
        if (texts.Length > MaxPositions)
        {
            error = $"A candidate has {texts.Length.ToString(CultureInfo.InvariantCulture)} positions; the most is {MaxPositions}.";
            return false;
        }
        for (var index = 0; index < texts.Length; index++)
        {
            var choices = new List<WhyNotChoice>();
            var offered = texts[index].Split('|');
            if (offered.Length > MaxChoices)
            {
                error = $"Position {index.ToString(CultureInfo.InvariantCulture)} offers {offered.Length.ToString(CultureInfo.InvariantCulture)} choices; the most is {MaxChoices}.";
                return false;
            }
            foreach (var text in offered)
            {
                if (!TryChoice(text.Trim(), out var choice, out var problem))
                {
                    error = $"Position {index.ToString(CultureInfo.InvariantCulture)} of '{spec}': {problem}";
                    return false;
                }
                choices.Add(choice!);
            }
            positions.Add(choices);
        }
        candidate = new WhyNotCandidate(positions);
        error = string.Empty;
        return true;
    }

    private static bool TryChoice(string text, out WhyNotChoice? choice, out string error)
    {
        choice = null;
        var at = text.Split('@');
        if (at.Length > 2 || at.Length == 2 && at[1].Length == 0)
        {
            error = "an allomorph is written id@allomorph-id, once.";
            return false;
        }
        var allomorph = at.Length == 2 ? at[1] : null;
        var head = at[0];
        if (head.Length == 0)
        {
            error = "a choice is empty.";
            return false;
        }
        string? morpheme = head;
        string? pair = null;
        var equals = head.IndexOf('=', StringComparison.Ordinal);
        if (equals >= 0)
        {
            morpheme = head[..equals];
            pair = head[(equals + 1)..];
        }
        else if (head.Contains(':', StringComparison.Ordinal))
        {
            morpheme = null;
            pair = head;
        }
        if (morpheme is { Length: 0 } || morpheme is not null && morpheme.Any(char.IsWhiteSpace))
        {
            error = "a morph id is empty or has a space in it.";
            return false;
        }
        string? entry = null, msa = null;
        if (pair is not null)
        {
            var guids = pair.Split(':');
            if (guids.Length != 2 || !TryGuid(guids[0], out entry) || !TryGuid(guids[1], out msa))
            {
                error = $"'{pair}' is not entry:msa with each side a GUID or a Motif id.";
                return false;
            }
        }
        choice = new WhyNotChoice(morpheme, entry, msa, allomorph);
        error = string.Empty;
        return true;
    }

    private static bool TryGuid(string text, out string? guid)
    {
        guid = null;
        var lower = text.Trim().ToLowerInvariant();
        if (WhyNotInput.IsGuid(lower))
        {
            guid = lower;
            return true;
        }
        if (!CanonicalId.TryParse(text.Trim(), out var id)) return false;
        guid = id.ToGuid().ToString("D", CultureInfo.InvariantCulture);
        return true;
    }
}
