using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SIL.Motif.Contract.HandAnalyses;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record HandAnalysisCheckRequest(string ProjectPath, string InputJson);

/// <summary>Resolution of saved identities only; neither approval nor a guarantee about a future mutation.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record HandAnalysisCheckResponse(Guid RequestId, Guid ProjectGuid,
    IReadOnlyList<Guid> WordformMatches, Guid? WordformId, IReadOnlyList<HandAnalysisPieceCheck> Pieces,
    IReadOnlyList<HandAnalysisDiagnostic> Diagnostics)
{
    public bool Ok => true;
    public bool Changed => false;
    public bool HomeGuardsChecked => false;
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record HandAnalysisPieceCheck(int Index, IReadOnlyList<Guid> MorphTypeMatches,
    IReadOnlyList<Guid> AllomorphMatches, Guid? EntryId, Guid? AllomorphId, Guid? SenseId, Guid? MsaId)
{
    public string PieceKey { get; init; } = "";
    public Guid EffectiveMorphTypeId { get; init; }
    public string TypedSpelling { get; init; } = "";
    public Guid? VariantRefId { get; init; }
    public Guid? InflTypeId { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record HandAnalysisDiagnostic(string Code, string Pointer, string Message,
    IReadOnlyList<Guid>? Matches = null, string? Consequence = null);

/// <summary>Committed annotation result for the full-fidelity Home; check never manufactures audit or revision ids.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record HandAnalysisResponse(bool Ok, Guid RequestId, string HomeRevision, string ExpectationRevision,
    string AuditId, bool Changed, Guid? WordformId, Guid? AnalysisId, Guid? GlossId, IReadOnlyList<Guid> BundleIds,
    IReadOnlyDictionary<string, Guid> CreationIds, IReadOnlyDictionary<string, Guid> PieceBundleIds,
    IReadOnlyList<Guid> ReusedIds, IReadOnlyList<Guid> CreatedIds,
    IReadOnlyList<Guid> CompletedIds, IReadOnlyList<Guid> DeletedIds, string OccurrenceStatus,
    IReadOnlyList<HandAnalysisDiagnostic> Warnings);
