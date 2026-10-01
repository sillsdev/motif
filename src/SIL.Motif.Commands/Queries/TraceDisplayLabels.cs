using System;
using System.Collections.Generic;
using System.Linq;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Commands.Queries;

/// <summary>Capture-time display names joined by exact ref id, without changing the recorded producer labels.</summary>
public sealed class TraceDisplayLabels
{
    private readonly IReadOnlyDictionary<string, TraceRef> _refs;

    public TraceDisplayLabels(IReadOnlyList<TraceRef> refs) =>
        _refs = refs.ToDictionary(reference => reference.Id, StringComparer.Ordinal);

    /// <summary>
    /// The captured FieldWorks name, otherwise the ref's producer label. A missing ref uses the caller's
    /// recorded label; display text never establishes a match.
    /// </summary>
    public string? Resolve(string? refId, string? fallback) =>
        refId is not null && _refs.TryGetValue(refId, out var reference)
            ? reference.CapturedFieldWorksLabel ?? reference.Label : fallback;
}
