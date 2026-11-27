using System;
using System.Globalization;

namespace SIL.Motif.Contract.Ids;

/// <summary>
/// The one parser for an id a person or agent types to name a project object. It accepts either spelling of the same
/// identity: the canonical id a Proposal names, or the guid a <c>.fwdata</c> file stores (any form <see cref="Guid.TryParse(string?, out Guid)"/> reads: hyphenated, 32 digits, braced, or the <c>{0x...}</c> form). The result is a
/// <see cref="CanonicalId"/>, so a caller reads <see cref="CanonicalId.ToGuid"/> or <see cref="CanonicalId.Value"/>.
/// </summary>
public static class ObjectId
{
    /// <summary>The refusal message every caller shares when text is neither spelling.</summary>
    public static string NotAnIdMessage(string? text) =>
        $"'{text}' is neither a canonical id (an optional prefix and 22 base64url characters) nor a guid (8-4-4-4-12 hex digits).";

    /// <summary>Parses a hyphenated guid or a canonical id; surrounding whitespace is ignored.</summary>
    public static bool TryParse(string? text, out CanonicalId id) => TryParse(text, out id, out _);

    /// <summary>As <see cref="TryParse(string?, out CanonicalId)"/>, with the reason it failed.</summary>
    public static bool TryParse(string? text, out CanonicalId id, out string? error)
    {
        var trimmed = text?.Trim();
        if (trimmed is not null && Guid.TryParse(trimmed, out var guid))
        {
            id = CanonicalId.FromGuid(guid);
            error = null;
            return true;
        }
        if (CanonicalId.TryParse(trimmed, out id))
        {
            error = null;
            return true;
        }
        error = NotAnIdMessage(text);
        return false;
    }

    /// <summary>The hyphenated lower-case guid spelling of <paramref name="id"/>, as the project file stores it.</summary>
    public static string ToGuidText(CanonicalId id) => id.ToGuid().ToString("D", CultureInfo.InvariantCulture);
}
