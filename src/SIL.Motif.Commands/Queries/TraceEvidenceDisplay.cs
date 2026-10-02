using System.Collections.Generic;
using System.Text.Json;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Commands.Queries;

/// <summary>Formats captured evaluator facts without assigning an unrecorded cause or project identity.</summary>
public static class TraceEvidenceDisplay
{
    public static IReadOnlyList<string> Details(TraceStep step)
    {
        var lines = new List<string>();
        if (step.EventEvidence is { } evidence)
        {
            if (evidence.BlockReason is { } reason) lines.Add($"Block reason: {reason}");
            if (evidence.BlockedByEntry is { } entry)
                lines.Add($"Replacement entry: {entry.Id} ({entry.Quality})");
            if (evidence.LookupResult is { } lookup)
                lines.Add($"Lookup completed: {lookup.MatchCount:N0} materialized roots ({lookup.Mode}); not analyses");
            foreach (var slot in evidence.Slots) lines.Add("Recorded template slot: " + slot.GetRawText());
            if (evidence.PartialParseCause is { } cause) lines.Add($"Recorded completion gate: {cause}");
            if (evidence.NonUnapplicationReason is { } nonUnapplication)
                lines.Add("Phonological non-unapplication: " + nonUnapplication.GetRawText());
        }
        if (step.FailureEvidence?.Payload is { ValueKind: JsonValueKind.Object } payload)
            lines.Add("Typed rejection operands (grammar-local where marked): " + payload.GetRawText());
        if (step.FailureEvidence is { Status: "unavailable", UnavailableReason: { } unavailable })
            lines.Add("Rejection details unavailable: " + unavailable);
        return lines;
    }
}
