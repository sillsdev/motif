using System.Collections.Generic;
using System.Text.Json;

namespace SIL.Motif.Contract.Responses;

/// <summary>Facts captured by an event's evaluator; absence never establishes a negative result.</summary>
public sealed record TraceEventEvidence(
    string? ProducerStepId,
    string? BlockReason,
    TraceRecordedIdentity? BlockedByEntry,
    TraceLookupResult? LookupResult,
    IReadOnlyList<JsonElement> Slots,
    string? PartialParseCause,
    JsonElement? NonUnapplicationReason);

/// <summary>A producer identity whose quality governs its scope, independently of its display label.</summary>
public sealed record TraceRecordedIdentity(string Kind, string Id, string Quality);

/// <summary>
/// One completed lookup's materialized root candidates, including allomorph expansion, not successful analyses.
/// Lookup completion does not establish completion of the enclosing search.
/// </summary>
public sealed record TraceLookupResult(string Status, bool Completed, long MatchCount, string Mode);
