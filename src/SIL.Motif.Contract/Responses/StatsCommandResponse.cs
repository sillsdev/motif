using System.Collections.Generic;
using System.Text.Json;

namespace SIL.Motif.Contract.Responses;

/// <summary>
/// What a <c>stats</c> query resolved and what PanGloss answered (design decision 5): which Assessment
/// supplied the cache, the grammar and cache paths Motif contributed, and PanGloss's own output in
/// whichever shape the caller asked for. <see cref="Text"/> is populated for
/// <see cref="Requests.StatsOutputKind.Text"/>; <see cref="Rows"/> is populated for
/// <see cref="Requests.StatsOutputKind.JsonRows"/>; the other is <c>null</c>.
/// </summary>
public sealed record StatsCommandResponse(
    string AssessmentId,
    string GrammarPath,
    string CachePath,
    string? Text,
    IReadOnlyList<JsonElement>? Rows);
