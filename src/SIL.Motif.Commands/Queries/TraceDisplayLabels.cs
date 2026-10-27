using System;
using System.Collections.Generic;
using System.Linq;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Commands.Queries;

/// <summary>Capture-time display names joined by exact ref id, without changing the recorded producer labels.</summary>
public sealed class TraceDisplayLabels
{
    private readonly IReadOnlyDictionary<string, TraceRef> _refs;

    private readonly bool _showSource;

    /// <param name="refs">The reading's refs.</param>
    /// <param name="showSource">Keeps the producer's own label after a captured name, as "name (label)".</param>
    public TraceDisplayLabels(IReadOnlyList<TraceRef> refs, bool showSource = false)
    {
        _refs = refs.ToDictionary(reference => reference.Id, StringComparer.Ordinal);
        _showSource = showSource;
    }

    /// <summary>
    /// The captured FieldWorks name, otherwise the ref's producer label. A missing ref uses the caller's
    /// recorded label; display text never establishes a match.
    /// </summary>
    public string? Resolve(string? refId, string? fallback)
    {
        if (refId is null || !_refs.TryGetValue(refId, out var reference)) return fallback;
        if (reference.CapturedFieldWorksLabel is not { } name) return reference.Label;
        return _showSource && !string.IsNullOrEmpty(fallback) && fallback != name ? $"{name} ({fallback})" : name;
    }
}
