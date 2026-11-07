using System.Globalization;
using System.Text.Json;
using SIL.Motif.Commands;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.ViewModels;

/// <summary>One plain sentence for what an operation of a Proposal changes.</summary>
internal static class ProposalOperationSentence
{
    private static readonly string[] NameProperties = ["text", "name", "label", "form", "abbreviation"];

    /// <summary>
    /// "Adds the allomorph -an (affects 12 words)." when the operation names what it makes, the kind's title when
    /// it does not, and no count phrase when no word is known to be affected.
    /// </summary>
    internal static string For(ProposalOperationView operation, int affectedWords)
    {
        var kind = ChangeVocabulary.Of(operation.Kind, MemberOf(operation.AfterJson));
        var name = kind.Key == ChangeVocabulary.GenericKey || !kind.Title.StartsWith("Add", StringComparison.Ordinal)
            ? null : NameOf(operation.AfterJson);
        var what = name is null ? kind.Title
            : string.Format(CultureInfo.CurrentCulture, MotifAppStrings.ProposalOperationNamedFormat,
                kind.Singular, name);
        return affectedWords > 0
            ? string.Format(CultureInfo.CurrentCulture, MotifAppStrings.ProposalOperationSentenceFormat, what,
                PluralText.Format(MotifAppStrings.ProposalOperationAffectsPattern, affectedWords))
            : string.Format(CultureInfo.CurrentCulture, MotifAppStrings.ProposalOperationSentenceNoCountFormat, what);
    }

    private static string? NameOf(string? afterJson) => Payload(afterJson) is { } payload
        ? NameProperties.Select(property => payload.TryGetProperty(property, out var value) &&
                value.ValueKind == JsonValueKind.String ? value.GetString()?.Trim() : null)
            .FirstOrDefault(text => !string.IsNullOrEmpty(text))
        : null;

    private static string? MemberOf(string? afterJson) => Payload(afterJson) is { } payload &&
        payload.TryGetProperty("member", out var member) && member.ValueKind == JsonValueKind.String
        ? member.GetString() : null;

    private static JsonElement? Payload(string? afterJson)
    {
        if (string.IsNullOrWhiteSpace(afterJson)) return null;
        try
        {
            using var document = JsonDocument.Parse(afterJson);
            return document.RootElement.ValueKind == JsonValueKind.Object ? document.RootElement.Clone() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
