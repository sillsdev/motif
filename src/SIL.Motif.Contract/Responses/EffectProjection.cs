using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SIL.Motif.Contract.Responses;

/// <summary>One writing-system alternative that actually moved, on one <see cref="EffectView"/>.</summary>
/// <param name="Ws">The writing-system identifier for this alternative.</param>
/// <param name="Before"><c>null</c> when the alternative was absent before the change.</param>
/// <param name="After"><c>null</c> when the alternative is absent after the change.</param>
public sealed record EffectChange(string Ws, string? Before, string? After);

/// <summary>
/// One identity-keyed field transition, shaped for display: only the writing-system alternatives
/// that actually differ, in place of <c>ExpectedEffect</c>'s full before/after maps.
/// </summary>
public sealed record EffectView(string CanonicalId, string Field, IReadOnlyList<EffectChange> Changes,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] JsonElement? Preview = null)
{
    /// <summary>The Proposal operations whose targets or created entities match this observed effect.</summary>
    public IReadOnlyList<string> OperationIds { get; init; } = [];
}
