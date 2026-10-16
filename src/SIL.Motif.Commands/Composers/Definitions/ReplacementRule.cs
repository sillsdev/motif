using System.Text.Json;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;

namespace SIL.Motif.Commands;

/// <summary>
/// Says which operations already in a Draft an authored intent replaces: those of the listed kinds that act on
/// the same <c>target</c> the intent names. Two edits of one slot cannot both stand, so the earlier one goes.
/// </summary>
/// <param name="Kinds">The operation kinds an earlier edit of the same target may have been recorded as.</param>
internal sealed record ReplacementRule(IReadOnlyList<string> Kinds)
{
    /// <summary>A rule matching operations of <paramref name="kinds"/> on the intent's own target.</summary>
    public static ReplacementRule SameTarget(params string[] kinds) => new(kinds);

    /// <summary>Whether <paramref name="operation"/> is an earlier edit that <paramref name="authored"/> replaces.</summary>
    public bool Matches(JsonElement authored, OperationEnvelope operation) =>
        authored.TryGetProperty("target", out var targetValue) && targetValue.ValueKind == JsonValueKind.String &&
        CanonicalId.TryParse(targetValue.GetString(), out var target) &&
        operation.Target is { } existing && CanonicalId.ByGuid.Equals(existing, target) &&
        Kinds.Contains(operation.Kind, StringComparer.Ordinal);
}
